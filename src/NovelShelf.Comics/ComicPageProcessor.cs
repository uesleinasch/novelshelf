using System.Runtime.InteropServices;
using SkiaSharp;

namespace NovelShelf.Comics;

public sealed record ComicPageOptions(
    KindleProfile Profile,
    bool RightToLeft = false,
    ComicLayout? Layout = null,
    int JpegQuality = 85)
{
    public ComicLayout EffectiveLayout => (Layout ?? ComicLayout.Default).Normalized();
}

public static class ComicPageProcessor
{
    private const double WebtoonAspect = 2.0;
    private const int BackgroundTolerance = 48;
    private const int UniformRowRange = 12;

    public static bool IsWebtoon(IReadOnlyList<byte[]> pages)
    {
        var ratios = pages.Select(Dimensions)
            .Where(d => d.Width > 0)
            .Select(d => (double)d.Height / d.Width)
            .Order()
            .ToList();
        return ratios.Count > 0 && ratios[ratios.Count / 2] >= WebtoonAspect;
    }

    public static bool UsesStrip(IReadOnlyList<byte[]> pages, ComicLayout layout) => layout.Mode switch
    {
        ComicReadingMode.Strip => true,
        ComicReadingMode.Pages => false,
        _ => IsWebtoon(pages),
    };

    public static List<byte[]> ProcessChapter(IReadOnlyList<byte[]> pages, ComicPageOptions options) =>
        UsesStrip(pages, options.EffectiveLayout)
            ? ProcessStrip(pages, options)
            : pages.SelectMany(p => ProcessPage(p, options)).ToList();

    public static List<byte[]> ProcessPage(byte[] data, ComicPageOptions options)
    {
        var layout = options.EffectiveLayout;
        using var source = DecodeOpaque(data);
        if (source is null)
            return [];

        var bounds = layout.Margins switch
        {
            MarginMode.Auto => ContentBounds(source),
            MarginMode.Manual => ManualBounds(source.Width, source.Height, layout),
            _ => new SKRectI(0, 0, source.Width, source.Height),
        };
        using var cropped = Crop(source, bounds);
        var output = new List<byte[]>();
        if (cropped.Width > cropped.Height * 1.05 && layout.WidePages != WidePageMode.Keep)
        {
            if (layout.WidePages == WidePageMode.Rotate)
            {
                using var rotated = Rotate(cropped, options.RightToLeft);
                output.Add(FitAndEncode(rotated, options));
                return output;
            }
            var half = cropped.Width / 2;
            using var left = Crop(cropped, new SKRectI(0, 0, half, cropped.Height));
            using var right = Crop(cropped, new SKRectI(half, 0, cropped.Width, cropped.Height));
            foreach (var part in options.RightToLeft ? new[] { right, left } : [left, right])
                output.Add(FitAndEncode(part, options));
            return output;
        }
        output.Add(FitAndEncode(cropped, options));
        return output;
    }

    public static List<byte[]> ProcessStrip(IReadOnlyList<byte[]> pages, ComicPageOptions options)
    {
        var profile = options.Profile;
        var layout = options.EffectiveLayout;
        var strip = new StripBuffer(profile.Width, profile.Color ? 4 : 1);
        var output = new List<byte[]>();
        var keep = (int)Math.Round(profile.Height * layout.Overlap);
        var minimumCut = (int)Math.Round(profile.Height * (1 - layout.CutSearch));

        foreach (var data in pages)
        {
            using var source = DecodeOpaque(data);
            if (source is null)
                continue;
            var bounds = layout.Margins == MarginMode.Manual
                ? ManualBounds(source.Width, source.Height, layout with { MarginTop = 0, MarginBottom = 0 })
                : new SKRectI(0, 0, source.Width, source.Height);
            using var cropped = Crop(source, bounds);
            var height = Math.Max(1, (int)Math.Round((double)cropped.Height * profile.Width / cropped.Width));
            using var scaled = ScaleTo(cropped, profile.Width, height);
            strip.Append(scaled);
            while (strip.Rows >= profile.Height + profile.Height / 4)
                EmitSlice(strip, options, output, minimumCut, keep);
        }
        while (strip.Rows > keep + profile.Height / 20)
        {
            if (strip.Rows <= profile.Height)
            {
                var rows = strip.Rows;
                var last = strip.Take(rows, 0);
                if (!strip.IsBlank(last, UniformRowRange))
                    output.Add(EncodeSlice(last, rows, options));
                break;
            }
            EmitSlice(strip, options, output, minimumCut, keep);
        }
        return output;
    }

