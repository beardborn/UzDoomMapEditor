using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text.Json;
using UzDoomMapEditor.Core;

namespace UzDoomMapEditor.Editor;

internal enum EditorTool
{
    Select,
    Room,
    Door,
    PlayerStart
}

public sealed class MainForm : Form
{
    private readonly MapCanvas _canvas = new();
    private readonly PropertyGrid _properties = new() { Dock = DockStyle.Fill, HelpVisible = true, ToolbarVisible = false };
    private readonly ToolStripStatusLabel _status = new("Ready");

    private EditorProject _project = new();
    private string? _projectPath;
    private string? _uzDoomPath;
    private string? _iwadPath;

    public MainForm()
    {
        Text = "UZDoom Map Editor";
        Width = 1400;
        Height = 900;
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        var menu = BuildMenu();
        var toolbar = BuildToolbar();
        var statusStrip = new StatusStrip();
        statusStrip.Items.Add(_status);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 1050,
            FixedPanel = FixedPanel.Panel2
        };
        split.Panel1.Controls.Add(_canvas);
        split.Panel2.Controls.Add(_properties);

        Controls.Add(split);
        Controls.Add(statusStrip);
        Controls.Add(toolbar);
        Controls.Add(menu);
        MainMenuStrip = menu;

        _canvas.Dock = DockStyle.Fill;
        _canvas.SelectionChanged += selected => _properties.SelectedObject = selected;
        _canvas.ProjectChanged += UpdateTitle;
        _properties.PropertyValueChanged += (_, _) =>
        {
            _canvas.Invalidate();
            UpdateTitle();
        };

