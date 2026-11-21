using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using NovelShelf.Comics;

namespace NovelShelf.Vision;

public sealed class OnnxTextRegionDetector : ITextRegionDetector, IDisposable
{
    private const int InputSize = 640;
    private const float MinScore = 0.5f;

    private readonly InferenceSession _session;

    public OnnxTextRegionDetector(string modelPath, string id)
    {
        Id = id;
        using var options = new SessionOptions { IntraOpNumThreads = 2, InterOpNumThreads = 1 };
        _session = new InferenceSession(modelPath, options);
    }

    public string Id { get; }

    public IReadOnlyList<TextRegion> Detect(byte[] pixels, int width, int height, int channels)
    {
        var input = new DenseTensor<float>([1, 3, InputSize, InputSize]);
        var buffer = input.Buffer.Span;
        const int plane = InputSize * InputSize;
        for (var y = 0; y < InputSize; y++)
        {
            var sy = Math.Min(height - 1, (int)((y + 0.5) * height / InputSize));
            for (var x = 0; x < InputSize; x++)
            {
                var sx = Math.Min(width - 1, (int)((x + 0.5) * width / InputSize));
                var i = (sy * width + sx) * channels;
                var target = y * InputSize + x;
                if (channels >= 3)
                {
                    buffer[target] = pixels[i] / 255f;
                    buffer[plane + target] = pixels[i + 1] / 255f;
                    buffer[2 * plane + target] = pixels[i + 2] / 255f;
                }
                else
                {
                    var gray = pixels[i] / 255f;
                    buffer[target] = gray;
                    buffer[plane + target] = gray;
                    buffer[2 * plane + target] = gray;
                }
            }
        }
        var size = new DenseTensor<long>(new long[] { width, height }, [1, 2]);

        using var results = _session.Run([
            NamedOnnxValue.CreateFromTensor("images", input),
            NamedOnnxValue.CreateFromTensor("orig_target_sizes", size),
        ]);
        var labels = results.First(r => r.Name == "labels").AsTensor<long>();
        var boxes = results.First(r => r.Name == "boxes").AsTensor<float>();
        var scores = results.First(r => r.Name == "scores").AsTensor<float>();

        var regions = new List<TextRegion>();
        for (var i = 0; i < scores.Dimensions[1]; i++)
        {
            if (scores[0, i] < MinScore || labels[0, i] is < 0 or > 2)
                continue;
            var box = new PanelBox(
                Math.Clamp((int)boxes[0, i, 0], 0, width), Math.Clamp((int)boxes[0, i, 1], 0, height),
                Math.Clamp((int)Math.Ceiling(boxes[0, i, 2]), 0, width), Math.Clamp((int)Math.Ceiling(boxes[0, i, 3]), 0, height));
            if (box.Width > 0 && box.Height > 0)
                regions.Add(new TextRegion(box, (TextRegionKind)labels[0, i], scores[0, i]));
        }
        return regions;
    }

    public void Dispose() => _session.Dispose();
}
