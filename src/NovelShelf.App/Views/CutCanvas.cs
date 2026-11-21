using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using NovelShelf.Comics;

namespace NovelShelf.App.Views;

public enum CutShape
{
    Panel,
    Row,
}

public sealed class CutCanvas : Control
{
    private const double HitDistance = 7;
    private const double HandleSize = 8;
    private const double MinPanelSide = 0.03;
    private const double NewPanelWidth = 0.4;
    private const double NewPanelHeight = 0.25;

    public static readonly StyledProperty<Bitmap?> SourceProperty =
        AvaloniaProperty.Register<CutCanvas, Bitmap?>(nameof(Source));

    public static readonly StyledProperty<PageCuts?> CutsProperty =
        AvaloniaProperty.Register<CutCanvas, PageCuts?>(nameof(Cuts), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> EditsPanelsProperty =
        AvaloniaProperty.Register<CutCanvas, bool>(nameof(EditsPanels));

    public static readonly StyledProperty<bool> RightToLeftProperty =
        AvaloniaProperty.Register<CutCanvas, bool>(nameof(RightToLeft));

    public static readonly StyledProperty<IReadOnlyList<PanelFrame>?> DetectedProperty =
        AvaloniaProperty.Register<CutCanvas, IReadOnlyList<PanelFrame>?>(nameof(Detected));

    public static readonly StyledProperty<IReadOnlyList<PanelFrame>?> BubblesProperty =
        AvaloniaProperty.Register<CutCanvas, IReadOnlyList<PanelFrame>?>(nameof(Bubbles));

    private static readonly IPen BubblePen = new Pen(new SolidColorBrush(Color.FromArgb(0xd0, 0xea, 0x58, 0x0c)), 2, new DashStyle([3, 2], 0));
    private static readonly IPen DetectedPen = new Pen(new SolidColorBrush(Color.FromArgb(0xc0, 0x16, 0xa3, 0x4a)), 2);
    private static readonly IBrush PanelColor = new SolidColorBrush(Color.FromRgb(0x16, 0xa3, 0x4a));
    private static readonly IPen PanelPen = new Pen(PanelColor, 3);
    private static readonly IBrush PanelFill = new SolidColorBrush(Color.FromArgb(0x22, 0x16, 0xa3, 0x4a));
    private static readonly IPen RowPen = new Pen(new SolidColorBrush(Color.FromRgb(0xe1, 0x1d, 0x48)), 2);
    private static readonly IBrush SelectedColor = new SolidColorBrush(Color.FromRgb(0xf5, 0x9e, 0x0b));
    private static readonly IPen SelectedPen = new Pen(SelectedColor, 4);
    private static readonly IPen HandlePen = new Pen(SelectedColor, 2);
    private static readonly IPen PreviewPen = new Pen(SelectedColor, 2, new DashStyle([4, 3], 0));

    private List<double> _rows = [];
    private List<PanelFrame> _panels = [];
    private (CutShape Shape, int Index)? _selected;
    private Edges _grab;
    private Point _grabStart;
    private PanelFrame? _grabFrame;
    private bool _dragging;
    private (CutShape Shape, Point Point)? _preview;

    [Flags]
    private enum Edges
    {
        None = 0,
        Left = 1,
        Top = 2,
        Right = 4,
        Bottom = 8,
        Move = 16,
    }

    static CutCanvas()
    {
        AffectsRender<CutCanvas>(SourceProperty, CutsProperty, DetectedProperty, BubblesProperty, EditsPanelsProperty, RightToLeftProperty);
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

    public bool EditsPanels
    {
        get => GetValue(EditsPanelsProperty);
        set => SetValue(EditsPanelsProperty, value);
    }

    public bool RightToLeft
    {
        get => GetValue(RightToLeftProperty);
        set => SetValue(RightToLeftProperty, value);
    }

    public IReadOnlyList<PanelFrame>? Detected
    {
        get => GetValue(DetectedProperty);
        set => SetValue(DetectedProperty, value);
    }

    public IReadOnlyList<PanelFrame>? Bubbles
    {
        get => GetValue(BubblesProperty);
        set => SetValue(BubblesProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CutsProperty && !_dragging)
        {
            _rows = Cuts?.Rows.ToList() ?? [];
            _panels = Cuts?.Panels.ToList() ?? [];
            if (change.OldValue is null || _selected is { } s && s.Index >= Count(s.Shape))
                _selected = null;
        }
        else if (change.Property == SourceProperty || change.Property == EditsPanelsProperty)
        {
            _selected = null;
        }
    }

    public void Add(CutShape shape, Point? at = null)
    {
        var area = ImageRect;
        if (area.Width <= 0 || (shape == CutShape.Panel) != EditsPanels)
            return;
        var (fx, fy) = at is { } point && area.Contains(point)
            ? ((point.X - area.Left) / area.Width, (point.Y - area.Top) / area.Height)
            : (0.5, shape == CutShape.Row ? MiddleOfTallestBand() : 0.5);
        if (shape == CutShape.Row)
        {
            _rows.Add(Math.Clamp(fy, 0.01, 0.99));
            _selected = (CutShape.Row, _rows.Count - 1);
        }
        else
        {
            var left = Math.Clamp(fx - NewPanelWidth / 2, 0, 1 - NewPanelWidth);
            var top = Math.Clamp(fy - NewPanelHeight / 2, 0, 1 - NewPanelHeight);
            _panels.Add(new PanelFrame(left, top, left + NewPanelWidth, top + NewPanelHeight));
            _selected = (CutShape.Panel, _panels.Count - 1);
        }
        _preview = null;
        Focus();
        Commit();
    }

    public void ShowPreview(CutShape shape, Point point)
    {
        _preview = ImageRect.Contains(point) ? (shape, point) : null;
        InvalidateVisual();
    }

    public void HidePreview()
    {
        _preview = null;
        InvalidateVisual();
    }

    public void DeleteSelected()
    {
        if (_selected is not { } selected)
            return;
        if (selected.Shape == CutShape.Row)
            _rows.RemoveAt(selected.Index);
        else
            _panels.RemoveAt(selected.Index);
        _selected = null;
        Commit();
    }

    private int Count(CutShape shape) => shape == CutShape.Row ? _rows.Count : _panels.Count;

    private double MiddleOfTallestBand()
    {
        var edges = _rows.Order().Prepend(0).Append(1).ToList();
        var (top, bottom) = edges.Zip(edges.Skip(1)).MaxBy(e => e.Second - e.First);
        return (top + bottom) / 2;
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

    private static Rect ToScreen(Rect area, PanelFrame frame) => new(
        new Point(area.Left + frame.Left * area.Width, area.Top + frame.Top * area.Height),
        new Point(area.Left + frame.Right * area.Width, area.Top + frame.Bottom * area.Height));

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var area = ImageRect;
        if (Source is null || area.Width <= 0)
            return;
        context.DrawImage(Source, new Rect(Source.Size), area);
        foreach (var panel in Detected ?? [])
            context.DrawRectangle(null, DetectedPen, ToScreen(area, panel).Deflate(2));
        foreach (var bubble in Bubbles ?? [])
            context.DrawRectangle(null, BubblePen, ToScreen(area, bubble));

        if (EditsPanels)
            DrawPanels(context, area);
        else
        {
            for (var i = 0; i < _rows.Count; i++)
            {
                var y = area.Top + _rows[i] * area.Height;
                context.DrawLine(_selected == (CutShape.Row, i) ? SelectedPen : RowPen, new Point(area.Left, y), new Point(area.Right, y));
            }
        }
        DrawPreview(context, area);
    }

    private void DrawPanels(DrawingContext context, Rect area)
    {
        var order = new PageCuts { Panels = _panels }.PanelOrder(RightToLeft);
        for (var n = 0; n < order.Count; n++)
        {
            var i = order[n];
            var selected = _selected == (CutShape.Panel, i);
            var box = ToScreen(area, _panels[i]);
            context.DrawRectangle(PanelFill, selected ? SelectedPen : PanelPen, box);
            var label = new FormattedText((n + 1).ToString(CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 14, Brushes.White);
            var badge = new Rect(box.Left, box.Top, label.Width + 10, label.Height + 4);
            context.DrawRectangle(selected ? SelectedColor : PanelColor, null, badge);
            context.DrawText(label, new Point(badge.Left + 5, badge.Top + 2));
            if (!selected)
                continue;
            foreach (var x in new[] { box.Left, box.Center.X, box.Right })
                foreach (var y in new[] { box.Top, box.Center.Y, box.Bottom })
                    if (x != box.Center.X || y != box.Center.Y)
                        context.DrawRectangle(Brushes.White, HandlePen,
                            new Rect(x - HandleSize / 2, y - HandleSize / 2, HandleSize, HandleSize));
        }
    }

    private void DrawPreview(DrawingContext context, Rect area)
    {
        if (_preview is not { } preview)
            return;
        if (preview.Shape == CutShape.Row)
        {
            context.DrawLine(PreviewPen, new Point(area.Left, preview.Point.Y), new Point(area.Right, preview.Point.Y));
            return;
        }
        var fx = (preview.Point.X - area.Left) / area.Width;
        var fy = (preview.Point.Y - area.Top) / area.Height;
        var left = Math.Clamp(fx - NewPanelWidth / 2, 0, 1 - NewPanelWidth);
        var top = Math.Clamp(fy - NewPanelHeight / 2, 0, 1 - NewPanelHeight);
        context.DrawRectangle(null, PreviewPen, ToScreen(area, new PanelFrame(left, top, left + NewPanelWidth, top + NewPanelHeight)));
    }

    private (CutShape Shape, int Index, Edges Grab)? HitTest(Point point)
    {
        var area = ImageRect;
        if (!EditsPanels)
        {
            for (var i = 0; i < _rows.Count; i++)
            {
                if (Math.Abs(point.Y - (area.Top + _rows[i] * area.Height)) <= HitDistance)
                    return (CutShape.Row, i, Edges.Top);
            }
            return null;
        }

        var candidates = Enumerable.Range(0, _panels.Count)
            .OrderByDescending(i => _selected == (CutShape.Panel, i))
            .ThenBy(i => (_panels[i].Right - _panels[i].Left) * (_panels[i].Bottom - _panels[i].Top))
            .ToList();
        foreach (var i in candidates)
        {
            var box = ToScreen(area, _panels[i]);
            if (EdgesAt(box, point) is var edges and not Edges.None)
                return (CutShape.Panel, i, edges);
        }
        foreach (var i in candidates)
        {
            if (ToScreen(area, _panels[i]).Contains(point))
                return (CutShape.Panel, i, Edges.Move);
        }
        return null;
    }

    private static Edges EdgesAt(Rect box, Point point)
    {
        if (!box.Inflate(HitDistance).Contains(point))
            return Edges.None;
        var edges = Edges.None;
        if (Math.Abs(point.X - box.Left) <= HitDistance)
            edges |= Edges.Left;
        else if (Math.Abs(point.X - box.Right) <= HitDistance)
            edges |= Edges.Right;
        if (Math.Abs(point.Y - box.Top) <= HitDistance)
            edges |= Edges.Top;
        else if (Math.Abs(point.Y - box.Bottom) <= HitDistance)
            edges |= Edges.Bottom;
        return edges;
    }

    private static Cursor CursorFor(Edges edges) => new(edges switch
    {
        Edges.Left | Edges.Top => StandardCursorType.TopLeftCorner,
        Edges.Right | Edges.Bottom => StandardCursorType.BottomRightCorner,
        Edges.Right | Edges.Top => StandardCursorType.TopRightCorner,
        Edges.Left | Edges.Bottom => StandardCursorType.BottomLeftCorner,
        Edges.Left or Edges.Right => StandardCursorType.SizeWestEast,
        Edges.Top or Edges.Bottom => StandardCursorType.SizeNorthSouth,
        Edges.Move => StandardCursorType.SizeAll,
        _ => StandardCursorType.Arrow,
    });

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (ImageRect.Width <= 0)
            return;
        var point = e.GetPosition(this);
        var hit = HitTest(point);
        _selected = hit is { } h ? (h.Shape, h.Index) : null;
        _grab = hit?.Grab ?? Edges.None;
        _grabStart = point;
        _grabFrame = hit is { Shape: CutShape.Panel } p ? _panels[p.Index] : null;
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
            Cursor = HitTest(point) is { } hit ? CursorFor(hit.Grab) : Cursor.Default;
            return;
        }
        var area = ImageRect;
        if (selected.Shape == CutShape.Row)
            _rows[selected.Index] = Math.Clamp((point.Y - area.Top) / area.Height, 0.01, 0.99);
        else if (_grabFrame is { } start)
            _panels[selected.Index] = Reshape(start, _grab,
                (point.X - _grabStart.X) / area.Width, (point.Y - _grabStart.Y) / area.Height);
        InvalidateVisual();
    }

    private static PanelFrame Reshape(PanelFrame start, Edges grab, double dx, double dy)
    {
        if (grab == Edges.Move)
        {
            dx = Math.Clamp(dx, -start.Left, 1 - start.Right);
            dy = Math.Clamp(dy, -start.Top, 1 - start.Bottom);
            return new PanelFrame(start.Left + dx, start.Top + dy, start.Right + dx, start.Bottom + dy);
        }
        var (left, top, right, bottom) = (start.Left, start.Top, start.Right, start.Bottom);
        if (grab.HasFlag(Edges.Left))
            left = Math.Clamp(left + dx, 0, right - MinPanelSide);
        if (grab.HasFlag(Edges.Right))
            right = Math.Clamp(right + dx, left + MinPanelSide, 1);
        if (grab.HasFlag(Edges.Top))
            top = Math.Clamp(top + dy, 0, bottom - MinPanelSide);
        if (grab.HasFlag(Edges.Bottom))
            bottom = Math.Clamp(bottom + dy, top + MinPanelSide, 1);
        return new PanelFrame(left, top, right, bottom);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging)
            return;
        _dragging = false;
        e.Pointer.Capture(null);
        if (e.GetPosition(this) != _grabStart)
            Commit();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is not (Key.Delete or Key.Back) || _selected is null)
            return;
        DeleteSelected();
        e.Handled = true;
    }

    private void Commit()
    {
        var selectedRow = _selected is { Shape: CutShape.Row } s ? _rows[s.Index] : (double?)null;
        var cuts = ((Cuts ?? new PageCuts()) with { Rows = _rows.ToList(), Panels = _panels.ToList() }).Normalized();
        var keptPanels = cuts.Panels.Count == _panels.Count;
        _rows = cuts.Rows.ToList();
        _panels = cuts.Panels.ToList();
        _selected = selectedRow is { } row && _rows.IndexOf(row) is >= 0 and var r ? (CutShape.Row, r)
            : _selected is { Shape: CutShape.Panel } && keptPanels ? _selected
            : null;
        _dragging = true;
        Cuts = cuts;
        _dragging = false;
        InvalidateVisual();
    }
}
