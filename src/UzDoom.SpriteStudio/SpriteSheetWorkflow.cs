using System.Drawing.Imaging;
using UzDoom.Core;

namespace UzDoom.SpriteStudio;

internal sealed record SpriteSheetItem(string Name, DoomPatchImage Image);
internal sealed record SpriteSheetReplacement(string Name, Bitmap Bitmap);

internal static class SpriteSheetWorkflow
{
    private const int CellPadding = 16;

    public static string ManifestPathFor(string pngPath)
        => Path.Combine(Path.GetDirectoryName(pngPath) ?? string.Empty, Path.GetFileNameWithoutExtension(pngPath) + ".csv");

    public static void Export(string family, IReadOnlyList<SpriteSheetItem> items, DoomPalette palette, string pngPath)
    {
        if (items.Count == 0)
            throw new InvalidOperationException("The selected family has no sprites to export.");

        var columns = Math.Min(8, Math.Max(1, items.Count));
        var rows = (items.Count + columns - 1) / columns;
        var cellWidth = items.Max(i => i.Image.Width) + CellPadding * 2;
        var cellHeight = items.Max(i => i.Image.Height) + CellPadding * 2;

        using var sheet = new Bitmap(checked(columns * cellWidth), checked(rows * cellHeight), PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(sheet))
        {
            graphics.Clear(Color.Transparent);
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;

            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var col = i % columns;
                var row = i / columns;
                var cellX = col * cellWidth;
                var cellY = row * cellHeight;
                using var sprite = SpriteBitmapFactory.ToBitmap(item.Image, palette);
                var x = cellX + (cellWidth - sprite.Width) / 2;
                var y = cellY + (cellHeight - sprite.Height) / 2;
                graphics.DrawImage(sprite, new Rectangle(x, y, sprite.Width, sprite.Height), 0, 0, sprite.Width, sprite.Height, GraphicsUnit.Pixel);
            }
        }

        sheet.Save(pngPath, ImageFormat.Png);

        var manifest = new List<string>
        {
            "Name,CellX,CellY,CellWidth,CellHeight,OriginalWidth,OriginalHeight,LeftOffset,TopOffset"
        };
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var col = i % columns;
            var row = i / columns;
            manifest.Add(string.Join(',',
                item.Name,
                col * cellWidth,
                row * cellHeight,
                cellWidth,
                cellHeight,
                item.Image.Width,
                item.Image.Height,
                item.Image.LeftOffset,
                item.Image.TopOffset));
        }

        File.WriteAllLines(ManifestPathFor(pngPath), manifest);
    }

    public static List<SpriteSheetReplacement> Import(string pngPath)
    {
        var manifestPath = ManifestPathFor(pngPath);
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("The sprite-sheet manifest CSV was not found. Keep the PNG and CSV together when editing a sheet.", manifestPath);

        using var sheet = new Bitmap(pngPath);
        var lines = File.ReadAllLines(manifestPath);
        if (lines.Length < 2)
            throw new InvalidDataException("The sprite-sheet manifest is empty.");

        var result = new List<SpriteSheetReplacement>();
        try
        {
            foreach (var line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var parts = line.Split(',');
                if (parts.Length < 5 ||
                    !int.TryParse(parts[1], out var cellX) ||
                    !int.TryParse(parts[2], out var cellY) ||
                    !int.TryParse(parts[3], out var cellWidth) ||
                    !int.TryParse(parts[4], out var cellHeight))
                    throw new InvalidDataException($"Invalid sprite-sheet manifest row: {line}");

                var cell = Rectangle.Intersect(new Rectangle(cellX, cellY, cellWidth, cellHeight), new Rectangle(0, 0, sheet.Width, sheet.Height));
                if (cell.Width <= 0 || cell.Height <= 0)
                    throw new InvalidDataException($"Sprite cell for {parts[0]} lies outside the PNG.");

                var opaqueBounds = FindOpaqueBounds(sheet, cell);
                if (opaqueBounds is null)
                    continue;

                var bounds = opaqueBounds.Value;
                var sprite = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
                using (var graphics = Graphics.FromImage(sprite))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.DrawImage(sheet, new Rectangle(0, 0, bounds.Width, bounds.Height), bounds, GraphicsUnit.Pixel);
                }
                result.Add(new SpriteSheetReplacement(parts[0].Trim().ToUpperInvariant(), sprite));
            }

            return result;
        }
        catch
        {
            foreach (var replacement in result)
                replacement.Bitmap.Dispose();
            throw;
        }
    }

    private static Rectangle? FindOpaqueBounds(Bitmap bitmap, Rectangle area)
    {
        var minX = area.Right;
        var minY = area.Bottom;
        var maxX = -1;
        var maxY = -1;

        for (var y = area.Top; y < area.Bottom; y++)
        {
            for (var x = area.Left; x < area.Right; x++)
            {
                if (bitmap.GetPixel(x, y).A < 128)
                    continue;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        return maxX < minX || maxY < minY
            ? null
            : Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
    }
}