    private static void EmitSlice(StripBuffer strip, ComicPageOptions options, List<byte[]> output, int minimumCut, int keep)
    {
        var cut = strip.FindCut(options.Profile.Height, Math.Max(1, minimumCut), UniformRowRange);
        var slice = strip.Take(cut, Math.Min(keep, cut - 1));
        if (!strip.IsBlank(slice, UniformRowRange))
            output.Add(EncodeSlice(slice, cut, options));
    }

    private static byte[] EncodeSlice(byte[] rows, int rowCount, ComicPageOptions options)
    {
        var profile = options.Profile;
        var colorType = profile.Color ? SKColorType.Rgba8888 : SKColorType.Gray8;
        using var page = new SKBitmap(new SKImageInfo(profile.Width, profile.Height, colorType, SKAlphaType.Opaque));
        var pixels = new byte[page.RowBytes * profile.Height];
        Array.Fill(pixels, (byte)255);
        var rowBytes = profile.Width * page.BytesPerPixel;
        var top = (profile.Height - rowCount) / 2;
        for (var y = 0; y < rowCount; y++)
            Buffer.BlockCopy(rows, y * rowBytes, pixels, (top + y) * page.RowBytes, rowBytes);
        ApplyGamma(pixels, page.BytesPerPixel, options.EffectiveLayout.Gamma);
        Marshal.Copy(pixels, 0, page.GetPixels(), pixels.Length);
        return Encode(page, options.JpegQuality);
    }

    private static byte[] FitAndEncode(SKBitmap source, ComicPageOptions options)
    {
        var profile = options.Profile;
        var scale = Math.Min((double)profile.Width / source.Width, (double)profile.Height / source.Height);
        var width = Math.Clamp((int)Math.Round(source.Width * scale), 1, profile.Width);
        var height = Math.Clamp((int)Math.Round(source.Height * scale), 1, profile.Height);
        using var scaled = ScaleTo(source, width, height);

        var colorType = profile.Color ? SKColorType.Rgba8888 : SKColorType.Gray8;
        using var canvasPage = new SKBitmap(new SKImageInfo(profile.Width, profile.Height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(canvasPage))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(scaled, (profile.Width - width) / 2, (profile.Height - height) / 2);
        }

        using var page = profile.Color
            ? canvasPage.Copy()
            : canvasPage.Copy(colorType) ?? throw new InvalidOperationException("Falha ao converter a página para tons de cinza.");
        var gamma = options.EffectiveLayout.Gamma;
        if (Math.Abs(gamma - 1) > 0.001)
        {
            var pixels = new byte[page.RowBytes * page.Height];
            Marshal.Copy(page.GetPixels(), pixels, 0, pixels.Length);
            ApplyGamma(pixels, page.BytesPerPixel, gamma);
            Marshal.Copy(pixels, 0, page.GetPixels(), pixels.Length);
        }
        return Encode(page, options.JpegQuality);
    }

    internal static void ApplyGamma(byte[] pixels, int bytesPerPixel, double gamma)
    {
        if (Math.Abs(gamma - 1) <= 0.001)
            return;
        var table = new byte[256];
        for (var i = 0; i < 256; i++)
            table[i] = (byte)Math.Round(255 * Math.Pow(i / 255.0, gamma));
        for (var i = 0; i < pixels.Length; i++)
        {
            if (bytesPerPixel == 4 && i % 4 == 3)
                continue;
            pixels[i] = table[pixels[i]];
        }
    }