        SetProject(new EditorProject());
        SetTool(EditorTool.Select);
    }

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip();

        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.Add(new ToolStripMenuItem("&New", null, (_, _) => NewProject(), Keys.Control | Keys.N));
        file.DropDownItems.Add(new ToolStripMenuItem("&Open...", null, (_, _) => OpenProject(), Keys.Control | Keys.O));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(new ToolStripMenuItem("&Save", null, (_, _) => SaveProject(), Keys.Control | Keys.S));
        file.DropDownItems.Add(new ToolStripMenuItem("Save &As...", null, (_, _) => SaveProjectAs()));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(new ToolStripMenuItem("Export &UDMF Text...", null, (_, _) => ExportUdmf()));
        file.DropDownItems.Add(new ToolStripMenuItem("Export Test &WAD...", null, (_, _) => ExportWad()));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(new ToolStripMenuItem("E&xit", null, (_, _) => Close()));

        var view = new ToolStripMenuItem("&View");
        view.DropDownItems.Add(new ToolStripMenuItem("Reset View", null, (_, _) => _canvas.ResetView(), Keys.Home));

        var build = new ToolStripMenuItem("&Build");
        build.DropDownItems.Add(new ToolStripMenuItem("&Test Map in UZDoom", null, (_, _) => TestMap(), Keys.F5));

        menu.Items.Add(file);
        menu.Items.Add(view);
        menu.Items.Add(build);
        return menu;
    }

    private ToolStrip BuildToolbar()
    {
        var toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };

        var select = new ToolStripButton("Select") { CheckOnClick = true, Tag = EditorTool.Select };
        var room = new ToolStripButton("Room") { CheckOnClick = true, Tag = EditorTool.Room };
        var door = new ToolStripButton("Door") { CheckOnClick = true, Tag = EditorTool.Door };
        var player = new ToolStripButton("Player Start") { CheckOnClick = true, Tag = EditorTool.PlayerStart };
        var test = new ToolStripButton("▶ Test (F5)");

        select.Click += ToolButtonClicked;
        room.Click += ToolButtonClicked;
        door.Click += ToolButtonClicked;
        player.Click += ToolButtonClicked;
        test.Click += (_, _) => TestMap();

        toolbar.Items.Add(select);
        toolbar.Items.Add(room);
        toolbar.Items.Add(door);
        toolbar.Items.Add(player);
        toolbar.Items.Add(new ToolStripSeparator());
        toolbar.Items.Add(test);

        return toolbar;
    }

    private void ToolButtonClicked(object? sender, EventArgs e)
    {
        if (sender is ToolStripButton button && button.Tag is EditorTool tool)
            SetTool(tool);
    }

    private void SetTool(EditorTool tool)
    {
        _canvas.Tool = tool;
        _status.Text = tool switch
        {
            EditorTool.Select => "Select: click an object; drag to move. Middle mouse pans. Wheel zooms.",
            EditorTool.Room => "Room: drag a rectangle on the grid.",
            EditorTool.Door => "Door: drag a rectangular connector across the gap between two room edges.",
            EditorTool.PlayerStart => "Player Start: click to place the player start.",
            _ => "Ready"
        };

        foreach (Control control in Controls)
        {
            if (control is not ToolStrip strip || strip is MenuStrip || strip is StatusStrip) continue;
            foreach (ToolStripItem item in strip.Items)
            {
                if (item is ToolStripButton button && button.Tag is EditorTool buttonTool)
                    button.Checked = buttonTool == tool;
            }
        }

        _canvas.Focus();
    }

    private void NewProject()
    {
        SetProject(new EditorProject());
        _projectPath = null;
        UpdateTitle();
    }

    private void SetProject(EditorProject project)
    {
        project.Rooms ??= new List<Room>();
        project.Doors ??= new List<Door>();
        project.Things ??= new List<MapThing>();
        _project = project;
        _canvas.Project = _project;
        _properties.SelectedObject = null;
        _canvas.ResetView();
        UpdateTitle();
    }

    private void OpenProject()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "UZDoom Map Editor Project (*.uzmap)|*.uzmap|JSON (*.json)|*.json|All files (*.*)|*.*",
            Title = "Open project"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var json = File.ReadAllText(dialog.FileName);
            var project = JsonSerializer.Deserialize<EditorProject>(json) ?? throw new InvalidDataException("Project file was empty.");
            _projectPath = dialog.FileName;
            SetProject(project);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not open project", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveProject()
    {
        if (string.IsNullOrWhiteSpace(_projectPath))
        {
            SaveProjectAs();
            return;
        }

        WriteProject(_projectPath);
    }

    private void SaveProjectAs()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "UZDoom Map Editor Project (*.uzmap)|*.uzmap|JSON (*.json)|*.json",
            DefaultExt = "uzmap",
            AddExtension = true,
            FileName = string.IsNullOrWhiteSpace(_project.Name) ? "Untitled.uzmap" : $"{_project.Name}.uzmap"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _projectPath = dialog.FileName;
        if (_project.Name == "Untitled") _project.Name = Path.GetFileNameWithoutExtension(dialog.FileName);
        WriteProject(dialog.FileName);
    }

    private void WriteProject(string path)
    {
        var json = JsonSerializer.Serialize(_project, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
        _status.Text = $"Saved {Path.GetFileName(path)}";
        UpdateTitle();
    }

    private void ExportUdmf()
    {
        if (!TryBuildUdmf(out var text)) return;

        using var dialog = new SaveFileDialog
        {
            Filter = "UDMF TEXTMAP (*.txt)|*.txt|All files (*.*)|*.*",
            FileName = $"{_project.MapName}_TEXTMAP.txt"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        File.WriteAllText(dialog.FileName, text);
        _status.Text = $"Exported UDMF: {dialog.FileName}";
    }

    private void ExportWad()
    {
        if (!ValidateForTest()) return;
        if (!TryBuildUdmf(out var text)) return;

        using var dialog = new SaveFileDialog
        {
            Filter = "Doom WAD (*.wad)|*.wad",
            FileName = $"{_project.MapName}.wad"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        WadWriter.WritePwad(dialog.FileName, _project.MapName, text);
        _status.Text = $"Exported WAD: {dialog.FileName}";
    }

    private void TestMap()
    {
        if (!ValidateForTest()) return;
        if (!TryBuildUdmf(out var text)) return;
        if (!ChooseUzDoom()) return;
        if (!ChooseIwad()) return;

        try
        {
            var testDir = Path.Combine(Path.GetTempPath(), "UzDoomMapEditor");
            Directory.CreateDirectory(testDir);
            var wadPath = Path.Combine(testDir, "editor-test.wad");
            WadWriter.WritePwad(wadPath, _project.MapName, text);

            var startInfo = new ProcessStartInfo
            {
                FileName = _uzDoomPath!,
                WorkingDirectory = Path.GetDirectoryName(_uzDoomPath!)!,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("-iwad");
            startInfo.ArgumentList.Add(_iwadPath!);
            startInfo.ArgumentList.Add("-file");
            startInfo.ArgumentList.Add(wadPath);
            startInfo.ArgumentList.Add("+map");
            startInfo.ArgumentList.Add(_project.MapName);

            Process.Start(startInfo);
            _status.Text = $"Testing {_project.MapName} in UZDoom";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not launch UZDoom", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private bool ValidateForTest()
    {
        if (_project.Rooms.Count == 0)
        {
            MessageBox.Show(this, "Draw at least one room first.", "Nothing to test", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        if (_project.Things.All(t => t.Type != 1))
        {
            MessageBox.Show(this, "Place a Player Start first.", "Player start required", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        foreach (var door in _project.Doors)
        {
            if (CountTouchingRooms(door) < 2)
            {
                MessageBox.Show(
                    this,
                    $"{door.Name} is not connected to two room edges yet.\n\nFor now, leave a gap between two rooms and drag the Door rectangle so it touches both rooms.",
                    "Door is not connected",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return false;
            }
        }

        return true;
    }

    private int CountTouchingRooms(Door door)
    {
        var count = 0;
        var doorX2 = door.X + door.Width;
        var doorY2 = door.Y + door.Depth;

        foreach (var room in _project.Rooms)
        {
            var roomX2 = room.X + room.Width;
            var roomY2 = room.Y + room.Depth;
            var overlapX = Math.Min(roomX2, doorX2) - Math.Max(room.X, door.X);
            var overlapY = Math.Min(roomY2, doorY2) - Math.Max(room.Y, door.Y);

            var sharesVerticalEdge = overlapY > 0 && (roomX2 == door.X || room.X == doorX2);
            var sharesHorizontalEdge = overlapX > 0 && (roomY2 == door.Y || room.Y == doorY2);
            if (sharesVerticalEdge || sharesHorizontalEdge) count++;
        }

        return count;
    }

    private bool TryBuildUdmf(out string text)
    {
        try
        {
            text = UdmfExporter.BuildText(_project);
            return true;
        }
        catch (Exception ex)
        {
            text = string.Empty;
            MessageBox.Show(this, ex.Message, "Map geometry problem", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    private bool ChooseUzDoom()
    {
        if (!string.IsNullOrWhiteSpace(_uzDoomPath) && File.Exists(_uzDoomPath)) return true;

        using var dialog = new OpenFileDialog
        {
            Filter = "UZDoom executable (uzdoom.exe)|uzdoom.exe|Executables (*.exe)|*.exe",
            Title = "Locate uzdoom.exe"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return false;
        _uzDoomPath = dialog.FileName;
        return true;
    }

    private bool ChooseIwad()
    {
        if (!string.IsNullOrWhiteSpace(_iwadPath) && File.Exists(_iwadPath)) return true;

        using var dialog = new OpenFileDialog
        {
            Filter = "IWAD (*.wad)|*.wad|All files (*.*)|*.*",
            Title = "Choose a Doom-compatible IWAD (Freedoom is fine for testing)"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return false;
        _iwadPath = dialog.FileName;
        return true;
    }

    private void UpdateTitle()
    {
        var name = string.IsNullOrWhiteSpace(_project.Name) ? "Untitled" : _project.Name;
        Text = $"UZDoom Map Editor - {name}";
    }
}

public sealed class MapCanvas : Control
{
    public const int GridSize = 64;

    private EditorProject _project = new();
    private EditorTool _tool;
    private object? _selected;

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
    private int _dragStartX;
    private int _dragStartY;

    public event Action<object?>? SelectionChanged;
    public event Action? ProjectChanged;

    public EditorProject Project
    {
        get => _project;
        set
        {
            _project = value ?? new EditorProject();
            _project.Rooms ??= new List<Room>();
            _project.Doors ??= new List<Door>();
            _project.Things ??= new List<MapThing>();
            _selected = null;
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

    public MapCanvas()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(34, 36, 40);
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
        e.Graphics.SmoothingMode = SmoothingMode.None;
        DrawGrid(e.Graphics);

        foreach (var room in _project.Rooms)
            DrawRoom(e.Graphics, room, ReferenceEquals(room, _selected));

        foreach (var door in _project.Doors)
            DrawDoor(e.Graphics, door, ReferenceEquals(door, _selected));

        foreach (var thing in _project.Things)
            DrawThing(e.Graphics, thing, ReferenceEquals(thing, _selected));

        if (_drawingArea)
            DrawDraftArea(e.Graphics);
    }

    private void DrawGrid(Graphics g)
    {
        var a = ScreenToWorld(new Point(0, 0));
        var b = ScreenToWorld(new Point(ClientSize.Width, ClientSize.Height));
        var minX = Math.Min(a.X, b.X);
        var maxX = Math.Max(a.X, b.X);
        var minY = Math.Min(a.Y, b.Y);
        var maxY = Math.Max(a.Y, b.Y);

        var firstX = (int)Math.Floor(minX / GridSize) * GridSize;
        var firstY = (int)Math.Floor(minY / GridSize) * GridSize;

        using var gridPen = new Pen(Color.FromArgb(55, 58, 64));
        using var majorPen = new Pen(Color.FromArgb(72, 76, 84));
        using var axisPen = new Pen(Color.FromArgb(115, 120, 132));

        for (var x = firstX; x <= maxX + GridSize; x += GridSize)
        {
            var sx = WorldToScreen(x, 0).X;
            var pen = x == 0 ? axisPen : (Math.Abs(x / GridSize) % 4 == 0 ? majorPen : gridPen);
            g.DrawLine(pen, sx, 0, sx, ClientSize.Height);
        }

        for (var y = firstY; y <= maxY + GridSize; y += GridSize)
        {
            var sy = WorldToScreen(0, y).Y;
            var pen = y == 0 ? axisPen : (Math.Abs(y / GridSize) % 4 == 0 ? majorPen : gridPen);
            g.DrawLine(pen, 0, sy, ClientSize.Width, sy);
        }
    }

    private void DrawRoom(Graphics g, Room room, bool selected)
    {
        var rect = WorldRectToScreen(room.X, room.Y, room.Width, room.Depth);
        using var fill = new SolidBrush(selected ? Color.FromArgb(80, 80, 160, 230) : Color.FromArgb(55, 120, 145, 165));
        using var outline = new Pen(selected ? Color.DeepSkyBlue : Color.Silver, selected ? 3f : 1.5f);
        g.FillRectangle(fill, rect);
        g.DrawRectangle(outline, rect.X, rect.Y, rect.Width, rect.Height);

        using var textBrush = new SolidBrush(Color.WhiteSmoke);
        g.DrawString(room.Name, Font, textBrush, rect.X + 5, rect.Y + 5);
    }

    private void DrawDoor(Graphics g, Door door, bool selected)
    {
        var rect = WorldRectToScreen(door.X, door.Y, door.Width, door.Depth);
        using var fill = new SolidBrush(selected ? Color.FromArgb(150, 255, 155, 45) : Color.FromArgb(105, 210, 125, 35));
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
        var radius = Math.Max(7f, 12f * _zoom);
        var rect = new RectangleF(p.X - radius, p.Y - radius, radius * 2, radius * 2);
        using var fill = new SolidBrush(thing.Type == 1 ? Color.FromArgb(220, 90, 210, 110) : Color.FromArgb(220, 220, 180, 70));
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
                ProjectChanged?.Invoke();
                Invalidate();
                break;

            case EditorTool.Select:
                var hit = HitTest(e.Location);
                Select(hit);
                if (hit is Room room)
                    BeginDrag(world, room.X, room.Y);
                else if (hit is Door door)
                    BeginDrag(world, door.X, door.Y);
                else if (hit is MapThing thing)
                    BeginDrag(world, thing.X, thing.Y);
                break;
        }
    }

    private void BeginDrag(PointF world, int x, int y)
    {
        _dragging = true;
        _dragStartWorld = world;
        _dragStartX = x;
        _dragStartY = y;
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

        if (_dragging && _selected is not null)
        {
            var dx = (int)(world.X - _dragStartWorld.X);
            var dy = (int)(world.Y - _dragStartWorld.Y);

            switch (_selected)
            {
                case Room room:
                    room.X = _dragStartX + dx;
                    room.Y = _dragStartY + dy;
                    break;
                case Door door:
                    door.X = _dragStartX + dx;
                    door.Y = _dragStartY + dy;
                    break;
                case MapThing thing:
                    thing.X = _dragStartX + dx;
                    thing.Y = _dragStartY + dy;
                    break;
            }

            ProjectChanged?.Invoke();
            Invalidate();
        }
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

            if (width >= GridSize && depth >= GridSize)
            {
                if (_drawingTool == EditorTool.Room)
                {
                    var room = new Room
                    {
                        Name = $"Room {_project.Rooms.Count + 1}",
                        X = x,
                        Y = y,
                        Width = width,
                        Depth = depth
                    };
                    _project.Rooms.Add(room);
                    Select(room);
                }
                else if (_drawingTool == EditorTool.Door)
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

                ProjectChanged?.Invoke();
            }

            Invalidate();
        }

        if (_dragging)
        {
            _dragging = false;
            Capture = false;
            ProjectChanged?.Invoke();
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);

        var before = ScreenToWorld(e.Location);
        var factor = e.Delta > 0 ? 1.15f : 1f / 1.15f;
        _zoom = Math.Clamp(_zoom * factor, 0.25f, 4f);
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

        if (e.KeyCode == Keys.Delete && _selected is not null)
        {
            if (_selected is Room room) _project.Rooms.Remove(room);
            if (_selected is Door door) _project.Doors.Remove(door);
            if (_selected is MapThing thing) _project.Things.Remove(thing);
            Select(null);
            ProjectChanged?.Invoke();
            Invalidate();
            e.Handled = true;
        }
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

        for (var i = _project.Rooms.Count - 1; i >= 0; i--)
        {
            var room = _project.Rooms[i];
            if (world.X >= room.X && world.X <= room.X + room.Width &&
                world.Y >= room.Y && world.Y <= room.Y + room.Depth)
                return room;
        }

        return null;
    }

    private void Select(object? value)
    {
        _selected = value;
        SelectionChanged?.Invoke(value);
        Invalidate();
    }

    private PointF Snap(PointF point) => new(SnapValue(point.X), SnapValue(point.Y));
    private static int SnapValue(float value) => (int)Math.Round(value / GridSize) * GridSize;

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
}
