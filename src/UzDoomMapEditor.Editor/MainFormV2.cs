using System.Diagnostics;
using System.Text.Json;
using UzDoomMapEditor.Core;

namespace UzDoomMapEditor.Editor;

public sealed class MainForm : Form
{
    private readonly MapCanvas _canvas = new();
    private readonly Map3DPreview _preview = new();
    private readonly TextureBrowserControl _assetBrowser = new();
    private readonly PropertyGrid _properties = new() { Dock = DockStyle.Fill, HelpVisible = true, ToolbarVisible = false };
    private readonly ToolStripStatusLabel _status = new("Ready");
    private readonly EditorHistory _history = new();

    private ToolStrip _toolbar = null!;
    private ToolStripButton _undoButton = null!;
    private ToolStripButton _redoButton = null!;
    private ToolStripComboBox _gridCombo = null!;

    private EditorProject _project = new();
    private object? _selectedObject;
    private string? _projectPath;
    private string? _projectRoot;
    private string? _uzDoomPath;
    private string? _iwadPath;

    public MainForm()
    {
        Text = "UZDoom Map Editor";
        Width = 1500;
        Height = 950;
        MinimumSize = new Size(1000, 650);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        var menu = BuildMenu();
        _toolbar = BuildToolbar();
        var statusStrip = new StatusStrip();
        statusStrip.Items.Add(_status);

        var workspace = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            Size = new Size(1100, 700),
            SplitterDistance = 500
        };
        workspace.Panel1.Controls.Add(_canvas);
        workspace.Panel2.Controls.Add(_preview);
        _canvas.Dock = DockStyle.Fill;
        _preview.Dock = DockStyle.Fill;

