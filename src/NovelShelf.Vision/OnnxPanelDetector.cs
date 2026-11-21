using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using NovelShelf.Comics;

namespace NovelShelf.Vision;

public sealed class OnnxPanelDetector : IPanelRegionDetector, IDisposable
{
    private const int InputSize = 640;
    private const float Padding = 114 / 255f;
    private const float MinScore = 0.4f;
    private const float SameBox = 0.5f;
    private const int PanelClass = 0;

    private readonly InferenceSession _session;
    private readonly string _input;

    public OnnxPanelDetector(string modelPath, string id)
    {
        Id = id;
        using var options = new SessionOptions { IntraOpNumThreads = 2, InterOpNumThreads = 1 };
        _session = new InferenceSession(modelPath, options);
        _input = _session.InputMetadata.Keys.First();
    }

    public string Id { get; }

    public IReadOnlyList<PanelBox> Detect(byte[] pixels, int width, int height, int channels)
    {
        var letterbox = Letterbox.For(width, height, InputSize);
        var input = new DenseTensor<float>([1, 3, InputSize, InputSize]);
        Fill(input.Buffer.Span, pixels, width, height, channels, letterbox);
        using var results = _session.Run([NamedOnnxValue.CreateFromTensor(_input, input)]);
        var output = results.First().AsTensor<float>();
        return Decode(output, letterbox, width, height);
    }

    internal static IReadOnlyList<PanelBox> Decode(Tensor<float> output, Letterbox letterbox, int width, int height)
    {
        var classes = output.Dimensions[1] - 4;
        var candidates = new List<(PanelBox Box, float Score)>();
        for (var i = 0; i < output.Dimensions[2]; i++)
        {
            var score = output[0, 4 + PanelClass, i];
            if (score < MinScore || Enumerable.Range(0, classes).Any(c => c != PanelClass && output[0, 4 + c, i] > score))
                continue;
            float cx = output[0, 0, i], cy = output[0, 1, i], w = output[0, 2, i], h = output[0, 3, i];
            var box = letterbox.ToSource(cx - w / 2, cy - h / 2, cx + w / 2, cy + h / 2, width, height);
            if (box.Width > 0 && box.Height > 0)
                candidates.Add((box, score));
        }
        var kept = new List<PanelBox>();
        foreach (var (box, _) in candidates.OrderByDescending(c => c.Score))
            if (!kept.Any(k => IoU(k, box) > SameBox))
                kept.Add(box);
        return kept;
    }

    private static void Fill(Span<float> buffer, byte[] pixels, int width, int height, int channels, Letterbox box)
    {
        const int plane = InputSize * InputSize;
        buffer.Fill(Padding);
        Span<float> sum = stackalloc float[3];
        for (var y = 0; y < box.Height; y++)
        {
            var top = Math.Min(height - 1, (int)(y / box.Scale));
            var bottom = Math.Clamp((int)Math.Ceiling((y + 1) / box.Scale), top + 1, height);
            for (var x = 0; x < box.Width; x++)
            {
                var left = Math.Min(width - 1, (int)(x / box.Scale));
                var right = Math.Clamp((int)Math.Ceiling((x + 1) / box.Scale), left + 1, width);
                sum.Clear();
                for (var sy = top; sy < bottom; sy++)
                {
                    for (var sx = left; sx < right; sx++)
                    {
                        var i = (sy * width + sx) * channels;
                        sum[0] += pixels[i];
                        sum[1] += pixels[channels >= 3 ? i + 1 : i];
                        sum[2] += pixels[channels >= 3 ? i + 2 : i];
                    }
                }
                var scale = 255f * (bottom - top) * (right - left);
                var target = (box.Top + y) * InputSize + box.Left + x;
                buffer[target] = sum[0] / scale;
                buffer[plane + target] = sum[1] / scale;
                buffer[2 * plane + target] = sum[2] / scale;
            }
        }
    }

    private static float IoU(PanelBox a, PanelBox b)
    {
        long w = Math.Max(0, Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left));
        long h = Math.Max(0, Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top));
        var inter = w * h;
        var union = a.Area + b.Area - inter;
        return union <= 0 ? 0 : (float)inter / union;
    }

    public void Dispose() => _session.Dispose();
}

internal readonly record struct Letterbox(double Scale, int Left, int Top, int Width, int Height)
{
    public static Letterbox For(int width, int height, int size)
    {
        var scale = Math.Min((double)size / width, (double)size / height);
        var w = Math.Max(1, (int)Math.Round(width * scale));
        var h = Math.Max(1, (int)Math.Round(height * scale));
        return new Letterbox(scale, (size - w) / 2, (size - h) / 2, w, h);
    }

    public PanelBox ToSource(float left, float top, float right, float bottom, int width, int height) => new(
        Math.Clamp((int)Math.Floor((left - Left) / Scale), 0, width),
        Math.Clamp((int)Math.Floor((top - Top) / Scale), 0, height),
        Math.Clamp((int)Math.Ceiling((right - Left) / Scale), 0, width),
        Math.Clamp((int)Math.Ceiling((bottom - Top) / Scale), 0, height));
}
