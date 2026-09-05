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

    SmokeTestActorParser();
    SmokeTestMaterialCatalog();

    Console.WriteLine("UzDoom.Core WAD, actor-state and material catalog smoke tests passed.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    return 1;
}

static void SmokeTestActorParser()
{
    const string source = """
        // DECORATE-style definition
        actor TestZombie : Actor 3004
        {
            States
            {
            Spawn:
                POSS AB 10 A_Look
                Loop
            See:
                POSS AABBCCDD 4 A_Chase
                Loop
            Missile:
                POSS E 8 Bright A_FaceTarget
                POSS F 8 A_PosAttack
                Goto See
            }
        }

        /* ZScript-style definition */
        class TestWeapon : Weapon
        {
            States
            {
            Ready:
                PISG A 1 A_WeaponReady;
                Loop;
            Fire:
                PISG BC 4;
                Goto Ready;
            }
        }
        """;

    var actors = ActorDefinitionParser.Parse(source, "TEST");
    Require(actors.Count == 2, "Actor parser did not find both DECORATE and ZScript-style definitions.");

    var zombie = actors.Single(actor => actor.Name == "TestZombie");
    Require(zombie.Parent == "Actor", "Actor parent was not parsed.");
    Require(zombie.States.Any(state => state.Label == "Spawn" && state.Frames.Count == 2), "Spawn state frames were not expanded.");
    var missile = zombie.States.Single(state => state.Label == "Missile");
    Require(missile.Frames.Count == 2, "Missile state frames were not parsed.");
    Require(missile.Frames[0].Bright, "Bright state flag was not parsed.");
    Require(string.Equals(missile.FlowControl, "Goto See", StringComparison.OrdinalIgnoreCase), "State flow control was not parsed.");

    var weapon = actors.Single(actor => actor.Name == "TestWeapon");
    Require(weapon.Parent == "Weapon", "ZScript class parent was not parsed.");
    Require(weapon.States.Single(state => state.Label == "Fire").Frames.Select(frame => frame.Frame).SequenceEqual(new[] { 'B', 'C' }), "Multi-frame ZScript state was not expanded.");
}

static void SmokeTestMaterialCatalog()
{
    var playpal = BuildPlaypal();
    var flat = Enumerable.Range(0, 64 * 64).Select(i => (byte)(i % 256)).ToArray();
    var lumps = new (string Name, byte[] Data)[]
    {
        ("PLAYPAL", playpal),
        ("PNAMES", BuildPNames("PATCHA")),
        ("PATCHA", BuildTwoByTwoPatch()),
        ("TEXTURE1", BuildTexture1()),
        ("F_START", Array.Empty<byte>()),
        ("FLAT1", flat),
        ("F_END", Array.Empty<byte>())
    };

    using var stream = BuildWad(lumps);
    var wad = WadFile.Load(stream);
    var materials = DoomMaterialCatalog.Load(wad);

    var texture = materials.Single(m => m.Name == "WALLA" && m.Kind == DoomMaterialKind.Texture);
    Require(texture.Width == 2 && texture.Height == 2, "Classic texture dimensions were decoded incorrectly.");
    Require(texture.PaletteIndices.SequenceEqual(new byte[] { 3, 5, 4, 6 }), "Classic texture patch composition was decoded incorrectly.");
    Require(texture.OpaqueMask.All(v => v), "Classic texture opacity was decoded incorrectly.");

    var decodedFlat = materials.Single(m => m.Name == "FLAT1" && m.Kind == DoomMaterialKind.Flat);
    Require(decodedFlat.Width == 64 && decodedFlat.Height == 64, "Flat dimensions were decoded incorrectly.");
    Require(decodedFlat.PaletteIndices.SequenceEqual(flat), "Flat palette pixels were decoded incorrectly.");
}

static MemoryStream BuildSyntheticWad()
{
    var lumps = new (string Name, byte[] Data)[]
    {
        ("PLAYPAL", BuildPlaypal()),
        ("S_START", Array.Empty<byte>()),
        ("TSTA0", BuildTwoByTwoPatch()),
        ("S_END", Array.Empty<byte>()),
        ("KEEP", new byte[] { 1, 2, 3, 4 })
    };

    return BuildWad(lumps);
}

static byte[] BuildPlaypal()
{
    var playpal = new byte[768];
    for (var i = 0; i < 256; i++)
    {
        playpal[i * 3] = (byte)i;
        playpal[i * 3 + 1] = (byte)i;
        playpal[i * 3 + 2] = (byte)i;
    }
    return playpal;
}

static byte[] BuildPNames(params string[] names)
{
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
    writer.Write(names.Length);
    foreach (var name in names)
        WriteName(writer, name);
    writer.Flush();
    return stream.ToArray();
}

static byte[] BuildTexture1()
{
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

    writer.Write(1);          // texture count
    writer.Write(8);          // offset of first maptexture
    WriteName(writer, "WALLA");
    writer.Write(0);          // masked
    writer.Write((short)2);   // width
    writer.Write((short)2);   // height
    writer.Write(0);          // obsolete column directory
    writer.Write((short)1);   // patch count
    writer.Write((short)0);   // origin x
    writer.Write((short)0);   // origin y
    writer.Write((short)0);   // PNAMES index
    writer.Write((short)0);   // stepdir
    writer.Write((short)0);   // colormap

    writer.Flush();
    return stream.ToArray();
}

static MemoryStream BuildWad((string Name, byte[] Data)[] lumps)
{
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
