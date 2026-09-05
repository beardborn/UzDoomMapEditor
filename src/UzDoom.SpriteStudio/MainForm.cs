using System.Drawing.Imaging;
using UzDoom.Core;

namespace UzDoom.SpriteStudio;

internal sealed class MainForm : Form
{
    private readonly TreeView _familyTree = new();
    private readonly TextBox _familySearch = new();
    private readonly ListView _spriteList = new();
    private readonly ImageList _thumbnails = new();
    private readonly SpritePreviewControl _preview = new();
    private readonly Label _nameValue = new();
    private readonly Label _familyValue = new();
    private readonly Label _frameValue = new();
    private readonly Label _rotationValue = new();
    private readonly Label _sizeValue = new();
    private readonly Label _indexValue = new();
    private readonly NumericUpDown _leftOffset = new();
    private readonly NumericUpDown _topOffset = new();
    private readonly Button _applyOffsets = new();
    private readonly Button _editPixels = new();
    private readonly ToolStripStatusLabel _status = new();
    private readonly ComboBox _rotationCombo = new();
    private readonly NumericUpDown _animationDelay = new();
    private readonly Button _playButton = new();
    private readonly System.Windows.Forms.Timer _animationTimer = new();
    private readonly List<SpriteEntry> _entries = new();
    private readonly List<AnimationFrame> _animationFrames = new();

    private WadFile? _wad;
    private DoomPalette? _palette;
    private string? _sourcePath;
    private bool _dirty;
    private bool _updatingFields;
    private int _animationIndex;

    public MainForm()
    {
        Text = "UzDoom Sprite Studio v0.3";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 1450;
        Height = 860;
        MinimumSize = new Size(1050, 650);
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
            Size = new Size(1360, 730),
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            BackColor = Color.FromArgb(22, 24, 27),
            SplitterDistance = 230
        };
        outer.Panel1MinSize = 190;
        outer.Panel2MinSize = 700;
        outer.Panel1.Controls.Add(BuildFamilyPanel());

        var browserSplit = new SplitContainer
        {
            Size = new Size(1120, 730),
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            BackColor = Color.FromArgb(22, 24, 27),
            SplitterDistance = 330
        };
        browserSplit.Panel1MinSize = 260;
        browserSplit.Panel2MinSize = 520;

        ConfigureSpriteList();
        browserSplit.Panel1.Controls.Add(_spriteList);

        var editorSplit = new SplitContainer
        {
            Size = new Size(780, 730),
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            BackColor = Color.FromArgb(22, 24, 27),
            SplitterDistance = 540
        };
        editorSplit.Panel1MinSize = 360;
        editorSplit.Panel2MinSize = 230;

