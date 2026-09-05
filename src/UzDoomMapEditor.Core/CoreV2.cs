using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;

namespace UzDoomMapEditor.Core;

public enum SectorFloorShape
{
    Flat,
    Ramp
}

public enum RampDirection
{
    East,
    West,
    North,
    South
}

public sealed class EditorProject
{
    public string Name { get; set; } = "Untitled";
    public string MapName { get; set; } = "MAP01";
    public List<Sector> Sectors { get; set; } = new();
    public List<Door> Doors { get; set; } = new();
    public List<MapThing> Things { get; set; } = new();

    [Browsable(false)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<Room>? Rooms { get; set; }

    public void Normalize()
    {
        Sectors ??= new List<Sector>();
        Doors ??= new List<Door>();
        Things ??= new List<MapThing>();

        if (Rooms is { Count: > 0 })
        {
            foreach (var room in Rooms)
                Sectors.Add(room.ToSector());
        }
        Rooms = null;

        foreach (var sector in Sectors)
        {
            sector.Vertices ??= new List<MapVertex>();
            sector.Name ??= "Sector";
            sector.WallTexture ??= "STARTAN3";
            sector.FloorTexture ??= "FLOOR0_1";
            sector.CeilingTexture ??= "CEIL1_1";
            if (sector.FloorShape == SectorFloorShape.Ramp && sector.RampEndHeight == sector.FloorHeight)
                sector.RampEndHeight = sector.FloorHeight + 64;
            var highestFloor = Math.Max(sector.FloorHeight, sector.RampEndHeight);
            if (sector.CeilingHeight <= highestFloor)
                sector.CeilingHeight = highestFloor + 64;
        }
    }
}

public sealed class MapVertex
{
    public MapVertex() { }
    public MapVertex(int x, int y) { X = x; Y = y; }

    [Category("Position")]
    public int X { get; set; }

    [Category("Position")]
    public int Y { get; set; }

    public override string ToString() => $"({X}, {Y})";
}

public sealed class Sector
{
    [Category("Identity")]
    public string Name { get; set; } = "Sector";

    [Browsable(false)]
    public List<MapVertex> Vertices { get; set; } = new();

    [Category("Heights")]
    [Description("Floor height for a flat sector, or the START height for a ramp.")]
    public int FloorHeight { get; set; }

    [Category("Heights")]
    public int CeilingHeight { get; set; } = 128;

    [Category("Floor Shape")]
    public SectorFloorShape FloorShape { get; set; } = SectorFloorShape.Flat;

    [Category("Floor Shape")]
    [Description("Direction the ramp rises. Only used when Floor Shape is Ramp.")]
    public RampDirection RampDirection { get; set; } = RampDirection.East;

    [Category("Floor Shape")]
    [Description("Height at the far end of the ramp. Floor Height is the start height.")]
    public int RampEndHeight { get; set; } = 64;

    [Category("Textures")]
    public string WallTexture { get; set; } = "STARTAN3";

    [Category("Textures")]
    public string FloorTexture { get; set; } = "FLOOR0_1";

    [Category("Textures")]
    public string CeilingTexture { get; set; } = "CEIL1_1";

    [Category("Lighting")]
    [DefaultValue(160)]
    public int LightLevel { get; set; } = 160;

    [Category("Geometry")]
    [ReadOnly(true)]
    public int VertexCount => Vertices?.Count ?? 0;

    [Category("Geometry")]
    [ReadOnly(true)]
    public string FloorSummary => FloorShape == SectorFloorShape.Flat
        ? $"Flat @ {FloorHeight}"
        : $"Ramp {RampDirection}: {FloorHeight} → {RampEndHeight}";

