using System.ComponentModel;
using System.Drawing.Drawing2D;
using UzDoomMapEditor.Core;

namespace UzDoomMapEditor.Editor;

internal sealed class VertexSelection
{
    [Browsable(false)]
    public Sector Sector { get; }

    [Browsable(false)]
    public int Index { get; }

    public VertexSelection(Sector sector, int index)
    {
        Sector = sector;
        Index = index;
    }

    [Category("Vertex")]
    [ReadOnly(true)]
    public string SectorName => Sector.Name;

    [Category("Vertex")]
    [ReadOnly(true)]
    public int VertexNumber => Index + 1;

    [Category("Position")]
    public int X
    {
        get => Sector.Vertices[Index].X;
        set => Sector.Vertices[Index].X = value;
    }

    [Category("Position")]
    public int Y
    {
        get => Sector.Vertices[Index].Y;
        set => Sector.Vertices[Index].Y = value;
    }

    public override string ToString() => $"{Sector.Name} vertex {Index + 1}";
}

internal sealed class EdgeSelection
{
    [Browsable(false)]
    public Sector Sector { get; }

    [Browsable(false)]
    public int StartIndex { get; }

    public EdgeSelection(Sector sector, int startIndex)
    {
        Sector = sector;
        StartIndex = startIndex;
    }

    private MapVertex A => Sector.Vertices[StartIndex];
    private MapVertex B => Sector.Vertices[(StartIndex + 1) % Sector.Vertices.Count];

    [Category("Edge")]
    [ReadOnly(true)]
    public string SectorName => Sector.Name;

    [Category("Edge")]
    [ReadOnly(true)]
    public int EdgeNumber => StartIndex + 1;

    [Category("Edge")]
    [ReadOnly(true)]
    public double Length => Math.Round(Math.Sqrt(Math.Pow(B.X - A.X, 2) + Math.Pow(B.Y - A.Y, 2)), 2);

    [Category("Start")]
    [ReadOnly(true)]
    public int X1 => A.X;

    [Category("Start")]
    [ReadOnly(true)]
    public int Y1 => A.Y;

    [Category("End")]
    [ReadOnly(true)]
    public int X2 => B.X;

    [Category("End")]
    [ReadOnly(true)]
    public int Y2 => B.Y;

    public override string ToString() => $"{Sector.Name} edge {StartIndex + 1}";
}

public sealed class MapCanvas : Control
{
    private EditorProject _project = new();
    private EditorTool _tool;
    private object? _selected;
    private int _gridSize = 64;

    private float _zoom = 1f;
    private PointF _origin;
    private bool _originInitialised;

    private bool _drawingArea;
    private EditorTool _drawingTool;
    private PointF _areaStart;
    private PointF _areaCurrent;

    private bool _panning;
    private Point _panMouseStart;
    private PointF _panOriginStart;

    private bool _dragging;
    private PointF _dragStartWorld;
    private readonly List<(MapVertex Vertex, int X, int Y)> _dragVertices = new();
    private Door? _dragDoor;
    private int _dragDoorX;
    private int _dragDoorY;
    private MapThing? _dragThing;
    private int _dragThingX;
    private int _dragThingY;

    public event Action<object?>? SelectionChanged;
    public event Action? ProjectEdited;
    public event Action? ProjectPreviewChanged;

    public EditorProject Project
    {
        get => _project;
        set
        {
            _project = value ?? new EditorProject();
            _project.Normalize();
            Select(null);
            Invalidate();
        }
    }

    internal EditorTool Tool
    {
        get => _tool;
        set
        {
            _tool = value;
            Cursor = value == EditorTool.Select ? Cursors.Default : Cursors.Cross;
            Invalidate();
        }
    }

    public int GridSize
    {
        get => _gridSize;
        set
        {
            _gridSize = Math.Clamp(value, 1, 1024);
            Invalidate();
        }
    }

