using Microsoft.ML.OnnxRuntime.Tensors;
using NovelShelf.Comics;
using NovelShelf.Vision;

namespace NovelShelf.Tests.Vision;

public sealed class PanelDecodeTests
{
    private static DenseTensor<float> Output(params (float Cx, float Cy, float W, float H, float Panel, float Bubble)[] rows)
    {
        var tensor = new DenseTensor<float>([1, 6, rows.Length]);
        for (var i = 0; i < rows.Length; i++)
        {
            var (cx, cy, w, h, panel, bubble) = rows[i];
            tensor[0, 0, i] = cx;
            tensor[0, 1, i] = cy;
            tensor[0, 2, i] = w;
            tensor[0, 3, i] = h;
            tensor[0, 4, i] = panel;
            tensor[0, 5, i] = bubble;
        }
        return tensor;
    }

    [Fact]
    public void Boxes_go_back_to_page_pixels_without_the_letterbox()
    {
        var letterbox = Letterbox.For(1000, 2000, 640);

        var panels = OnnxPanelDetector.Decode(Output((320, 160, 320, 320, 0.9f, 0.1f)), letterbox, 1000, 2000);

        Assert.Equal(160, letterbox.Left);
        Assert.Equal(new PanelBox(0, 0, 1000, 1000), Assert.Single(panels));
    }

    [Fact]
    public void Weak_bubble_and_duplicate_boxes_are_dropped()
    {
        var letterbox = Letterbox.For(640, 640, 640);

        var panels = OnnxPanelDetector.Decode(Output(
            (100, 100, 100, 100, 0.9f, 0.0f),
            (102, 101, 100, 100, 0.8f, 0.0f),
            (400, 400, 100, 100, 0.2f, 0.0f),
            (300, 300, 50, 50, 0.5f, 0.9f)), letterbox, 640, 640);

        Assert.Equal(new PanelBox(50, 50, 150, 150), Assert.Single(panels));
    }
}