    public double GetFloorHeightAt(double x, double y)
    {
        if (FloorShape != SectorFloorShape.Ramp || Vertices.Count == 0)
            return FloorHeight;

        var minX = Vertices.Min(v => v.X);
        var maxX = Vertices.Max(v => v.X);
        var minY = Vertices.Min(v => v.Y);
        var maxY = Vertices.Max(v => v.Y);
        double t;

        switch (RampDirection)
        {
            case RampDirection.East:
                t = maxX == minX ? 0 : (x - minX) / (maxX - minX);
                break;
            case RampDirection.West:
                t = maxX == minX ? 0 : (maxX - x) / (maxX - minX);
                break;
            case RampDirection.North:
                t = maxY == minY ? 0 : (y - minY) / (maxY - minY);
                break;
            case RampDirection.South:
                t = maxY == minY ? 0 : (maxY - y) / (maxY - minY);
                break;
            default:
                t = 0;
                break;
        }

        t = Math.Clamp(t, 0.0, 1.0);
        return FloorHeight + (RampEndHeight - FloorHeight) * t;
    }

    public static Sector Rectangle(string name, int x, int y, int width, int depth)
    {
        var x2 = x + Math.Max(1, width);
        var y2 = y + Math.Max(1, depth);
        return new Sector
        {
            Name = name,
            Vertices = new List<MapVertex>
            {
                new(x, y),
                new(x, y2),
                new(x2, y2),
                new(x2, y)
            }
        };
    }

    public override string ToString() => Name;
}

public sealed class Room
{
    public string Name { get; set; } = "Room";
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; } = 256;
    public int Depth { get; set; } = 256;
    public int FloorHeight { get; set; }
    public int CeilingHeight { get; set; } = 128;
    public string WallTexture { get; set; } = "STARTAN3";
    public string FloorTexture { get; set; } = "FLOOR0_1";
    public string CeilingTexture { get; set; } = "CEIL1_1";
    public int LightLevel { get; set; } = 160;

    public Sector ToSector()
    {
        var sector = Sector.Rectangle(Name, X, Y, Width, Depth);
        sector.FloorHeight = FloorHeight;
        sector.CeilingHeight = CeilingHeight;
        sector.WallTexture = WallTexture;
        sector.FloorTexture = FloorTexture;
        sector.CeilingTexture = CeilingTexture;
        sector.LightLevel = LightLevel;
        return sector;
    }
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
    [Description("Width along the doorway. New doors default to 128 map units so they do not look like narrow slots.")]
    public int Width { get; set; } = 128;

    [Category("Size")]
    [Description("Depth through the wall. New doors default to 64 map units, one normal editor grid square.")]
    public int Depth { get; set; } = 64;

    [Category("Heights")]
    public int FloorHeight { get; set; }

    [Category("Door")]
    public int Tag { get; set; } = 100;

    [Category("Door")]
    public int Speed { get; set; } = 16;

    [Category("Door")]
    [Description("How long the door stays open before closing, in Doom tics (35 tics = 1 second).")]
    public int DelayTics { get; set; } = 150;

    [Category("Textures")]
    public string DoorTexture { get; set; } = "BIGDOOR2";

    [Category("Textures")]
    public string SideTexture { get; set; } = "STARTAN3";

    [Category("Textures")]
    public string FloorTexture { get; set; } = "FLOOR0_1";

    [Category("Textures")]
    public string CeilingTexture { get; set; } = "CEIL1_1";

    [Category("Lighting")]
    public int LightLevel { get; set; } = 160;

    public IReadOnlyList<MapVertex> GetVertices() => new[]
    {
        new MapVertex(X, Y),
        new MapVertex(X, Y + Math.Max(1, Depth)),
        new MapVertex(X + Math.Max(1, Width), Y + Math.Max(1, Depth)),
        new MapVertex(X + Math.Max(1, Width), Y)
    };

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

public static class GeometryUtil
{
    public static bool PointInPolygon(double x, double y, IReadOnlyList<MapVertex> vertices, bool includeBoundary = true)
    {
        if (vertices.Count < 3) return false;
        var inside = false;
        for (var i = 0; i < vertices.Count; i++)
        {
            var a = vertices[i];
            var b = vertices[(i + 1) % vertices.Count];
            if (PointOnSegment(x, y, a.X, a.Y, b.X, b.Y))
                return includeBoundary;

            var intersects = ((a.Y > y) != (b.Y > y)) &&
                             (x < (double)(b.X - a.X) * (y - a.Y) / (b.Y - a.Y) + a.X);
            if (intersects) inside = !inside;
        }
        return inside;
    }

