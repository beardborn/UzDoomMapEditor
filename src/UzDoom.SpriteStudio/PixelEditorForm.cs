using System.Drawing.Drawing2D;
using UzDoom.Core;

namespace UzDoom.SpriteStudio;

internal enum PixelTool
{
    Pencil,
    Eraser,
    Fill,
    Picker
}

internal sealed class PixelEditorForm : Form
{
    private readonly PixelCanvas _canvas;
    private readonly PaletteGrid _paletteGrid;
    private readonly ToolStripStatusLabel _status = new();
    private readonly ToolStripButton _pencilButton = new("Pencil") { CheckOnClick = true };
    private readonly ToolStripButton _eraserButton = new("Eraser") { CheckOnClick = true };
    private readonly ToolStripButton _fillButton = new("Fill") { CheckOnClick = true };
    private readonly ToolStripButton _pickerButton = new("Picker") { CheckOnClick = true };

    public PixelEditorForm(DoomPatchImage image, DoomPalette palette, string spriteName)
    {
        Text = $"Pixel Editor - {spriteName}";
        StartPosition = FormStartPosition.CenterParent;
        Width = 1050;
        Height = 760;
        MinimumSize = new Size(760, 520);
        BackColor = Color.FromArgb(28, 30, 34);
        ForeColor = Color.Gainsboro;

        _canvas = new PixelCanvas(image, palette)
        {
            Dock = DockStyle.Fill
        };
        _canvas.PixelHovered += (_, info) =>
            _status.Text = info is null ? "" : $"X {info.Value.X}, Y {info.Value.Y}, palette {info.Value.PaletteIndex}";
        _canvas.ColorPicked += (_, index) => _paletteGrid.SelectedIndex = index;

        _paletteGrid = new PaletteGrid(palette)
        {
            Dock = DockStyle.Top,
            Height = 306
        };
        _paletteGrid.SelectedIndexChanged += (_, index) => _canvas.SelectedPaletteIndex = index;

        var toolbar = BuildToolbar();
        var right = BuildRightPanel();
        var status = new StatusStrip
        {
            BackColor = Color.FromArgb(36, 38, 43),
            ForeColor = Color.Gainsboro,
            SizingGrip = false
        };
        status.Items.Add(_status);
        _status.Text = "Pencil selected. Left-click to draw.";

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            Size = new Size(1000, 650),
            SplitterDistance = 700,
            BackColor = Color.FromArgb(20, 22, 25)
        };
        split.Panel1MinSize = 420;
        split.Panel2MinSize = 250;
        split.Panel1.Controls.Add(_canvas);
        split.Panel2.Controls.Add(right);

        Controls.Add(split);
        Controls.Add(status);
        Controls.Add(toolbar);

        SetTool(PixelTool.Pencil);
        _paletteGrid.SelectedIndex = 0;
    }

    public DoomPatchImage? EditedImage { get; private set; }

    private ToolStrip BuildToolbar()
    {
        var bar = new ToolStrip
        {
            Dock = DockStyle.Top,
            GripStyle = ToolStripGripStyle.Hidden,
            BackColor = Color.FromArgb(43, 45, 50),
            ForeColor = Color.White,
            Padding = new Padding(6, 3, 6, 3)
        };

        _pencilButton.Click += (_, _) => SetTool(PixelTool.Pencil);
        _eraserButton.Click += (_, _) => SetTool(PixelTool.Eraser);
        _fillButton.Click += (_, _) => SetTool(PixelTool.Fill);
        _pickerButton.Click += (_, _) => SetTool(PixelTool.Picker);

        bar.Items.AddRange(new ToolStripItem[]
        {
            _pencilButton, _eraserButton, _fillButton, _pickerButton,
            new ToolStripSeparator(),
            new ToolStripButton("Undo", null, (_, _) => _canvas.Undo()),
            new ToolStripButton("Redo", null, (_, _) => _canvas.Redo()),
            new ToolStripSeparator()
        });

        var zoomLabel = new ToolStripLabel("Zoom");
        var zoom = new ToolStripComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70 };
        zoom.Items.AddRange(new object[] { "2x", "4x", "8x", "12x", "16x", "24x", "32x" });
        zoom.SelectedItem = "8x";
        zoom.SelectedIndexChanged += (_, _) =>
        {
            if (zoom.SelectedItem is string text && int.TryParse(text.TrimEnd('x'), out var value))
                _canvas.Zoom = value;
        };
        bar.Items.Add(zoomLabel);
        bar.Items.Add(zoom);
        return bar;
    }

    private Control BuildRightPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            BackColor = Color.FromArgb(35, 37, 42)
        };

        var title = new Label
        {
            Text = "WAD PALETTE",
            Dock = DockStyle.Top,
            Height = 28,
            Font = new Font(Font.FontFamily, 10f, FontStyle.Bold),
            ForeColor = Color.White
        };

        var info = new Label
        {
            Dock = DockStyle.Top,
            Height = 70,
            Text = "Pencil uses the selected PLAYPAL colour.\r\nEraser makes pixels transparent.\r\nFill replaces a connected area.",
            ForeColor = Color.Silver
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(4)
        };

        var apply = new Button
        {
            Text = "Apply Changes",
            Width = 120,
            Height = 32,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(66, 83, 116),
            ForeColor = Color.White
        };
        apply.Click += (_, _) =>
        {
            EditedImage = _canvas.BuildImage();
            DialogResult = DialogResult.OK;
            Close();
        };

        var cancel = new Button
        {
            Text = "Cancel",
            Width = 90,
            Height = 32,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(60, 62, 68),
            ForeColor = Color.White
        };
        cancel.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        buttons.Controls.Add(apply);
        buttons.Controls.Add(cancel);

        panel.Controls.Add(buttons);
        panel.Controls.Add(info);
        panel.Controls.Add(_paletteGrid);
        panel.Controls.Add(title);
        return panel;
    }

    private void SetTool(PixelTool tool)
    {
        _canvas.Tool = tool;
        _pencilButton.Checked = tool == PixelTool.Pencil;
        _eraserButton.Checked = tool == PixelTool.Eraser;
        _fillButton.Checked = tool == PixelTool.Fill;
        _pickerButton.Checked = tool == PixelTool.Picker;
        _status.Text = tool switch
        {
            PixelTool.Pencil => "Pencil selected. Left-click or drag to draw.",
            PixelTool.Eraser => "Eraser selected. Left-click or drag to make pixels transparent.",
            PixelTool.Fill => "Fill selected. Click a connected area to recolour it.",
            PixelTool.Picker => "Picker selected. Click a pixel to choose its palette colour.",
            _ => string.Empty
        };
    }
}

