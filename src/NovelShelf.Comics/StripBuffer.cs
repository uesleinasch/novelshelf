namespace NovelShelf.Comics;

internal sealed class StripBuffer(int width, int bytesPerPixel)
{
    private byte[] _data = new byte[width * bytesPerPixel * 1024];
    private int _rows;

    public int Rows => _rows;
    private int RowBytes => width * bytesPerPixel;

    public void Append(Raster image)
    {
        if (image.Width != width || image.Channels != bytesPerPixel)
            throw new ArgumentException("A imagem precisa ter a largura e os canais da faixa.", nameof(image));
        Ensure(_rows + image.Height);
        Buffer.BlockCopy(image.Data, 0, _data, _rows * RowBytes, image.Height * RowBytes);
        _rows += image.Height;
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

    public void SkipLeadingWhite(int uniformRange, int whiteLevel = 235)
    {
        var skip = 0;
        while (skip < _rows)
        {
            var (min, max) = RowRange(_data, skip);
            if (min < whiteLevel || max - min > uniformRange)
                break;
            skip++;
        }
        if (skip == 0)
            return;
        Buffer.BlockCopy(_data, skip * RowBytes, _data, 0, (_rows - skip) * RowBytes);
        _rows -= skip;
    }

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
