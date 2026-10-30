using System.Text.Json;

namespace CorporateTaxCategorization.Api.Services;

/// <summary>Durable inbox polling catches new files and files left behind by a restart.</summary>
public sealed class WorkbookImportWorker(IServiceScopeFactory scopes, WorkbookStorage storage, ILogger<WorkbookImportWorker> logger) : BackgroundService
{
    private readonly Dictionary<string, (long Length, DateTime Modified)> observed = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var folder in Directory.EnumerateDirectories(storage.Inbox))
                {
                    // Do not follow directory links out of the configured inbox.
                    if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) continue;
                    string client;
                    try { client = WorkbookStorage.ValidateClient(Path.GetFileName(folder)); }
                    catch (ArgumentException) { continue; }
                    foreach (var file in Directory.EnumerateFiles(folder, "*.xlsx"))
                    {
                        if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;
                        var info = new FileInfo(file);
                        var stamp = (info.Length, info.LastWriteTimeUtc);
                        if (!observed.TryGetValue(file, out var previous) || previous != stamp)
                        { observed[file] = stamp; continue; }
                        try
                        {
                            using var scope = scopes.CreateScope();
                            var id = await scope.ServiceProvider.GetRequiredService<WorkbookImporter>().ImportAsync(client, file, stoppingToken);
                            var archive = Path.Combine(storage.ClientDirectory("archive", client), $"{id}-{Guid.NewGuid():N}.xlsx");
                            File.Move(file, archive);
                            observed.Remove(file);
                            logger.LogInformation("Imported workbook {File} for {Client} as {ImportId}", info.Name, client, id);
                        }
                        catch (Exception ex) when (ex is InvalidDataException or FormatException or ArgumentException)
                        {
                            var rejected = Path.Combine(storage.ClientDirectory("rejected", client), $"{Guid.NewGuid():N}-{info.Name}");
                            File.Move(file, rejected);
                            await File.WriteAllTextAsync(rejected + ".error.json", JsonSerializer.Serialize(new { clientId = client, error = ex.Message }), stoppingToken);
                            observed.Remove(file);
                            logger.LogWarning(ex, "Rejected workbook {File}", info.Name);
                        }
                        catch (IOException ex) { logger.LogDebug(ex, "Workbook not ready; will retry"); }
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Import scan failed; files remain in inbox for retry"); }
            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }
    }
}