internal sealed class PaletteGrid : Control
{
    private readonly DoomPalette _palette;
    private int _selectedIndex;

    public PaletteGrid(DoomPalette palette)
    {
        _palette = palette;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(30, 32, 36);
        Cursor = Cursors.Hand;
    }

    public event EventHandler<int>? SelectedIndexChanged;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            var clamped = Math.Clamp(value, 0, 255);
            if (_selectedIndex == clamped)
                return;
            _selectedIndex = clamped;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, clamped);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        const int cell = 17;
        var x = e.X / cell;
        var y = e.Y / cell;
        if (x is < 0 or >= 16 || y is < 0 or >= 16)
            return;
        SelectedIndex = y * 16 + x;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        const int cell = 17;
        for (var i = 0; i < 256; i++)
        {
            var x = (i % 16) * cell;
            var y = (i / 16) * cell;
            var c = _palette.Colors[i];
            using var brush = new SolidBrush(Color.FromArgb(c.R, c.G, c.B));
            e.Graphics.FillRectangle(brush, x, y, cell, cell);
            if (i == _selectedIndex)
            {
                using var outer = new Pen(Color.White, 2f);
                using var inner = new Pen(Color.Black, 1f);
                e.Graphics.DrawRectangle(outer, x + 1, y + 1, cell - 3, cell - 3);
                e.Graphics.DrawRectangle(inner, x + 3, y + 3, cell - 7, cell - 7);
            }
        }
    }
}

internal readonly record struct PixelHoverInfo(int X, int Y, int PaletteIndex);

internal sealed class PixelCanvas : ScrollableControl
{
    private readonly DoomPalette _palette;
    private readonly int _width;
    private readonly int _height;
    private readonly int _leftOffset;
    private readonly int _topOffset;
    private byte[] _indices;
    private bool[] _opaque;
    private readonly Stack<PixelState> _undo = new();
    private readonly Stack<PixelState> _redo = new();
    private bool _drawing;
    private bool _transactionStarted;
    private int _zoom = 8;

    public PixelCanvas(DoomPatchImage image, DoomPalette palette)
    {
        _palette = palette;
        _width = image.Width;
        _height = image.Height;
        _leftOffset = image.LeftOffset;
        _topOffset = image.TopOffset;
        _indices = image.PaletteIndices.ToArray();
        _opaque = image.OpaqueMask.ToArray();
        AutoScroll = true;
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Color.FromArgb(23, 25, 29);
        UpdateScrollSize();
    }

    public event EventHandler<PixelHoverInfo?>? PixelHovered;
    public event EventHandler<int>? ColorPicked;

    public PixelTool Tool { get; set; }
    public int SelectedPaletteIndex { get; set; }

    public int Zoom
    {
        get => _zoom;
        set
        {
            _zoom = Math.Clamp(value, 1, 64);
            UpdateScrollSize();
            Invalidate();
        }
    }

    public DoomPatchImage BuildImage()
        => new(_width, _height, _leftOffset, _topOffset, _indices.ToArray(), _opaque.ToArray());

    public void Undo()
    {
        if (_undo.Count == 0)
            return;
        _redo.Push(Capture());
        Restore(_undo.Pop());
    }

    public void Redo()
    {
        if (_redo.Count == 0)
            return;
        _undo.Push(Capture());
        Restore(_redo.Pop());
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || !TryGetPixel(e.Location, out var x, out var y))
            return;

        if (Tool == PixelTool.Picker)
        {
            var index = y * _width + x;
            if (_opaque[index])
                ColorPicked?.Invoke(this, _indices[index]);
            return;
        }

