using System.Security.Cryptography;
using CorporateTaxCategorization.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace CorporateTaxCategorization.Api.Services;

public static class DevelopmentDatabaseStartup
{
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration configuration, ILogger logger)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaxDbContext>();
        if (!await db.Database.CanConnectAsync()) await db.Database.EnsureCreatedAsync();
        if (!configuration.GetValue<bool>("Database:ResetOnStartup")) return;

        logger.LogWarning("ResetOnStartup enabled: recreating application tax schema and removing legacy application tables. Saved predictions will be deleted.");
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            // Only the six application tables are owned here. Never drop the database.
            // Remove legacy dbo tables from the previous increment as well.
            await db.Database.ExecuteSqlRawAsync("""
                DROP TABLE IF EXISTS [tax].[Predictions];
                DROP TABLE IF EXISTS [tax].[PredictionBatches];
                DROP TABLE IF EXISTS [tax].[CategorizationRuns];
                DROP TABLE IF EXISTS [tax].[RawRecords];
                DROP TABLE IF EXISTS [tax].[CategoryReferences];
                DROP TABLE IF EXISTS [tax].[Imports];
                DROP TABLE IF EXISTS [dbo].[Predictions];
                DROP TABLE IF EXISTS [dbo].[PredictionBatches];
                DROP TABLE IF EXISTS [dbo].[CategorizationRuns];
                DROP TABLE IF EXISTS [dbo].[RawRecords];
                DROP TABLE IF EXISTS [dbo].[CategoryReferences];
                DROP TABLE IF EXISTS [dbo].[Imports];
                """);
            await db.Database.ExecuteSqlRawAsync("IF SCHEMA_ID(N'tax') IS NOT NULL EXEC(N'DROP SCHEMA [tax]');");
            // EF emits GO separators for scripts; execute each batch within this transaction.
            foreach (var batch in System.Text.RegularExpressions.Regex.Split(db.Database.GenerateCreateScript(), @"(?m)^GO\s*$"))
                if (!string.IsNullOrWhiteSpace(batch)) await db.Database.ExecuteSqlRawAsync(batch);
            await transaction.CommitAsync();
        }

        var storage = scope.ServiceProvider.GetRequiredService<WorkbookStorage>();
        // Replay the original Excel files; the import worker, not startup, writes raw SQL rows.
        foreach (var clientDirectory in Directory.EnumerateDirectories(Path.Combine(storage.Root, "archive")))
        {
            if ((File.GetAttributes(clientDirectory) & FileAttributes.ReparsePoint) != 0) continue;
            var client = WorkbookStorage.ValidateClient(Path.GetFileName(clientDirectory));
            foreach (var file in Directory.EnumerateFiles(clientDirectory, "*.xlsx"))
                await QueueAsync(file, client, storage);
        }
        var sample = Path.Combine(AppContext.BaseDirectory, "SampleData", "SYNTH-001-FY2026.xlsx");
        if (File.Exists(sample)) await QueueAsync(sample, "SYNTH-001", storage);
        logger.LogInformation("Schema recreated. Archived workbooks and sample queued for background raw import; categorization awaits user action.");
    }

    private static async Task QueueAsync(string file, string client, WorkbookStorage storage)
    {
        if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) return;
        await using var input = File.OpenRead(file);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(input));
        input.Position = 0;
        var target = Path.Combine(storage.ClientDirectory("inbox", client), $"replay-{hash}.xlsx");
        if (File.Exists(target)) return;
        var staging = target + ".uploading";
        await using (var output = File.Create(staging)) await input.CopyToAsync(output);
        File.Move(staging, target, overwrite: true);
    }
}