    public MapCanvas()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(31, 33, 37);
        ForeColor = Color.Gainsboro;
        TabStop = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    public void ResetView()
    {
        _zoom = 1f;
        _origin = new PointF(ClientSize.Width / 2f, ClientSize.Height / 2f);
        _originInitialised = true;
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (!_originInitialised && ClientSize.Width > 0 && ClientSize.Height > 0)
            ResetView();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        DrawGrid(e.Graphics);

        foreach (var sector in _project.Sectors)
            DrawSector(e.Graphics, sector);

        foreach (var door in _project.Doors)
            DrawDoor(e.Graphics, door, ReferenceEquals(door, _selected));

        foreach (var thing in _project.Things)
            DrawThing(e.Graphics, thing, ReferenceEquals(thing, _selected));

        if (_drawingArea)
            DrawDraftArea(e.Graphics);

        DrawViewportLabel(e.Graphics);
    }

    private void DrawViewportLabel(Graphics g)
    {
        using var bg = new SolidBrush(Color.FromArgb(175, 15, 16, 18));
        using var fg = new SolidBrush(Color.Gainsboro);
        var text = $"TOP  |  Grid {_gridSize}  |  {_tool}";
        var size = g.MeasureString(text, Font);
        g.FillRectangle(bg, 8, 8, size.Width + 12, size.Height + 6);
        g.DrawString(text, Font, fg, 14, 11);
    }

    private void DrawGrid(Graphics g)
    {
        var a = ScreenToWorld(new Point(0, 0));
        var b = ScreenToWorld(new Point(ClientSize.Width, ClientSize.Height));
        var minX = Math.Min(a.X, b.X);
        var maxX = Math.Max(a.X, b.X);
        var minY = Math.Min(a.Y, b.Y);
        var maxY = Math.Max(a.Y, b.Y);

        var firstX = (int)Math.Floor(minX / _gridSize) * _gridSize;
        var firstY = (int)Math.Floor(minY / _gridSize) * _gridSize;

        using var gridPen = new Pen(Color.FromArgb(48, 51, 57));
        using var majorPen = new Pen(Color.FromArgb(66, 70, 78));
        using var axisPen = new Pen(Color.FromArgb(110, 116, 128), 1.5f);

        var maxLines = 3000;
        var count = 0;
        for (var x = firstX; x <= maxX + _gridSize && count < maxLines; x += _gridSize, count++)
        {
            var sx = WorldToScreen(x, 0).X;
            var pen = x == 0 ? axisPen : (Math.Abs(x / _gridSize) % 8 == 0 ? majorPen : gridPen);
            g.DrawLine(pen, sx, 0, sx, ClientSize.Height);
        }

        count = 0;
        for (var y = firstY; y <= maxY + _gridSize && count < maxLines; y += _gridSize, count++)
        {
            var sy = WorldToScreen(0, y).Y;
            var pen = y == 0 ? axisPen : (Math.Abs(y / _gridSize) % 8 == 0 ? majorPen : gridPen);
            g.DrawLine(pen, 0, sy, ClientSize.Width, sy);
        }
    }

    private void DrawSector(Graphics g, Sector sector)
    {
        if (sector.Vertices.Count < 3) return;

        var points = sector.Vertices.Select(v => WorldToScreen(v.X, v.Y)).ToArray();
        var selected = ReferenceEquals(sector, _selected) ||
                       _selected is VertexSelection vertex && ReferenceEquals(vertex.Sector, sector) ||
                       _selected is EdgeSelection edge && ReferenceEquals(edge.Sector, sector);

        using var fill = new SolidBrush(selected ? Color.FromArgb(78, 52, 122, 172) : Color.FromArgb(48, 92, 112, 128));
        using var outline = new Pen(selected ? Color.DeepSkyBlue : Color.Silver, selected ? 2.5f : 1.4f);
        g.FillPolygon(fill, points);
        g.DrawPolygon(outline, points);

        var center = SectorCenter(sector);
        var screenCenter = WorldToScreen(center.X, center.Y);
        using var textBrush = new SolidBrush(Color.WhiteSmoke);
        g.DrawString(sector.Name, Font, textBrush, screenCenter.X + 5, screenCenter.Y + 5);

        if (_tool is EditorTool.Vertex or EditorTool.Edge || selected)
            DrawSectorHandles(g, sector);
    }

