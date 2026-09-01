using System.ComponentModel;
using System.Globalization;
using System.Text;

namespace UzDoomMapEditor.Core;

public sealed class EditorProject
{
    public string Name { get; set; } = "Untitled";
    public string MapName { get; set; } = "MAP01";
    public List<Room> Rooms { get; set; } = new();
    public List<Door> Doors { get; set; } = new();
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

public sealed class Door
{
    [Category("Identity")]
    public string Name { get; set; } = "Door";

    [Category("Position")]
    public int X { get; set; }

    [Category("Position")]
    public int Y { get; set; }

    [Category("Size")]
    [Description("Door connector width in map units. It should touch a room on two opposite sides.")]
    public int Width { get; set; } = 64;

    [Category("Size")]
    [Description("Door connector depth in map units. It should touch a room on two opposite sides.")]
    public int Depth { get; set; } = 128;

    [Category("Heights")]
    [Description("Usually the same floor height as the rooms on either side.")]
    public int FloorHeight { get; set; }

    [Category("Door")]
    [Description("Unique UZDoom sector tag used by the door action.")]
    public int Tag { get; set; } = 100;

    [Category("Door")]
    [Description("Door_Raise speed. 16 is a good normal starting value.")]
    public int Speed { get; set; } = 16;

    [Category("Door")]
    [Description("How long the door stays open before closing, in Doom tics (35 tics = 1 second).")]
    public int DelayTics { get; set; } = 150;

    [Category("Textures")]
    [Description("Texture shown on the moving door face.")]
    public string DoorTexture { get; set; } = "BIGDOOR2";

    [Category("Textures")]
    public string SideTexture { get; set; } = "STARTAN3";

    [Category("Textures")]
    public string FloorTexture { get; set; } = "FLOOR0_1";

    [Category("Textures")]
    public string CeilingTexture { get; set; } = "CEIL1_1";

    [Category("Lighting")]
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
    private sealed record SectorRect(
        string Name,
        int X1,
        int Y1,
        int X2,
        int Y2,
        int FloorHeight,
        int CeilingHeight,
        string WallTexture,
        string FloorTexture,
        string CeilingTexture,
        int LightLevel,
        Door? Door);

    private readonly record struct PointKey(int X, int Y);

    private readonly record struct EdgeKey(PointKey A, PointKey B)
    {
        public static EdgeKey Create(PointKey a, PointKey b)
        {
            return Compare(a, b) <= 0 ? new EdgeKey(a, b) : new EdgeKey(b, a);
        }

        private static int Compare(PointKey a, PointKey b)
        {
            var x = a.X.CompareTo(b.X);
            return x != 0 ? x : a.Y.CompareTo(b.Y);
        }
    }

    private sealed class EdgeInfo
    {
        public EdgeKey Key { get; init; }
        public List<int> Sectors { get; } = new();
    }

    public static string BuildText(EditorProject project)
    {
        project.Rooms ??= new List<Room>();
        project.Doors ??= new List<Door>();
        project.Things ??= new List<MapThing>();

        var regions = BuildRegions(project);
        ValidateRegions(regions);

        var vertices = new StringBuilder();
        var sectors = new StringBuilder();
        var sidedefs = new StringBuilder();
        var linedefs = new StringBuilder();
        var things = new StringBuilder();

        vertices.AppendLine("namespace = \"zdoom\";");
        vertices.AppendLine();

        for (var i = 0; i < regions.Count; i++)
            AppendSector(sectors, regions[i]);

        var xCuts = regions.SelectMany(r => new[] { r.X1, r.X2 }).Distinct().OrderBy(v => v).ToArray();
        var yCuts = regions.SelectMany(r => new[] { r.Y1, r.Y2 }).Distinct().OrderBy(v => v).ToArray();
        var edges = new Dictionary<EdgeKey, EdgeInfo>();

        for (var sectorIndex = 0; sectorIndex < regions.Count; sectorIndex++)
        {
            var r = regions[sectorIndex];
            AddVerticalEdge(edges, r.X1, r.Y1, r.Y2, yCuts, sectorIndex);
            AddVerticalEdge(edges, r.X2, r.Y1, r.Y2, yCuts, sectorIndex);
            AddHorizontalEdge(edges, r.Y1, r.X1, r.X2, xCuts, sectorIndex);
            AddHorizontalEdge(edges, r.Y2, r.X1, r.X2, xCuts, sectorIndex);
        }

        var vertexIndices = new Dictionary<PointKey, int>();
        var sideIndex = 0;

        foreach (var edge in edges.Values.OrderBy(e => e.Key.A.X).ThenBy(e => e.Key.A.Y).ThenBy(e => e.Key.B.X).ThenBy(e => e.Key.B.Y))
        {
            if (edge.Sectors.Count == 0) continue;
            if (edge.Sectors.Count > 2)
                throw new InvalidOperationException("More than two sectors share the same boundary. Move overlapping rooms/doors apart.");

            var a = edge.Key.A;
            var b = edge.Key.B;
            var sectorA = edge.Sectors[0];
            int? sectorB = edge.Sectors.Count == 2 ? edge.Sectors[1] : null;

            // Orient the line so sectorA lies on Doom's front/right side.
            if (!IsSectorOnRight(a, b, regions[sectorA]))
                (a, b) = (b, a);

            if (sectorB is not null && IsSectorOnRight(a, b, regions[sectorB.Value]))
                (sectorA, sectorB) = (sectorB.Value, sectorA);

            var v1 = GetVertexIndex(vertices, vertexIndices, a);
            var v2 = GetVertexIndex(vertices, vertexIndices, b);

            if (sectorB is null)
            {
                AppendSideDef(sidedefs, sectorA, regions[sectorA].WallTexture, "-", "-");
                AppendOneSidedLine(linedefs, v1, v2, sideIndex);
                sideIndex++;
                continue;
            }

            var front = regions[sectorA];
            var back = regions[sectorB.Value];
            var door = front.Door ?? back.Door;
            var topTexture = door?.DoorTexture ?? "-";

            AppendSideDef(sidedefs, sectorA, "-", topTexture, "-");
            var frontSide = sideIndex++;
            AppendSideDef(sidedefs, sectorB.Value, "-", topTexture, "-");
            var backSide = sideIndex++;

            AppendTwoSidedLine(linedefs, v1, v2, frontSide, backSide, door);
        }

        foreach (var thing in project.Things)
            AppendThing(things, thing);

        return vertices.ToString() + sectors + sidedefs + linedefs + things;
    }

