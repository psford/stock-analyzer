using System.Net.Http.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace StockAnalyzer.Core.Services;

/// <summary>
/// Service for processing images with ML-based animal detection.
/// Uses YOLOv8n ONNX model to detect cats and dogs, then crops images
/// to center on the detected animal for better thumbnail display.
/// </summary>
public class ImageProcessingService : IDisposable
{
    private readonly InferenceSession _session;
    private readonly HttpClient _httpClient;
    private readonly int _targetWidth;
    private readonly int _targetHeight;
    private bool _disposed;

    // COCO class indices for cat and dog
    private const int CatClassId = 15;
    private const int DogClassId = 16;

    // Model input size
    private const int ModelInputSize = 640;

    // Confidence threshold for detection - high threshold ensures clear animal faces
    private const float ConfidenceThreshold = 0.50f;

    // Minimum detection box size (as fraction of image) to ensure animal is prominent
    private const float MinDetectionSizeFraction = 0.20f;

    public ImageProcessingService(string modelPath, int targetWidth = 320, int targetHeight = 150, HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _targetWidth = targetWidth;
        _targetHeight = targetHeight;

        // Load ONNX model
        using var sessionOptions = new SessionOptions();
        sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
        _session = new InferenceSession(modelPath, sessionOptions);
    }

    /// <summary>
    /// Fetch and process a cat image from cataas.com.
    /// </summary>
    public async Task<byte[]?> GetProcessedCatImageAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Fetch random cat image
            var imageBytes = await _httpClient.GetByteArrayAsync(
                $"https://cataas.com/cat?width=640&height=640&t={Guid.NewGuid()}",
                cancellationToken);

            return ProcessImage(imageBytes, CatClassId);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Fetch and process a dog image from dog.ceo.
    /// </summary>
    public async Task<byte[]?> GetProcessedDogImageAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // First get a random image URL from dog.ceo API
            var response = await _httpClient.GetFromJsonAsync<DogApiResponse>(
                "https://dog.ceo/api/breeds/image/random",
                cancellationToken);

            if (response?.Message == null)
                return null;

            // Fetch the actual image
            var imageBytes = await _httpClient.GetByteArrayAsync(response.Message, cancellationToken);

