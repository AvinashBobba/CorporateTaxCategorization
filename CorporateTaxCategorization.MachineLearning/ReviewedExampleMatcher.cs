using System.Numerics.Tensors;

namespace CorporateTaxCategorization.MachineLearning;

// These are independent accounting dimensions, not mutually exclusive classes.
public sealed record AccountingLabels(string CapitalRevenue, string? TangibleAsset, string? ProfitAndLoss);
public sealed record ReviewedExample(string Id, AccountingLabels Labels, float[] Embedding);
public sealed record SimilarExample(string Id, AccountingLabels Labels, float Similarity);

/// <summary>Ranks reviewed precedents. Similarity is not a calibrated probability or tax decision.</summary>
public static class ReviewedExampleMatcher
{
    public static IReadOnlyList<SimilarExample> Find(float[] query, IEnumerable<ReviewedExample> examples, int count = 3)
    {
        ArgumentNullException.ThrowIfNull(examples);
        Validate(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        return examples.Select(example =>
        {
            Validate(example.Embedding);
            if (example.Embedding.Length != query.Length)
                throw new ArgumentException("All embeddings must use the same model and dimension.", nameof(examples));
            var score = TensorPrimitives.CosineSimilarity<float>(query, example.Embedding);
            if (!float.IsFinite(score))
                throw new ArgumentException("Embedding magnitudes exceed supported numeric range.");
            return new SimilarExample(example.Id, example.Labels, score);
        }).OrderByDescending(x => x.Similarity).ThenBy(x => x.Id, StringComparer.Ordinal).Take(count).ToArray();
    }

    private static void Validate(float[] vector)
    {
        ArgumentNullException.ThrowIfNull(vector);
        if (vector.Length == 0 || vector.Any(x => !float.IsFinite(x)) || !vector.Any(x => x != 0))
            throw new ArgumentException("An embedding must be nonempty, finite and nonzero.", nameof(vector));
    }
}