    public static bool PointOnSegment(double px, double py, double ax, double ay, double bx, double by)
    {
        var cross = (px - ax) * (by - ay) - (py - ay) * (bx - ax);
        if (Math.Abs(cross) > 0.0001) return false;
        var dot = (px - ax) * (bx - ax) + (py - ay) * (by - ay);
        if (dot < -0.0001) return false;
        var len2 = (bx - ax) * (bx - ax) + (by - ay) * (by - ay);
        return dot <= len2 + 0.0001;
    }

    public static double DistancePointToSegment(double px, double py, double ax, double ay, double bx, double by)
    {
        var dx = bx - ax;
        var dy = by - ay;
        var len2 = dx * dx + dy * dy;
        if (len2 <= double.Epsilon)
            return Math.Sqrt((px - ax) * (px - ax) + (py - ay) * (py - ay));
        var t = Math.Clamp(((px - ax) * dx + (py - ay) * dy) / len2, 0.0, 1.0);
        var qx = ax + t * dx;
        var qy = ay + t * dy;
        var ex = px - qx;
        var ey = py - qy;
        return Math.Sqrt(ex * ex + ey * ey);
    }

    public static double SignedArea(IReadOnlyList<MapVertex> vertices)
    {
        double area = 0;
        for (var i = 0; i < vertices.Count; i++)
        {
            var a = vertices[i];
            var b = vertices[(i + 1) % vertices.Count];
            area += (double)a.X * b.Y - (double)b.X * a.Y;
        }
        return area / 2.0;
    }
}

public static class UdmfExporter
{
    private readonly record struct PointKey(int X, int Y);
    private readonly record struct Plane(double A, double B, double C, double D);

    private sealed class Region
    {
        public required string Name { get; init; }
        public required List<PointKey> Vertices { get; init; }
        public required int FloorHeight { get; init; }
        public required int CeilingHeight { get; init; }
        public required string WallTexture { get; init; }
        public required string FloorTexture { get; init; }
        public required string CeilingTexture { get; init; }
        public required int LightLevel { get; init; }
        public Plane? FloorPlane { get; init; }
        public Door? Door { get; init; }
    }

    private sealed record RawEdge(int Sector, PointKey A, PointKey B);
    private sealed record EdgeUse(int Sector, PointKey A, PointKey B);

    private readonly record struct EdgeKey(PointKey A, PointKey B)
    {
        public static EdgeKey Create(PointKey a, PointKey b)
            => Compare(a, b) <= 0 ? new EdgeKey(a, b) : new EdgeKey(b, a);

        private static int Compare(PointKey a, PointKey b)
        {
            var x = a.X.CompareTo(b.X);
            return x != 0 ? x : a.Y.CompareTo(b.Y);
        }
    }

    public static string BuildText(EditorProject project)
    {
        project.Normalize();
        var regions = BuildRegions(project);
        ValidateRegions(regions);

        var vertices = new StringBuilder();
        var sectors = new StringBuilder();
        var sidedefs = new StringBuilder();
        var linedefs = new StringBuilder();
        var things = new StringBuilder();
        vertices.AppendLine("namespace = \"zdoom\";");
        vertices.AppendLine();

        foreach (var region in regions)
            AppendSector(sectors, region);

        var rawEdges = new List<RawEdge>();
        var allPoints = new HashSet<PointKey>();
        for (var sectorIndex = 0; sectorIndex < regions.Count; sectorIndex++)
        {
            var region = regions[sectorIndex];
            for (var i = 0; i < region.Vertices.Count; i++)
            {
                var a = region.Vertices[i];
                var b = region.Vertices[(i + 1) % region.Vertices.Count];
                rawEdges.Add(new RawEdge(sectorIndex, a, b));
                allPoints.Add(a);
                allPoints.Add(b);
            }
        }

        var edges = SplitAndGroupEdges(rawEdges, allPoints);
        var vertexIndices = new Dictionary<PointKey, int>();
        var sideIndex = 0;

        foreach (var pair in edges.OrderBy(e => e.Key.A.X).ThenBy(e => e.Key.A.Y).ThenBy(e => e.Key.B.X).ThenBy(e => e.Key.B.Y))
        {
            var uses = pair.Value;
            if (uses.Count > 2)
                throw new InvalidOperationException("More than two sectors share a boundary segment. Separate the overlapping geometry.");

            var first = uses[0];
            var v1 = GetVertexIndex(vertices, vertexIndices, first.A);
            var v2 = GetVertexIndex(vertices, vertexIndices, first.B);

            if (uses.Count == 1)
            {
                AppendSideDef(sidedefs, first.Sector, regions[first.Sector].WallTexture, "-", "-");
                AppendOneSidedLine(linedefs, v1, v2, sideIndex++);
                continue;
            }

            var second = uses[1];
            var front = regions[first.Sector];
            var back = regions[second.Sector];
            var door = front.Door ?? back.Door;
            var frontTop = door?.DoorTexture ?? front.WallTexture;
            var backTop = door?.DoorTexture ?? back.WallTexture;
            AppendSideDef(sidedefs, first.Sector, "-", frontTop, front.WallTexture);
            var frontSide = sideIndex++;
            AppendSideDef(sidedefs, second.Sector, "-", backTop, back.WallTexture);
            var backSide = sideIndex++;
            AppendTwoSidedLine(linedefs, v1, v2, frontSide, backSide, door);
        }

        foreach (var thing in project.Things)
            AppendThing(things, thing);

        return vertices.ToString() + sectors + sidedefs + linedefs + things;
    }

