using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using ClosedXML.Excel;
using CorporateTaxCategorization.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace CorporateTaxCategorization.Api.Services;

public sealed class WorkbookImporter(TaxDbContext db)
{
    public static readonly string[] Types = ["DPL", "TFA", "CRD"];
    public static readonly string[] Headers = ["ClientId", "EntityId", "Period", "RecordId", "AccountCode", "Description", "Amount", "Currency", "IsSynthetic", "SourceReference"];

    public async Task<Guid> ImportAsync(string clientId, string filePath, CancellationToken ct)
    {
        byte[] bytes;
        // Refuse files still held by a copying process.
        using (var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            if (file.Length > 5 * 1024 * 1024) throw new InvalidDataException("Workbook exceeds 5 MB.");
            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, ct);
            bytes = buffer.ToArray();
        }
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var prior = await db.Imports.SingleOrDefaultAsync(x => x.ClientId == clientId && x.Sha256 == hash, ct);
        if (prior is not null) return prior.Id;
        using (var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
            if (zip.Entries.Count > 500 || zip.Entries.Sum(x => x.Length) > 50 * 1024 * 1024)
                throw new InvalidDataException("Expanded workbook exceeds import limits.");
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        if (workbook.Worksheets.Count != 3 || Types.Any(t => !workbook.Worksheets.Any(s => s.Name == t)))
            throw new InvalidDataException("Expected exactly three sheets named DPL, TFA and CRD.");
        var import = new WorkbookImport { ClientId = clientId, FileName = Path.GetFileName(filePath), Sha256 = hash, OriginalFile = bytes };
        var records = new List<RawRecord>();
        bool? synthetic = null;
        foreach (var type in Types)
        {
            var sheet = workbook.Worksheet(type);
            if (!Headers.SequenceEqual(Enumerable.Range(1, Headers.Length).Select(c => sheet.Cell(1, c).GetString())))
                throw new InvalidDataException($"{type}: header row must match the supplied template.");
            var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
            if (lastRow > 1001 || lastRow < 2) throw new InvalidDataException($"{type}: expected 1–1000 data rows.");
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int row = 2; row <= lastRow; row++)
            {
                var cells = Enumerable.Range(1, Headers.Length).Select(c => sheet.Cell(row, c)).ToArray();
                if (cells.Any(c => c.HasFormula)) throw new InvalidDataException($"{type} row {row}: formulas are not supported by this template.");
                string Text(int column, int max) {
                    var value = cells[column - 1].GetString().Trim();
                    if (value.Length == 0 || value.Length > max) throw new InvalidDataException($"{type} row {row}: invalid {Headers[column - 1]}.");
                    return value;
                }
                if (!Text(1, 64).Equals(clientId, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"{type} row {row}: ClientId must match the inbox folder.");
                var entity = Text(2, 64); var period = Text(3, 20);
                if (records.Count == 0) { import.EntityId = entity; import.Period = period; }
                if (entity != import.EntityId || period != import.Period)
                    throw new InvalidDataException("One workbook must contain one entity and period.");
                var id = Text(4, 80);
                if (!ids.Add(id)) throw new InvalidDataException($"{type}: duplicate RecordId {id}.");
                if (!cells[6].TryGetValue<decimal>(out var amount) || Math.Abs(amount) >= 1_000_000_000_000_000m)
                    throw new InvalidDataException($"{type} row {row}: invalid amount.");
                var currency = Text(8, 3).ToUpperInvariant();
                if (currency.Length != 3 || !currency.All(char.IsAsciiLetter)) throw new InvalidDataException("Currency must have three letters.");
                if (!bool.TryParse(Text(9, 5), out var isSynthetic)) throw new InvalidDataException("IsSynthetic must be TRUE or FALSE.");
                synthetic ??= isSynthetic;
                if (synthetic != isSynthetic) throw new InvalidDataException("Do not mix synthetic and real rows.");
                if (isSynthetic && !clientId.StartsWith("SYNTH-", StringComparison.Ordinal))
                    throw new InvalidDataException("Synthetic workbooks require a SYNTH- client ID.");
                records.Add(new RawRecord {
                    ImportId = import.Id, ClientId = clientId, SheetType = type, SourceRow = row, RecordId = id,
                    AccountCode = Text(5, 80), Description = Text(6, 2000), Amount = amount, Currency = currency,
                    RawCellsJson = JsonSerializer.Serialize(Headers.Zip(cells.Select(c => c.GetString())).ToDictionary(x => x.First, x => x.Second))
                });
            }
        }
        import.IsSynthetic = synthetic == true;
        db.Imports.Add(import);
        db.RawRecords.AddRange(records);
        // Single SaveChanges transaction: all raw rows committed before prediction sees them.
        await db.SaveChangesAsync(ct);
        return import.Id;
    }
}
