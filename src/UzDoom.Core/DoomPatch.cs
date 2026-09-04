using System.Buffers.Binary;

namespace UzDoom.Core;

public sealed class DoomPatchImage
{
    public DoomPatchImage(int width, int height, int leftOffset, int topOffset, byte[] paletteIndices, bool[] opaqueMask)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (paletteIndices.Length != checked(width * height))
            throw new ArgumentException("Palette data does not match image dimensions.", nameof(paletteIndices));
        if (opaqueMask.Length != paletteIndices.Length)
            throw new ArgumentException("Opacity mask does not match image dimensions.", nameof(opaqueMask));

        Width = width;
        Height = height;
        LeftOffset = leftOffset;
        TopOffset = topOffset;
        PaletteIndices = paletteIndices;
        OpaqueMask = opaqueMask;
    }

    public int Width { get; }
    public int Height { get; }
    public int LeftOffset { get; }
    public int TopOffset { get; }
    public byte[] PaletteIndices { get; }
    public bool[] OpaqueMask { get; }

    public DoomPatchImage WithOffsets(int leftOffset, int topOffset)
        => new(Width, Height, leftOffset, topOffset, PaletteIndices.ToArray(), OpaqueMask.ToArray());
}

public static class DoomPatchCodec
{
    public static DoomPatchImage Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8)
            throw new InvalidDataException("Patch lump is too small.");

        var width = BinaryPrimitives.ReadInt16LittleEndian(data[0..2]);
        var height = BinaryPrimitives.ReadInt16LittleEndian(data[2..4]);
        var leftOffset = BinaryPrimitives.ReadInt16LittleEndian(data[4..6]);
        var topOffset = BinaryPrimitives.ReadInt16LittleEndian(data[6..8]);

        if (width <= 0 || height <= 0)
            throw new InvalidDataException("Patch has invalid dimensions.");

        var headerSize = checked(8 + width * 4);
        if (data.Length < headerSize)
            throw new InvalidDataException("Patch column table is truncated.");

        var pixels = new byte[checked(width * height)];
        var opaque = new bool[pixels.Length];

        for (var x = 0; x < width; x++)
        {
            var columnOffset = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(8 + x * 4, 4));
            if (columnOffset < headerSize || columnOffset >= data.Length)
                throw new InvalidDataException($"Patch column {x} has an invalid offset.");

            var p = columnOffset;
            var previousTop = -1;

            while (true)
            {
                if (p >= data.Length)
                    throw new InvalidDataException($"Patch column {x} has no terminator.");

                var topDelta = data[p++];
                if (topDelta == 255)
                    break;

                if (p + 2 > data.Length)
                    throw new InvalidDataException($"Patch column {x} has a truncated post header.");

                var postLength = data[p++];
                p++; // unused byte

                if (p + postLength + 1 > data.Length)
                    throw new InvalidDataException($"Patch column {x} has truncated post data.");

                var absoluteTop = topDelta;
                if (previousTop >= 0 && topDelta <= previousTop)
                    absoluteTop = previousTop + topDelta;
                previousTop = absoluteTop;

                for (var y = 0; y < postLength; y++)
                {
                    var targetY = absoluteTop + y;
                    if ((uint)targetY >= (uint)height)
                        continue;

                    var index = targetY * width + x;
                    pixels[index] = data[p + y];
                    opaque[index] = true;
                }

                p += postLength;
                p++; // trailing unused byte
            }
        }

        return new DoomPatchImage(width, height, leftOffset, topOffset, pixels, opaque);
    }

    public static byte[] Encode(DoomPatchImage image)
    {
        if (image.Width > short.MaxValue || image.Height > 255)
            throw new NotSupportedException("Classic patch export currently supports widths up to 32767 and heights up to 255 pixels.");
        if (image.LeftOffset is < short.MinValue or > short.MaxValue ||
            image.TopOffset is < short.MinValue or > short.MaxValue)
            throw new NotSupportedException("Patch offsets must fit in a signed 16-bit value.");

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write((short)image.Width);
        writer.Write((short)image.Height);
        writer.Write((short)image.LeftOffset);
        writer.Write((short)image.TopOffset);

        var columnOffsetPosition = stream.Position;
        for (var x = 0; x < image.Width; x++)
            writer.Write(0);

        var columnOffsets = new int[image.Width];
        for (var x = 0; x < image.Width; x++)
        {
            columnOffsets[x] = checked((int)stream.Position);
            var y = 0;

            while (y < image.Height)
            {
                while (y < image.Height && !image.OpaqueMask[y * image.Width + x])
                    y++;

                if (y >= image.Height)
                    break;

                var runStart = y;
                while (y < image.Height && image.OpaqueMask[y * image.Width + x] && y - runStart < 255)
                    y++;

                var runLength = y - runStart;
                if (runStart > 254)
                    throw new NotSupportedException("This image requires Doom tall-patch posts, which are not enabled in v0.1 yet.");

                writer.Write((byte)runStart);
                writer.Write((byte)runLength);
                writer.Write((byte)0);

                for (var row = runStart; row < runStart + runLength; row++)
                    writer.Write(image.PaletteIndices[row * image.Width + x]);

                writer.Write((byte)0);
            }

            writer.Write((byte)255);
        }

        var end = stream.Position;
        stream.Position = columnOffsetPosition;
        foreach (var offset in columnOffsets)
            writer.Write(offset);
        stream.Position = end;
        writer.Flush();

        return stream.ToArray();
    }
}
