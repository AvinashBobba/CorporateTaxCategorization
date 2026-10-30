using CorporateTaxCategorization.Api.Data;
using CorporateTaxCategorization.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CorporateTaxCategorization.Api.Controllers;

[ApiController]
[Route("api/clients/{clientId}/workbooks")]
public sealed class WorkbooksController(TaxDbContext db, WorkbookStorage storage) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> Upload(string clientId, IFormFile file, CancellationToken ct)
    {
        try { clientId = WorkbookStorage.ValidateClient(clientId); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        if (file.Length == 0 || file.Length > 5 * 1024 * 1024 || !Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Upload a nonempty .xlsx file up to 5 MB." });
        var uploadId = Guid.NewGuid();
        var target = Path.Combine(storage.ClientDirectory("inbox", clientId), $"{uploadId}.xlsx");
        var temporary = target + ".uploading";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew)) await file.CopyToAsync(stream, ct);
            System.IO.File.Move(temporary, target);
        }
        finally { if (System.IO.File.Exists(temporary)) System.IO.File.Delete(temporary); }
        return Accepted($"/api/clients/{clientId}/workbooks", new { uploadId, clientId, status = "QueuedForImport", fileName = $"{uploadId}.xlsx" });
    }

    [HttpGet]
    public async Task<IActionResult> List(string clientId, CancellationToken ct)
    {
        try { clientId = WorkbookStorage.ValidateClient(clientId); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        var imports = await db.Imports.AsNoTracking().Where(x => x.ClientId == clientId).OrderByDescending(x => x.ImportedUtc)
            .Select(x => new { x.Id, x.ClientId, x.EntityId, x.Period, x.FileName, x.IsSynthetic, x.ImportedUtc,
                total = db.RawRecords.Count(r => r.ImportId == x.Id), pending = db.RawRecords.Count(r => r.ImportId == x.Id && r.Status == "Pending"),
                predicted = db.Predictions.Count(p => db.RawRecords.Any(r => r.Id == p.RawRecordId && r.ImportId == x.Id)) }).Take(100).ToListAsync(ct);
        var counts = await db.RawRecords.Where(x => x.ClientId == clientId).GroupBy(x => new { x.ImportId, x.SheetType })
            .Select(g => new { g.Key.ImportId, g.Key.SheetType, total = g.Count(), pending = g.Count(x => x.Status == "Pending") }).ToListAsync(ct);
        var active = await db.CategorizationRuns.Where(x => x.ClientId == clientId && (x.Status == "Queued" || x.Status == "Running"))
            .Select(x => new { x.ImportId, x.SheetType }).ToListAsync(ct);
        return Ok(imports.Select(x => new { x.Id, x.ClientId, x.EntityId, x.Period, x.FileName, x.IsSynthetic, x.ImportedUtc,
            x.total, x.pending, x.predicted, importStatus = "Succeeded",
            sheets = counts.Where(c => c.ImportId == x.Id).Select(c => new { type = c.SheetType, c.total, c.pending,
                canCategorize = c.pending > 0 && !active.Any(a => a.ImportId == x.Id && a.SheetType == c.SheetType) }) }));
    }

    [HttpGet("{importId:guid}/records")]
    public async Task<IActionResult> Records(string clientId, Guid importId, [FromQuery] string type, [FromQuery] int skip = 0, [FromQuery] int take = 20, CancellationToken ct = default)
    {
        try { clientId = WorkbookStorage.ValidateClient(clientId); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        type = type?.ToUpperInvariant() ?? "";
        if (!WorkbookImporter.Types.Contains(type) || skip < 0 || take < 1 || take > 20)
            return BadRequest(new { error = "type must be DPL, TFA or CRD; skip >= 0; take between 1 and 20." });
        if (!await db.Imports.AnyAsync(x => x.Id == importId && x.ClientId == clientId, ct)) return NotFound();
        var query = db.RawRecords.AsNoTracking().Where(x => x.ClientId == clientId && x.ImportId == importId && x.SheetType == type);
        var rows = await (from row in query
            join prediction in db.Predictions on row.Id equals prediction.RawRecordId into predictions
            from prediction in predictions.DefaultIfEmpty()
            orderby row.Id
            select new { row.Id, row.RecordId, row.SheetType, row.SourceRow, row.Description, row.Amount, row.Currency, row.Status,
                suggestedCategory = prediction == null ? null : prediction.SuggestedCategory,
                similarity = prediction == null ? null : prediction.Similarity,
                reviewRequired = prediction == null ? (bool?)null : prediction.ReviewRequired,
                batchId = prediction == null ? (Guid?)null : prediction.BatchId,
                candidatesJson = prediction == null ? null : prediction.CandidatesJson,
                modelVersion = prediction == null ? null : prediction.ModelVersion,
                wasTruncated = prediction == null ? (bool?)null : prediction.WasTruncated }).Skip(skip).Take(take).ToListAsync(ct);
        return Ok(new { clientId, importId, type, total = await query.CountAsync(ct), skip, take,
            rows = rows.Select(x => new { x.Id, x.RecordId, x.SheetType, x.SourceRow, x.Description, x.Amount, x.Currency, x.Status,
                x.suggestedCategory, x.similarity, x.reviewRequired, x.batchId, x.modelVersion, x.wasTruncated,
                candidates = x.candidatesJson is null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(x.candidatesJson) }) });
    }

    [HttpGet("{importId:guid}/batches")]
    public async Task<IActionResult> Batches(string clientId, Guid importId, CancellationToken ct)
    {
        try { clientId = WorkbookStorage.ValidateClient(clientId); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        if (!await db.Imports.AnyAsync(x => x.Id == importId && x.ClientId == clientId, ct)) return NotFound();
        return Ok(await db.PredictionBatches.AsNoTracking().Where(x => x.ClientId == clientId && x.ImportId == importId).OrderBy(x => x.CompletedUtc).ToListAsync(ct));
    }

    [HttpPost("{importId:guid}/categorize")]
    public async Task<IActionResult> Categorize(string clientId, Guid importId, [FromQuery] string type, CancellationToken ct)
    {
        try { clientId = WorkbookStorage.ValidateClient(clientId); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        type = type?.ToUpperInvariant() ?? "";
        if (!WorkbookImporter.Types.Contains(type)) return BadRequest(new { error = "type must be DPL, TFA or CRD." });
        if (!await db.Imports.AnyAsync(x => x.Id == importId && x.ClientId == clientId, ct)) return NotFound();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var resource = $"predict:{clientId}:{importId}:{type}";
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; IF @r < 0 THROW 51000, 'Prediction lock unavailable', 1;", ct);
        var current = await db.CategorizationRuns.FirstOrDefaultAsync(x => x.ClientId == clientId && x.ImportId == importId && x.SheetType == type && (x.Status == "Queued" || x.Status == "Running"), ct);
        if (current is not null) return Accepted($"/api/clients/{clientId}/workbooks/{importId}/runs", current);
        if (!await db.RawRecords.AnyAsync(x => x.ClientId == clientId && x.ImportId == importId && x.SheetType == type && x.Status == "Pending", ct))
            return Conflict(new { error = "No pending rows remain for this client, workbook and type." });
        var run = new CategorizationRun { ClientId = clientId, ImportId = importId, SheetType = type };
        db.CategorizationRuns.Add(run);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Accepted($"/api/clients/{clientId}/workbooks/{importId}/runs", run);
    }

    [HttpGet("{importId:guid}/runs")]
    public async Task<IActionResult> Runs(string clientId, Guid importId, CancellationToken ct)
    {
        try { clientId = WorkbookStorage.ValidateClient(clientId); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        if (!await db.Imports.AnyAsync(x => x.Id == importId && x.ClientId == clientId, ct)) return NotFound();
        return Ok(await db.CategorizationRuns.AsNoTracking().Where(x => x.ClientId == clientId && x.ImportId == importId).OrderByDescending(x => x.CreatedUtc).ToListAsync(ct));
    }

    [HttpGet("inbox")]
    public IActionResult Inbox(string clientId)
    {
        try { clientId = WorkbookStorage.ValidateClient(clientId); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        var pending = Directory.EnumerateFiles(storage.ClientDirectory("inbox", clientId), "*.xlsx").Select(Path.GetFileName).ToArray();
        var errors = Directory.EnumerateFiles(storage.ClientDirectory("rejected", clientId), "*.error.json")
            .Select(path => new { file = Path.GetFileName(path), error = System.IO.File.ReadAllText(path) }).ToArray();
        return Ok(new { clientId, pending, rejected = errors });
    }
}