    private void DrawSectorHandles(Graphics g, Sector sector)
    {
        using var edgePen = new Pen(Color.FromArgb(175, 120, 190, 215), _tool == EditorTool.Edge ? 2f : 1f);
        for (var i = 0; i < sector.Vertices.Count; i++)
        {
            var a = WorldToScreen(sector.Vertices[i].X, sector.Vertices[i].Y);
            var b = WorldToScreen(sector.Vertices[(i + 1) % sector.Vertices.Count].X, sector.Vertices[(i + 1) % sector.Vertices.Count].Y);
            var selectedEdge = _selected is EdgeSelection es && ReferenceEquals(es.Sector, sector) && es.StartIndex == i;
            using var selectedPen = selectedEdge ? new Pen(Color.Gold, 4f) : null;
            g.DrawLine(selectedPen ?? edgePen, a, b);
        }

        if (_tool == EditorTool.Vertex || _selected is VertexSelection)
        {
            for (var i = 0; i < sector.Vertices.Count; i++)
            {
                var p = WorldToScreen(sector.Vertices[i].X, sector.Vertices[i].Y);
                var selectedVertex = _selected is VertexSelection vs && ReferenceEquals(vs.Sector, sector) && vs.Index == i;
                var size = selectedVertex ? 10f : 7f;
                using var brush = new SolidBrush(selectedVertex ? Color.Gold : Color.DeepSkyBlue);
                using var pen = new Pen(Color.Black);
                var rect = new RectangleF(p.X - size / 2, p.Y - size / 2, size, size);
                g.FillRectangle(brush, rect);
                g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
            }
        }
    }

    private void DrawDoor(Graphics g, Door door, bool selected)
    {
        var rect = WorldRectToScreen(door.X, door.Y, door.Width, door.Depth);
        using var fill = new SolidBrush(selected ? Color.FromArgb(155, 255, 155, 45) : Color.FromArgb(105, 210, 125, 35));
        using var outline = new Pen(selected ? Color.Gold : Color.Orange, selected ? 3f : 2f);
        g.FillRectangle(fill, rect);
        g.DrawRectangle(outline, rect.X, rect.Y, rect.Width, rect.Height);
        g.DrawLine(outline, rect.Left, rect.Top, rect.Right, rect.Bottom);
        g.DrawLine(outline, rect.Right, rect.Top, rect.Left, rect.Bottom);
        using var textBrush = new SolidBrush(Color.White);
        g.DrawString(door.Name, Font, textBrush, rect.X + 4, rect.Y + 4);
    }

    private void DrawThing(Graphics g, MapThing thing, bool selected)
    {
        var p = WorldToScreen(thing.X, thing.Y);
        var radius = Math.Max(7f, 11f * _zoom);
        var rect = new RectangleF(p.X - radius, p.Y - radius, radius * 2, radius * 2);
        using var fill = new SolidBrush(thing.Type == 1 ? Color.FromArgb(225, 80, 215, 105) : Color.FromArgb(225, 220, 180, 70));
        using var outline = new Pen(selected ? Color.White : Color.Black, selected ? 3f : 1f);
        g.FillEllipse(fill, rect);
        g.DrawEllipse(outline, rect);

        var radians = thing.Angle * MathF.PI / 180f;
        var end = new PointF(p.X + MathF.Cos(radians) * radius * 1.6f, p.Y - MathF.Sin(radians) * radius * 1.6f);
        g.DrawLine(outline, p, end);
    }