        var previewPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(24, 26, 30) };
        _preview.Dock = DockStyle.Fill;
        previewPanel.Controls.Add(_preview);
        previewPanel.Controls.Add(BuildAnimationPanel());
        editorSplit.Panel1.Controls.Add(previewPanel);
        editorSplit.Panel2.Controls.Add(BuildInfoPanel());
        browserSplit.Panel2.Controls.Add(editorSplit);
        outer.Panel2.Controls.Add(browserSplit);

        Controls.Add(outer);
        Controls.Add(statusStrip);
        Controls.Add(toolbar);
        Controls.Add(menu);
        MainMenuStrip = menu;

        _animationTimer.Interval = 120;
        _animationTimer.Tick += (_, _) => AdvanceAnimation();
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
        file.DropDownItems.Add("Export Selected Family...", null, (_, _) => ExportSelectedFamily());
        file.DropDownItems.Add("Export Family Sprite Sheet...", null, (_, _) => ExportFamilySheet());
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("Replace Selected From PNG...", null, (_, _) => ImportSelected());
        file.DropDownItems.Add("Import Family From Folder...", null, (_, _) => ImportFamilyFolder());
        file.DropDownItems.Add("Import Family Sprite Sheet...", null, (_, _) => ImportFamilySheet());
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("Exit", null, (_, _) => Close());

        var edit = new ToolStripMenuItem("Edit");
        edit.DropDownItems.Add("Edit Selected Sprite Pixels...", null, (_, _) => EditSelectedPixels());
        edit.DropDownItems.Add("Apply Sprite Offsets", null, (_, _) => ApplyOffsets());

        var spriteSet = new ToolStripMenuItem("Sprite Set");
        spriteSet.DropDownItems.Add("Frame / Rotation Grid...", null, (_, _) => ShowSpriteSetGrid());
        spriteSet.DropDownItems.Add(new ToolStripSeparator());
        spriteSet.DropDownItems.Add("Auto Align Family - Center + Visible Feet", null, (_, _) => AutoAlignFamily());
        spriteSet.DropDownItems.Add("Copy Selected Offsets To Family", null, (_, _) => CopySelectedOffsetsToFamily());

        menu.Items.Add(file);
        menu.Items.Add(edit);
        menu.Items.Add(spriteSet);
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
        toolbar.Items.Add(new ToolStripButton("Set Grid", null, (_, _) => ShowSpriteSetGrid()));
        toolbar.Items.Add(new ToolStripButton("Edit Pixels", null, (_, _) => EditSelectedPixels()));
        toolbar.Items.Add(new ToolStripButton("Export Family", null, (_, _) => ExportSelectedFamily()));
        toolbar.Items.Add(new ToolStripButton("Import Family", null, (_, _) => ImportFamilyFolder()));
        toolbar.Items.Add(new ToolStripButton("Sheet Export", null, (_, _) => ExportFamilySheet()));
        toolbar.Items.Add(new ToolStripButton("Sheet Import", null, (_, _) => ImportFamilySheet()));
        return toolbar;
    }

    private Control BuildFamilyPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(31, 33, 38),
            Padding = new Padding(8)
        };

        var title = new Label
        {
            Text = "SPRITE FAMILIES",
            Dock = DockStyle.Top,
            Height = 28,
            Font = new Font(Font.FontFamily, 9f, FontStyle.Bold),
            ForeColor = Color.White
        };

        _familySearch.Dock = DockStyle.Top;
        _familySearch.Height = 28;
        _familySearch.PlaceholderText = "Filter families...";
        _familySearch.BackColor = Color.FromArgb(47, 49, 55);
        _familySearch.ForeColor = Color.White;
        _familySearch.BorderStyle = BorderStyle.FixedSingle;
        _familySearch.TextChanged += (_, _) => RebuildFamilyTree(_familySearch.Text);

        _familyTree.Dock = DockStyle.Fill;
        _familyTree.BackColor = Color.FromArgb(31, 33, 38);
        _familyTree.ForeColor = Color.Gainsboro;
        _familyTree.BorderStyle = BorderStyle.None;
        _familyTree.HideSelection = false;
        _familyTree.FullRowSelect = true;
        _familyTree.AfterSelect += (_, _) => PopulateSelectedFamily();

        panel.Controls.Add(_familyTree);
        panel.Controls.Add(_familySearch);
        panel.Controls.Add(title);
        return panel;
    }

    private void ConfigureSpriteList()
    {
        _thumbnails.ImageSize = new Size(80, 80);
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
        _spriteList.SelectedIndexChanged += (_, _) =>
        {
            StopAnimation();
            ShowSelectedSprite();
        };
    }

    private Control BuildAnimationPanel()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(8, 7, 8, 5),
            BackColor = Color.FromArgb(38, 40, 45)
        };

        _playButton.Text = "▶ Animate";
        _playButton.Width = 90;
        _playButton.Height = 28;
        _playButton.FlatStyle = FlatStyle.Flat;
        _playButton.BackColor = Color.FromArgb(62, 65, 72);
        _playButton.ForeColor = Color.White;
        _playButton.Click += (_, _) => ToggleAnimation();

        _rotationCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _rotationCombo.Width = 105;
        for (var i = 1; i <= 8; i++)
            _rotationCombo.Items.Add($"Rotation {i}");
        _rotationCombo.SelectedIndex = 0;
        _rotationCombo.SelectedIndexChanged += (_, _) =>
        {
            StopAnimation();
            BuildAnimationFrames();
        };

        _animationDelay.Minimum = 35;
        _animationDelay.Maximum = 1000;
        _animationDelay.Increment = 10;
        _animationDelay.Value = 120;
        _animationDelay.Width = 72;
        _animationDelay.BackColor = Color.FromArgb(48, 50, 56);
        _animationDelay.ForeColor = Color.White;
        _animationDelay.ValueChanged += (_, _) => _animationTimer.Interval = (int)_animationDelay.Value;

        panel.Controls.Add(_playButton);
        panel.Controls.Add(MakeSmallLabel("View"));
        panel.Controls.Add(_rotationCombo);
        panel.Controls.Add(MakeSmallLabel("Delay ms"));
        panel.Controls.Add(_animationDelay);
        return panel;
    }

    private static Label MakeSmallLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(10, 6, 4, 0),
        ForeColor = Color.Silver
    };

    private Control BuildInfoPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
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

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 350,
            ColumnCount = 2,
            RowCount = 8,
            AutoSize = false,
            BackColor = panel.BackColor
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddInfoRow(table, 0, "Name", _nameValue);
        AddInfoRow(table, 1, "Family", _familyValue);
        AddInfoRow(table, 2, "Frame", _frameValue);
        AddInfoRow(table, 3, "Rotation", _rotationValue);
        AddInfoRow(table, 4, "Lump #", _indexValue);
        AddInfoRow(table, 5, "Size", _sizeValue);

        ConfigureOffset(_leftOffset);
        ConfigureOffset(_topOffset);
        table.Controls.Add(MakeLabel("Left offset"), 0, 6);
        table.Controls.Add(_leftOffset, 1, 6);
        table.Controls.Add(MakeLabel("Top offset"), 0, 7);
        table.Controls.Add(_topOffset, 1, 7);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 92,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0)
        };

        _editPixels.Text = "Edit Pixels...";
        _editPixels.Width = 180;
        _editPixels.Height = 32;
        _editPixels.FlatStyle = FlatStyle.Flat;
        _editPixels.BackColor = Color.FromArgb(66, 83, 116);
        _editPixels.ForeColor = Color.White;
        _editPixels.Click += (_, _) => EditSelectedPixels();

        _applyOffsets.Text = "Apply Offsets";
        _applyOffsets.Width = 180;
        _applyOffsets.Height = 32;
        _applyOffsets.FlatStyle = FlatStyle.Flat;
        _applyOffsets.BackColor = Color.FromArgb(65, 68, 76);
        _applyOffsets.ForeColor = Color.White;
        _applyOffsets.Click += (_, _) => ApplyOffsets();

        buttons.Controls.Add(_editPixels);
        buttons.Controls.Add(_applyOffsets);

        var help = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 108,
            Text = "Red crosshair = Doom sprite origin.\r\n\r\nNames such as A2A8 contain a mirrored second rotation. Animation preview handles that automatically.",
            ForeColor = Color.Silver
        };

        panel.Controls.Add(help);
        panel.Controls.Add(buttons);
        panel.Controls.Add(table);
        panel.Controls.Add(title);
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
            _status.Text = "Reading WAD and decoding sprites...";
            var wad = WadFile.Open(dialog.FileName);
            var playpal = wad.FindFirst("PLAYPAL") ?? throw new InvalidDataException("This WAD has no PLAYPAL lump.");
            var palette = DoomPalette.FromPlaypal(playpal.Data.Span);

            _wad = wad;
            _palette = palette;
            _sourcePath = dialog.FileName;
            _dirty = false;
            DecodeSprites();
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

    private void DecodeSprites()
    {
        if (_wad is null)
            return;

        StopAnimation();
        _entries.Clear();
        _spriteList.Items.Clear();
        _thumbnails.Images.Clear();
        _familyTree.Nodes.Clear();
        _preview.SetSprite(null);

        var skipped = 0;
        foreach (var index in _wad.GetSpriteLumpIndices())
        {
            var lump = _wad.Lumps[index];
            try
            {
                var image = DoomPatchCodec.Decode(lump.Data.Span);
                _entries.Add(new SpriteEntry(index, lump.Name, image, SpriteNameParser.Parse(lump.Name)));
            }
            catch (InvalidDataException)
            {
                skipped++;
            }
        }

        RebuildFamilyTree(_familySearch.Text);
        var familyCount = _entries.Select(e => e.NameInfo.Family).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        _status.Text = $"Loaded {_entries.Count:N0} sprites in {familyCount:N0} families" +
                       (skipped > 0 ? $". Skipped {skipped:N0} invalid/non-patch lumps." : ".");
    }

    private void RebuildFamilyTree(string? filter)
    {
        if (_entries.Count == 0)
            return;

        var previous = CurrentFamily;
        var query = (filter ?? string.Empty).Trim();
        var groups = _entries
            .GroupBy(e => e.NameInfo.Family, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Where(g => query.Length == 0 || g.Key.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        _familyTree.BeginUpdate();
        try
        {
            _familyTree.Nodes.Clear();
            foreach (var group in groups)
            {
                _familyTree.Nodes.Add(new TreeNode($"{group.Key}  ({group.Count()})")
                {
                    Tag = group.Key
                });
            }

            var select = _familyTree.Nodes.Cast<TreeNode>()
                .FirstOrDefault(node => string.Equals(node.Tag as string, previous, StringComparison.OrdinalIgnoreCase))
                ?? (_familyTree.Nodes.Count > 0 ? _familyTree.Nodes[0] : null);
            if (select is not null)
                _familyTree.SelectedNode = select;
        }
        finally
        {
            _familyTree.EndUpdate();
        }
    }

    private void PopulateSelectedFamily()
    {
        if (_palette is null || CurrentFamily is not { Length: > 0 } family)
            return;

        StopAnimation();
        _spriteList.BeginUpdate();
        try
        {
            _spriteList.Items.Clear();
            _thumbnails.Images.Clear();
            _preview.SetSprite(null);

            foreach (var entry in EntriesForFamily(family))
            {
                using var bitmap = SpriteBitmapFactory.ToBitmap(entry.Image, _palette);
                var thumbnail = SpriteBitmapFactory.CreateThumbnail(bitmap, 80, 80);
                var key = entry.LumpIndex.ToString();
                _thumbnails.Images.Add(key, thumbnail);
                _spriteList.Items.Add(new ListViewItem(entry.Name)
                {
                    ImageKey = key,
                    Tag = entry
                });
            }

            BuildAnimationFrames();
            _status.Text = $"{family}: {_spriteList.Items.Count:N0} sprite lumps.";
            if (_spriteList.Items.Count > 0)
                _spriteList.Items[0].Selected = true;
        }
        finally
        {
            _spriteList.EndUpdate();
        }
    }

    private IEnumerable<SpriteEntry> EntriesForFamily(string family)
        => _entries.Where(e => string.Equals(e.NameInfo.Family, family, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase);

    private void ShowSelectedSprite()
    {
        var entry = CurrentEntry;
        if (entry is null || _palette is null)
        {
            _preview.SetSprite(null);
            return;
        }

        _preview.SetSprite(SpriteBitmapFactory.ToBitmap(entry.Image, _palette), entry.Image.LeftOffset, entry.Image.TopOffset);
        UpdateInfo(entry);
    }

    private void UpdateInfo(SpriteEntry entry)
    {
        _nameValue.Text = entry.Name;
        _familyValue.Text = entry.NameInfo.Family;
        _frameValue.Text = entry.NameInfo.FrameText;
        _rotationValue.Text = entry.NameInfo.RotationText;
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

    private void ExportSelectedFamily()
    {
        if (_palette is null || CurrentFamily is not { Length: > 0 } family)
        {
            MessageBox.Show(this, "Select a sprite family first.", "Export Family", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new FolderBrowserDialog
        {
            Description = $"Choose a folder for the {family} sprite family",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            var folder = Path.Combine(dialog.SelectedPath, family);
            Directory.CreateDirectory(folder);
            var entries = EntriesForFamily(family).ToList();
            var metadata = new List<string> { "Name,Width,Height,LeftOffset,TopOffset,Frames,Rotations" };

            foreach (var entry in entries)
            {
                using var bitmap = SpriteBitmapFactory.ToBitmap(entry.Image, _palette);
                bitmap.Save(Path.Combine(folder, entry.Name + ".png"), ImageFormat.Png);
                metadata.Add(string.Join(',',
                    entry.Name,
                    entry.Image.Width,
                    entry.Image.Height,
                    entry.Image.LeftOffset,
                    entry.Image.TopOffset,
                    entry.NameInfo.FrameText.Replace(',', '/'),
                    entry.NameInfo.RotationText.Replace(',', '/')));
            }

            File.WriteAllLines(Path.Combine(folder, "sprite-info.csv"), metadata);
            _status.Text = $"Exported {entries.Count:N0} {family} sprites to {folder}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Family export failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExportFamilySheet()
    {
        if (_palette is null || CurrentFamily is not { Length: > 0 } family)
        {
            MessageBox.Show(this, "Select a sprite family first.", "Export Sprite Sheet", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "PNG image (*.png)|*.png",
            FileName = family + "-sheet.png",
            Title = $"Export {family} Sprite Sheet"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            var items = EntriesForFamily(family)
                .Select(entry => new SpriteSheetItem(entry.Name, entry.Image))
                .ToList();
            SpriteSheetWorkflow.Export(family, items, _palette, dialog.FileName);
            _status.Text = $"Exported {family} sprite sheet and manifest CSV.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Sprite-sheet export failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ImportSelected()
    {
        var entry = CurrentEntry;
        if (entry is null || _palette is null || _wad is null)
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
            ReplaceEntryImage(entry, imported);
            _status.Text = $"Replaced {entry.Name}. Save As to build a new WAD.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Import failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ImportFamilyFolder()
    {
        if (_palette is null || _wad is null || CurrentFamily is not { Length: > 0 } family)
        {
            MessageBox.Show(this, "Select a sprite family first.", "Import Family", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new FolderBrowserDialog
        {
            Description = $"Choose the folder containing replacement PNGs for {family}",
            UseDescriptionForTitle = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            var files = Directory.EnumerateFiles(dialog.SelectedPath, "*.png", SearchOption.TopDirectoryOnly)
                .ToDictionary(path => Path.GetFileNameWithoutExtension(path).ToUpperInvariant(), path => path, StringComparer.OrdinalIgnoreCase);
            var prepared = new List<(SpriteEntry Entry, DoomPatchImage Image)>();

            foreach (var entry in EntriesForFamily(family))
            {
                if (!files.TryGetValue(entry.Name, out var path))
                    continue;
                using var bitmap = new Bitmap(path);
                prepared.Add((entry, SpriteBitmapFactory.FromBitmap(bitmap, _palette, entry.Image.LeftOffset, entry.Image.TopOffset)));
            }

            if (prepared.Count == 0)
            {
                MessageBox.Show(this, $"No PNG filenames matched the {family} lump names. Expected names such as {EntriesForFamily(family).First().Name}.png.", "Import Family", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            ApplyPreparedBatch(prepared);
            _status.Text = $"Imported {prepared.Count:N0} replacement sprites for {family}. Save As to build the WAD.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Family import failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ImportFamilySheet()
    {
        if (_palette is null || _wad is null || CurrentFamily is not { Length: > 0 } family)
        {
            MessageBox.Show(this, "Select a sprite family first.", "Import Sprite Sheet", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Filter = "PNG image (*.png)|*.png",
            Title = $"Import {family} Sprite Sheet"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        List<SpriteSheetReplacement>? replacements = null;
        try
        {
            replacements = SpriteSheetWorkflow.Import(dialog.FileName);
            var byName = EntriesForFamily(family).ToDictionary(entry => entry.Name, StringComparer.OrdinalIgnoreCase);
            var prepared = new List<(SpriteEntry Entry, DoomPatchImage Image)>();

            foreach (var replacement in replacements)
            {
                if (!byName.TryGetValue(replacement.Name, out var entry))
                    continue;
                prepared.Add((entry, SpriteBitmapFactory.FromBitmap(replacement.Bitmap, _palette, entry.Image.LeftOffset, entry.Image.TopOffset)));
            }

            if (prepared.Count == 0)
                throw new InvalidDataException($"The sheet manifest contained no sprites matching the selected {family} family.");

            ApplyPreparedBatch(prepared);
            _status.Text = $"Imported {prepared.Count:N0} sprites from the {family} sheet. Save As to build the WAD.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Sprite-sheet import failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (replacements is not null)
                foreach (var replacement in replacements)
                    replacement.Bitmap.Dispose();
        }
    }

    private void ShowSpriteSetGrid()
    {
        if (CurrentFamily is not { Length: > 0 } family)
        {
            MessageBox.Show(this, "Select a sprite family first.", "Sprite Set Grid", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var items = EntriesForFamily(family)
            .Select(entry => new SpriteGridItem(entry.Name, entry.Image, entry.NameInfo))
            .ToList();
        using var grid = new SpriteSetGridForm(family, items, SelectSpriteByName);
        grid.ShowDialog(this);
    }

    private void SelectSpriteByName(string name)
    {
        foreach (ListViewItem item in _spriteList.Items)
        {
            if (item.Tag is not SpriteEntry entry || !string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
                continue;
            item.Selected = true;
            item.Focused = true;
            item.EnsureVisible();
            ShowSelectedSprite();
            return;
        }
    }

    private void AutoAlignFamily()
    {
        if (_wad is null || CurrentFamily is not { Length: > 0 } family)
        {
            MessageBox.Show(this, "Select a sprite family first.", "Auto Align Family", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            var prepared = new List<(SpriteEntry Entry, DoomPatchImage Image)>();
            foreach (var entry in EntriesForFamily(family))
            {
                var bottom = FindBottomOpaqueRow(entry.Image);
                var topOffset = bottom >= 0 ? bottom + 1 : entry.Image.TopOffset;
                var leftOffset = entry.Image.Width / 2;
                prepared.Add((entry, entry.Image.WithOffsets(leftOffset, topOffset)));
            }

            ApplyPreparedBatch(prepared);
            _status.Text = $"Auto-aligned {prepared.Count:N0} {family} sprites to horizontal center and visible feet.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Auto align failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void CopySelectedOffsetsToFamily()
    {
        var selected = CurrentEntry;
        if (_wad is null || selected is null || CurrentFamily is not { Length: > 0 } family)
        {
            MessageBox.Show(this, "Select a sprite first.", "Copy Offsets", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            var prepared = EntriesForFamily(family)
                .Select(entry => (entry, entry.Image.WithOffsets(selected.Image.LeftOffset, selected.Image.TopOffset)))
                .ToList();
            ApplyPreparedBatch(prepared);
            _status.Text = $"Copied {selected.Name} offsets to {prepared.Count:N0} {family} sprites.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Copy offsets failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static int FindBottomOpaqueRow(DoomPatchImage image)
    {
        for (var y = image.Height - 1; y >= 0; y--)
        {
            var row = y * image.Width;
            for (var x = 0; x < image.Width; x++)
                if (image.OpaqueMask[row + x])
                    return y;
        }
        return -1;
    }

    private void ApplyPreparedBatch(IEnumerable<(SpriteEntry Entry, DoomPatchImage Image)> prepared)
    {
        if (_wad is null)
            return;

        var batch = prepared.ToList();
        foreach (var item in batch)
        {
            var encoded = DoomPatchCodec.Encode(item.Image);
            _wad.ReplaceLump(item.Entry.LumpIndex, encoded);
            item.Entry.Image = item.Image;
        }

        MarkDirty();
        PopulateSelectedFamily();
    }

    private void EditSelectedPixels()
    {
        var entry = CurrentEntry;
        if (entry is null || _palette is null || _wad is null)
        {
            MessageBox.Show(this, "Select a sprite first.", "Pixel Editor", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        StopAnimation();
        using var editor = new PixelEditorForm(entry.Image, _palette, entry.Name);
        if (editor.ShowDialog(this) != DialogResult.OK || editor.EditedImage is null)
            return;

        try
        {
            ReplaceEntryImage(entry, editor.EditedImage);
            _status.Text = $"Edited {entry.Name}. Save As to build a new WAD.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not apply pixel edit", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ReplaceEntryImage(SpriteEntry entry, DoomPatchImage image)
    {
        if (_wad is null)
            return;

        _wad.ReplaceLump(entry.LumpIndex, DoomPatchCodec.Encode(image));
        entry.Image = image;
        RefreshThumbnail(entry);
        BuildAnimationFrames();
        MarkDirty();
        ShowSelectedSprite();
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

    private void RefreshThumbnail(SpriteEntry entry)
    {
        if (_palette is null)
            return;

        var item = _spriteList.Items.Cast<ListViewItem>()
            .FirstOrDefault(i => ReferenceEquals(i.Tag, entry));
        if (item is null)
            return;

        var key = entry.LumpIndex.ToString();
        using var source = SpriteBitmapFactory.ToBitmap(entry.Image, _palette);
        var thumbnail = SpriteBitmapFactory.CreateThumbnail(source, 80, 80);
        _thumbnails.Images.RemoveByKey(key);
        _thumbnails.Images.Add(key, thumbnail);
        item.ImageKey = key;
        _spriteList.Invalidate();
    }

    private void BuildAnimationFrames()
    {
        _animationFrames.Clear();
        _animationIndex = 0;
        if (CurrentFamily is not { Length: > 0 } family)
            return;

        var rotation = _rotationCombo.SelectedIndex + 1;
        var entries = EntriesForFamily(family).ToList();
        var frames = entries.SelectMany(e => e.NameInfo.Slots.Select(slot => slot.Frame)).Distinct().OrderBy(c => c);

        foreach (var frame in frames)
        {
            SpriteEntry? chosenEntry = null;
            SpriteSlot? chosenSlot = null;

            foreach (var entry in entries)
            {
                var slot = SpriteNameParser.FindSlot(entry.NameInfo, frame, rotation);
                if (slot is null)
                    continue;
                chosenEntry = entry;
                chosenSlot = slot;
                if (slot.Value.Rotation == rotation)
                    break;
            }

            if (chosenEntry is not null && chosenSlot is not null)
                _animationFrames.Add(new AnimationFrame(chosenEntry, chosenSlot.Value));
        }
    }

    private void ToggleAnimation()
    {
        if (_animationTimer.Enabled)
        {
            StopAnimation();
            ShowSelectedSprite();
            return;
        }

        BuildAnimationFrames();
        if (_animationFrames.Count < 2)
        {
            MessageBox.Show(this, "This family does not have at least two frames for the selected rotation.", "Animation Preview", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _animationTimer.Interval = (int)_animationDelay.Value;
        _animationIndex = 0;
        _playButton.Text = "■ Stop";
        ShowAnimationFrame(_animationFrames[0]);
        _animationTimer.Start();
    }

    private void AdvanceAnimation()
    {
        if (_animationFrames.Count == 0)
            return;
        _animationIndex = (_animationIndex + 1) % _animationFrames.Count;
        ShowAnimationFrame(_animationFrames[_animationIndex]);
    }

    private void ShowAnimationFrame(AnimationFrame frame)
    {
        if (_palette is null)
            return;

        var bitmap = SpriteBitmapFactory.ToBitmap(frame.Entry.Image, _palette);
        if (frame.Slot.Mirrored)
            bitmap.RotateFlip(RotateFlipType.RotateNoneFlipX);

        _preview.SetSprite(bitmap, frame.Entry.Image.LeftOffset, frame.Entry.Image.TopOffset);
        UpdateInfo(frame.Entry);
        _status.Text = $"Animating {frame.Entry.NameInfo.Family}, frame {frame.Slot.Frame}, rotation {_rotationCombo.SelectedIndex + 1}" +
                       (frame.Slot.Mirrored ? " (mirrored)." : ".");
    }

    private void StopAnimation()
    {
        if (!_animationTimer.Enabled)
            return;
        _animationTimer.Stop();
        _playButton.Text = "▶ Animate";
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
            ? "UzDoom Sprite Studio v0.3"
            : $"UzDoom Sprite Studio v0.3 - {file}{(_dirty ? " *" : string.Empty)}";
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        StopAnimation();
        if (!ConfirmDiscardChanges())
            e.Cancel = true;
    }

    private string? CurrentFamily => _familyTree.SelectedNode?.Tag as string;
    private ListViewItem? CurrentItem => _spriteList.SelectedItems.Count == 0 ? null : _spriteList.SelectedItems[0];
    private SpriteEntry? CurrentEntry => CurrentItem?.Tag as SpriteEntry;

    private sealed class SpriteEntry(int lumpIndex, string name, DoomPatchImage image, SpriteNameInfo nameInfo)
    {
        public int LumpIndex { get; } = lumpIndex;
        public string Name { get; } = name;
        public DoomPatchImage Image { get; set; } = image;
        public SpriteNameInfo NameInfo { get; } = nameInfo;
    }

    private readonly record struct AnimationFrame(SpriteEntry Entry, SpriteSlot Slot);
}
