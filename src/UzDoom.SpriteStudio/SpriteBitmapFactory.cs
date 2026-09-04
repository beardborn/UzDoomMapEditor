using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using UzDoom.Core;

namespace UzDoom.SpriteStudio;

internal static class SpriteBitmapFactory
{
    public static Bitmap ToBitmap(DoomPatchImage image, DoomPalette palette)
    {
        var bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);
        var rect = new Rectangle(0, 0, image.Width, image.Height);
        var bits = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        try
        {
            var bytes = new byte[checked(Math.Abs(bits.Stride) * image.Height)];
            for (var y = 0; y < image.Height; y++)
            {
                var targetRow = bits.Stride >= 0 ? y : image.Height - 1 - y;
                var rowStart = targetRow * Math.Abs(bits.Stride);

                for (var x = 0; x < image.Width; x++)
                {
                    var source = y * image.Width + x;
                    var target = rowStart + x * 4;
                    if (!image.OpaqueMask[source])
                    {
                        bytes[target + 3] = 0;
                        continue;
                    }

                    var color = palette.Colors[image.PaletteIndices[source]];
                    bytes[target] = color.B;
                    bytes[target + 1] = color.G;
                    bytes[target + 2] = color.R;
                    bytes[target + 3] = 255;
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

    public static Bitmap CreateThumbnail(Bitmap source, int width, int height)
    {
        var thumbnail = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(thumbnail);
        graphics.Clear(Color.FromArgb(36, 38, 43));
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;

        var scale = Math.Min((float)(width - 8) / source.Width, (float)(height - 8) / source.Height);
        if (scale > 1f)
            scale = Math.Max(1f, (float)Math.Floor(scale));

        var drawWidth = Math.Max(1, (int)Math.Round(source.Width * scale));
        var drawHeight = Math.Max(1, (int)Math.Round(source.Height * scale));
        var x = (width - drawWidth) / 2;
        var y = (height - drawHeight) / 2;
        graphics.DrawImage(source, new Rectangle(x, y, drawWidth, drawHeight), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel);
        return thumbnail;
    }

    public static DoomPatchImage FromBitmap(Bitmap bitmap, DoomPalette palette, int leftOffset, int topOffset)
    {
        if (bitmap.Width <= 0 || bitmap.Height <= 0)
            throw new InvalidDataException("Image has invalid dimensions.");
        if (bitmap.Height > 255)
            throw new NotSupportedException("v0.1 classic patch import supports sprites up to 255 pixels tall.");
        if (bitmap.Width > short.MaxValue)
            throw new NotSupportedException("Image is too wide for a classic Doom patch.");

        var indices = new byte[checked(bitmap.Width * bitmap.Height)];
        var opaque = new bool[indices.Length];

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                var index = y * bitmap.Width + x;
                if (color.A < 128)
                    continue;

                opaque[index] = true;
                indices[index] = (byte)palette.FindNearestIndex(color.R, color.G, color.B);
            }
        }

        return new DoomPatchImage(bitmap.Width, bitmap.Height, leftOffset, topOffset, indices, opaque);
    }
}
