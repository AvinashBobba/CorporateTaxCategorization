using System.Globalization;
using System.Text.Json;
using CorporateTaxCategorization.Api.Data;
using CorporateTaxCategorization.MachineLearning;
using Microsoft.EntityFrameworkCore;

namespace CorporateTaxCategorization.Api.Services;

public sealed class PredictionWorker(IServiceScopeFactory scopes, ILogger<PredictionWorker> logger) : BackgroundService
{
    public const int BatchSize = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            Guid? activeRunId = null;
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<TaxDbContext>();
                // Only explicit user-created runs are eligible. Import never queues prediction.
                var next = await db.CategorizationRuns.AsNoTracking().Where(x => x.Status == "Queued" || x.Status == "Running").OrderBy(x => x.CreatedUtc).FirstOrDefaultAsync(stoppingToken);
                if (next is not null)
                {
                    activeRunId = next.Id;
                    var encoder = scope.ServiceProvider.GetRequiredService<MiniLmTextEncoder>();
                    await using var transaction = await db.Database.BeginTransactionAsync(stoppingToken);
                    // Across API instances, serialize claims for the same client/import/type.
                    var resource = $"predict:{next.ClientId}:{next.ImportId}:{next.SheetType}";
                    await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; IF @r < 0 THROW 51000, 'Prediction lock unavailable', 1;", stoppingToken);
                    var run = await db.CategorizationRuns.SingleAsync(x => x.Id == next.Id, stoppingToken);
                    if (run.Status is not ("Queued" or "Running")) { await transaction.CommitAsync(stoppingToken); continue; }
                    run.Status = "Running";
                    var import = await db.Imports.SingleAsync(x => x.Id == next.ImportId && x.ClientId == next.ClientId, stoppingToken);
                    if (import.IsSynthetic)
                    {
                        // A separate lock prevents simultaneous first-use seeding across sheet types.
                        var seedLock = $"categories:{next.ClientId}";
                        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r = sys.sp_getapplock @Resource={seedLock}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; IF @r < 0 THROW 51000, 'Category lock unavailable', 1;", stoppingToken);
                        await SyntheticCategories.SeedAsync(db, encoder, next.ClientId, stoppingToken);
                    }
                    var references = await db.CategoryReferences.AsNoTracking().Where(x => x.ClientId == next.ClientId &&
                        x.SheetType == next.SheetType && x.ModelVersion == SyntheticCategories.ModelVersion && x.IsSynthetic == import.IsSynthetic).ToListAsync(stoppingToken);
                    var rows = await db.RawRecords.Where(x => x.ClientId == next.ClientId && x.ImportId == next.ImportId &&
                        x.SheetType == next.SheetType && x.Status == "Pending").OrderBy(x => x.Id).Take(BatchSize).ToListAsync(stoppingToken);
                    if (rows.Count > 0 && references.Count == 0)
                    {
                        run.Status = "Failed";
                        run.Error = "No compatible client/type reference categories. Add reference data before categorizing real workbooks.";
                        await db.SaveChangesAsync(stoppingToken);
                    }
                    else if (rows.Count > 0)
                    {
                        var examples = references.Select(x => new ReviewedExample(x.Id.ToString(CultureInfo.InvariantCulture),
                            new AccountingLabels(x.Category, null, null), JsonSerializer.Deserialize<float[]>(x.EmbeddingJson)!)).ToArray();
                        var batch = new PredictionBatch { RunId = run.Id, ClientId = next.ClientId, ImportId = next.ImportId, SheetType = next.SheetType, Processed = rows.Count };
                        db.PredictionBatches.Add(batch);
                        foreach (var row in rows)
                        {
                            stoppingToken.ThrowIfCancellationRequested();
                            var embedding = encoder.Encode(row.Description);
                            var matches = ReviewedExampleMatcher.Find(embedding.Vector, examples).Select(x => new
                            {
                                referenceId = long.Parse(x.Id, CultureInfo.InvariantCulture), category = x.Labels.CapitalRevenue,
                                similarity = x.Similarity, evidenceSource = import.IsSynthetic ? "SyntheticCategoryDescription" : "ClientReference"
                            }).ToArray();
                            // A nearest category is only a suggestion; do not interpret similarity as confidence.
                            var best = matches.FirstOrDefault();
                            db.Predictions.Add(new Prediction { RawRecordId = row.Id, BatchId = batch.Id,
                                SuggestedCategory = best is { similarity: > 0 } ? best.category : null,
                                Similarity = best?.similarity, CandidatesJson = JsonSerializer.Serialize(matches),
                                ModelVersion = SyntheticCategories.ModelVersion, WasTruncated = embedding.WasTruncated });
                            row.Status = "NeedsReview";
                        }
                        await db.SaveChangesAsync(stoppingToken);
                        run.Processed += rows.Count;
                        logger.LogInformation("Predicted {Count} SQL rows for {Client}/{Type}; batch {BatchId}", rows.Count, batch.ClientId, batch.SheetType, batch.Id);
                    }
                    if (run.Status != "Failed" && !await db.RawRecords.AnyAsync(x => x.ClientId == next.ClientId && x.ImportId == next.ImportId && x.SheetType == next.SheetType && x.Status == "Pending", stoppingToken))
                    { run.Status = "Completed"; run.CompletedUtc = DateTime.UtcNow; }
                    await db.SaveChangesAsync(stoppingToken);
                    await transaction.CommitAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Prediction batch failed; pending rows preserved");
                if (activeRunId is not null)
                {
                    try
                    {
                        using var failureScope = scopes.CreateScope();
                        var failureDb = failureScope.ServiceProvider.GetRequiredService<TaxDbContext>();
                        await failureDb.CategorizationRuns.Where(x => x.Id == activeRunId && (x.Status == "Queued" || x.Status == "Running"))
                            .ExecuteUpdateAsync(set => set.SetProperty(x => x.Status, "Failed")
                                .SetProperty(x => x.Error, "Prediction failed. Check server logs; pending rows are preserved. Categorize again to retry."), stoppingToken);
                    }
                    catch (Exception failure) { logger.LogError(failure, "Unable to persist run failure"); }
                }
            }
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }
}
