using System.Buffers.Binary;
using System.Text;

namespace UzDoom.Core;

public enum DoomMaterialKind
{
    Texture,
    Flat
}

public sealed record DoomMaterialImage(
    string Name,
    int Width,
    int Height,
    byte[] PaletteIndices,
    bool[] OpaqueMask,
    DoomMaterialKind Kind);

public static class DoomMaterialCatalog
{
    public static IReadOnlyList<DoomMaterialImage> Load(WadFile wad)
    {
        ArgumentNullException.ThrowIfNull(wad);

        var materials = new List<DoomMaterialImage>();
        var patchNames = ReadPatchNames(wad.FindLast("PNAMES"));

        if (patchNames.Count > 0)
        {
            AddTextureLump(wad, wad.FindLast("TEXTURE1"), patchNames, materials);
            AddTextureLump(wad, wad.FindLast("TEXTURE2"), patchNames, materials);
        }

        AddFlats(wad, materials);

        return materials
            .GroupBy(m => (m.Kind, Name: m.Name), MaterialKeyComparer.Instance)
            .Select(group => group.Last())
            .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Kind)
            .ToArray();
    }

    private static IReadOnlyList<string> ReadPatchNames(WadLump? pnames)
    {
        if (pnames is null || pnames.Data.Length < 4)
            return Array.Empty<string>();

        var data = pnames.Data.Span;
        var count = BinaryPrimitives.ReadInt32LittleEndian(data);
        if (count < 0 || data.Length < 4L + count * 8L)
            throw new InvalidDataException("PNAMES lump is truncated or invalid.");

        var names = new string[count];
        for (var i = 0; i < count; i++)
            names[i] = ReadName(data.Slice(4 + i * 8, 8));
        return names;
    }

    private static void AddTextureLump(
        WadFile wad,
        WadLump? textureLump,
        IReadOnlyList<string> patchNames,
        List<DoomMaterialImage> output)
    {
        if (textureLump is null || textureLump.Data.Length < 4)
            return;

        var data = textureLump.Data.Span;
        var count = BinaryPrimitives.ReadInt32LittleEndian(data);
        if (count < 0 || data.Length < 4L + count * 4L)
            throw new InvalidDataException($"{textureLump.Name} lump is truncated or invalid.");

        for (var i = 0; i < count; i++)
        {
            var offset = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(4 + i * 4, 4));
            if (offset < 0 || offset + 22 > data.Length)
                continue;

            try
            {
                var material = DecodeTexture(wad, data, offset, patchNames);
                if (material is not null)
                    output.Add(material);
            }
            catch (InvalidDataException)
            {
                // One malformed texture definition should not hide the rest of the IWAD browser.
            }
        }
    }

    private static DoomMaterialImage? DecodeTexture(
        WadFile wad,
        ReadOnlySpan<byte> data,
        int offset,
        IReadOnlyList<string> patchNames)
    {
        var name = ReadName(data.Slice(offset, 8));
        if (name.Length == 0)
            return null;

        var width = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset + 12, 2));
        var height = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset + 14, 2));
        var patchCount = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset + 20, 2));

        if (width <= 0 || height <= 0 || patchCount < 0 || width > 4096 || height > 4096)
            return null;

        var patchTable = offset + 22;
        if (patchTable + patchCount * 10L > data.Length)
            throw new InvalidDataException($"Texture {name} has a truncated patch table.");

        var pixelCount = checked(width * height);
        var pixels = new byte[pixelCount];
        var opaque = new bool[pixelCount];

        for (var p = 0; p < patchCount; p++)
        {
            var entry = patchTable + p * 10;
            var originX = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(entry, 2));
            var originY = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(entry + 2, 2));
            var patchIndex = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(entry + 4, 2));

            if (patchIndex < 0 || patchIndex >= patchNames.Count)
                continue;

            var patchLump = wad.FindLast(patchNames[patchIndex]);
            if (patchLump is null || patchLump.Data.Length < 8)
                continue;

            DoomPatchImage patch;
            try
            {
                patch = DoomPatchCodec.Decode(patchLump.Data.Span);
            }
            catch (InvalidDataException)
            {
                continue;
            }

            for (var py = 0; py < patch.Height; py++)
            {
                var dy = originY + py;
                if ((uint)dy >= (uint)height)
                    continue;

                for (var px = 0; px < patch.Width; px++)
                {
                    var sourceIndex = py * patch.Width + px;
                    if (!patch.OpaqueMask[sourceIndex])
                        continue;

                    var dx = originX + px;
                    if ((uint)dx >= (uint)width)
                        continue;

                    var destinationIndex = dy * width + dx;
                    pixels[destinationIndex] = patch.PaletteIndices[sourceIndex];
                    opaque[destinationIndex] = true;
                }
            }
        }

        return new DoomMaterialImage(name, width, height, pixels, opaque, DoomMaterialKind.Texture);
    }

    private static void AddFlats(WadFile wad, List<DoomMaterialImage> output)
    {
        var inFlats = false;

        foreach (var lump in wad.Lumps)
        {
            var name = lump.Name.ToUpperInvariant();
            if (IsFlatStartMarker(name))
            {
                inFlats = true;
                continue;
            }

            if (IsFlatEndMarker(name))
            {
                inFlats = false;
                continue;
            }

            if (!inFlats || lump.Data.Length != 64 * 64 || lump.Name.Length == 0)
                continue;

            output.Add(new DoomMaterialImage(
                lump.Name.ToUpperInvariant(),
                64,
                64,
                lump.GetDataCopy(),
                Enumerable.Repeat(true, 64 * 64).ToArray(),
                DoomMaterialKind.Flat));
        }
    }

    private static bool IsFlatStartMarker(string name)
        => name is "F_START" or "FF_START"
           || (name.Length <= 8 && name.StartsWith('F') && name.EndsWith("_START", StringComparison.Ordinal));

    private static bool IsFlatEndMarker(string name)
        => name is "F_END" or "FF_END"
           || (name.Length <= 8 && name.StartsWith('F') && name.EndsWith("_END", StringComparison.Ordinal));

    private static string ReadName(ReadOnlySpan<byte> bytes)
    {
        var end = bytes.IndexOf((byte)0);
        if (end < 0) end = bytes.Length;
        return Encoding.ASCII.GetString(bytes[..end]).Trim().ToUpperInvariant();
    }

    private sealed class MaterialKeyComparer : IEqualityComparer<(DoomMaterialKind Kind, string Name)>
    {
        public static readonly MaterialKeyComparer Instance = new();

        public bool Equals((DoomMaterialKind Kind, string Name) x, (DoomMaterialKind Kind, string Name) y)
            => x.Kind == y.Kind && string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((DoomMaterialKind Kind, string Name) obj)
            => HashCode.Combine(obj.Kind, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name));
    }
}
