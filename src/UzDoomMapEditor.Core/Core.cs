using System.ComponentModel;
using System.Globalization;
using System.Text;

namespace UzDoomMapEditor.Core;

public sealed class EditorProject
{
    public string Name { get; set; } = "Untitled";
    public string MapName { get; set; } = "MAP01";
    public List<Room> Rooms { get; set; } = new();
    public List<MapThing> Things { get; set; } = new();
}

public sealed class Room
{
    [Category("Identity")]
    public string Name { get; set; } = "Room";

    [Category("Position")]
    public int X { get; set; }

    [Category("Position")]
    public int Y { get; set; }

    [Category("Size")]
    public int Width { get; set; } = 256;

    [Category("Size")]
    public int Depth { get; set; } = 256;

    [Category("Heights")]
    public int FloorHeight { get; set; }

    [Category("Heights")]
    public int CeilingHeight { get; set; } = 128;

    [Category("Textures")]
    public string WallTexture { get; set; } = "STARTAN3";

    [Category("Textures")]
    public string FloorTexture { get; set; } = "FLOOR0_1";

    [Category("Textures")]
    public string CeilingTexture { get; set; } = "CEIL1_1";

    [Category("Lighting")]
    [DefaultValue(160)]
    public int LightLevel { get; set; } = 160;

    public override string ToString() => Name;
}

public sealed class MapThing
{
    [Category("Identity")]
    public string Name { get; set; } = "Thing";

    [Category("Doom")]
    [Description("Doom/UZDoom thing type. Player 1 Start is type 1.")]
    public int Type { get; set; } = 1;

    [Category("Position")]
    public int X { get; set; }

    [Category("Position")]
    public int Y { get; set; }

    [Category("Position")]
    public int Height { get; set; }

    [Category("Position")]
    public int Angle { get; set; }

    public override string ToString() => Name;
}

public static class UdmfExporter
{
    public static string BuildText(EditorProject project)
    {
        var vertices = new StringBuilder();
        var sectors = new StringBuilder();
        var sidedefs = new StringBuilder();
        var linedefs = new StringBuilder();
        var things = new StringBuilder();

        vertices.AppendLine("namespace = \"zdoom\";");
        vertices.AppendLine();

        var vertexIndex = 0;
        var sideIndex = 0;

        for (var roomIndex = 0; roomIndex < project.Rooms.Count; roomIndex++)
        {
            var room = project.Rooms[roomIndex];
            var x1 = room.X;
            var y1 = room.Y;
            var x2 = room.X + Math.Max(1, room.Width);
            var y2 = room.Y + Math.Max(1, room.Depth);

            // Clockwise winding keeps the room interior on the front/right side of each linedef.
            AppendVertex(vertices, x1, y1);
            AppendVertex(vertices, x1, y2);
            AppendVertex(vertices, x2, y2);
            AppendVertex(vertices, x2, y1);

            sectors.AppendLine("sector");
            sectors.AppendLine("{");
            sectors.AppendLine($"    heightfloor = {room.FloorHeight};");
            sectors.AppendLine($"    heightceiling = {room.CeilingHeight};");
            sectors.AppendLine($"    texturefloor = {Quote(room.FloorTexture)};");
            sectors.AppendLine($"    textureceiling = {Quote(room.CeilingTexture)};");
            sectors.AppendLine($"    lightlevel = {Math.Clamp(room.LightLevel, 0, 255)};");
            sectors.AppendLine("}");
            sectors.AppendLine();

            for (var i = 0; i < 4; i++)
            {
                sidedefs.AppendLine("sidedef");
                sidedefs.AppendLine("{");
                sidedefs.AppendLine($"    sector = {roomIndex};");
                sidedefs.AppendLine($"    texturemiddle = {Quote(room.WallTexture)};");
                sidedefs.AppendLine("}");
                sidedefs.AppendLine();
            }

            AppendLine(linedefs, vertexIndex + 0, vertexIndex + 1, sideIndex + 0);
            AppendLine(linedefs, vertexIndex + 1, vertexIndex + 2, sideIndex + 1);
            AppendLine(linedefs, vertexIndex + 2, vertexIndex + 3, sideIndex + 2);
            AppendLine(linedefs, vertexIndex + 3, vertexIndex + 0, sideIndex + 3);

            vertexIndex += 4;
            sideIndex += 4;
        }

        foreach (var thing in project.Things)
        {
            things.AppendLine("thing");
            things.AppendLine("{");
            things.AppendLine($"    x = {F(thing.X)};");
            things.AppendLine($"    y = {F(thing.Y)};");
            things.AppendLine($"    height = {F(thing.Height)};");
            things.AppendLine($"    angle = {thing.Angle};");
            things.AppendLine($"    type = {thing.Type};");
            things.AppendLine("    skill1 = true;");
            things.AppendLine("    skill2 = true;");
            things.AppendLine("    skill3 = true;");
            things.AppendLine("    skill4 = true;");
            things.AppendLine("    skill5 = true;");
            things.AppendLine("    single = true;");
            things.AppendLine("    coop = true;");
            things.AppendLine("    dm = true;");
            things.AppendLine("}");
            things.AppendLine();
        }

        return vertices.ToString() + sectors + sidedefs + linedefs + things;
    }