    private static List<Region> BuildRegions(EditorProject project)
    {
        var regions = new List<Region>();
        foreach (var sector in project.Sectors)
        {
            if (sector.Vertices.Count < 3)
                throw new InvalidOperationException($"{sector.Name} needs at least three vertices.");

            var points = sector.Vertices.Select(v => new PointKey(v.X, v.Y)).ToList();
            NormalizeClockwise(points);
            var maxFloor = sector.FloorShape == SectorFloorShape.Ramp
                ? Math.Max(sector.FloorHeight, sector.RampEndHeight)
                : sector.FloorHeight;

            regions.Add(new Region
            {
                Name = sector.Name,
                Vertices = points,
                FloorHeight = sector.FloorHeight,
                CeilingHeight = Math.Max(maxFloor + 1, sector.CeilingHeight),
                WallTexture = sector.WallTexture,
                FloorTexture = sector.FloorTexture,
                CeilingTexture = sector.CeilingTexture,
                LightLevel = Math.Clamp(sector.LightLevel, 0, 255),
                FloorPlane = sector.FloorShape == SectorFloorShape.Ramp ? BuildFloorPlane(sector) : null
            });
        }

        foreach (var door in project.Doors)
        {
            var points = door.GetVertices().Select(v => new PointKey(v.X, v.Y)).ToList();
            NormalizeClockwise(points);
            regions.Add(new Region
            {
                Name = door.Name,
                Vertices = points,
                FloorHeight = door.FloorHeight,
                CeilingHeight = door.FloorHeight,
                WallTexture = door.SideTexture,
                FloorTexture = door.FloorTexture,
                CeilingTexture = door.CeilingTexture,
                LightLevel = Math.Clamp(door.LightLevel, 0, 255),
                Door = door
            });
        }

        return regions;
    }

    private static Plane BuildFloorPlane(Sector sector)
    {
        var minX = sector.Vertices.Min(v => v.X);
        var maxX = sector.Vertices.Max(v => v.X);
        var minY = sector.Vertices.Min(v => v.Y);
        var maxY = sector.Vertices.Max(v => v.Y);
        var delta = sector.RampEndHeight - sector.FloorHeight;

        return sector.RampDirection switch
        {
            RampDirection.East => MakePositiveAxisPlane(delta, maxX - minX, minX, sector.FloorHeight, xAxis: true),
            RampDirection.West => MakeNegativeAxisPlane(delta, maxX - minX, maxX, sector.FloorHeight, xAxis: true),
            RampDirection.North => MakePositiveAxisPlane(delta, maxY - minY, minY, sector.FloorHeight, xAxis: false),
            RampDirection.South => MakeNegativeAxisPlane(delta, maxY - minY, maxY, sector.FloorHeight, xAxis: false),
            _ => new Plane(0, 0, 1, -sector.FloorHeight)
        };
    }