        BeginTransaction();
        _drawing = true;
        ApplyTool(x, y);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (TryGetPixel(e.Location, out var x, out var y))
        {
            var index = y * _width + x;
            PixelHovered?.Invoke(this, new PixelHoverInfo(x, y, _opaque[index] ? _indices[index] : -1));
            if (_drawing && e.Button == MouseButtons.Left && Tool is PixelTool.Pencil or PixelTool.Eraser)
                ApplyTool(x, y);
        }
        else
        {
            PixelHovered?.Invoke(this, null);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _drawing = false;
        _transactionStarted = false;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        PixelHovered?.Invoke(this, null);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.None;
        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;

        var offset = AutoScrollPosition;
        var left = 20 + offset.X;
        var top = 20 + offset.Y;

        DrawCheckerboard(e.Graphics, left, top);

        for (var y = 0; y < _height; y++)
        {
            for (var x = 0; x < _width; x++)
            {
                var index = y * _width + x;
                if (!_opaque[index])
                    continue;
                var c = _palette.Colors[_indices[index]];
                using var brush = new SolidBrush(Color.FromArgb(c.R, c.G, c.B));
                e.Graphics.FillRectangle(brush, left + x * _zoom, top + y * _zoom, _zoom, _zoom);
            }
        }

        if (_zoom >= 8)
        {
            using var gridPen = new Pen(Color.FromArgb(45, 0, 0, 0), 1f);
            for (var x = 0; x <= _width; x++)
                e.Graphics.DrawLine(gridPen, left + x * _zoom, top, left + x * _zoom, top + _height * _zoom);
            for (var y = 0; y <= _height; y++)
                e.Graphics.DrawLine(gridPen, left, top + y * _zoom, left + _width * _zoom, top + y * _zoom);
        }
    }

    private void DrawCheckerboard(Graphics graphics, int left, int top)
    {
        var cell = Math.Max(1, _zoom);
        using var a = new SolidBrush(Color.FromArgb(62, 64, 69));
        using var b = new SolidBrush(Color.FromArgb(79, 82, 88));
        for (var y = 0; y < _height; y++)
        {
            for (var x = 0; x < _width; x++)
            {
                var brush = ((x + y) & 1) == 0 ? a : b;
                graphics.FillRectangle(brush, left + x * cell, top + y * cell, cell, cell);
            }
        }
    }

    private void ApplyTool(int x, int y)
    {
        var index = y * _width + x;
        switch (Tool)
        {
            case PixelTool.Pencil:
                _opaque[index] = true;
                _indices[index] = (byte)SelectedPaletteIndex;
                break;
            case PixelTool.Eraser:
                _opaque[index] = false;
                break;
            case PixelTool.Fill:
                FloodFill(x, y, (byte)SelectedPaletteIndex);
                _drawing = false;
                _transactionStarted = false;
                break;
        }
        Invalidate();
    }

    private void FloodFill(int startX, int startY, byte newColor)
    {
        var start = startY * _width + startX;
        var targetOpaque = _opaque[start];
        var targetColor = _indices[start];
        if (targetOpaque && targetColor == newColor)
            return;

        var queue = new Queue<int>();
        var seen = new bool[_indices.Length];
        queue.Enqueue(start);
        seen[start] = true;

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (_opaque[current] != targetOpaque)
                continue;
            if (targetOpaque && _indices[current] != targetColor)
                continue;

            _opaque[current] = true;
            _indices[current] = newColor;

            var x = current % _width;
            var y = current / _width;
            TryQueue(x - 1, y);
            TryQueue(x + 1, y);
            TryQueue(x, y - 1);
            TryQueue(x, y + 1);
        }

        void TryQueue(int x, int y)
        {
            if ((uint)x >= (uint)_width || (uint)y >= (uint)_height)
                return;
            var next = y * _width + x;
            if (seen[next])
                return;
            seen[next] = true;
            queue.Enqueue(next);
        }
    }

    private void BeginTransaction()
    {
        if (_transactionStarted)
            return;
        _undo.Push(Capture());
        if (_undo.Count > 50)
        {
            var keep = _undo.Take(50).Reverse().ToArray();
            _undo.Clear();
            foreach (var state in keep)
                _undo.Push(state);
        }
        _redo.Clear();
        _transactionStarted = true;
    }

    private PixelState Capture() => new(_indices.ToArray(), _opaque.ToArray());

    private void Restore(PixelState state)
    {
        _indices = state.Indices.ToArray();
        _opaque = state.Opaque.ToArray();
        Invalidate();
    }

    private bool TryGetPixel(Point location, out int x, out int y)
    {
        var offset = AutoScrollPosition;
        var left = 20 + offset.X;
        var top = 20 + offset.Y;
        x = (location.X - left) / _zoom;
        y = (location.Y - top) / _zoom;
        return location.X >= left && location.Y >= top && x >= 0 && y >= 0 && x < _width && y < _height;
    }

    private void UpdateScrollSize()
    {
        AutoScrollMinSize = new Size(_width * _zoom + 40, _height * _zoom + 40);
    }

    private sealed record PixelState(byte[] Indices, bool[] Opaque);
}