    private void DrawDraftArea(Graphics g)
    {
        var x = Math.Min(_areaStart.X, _areaCurrent.X);
        var y = Math.Min(_areaStart.Y, _areaCurrent.Y);
        var w = Math.Abs(_areaCurrent.X - _areaStart.X);
        var d = Math.Abs(_areaCurrent.Y - _areaStart.Y);
        var rect = WorldRectToScreen(x, y, w, d);
        using var pen = new Pen(_drawingTool == EditorTool.Door ? Color.Orange : Color.Gold, 2f) { DashStyle = DashStyle.Dash };
        g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();

        if (e.Button == MouseButtons.Middle)
        {
            _panning = true;
            _panMouseStart = e.Location;
            _panOriginStart = _origin;
            Capture = true;
            return;
        }

        if (e.Button != MouseButtons.Left) return;
        var world = Snap(ScreenToWorld(e.Location));

        switch (_tool)
        {
            case EditorTool.Room:
            case EditorTool.Door:
                _drawingArea = true;
                _drawingTool = _tool;
                _areaStart = world;
                _areaCurrent = world;
                Capture = true;
                break;

            case EditorTool.PlayerStart:
                _project.Things.RemoveAll(t => t.Type == 1);
                var start = new MapThing { Name = "Player 1 Start", Type = 1, X = (int)world.X, Y = (int)world.Y };
                _project.Things.Add(start);
                Select(start);
                ProjectEdited?.Invoke();
                Invalidate();
                break;

            case EditorTool.Vertex:
                var vertexHit = HitVertex(e.Location);
                if (vertexHit is not null)
                {
                    Select(vertexHit);
                    BeginDrag(world, vertexHit);
                }
                else
                {
                    Select(HitSector(e.Location));
                }
                break;

            case EditorTool.Edge:
                var edgeHit = HitEdge(e.Location);
                if (edgeHit is not null)
                {
                    Select(edgeHit);
                    BeginDrag(world, edgeHit);
                }
                else
                {
                    Select(HitSector(e.Location));
                }
                break;

            case EditorTool.Select:
                var hit = HitTest(e.Location);
                Select(hit);
                if (hit is Sector or Door or MapThing)
                    BeginDrag(world, hit);
                break;
        }
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button != MouseButtons.Left || _tool != EditorTool.Vertex) return;

        var edge = HitEdge(e.Location);
        if (edge is null) return;

        var point = Snap(ScreenToWorld(e.Location));
        var a = edge.Sector.Vertices[edge.StartIndex];
        var b = edge.Sector.Vertices[(edge.StartIndex + 1) % edge.Sector.Vertices.Count];
        if ((a.X == (int)point.X && a.Y == (int)point.Y) || (b.X == (int)point.X && b.Y == (int)point.Y)) return;