    private static Plane MakePositiveAxisPlane(int delta, int run, int startCoord, int startHeight, bool xAxis)
    {
        if (run == 0) return new Plane(0, 0, 1, -startHeight);
        var m = delta / (double)run;
        var coefficient = -m;
        var d = m * startCoord - startHeight;
        return xAxis ? new Plane(coefficient, 0, 1, d) : new Plane(0, coefficient, 1, d);
    }

    private static Plane MakeNegativeAxisPlane(int delta, int run, int startCoord, int startHeight, bool xAxis)
    {
        if (run == 0) return new Plane(0, 0, 1, -startHeight);
        var m = delta / (double)run;
        var coefficient = m;
        var d = -(startHeight + m * startCoord);
        return xAxis ? new Plane(coefficient, 0, 1, d) : new Plane(0, coefficient, 1, d);
    }

    private static void NormalizeClockwise(List<PointKey> points)
    {
        double area = 0;
        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            area += (double)a.X * b.Y - (double)b.X * a.Y;
        }
        if (area > 0) points.Reverse();
    }

    private static Dictionary<EdgeKey, List<EdgeUse>> SplitAndGroupEdges(IReadOnlyList<RawEdge> rawEdges, IReadOnlyCollection<PointKey> allPoints)
    {
        var grouped = new Dictionary<EdgeKey, List<EdgeUse>>();
        foreach (var raw in rawEdges)
        {
            var points = allPoints.Where(p => PointOnSegment(p, raw.A, raw.B)).ToList();
            points.Sort((p1, p2) => ParameterAlong(raw.A, raw.B, p1).CompareTo(ParameterAlong(raw.A, raw.B, p2)));
            for (var i = 0; i < points.Count - 1; i++)
            {
                var a = points[i];
                var b = points[i + 1];
                if (a == b) continue;
                var key = EdgeKey.Create(a, b);
                if (!grouped.TryGetValue(key, out var uses))
                {
                    uses = new List<EdgeUse>();
                    grouped.Add(key, uses);
                }
                if (uses.All(u => u.Sector != raw.Sector))
                    uses.Add(new EdgeUse(raw.Sector, a, b));
            }
        }
        return grouped;
    }

    private static bool PointOnSegment(PointKey p, PointKey a, PointKey b)
    {
        var cross = (long)(p.X - a.X) * (b.Y - a.Y) - (long)(p.Y - a.Y) * (b.X - a.X);
        if (cross != 0) return false;
        return p.X >= Math.Min(a.X, b.X) && p.X <= Math.Max(a.X, b.X) &&
               p.Y >= Math.Min(a.Y, b.Y) && p.Y <= Math.Max(a.Y, b.Y);
    }

