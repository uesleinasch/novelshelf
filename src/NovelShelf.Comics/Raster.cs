using System.Runtime.InteropServices;
using SkiaSharp;

namespace NovelShelf.Comics;

internal sealed class Raster(int width, int height, int channels, byte[] data)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public int Channels { get; } = channels;
    public byte[] Data { get; } = data;
    public int Stride => Width * Channels;

    public static Raster Blank(int width, int height, int channels)
    {
        var data = new byte[width * height * channels];
        Array.Fill(data, (byte)255);
        return new Raster(width, height, channels, data);
    }

    public static (int Width, int Height) Measure(byte[] encoded)
    {
        using var stream = new SKMemoryStream(encoded);
        using var codec = SKCodec.Create(stream);
        return codec is null ? (0, 0) : (codec.Info.Width, codec.Info.Height);
    }

    public static Raster? Decode(byte[] encoded, bool color)
    {
        using var stream = new SKMemoryStream(encoded);
        using var codec = SKCodec.Create(stream);
        if (codec is null || codec.Info.Width == 0 || codec.Info.Height == 0)
            return null;
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        var result = codec.GetPixels(info, bitmap.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
            return null;

        var source = bitmap.GetPixelSpan();
        int width = info.Width, height = info.Height, rowBytes = bitmap.RowBytes;
        var channels = color ? 4 : 1;
        var data = new byte[width * height * channels];
        for (var y = 0; y < height; y++)
        {
            var row = source.Slice(y * rowBytes, width * 4);
            var target = y * width * channels;
            for (var x = 0; x < width; x++)
            {
                int r = row[x * 4], g = row[x * 4 + 1], b = row[x * 4 + 2], a = row[x * 4 + 3];
                if (a < 255)
                {
                    r = (r * a + 255 * (255 - a)) / 255;
                    g = (g * a + 255 * (255 - a)) / 255;
                    b = (b * a + 255 * (255 - a)) / 255;
                }
                if (color)
                {
                    var i = target + x * 4;
                    data[i] = (byte)r;
                    data[i + 1] = (byte)g;
                    data[i + 2] = (byte)b;
                    data[i + 3] = 255;
                }
                else
                {
                    data[target + x] = (byte)((r * 299 + g * 587 + b * 114) / 1000);
                }
            }
        }
        return new Raster(width, height, channels, data);
    }

    public byte Luma(int x, int y)
    {
        var i = (y * Width + x) * Channels;
        return Channels == 1 ? Data[i] : (byte)((Data[i] * 299 + Data[i + 1] * 587 + Data[i + 2] * 114) / 1000);
    }

    public Raster Crop(SKRectI bounds)
    {
        if (bounds.Left == 0 && bounds.Top == 0 && bounds.Width == Width && bounds.Height == Height)
            return this;
        var data = new byte[bounds.Width * bounds.Height * Channels];
        var rowBytes = bounds.Width * Channels;
        for (var y = 0; y < bounds.Height; y++)
            Buffer.BlockCopy(Data, ((bounds.Top + y) * Width + bounds.Left) * Channels, data, y * rowBytes, rowBytes);
        return new Raster(bounds.Width, bounds.Height, Channels, data);
    }

    public Raster Rotate(bool counterClockwise)
    {
        var data = new byte[Data.Length];
        int newWidth = Height, newHeight = Width;
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var (nx, ny) = counterClockwise ? (y, Width - 1 - x) : (Height - 1 - y, x);
                Buffer.BlockCopy(Data, (y * Width + x) * Channels, data, (ny * newWidth + nx) * Channels, Channels);
            }
        }
        return new Raster(newWidth, newHeight, Channels, data);
    }

    public Raster Resize(int width, int height)
    {
        if (width == Width && height == Height)
            return this;
        var horizontal = width == Width ? this : ResizeAxis(this, width, horizontal: true);
        return height == Height ? horizontal : ResizeAxis(horizontal, height, horizontal: false);
    }

    public void Paste(Raster source, int left, int top)
    {
        var rowBytes = source.Width * Channels;
        for (var y = 0; y < source.Height; y++)
            Buffer.BlockCopy(source.Data, y * rowBytes, Data, ((top + y) * Width + left) * Channels, rowBytes);
    }

    public void ApplyGamma(double gamma)
    {
        if (Math.Abs(gamma - 1) <= 0.001)
            return;
        var table = new byte[256];
        for (var i = 0; i < 256; i++)
            table[i] = (byte)Math.Round(255 * Math.Pow(i / 255.0, gamma));
        for (var i = 0; i < Data.Length; i++)
        {
            if (Channels == 4 && i % 4 == 3)
                continue;
            Data[i] = table[Data[i]];
        }
    }

    public void AutoLevel(double clip = 0.005)
    {
        var histogram = new int[256];
        for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
                histogram[Luma(x, y)]++;
        var cut = (long)(Width * Height * clip);
        int low = 0, high = 255;
        for (long seen = 0; low < 255 && (seen += histogram[low]) <= cut; low++) { }
        for (long seen = 0; high > 0 && (seen += histogram[high]) <= cut; high--) { }
        if (high - low < 64 || (low == 0 && high == 255))
            return;

        var table = new byte[256];
        for (var i = 0; i < 256; i++)
            table[i] = (byte)Math.Clamp((i - low) * 255 / (high - low), 0, 255);
        for (var i = 0; i < Data.Length; i++)
        {
            if (Channels == 4 && i % 4 == 3)
                continue;
            Data[i] = table[Data[i]];
        }
    }

    public void Sharpen(double amount = 0.6, int threshold = 4)
    {
        var blurred = new byte[Data.Length];
        var row = new int[Stride];
        for (var y = 0; y < Height; y++)
        {
            int above = Math.Max(0, y - 1) * Stride, here = y * Stride, below = Math.Min(Height - 1, y + 1) * Stride;
            for (var i = 0; i < Stride; i++)
                row[i] = Data[above + i] + 2 * Data[here + i] + Data[below + i];
            for (var x = 0; x < Width; x++)
            {
                int left = Math.Max(0, x - 1) * Channels, center = x * Channels, right = Math.Min(Width - 1, x + 1) * Channels;
                for (var c = 0; c < Channels; c++)
                    blurred[here + center + c] = (byte)((row[left + c] + 2 * row[center + c] + row[right + c] + 8) / 16);
            }
        }
        for (var i = 0; i < Data.Length; i++)
        {
            if (Channels == 4 && i % 4 == 3)
                continue;
            var detail = Data[i] - blurred[i];
            if (Math.Abs(detail) > threshold)
                Data[i] = Clamp((float)(Data[i] + amount * detail));
        }
    }

    public byte[] EncodeJpeg(int quality)
    {
        var colorType = Channels == 1 ? SKColorType.Gray8 : SKColorType.Rgba8888;
        using var bitmap = new SKBitmap(new SKImageInfo(Width, Height, colorType, SKAlphaType.Opaque));
        var pointer = bitmap.GetPixels();
        for (var y = 0; y < Height; y++)
            Marshal.Copy(Data, y * Stride, pointer + y * bitmap.RowBytes, Stride);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, quality)
            ?? throw new InvalidOperationException("Falha ao gerar o JPEG da página.");
        return encoded.ToArray();
    }

    private static Raster ResizeAxis(Raster source, int size, bool horizontal)
    {
        var inSize = horizontal ? source.Width : source.Height;
        var (starts, counts, weights, taps) = Coefficients(inSize, size);
        int channels = source.Channels;
        int width = horizontal ? size : source.Width, height = horizontal ? source.Height : size;
        var data = new byte[width * height * channels];
        var src = source.Data;

        if (horizontal)
        {
            for (var y = 0; y < height; y++)
            {
                var srcRow = y * source.Width * channels;
                var dstRow = y * width * channels;
                for (var x = 0; x < width; x++)
                {
                    for (var c = 0; c < channels; c++)
                    {
                        float sum = 0;
                        var w = x * taps;
                        for (var k = 0; k < counts[x]; k++)
                            sum += src[srcRow + (starts[x] + k) * channels + c] * weights[w + k];
                        data[dstRow + x * channels + c] = Clamp(sum);
                    }
                }
            }
        }
        else
        {
            var rowBytes = width * channels;
            for (var y = 0; y < height; y++)
            {
                var w = y * taps;
                var dstRow = y * rowBytes;
                for (var i = 0; i < rowBytes; i++)
                {
                    float sum = 0;
                    for (var k = 0; k < counts[y]; k++)
                        sum += src[(starts[y] + k) * rowBytes + i] * weights[w + k];
                    data[dstRow + i] = Clamp(sum);
                }
            }
        }
        return new Raster(width, height, channels, data);
    }

    private static (int[] Starts, int[] Counts, float[] Weights, int Taps) Coefficients(int inSize, int outSize)
    {
        var scale = (double)inSize / outSize;
        var filterScale = Math.Max(scale, 1.0);
        var support = filterScale;
        var taps = (int)Math.Ceiling(support) * 2 + 1;
        var starts = new int[outSize];
        var counts = new int[outSize];
        var weights = new float[outSize * taps];
        for (var i = 0; i < outSize; i++)
        {
            var center = (i + 0.5) * scale;
            var min = Math.Max(0, (int)(center - support + 0.5));
            var max = Math.Min(inSize, (int)(center + support + 0.5));
            max = Math.Min(max, min + taps);
            double total = 0;
            for (var j = min; j < max; j++)
            {
                var weight = Math.Max(0, 1 - Math.Abs((j - center + 0.5) / filterScale));
                weights[i * taps + (j - min)] = (float)weight;
                total += weight;
            }
            if (total <= 0)
            {
                weights[i * taps] = 1;
                total = 1;
                max = min + 1;
            }
            for (var j = 0; j < max - min; j++)
                weights[i * taps + j] = (float)(weights[i * taps + j] / total);
            starts[i] = min;
            counts[i] = max - min;
        }
        return (starts, counts, weights, taps);
    }

    private static byte Clamp(float value) => value <= 0 ? (byte)0 : value >= 255 ? (byte)255 : (byte)(value + 0.5f);
}
