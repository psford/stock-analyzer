using BERTTokenizers;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace StockAnalyzer.Core.Services;

/// <summary>
/// FinBERT sentiment analysis using ONNX Runtime.
/// Provides high-accuracy financial text sentiment classification.
///
/// FinBERT is a BERT model fine-tuned specifically for financial text,
/// making it ideal for analyzing stock news headlines.
/// </summary>
public sealed class FinBertSentimentService : IDisposable
{
    private readonly InferenceSession _session;
    private readonly BertBaseTokenizer _tokenizer;
    private const int MaxSequenceLength = 128;

    // FinBERT label order: positive, negative, neutral
    private static readonly string[] Labels = ["positive", "negative", "neutral"];

    private bool _disposed;

    /// <summary>
    /// Creates a new FinBERT sentiment service.
    /// </summary>
    /// <param name="modelPath">Path to the finbert.onnx model file</param>
    public FinBertSentimentService(string modelPath)
    {
        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"FinBERT model not found: {modelPath}");

        using var sessionOptions = new SessionOptions();
        sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;

        // Use CPU execution - GPU would require CUDA setup
        sessionOptions.AppendExecutionProvider_CPU(0);

        _session = new InferenceSession(modelPath, sessionOptions);
        // BertBaseTokenizer uses its built-in BERT vocabulary
        _tokenizer = new BertBaseTokenizer();
    }

    /// <summary>
    /// FinBERT analysis result.
    /// </summary>
    /// <param name="Label">Predicted sentiment: "positive", "negative", or "neutral"</param>
    /// <param name="Confidence">Confidence score for the predicted label (0-1)</param>
    /// <param name="PositiveProb">Probability of positive sentiment (0-1)</param>
    /// <param name="NegativeProb">Probability of negative sentiment (0-1)</param>
    /// <param name="NeutralProb">Probability of neutral sentiment (0-1)</param>
    public record FinBertResult(
        string Label,
        float Confidence,
        float PositiveProb,
        float NegativeProb,
        float NeutralProb
    );

    /// <summary>
    /// Analyze sentiment of text using FinBERT.
    /// </summary>
    /// <param name="text">Text to analyze (headline, sentence, etc.)</param>
    /// <returns>Sentiment analysis result with label and probabilities</returns>
    public FinBertResult Analyze(string text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(text))
            return new FinBertResult("neutral", 1.0f, 0f, 0f, 1.0f);

        // Tokenize the input text
        var encoded = _tokenizer.Encode(MaxSequenceLength, text);

        // Create input tensors
        var inputIds = new DenseTensor<long>(
            encoded.Select(t => (long)t.InputIds).ToArray(),
            [1, encoded.Count]);
        var attentionMask = new DenseTensor<long>(
            encoded.Select(t => (long)t.AttentionMask).ToArray(),
            [1, encoded.Count]);
        var tokenTypeIds = new DenseTensor<long>(
            encoded.Select(t => (long)t.TokenTypeIds).ToArray(),
            [1, encoded.Count]);

        // Prepare inputs for ONNX Runtime
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIds),
            NamedOnnxValue.CreateFromTensor("attention_mask", attentionMask),
            NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIds)
        };

        // Run inference
        using var results = _session.Run(inputs);
        var logits = results.First().AsTensor<float>();

        // Apply softmax to get probabilities
        var probs = Softmax([logits[0, 0], logits[0, 1], logits[0, 2]]);

        // Find the predicted class
        int maxIdx = 0;
        for (int i = 1; i < probs.Length; i++)
        {
            if (probs[i] > probs[maxIdx])
                maxIdx = i;
        }

        return new FinBertResult(
            Labels[maxIdx],
            probs[maxIdx],
            probs[0],  // positive
            probs[1],  // negative
            probs[2]   // neutral
        );
    }

    /// <summary>
    /// Softmax function to convert logits to probabilities.
    /// </summary>
    private static float[] Softmax(float[] logits)
    {
        // Subtract max for numerical stability
        float maxLogit = logits.Max();
        float[] exps = new float[logits.Length];
        float sum = 0;

        for (int i = 0; i < logits.Length; i++)
        {
            exps[i] = MathF.Exp(logits[i] - maxLogit);
            sum += exps[i];
        }

        for (int i = 0; i < exps.Length; i++)
        {
            exps[i] /= sum;
        }

        return exps;
    }

    /// <summary>
    /// Dispose of the ONNX session resources.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _session?.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
