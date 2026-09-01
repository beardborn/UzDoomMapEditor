using System.IO.Compression;
using System.Text.Json;

namespace UzDoomMapEditor.Editor;

// Category now means "where to apply this selected texture", not what kind of
// texture the PNG permanently is. Every imported PNG lives in one library and
// can be used on any surface.
internal enum TextureCategory
{
    Walls,
    Floors,
    Ceilings,
    Doors
}

internal sealed class TextureAsset
{
    public TextureAsset(string name, string filePath, int width, int height)
    {
        Name = name;
        FilePath = filePath;
        Width = width;
        Height = height;
    }

    public string Name { get; }
    public string FilePath { get; }
    public int Width { get; }
    public int Height { get; }
    public TextureCategory Category { get; set; } = TextureCategory.Walls;
    public string SizeText => $"{Width}×{Height}";
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
        Directory.CreateDirectory(GetTextureDirectory(projectRoot));
    }

    public static void WriteDescriptor(string projectRoot, string name)
    {
        EnsureStructure(projectRoot);
        var descriptor = new GameProjectDescriptor { Name = name };
        var json = JsonSerializer.Serialize(descriptor, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(projectRoot, "project.uzgame"), json);
    }

    public static TextureAsset ImportPng(string projectRoot, string sourcePath)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Texture file was not found.", sourcePath);

        if (!string.Equals(Path.GetExtension(sourcePath), ".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Texture import currently accepts PNG files. Convert the image to PNG first.");

        EnsureStructure(projectRoot);
        var name = MakeUniqueName(projectRoot, SanitizeTextureName(Path.GetFileNameWithoutExtension(sourcePath)));
        var destination = Path.Combine(GetTextureDirectory(projectRoot), name + ".png");
        File.Copy(sourcePath, destination, overwrite: false);

        using var image = Image.FromFile(destination);
        return new TextureAsset(name, destination, image.Width, image.Height);
    }

    public static IReadOnlyList<TextureAsset> Scan(string? projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot))
            return Array.Empty<TextureAsset>();

        var root = GetTextureDirectory(projectRoot);
        if (!Directory.Exists(root)) return Array.Empty<TextureAsset>();

        var result = new List<TextureAsset>();
        foreach (var file in Directory.EnumerateFiles(root, "*.png", SearchOption.AllDirectories))
        {
            try
            {
                using var image = Image.FromFile(file);
                result.Add(new TextureAsset(
                    Path.GetFileNameWithoutExtension(file).ToUpperInvariant(),
                    file,
                    image.Width,
                    image.Height));
            }
            catch
            {
                // A broken PNG should not take the editor down with it.
            }
        }

        return result
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string GetTextureDirectory(string projectRoot)
        => Path.Combine(projectRoot, "Assets", "Textures");

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
            // One modern texture namespace. UZDoom can reference these names on
            // walls, floors and ceilings, so the artist never has to categorise
            // the image before using it.
            var enginePath = $"textures/{asset.Name}.png";
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
    private readonly TextBox _search = new();
    private readonly Button _import = new();
    private readonly Button _wall = new();
    private readonly Button _floor = new();
    private readonly Button _ceiling = new();
    private readonly Button _door = new();
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
            Height = 40,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = DarkTheme.Surface,
            Padding = new Padding(4, 3, 4, 2)
        };

        var title = new Label
        {
            Text = "PROJECT TEXTURES",
            AutoSize = true,
            ForeColor = DarkTheme.Text,
            Margin = new Padding(3, 7, 8, 0)
        };

        _search.Width = 170;
        _search.PlaceholderText = "Search textures...";
        _search.TextChanged += (_, _) => Reload();

        ConfigureButton(_import, "Import PNG", (_, _) => ImportTextures(), selectedOnly: false);
        ConfigureButton(_wall, "Apply Wall", (_, _) => ApplySelected(TextureCategory.Walls));
        ConfigureButton(_floor, "Apply Floor", (_, _) => ApplySelected(TextureCategory.Floors));
        ConfigureButton(_ceiling, "Apply Ceiling", (_, _) => ApplySelected(TextureCategory.Ceilings));
        ConfigureButton(_door, "Apply Door", (_, _) => ApplySelected(TextureCategory.Doors));

        _hint.AutoSize = true;
        _hint.ForeColor = DarkTheme.MutedText;
        _hint.Margin = new Padding(10, 7, 0, 0);

        bar.Controls.Add(title);
        bar.Controls.Add(_search);
        bar.Controls.Add(_import);
        bar.Controls.Add(_wall);
        bar.Controls.Add(_floor);
        bar.Controls.Add(_ceiling);
        bar.Controls.Add(_door);
        bar.Controls.Add(_hint);

        _images.ColorDepth = ColorDepth.Depth32Bit;
        _images.ImageSize = new Size(88, 88);
        _images.TransparentColor = Color.Transparent;

        _list.Dock = DockStyle.Fill;
        _list.View = View.LargeIcon;
        _list.LargeImageList = _images;
        _list.MultiSelect = false;
        _list.HideSelection = false;
        _list.ShowItemToolTips = true;
        _list.BackColor = DarkTheme.Window;
        _list.ForeColor = DarkTheme.Text;
        _list.BorderStyle = BorderStyle.FixedSingle;
        _list.SelectedIndexChanged += (_, _) => UpdateSelectionState();
        _list.DoubleClick += (_, _) => ApplySelected(TextureCategory.Walls);

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
        _hint.Text = enabled
            ? "Any PNG works • 64/128/256 px are handy retro sizes • exact size shown below each thumbnail"
            : "Save the map or create a game project to import textures";
        Reload();
    }

    // Kept so the existing menu and toolbar need no special category logic.
    public void ImportCurrentCategory() => ImportTextures();

    public void ImportTextures()
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
            Title = "Import project textures"
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
            var search = _search.Text.Trim();

            foreach (var asset in TextureAssetLibrary.Scan(_projectRoot)
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
                    var item = new ListViewItem($"{asset.Name}\n{asset.SizeText}", asset.Name)
                    {
                        Tag = asset,
                        ToolTipText = $"{asset.Name} • {asset.SizeText} PNG"
                    };
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
            UpdateSelectionState();
        }
    }

    private static void ConfigureButton(Button button, string text, EventHandler click, bool selectedOnly = true)
    {
        button.Text = text;
        button.AutoSize = true;
        button.Enabled = !selectedOnly;
        button.Click += click;
    }

    private void UpdateSelectionState()
    {
        var selected = SelectedAsset;
        var enabled = selected is not null;
        _wall.Enabled = enabled;
        _floor.Enabled = enabled;
        _ceiling.Enabled = enabled;
        _door.Enabled = enabled;

        if (selected is not null)
            _hint.Text = $"{selected.Name} • {selected.SizeText} • choose Wall / Floor / Ceiling / Door";
        else if (!string.IsNullOrWhiteSpace(_projectRoot))
            _hint.Text = "Any PNG works • 64/128/256 px are handy retro sizes • exact size shown below each thumbnail";
    }

    private void ApplySelected(TextureCategory target)
    {
        if (SelectedAsset is not { } asset) return;
        asset.Category = target;
        ApplyRequested?.Invoke(asset);
    }

    private void ImportFiles(IEnumerable<string> files)
    {
        if (string.IsNullOrWhiteSpace(_projectRoot)) return;
        TextureAsset? last = null;

        try
        {
            foreach (var file in files.Where(f => string.Equals(Path.GetExtension(f), ".png", StringComparison.OrdinalIgnoreCase)))
                last = TextureAssetLibrary.ImportPng(_projectRoot, file);

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