    private static void AppendVertex(StringBuilder sb, int x, int y)
    {
        sb.AppendLine("vertex");
        sb.AppendLine("{");
        sb.AppendLine($"    x = {F(x)};");
        sb.AppendLine($"    y = {F(y)};");
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static void AppendLine(StringBuilder sb, int v1, int v2, int sideFront)
    {
        sb.AppendLine("linedef");
        sb.AppendLine("{");
        sb.AppendLine($"    v1 = {v1};");
        sb.AppendLine($"    v2 = {v2};");
        sb.AppendLine($"    sidefront = {sideFront};");
        sb.AppendLine("    blocking = true;");
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static string F(int value) => value.ToString("0.0", CultureInfo.InvariantCulture);
    private static string Quote(string value) => $"\"{(value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
}

public static class WadWriter
{
    private sealed record Lump(string Name, byte[] Data, int Position = 0);

    public static void WritePwad(string filePath, string mapName, string textMap)
    {
        var safeMapName = NormalizeLumpName(mapName);
        var lumps = new List<Lump>
        {
            new(safeMapName, Array.Empty<byte>()),
            new("TEXTMAP", new UTF8Encoding(false).GetBytes(textMap)),
            new("ENDMAP", Array.Empty<byte>())
        };

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);

        using var stream = File.Create(filePath);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        writer.Write(Encoding.ASCII.GetBytes("PWAD"));
        writer.Write(lumps.Count);
        writer.Write(0); // directory offset placeholder

        var written = new List<Lump>(lumps.Count);
        foreach (var lump in lumps)
        {
            var position = checked((int)stream.Position);
            writer.Write(lump.Data);
            written.Add(lump with { Position = position });
        }

        var directoryOffset = checked((int)stream.Position);
        foreach (var lump in written)
        {
            writer.Write(lump.Position);
            writer.Write(lump.Data.Length);
            WriteLumpName(writer, lump.Name);
        }

        stream.Position = 8;
        writer.Write(directoryOffset);
    }

    private static string NormalizeLumpName(string value)
    {
        var chars = (value ?? "MAP01").ToUpperInvariant()
            .Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-')
            .Take(8)
            .ToArray();
        return chars.Length == 0 ? "MAP01" : new string(chars);
    }

    private static void WriteLumpName(BinaryWriter writer, string name)
    {
        var bytes = Encoding.ASCII.GetBytes(NormalizeLumpName(name));
        var buffer = new byte[8];
        Array.Copy(bytes, buffer, Math.Min(bytes.Length, buffer.Length));
        writer.Write(buffer);
    }
}
