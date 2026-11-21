using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using NovelShelf.Comics;

namespace NovelShelf.App.Views;

public sealed class CutCanvas : Control
{
    private const double HitDistance = 7;

    public static readonly StyledProperty<Bitmap?> SourceProperty =
        AvaloniaProperty.Register<CutCanvas, Bitmap?>(nameof(Source));

    public static readonly StyledProperty<PageCuts?> CutsProperty =
        AvaloniaProperty.Register<CutCanvas, PageCuts?>(nameof(Cuts), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<IReadOnlyList<PanelFrame>?> PanelsProperty =
        AvaloniaProperty.Register<CutCanvas, IReadOnlyList<PanelFrame>?>(nameof(Panels));

    private static readonly IPen PanelPen = new Pen(new SolidColorBrush(Color.FromArgb(0xc0, 0x16, 0xa3, 0x4a)), 2);
    private static readonly IBrush PanelLabel = new SolidColorBrush(Color.FromRgb(0x16, 0xa3, 0x4a));
    private static readonly IPen RowPen = new Pen(new SolidColorBrush(Color.FromRgb(0xe1, 0x1d, 0x48)), 2);
    private static readonly IPen ColumnPen = new Pen(new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xeb)), 2);
    private static readonly IPen SelectedPen = new Pen(new SolidColorBrush(Color.FromRgb(0xf5, 0x9e, 0x0b)), 4);

    private List<double> _rows = [];
    private List<VerticalCut> _columns = [];
    private (bool IsRow, int Index)? _selected;
    private bool _dragging;

    static CutCanvas()
    {
        AffectsRender<CutCanvas>(SourceProperty, CutsProperty, PanelsProperty);
        FocusableProperty.OverrideDefaultValue<CutCanvas>(true);
    }

    public Bitmap? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public PageCuts? Cuts
    {
        get => GetValue(CutsProperty);
        set => SetValue(CutsProperty, value);
    }

    public IReadOnlyList<PanelFrame>? Panels
    {
        get => GetValue(PanelsProperty);
        set => SetValue(PanelsProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CutsProperty && !_dragging)
        {
            _rows = Cuts?.Rows.ToList() ?? [];
            _columns = Cuts?.Columns.ToList() ?? [];
            if (change.OldValue is null || _selected is { } s && (s.IsRow ? s.Index >= _rows.Count : s.Index >= _columns.Count))
                _selected = null;
        }
        else if (change.Property == SourceProperty)
        {
            _selected = null;
        }
    }

    private Rect ImageRect
    {
        get
        {
            if (Source is null || Bounds.Width <= 0 || Bounds.Height <= 0)
                return default;
            var size = Source.Size;
            var scale = Math.Min(Bounds.Width / size.Width, Bounds.Height / size.Height);
            var width = size.Width * scale;
            var height = size.Height * scale;
            return new Rect((Bounds.Width - width) / 2, (Bounds.Height - height) / 2, width, height);
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var area = ImageRect;
        if (Source is null || area.Width <= 0)
            return;
        context.DrawImage(Source, new Rect(Source.Size), area);
        DrawPanels(context, area);

        for (var i = 0; i < _rows.Count; i++)
        {
            var y = area.Top + _rows[i] * area.Height;
            context.DrawLine(_selected == (true, i) ? SelectedPen : RowPen, new Point(area.Left, y), new Point(area.Right, y));
        }
        for (var i = 0; i < _columns.Count; i++)
        {
            var (top, bottom) = BandAround(_columns[i].Y);
            var x = area.Left + _columns[i].X * area.Width;
            context.DrawLine(_selected == (false, i) ? SelectedPen : ColumnPen,
                new Point(x, area.Top + top * area.Height), new Point(x, area.Top + bottom * area.Height));
        }
    }

    private void DrawPanels(DrawingContext context, Rect area)
    {
        if (Panels is not { Count: > 0 } panels)
            return;
        for (var i = 0; i < panels.Count; i++)
        {
            var panel = panels[i];
            var box = new Rect(
                new Point(area.Left + panel.Left * area.Width, area.Top + panel.Top * area.Height),
                new Point(area.Left + panel.Right * area.Width, area.Top + panel.Bottom * area.Height)).Deflate(2);
            context.DrawRectangle(null, PanelPen, box);
            var label = new FormattedText((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 14, Brushes.White);
            var badge = new Rect(box.Left, box.Top, label.Width + 10, label.Height + 4);
            context.DrawRectangle(PanelLabel, null, badge);
            context.DrawText(label, new Point(badge.Left + 5, badge.Top + 2));
        }
    }

    private (double Top, double Bottom) BandAround(double y)
    {
        var top = _rows.Where(r => r <= y).DefaultIfEmpty(0).Max();
        var bottom = _rows.Where(r => r > y).DefaultIfEmpty(1).Min();
        return (top, bottom);
    }

    private (bool IsRow, int Index)? HitTest(Point point)
    {
        var area = ImageRect;
        for (var i = 0; i < _columns.Count; i++)
        {
            var (top, bottom) = BandAround(_columns[i].Y);
            var x = area.Left + _columns[i].X * area.Width;
            if (Math.Abs(point.X - x) <= HitDistance
                && point.Y >= area.Top + top * area.Height && point.Y <= area.Top + bottom * area.Height)
                return (false, i);
        }
        for (var i = 0; i < _rows.Count; i++)
        {
            if (Math.Abs(point.Y - (area.Top + _rows[i] * area.Height)) <= HitDistance)
                return (true, i);
        }
        return null;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var area = ImageRect;
        if (area.Width <= 0)
            return;
        var point = e.GetPosition(this);
        var hit = HitTest(point);
        var fx = Math.Clamp((point.X - area.Left) / area.Width, 0, 1);
        var fy = Math.Clamp((point.Y - area.Top) / area.Height, 0, 1);

        if (hit is null && e.ClickCount == 2 && area.Contains(point))
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                _columns.Add(new VerticalCut(fx, fy));
                _selected = (false, _columns.Count - 1);
            }
            else
            {
                _rows.Add(fy);
                _selected = (true, _rows.Count - 1);
            }
            Commit();
            e.Handled = true;
            return;
        }

        _selected = hit;
        _dragging = hit is not null;
        if (_dragging)
            e.Pointer.Capture(this);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = e.GetPosition(this);
        if (!_dragging || _selected is not { } selected)
        {
            Cursor = HitTest(point) switch
            {
                { IsRow: true } => new Cursor(StandardCursorType.SizeNorthSouth),
                { IsRow: false } => new Cursor(StandardCursorType.SizeWestEast),
                _ => Cursor.Default,
            };
            return;
        }
        var area = ImageRect;
        if (selected.IsRow)
            _rows[selected.Index] = Math.Clamp((point.Y - area.Top) / area.Height, 0.01, 0.99);
        else
            _columns[selected.Index] = _columns[selected.Index] with { X = Math.Clamp((point.X - area.Left) / area.Width, 0.01, 0.99) };
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging)
            return;
        _dragging = false;
        e.Pointer.Capture(null);
        Commit();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is not (Key.Delete or Key.Back) || _selected is not { } selected)
            return;
        if (selected.IsRow)
            _rows.RemoveAt(selected.Index);
        else
            _columns.RemoveAt(selected.Index);
        _selected = null;
        Commit();
        e.Handled = true;
    }

    private void Commit()
    {
        var selectedRow = _selected is { IsRow: true } s ? _rows[s.Index] : (double?)null;
        var selectedColumn = _selected is { IsRow: false } c ? _columns[c.Index] : null;
        var cuts = new PageCuts { Rows = _rows.ToList(), Columns = _columns.ToList() }.Normalized();
        _rows = cuts.Rows.ToList();
        _columns = cuts.Columns.ToList();
        _selected = selectedRow is { } row && _rows.IndexOf(row) is >= 0 and var r ? (true, r)
            : selectedColumn is not null && _columns.IndexOf(selectedColumn) is >= 0 and var k ? (false, k)
            : null;
        _dragging = true;
        Cuts = cuts;
        _dragging = false;
        InvalidateVisual();
    }
}
