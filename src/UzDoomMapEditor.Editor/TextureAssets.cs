using System.IO.Compression;
using System.Text.Json;

namespace UzDoomMapEditor.Editor;

internal enum TextureCategory
{
    Walls,
    Floors,
    Ceilings,
    Doors
}

internal sealed record TextureAsset(string Name, string FilePath, TextureCategory Category)
{
    public override string ToString() => Name;
}

internal sealed class GameProjectDescriptor
{
    public string Name { get; set; } = "Untitled";
    public int FormatVersion { get; set; } = 1;
}

internal static class TextureAssetLibrary
{
    public static string GetProjectRoot(string mapPath)
    {
        var mapDirectory = Path.GetDirectoryName(Path.GetFullPath(mapPath))
            ?? throw new InvalidOperationException("The map file has no parent directory.");

        if (string.Equals(Path.GetFileName(mapDirectory), "Maps", StringComparison.OrdinalIgnoreCase))
            return Directory.GetParent(mapDirectory)?.FullName ?? mapDirectory;

        return mapDirectory;
    }

    public static void EnsureStructure(string projectRoot)
    {
        Directory.CreateDirectory(projectRoot);
        Directory.CreateDirectory(Path.Combine(projectRoot, "Maps"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "Build"));

        foreach (TextureCategory category in Enum.GetValues<TextureCategory>())
            Directory.CreateDirectory(GetCategoryDirectory(projectRoot, category));
    }

    public static void WriteDescriptor(string projectRoot, string name)
    {
        EnsureStructure(projectRoot);
        var descriptor = new GameProjectDescriptor { Name = name };
        var json = JsonSerializer.Serialize(descriptor, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(projectRoot, "project.uzgame"), json);
    }

    public static TextureAsset ImportPng(string projectRoot, string sourcePath, TextureCategory category)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Texture file was not found.", sourcePath);

        if (!string.Equals(Path.GetExtension(sourcePath), ".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Texture import currently accepts PNG files. Convert the image to PNG first.");

        EnsureStructure(projectRoot);
        var name = MakeUniqueName(projectRoot, SanitizeTextureName(Path.GetFileNameWithoutExtension(sourcePath)));
        var destination = Path.Combine(GetCategoryDirectory(projectRoot, category), name + ".png");
        File.Copy(sourcePath, destination, overwrite: false);
        return new TextureAsset(name, destination, category);
    }

    public static IReadOnlyList<TextureAsset> Scan(string? projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot))
            return Array.Empty<TextureAsset>();

        var result = new List<TextureAsset>();
        foreach (TextureCategory category in Enum.GetValues<TextureCategory>())
        {
            var directory = GetCategoryDirectory(projectRoot, category);
            if (!Directory.Exists(directory)) continue;

            foreach (var file in Directory.EnumerateFiles(directory, "*.png", SearchOption.TopDirectoryOnly))
                result.Add(new TextureAsset(Path.GetFileNameWithoutExtension(file).ToUpperInvariant(), file, category));
        }

        return result
            .OrderBy(a => a.Category)
            .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string GetCategoryDirectory(string projectRoot, TextureCategory category)
        => Path.Combine(projectRoot, "Assets", "Textures", category.ToString());

    public static string GetPk3Namespace(TextureCategory category)
        => category is TextureCategory.Floors or TextureCategory.Ceilings ? "flats" : "textures";

    private static string SanitizeTextureName(string source)
    {
        var chars = source
            .ToUpperInvariant()
            .Where(c => char.IsLetterOrDigit(c) || c == '_')
            .Take(8)
            .ToArray();

        return chars.Length == 0 ? "TEXTURE" : new string(chars);
    }

    private static string MakeUniqueName(string projectRoot, string proposed)
    {
        var existing = Scan(projectRoot)
            .Select(a => a.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!existing.Contains(proposed)) return proposed;

        for (var i = 2; i < 10000; i++)
        {
            var suffix = i.ToString();
            var prefixLength = Math.Max(1, 8 - suffix.Length);
            var candidate = proposed[..Math.Min(prefixLength, proposed.Length)] + suffix;
            if (!existing.Contains(candidate)) return candidate;
        }

        throw new InvalidOperationException("Could not generate a unique texture name.");
    }
}