            return ProcessImage(imageBytes, DogClassId);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Process an image: detect animal and crop centered on detection.
    /// Returns null if no valid detection found - caller should try another image.
    /// </summary>
    private byte[]? ProcessImage(byte[] imageData, int targetClassId)
    {
        try
        {
            using var image = Image.Load<Rgb24>(imageData);

            // Run detection
            var detection = DetectAnimal(image, targetClassId);

            // Reject image if no detection found - ensures face is in frame
            if (!detection.HasValue)
            {
                return null;
            }

            // Verify detection is large enough (animal should be prominent in image)
            float detectionArea = detection.Value.Width * detection.Value.Height;
            float imageArea = image.Width * image.Height;
            if (detectionArea / imageArea < MinDetectionSizeFraction * MinDetectionSizeFraction)
            {
                return null; // Animal too small in frame
            }

            // Crop image centered on detection
            using var cropped = CropToTarget(image, detection);

            // Encode as JPEG
            using var ms = new MemoryStream();
            cropped.SaveAsJpeg(ms);
            return ms.ToArray();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Run YOLO inference to detect an animal in the image.
    /// </summary>
    private Rectangle? DetectAnimal(Image<Rgb24> image, int targetClassId)
    {
        // Preprocess: resize to 640x640, normalize to 0-1
        var inputTensor = PreprocessImage(image);

        // Run inference
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("images", inputTensor)
        };

        using var results = _session.Run(inputs);
        var output = results.First().AsTensor<float>();

        // Parse YOLO output
        return ParseYoloOutput(output, image.Width, image.Height, targetClassId);
    }

    /// <summary>
    /// Preprocess image for YOLO: resize, normalize, convert to tensor.
    /// </summary>
    private DenseTensor<float> PreprocessImage(Image<Rgb24> image)
    {
        // Create resized copy
        using var resized = image.Clone(x => x.Resize(ModelInputSize, ModelInputSize));

        // Create tensor (BCHW format)
        var tensor = new DenseTensor<float>(new[] { 1, 3, ModelInputSize, ModelInputSize });

        for (int y = 0; y < ModelInputSize; y++)
        {
            for (int x = 0; x < ModelInputSize; x++)
            {
                var pixel = resized[x, y];
                tensor[0, 0, y, x] = pixel.R / 255f;  // R channel
                tensor[0, 1, y, x] = pixel.G / 255f;  // G channel
                tensor[0, 2, y, x] = pixel.B / 255f;  // B channel
            }
        }

        return tensor;
    }

    /// <summary>
    /// Parse YOLOv8 output tensor to find the best detection for target class.
    /// YOLOv8 output shape: (1, 84, 8400) where 84 = 4 bbox + 80 classes
    /// </summary>
    private Rectangle? ParseYoloOutput(Tensor<float> output, int originalWidth, int originalHeight, int targetClassId)
    {
        Rectangle? bestDetection = null;
        float bestConfidence = ConfidenceThreshold;

        // YOLOv8 output: [batch, 84, num_detections]
        // Rows 0-3: x_center, y_center, width, height
        // Rows 4-83: class probabilities for 80 COCO classes

        int numDetections = output.Dimensions[2];

        for (int i = 0; i < numDetections; i++)
        {
            // Get class confidence for target class (cat=15 or dog=16)
            float confidence = output[0, 4 + targetClassId, i];

            if (confidence > bestConfidence)
            {
                // Get bounding box (normalized coordinates)
                float xCenter = output[0, 0, i];
                float yCenter = output[0, 1, i];
                float width = output[0, 2, i];
                float height = output[0, 3, i];

                // Convert from model coordinates to original image coordinates
                float scaleX = (float)originalWidth / ModelInputSize;
                float scaleY = (float)originalHeight / ModelInputSize;

                int x1 = (int)((xCenter - width / 2) * scaleX);
                int y1 = (int)((yCenter - height / 2) * scaleY);
                int w = (int)(width * scaleX);
                int h = (int)(height * scaleY);

                // Clamp to image bounds
                x1 = Math.Max(0, Math.Min(x1, originalWidth - 1));
                y1 = Math.Max(0, Math.Min(y1, originalHeight - 1));
                w = Math.Min(w, originalWidth - x1);
                h = Math.Min(h, originalHeight - y1);

                if (w > 0 && h > 0)
                {
                    bestDetection = new Rectangle(x1, y1, w, h);
                    bestConfidence = confidence;
                }
            }
        }

        return bestDetection;
    }

    /// <summary>
    /// Crop image to target dimensions, centered on detection if provided.
    /// </summary>
    private Image<Rgb24> CropToTarget(Image<Rgb24> image, Rectangle? detection)
    {
        int centerX, centerY;

        if (detection.HasValue)
        {
            // Center on the detected animal
            centerX = detection.Value.X + detection.Value.Width / 2;
            centerY = detection.Value.Y + detection.Value.Height / 2;
        }
        else
        {
            // Default: center of image, biased toward top (where faces usually are)
            centerX = image.Width / 2;
            centerY = image.Height / 3;  // Top third bias
        }

        // Calculate crop rectangle maintaining aspect ratio
        float targetAspect = (float)_targetWidth / _targetHeight;
        float imageAspect = (float)image.Width / image.Height;

        int cropWidth, cropHeight;

        if (imageAspect > targetAspect)
        {
            // Image is wider than target - height-constrained
            cropHeight = image.Height;
            cropWidth = (int)(cropHeight * targetAspect);
        }
        else
        {
            // Image is taller than target - width-constrained
            cropWidth = image.Width;
            cropHeight = (int)(cropWidth / targetAspect);
        }

        // Center crop rectangle on detection/center point
        int cropX = Math.Max(0, Math.Min(centerX - cropWidth / 2, image.Width - cropWidth));
        int cropY = Math.Max(0, Math.Min(centerY - cropHeight / 2, image.Height - cropHeight));

        // Clone and crop
        var cropped = image.Clone(ctx =>
        {
            ctx.Crop(new Rectangle(cropX, cropY, cropWidth, cropHeight));
            ctx.Resize(_targetWidth, _targetHeight);
        });

        return cropped;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _session?.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }

    private record DogApiResponse(string Message, string Status);
}