    private static List<SectorRect> BuildRegions(EditorProject project)
    {
        var regions = new List<SectorRect>();

        foreach (var room in project.Rooms)
        {
            regions.Add(new SectorRect(
                room.Name,
                room.X,
                room.Y,
                room.X + Math.Max(1, room.Width),
                room.Y + Math.Max(1, room.Depth),
                room.FloorHeight,
                Math.Max(room.FloorHeight + 1, room.CeilingHeight),
                room.WallTexture,
                room.FloorTexture,
                room.CeilingTexture,
                Math.Clamp(room.LightLevel, 0, 255),
                null));
        }

        foreach (var door in project.Doors)
        {
            // A classic Doom door is a sector whose ceiling starts at the floor.
            // Door_Raise lifts it to the neighboring ceiling when the player uses it.
            regions.Add(new SectorRect(
                door.Name,
                door.X,
                door.Y,
                door.X + Math.Max(1, door.Width),
                door.Y + Math.Max(1, door.Depth),
                door.FloorHeight,
                door.FloorHeight,
                door.SideTexture,
                door.FloorTexture,
                door.CeilingTexture,
                Math.Clamp(door.LightLevel, 0, 255),
                door));
        }

        return regions;
    }

    private static void ValidateRegions(IReadOnlyList<SectorRect> regions)
    {
        for (var i = 0; i < regions.Count; i++)
        {
            var a = regions[i];
            if (a.X2 <= a.X1 || a.Y2 <= a.Y1)
                throw new InvalidOperationException($"{a.Name} has an invalid size.");

            for (var j = i + 1; j < regions.Count; j++)
            {
                var b = regions[j];
                var overlapX = Math.Min(a.X2, b.X2) - Math.Max(a.X1, b.X1);
                var overlapY = Math.Min(a.Y2, b.Y2) - Math.Max(a.Y1, b.Y1);
                if (overlapX > 0 && overlapY > 0)
                    throw new InvalidOperationException($"{a.Name} overlaps {b.Name}. Rooms and doors may touch edges but cannot overlap yet.");
            }
        }
    }

    private static void AddVerticalEdge(Dictionary<EdgeKey, EdgeInfo> edges, int x, int y1, int y2, int[] cuts, int sectorIndex)
    {
        var points = cuts.Where(v => v >= y1 && v <= y2).Prepend(y1).Append(y2).Distinct().OrderBy(v => v).ToArray();
        for (var i = 0; i < points.Length - 1; i++)
            AddEdge(edges, new PointKey(x, points[i]), new PointKey(x, points[i + 1]), sectorIndex);
    }

    private static void AddHorizontalEdge(Dictionary<EdgeKey, EdgeInfo> edges, int y, int x1, int x2, int[] cuts, int sectorIndex)
    {
        var points = cuts.Where(v => v >= x1 && v <= x2).Prepend(x1).Append(x2).Distinct().OrderBy(v => v).ToArray();
        for (var i = 0; i < points.Length - 1; i++)
            AddEdge(edges, new PointKey(points[i], y), new PointKey(points[i + 1], y), sectorIndex);
    }