        var editorAndAssets = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            Size = new Size(1160, 820),
            SplitterDistance = 650,
            FixedPanel = FixedPanel.Panel2,
            Panel2MinSize = 170
        };
        editorAndAssets.Panel1.Controls.Add(workspace);
        editorAndAssets.Panel2.Controls.Add(_assetBrowser);

        var outer = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            Size = new Size(1450, 850),
            SplitterDistance = 1160,
            FixedPanel = FixedPanel.Panel2
        };
        outer.Panel1.Controls.Add(editorAndAssets);
        outer.Panel2.Controls.Add(_properties);

        Controls.Add(outer);
        Controls.Add(statusStrip);
        Controls.Add(_toolbar);
        Controls.Add(menu);
        MainMenuStrip = menu;

        _canvas.SelectionChanged += selected =>
        {
            _selectedObject = selected;
            _properties.SelectedObject = selected;
            _properties.Refresh();
            UpdateMaterialTarget(selected);
        };
        _canvas.ProjectEdited += CommitEdit;
        _canvas.ProjectPreviewChanged += RefreshViews;

        _assetBrowser.ApplyRequested += ApplyTextureAsset;
        _assetBrowser.AssetImported += asset => _status.Text = $"Imported {asset.Name} ({asset.Width}×{asset.Height})";
        _assetBrowser.BaseIwadChanged += path =>
        {
            _iwadPath = path;
            _status.Text = $"Loaded IWAD materials: {Path.GetFileName(path)}";
        };

        _properties.PropertyValueChanged += (_, _) =>
        {
            CommitEdit();
            RefreshViews();
        };

        ApplyProject(new EditorProject(), resetHistory: true, resetViews: true);
        ConfigureProjectAssets();
        SetTool(EditorTool.Select);
    }

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip();

        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.Add(new ToolStripMenuItem("Create &Game Project...", null, (_, _) => CreateGameProject(), Keys.Control | Keys.Shift | Keys.N));
        file.DropDownItems.Add(new ToolStripMenuItem("&New Map", null, (_, _) => NewProject(), Keys.Control | Keys.N));
        file.DropDownItems.Add(new ToolStripMenuItem("&Open...", null, (_, _) => OpenProject(), Keys.Control | Keys.O));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(new ToolStripMenuItem("&Save", null, (_, _) => SaveProject(), Keys.Control | Keys.S));
        file.DropDownItems.Add(new ToolStripMenuItem("Save &As...", null, (_, _) => SaveProjectAs()));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(new ToolStripMenuItem("Export &UDMF Text...", null, (_, _) => ExportUdmf()));
        file.DropDownItems.Add(new ToolStripMenuItem("Export Test &WAD...", null, (_, _) => ExportWad()));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(new ToolStripMenuItem("E&xit", null, (_, _) => Close()));

        var edit = new ToolStripMenuItem("&Edit");
        edit.DropDownItems.Add(new ToolStripMenuItem("&Undo", null, (_, _) => Undo(), Keys.Control | Keys.Z));
        edit.DropDownItems.Add(new ToolStripMenuItem("&Redo", null, (_, _) => Redo(), Keys.Control | Keys.Y));

        var create = new ToolStripMenuItem("&Create");
        create.DropDownItems.Add(new ToolStripMenuItem("Room", null, (_, _) => SetTool(EditorTool.Room)));
        create.DropDownItems.Add(new ToolStripMenuItem("Ramp", null, (_, _) => SetTool(EditorTool.Ramp)));
        create.DropDownItems.Add(new ToolStripMenuItem("Stairs", null, (_, _) => SetTool(EditorTool.Stairs)));
        create.DropDownItems.Add(new ToolStripMenuItem("Door", null, (_, _) => SetTool(EditorTool.Door)));
        create.DropDownItems.Add(new ToolStripMenuItem("Player Start", null, (_, _) => SetTool(EditorTool.PlayerStart)));

        var view = new ToolStripMenuItem("&View");
        view.DropDownItems.Add(new ToolStripMenuItem("Reset 2D View", null, (_, _) => _canvas.ResetView()));
        view.DropDownItems.Add(new ToolStripMenuItem("Reset 3D View", null, (_, _) => _preview.ResetView()));

        var assets = new ToolStripMenuItem("&Assets");
        assets.DropDownItems.Add(new ToolStripMenuItem("Load Base &IWAD Materials...", null, (_, _) => _assetBrowser.LoadBaseIwad()));
        assets.DropDownItems.Add(new ToolStripMenuItem("&Import PNG Texture...", null, (_, _) => _assetBrowser.ImportCurrentCategory()));
        assets.DropDownItems.Add(new ToolStripMenuItem("&Refresh Material Browser", null, (_, _) => _assetBrowser.Reload()));
        assets.DropDownItems.Add(new ToolStripMenuItem("Open Project Asset &Folder", null, (_, _) => OpenAssetFolder()));

        var build = new ToolStripMenuItem("&Build");
        build.DropDownItems.Add(new ToolStripMenuItem("&Test Map in UZDoom", null, (_, _) => TestMap(), Keys.F5));
        build.DropDownItems.Add(new ToolStripMenuItem("Build Texture &PK3...", null, (_, _) => BuildTexturePk3()));

        menu.Items.Add(file);
        menu.Items.Add(edit);
        menu.Items.Add(create);
        menu.Items.Add(view);
        menu.Items.Add(assets);
        menu.Items.Add(build);
        return menu;
    }

    private ToolStrip BuildToolbar()
    {
        var toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
        AddToolButton(toolbar, "Select", EditorTool.Select);
        AddToolButton(toolbar, "Vertex", EditorTool.Vertex);
        AddToolButton(toolbar, "Edge", EditorTool.Edge);
        toolbar.Items.Add(new ToolStripSeparator());
        AddToolButton(toolbar, "Room", EditorTool.Room);
        AddToolButton(toolbar, "Ramp ↗", EditorTool.Ramp);
        AddToolButton(toolbar, "Stairs ▟", EditorTool.Stairs);
        AddToolButton(toolbar, "Door", EditorTool.Door);
        AddToolButton(toolbar, "Player Start", EditorTool.PlayerStart);

        toolbar.Items.Add(new ToolStripSeparator());
        _undoButton = new ToolStripButton("Undo") { ToolTipText = "Undo (Ctrl+Z)" };
        _redoButton = new ToolStripButton("Redo") { ToolTipText = "Redo (Ctrl+Y)" };
        _undoButton.Click += (_, _) => Undo();
        _redoButton.Click += (_, _) => Redo();
        toolbar.Items.Add(_undoButton);
        toolbar.Items.Add(_redoButton);

        toolbar.Items.Add(new ToolStripSeparator());
        toolbar.Items.Add(new ToolStripLabel("Grid"));
        _gridCombo = new ToolStripComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AutoSize = false, Width = 70 };
        _gridCombo.Items.AddRange(new object[] { "1", "2", "4", "8", "16", "32", "64", "128", "256" });
        _gridCombo.SelectedItem = "64";
        _gridCombo.SelectedIndexChanged += (_, _) =>
        {
            if (int.TryParse(_gridCombo.SelectedItem?.ToString(), out var grid))
            {
                _canvas.GridSize = grid;
                _status.Text = $"Grid snap: {grid} units";
            }
        };
        toolbar.Items.Add(_gridCombo);

        toolbar.Items.Add(new ToolStripSeparator());
        var importTexture = new ToolStripButton("Import Texture") { ToolTipText = "Import a PNG into the project material library" };
        importTexture.Click += (_, _) => _assetBrowser.ImportCurrentCategory();
        toolbar.Items.Add(importTexture);

        var reset3D = new ToolStripButton("Fit 3D");
        reset3D.Click += (_, _) => _preview.ResetView();
        toolbar.Items.Add(reset3D);

        var test = new ToolStripButton("▶ Test (F5)");
        test.Click += (_, _) => TestMap();
        toolbar.Items.Add(new ToolStripSeparator());
        toolbar.Items.Add(test);
        return toolbar;
    }

    private void AddToolButton(ToolStrip toolbar, string text, EditorTool tool)
    {
        var button = new ToolStripButton(text) { CheckOnClick = true, Tag = tool };
        button.Click += ToolButtonClicked;
        toolbar.Items.Add(button);
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
            EditorTool.Select => "Select: click an object or sector and drag it. Middle mouse pans the 2D view.",
            EditorTool.Vertex => "Vertex: drag corners. Double-click an edge to insert a vertex. Delete removes a selected vertex when possible.",
            EditorTool.Edge => "Edge: click an edge and drag both vertices together.",
            EditorTool.Room => "Room: drag a rectangle to create a normal flat sector.",
            EditorTool.Ramp => "Ramp: drag a rectangle in the direction it should rise. Default rise is 64; edit Start/End Height in Properties.",
            EditorTool.Stairs => "Stairs: drag in the direction they should climb. Steps are generated automatically at roughly 32-unit treads.",
            EditorTool.Door => $"Door: click a highlighted shared wall. New doors are {RampStairDesigner.DefaultDoorWidth} wide × {RampStairDesigner.DefaultDoorDepth} deep.",
            EditorTool.PlayerStart => "Player Start: click inside a sector to place the player.",
            _ => "Ready"
        };

        foreach (ToolStripItem item in _toolbar.Items)
            if (item is ToolStripButton button && button.Tag is EditorTool buttonTool)
                button.Checked = buttonTool == tool;
        _canvas.Focus();
    }

    private void UpdateMaterialTarget(object? selected)
    {
        switch (selected)
        {
            case Door:
                _assetBrowser.SetPreferredTarget(TextureCategory.Doors, "Door selected • double-click a material to apply it to the moving door face.");
                break;
            case Sector sector when sector.FloorShape == SectorFloorShape.Ramp:
                _assetBrowser.SetPreferredTarget(TextureCategory.Floors, "Ramp selected • double-click applies the material to the sloped floor.");
                break;
            case Sector:
            case VertexSelection:
            case EdgeSelection:
                _assetBrowser.SetPreferredTarget(TextureCategory.Walls, "Sector selected • use Wall / Floor / Ceiling, or double-click for Wall.");
                break;
            default:
                _assetBrowser.SetPreferredTarget(TextureCategory.Walls, "Select a sector or door, then choose where to apply the material.");
                break;
        }
    }

    private void CreateGameProject()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select or create the folder that will contain the game project.",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var root = Path.GetFullPath(dialog.SelectedPath);
            var name = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(name)) name = "UZDoomGame";
            TextureAssetLibrary.WriteDescriptor(root, name);
            _projectRoot = root;
            _projectPath = Path.Combine(root, "Maps", "MAP01.uzmap");
            var project = new EditorProject { Name = name, MapName = "MAP01" };
            ApplyProject(project, resetHistory: true, resetViews: true);
            WriteProject(_projectPath);
            ConfigureProjectAssets();
            SetTool(EditorTool.Select);
            _status.Text = $"Created game project: {root}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not create game project", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void NewProject()
    {
        _projectPath = null;
        _projectRoot = null;
        ApplyProject(new EditorProject(), resetHistory: true, resetViews: true);
        ConfigureProjectAssets();
        SetTool(EditorTool.Select);
    }

    private void ApplyProject(EditorProject project, bool resetHistory, bool resetViews)
    {
        project.Normalize();
        _project = project;
        _selectedObject = null;
        _canvas.Project = _project;
        _preview.Project = _project;
        _properties.SelectedObject = null;
        if (resetHistory) _history.Reset(_project);
        if (resetViews)
        {
            _canvas.ResetView();
            _preview.ResetView();
        }
        UpdateHistoryButtons();
        UpdateTitle();
        RefreshViews();
    }

    private void CommitEdit()
    {
        _project.Normalize();
        _history.Commit(_project);
        UpdateHistoryButtons();
        UpdateTitle();
    }

    private void Undo()
    {
        if (!_history.TryUndo(out var project)) return;
        ApplyProject(project, resetHistory: false, resetViews: false);
        _status.Text = "Undo";
    }

    private void Redo()
    {
        if (!_history.TryRedo(out var project)) return;
        ApplyProject(project, resetHistory: false, resetViews: false);
        _status.Text = "Redo";
    }

    private void UpdateHistoryButtons()
    {
        if (_undoButton is null || _redoButton is null) return;
        _undoButton.Enabled = _history.CanUndo;
        _redoButton.Enabled = _history.CanRedo;
    }

    private void RefreshViews()
    {
        _canvas.Invalidate();
        _preview.Invalidate();
        _properties.Refresh();
    }

    private void OpenProject()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "UZDoom Game Project (*.uzgame)|*.uzgame|UZDoom Map (*.uzmap)|*.uzmap|JSON (*.json)|*.json|All files (*.*)|*.*",
            Title = "Open game project or map"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            EditorProject project;
            if (string.Equals(Path.GetExtension(dialog.FileName), ".uzgame", StringComparison.OrdinalIgnoreCase))
            {
                _projectRoot = Path.GetDirectoryName(Path.GetFullPath(dialog.FileName));
                if (string.IsNullOrWhiteSpace(_projectRoot)) throw new InvalidDataException("Game project path is invalid.");
                TextureAssetLibrary.EnsureStructure(_projectRoot);
                var descriptor = JsonSerializer.Deserialize<GameProjectDescriptor>(File.ReadAllText(dialog.FileName));
                var mapsDirectory = Path.Combine(_projectRoot, "Maps");
                _projectPath = Directory.EnumerateFiles(mapsDirectory, "*.uzmap", SearchOption.TopDirectoryOnly)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();

                if (_projectPath is null)
                {
                    _projectPath = Path.Combine(mapsDirectory, "MAP01.uzmap");
                    project = new EditorProject { Name = descriptor?.Name ?? Path.GetFileName(_projectRoot), MapName = "MAP01" };
                    ApplyProject(project, resetHistory: true, resetViews: true);
                    WriteProject(_projectPath);
                }
                else
                {
                    project = JsonSerializer.Deserialize<EditorProject>(File.ReadAllText(_projectPath))
                        ?? throw new InvalidDataException("Map file was empty.");
                    ApplyProject(project, resetHistory: true, resetViews: true);
                }
            }
            else
            {
                project = JsonSerializer.Deserialize<EditorProject>(File.ReadAllText(dialog.FileName))
                    ?? throw new InvalidDataException("Project file was empty.");
                _projectPath = dialog.FileName;
                _projectRoot = TextureAssetLibrary.GetProjectRoot(_projectPath);
                ApplyProject(project, resetHistory: true, resetViews: true);
            }
            ConfigureProjectAssets();
            _status.Text = $"Opened {Path.GetFileName(_projectPath)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not open project", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveProject()
    {
        if (string.IsNullOrWhiteSpace(_projectPath)) { SaveProjectAs(); return; }
        WriteProject(_projectPath);
    }

    private void SaveProjectAs()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "UZDoom Map Editor Project (*.uzmap)|*.uzmap|JSON (*.json)|*.json",
            DefaultExt = "uzmap",
            AddExtension = true,
            FileName = string.IsNullOrWhiteSpace(_project.Name) ? "Untitled.uzmap" : $"{_project.MapName}.uzmap"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _projectPath = dialog.FileName;
        _projectRoot = TextureAssetLibrary.GetProjectRoot(_projectPath);
        if (_project.Name == "Untitled") _project.Name = Path.GetFileNameWithoutExtension(dialog.FileName);
        WriteProject(dialog.FileName);
        ConfigureProjectAssets();
    }

    private void WriteProject(string path)
    {
        _project.Normalize();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(_project, new JsonSerializerOptions { WriteIndented = true }));
        _status.Text = $"Saved {Path.GetFileName(path)}";
        UpdateTitle();
    }

    private void ConfigureProjectAssets()
    {
        if (string.IsNullOrWhiteSpace(_projectPath))
        {
            _projectRoot = null;
            _assetBrowser.SetProjectRoot(null);
            return;
        }
        _projectRoot ??= TextureAssetLibrary.GetProjectRoot(_projectPath);
        TextureAssetLibrary.EnsureStructure(_projectRoot);
        _assetBrowser.SetProjectRoot(_projectRoot);
    }

    private void ApplyTextureAsset(TextureAsset asset)
    {
        var sector = _selectedObject switch
        {
            Sector direct => direct,
            VertexSelection vertex => vertex.Sector,
            EdgeSelection edge => edge.Sector,
            _ => null
        };

        var applied = false;
        if (sector is not null)
        {
            switch (asset.Category)
            {
                case TextureCategory.Walls: sector.WallTexture = asset.Name; applied = true; break;
                case TextureCategory.Floors: sector.FloorTexture = asset.Name; applied = true; break;
                case TextureCategory.Ceilings: sector.CeilingTexture = asset.Name; applied = true; break;
            }
        }
        else if (_selectedObject is Door door)
        {
            switch (asset.Category)
            {
                case TextureCategory.Walls: door.SideTexture = asset.Name; applied = true; break;
                case TextureCategory.Floors: door.FloorTexture = asset.Name; applied = true; break;
                case TextureCategory.Ceilings: door.CeilingTexture = asset.Name; applied = true; break;
                case TextureCategory.Doors: door.DoorTexture = asset.Name; applied = true; break;
            }
        }

        if (!applied)
        {
            var target = asset.Category == TextureCategory.Doors ? "a door" : "a sector or door";
            MessageBox.Show(this, $"Select {target} in the 2D editor, then apply the material.", "Nothing compatible selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        CommitEdit();
        RefreshViews();
        _status.Text = $"Applied {asset.Name} ({asset.Category})";
    }

    private void OpenAssetFolder()
    {
        if (string.IsNullOrWhiteSpace(_projectRoot))
        {
            MessageBox.Show(this, "Save the map or create a game project first.", "Project required", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        TextureAssetLibrary.EnsureStructure(_projectRoot);
        var path = Path.Combine(_projectRoot, "Assets");
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }

    private void BuildTexturePk3()
    {
        if (string.IsNullOrWhiteSpace(_projectRoot))
        {
            MessageBox.Show(this, "Save the map or create a game project first.", "Project required", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var defaultName = string.IsNullOrWhiteSpace(_project.Name) ? "game-assets.pk3" : $"{_project.Name}-assets.pk3";
        using var dialog = new SaveFileDialog
        {
            Filter = "UZDoom PK3 (*.pk3)|*.pk3",
            DefaultExt = "pk3",
            AddExtension = true,
            InitialDirectory = Path.Combine(_projectRoot, "Build"),
            FileName = defaultName
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var count = Pk3Builder.BuildTexturePk3(_projectRoot, dialog.FileName);
            _status.Text = $"Built {Path.GetFileName(dialog.FileName)} with {count} custom texture(s)";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "PK3 build failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
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
        if (!ValidateForTest() || !TryBuildUdmf(out var text)) return;
        using var dialog = new SaveFileDialog { Filter = "Doom WAD (*.wad)|*.wad", FileName = $"{_project.MapName}.wad" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        WadWriter.WritePwad(dialog.FileName, _project.MapName, text);
        _status.Text = $"Exported WAD: {dialog.FileName}";
    }

    private void TestMap()
    {
        if (!ValidateForTest() || !TryBuildUdmf(out var text) || !ChooseUzDoom() || !ChooseIwad()) return;
        try
        {
            var testDir = Path.Combine(Path.GetTempPath(), "UzDoomMapEditor");
            Directory.CreateDirectory(testDir);
            var wadPath = Path.Combine(testDir, "editor-test.wad");
            WadWriter.WritePwad(wadPath, _project.MapName, text);

            string? pk3Path = null;
            var textureCount = 0;
            if (!string.IsNullOrWhiteSpace(_projectRoot))
            {
                textureCount = TextureAssetLibrary.Scan(_projectRoot).Count;
                if (textureCount > 0)
                {
                    pk3Path = Path.Combine(testDir, "editor-assets.pk3");
                    Pk3Builder.BuildTexturePk3(_projectRoot, pk3Path);
                }
            }

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
            if (pk3Path is not null) startInfo.ArgumentList.Add(pk3Path);
            startInfo.ArgumentList.Add("+map");
            startInfo.ArgumentList.Add(_project.MapName);
            Process.Start(startInfo);
            _status.Text = textureCount > 0
                ? $"Testing {_project.MapName} in UZDoom with {textureCount} custom texture(s)"
                : $"Testing {_project.MapName} in UZDoom";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not launch UZDoom", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private bool ValidateForTest()
    {
        if (_project.Sectors.Count == 0)
        {
            MessageBox.Show(this, "Draw at least one room/sector first.", "Nothing to test", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }
        if (_project.Things.All(t => t.Type != 1))
        {
            MessageBox.Show(this, "Place a Player Start first.", "Player start required", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }
        foreach (var door in _project.Doors)
        {
            if (CountTouchingSectors(door) < 2)
            {
                MessageBox.Show(this, $"{door.Name} is no longer connected to two sector boundaries. Move or recreate it on a shared wall.", "Door is not connected", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
        }
        return true;
    }

    private int CountTouchingSectors(Door door)
    {
        var doorVertices = door.GetVertices();
        var count = 0;
        foreach (var sector in _project.Sectors)
        {
            var touches = false;
            for (var i = 0; i < doorVertices.Count && !touches; i++)
            {
                var da = doorVertices[i];
                var db = doorVertices[(i + 1) % doorVertices.Count];
                for (var j = 0; j < sector.Vertices.Count; j++)
                {
                    var sa = sector.Vertices[j];
                    var sb = sector.Vertices[(j + 1) % sector.Vertices.Count];
                    if (SegmentsShareLength(da, db, sa, sb)) { touches = true; break; }
                }
            }
            if (touches) count++;
        }
        return count;
    }

    private static bool SegmentsShareLength(MapVertex a, MapVertex b, MapVertex c, MapVertex d)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var crossDir = (long)dx * (d.Y - c.Y) - (long)dy * (d.X - c.X);
        var crossOffset = (long)dx * (c.Y - a.Y) - (long)dy * (c.X - a.X);
        if (crossDir != 0 || crossOffset != 0) return false;
        var len2 = (double)dx * dx + (double)dy * dy;
        if (len2 <= 0) return false;
        var tc = ((c.X - a.X) * dx + (c.Y - a.Y) * dy) / len2;
        var td = ((d.X - a.X) * dx + (d.Y - a.Y) * dy) / len2;
        var overlap = Math.Min(1.0, Math.Max(tc, td)) - Math.Max(0.0, Math.Min(tc, td));
        return overlap > 0.0001;
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
        if (!string.IsNullOrWhiteSpace(_assetBrowser.BaseIwadPath) && File.Exists(_assetBrowser.BaseIwadPath))
        {
            _iwadPath = _assetBrowser.BaseIwadPath;
            return true;
        }

        using var dialog = new OpenFileDialog
        {
            Filter = "IWAD (*.wad)|*.wad|All files (*.*)|*.*",
            Title = "Choose a Doom-compatible IWAD (Freedoom is fine for testing)"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return false;
        _iwadPath = dialog.FileName;
        try { _assetBrowser.SetBaseIwad(_iwadPath); } catch { /* Testing can still proceed even if material preview fails. */ }
        return true;
    }

    private void UpdateTitle()
    {
        var name = string.IsNullOrWhiteSpace(_project.Name) ? "Untitled" : _project.Name;
        var map = string.IsNullOrWhiteSpace(_project.MapName) ? "MAP01" : _project.MapName;
        Text = $"UZDoom Map Editor - {name} / {map}";
    }
}
