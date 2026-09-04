using System.Drawing.Imaging;
using UzDoom.Core;

namespace UzDoom.SpriteStudio;

internal sealed class MainForm : Form
{
    private readonly ListView _spriteList = new();
    private readonly ImageList _thumbnails = new();
    private readonly SpritePreviewControl _preview = new();
    private readonly Label _nameValue = new();
    private readonly Label _sizeValue = new();
    private readonly Label _indexValue = new();
    private readonly NumericUpDown _leftOffset = new();
    private readonly NumericUpDown _topOffset = new();
    private readonly Button _applyOffsets = new();
    private readonly ToolStripStatusLabel _status = new();

    private WadFile? _wad;
    private DoomPalette? _palette;
    private string? _sourcePath;
    private bool _dirty;
    private bool _updatingFields;

    public MainForm()
    {
        Text = "UzDoom Sprite Studio v0.1";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 1280;
        Height = 820;
        MinimumSize = new Size(900, 600);
        BackColor = Color.FromArgb(30, 32, 36);
        ForeColor = Color.Gainsboro;

        var menu = BuildMenu();
        var toolbar = BuildToolbar();
        var statusStrip = new StatusStrip
        {
            BackColor = Color.FromArgb(37, 39, 44),
            ForeColor = Color.Gainsboro,
            SizingGrip = false
        };
        statusStrip.Items.Add(_status);
        _status.Text = "Open a WAD to begin.";

        var outer = new SplitContainer
        {
            Size = new Size(1180, 700),
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            BackColor = Color.FromArgb(22, 24, 27),
            Panel1MinSize = 220,
            Panel2MinSize = 500,
            SplitterDistance = 300
        };

        ConfigureSpriteList();
        outer.Panel1.Controls.Add(_spriteList);

        var editorSplit = new SplitContainer
        {
            Size = new Size(1000, 700),
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            BackColor = Color.FromArgb(22, 24, 27),
            Panel1MinSize = 350,
            Panel2MinSize = 220,
            SplitterDistance = 650
        };
        _preview.Dock = DockStyle.Fill;
        editorSplit.Panel1.Controls.Add(_preview);
        editorSplit.Panel2.Controls.Add(BuildInfoPanel());
        outer.Panel2.Controls.Add(editorSplit);

        Controls.Add(outer);
        Controls.Add(statusStrip);
        Controls.Add(toolbar);
        Controls.Add(menu);
        MainMenuStrip = menu;

        FormClosing += OnFormClosing;
    }

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip
        {
            BackColor = Color.FromArgb(37, 39, 44),
            ForeColor = Color.White
        };

        var file = new ToolStripMenuItem("File");
        file.DropDownItems.Add("Open WAD...", null, (_, _) => OpenWad());
        file.DropDownItems.Add("Save WAD As...", null, (_, _) => SaveWadAs());
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("Export Selected PNG...", null, (_, _) => ExportSelected());
        file.DropDownItems.Add("Replace Selected From PNG...", null, (_, _) => ImportSelected());
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("Exit", null, (_, _) => Close());