internal static class Pk3Builder
{
    public static int BuildTexturePk3(string projectRoot, string outputPath)
    {
        TextureAssetLibrary.EnsureStructure(projectRoot);
        var assets = TextureAssetLibrary.Scan(projectRoot);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        if (File.Exists(outputPath)) File.Delete(outputPath);

        using var archive = ZipFile.Open(outputPath, ZipArchiveMode.Create);
        var engineNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var asset in assets)
        {
            var enginePath = $"{TextureAssetLibrary.GetPk3Namespace(asset.Category)}/{Path.GetFileName(asset.FilePath)}";
            if (!engineNames.Add(enginePath))
                throw new InvalidOperationException($"Two project textures would both become '{enginePath}' in the PK3. Rename one of them.");

            var entry = archive.CreateEntry(enginePath, CompressionLevel.Optimal);
            using var input = File.OpenRead(asset.FilePath);
            using var output = entry.Open();
            input.CopyTo(output);
        }

        return assets.Count;
    }
}

internal sealed class TextureBrowserControl : UserControl
{
    private readonly ComboBox _category = new();
    private readonly TextBox _search = new();
    private readonly Button _import = new();
    private readonly Button _apply = new();
    private readonly Label _hint = new();
    private readonly ListView _list = new();
    private readonly ImageList _images = new();

    private string? _projectRoot;

    public event Action<TextureAsset>? ApplyRequested;
    public event Action<TextureAsset>? AssetImported;

    public TextureBrowserControl()
    {
        Dock = DockStyle.Fill;
        BackColor = DarkTheme.Panel;
        ForeColor = DarkTheme.Text;
        Padding = new Padding(6);
        AllowDrop = true;

        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 34,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = DarkTheme.Surface,
            Padding = new Padding(4, 3, 4, 2)
        };

        var title = new Label
        {
            Text = "TEXTURES",
            AutoSize = true,
            ForeColor = DarkTheme.Text,
            Margin = new Padding(3, 6, 8, 0)
        };

        _category.DropDownStyle = ComboBoxStyle.DropDownList;
        _category.Width = 110;
        _category.Items.AddRange(Enum.GetValues<TextureCategory>().Cast<object>().ToArray());
        _category.SelectedItem = TextureCategory.Walls;
        _category.SelectedIndexChanged += (_, _) => Reload();

        _search.Width = 180;
        _search.PlaceholderText = "Search textures...";
        _search.TextChanged += (_, _) => Reload();

        _import.Text = "Import PNG";
        _import.AutoSize = true;
        _import.Click += (_, _) => ImportCurrentCategory();

        _apply.Text = "Apply Selected";
        _apply.AutoSize = true;
        _apply.Enabled = false;
        _apply.Click += (_, _) => ApplySelected();

        _hint.AutoSize = true;
        _hint.ForeColor = DarkTheme.MutedText;
        _hint.Margin = new Padding(10, 6, 0, 0);

        bar.Controls.Add(title);
        bar.Controls.Add(_category);
        bar.Controls.Add(_search);
        bar.Controls.Add(_import);
        bar.Controls.Add(_apply);
        bar.Controls.Add(_hint);

        _images.ColorDepth = ColorDepth.Depth32Bit;
        _images.ImageSize = new Size(80, 80);
        _images.TransparentColor = Color.Transparent;

        _list.Dock = DockStyle.Fill;
        _list.View = View.LargeIcon;
        _list.LargeImageList = _images;
        _list.MultiSelect = false;
        _list.HideSelection = false;
        _list.BackColor = DarkTheme.Window;
        _list.ForeColor = DarkTheme.Text;
        _list.BorderStyle = BorderStyle.FixedSingle;
        _list.SelectedIndexChanged += (_, _) => _apply.Enabled = SelectedAsset is not null;
        _list.DoubleClick += (_, _) => ApplySelected();