    private static double ParameterAlong(PointKey a, PointKey b, PointKey p)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var len2 = (double)dx * dx + (double)dy * dy;
        return len2 <= double.Epsilon ? 0 : ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2;
    }

    private static void ValidateRegions(IReadOnlyList<Region> regions)
    {
        for (var i = 0; i < regions.Count; i++)
        {
            var a = regions[i];
            if (Math.Abs(SignedArea(a.Vertices)) < 0.5)
                throw new InvalidOperationException($"{a.Name} has zero or invalid area.");
            if (HasSelfIntersection(a.Vertices))
                throw new InvalidOperationException($"{a.Name} crosses over itself. Move its vertices so the outline does not self-intersect.");

            for (var j = i + 1; j < regions.Count; j++)
            {
                var b = regions[j];
                if (PolygonsOverlap(a.Vertices, b.Vertices))
                    throw new InvalidOperationException($"{a.Name} overlaps {b.Name}. Sectors may share edges but their interiors cannot overlap.");
            }
        }
    }

    private static bool PolygonsOverlap(IReadOnlyList<PointKey> a, IReadOnlyList<PointKey> b)
    {
        foreach (var p in a) if (PointInPolygonStrict(p, b)) return true;
        foreach (var p in b) if (PointInPolygonStrict(p, a)) return true;
        for (var i = 0; i < a.Count; i++)
        {
            var a1 = a[i];
            var a2 = a[(i + 1) % a.Count];
            for (var j = 0; j < b.Count; j++)
            {
                var b1 = b[j];
                var b2 = b[(j + 1) % b.Count];
                if (ProperSegmentIntersection(a1, a2, b1, b2)) return true;
            }
        }
        return false;
    }

    private static bool HasSelfIntersection(IReadOnlyList<PointKey> p)
    {
        for (var i = 0; i < p.Count; i++)
        {
            var a1 = p[i];
            var a2 = p[(i + 1) % p.Count];
            for (var j = i + 1; j < p.Count; j++)
            {
                if (j == i || (j + 1) % p.Count == i || (i + 1) % p.Count == j) continue;
                var b1 = p[j];
                var b2 = p[(j + 1) % p.Count];
                if (ProperSegmentIntersection(a1, a2, b1, b2)) return true;
            }
        }
        return false;
    }

    private static bool ProperSegmentIntersection(PointKey a, PointKey b, PointKey c, PointKey d)
    {
        var o1 = Orientation(a, b, c);
        var o2 = Orientation(a, b, d);
        var o3 = Orientation(c, d, a);
        var o4 = Orientation(c, d, b);
        if (o1 == 0 || o2 == 0 || o3 == 0 || o4 == 0) return false;
        return o1 != o2 && o3 != o4;
    }

    private static int Orientation(PointKey a, PointKey b, PointKey c)
    {
        var value = (long)(b.X - a.X) * (c.Y - a.Y) - (long)(b.Y - a.Y) * (c.X - a.X);
        return value == 0 ? 0 : value > 0 ? 1 : -1;
    }

    private static bool PointInPolygonStrict(PointKey p, IReadOnlyList<PointKey> polygon)
    {
        for (var i = 0; i < polygon.Count; i++)
            if (PointOnSegment(p, polygon[i], polygon[(i + 1) % polygon.Count])) return false;

        var inside = false;
        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];
            var intersects = ((a.Y > p.Y) != (b.Y > p.Y)) &&
                             (p.X < (double)(b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X);
            if (intersects) inside = !inside;
        }
        return inside;
    }

    private static double SignedArea(IReadOnlyList<PointKey> points)
    {
        double area = 0;
        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            area += (double)a.X * b.Y - (double)b.X * a.Y;
        }
        return area / 2.0;
    }

    private static int GetVertexIndex(StringBuilder vertices, Dictionary<PointKey, int> indices, PointKey point)
    {
        if (indices.TryGetValue(point, out var existing)) return existing;
        var index = indices.Count;
        indices.Add(point, index);
        AppendVertex(vertices, point.X, point.Y);
        return index;
    }

    private static void AppendSector(StringBuilder sb, Region region)
    {
        sb.AppendLine("sector");
        sb.AppendLine("{");
        sb.AppendLine($"    heightfloor = {region.FloorHeight};");
        sb.AppendLine($"    heightceiling = {region.CeilingHeight};");
        sb.AppendLine($"    texturefloor = {Quote(region.FloorTexture)};");
        sb.AppendLine($"    textureceiling = {Quote(region.CeilingTexture)};");
        sb.AppendLine($"    lightlevel = {region.LightLevel};");

        if (region.FloorPlane is { } plane)
        {
            sb.AppendLine($"    floorplane_a = {F(plane.A)};");
            sb.AppendLine($"    floorplane_b = {F(plane.B)};");
            sb.AppendLine($"    floorplane_c = {F(plane.C)};");
            sb.AppendLine($"    floorplane_d = {F(plane.D)};");
        }

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
            sb.AppendLine("    special = 12;");
            sb.AppendLine($"    arg0 = {Math.Max(1, door.Tag)};");
            sb.AppendLine($"    arg1 = {Math.Clamp(door.Speed, 1, 255)};");
            sb.AppendLine($"    arg2 = {Math.Clamp(door.DelayTics, 0, 255)};");
            sb.AppendLine("    playeruse = true;");
            sb.AppendLine("    repeatspecial = true;");
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
    private static string F(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);
    private static string Quote(string? value) => $"\"{(value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
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