        menu.Items.Add(file);
        return menu;
    }

    private ToolStrip BuildToolbar()
    {
        var toolbar = new ToolStrip
        {
            Dock = DockStyle.Top,
            GripStyle = ToolStripGripStyle.Hidden,
            BackColor = Color.FromArgb(43, 45, 50),
            ForeColor = Color.White,
            Padding = new Padding(6, 3, 6, 3)
        };

        toolbar.Items.Add(new ToolStripButton("Open", null, (_, _) => OpenWad()));
        toolbar.Items.Add(new ToolStripButton("Save As", null, (_, _) => SaveWadAs()));
        toolbar.Items.Add(new ToolStripSeparator());
        toolbar.Items.Add(new ToolStripButton("Export PNG", null, (_, _) => ExportSelected()));
        toolbar.Items.Add(new ToolStripButton("Replace PNG", null, (_, _) => ImportSelected()));
        return toolbar;
    }

    private void ConfigureSpriteList()
    {
        _thumbnails.ImageSize = new Size(72, 72);
        _thumbnails.ColorDepth = ColorDepth.Depth32Bit;
        _thumbnails.TransparentColor = Color.Transparent;

        _spriteList.Dock = DockStyle.Fill;
        _spriteList.View = View.LargeIcon;
        _spriteList.LargeImageList = _thumbnails;
        _spriteList.MultiSelect = false;
        _spriteList.HideSelection = false;
        _spriteList.BackColor = Color.FromArgb(31, 33, 38);
        _spriteList.ForeColor = Color.Gainsboro;
        _spriteList.BorderStyle = BorderStyle.None;
        _spriteList.SelectedIndexChanged += (_, _) => ShowSelectedSprite();
    }

    private Control BuildInfoPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            BackColor = Color.FromArgb(35, 37, 42)
        };

        var title = new Label
        {
            Text = "SPRITE INFO",
            Dock = DockStyle.Top,
            Height = 34,
            Font = new Font(Font.FontFamily, 12f, FontStyle.Bold),
            ForeColor = Color.White
        };
        panel.Controls.Add(title);

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Top = 50,
            Height = 300,
            ColumnCount = 2,
            RowCount = 6,
            AutoSize = false,
            BackColor = panel.BackColor
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddInfoRow(table, 0, "Name", _nameValue);
        AddInfoRow(table, 1, "Lump #", _indexValue);
        AddInfoRow(table, 2, "Size", _sizeValue);

        ConfigureOffset(_leftOffset);
        ConfigureOffset(_topOffset);
        table.Controls.Add(MakeLabel("Left offset"), 0, 3);
        table.Controls.Add(_leftOffset, 1, 3);
        table.Controls.Add(MakeLabel("Top offset"), 0, 4);
        table.Controls.Add(_topOffset, 1, 4);

        _applyOffsets.Text = "Apply Offsets";
        _applyOffsets.Dock = DockStyle.Fill;
        _applyOffsets.FlatStyle = FlatStyle.Flat;
        _applyOffsets.BackColor = Color.FromArgb(65, 68, 76);
        _applyOffsets.ForeColor = Color.White;
        _applyOffsets.Click += (_, _) => ApplyOffsets();
        table.Controls.Add(_applyOffsets, 1, 5);

        panel.Controls.Add(table);

        var help = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 105,
            Text = "Red crosshair = Doom sprite origin.\r\n\r\nPNG import keeps the current offsets and converts colours to this WAD's PLAYPAL palette.",
            ForeColor = Color.Silver
        };
        panel.Controls.Add(help);
        return panel;
    }

    private static void AddInfoRow(TableLayoutPanel table, int row, string label, Label value)
    {
        table.Controls.Add(MakeLabel(label), 0, row);
        value.Dock = DockStyle.Fill;
        value.TextAlign = ContentAlignment.MiddleLeft;
        value.ForeColor = Color.White;
        table.Controls.Add(value, 1, row);
    }

    private static Label MakeLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Color.Silver
    };

    private static void ConfigureOffset(NumericUpDown control)
    {
        control.Minimum = short.MinValue;
        control.Maximum = short.MaxValue;
        control.Dock = DockStyle.Fill;
        control.BackColor = Color.FromArgb(48, 50, 56);
        control.ForeColor = Color.White;
        control.BorderStyle = BorderStyle.FixedSingle;
    }

    private void OpenWad()
    {
        if (!ConfirmDiscardChanges())
            return;

        using var dialog = new OpenFileDialog
        {
            Filter = "Doom WAD files (*.wad)|*.wad|All files (*.*)|*.*",
            Title = "Open WAD"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            Cursor = Cursors.WaitCursor;
            _status.Text = "Reading WAD...";
            var wad = WadFile.Open(dialog.FileName);
            var playpal = wad.FindFirst("PLAYPAL") ?? throw new InvalidDataException("This WAD has no PLAYPAL lump.");
            var palette = DoomPalette.FromPlaypal(playpal.Data.Span);

            _wad = wad;
            _palette = palette;
            _sourcePath = dialog.FileName;
            _dirty = false;
            PopulateSprites();
            UpdateTitle();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not open WAD", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _status.Text = "Open failed.";
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void PopulateSprites()
    {
        if (_wad is null || _palette is null)
            return;

        _spriteList.BeginUpdate();
        try
        {
            _spriteList.Items.Clear();
            _thumbnails.Images.Clear();
            _preview.SetSprite(null);

            var spriteIndices = _wad.GetSpriteLumpIndices();
            var skipped = 0;

            foreach (var index in spriteIndices)
            {
                var lump = _wad.Lumps[index];
                try
                {
                    var image = DoomPatchCodec.Decode(lump.Data.Span);
                    using var bitmap = SpriteBitmapFactory.ToBitmap(image, _palette);
                    var thumbnail = SpriteBitmapFactory.CreateThumbnail(bitmap, 72, 72);
                    var key = index.ToString();
                    _thumbnails.Images.Add(key, thumbnail);

                    var entry = new SpriteEntry(index, lump.Name, image);
                    _spriteList.Items.Add(new ListViewItem(lump.Name)
                    {
                        ImageKey = key,
                        Tag = entry
                    });
                }
                catch (InvalidDataException)
                {
                    skipped++;
                }
            }

            _status.Text = $"Loaded {_spriteList.Items.Count:N0} sprites" + (skipped > 0 ? $". Skipped {skipped:N0} non-patch/invalid lumps." : ".");
            if (_spriteList.Items.Count > 0)
                _spriteList.Items[0].Selected = true;
        }
        finally
        {
            _spriteList.EndUpdate();
        }
    }

    private void ShowSelectedSprite()
    {
        var entry = CurrentEntry;
        if (entry is null || _palette is null)
        {
            _preview.SetSprite(null);
            return;
        }

        _preview.SetSprite(SpriteBitmapFactory.ToBitmap(entry.Image, _palette), entry.Image.LeftOffset, entry.Image.TopOffset);
        _nameValue.Text = entry.Name;
        _indexValue.Text = entry.LumpIndex.ToString();
        _sizeValue.Text = $"{entry.Image.Width} × {entry.Image.Height}";

        _updatingFields = true;
        _leftOffset.Value = Math.Clamp(entry.Image.LeftOffset, short.MinValue, short.MaxValue);
        _topOffset.Value = Math.Clamp(entry.Image.TopOffset, short.MinValue, short.MaxValue);
        _updatingFields = false;
    }

    private void ExportSelected()
    {
        var entry = CurrentEntry;
        if (entry is null || _palette is null)
        {
            MessageBox.Show(this, "Select a sprite first.", "Export PNG", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "PNG image (*.png)|*.png",
            FileName = entry.Name + ".png",
            Title = "Export Sprite PNG"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        using var bitmap = SpriteBitmapFactory.ToBitmap(entry.Image, _palette);
        bitmap.Save(dialog.FileName, ImageFormat.Png);
        _status.Text = $"Exported {entry.Name}.";
    }

    private void ImportSelected()
    {
        var entry = CurrentEntry;
        var item = CurrentItem;
        if (entry is null || item is null || _palette is null || _wad is null)
        {
            MessageBox.Show(this, "Select a sprite first.", "Replace Sprite", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Filter = "PNG image (*.png)|*.png|Image files|*.png;*.bmp;*.gif;*.jpg;*.jpeg",
            Title = $"Replace {entry.Name}"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            using var bitmap = new Bitmap(dialog.FileName);
            var imported = SpriteBitmapFactory.FromBitmap(bitmap, _palette, entry.Image.LeftOffset, entry.Image.TopOffset);
            var encoded = DoomPatchCodec.Encode(imported);
            _wad.ReplaceLump(entry.LumpIndex, encoded);
            entry.Image = imported;
            RefreshThumbnail(item, entry);
            MarkDirty();
            ShowSelectedSprite();
            _status.Text = $"Replaced {entry.Name}. Save As to build a new WAD.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Import failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ApplyOffsets()
    {
        if (_updatingFields)
            return;

        var entry = CurrentEntry;
        if (entry is null || _wad is null)
            return;

        try
        {
            var updated = entry.Image.WithOffsets((int)_leftOffset.Value, (int)_topOffset.Value);
            _wad.ReplaceLump(entry.LumpIndex, DoomPatchCodec.Encode(updated));
            entry.Image = updated;
            MarkDirty();
            ShowSelectedSprite();
            _status.Text = $"Updated offsets for {entry.Name}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Offset update failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RefreshThumbnail(ListViewItem item, SpriteEntry entry)
    {
        if (_palette is null)
            return;

        var key = entry.LumpIndex.ToString();
        using var source = SpriteBitmapFactory.ToBitmap(entry.Image, _palette);
        var thumbnail = SpriteBitmapFactory.CreateThumbnail(source, 72, 72);
        _thumbnails.Images.RemoveByKey(key);
        _thumbnails.Images.Add(key, thumbnail);
        item.ImageKey = key;
        _spriteList.Invalidate();
    }

    private bool SaveWadAs()
    {
        if (_wad is null || string.IsNullOrWhiteSpace(_sourcePath))
        {
            MessageBox.Show(this, "Open a WAD first.", "Save WAD As", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "Doom WAD files (*.wad)|*.wad",
            FileName = "new" + Path.GetFileName(_sourcePath),
            InitialDirectory = Path.GetDirectoryName(_sourcePath),
            Title = "Build Edited WAD As"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return false;

        if (string.Equals(Path.GetFullPath(dialog.FileName), Path.GetFullPath(_sourcePath), StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "Sprite Studio will not overwrite the source WAD. Choose a new filename.", "Source WAD protected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            _status.Text = "Building WAD...";
            _wad.SaveAs(dialog.FileName);
            _dirty = false;
            UpdateTitle();
            _status.Text = $"Built {Path.GetFileName(dialog.FileName)} successfully.";
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Build failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _status.Text = "Build failed.";
            return false;
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private bool ConfirmDiscardChanges()
    {
        if (!_dirty)
            return true;

        var result = MessageBox.Show(this, "You have sprite changes that have not been built into a new WAD. Build them now?", "Unsaved changes", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
        return result switch
        {
            DialogResult.Yes => SaveWadAs(),
            DialogResult.No => true,
            _ => false
        };
    }

    private void MarkDirty()
    {
        _dirty = true;
        UpdateTitle();
    }

    private void UpdateTitle()
    {
        var file = string.IsNullOrWhiteSpace(_sourcePath) ? null : Path.GetFileName(_sourcePath);
        Text = file is null
            ? "UzDoom Sprite Studio v0.1"
            : $"UzDoom Sprite Studio v0.1 - {file}{(_dirty ? " *" : string.Empty)}";
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!ConfirmDiscardChanges())
            e.Cancel = true;
    }

    private ListViewItem? CurrentItem => _spriteList.SelectedItems.Count == 0 ? null : _spriteList.SelectedItems[0];
    private SpriteEntry? CurrentEntry => CurrentItem?.Tag as SpriteEntry;

    private sealed class SpriteEntry(int lumpIndex, string name, DoomPatchImage image)
    {
        public int LumpIndex { get; } = lumpIndex;
        public string Name { get; } = name;
        public DoomPatchImage Image { get; set; } = image;
    }
}
