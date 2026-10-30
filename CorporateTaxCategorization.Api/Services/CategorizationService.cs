using CorporateTaxCategorization.MachineLearning;

namespace CorporateTaxCategorization.Api.Services;

public sealed record ExampleResponse(string Id, string Description, AccountingLabels Labels);
public sealed record MatchResponse(string Id, string Description, AccountingLabels Labels, float Similarity);
public sealed record SuggestionResponse(string Model, string ExampleSource, bool ReviewRequired,
    int TokenCount, bool WasTruncated, IReadOnlyList<MatchResponse> Matches);

public sealed class CategorizationService
{
    private readonly MiniLmTextEncoder encoder;
    private readonly object inferenceLock = new();
    private readonly ReviewedExample[] embeddings;
    private readonly ExampleResponse[] examples =
    [
        new("DEMO-1", "Purchase of new laptop computers for office staff", new("Capital", "Computer equipment", null)),
        new("DEMO-2", "Purchase of office desks and chairs", new("Capital", "Furniture", null)),
        new("DEMO-3", "Monthly office electricity bill", new("Revenue", null, "Utilities")),
        new("DEMO-4", "Annual business insurance premium", new("Revenue", null, "Insurance")),
        new("DEMO-5", "Accountant fees for preparing annual accounts", new("Revenue", null, "Professional fees")),
        new("DEMO-6", "Repair and servicing of existing office printer", new("ReviewRequired", null, "Repairs and maintenance"))
    ];

    public CategorizationService(MiniLmTextEncoder encoder)
    {
        this.encoder = encoder;
        embeddings = examples.Select(x => new ReviewedExample(x.Id, x.Labels, encoder.Encode(x.Description).Vector)).ToArray();
    }

    public IReadOnlyList<ExampleResponse> GetExamples() => examples;

    public SuggestionResponse Suggest(string description, int count)
    {
        TextEmbedding query;
        // Serialize access to the shared tokenizer for the initial CPU implementation.
        lock (inferenceLock) query = encoder.Encode(description);
        var matches = ReviewedExampleMatcher.Find(query.Vector, embeddings, count)
            .Select(x => new MatchResponse(x.Id, examples.Single(e => e.Id == x.Id).Description, x.Labels, x.Similarity)).ToArray();
        return new("sentence-transformers/all-MiniLM-L6-v2", "SyntheticDemo", true, query.TokenCount, query.WasTruncated, matches);
    }
}
