using System.Drawing.Imaging;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using UzDoom.Core;

namespace UzDoomMapEditor.Editor;

// Category means where to apply the selected image, not what kind of image it is.
// Project PNGs, IWAD wall textures and IWAD flats all live in one visual browser.
internal enum TextureCategory
{
    Walls,
    Floors,
    Ceilings,
    Doors
}

internal enum TextureAssetSource
{
    Project,
    IwadTexture,
    IwadFlat
}

internal sealed class TextureAsset
{
    public TextureAsset(string name, string filePath, int width, int height)
    {
        Name = name;
        FilePath = filePath;
        Width = width;
        Height = height;
        Source = TextureAssetSource.Project;
    }

    public TextureAsset(DoomMaterialImage material)
    {
        Name = material.Name;
        Width = material.Width;
        Height = material.Height;
        WadMaterial = material;
        Source = material.Kind == DoomMaterialKind.Flat
            ? TextureAssetSource.IwadFlat
            : TextureAssetSource.IwadTexture;
    }

    public string Name { get; }
    public string? FilePath { get; }
    public int Width { get; }
    public int Height { get; }
    public DoomMaterialImage? WadMaterial { get; }
    public TextureAssetSource Source { get; }
    public TextureCategory Category { get; set; } = TextureCategory.Walls;
    public string SizeText => $"{Width}×{Height}";
    public string SourceText => Source switch
    {
        TextureAssetSource.Project => "Project PNG",
        TextureAssetSource.IwadTexture => "IWAD texture",
        TextureAssetSource.IwadFlat => "IWAD flat",
        _ => "Texture"
    };
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
            var enginePath = $"textures/{asset.Name}.png";
            if (!engineNames.Add(enginePath))
                throw new InvalidOperationException($"Two project textures would both become '{enginePath}' in the PK3. Rename one of them.");

            var entry = archive.CreateEntry(enginePath, CompressionLevel.Optimal);
            using var input = File.OpenRead(asset.FilePath!);
            using var output = entry.Open();
            input.CopyTo(output);
        }

        return assets.Count;
    }
}

internal sealed class TextureBrowserControl : UserControl
{
    private readonly TextBox _search = new();
    private readonly ComboBox _sourceFilter = new();
    private readonly Button _loadIwad = new();
    private readonly Button _import = new();
    private readonly Button _wall = new();
    private readonly Button _floor = new();
    private readonly Button _ceiling = new();
    private readonly Button _door = new();
    private readonly Label _hint = new();
    private readonly ListView _list = new();
    private readonly ImageList _images = new();
    private readonly List<TextureAsset> _iwadAssets = new();

    private string? _projectRoot;
    private string? _iwadPath;
    private DoomPalette? _iwadPalette;
    private TextureCategory _preferredTarget = TextureCategory.Walls;
    private string _selectionHint = "Select a sector or door, then choose where to apply the texture.";