    private static void AddEdge(Dictionary<EdgeKey, EdgeInfo> edges, PointKey a, PointKey b, int sectorIndex)
    {
        if (a == b) return;
        var key = EdgeKey.Create(a, b);
        if (!edges.TryGetValue(key, out var info))
        {
            info = new EdgeInfo { Key = key };
            edges.Add(key, info);
        }

        if (!info.Sectors.Contains(sectorIndex))
            info.Sectors.Add(sectorIndex);
    }

    private static bool IsSectorOnRight(PointKey a, PointKey b, SectorRect sector)
    {
        var midX = (a.X + b.X) / 2.0;
        var midY = (a.Y + b.Y) / 2.0;
        var centerX = (sector.X1 + sector.X2) / 2.0;
        var centerY = (sector.Y1 + sector.Y2) / 2.0;
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var cross = dx * (centerY - midY) - dy * (centerX - midX);
        return cross < 0;
    }

    private static int GetVertexIndex(StringBuilder vertices, Dictionary<PointKey, int> indices, PointKey point)
    {
        if (indices.TryGetValue(point, out var existing)) return existing;
        var index = indices.Count;
        indices.Add(point, index);
        AppendVertex(vertices, point.X, point.Y);
        return index;
    }

    private static void AppendSector(StringBuilder sb, SectorRect region)
    {
        sb.AppendLine("sector");
        sb.AppendLine("{");
        sb.AppendLine($"    heightfloor = {region.FloorHeight};");
        sb.AppendLine($"    heightceiling = {region.CeilingHeight};");
        sb.AppendLine($"    texturefloor = {Quote(region.FloorTexture)};");
        sb.AppendLine($"    textureceiling = {Quote(region.CeilingTexture)};");
        sb.AppendLine($"    lightlevel = {region.LightLevel};");
        if (region.Door is not null)
            sb.AppendLine($"    id = {Math.Max(1, region.Door.Tag)};");
        sb.AppendLine("}");
        sb.AppendLine();
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

    private static void AppendSideDef(StringBuilder sb, int sector, string middle, string top, string bottom)
    {
        sb.AppendLine("sidedef");
        sb.AppendLine("{");
        sb.AppendLine($"    sector = {sector};");
        sb.AppendLine($"    texturemiddle = {Quote(middle)};");
        sb.AppendLine($"    texturetop = {Quote(top)};");
        sb.AppendLine($"    texturebottom = {Quote(bottom)};");
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static void AppendOneSidedLine(StringBuilder sb, int v1, int v2, int sideFront)
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

    private static void AppendTwoSidedLine(StringBuilder sb, int v1, int v2, int sideFront, int sideBack, Door? door)
    {
        sb.AppendLine("linedef");
        sb.AppendLine("{");
        sb.AppendLine($"    v1 = {v1};");
        sb.AppendLine($"    v2 = {v2};");
        sb.AppendLine($"    sidefront = {sideFront};");
        sb.AppendLine($"    sideback = {sideBack};");
        sb.AppendLine("    twosided = true;");

        if (door is not null)
        {
            // ZDoom/Hexen-format special 12 = Door_Raise(tag, speed, delay).
            sb.AppendLine("    special = 12;");
            sb.AppendLine($"    arg0 = {Math.Max(1, door.Tag)};");
            sb.AppendLine($"    arg1 = {Math.Clamp(door.Speed, 1, 255)};");
            sb.AppendLine($"    arg2 = {Math.Clamp(door.DelayTics, 0, 255)};");
            sb.AppendLine("    playeruse = true;");
            sb.AppendLine("    repeatable = true;");
        }

        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static void AppendThing(StringBuilder sb, MapThing thing)
    {
        sb.AppendLine("thing");
        sb.AppendLine("{");
        sb.AppendLine($"    x = {F(thing.X)};");
        sb.AppendLine($"    y = {F(thing.Y)};");
        sb.AppendLine($"    height = {F(thing.Height)};");
        sb.AppendLine($"    angle = {thing.Angle};");
        sb.AppendLine($"    type = {thing.Type};");
        sb.AppendLine("    skill1 = true;");
        sb.AppendLine("    skill2 = true;");
        sb.AppendLine("    skill3 = true;");
        sb.AppendLine("    skill4 = true;");
        sb.AppendLine("    skill5 = true;");
        sb.AppendLine("    single = true;");
        sb.AppendLine("    coop = true;");
        sb.AppendLine("    dm = true;");
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
        writer.Write(0);

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
