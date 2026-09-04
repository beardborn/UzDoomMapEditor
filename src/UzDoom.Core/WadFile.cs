using System.Text;

namespace UzDoom.Core;

public sealed class WadFile
{
    private readonly List<WadLump> _lumps;

    private WadFile(string identification, List<WadLump> lumps)
    {
        Identification = identification;
        _lumps = lumps;
    }

    public string Identification { get; }
    public IReadOnlyList<WadLump> Lumps => _lumps;

    public static WadFile Open(string path)
    {
        using var stream = File.OpenRead(path);
        return Load(stream);
    }

    public static WadFile Load(Stream stream)
    {
        if (!stream.CanRead || !stream.CanSeek)
            throw new ArgumentException("WAD streams must be readable and seekable.", nameof(stream));

        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        if (stream.Length < 12)
            throw new InvalidDataException("File is too small to be a WAD.");

        stream.Position = 0;
        var idBytes = reader.ReadBytes(4);
        if (idBytes.Length != 4)
            throw new EndOfStreamException();

        var identification = Encoding.ASCII.GetString(idBytes);
        if (identification is not ("IWAD" or "PWAD"))
            throw new InvalidDataException($"Unsupported WAD identification '{identification}'.");

        var lumpCount = reader.ReadInt32();
        var directoryOffset = reader.ReadInt32();

        if (lumpCount < 0)
            throw new InvalidDataException("WAD lump count is negative.");

        var directorySize = checked((long)lumpCount * 16L);
        if (directoryOffset < 0 || (long)directoryOffset + directorySize > stream.Length)
            throw new InvalidDataException("WAD directory lies outside the file.");

        var directory = new (int Position, int Size, string Name)[lumpCount];
        stream.Position = directoryOffset;

        for (var i = 0; i < lumpCount; i++)
        {
            var position = reader.ReadInt32();
            var size = reader.ReadInt32();
            var nameBytes = reader.ReadBytes(8);
            if (nameBytes.Length != 8)
                throw new EndOfStreamException();

            if (position < 0 || size < 0 || (long)position + size > stream.Length)
                throw new InvalidDataException($"Lump #{i} points outside the file.");

            var name = Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');
            directory[i] = (position, size, name);
        }

        var lumps = new List<WadLump>(lumpCount);
        for (var i = 0; i < lumpCount; i++)
        {
            var entry = directory[i];
            stream.Position = entry.Position;
            var data = reader.ReadBytes(entry.Size);
            if (data.Length != entry.Size)
                throw new EndOfStreamException();

            lumps.Add(new WadLump(i, entry.Name, data));
        }

        return new WadFile(identification, lumps);
    }

    public WadLump? FindFirst(string name)
    {
        return _lumps.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public WadLump? FindLast(string name)
    {
        for (var i = _lumps.Count - 1; i >= 0; i--)
        {
            if (string.Equals(_lumps[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return _lumps[i];
        }

        return null;
    }

    public IReadOnlyList<int> GetSpriteLumpIndices() => WadNamespaces.FindSpriteLumpIndices(_lumps);

    public void ReplaceLump(int index, ReadOnlySpan<byte> data)
    {
        if ((uint)index >= (uint)_lumps.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        _lumps[index].ReplaceData(data);
    }

    public void SaveAs(string path)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        Save(stream);
    }

    public void Save(Stream stream)
    {
        if (!stream.CanWrite || !stream.CanSeek)
            throw new ArgumentException("WAD output streams must be writable and seekable.", nameof(stream));

        stream.SetLength(0);
        stream.Position = 0;

        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes(Identification));
        writer.Write(_lumps.Count);
        writer.Write(0); // directory offset placeholder

        var positions = new int[_lumps.Count];
        for (var i = 0; i < _lumps.Count; i++)
        {
            positions[i] = checked((int)stream.Position);
            var data = _lumps[i].Data.Span;
            writer.Write(data);
        }

        var directoryOffset = checked((int)stream.Position);
        for (var i = 0; i < _lumps.Count; i++)
        {
            var lump = _lumps[i];
            writer.Write(positions[i]);
            writer.Write(lump.Data.Length);
            WriteName(writer, lump.Name);
        }

        var end = stream.Position;
        stream.Position = 8;
        writer.Write(directoryOffset);
        stream.Position = end;
        writer.Flush();
    }

    private static void WriteName(BinaryWriter writer, string name)
    {
        var bytes = Encoding.ASCII.GetBytes(name);
        if (bytes.Length > 8)
            throw new InvalidDataException($"WAD lump name '{name}' is longer than 8 bytes.");

        writer.Write(bytes);
        for (var i = bytes.Length; i < 8; i++)
            writer.Write((byte)0);
    }
}

public sealed class WadLump
{
    private byte[] _data;

    internal WadLump(int index, string name, byte[] data)
    {
        Index = index;
        Name = name;
        _data = data;
    }

    public int Index { get; }
    public string Name { get; }
    public ReadOnlyMemory<byte> Data => _data;

    public byte[] GetDataCopy() => _data.ToArray();

    internal void ReplaceData(ReadOnlySpan<byte> data)
    {
        _data = data.ToArray();
    }

    public override string ToString() => $"{Index}: {Name} ({_data.Length} bytes)";
}

public static class WadNamespaces
{
    public static IReadOnlyList<int> FindSpriteLumpIndices(IReadOnlyList<WadLump> lumps)
    {
        var result = new List<int>();
        var inSprites = false;

        for (var i = 0; i < lumps.Count; i++)
        {
            var name = lumps[i].Name.ToUpperInvariant();
            if (IsSpriteStartMarker(name))
            {
                inSprites = true;
                continue;
            }

            if (IsSpriteEndMarker(name))
            {
                inSprites = false;
                continue;
            }

            if (inSprites && lumps[i].Data.Length > 0)
                result.Add(i);
        }

        return result;
    }

    private static bool IsSpriteStartMarker(string name)
    {
        return name is "S_START" or "SS_START" ||
               (name.Length <= 8 && name.StartsWith('S') && name.EndsWith("_START", StringComparison.Ordinal));
    }

    private static bool IsSpriteEndMarker(string name)
    {
        return name is "S_END" or "SS_END" ||
               (name.Length <= 8 && name.StartsWith('S') && name.EndsWith("_END", StringComparison.Ordinal));
    }
}