    private static byte[] Encode(SKBitmap bitmap, int quality)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality)
            ?? throw new InvalidOperationException("Falha ao gerar o JPEG da página.");
        return data.ToArray();
    }

    internal static (int Width, int Height) Dimensions(byte[] data)
    {
        using var stream = new SKMemoryStream(data);
        using var codec = SKCodec.Create(stream);
        return codec is null ? (0, 0) : (codec.Info.Width, codec.Info.Height);
    }

    private static SKBitmap? DecodeOpaque(byte[] data)
    {
        using var raw = SKBitmap.Decode(data);
        if (raw is null || raw.Width == 0 || raw.Height == 0)
            return null;
        var bitmap = new SKBitmap(new SKImageInfo(raw.Width, raw.Height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        canvas.DrawBitmap(raw, 0, 0);
        return bitmap;
    }

    private static SKRectI ManualBounds(int width, int height, ComicLayout layout)
    {
        var left = (int)Math.Round(width * layout.MarginLeft);
        var right = width - (int)Math.Round(width * layout.MarginRight);
        var top = (int)Math.Round(height * layout.MarginTop);
        var bottom = height - (int)Math.Round(height * layout.MarginBottom);
        if (right - left < width / 10 || bottom - top < height / 10)
            return new SKRectI(0, 0, width, height);
        return new SKRectI(left, top, right, bottom);
    }

    private static SKBitmap Crop(SKBitmap source, SKRectI bounds)
    {
        var bitmap = new SKBitmap(new SKImageInfo(bounds.Width, bounds.Height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(bitmap);
        canvas.DrawBitmap(source, bounds, new SKRect(0, 0, bounds.Width, bounds.Height));
        return bitmap;
    }

    private static SKBitmap Rotate(SKBitmap source, bool counterClockwise)
    {
        var bitmap = new SKBitmap(new SKImageInfo(source.Height, source.Width, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(bitmap);
        if (counterClockwise)
        {
            canvas.Translate(0, source.Width);
            canvas.RotateDegrees(-90);
        }
        else
        {
            canvas.Translate(source.Height, 0);
            canvas.RotateDegrees(90);
        }
        canvas.DrawBitmap(source, 0, 0);
        return bitmap;
    }

    private static SKBitmap ScaleTo(SKBitmap source, int width, int height)
    {
        var current = source;
        while (current.Width >= width * 2 && current.Height >= height * 2)
        {
            var half = Resize(current, current.Width / 2, current.Height / 2, new SKSamplingOptions(SKFilterMode.Linear));
            if (!ReferenceEquals(current, source))
                current.Dispose();
            current = half;
        }
        var result = Resize(current, width, height, new SKSamplingOptions(SKCubicResampler.Mitchell));
        if (!ReferenceEquals(current, source))
            current.Dispose();
        return result;
    }

    private static SKBitmap Resize(SKBitmap source, int width, int height, SKSamplingOptions sampling)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(bitmap);
        using var image = SKImage.FromBitmap(source);
        canvas.DrawImage(image, new SKRect(0, 0, width, height), sampling);
        return bitmap;
    }

    internal static SKRectI ContentBounds(SKBitmap bitmap)
    {
        int width = bitmap.Width, height = bitmap.Height;
        var full = new SKRectI(0, 0, width, height);
        var luma = Luma(bitmap);

        long sum = 0;
        var count = 0;
        for (var x = 0; x < width; x++)
        {
            sum += luma[x] + luma[(height - 1) * width + x];
            count += 2;
        }
        for (var y = 0; y < height; y++)
        {
            sum += luma[y * width] + luma[y * width + width - 1];
            count += 2;
        }
        var background = (int)(sum / count);
        if (background is > 60 and < 200)
            return full;

        bool IsContent(int i) => Math.Abs(luma[i] - background) > BackgroundTolerance;
        var minRow = Math.Max(1, width / 200);
        var minColumn = Math.Max(1, height / 200);

        bool RowHasContent(int y)
        {
            var n = 0;
            for (var x = 0; x < width; x++)
                if (IsContent(y * width + x) && ++n >= minRow)
                    return true;
            return false;
        }

        bool ColumnHasContent(int x, int top, int bottom)
        {
            var n = 0;
            for (var y = top; y < bottom; y++)
                if (IsContent(y * width + x) && ++n >= minColumn)
                    return true;
            return false;
        }

        var top = 0;
        while (top < height && !RowHasContent(top))
            top++;
        if (top == height)
            return full;
        var bottom = height;
        while (bottom > top && !RowHasContent(bottom - 1))
            bottom--;
        var left = 0;
        while (left < width && !ColumnHasContent(left, top, bottom))
            left++;
        var right = width;
        while (right > left && !ColumnHasContent(right - 1, top, bottom))
            right--;

        var padX = width / 100;
        var padY = height / 100;
        var bounds = new SKRectI(
            Math.Max(0, left - padX), Math.Max(0, top - padY),
            Math.Min(width, right + padX), Math.Min(height, bottom + padY));
        return bounds.Width < width / 2 || bounds.Height < height / 2 ? full : bounds;
    }

    internal static byte[] Luma(SKBitmap bitmap)
    {
        var span = bitmap.GetPixelSpan();
        var stride = bitmap.RowBytes;
        var luma = new byte[bitmap.Width * bitmap.Height];
        for (var y = 0; y < bitmap.Height; y++)
        {
            var row = y * stride;
            for (var x = 0; x < bitmap.Width; x++)
            {
                var i = row + x * 4;
                luma[y * bitmap.Width + x] = (byte)((span[i] * 299 + span[i + 1] * 587 + span[i + 2] * 114) / 1000);
            }
        }
        return luma;
    }
}
