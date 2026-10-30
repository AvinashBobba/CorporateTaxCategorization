using Microsoft.ML.OnnxRuntime;

namespace CorporateTaxCategorization.MachineLearning;

/// <summary>
/// CPU adapter for an exported encoder with one float32 [1, featureCount] input
/// and one float32 [1, embeddingSize] output. Token-based transformers need a separate adapter.
/// Reuse this session for a model version; dispose it at application shutdown.
/// </summary>
public sealed class OnnxEmbeddingSession : IDisposable
{
    private readonly InferenceSession session;
    private readonly string inputName;
    private readonly string outputName;
    private readonly int featureCount;

    public OnnxEmbeddingSession(string modelPath, int featureCount, string inputName = "features", string outputName = "embedding")
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(featureCount);
        session = new InferenceSession(modelPath);
        this.featureCount = featureCount;
        this.inputName = inputName;
        this.outputName = outputName;
        try
        {
            if (session.InputMetadata.Count != 1 || !session.InputMetadata.TryGetValue(inputName, out var input) ||
                !Matches(input, featureCount) || !session.OutputMetadata.TryGetValue(outputName, out var output) ||
                !Matches(output, null))
                throw new ArgumentException("Expected float32 features [1,N] and embedding [1,D]. Verify the exported model contract.", nameof(modelPath));
        }
        catch { session.Dispose(); throw; }
    }

    public float[] Encode(float[] features)
    {
        ArgumentNullException.ThrowIfNull(features);
        if (features.Length != featureCount || features.Any(x => !float.IsFinite(x)))
            throw new ArgumentException("Features must match the model dimension and contain finite values.", nameof(features));
        using var input = OrtValue.CreateTensorValueFromMemory(features, new long[] { 1, featureCount });
        using var options = new RunOptions();
        using var result = session.Run(options, new Dictionary<string, OrtValue> { [inputName] = input }, new[] { outputName });
        var shape = result[0].GetTensorTypeAndShape();
        if (shape.Shape.Length != 2 || shape.Shape[0] != 1)
            throw new InvalidOperationException("Model output must have shape [1,D].");
        var embedding = result[0].GetTensorDataAsSpan<float>().ToArray();
        if (embedding.Length == 0 || embedding.Any(x => !float.IsFinite(x)) || !embedding.Any(x => x != 0))
            throw new InvalidOperationException("Model produced an invalid embedding.");
        return embedding;
    }

    private static bool Matches(NodeMetadata metadata, int? width) =>
        metadata.IsTensor && metadata.ElementType == typeof(float) && metadata.Dimensions.Length == 2 &&
        (metadata.Dimensions[0] == 1 || metadata.Dimensions[0] == -1) &&
        (width is null ? metadata.Dimensions[1] != 0 : metadata.Dimensions[1] == width || metadata.Dimensions[1] == -1);

    public void Dispose() => session.Dispose();
}