    public event Action<TextureAsset>? ApplyRequested;
    public event Action<TextureAsset>? AssetImported;
    public event Action<string>? BaseIwadChanged;

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
            Height = 42,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = DarkTheme.Surface,
            Padding = new Padding(4, 3, 4, 2)
        };

        var title = new Label
        {
            Text = "MATERIALS",
            AutoSize = true,
            ForeColor = DarkTheme.Text,
            Margin = new Padding(3, 7, 8, 0)
        };

        _search.Width = 155;
        _search.PlaceholderText = "Search...";
        _search.TextChanged += (_, _) => Reload();

        _sourceFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        _sourceFilter.Width = 120;
        _sourceFilter.Items.AddRange(new object[] { "All", "Project", "IWAD textures", "IWAD flats" });
        _sourceFilter.SelectedIndex = 0;
        _sourceFilter.SelectedIndexChanged += (_, _) => Reload();

        ConfigureButton(_loadIwad, "Load IWAD", (_, _) => LoadBaseIwad(), selectedOnly: false);
        ConfigureButton(_import, "Import PNG", (_, _) => ImportTextures(), selectedOnly: false);
        ConfigureButton(_wall, "Wall", (_, _) => ApplySelected(TextureCategory.Walls));
        ConfigureButton(_floor, "Floor", (_, _) => ApplySelected(TextureCategory.Floors));
        ConfigureButton(_ceiling, "Ceiling", (_, _) => ApplySelected(TextureCategory.Ceilings));
        ConfigureButton(_door, "Door", (_, _) => ApplySelected(TextureCategory.Doors));

        _hint.AutoSize = true;
        _hint.ForeColor = DarkTheme.MutedText;
        _hint.Margin = new Padding(10, 7, 0, 0);

        bar.Controls.Add(title);
        bar.Controls.Add(_search);
        bar.Controls.Add(_sourceFilter);
        bar.Controls.Add(_loadIwad);
        bar.Controls.Add(_import);
        bar.Controls.Add(_wall);
        bar.Controls.Add(_floor);
        bar.Controls.Add(_ceiling);
        bar.Controls.Add(_door);
        bar.Controls.Add(_hint);

        _images.ColorDepth = ColorDepth.Depth32Bit;
        _images.ImageSize = new Size(96, 96);
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
        _list.DoubleClick += (_, _) => ApplySelected(_preferredTarget);

        Controls.Add(_list);
        Controls.Add(bar);

        DragEnter += OnTextureDragEnter;
        DragDrop += OnTextureDragDrop;
        SetProjectRoot(null);
        SetPreferredTarget(TextureCategory.Walls, _selectionHint);
    }

    public TextureAsset? SelectedAsset
        => _list.SelectedItems.Count == 0 ? null : _list.SelectedItems[0].Tag as TextureAsset;

    public string? BaseIwadPath => _iwadPath;

    public void SetProjectRoot(string? projectRoot)
    {
        _projectRoot = projectRoot;
        _import.Enabled = !string.IsNullOrWhiteSpace(projectRoot);
        Reload();
    }

    public void SetPreferredTarget(TextureCategory target, string selectionHint)
    {
        _preferredTarget = target;
        _selectionHint = selectionHint;

        foreach (var button in new[] { _wall, _floor, _ceiling, _door })
            button.BackColor = DarkTheme.Surface;

        PreferredButton(target).BackColor = Color.FromArgb(66, 83, 116);
        UpdateSelectionState();
    }

    public void SetBaseIwad(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            _iwadPath = null;
            _iwadPalette = null;
            _iwadAssets.Clear();
            Reload();
            return;
        }

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("IWAD file was not found.", fullPath);

        var wad = WadFile.Open(fullPath);
        var playpal = wad.FindFirst("PLAYPAL")
            ?? throw new InvalidDataException("The selected IWAD has no PLAYPAL lump, so its materials cannot be previewed.");

        _iwadPalette = DoomPalette.FromPlaypal(playpal.Data.Span);
        _iwadAssets.Clear();
        _iwadAssets.AddRange(DoomMaterialCatalog.Load(wad).Select(material => new TextureAsset(material)));
        _iwadPath = fullPath;
        Reload();
    }

    public void LoadBaseIwad()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Doom IWAD/PWAD (*.wad)|*.wad|All files (*.*)|*.*",
            Title = "Load base IWAD materials"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            SetBaseIwad(dialog.FileName);
            BaseIwadChanged?.Invoke(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not load IWAD materials", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // Kept so the existing menu and toolbar need no category-specific import logic.
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
        var selectedSource = SelectedAsset?.Source;
        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            _images.Images.Clear();

            var search = _search.Text.Trim();
            var assets = CombinedAssets()
                .Where(MatchesSourceFilter)
                .Where(a => search.Length == 0 || a.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
                .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(a => a.Source)
                .ToList();

            var imageIndex = 0;
            foreach (var asset in assets)
            {
                try
                {
                    using var thumb = CreateThumbnail(asset);
                    var key = $"{imageIndex++}:{asset.Source}:{asset.Name}";
                    _images.Images.Add(key, (Image)thumb.Clone());
                    var item = new ListViewItem($"{asset.Name}\n{asset.SizeText}\n{asset.SourceText}", key)
                    {
                        Tag = asset,
                        ToolTipText = $"{asset.Name} • {asset.SizeText} • {asset.SourceText}"
                    };
                    _list.Items.Add(item);
                    if (string.Equals(asset.Name, selectedName, StringComparison.OrdinalIgnoreCase) && asset.Source == selectedSource)
                        item.Selected = true;
                }
                catch
                {
                    // A broken source image should not make the whole browser unusable.
                }
            }
        }
        finally
        {
            _list.EndUpdate();
            UpdateSelectionState();
        }
    }

    private IEnumerable<TextureAsset> CombinedAssets()
    {
        // Project images intentionally come first. If they use the same name as an
        // IWAD material, the PK3 will override it in game and that is the preview
        // the mapper normally wants to see.
        var project = TextureAssetLibrary.Scan(_projectRoot);
        var projectNames = project.Select(a => a.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var asset in project)
            yield return asset;

        foreach (var asset in _iwadAssets)
            if (!projectNames.Contains(asset.Name))
                yield return asset;
    }

    private bool MatchesSourceFilter(TextureAsset asset)
    {
        return _sourceFilter.SelectedIndex switch
        {
            1 => asset.Source == TextureAssetSource.Project,
            2 => asset.Source == TextureAssetSource.IwadTexture,
            3 => asset.Source == TextureAssetSource.IwadFlat,
            _ => true
        };
    }

    private Bitmap CreateThumbnail(TextureAsset asset)
    {
        using var source = asset.Source == TextureAssetSource.Project
            ? new Bitmap(asset.FilePath!)
            : CreateIwadBitmap(asset);

        var thumb = new Bitmap(_images.ImageSize.Width, _images.ImageSize.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(thumb);
        g.Clear(Color.FromArgb(18, 19, 22));
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;

        var scale = Math.Min((float)thumb.Width / source.Width, (float)thumb.Height / source.Height);
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var x = (thumb.Width - width) / 2;
        var y = (thumb.Height - height) / 2;
        g.DrawImage(source, new Rectangle(x, y, width, height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel);
        return thumb;
    }

    private Bitmap CreateIwadBitmap(TextureAsset asset)
    {
        if (asset.WadMaterial is not { } material || _iwadPalette is null)
            throw new InvalidOperationException("IWAD material preview is unavailable.");

        var bitmap = new Bitmap(material.Width, material.Height, PixelFormat.Format32bppArgb);
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var bits = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[checked(Math.Abs(bits.Stride) * bitmap.Height)];
            for (var y = 0; y < material.Height; y++)
            {
                var row = y * Math.Abs(bits.Stride);
                for (var x = 0; x < material.Width; x++)
                {
                    var sourceIndex = y * material.Width + x;
                    if (!material.OpaqueMask[sourceIndex])
                        continue;

                    var colour = _iwadPalette.Colors[material.PaletteIndices[sourceIndex]];
                    var p = row + x * 4;
                    bytes[p] = colour.B;
                    bytes[p + 1] = colour.G;
                    bytes[p + 2] = colour.R;
                    bytes[p + 3] = 255;
                }
            }
            Marshal.Copy(bytes, 0, bits.Scan0, bytes.Length);
        }
        finally
        {
            bitmap.UnlockBits(bits);
        }

        return bitmap;
    }

    private static void ConfigureButton(Button button, string text, EventHandler click, bool selectedOnly = true)
    {
        button.Text = text;
        button.AutoSize = true;
        button.Enabled = !selectedOnly;
        button.FlatStyle = FlatStyle.Flat;
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
        _list.Enabled = _iwadAssets.Count > 0 || !string.IsNullOrWhiteSpace(_projectRoot);

        if (selected is not null)
        {
            _hint.Text = $"{selected.Name} • {selected.SizeText} • {selected.SourceText} • double-click = {TargetText(_preferredTarget)}";
        }
        else if (_iwadAssets.Count > 0)
        {
            _hint.Text = $"{_iwadAssets.Count:N0} IWAD materials loaded • {_selectionHint}";
        }
        else if (!string.IsNullOrWhiteSpace(_projectRoot))
        {
            _hint.Text = "Import PNGs or load the base IWAD to browse its textures visually.";
        }
        else
        {
            _hint.Text = "Load an IWAD to browse built-in materials, or create/save a project to import PNGs.";
        }
    }

    private Button PreferredButton(TextureCategory target) => target switch
    {
        TextureCategory.Floors => _floor,
        TextureCategory.Ceilings => _ceiling,
        TextureCategory.Doors => _door,
        _ => _wall
    };

    private static string TargetText(TextureCategory target) => target switch
    {
        TextureCategory.Floors => "apply to floor",
        TextureCategory.Ceilings => "apply to ceiling",
        TextureCategory.Doors => "apply to door face",
        _ => "apply to wall"
    };

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