        Controls.Add(_list);
        Controls.Add(bar);

        DragEnter += OnTextureDragEnter;
        DragDrop += OnTextureDragDrop;
        SetProjectRoot(null);
    }

    public TextureAsset? SelectedAsset
        => _list.SelectedItems.Count == 0 ? null : _list.SelectedItems[0].Tag as TextureAsset;

    public void SetProjectRoot(string? projectRoot)
    {
        _projectRoot = projectRoot;
        var enabled = !string.IsNullOrWhiteSpace(projectRoot);
        _import.Enabled = enabled;
        _list.Enabled = enabled;
        _hint.Text = enabled ? "Double-click to apply" : "Save the map or create a game project to import textures";
        Reload();
    }

    public void ImportCurrentCategory()
    {
        if (string.IsNullOrWhiteSpace(_projectRoot))
        {
            MessageBox.Show(this, "Save the map first, or create a game project. Imported textures need a project folder.", "Project required", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Filter = "PNG textures (*.png)|*.png",
            Multiselect = true,
            Title = $"Import {_category.SelectedItem ?? TextureCategory.Walls} textures"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        ImportFiles(dialog.FileNames);
    }

    public void Reload()
    {
        var selectedName = SelectedAsset?.Name;
        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            _images.Images.Clear();

            if (string.IsNullOrWhiteSpace(_projectRoot)) return;
            var category = _category.SelectedItem is TextureCategory value ? value : TextureCategory.Walls;
            var search = _search.Text.Trim();

            foreach (var asset in TextureAssetLibrary.Scan(_projectRoot)
                         .Where(a => a.Category == category)
                         .Where(a => search.Length == 0 || a.Name.Contains(search, StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    using var source = Image.FromFile(asset.FilePath);
                    var thumb = new Bitmap(_images.ImageSize.Width, _images.ImageSize.Height);
                    using (var g = Graphics.FromImage(thumb))
                    {
                        g.Clear(Color.FromArgb(18, 19, 22));
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                        var scale = Math.Min((float)thumb.Width / source.Width, (float)thumb.Height / source.Height);
                        var width = Math.Max(1, (int)(source.Width * scale));
                        var height = Math.Max(1, (int)(source.Height * scale));
                        var x = (thumb.Width - width) / 2;
                        var y = (thumb.Height - height) / 2;
                        g.DrawImage(source, new Rectangle(x, y, width, height));
                    }

                    _images.Images.Add(asset.Name, thumb);
                    var item = new ListViewItem(asset.Name, asset.Name) { Tag = asset };
                    _list.Items.Add(item);
                    if (string.Equals(asset.Name, selectedName, StringComparison.OrdinalIgnoreCase))
                        item.Selected = true;
                }
                catch
                {
                    // A broken image should not make the whole editor unusable.
                }
            }
        }
        finally
        {
            _list.EndUpdate();
            _apply.Enabled = SelectedAsset is not null;
        }
    }

    private void ApplySelected()
    {
        if (SelectedAsset is { } asset)
            ApplyRequested?.Invoke(asset);
    }

    private void ImportFiles(IEnumerable<string> files)
    {
        if (string.IsNullOrWhiteSpace(_projectRoot)) return;
        var category = _category.SelectedItem is TextureCategory value ? value : TextureCategory.Walls;
        TextureAsset? last = null;

        try
        {
            foreach (var file in files.Where(f => string.Equals(Path.GetExtension(f), ".png", StringComparison.OrdinalIgnoreCase)))
                last = TextureAssetLibrary.ImportPng(_projectRoot, file, category);

            Reload();
            if (last is not null) AssetImported?.Invoke(last);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Texture import failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnTextureDragEnter(object? sender, DragEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_projectRoot)) return;
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] files &&
            files.Any(f => string.Equals(Path.GetExtension(f), ".png", StringComparison.OrdinalIgnoreCase)))
            e.Effect = DragDropEffects.Copy;
    }

    private void OnTextureDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] files)
            ImportFiles(files);
    }
}