        var insertAt = edge.StartIndex + 1;
        edge.Sector.Vertices.Insert(insertAt, new MapVertex((int)point.X, (int)point.Y));
        Select(new VertexSelection(edge.Sector, insertAt));
        ProjectEdited?.Invoke();
        ProjectPreviewChanged?.Invoke();
        Invalidate();
    }

    private void BeginDrag(PointF world, object selected)
    {
        _dragging = true;
        _dragStartWorld = world;
        _dragVertices.Clear();
        _dragDoor = null;
        _dragThing = null;

        switch (selected)
        {
            case Sector sector:
                _dragVertices.AddRange(sector.Vertices.Select(v => (v, v.X, v.Y)));
                break;
            case VertexSelection vertex:
                var v = vertex.Sector.Vertices[vertex.Index];
                _dragVertices.Add((v, v.X, v.Y));
                break;
            case EdgeSelection edge:
                var a = edge.Sector.Vertices[edge.StartIndex];
                var b = edge.Sector.Vertices[(edge.StartIndex + 1) % edge.Sector.Vertices.Count];
                _dragVertices.Add((a, a.X, a.Y));
                _dragVertices.Add((b, b.X, b.Y));
                break;
            case Door door:
                _dragDoor = door;
                _dragDoorX = door.X;
                _dragDoorY = door.Y;
                break;
            case MapThing thing:
                _dragThing = thing;
                _dragThingX = thing.X;
                _dragThingY = thing.Y;
                break;
        }

        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_panning)
        {
            _origin = new PointF(
                _panOriginStart.X + e.X - _panMouseStart.X,
                _panOriginStart.Y + e.Y - _panMouseStart.Y);
            Invalidate();
            return;
        }

        var world = Snap(ScreenToWorld(e.Location));
        if (_drawingArea)
        {
            _areaCurrent = world;
            Invalidate();
            return;
        }

        if (!_dragging) return;

        var dx = (int)(world.X - _dragStartWorld.X);
        var dy = (int)(world.Y - _dragStartWorld.Y);
        foreach (var item in _dragVertices)
        {
            item.Vertex.X = item.X + dx;
            item.Vertex.Y = item.Y + dy;
        }

        if (_dragDoor is not null)
        {
            _dragDoor.X = _dragDoorX + dx;
            _dragDoor.Y = _dragDoorY + dy;
        }

        if (_dragThing is not null)
        {
            _dragThing.X = _dragThingX + dx;
            _dragThing.Y = _dragThingY + dy;
        }

        SelectionChanged?.Invoke(_selected);
        ProjectPreviewChanged?.Invoke();
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button == MouseButtons.Middle)
        {
            _panning = false;
            Capture = false;
            return;
        }

        if (e.Button != MouseButtons.Left) return;

        if (_drawingArea)
        {
            _drawingArea = false;
            Capture = false;

            var x = (int)Math.Min(_areaStart.X, _areaCurrent.X);
            var y = (int)Math.Min(_areaStart.Y, _areaCurrent.Y);
            var width = (int)Math.Abs(_areaCurrent.X - _areaStart.X);
            var depth = (int)Math.Abs(_areaCurrent.Y - _areaStart.Y);

            if (width >= _gridSize && depth >= _gridSize)
            {
                if (_drawingTool == EditorTool.Room)
                {
                    var sector = Sector.Rectangle($"Sector {_project.Sectors.Count + 1}", x, y, width, depth);
                    _project.Sectors.Add(sector);
                    Select(sector);
                }
                else
                {
                    var nextTag = _project.Doors.Count == 0 ? 100 : _project.Doors.Max(d => d.Tag) + 1;
                    var door = new Door
                    {
                        Name = $"Door {_project.Doors.Count + 1}",
                        X = x,
                        Y = y,
                        Width = width,
                        Depth = depth,
                        Tag = nextTag
                    };
                    _project.Doors.Add(door);
                    Select(door);
                }

                ProjectEdited?.Invoke();
                ProjectPreviewChanged?.Invoke();
            }

            Invalidate();
        }

        if (_dragging)
        {
            _dragging = false;
            Capture = false;
            ProjectEdited?.Invoke();
            ProjectPreviewChanged?.Invoke();
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        var before = ScreenToWorld(e.Location);
        var factor = e.Delta > 0 ? 1.15f : 1f / 1.15f;
        _zoom = Math.Clamp(_zoom * factor, 0.05f, 12f);
        var afterScreen = WorldToScreen(before.X, before.Y);
        _origin = new PointF(_origin.X + e.X - afterScreen.X, _origin.Y + e.Y - afterScreen.Y);
        Invalidate();
    }

    protected override bool IsInputKey(Keys keyData)
    {
        if (keyData == Keys.Delete) return true;
        return base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode != Keys.Delete || _selected is null) return;

        var changed = false;
        switch (_selected)
        {
            case Sector sector:
                changed = _project.Sectors.Remove(sector);
                break;
            case Door door:
                changed = _project.Doors.Remove(door);
                break;
            case MapThing thing:
                changed = _project.Things.Remove(thing);
                break;
            case VertexSelection vertex when vertex.Sector.Vertices.Count > 3:
                vertex.Sector.Vertices.RemoveAt(vertex.Index);
                changed = true;
                break;
        }

        if (changed)
        {
            Select(null);
            ProjectEdited?.Invoke();
            ProjectPreviewChanged?.Invoke();
            Invalidate();
        }

        e.Handled = true;
    }

    private object? HitTest(Point screenPoint)
    {
        for (var i = _project.Things.Count - 1; i >= 0; i--)
        {
            var thing = _project.Things[i];
            var p = WorldToScreen(thing.X, thing.Y);
            var dx = p.X - screenPoint.X;
            var dy = p.Y - screenPoint.Y;
            if (dx * dx + dy * dy <= 18f * 18f)
                return thing;
        }

        var world = ScreenToWorld(screenPoint);
        for (var i = _project.Doors.Count - 1; i >= 0; i--)
        {
            var door = _project.Doors[i];
            if (world.X >= door.X && world.X <= door.X + door.Width &&
                world.Y >= door.Y && world.Y <= door.Y + door.Depth)
                return door;
        }

        return HitSector(screenPoint);
    }

    private Sector? HitSector(Point screenPoint)
    {
        var world = ScreenToWorld(screenPoint);
        for (var i = _project.Sectors.Count - 1; i >= 0; i--)
        {
            var sector = _project.Sectors[i];
            if (GeometryUtil.PointInPolygon(world.X, world.Y, sector.Vertices))
                return sector;
        }
        return null;
    }

    private VertexSelection? HitVertex(Point screenPoint)
    {
        const float threshold = 12f;
        for (var s = _project.Sectors.Count - 1; s >= 0; s--)
        {
            var sector = _project.Sectors[s];
            for (var i = 0; i < sector.Vertices.Count; i++)
            {
                var p = WorldToScreen(sector.Vertices[i].X, sector.Vertices[i].Y);
                var dx = p.X - screenPoint.X;
                var dy = p.Y - screenPoint.Y;
                if (dx * dx + dy * dy <= threshold * threshold)
                    return new VertexSelection(sector, i);
            }
        }
        return null;
    }

    private EdgeSelection? HitEdge(Point screenPoint)
    {
        EdgeSelection? best = null;
        var bestDistance = 9.0;

        for (var s = _project.Sectors.Count - 1; s >= 0; s--)
        {
            var sector = _project.Sectors[s];
            for (var i = 0; i < sector.Vertices.Count; i++)
            {
                var a = WorldToScreen(sector.Vertices[i].X, sector.Vertices[i].Y);
                var b = WorldToScreen(sector.Vertices[(i + 1) % sector.Vertices.Count].X, sector.Vertices[(i + 1) % sector.Vertices.Count].Y);
                var distance = DistancePointToScreenSegment(screenPoint, a, b);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = new EdgeSelection(sector, i);
                }
            }
        }

        return best;
    }

    private static double DistancePointToScreenSegment(Point p, PointF a, PointF b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var len2 = dx * dx + dy * dy;
        if (len2 < 0.001) return double.MaxValue;
        var t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0f, 1f);
        var x = a.X + t * dx;
        var y = a.Y + t * dy;
        return Math.Sqrt((p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y));
    }

    private void Select(object? value)
    {
        _selected = value;
        SelectionChanged?.Invoke(value);
        Invalidate();
    }

    private PointF Snap(PointF point) => new(SnapValue(point.X), SnapValue(point.Y));
    private int SnapValue(float value) => (int)Math.Round(value / _gridSize) * _gridSize;

    private PointF WorldToScreen(float x, float y) => new(_origin.X + x * _zoom, _origin.Y - y * _zoom);

    private PointF ScreenToWorld(Point point) => new(
        (point.X - _origin.X) / _zoom,
        -((point.Y - _origin.Y) / _zoom));

    private RectangleF WorldRectToScreen(float x, float y, float width, float depth)
    {
        var a = WorldToScreen(x, y);
        var b = WorldToScreen(x + width, y + depth);
        return RectangleF.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
    }

    private static PointF SectorCenter(Sector sector)
    {
        if (sector.Vertices.Count == 0) return PointF.Empty;
        return new PointF(
            (float)sector.Vertices.Average(v => v.X),
            (float)sector.Vertices.Average(v => v.Y));
    }
}
