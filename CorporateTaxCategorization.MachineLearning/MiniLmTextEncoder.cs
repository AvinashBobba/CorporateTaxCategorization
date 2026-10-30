using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.Tokenizers;
using System.Numerics.Tensors;

namespace CorporateTaxCategorization.MachineLearning;

public sealed record TextEmbedding(float[] Vector, int TokenCount, bool WasTruncated);

/// <summary>Text adapter for the bundled MiniLM model. Reuse and dispose at shutdown.</summary>
public sealed class MiniLmTextEncoder : IDisposable
{
    public const int MaxTokens = 256;
    public const int Dimensions = 384;
    private readonly BertTokenizer tokenizer;
    private readonly InferenceSession session;
    private readonly object inferenceLock = new();

    public MiniLmTextEncoder(string modelDirectory)
    {
        tokenizer = BertTokenizer.Create(Path.Combine(modelDirectory, "vocab.txt"), new BertOptions
        {
            LowerCaseBeforeTokenization = true, RemoveNonSpacingMarks = true,
            IndividuallyTokenizeCjk = true, ApplyBasicTokenization = true
        });
        session = new InferenceSession(Path.Combine(modelDirectory, "onnx", "model.onnx"));
    }

    public TextEmbedding Encode(string text)
    {
        lock (inferenceLock) return EncodeCore(text);
    }

    private TextEmbedding EncodeCore(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (text.Length > 32_000) throw new ArgumentException("Use a description of at most 32,000 characters.", nameof(text));
        var tokens = tokenizer.EncodeToIds(text, MaxTokens, addSpecialTokens: true, out _, out var consumed);
        long[] ids = tokens.Select(x => (long)x).ToArray();
        long[] shape = [1, ids.Length];
        using var input = OrtValue.CreateTensorValueFromMemory(ids, shape);
        using var attention = OrtValue.CreateTensorValueFromMemory(Enumerable.Repeat(1L, ids.Length).ToArray(), shape);
        using var types = OrtValue.CreateTensorValueFromMemory(new long[ids.Length], shape);
        using var options = new RunOptions();
        using var output = session.Run(options, new Dictionary<string, OrtValue>
        {
            ["input_ids"] = input, ["attention_mask"] = attention, ["token_type_ids"] = types
        }, new[] { "last_hidden_state" });
        if (!output[0].GetTensorTypeAndShape().Shape.SequenceEqual(new long[] { 1, ids.Length, Dimensions }))
            throw new InvalidOperationException("Unexpected MiniLM output shape.");
        var values = output[0].GetTensorDataAsSpan<float>();
        var vector = new float[Dimensions];
        // Unpadded sequence: all mask values are 1, including CLS and SEP.
        for (int token = 0; token < ids.Length; token++)
            for (int d = 0; d < Dimensions; d++) vector[d] += values[token * Dimensions + d] / ids.Length;
        var norm = TensorPrimitives.Norm<float>(vector);
        if (!float.IsFinite(norm) || norm == 0) throw new InvalidOperationException("Invalid model embedding.");
        TensorPrimitives.Divide<float>(vector, norm, vector);
        return new TextEmbedding(vector, ids.Length, consumed < text.Length);
    }

    public void Dispose() => session.Dispose();
}
