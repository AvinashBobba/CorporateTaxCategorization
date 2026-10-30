using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace CorporateTaxCategorization.Api.Data;

public sealed class WorkbookImport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(64)] public string ClientId { get; set; } = "";
    [MaxLength(64)] public string EntityId { get; set; } = "";
    [MaxLength(20)] public string Period { get; set; } = "";
    [MaxLength(260)] public string FileName { get; set; } = "";
    [MaxLength(64)] public string Sha256 { get; set; } = "";
    public bool IsSynthetic { get; set; }
    public DateTime ImportedUtc { get; set; } = DateTime.UtcNow;
    public byte[] OriginalFile { get; set; } = [];
}

public sealed class RawRecord
{
    public long Id { get; set; }
    public Guid ImportId { get; set; }
    [MaxLength(64)] public string ClientId { get; set; } = "";
    [MaxLength(3)] public string SheetType { get; set; } = "";
    public int SourceRow { get; set; }
    [MaxLength(80)] public string RecordId { get; set; } = "";
    [MaxLength(80)] public string AccountCode { get; set; } = "";
    [MaxLength(2000)] public string Description { get; set; } = "";
    public decimal Amount { get; set; }
    [MaxLength(3)] public string Currency { get; set; } = "";
    public string RawCellsJson { get; set; } = "";
    [MaxLength(32)] public string Status { get; set; } = "Pending";
}

public sealed class CategoryReference
{
    public long Id { get; set; }
    [MaxLength(64)] public string ClientId { get; set; } = "";
    [MaxLength(3)] public string SheetType { get; set; } = "";
    [MaxLength(80)] public string Category { get; set; } = "";
    [MaxLength(2000)] public string Description { get; set; } = "";
    [MaxLength(150)] public string ModelVersion { get; set; } = "";
    public string EmbeddingJson { get; set; } = "";
    public bool IsSynthetic { get; set; }
}

public sealed class PredictionBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ImportId { get; set; }
    public Guid RunId { get; set; }
    [MaxLength(64)] public string ClientId { get; set; } = "";
    [MaxLength(3)] public string SheetType { get; set; } = "";
    public int Processed { get; set; }
    public DateTime CompletedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class CategorizationRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ImportId { get; set; }
    [MaxLength(64)] public string ClientId { get; set; } = "";
    [MaxLength(3)] public string SheetType { get; set; } = "";
    [MaxLength(20)] public string Status { get; set; } = "Queued";
    public int Processed { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedUtc { get; set; }
    [MaxLength(500)] public string? Error { get; set; }
}

public sealed class Prediction
{
    public long Id { get; set; }
    public long RawRecordId { get; set; }
    public Guid BatchId { get; set; }
    [MaxLength(80)] public string? SuggestedCategory { get; set; }
    public float? Similarity { get; set; }
    public string CandidatesJson { get; set; } = "";
    [MaxLength(150)] public string ModelVersion { get; set; } = "";
    public bool WasTruncated { get; set; }
    public bool ReviewRequired { get; set; } = true;
}

public sealed class TaxDbContext(DbContextOptions<TaxDbContext> options) : DbContext(options)
{
    public DbSet<WorkbookImport> Imports => Set<WorkbookImport>();
    public DbSet<RawRecord> RawRecords => Set<RawRecord>();
    public DbSet<CategoryReference> CategoryReferences => Set<CategoryReference>();
    public DbSet<PredictionBatch> PredictionBatches => Set<PredictionBatch>();
    public DbSet<Prediction> Predictions => Set<Prediction>();
    public DbSet<CategorizationRun> CategorizationRuns => Set<CategorizationRun>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("tax");
        model.Entity<WorkbookImport>().HasIndex(x => new { x.ClientId, x.Sha256 }).IsUnique();
        model.Entity<RawRecord>().Property(x => x.Amount).HasPrecision(19, 4);
        model.Entity<RawRecord>().HasIndex(x => new { x.ClientId, x.ImportId, x.SheetType, x.Status });
        model.Entity<RawRecord>().HasIndex(x => new { x.ImportId, x.SheetType, x.RecordId }).IsUnique();
        model.Entity<RawRecord>().HasOne<WorkbookImport>().WithMany().HasForeignKey(x => x.ImportId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<CategoryReference>().HasIndex(x => new { x.ClientId, x.SheetType, x.Category, x.ModelVersion, x.IsSynthetic }).IsUnique();
        model.Entity<Prediction>().HasIndex(x => x.RawRecordId).IsUnique();
        model.Entity<Prediction>().HasOne<RawRecord>().WithMany().HasForeignKey(x => x.RawRecordId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Prediction>().HasOne<PredictionBatch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<PredictionBatch>().HasOne<WorkbookImport>().WithMany().HasForeignKey(x => x.ImportId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<PredictionBatch>().HasOne<CategorizationRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<CategorizationRun>().HasOne<WorkbookImport>().WithMany().HasForeignKey(x => x.ImportId).OnDelete(DeleteBehavior.Restrict);
    }
}
