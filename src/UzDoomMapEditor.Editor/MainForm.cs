using System.Diagnostics;
using System.Text.Json;
using UzDoomMapEditor.Core;

namespace UzDoomMapEditor.Editor;

public sealed class MainForm : Form
{
    private readonly MapCanvas _canvas = new();
    private readonly Map3DPreview _preview = new();
    private readonly PropertyGrid _properties = new() { Dock = DockStyle.Fill, HelpVisible = true, ToolbarVisible = false };
    private readonly ToolStripStatusLabel _status = new("Ready");
    private readonly EditorHistory _history = new();

    private ToolStrip _toolbar = null!;
    private ToolStripButton _undoButton = null!;
    private ToolStripButton _redoButton = null!;
    private ToolStripComboBox _gridCombo = null!;

    private EditorProject _project = new();
    private string? _projectPath;
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
            SplitterDistance = 500
        };
        workspace.Panel1.Controls.Add(_canvas);
        workspace.Panel2.Controls.Add(_preview);
        _canvas.Dock = DockStyle.Fill;
        _preview.Dock = DockStyle.Fill;

        var outer = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 1160,
            FixedPanel = FixedPanel.Panel2
        };
        outer.Panel1.Controls.Add(workspace);
        outer.Panel2.Controls.Add(_properties);

        Controls.Add(outer);
        Controls.Add(statusStrip);
        Controls.Add(_toolbar);
        Controls.Add(menu);
        MainMenuStrip = menu;

        _canvas.SelectionChanged += selected =>
        {
            _properties.SelectedObject = selected;
            _properties.Refresh();
        };
        _canvas.ProjectEdited += CommitEdit;
        _canvas.ProjectPreviewChanged += RefreshViews;

        _properties.PropertyValueChanged += (_, _) =>
        {
            CommitEdit();
            RefreshViews();
        };

        ApplyProject(new EditorProject(), resetHistory: true, resetViews: true);
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

        var edit = new ToolStripMenuItem("&Edit");
        edit.DropDownItems.Add(new ToolStripMenuItem("&Undo", null, (_, _) => Undo(), Keys.Control | Keys.Z));
        edit.DropDownItems.Add(new ToolStripMenuItem("&Redo", null, (_, _) => Redo(), Keys.Control | Keys.Y));

        var view = new ToolStripMenuItem("&View");
        view.DropDownItems.Add(new ToolStripMenuItem("Reset 2D View", null, (_, _) => _canvas.ResetView()));
        view.DropDownItems.Add(new ToolStripMenuItem("Reset 3D View", null, (_, _) => _preview.ResetView()));

        var build = new ToolStripMenuItem("&Build");
        build.DropDownItems.Add(new ToolStripMenuItem("&Test Map in UZDoom", null, (_, _) => TestMap(), Keys.F5));

        menu.Items.Add(file);
        menu.Items.Add(edit);
        menu.Items.Add(view);
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
        _gridCombo = new ToolStripComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            AutoSize = false,
            Width = 70
        };
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
            EditorTool.Vertex => "Vertex: drag corners. Double-click an edge to insert a new vertex. Delete removes a selected vertex when possible.",
            EditorTool.Edge => "Edge: click an edge and drag both of its vertices together.",
            EditorTool.Room => "Room: drag a rectangle. It becomes a real editable polygon sector.",
            EditorTool.Door => "Door: drag a connector across the gap between two sector edges.",
            EditorTool.PlayerStart => "Player Start: click inside a sector to place the player.",
            _ => "Ready"
        };

        foreach (ToolStripItem item in _toolbar.Items)
        {
            if (item is ToolStripButton button && button.Tag is EditorTool buttonTool)
                button.Checked = buttonTool == tool;
        }

        _canvas.Focus();
    }

    private void NewProject()
    {
        _projectPath = null;
        ApplyProject(new EditorProject(), resetHistory: true, resetViews: true);
        SetTool(EditorTool.Select);
    }

    private void ApplyProject(EditorProject project, bool resetHistory, bool resetViews)
    {
        project.Normalize();
        _project = project;
        _canvas.Project = _project;
        _preview.Project = _project;
        _properties.SelectedObject = null;

        if (resetHistory)
            _history.Reset(_project);

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
            Filter = "UZDoom Map Editor Project (*.uzmap)|*.uzmap|JSON (*.json)|*.json|All files (*.*)|*.*",
            Title = "Open project"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var json = File.ReadAllText(dialog.FileName);
            var project = JsonSerializer.Deserialize<EditorProject>(json) ?? throw new InvalidDataException("Project file was empty.");
            _projectPath = dialog.FileName;
            ApplyProject(project, resetHistory: true, resetViews: true);
            _status.Text = $"Opened {Path.GetFileName(dialog.FileName)}";
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
        _project.Normalize();
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
                MessageBox.Show(
                    this,
                    $"{door.Name} is not touching two sector edges. Leave a gap between two rooms and place the door so opposite sides touch the sector boundaries.",
                    "Door is not connected",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
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
                    if (SegmentsShareLength(da, db, sa, sb))
                    {
                        touches = true;
                        break;
                    }
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
