using SkiaSharp;

namespace NovelShelf.Comics;

internal sealed class StripBuffer(int width, int bytesPerPixel)
{
    private byte[] _data = new byte[width * bytesPerPixel * 1024];
    private int _rows;

    public int Rows => _rows;
    private int RowBytes => width * bytesPerPixel;

    public void Append(SKBitmap rgba)
    {
        if (rgba.Width != width)
            throw new ArgumentException("A imagem precisa ter a largura da faixa.", nameof(rgba));
        Ensure(_rows + rgba.Height);
        var span = rgba.GetPixelSpan();
        for (var y = 0; y < rgba.Height; y++)
        {
            var source = y * rgba.RowBytes;
            var target = (_rows + y) * RowBytes;
            if (bytesPerPixel == 4)
            {
                span.Slice(source, RowBytes).CopyTo(_data.AsSpan(target, RowBytes));
                continue;
            }
            for (var x = 0; x < width; x++)
            {
                var i = source + x * 4;
                _data[target + x] = (byte)((span[i] * 299 + span[i + 1] * 587 + span[i + 2] * 114) / 1000);
            }
        }
        _rows += rgba.Height;
    }

    public int FindCut(int preferred, int minimum, int uniformRange)
    {
        var start = Math.Min(preferred, _rows);
        for (var y = start; y >= minimum; y--)
        {
            if (IsUniformRow(_data, y - 1, uniformRange))
                return y;
        }

        var calm = Activity(start - 1) * 35 / 100;
        var calmMinimum = Math.Max(minimum, start - start * 15 / 100);
        for (var y = start; y >= calmMinimum; y--)
        {
            if (Activity(y - 1) <= calm)
                return y;
        }
        return start;
    }

    private long Activity(int y)
    {
        long sum = 0;
        var start = y * RowBytes;
        var previous = Luma(_data, start);
        for (var x = 1; x < width; x++)
        {
            var value = Luma(_data, start + x * bytesPerPixel);
            sum += Math.Abs(value - previous);
            previous = value;
        }
        return sum;
    }

    private int Luma(byte[] data, int i) =>
        bytesPerPixel == 1 ? data[i] : (data[i] * 299 + data[i + 1] * 587 + data[i + 2] * 114) / 1000;

    public byte[] Take(int rows, int keep)
    {
        var slice = _data[..(rows * RowBytes)];
        var consumed = rows - keep;
        Buffer.BlockCopy(_data, consumed * RowBytes, _data, 0, (_rows - consumed) * RowBytes);
        _rows -= consumed;
        return slice;
    }

    public bool IsBlank(byte[] rows, int uniformRange)
    {
        var count = rows.Length / RowBytes;
        if (count == 0)
            return true;
        int min = 255, max = 0;
        for (var y = 0; y < count; y++)
        {
            var (rowMin, rowMax) = RowRange(rows, y);
            min = Math.Min(min, rowMin);
            max = Math.Max(max, rowMax);
            if (max - min > uniformRange)
                return false;
        }
        return true;
    }

    private bool IsUniformRow(byte[] data, int y, int uniformRange)
    {
        var (min, max) = RowRange(data, y);
        return max - min <= uniformRange;
    }

    private (int Min, int Max) RowRange(byte[] data, int y)
    {
        int min = 255, max = 0;
        var start = y * RowBytes;
        for (var x = 0; x < width; x++)
        {
            var i = start + x * bytesPerPixel;
            var value = bytesPerPixel == 1
                ? data[i]
                : (data[i] * 299 + data[i + 1] * 587 + data[i + 2] * 114) / 1000;
            if (value < min)
                min = value;
            if (value > max)
                max = value;
        }
        return (min, max);
    }

    private void Ensure(int rows)
    {
        var needed = rows * RowBytes;
        if (needed <= _data.Length)
            return;
        Array.Resize(ref _data, Math.Max(needed, _data.Length * 2));
    }
}
