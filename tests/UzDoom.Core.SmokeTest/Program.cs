using System.Text;
using UzDoom.Core;

try
{
    using var input = BuildSyntheticWad();
    var wad = WadFile.Load(input);

    Require(wad.Identification == "PWAD", "Identification was not preserved.");
    Require(wad.Lumps.Count == 5, "Unexpected lump count.");
    Require(wad.Lumps.Select(l => l.Name).SequenceEqual(new[] { "PLAYPAL", "S_START", "TSTA0", "S_END", "KEEP" }), "Lump ordering changed on load.");

    var spriteIndices = wad.GetSpriteLumpIndices();
    Require(spriteIndices.Count == 1 && spriteIndices[0] == 2, "Sprite namespace detection failed.");

    var decoded = DoomPatchCodec.Decode(wad.Lumps[2].Data.Span);
    Require(decoded.Width == 2 && decoded.Height == 2, "Patch dimensions decoded incorrectly.");
    Require(decoded.LeftOffset == 1 && decoded.TopOffset == 2, "Patch offsets decoded incorrectly.");
    Require(decoded.PaletteIndices.SequenceEqual(new byte[] { 3, 5, 4, 6 }), "Patch pixels decoded incorrectly.");
    Require(decoded.OpaqueMask.All(v => v), "Patch opacity decoded incorrectly.");

    var replacement = new DoomPatchImage(
        2,
        2,
        7,
        9,
        new byte[] { 10, 11, 12, 13 },
        new[] { true, true, true, true });

    wad.ReplaceLump(2, DoomPatchCodec.Encode(replacement));

    using var output = new MemoryStream();
    wad.Save(output);
    output.Position = 0;
    var rebuilt = WadFile.Load(output);

    Require(rebuilt.Lumps.Count == 5, "Lump count changed after rebuild.");
    Require(rebuilt.Lumps.Select(l => l.Name).SequenceEqual(wad.Lumps.Select(l => l.Name)), "Lump ordering changed after rebuild.");
    Require(rebuilt.Lumps[4].Data.Span.SequenceEqual(new byte[] { 1, 2, 3, 4 }), "Untouched lump data changed after rebuild.");

    var rebuiltSprite = DoomPatchCodec.Decode(rebuilt.Lumps[2].Data.Span);
    Require(rebuiltSprite.LeftOffset == 7 && rebuiltSprite.TopOffset == 9, "Replacement offsets did not survive rebuild.");
    Require(rebuiltSprite.PaletteIndices.SequenceEqual(new byte[] { 10, 11, 12, 13 }), "Replacement pixels did not survive rebuild.");

    Console.WriteLine("UzDoom.Core WAD round-trip smoke test passed.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    return 1;
}

static MemoryStream BuildSyntheticWad()
{
    var playpal = new byte[768];
    for (var i = 0; i < 256; i++)
    {
        playpal[i * 3] = (byte)i;
        playpal[i * 3 + 1] = (byte)i;
        playpal[i * 3 + 2] = (byte)i;
    }

    var patch = BuildTwoByTwoPatch();
    var lumps = new (string Name, byte[] Data)[]
    {
        ("PLAYPAL", playpal),
        ("S_START", Array.Empty<byte>()),
        ("TSTA0", patch),
        ("S_END", Array.Empty<byte>()),
        ("KEEP", new byte[] { 1, 2, 3, 4 })
    };

    var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
    writer.Write(Encoding.ASCII.GetBytes("PWAD"));
    writer.Write(lumps.Length);
    writer.Write(0);

    var positions = new int[lumps.Length];
    for (var i = 0; i < lumps.Length; i++)
    {
        positions[i] = checked((int)stream.Position);
        writer.Write(lumps[i].Data);
    }

    var directoryOffset = checked((int)stream.Position);
    for (var i = 0; i < lumps.Length; i++)
    {
        writer.Write(positions[i]);
        writer.Write(lumps[i].Data.Length);
        WriteName(writer, lumps[i].Name);
    }

    stream.Position = 8;
    writer.Write(directoryOffset);
    writer.Flush();
    stream.Position = 0;
    return stream;
}

static byte[] BuildTwoByTwoPatch()
{
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream);

    writer.Write((short)2);
    writer.Write((short)2);
    writer.Write((short)1);
    writer.Write((short)2);
    writer.Write(16);
    writer.Write(23);

    writer.Write((byte)0);
    writer.Write((byte)2);
    writer.Write((byte)0);
    writer.Write((byte)3);
    writer.Write((byte)4);
    writer.Write((byte)0);
    writer.Write((byte)255);

    writer.Write((byte)0);
    writer.Write((byte)2);
    writer.Write((byte)0);
    writer.Write((byte)5);
    writer.Write((byte)6);
    writer.Write((byte)0);
    writer.Write((byte)255);

    writer.Flush();
    return stream.ToArray();
}

static void WriteName(BinaryWriter writer, string name)
{
    var bytes = Encoding.ASCII.GetBytes(name);
    writer.Write(bytes);
    for (var i = bytes.Length; i < 8; i++)
        writer.Write((byte)0);
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
