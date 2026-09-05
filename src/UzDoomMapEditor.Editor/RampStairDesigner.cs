using UzDoomMapEditor.Core;

namespace UzDoomMapEditor.Editor;

internal static class RampStairDesigner
{
    public const int DefaultDoorWidth = 128;
    public const int DefaultDoorDepth = 64;
    public const int DefaultRampRise = 64;
    public const int DefaultStairRise = 8;
    public const int DefaultTread = 32;

    private enum BoundarySide
    {
        West,
        East,
        South,
        North
    }

    private readonly record struct Bounds(int MinX, int MinY, int MaxX, int MaxY)
    {
        public int Width => MaxX - MinX;
        public int Depth => MaxY - MinY;
    }

    private readonly record struct SideContact(BoundarySide Side, Sector Sector);

    public static Sector CreateRamp(string name, int x, int y, int width, int depth, PointF dragStart, PointF dragEnd)
    {
        var sector = Sector.Rectangle(name, x, y, width, depth);
        sector.FloorShape = SectorFloorShape.Ramp;
        sector.RampDirection = GetDragDirection(width, depth, dragStart, dragEnd);
        sector.RampEndHeight = sector.FloorHeight + DefaultRampRise;
        return sector;
    }

    public static IReadOnlyList<Sector> CreateStairs(
        string baseName,
        int x,
        int y,
        int width,
        int depth,
        PointF dragStart,
        PointF dragEnd,
        Func<string, int, int, int, int, Sector> sectorFactory)
    {
        var direction = GetDragDirection(width, depth, dragStart, dragEnd);
        var horizontal = direction is RampDirection.East or RampDirection.West;
        var run = horizontal ? width : depth;
        var count = Math.Clamp((int)Math.Round(run / (double)DefaultTread), 2, 24);
        var result = new List<Sector>(count);

        for (var i = 0; i < count; i++)
        {
            var step = sectorFactory($"{baseName} Step {i + 1}", 0, 0, 1, 1);
            result.Add(step);
        }

        var bounds = new Bounds(x, y, x + width, y + depth);
        LayoutStairs(result, bounds, direction);
        ApplyStairHeights(result, 0, DefaultStairRise * Math.Max(1, result.Count - 1));
        return result;
    }

    public static void MatchRampToNeighbours(Sector ramp, IEnumerable<Sector> sectors)
    {
        var bounds = GetBounds(ramp);
        var contacts = FindSideContacts(bounds, sectors.Where(s => !ReferenceEquals(s, ramp))).ToList();
        var direction = ChooseRiseDirection(bounds, contacts, ramp.RampDirection);
        ramp.RampDirection = direction;

        var startSide = StartSide(direction);
        var endSide = EndSide(direction);
        var startHeight = GetSideFloor(contacts, startSide);
        var endHeight = GetSideFloor(contacts, endSide);

        if (startHeight.HasValue && endHeight.HasValue)
        {
            if (endHeight.Value < startHeight.Value)
            {
                ramp.RampDirection = Opposite(direction);
                (startHeight, endHeight) = (endHeight, startHeight);
            }

            ramp.FloorHeight = startHeight.Value;
            ramp.RampEndHeight = endHeight.Value == startHeight.Value
                ? startHeight.Value + DefaultRampRise
                : endHeight.Value;
        }
        else if (startHeight.HasValue)
        {
            // A ramp with one connected side should rise OUT of that floor, not
            // dive below it. This was the cause of ramps looking sunk into rooms.
            ramp.FloorHeight = startHeight.Value;
            ramp.RampEndHeight = startHeight.Value + DefaultRampRise;
        }
        else if (endHeight.HasValue)
        {
            // Put the only neighbour on the low end and rise away from it.
            ramp.RampDirection = Opposite(direction);
            ramp.FloorHeight = endHeight.Value;
            ramp.RampEndHeight = endHeight.Value + DefaultRampRise;
        }

        var highest = Math.Max(ramp.FloorHeight, ramp.RampEndHeight);
        if (ramp.CeilingHeight <= highest)
            ramp.CeilingHeight = highest + 64;
    }

    public static void MatchStairsToNeighbours(IReadOnlyList<Sector> stairs, IEnumerable<Sector> sectors)
    {
        if (stairs.Count == 0) return;

        var bounds = GetBounds(stairs);
        var others = sectors.Except(stairs).ToList();
        var contacts = FindSideContacts(bounds, others).ToList();
        var fallback = InferStairDirection(stairs);
        var direction = ChooseRiseDirection(bounds, contacts, fallback);

        // Re-layout the treads when neighbouring rooms show that the staircase
        // should run on the other axis. A wide staircase can therefore run
        // north/south instead of being incorrectly forced east/west.
        LayoutStairs(stairs, bounds, direction);

        var startHeight = GetSideFloor(contacts, StartSide(direction));
        var endHeight = GetSideFloor(contacts, EndSide(direction));
        var defaultRise = DefaultStairRise * Math.Max(1, stairs.Count - 1);

        if (startHeight.HasValue && endHeight.HasValue)
        {
            if (endHeight.Value < startHeight.Value)
            {
                direction = Opposite(direction);
                LayoutStairs(stairs, bounds, direction);
                (startHeight, endHeight) = (endHeight, startHeight);
            }

            var top = endHeight.Value == startHeight.Value
                ? startHeight.Value + defaultRise
                : endHeight.Value;
            ApplyStairHeights(stairs, startHeight.Value, top);
        }
        else if (startHeight.HasValue)
        {
            ApplyStairHeights(stairs, startHeight.Value, startHeight.Value + defaultRise);
        }
        else if (endHeight.HasValue)
        {
            // Same rule as ramps: with one neighbour, that floor is the bottom
            // of the stairs. Never generate a staircase down into the floor.
            direction = Opposite(direction);
            LayoutStairs(stairs, bounds, direction);
            ApplyStairHeights(stairs, endHeight.Value, endHeight.Value + defaultRise);
        }
        else
        {
            ApplyStairHeights(stairs, 0, defaultRise);
        }
    }

    private static void LayoutStairs(IReadOnlyList<Sector> stairs, Bounds bounds, RampDirection direction)
    {
        var horizontal = direction is RampDirection.East or RampDirection.West;
        var count = stairs.Count;

        for (var i = 0; i < count; i++)
        {
            var t0 = i / (double)count;
            var t1 = (i + 1) / (double)count;
            int sx;
            int sy;
            int sw;
            int sd;

            if (horizontal)
            {
                var a = bounds.MinX + (int)Math.Round(bounds.Width * t0);
                var b = bounds.MinX + (int)Math.Round(bounds.Width * t1);
                if (direction == RampDirection.West)
                {
                    a = bounds.MaxX - (int)Math.Round(bounds.Width * t1);
                    b = bounds.MaxX - (int)Math.Round(bounds.Width * t0);
                }

                sx = Math.Min(a, b);
                sy = bounds.MinY;
                sw = Math.Max(1, Math.Abs(b - a));
                sd = Math.Max(1, bounds.Depth);
            }
            else
            {
                var a = bounds.MinY + (int)Math.Round(bounds.Depth * t0);
                var b = bounds.MinY + (int)Math.Round(bounds.Depth * t1);
                if (direction == RampDirection.South)
                {
                    a = bounds.MaxY - (int)Math.Round(bounds.Depth * t1);
                    b = bounds.MaxY - (int)Math.Round(bounds.Depth * t0);
                }

                sx = bounds.MinX;
                sy = Math.Min(a, b);
                sw = Math.Max(1, bounds.Width);
                sd = Math.Max(1, Math.Abs(b - a));
            }

            stairs[i].Vertices = Sector.Rectangle(stairs[i].Name, sx, sy, sw, sd).Vertices;
        }
    }

    private static void ApplyStairHeights(IReadOnlyList<Sector> stairs, int startHeight, int endHeight)
    {
        var highest = Math.Max(startHeight, endHeight);
        var commonCeiling = Math.Max(128, highest + 64);

        for (var i = 0; i < stairs.Count; i++)
        {
            var height = stairs.Count == 1
                ? startHeight
                : (int)Math.Round(startHeight + (endHeight - startHeight) * (i / (double)(stairs.Count - 1)));
            stairs[i].FloorHeight = height;
            stairs[i].CeilingHeight = commonCeiling;
        }
    }

    private static RampDirection ChooseRiseDirection(Bounds bounds, IReadOnlyList<SideContact> contacts, RampDirection fallback)
    {
        var west = GetSideFloor(contacts, BoundarySide.West);
        var east = GetSideFloor(contacts, BoundarySide.East);
        var south = GetSideFloor(contacts, BoundarySide.South);
        var north = GetSideFloor(contacts, BoundarySide.North);

        var horizontalScore = (west.HasValue ? 1 : 0) + (east.HasValue ? 1 : 0);
        var verticalScore = (south.HasValue ? 1 : 0) + (north.HasValue ? 1 : 0);

        var useHorizontal = horizontalScore > verticalScore
            ? true
            : verticalScore > horizontalScore
                ? false
                : fallback is RampDirection.East or RampDirection.West;

        if (useHorizontal)
        {
            if (west.HasValue && east.HasValue)
            {
                if (east.Value > west.Value) return RampDirection.East;
                if (west.Value > east.Value) return RampDirection.West;
                return fallback is RampDirection.East or RampDirection.West ? fallback : RampDirection.East;
            }
            if (west.HasValue) return RampDirection.East;
            if (east.HasValue) return RampDirection.West;
        }
        else
        {
            if (south.HasValue && north.HasValue)
            {
                if (north.Value > south.Value) return RampDirection.North;
                if (south.Value > north.Value) return RampDirection.South;
                return fallback is RampDirection.North or RampDirection.South ? fallback : RampDirection.North;
            }
            if (south.HasValue) return RampDirection.North;
            if (north.HasValue) return RampDirection.South;
        }

        return fallback;
    }

    private static IEnumerable<SideContact> FindSideContacts(Bounds target, IEnumerable<Sector> sectors)
    {
        foreach (var sector in sectors)
        {
            if (sector.Vertices.Count < 3) continue;
            var other = GetBounds(sector);

            if (Math.Abs(other.MaxX - target.MinX) <= 1 && RangesOverlap(target.MinY, target.MaxY, other.MinY, other.MaxY))
                yield return new SideContact(BoundarySide.West, sector);
            if (Math.Abs(other.MinX - target.MaxX) <= 1 && RangesOverlap(target.MinY, target.MaxY, other.MinY, other.MaxY))
                yield return new SideContact(BoundarySide.East, sector);
            if (Math.Abs(other.MaxY - target.MinY) <= 1 && RangesOverlap(target.MinX, target.MaxX, other.MinX, other.MaxX))
                yield return new SideContact(BoundarySide.South, sector);
            if (Math.Abs(other.MinY - target.MaxY) <= 1 && RangesOverlap(target.MinX, target.MaxX, other.MinX, other.MaxX))
                yield return new SideContact(BoundarySide.North, sector);
        }
    }

    private static int? GetSideFloor(IEnumerable<SideContact> contacts, BoundarySide side)
    {
        var heights = contacts
            .Where(c => c.Side == side)
            .Select(c => c.Sector.FloorHeight)
            .ToList();
        if (heights.Count == 0) return null;
        return (int)Math.Round(heights.Average());
    }

    private static RampDirection GetDragDirection(int width, int depth, PointF dragStart, PointF dragEnd)
    {
        var dx = dragEnd.X - dragStart.X;
        var dy = dragEnd.Y - dragStart.Y;
        if (Math.Abs(dx) >= Math.Abs(dy))
            return dx >= 0 ? RampDirection.East : RampDirection.West;
        return dy >= 0 ? RampDirection.North : RampDirection.South;
    }

    private static RampDirection InferStairDirection(IReadOnlyList<Sector> stairs)
    {
        if (stairs.Count < 2) return RampDirection.East;
        var first = GetBounds(stairs[0]);
        var last = GetBounds(stairs[^1]);
        var firstX = (first.MinX + first.MaxX) / 2.0;
        var firstY = (first.MinY + first.MaxY) / 2.0;
        var lastX = (last.MinX + last.MaxX) / 2.0;
        var lastY = (last.MinY + last.MaxY) / 2.0;
        var dx = lastX - firstX;
        var dy = lastY - firstY;
        if (Math.Abs(dx) >= Math.Abs(dy))
            return dx >= 0 ? RampDirection.East : RampDirection.West;
        return dy >= 0 ? RampDirection.North : RampDirection.South;
    }

    private static BoundarySide StartSide(RampDirection direction) => direction switch
    {
        RampDirection.East => BoundarySide.West,
        RampDirection.West => BoundarySide.East,
        RampDirection.North => BoundarySide.South,
        RampDirection.South => BoundarySide.North,
        _ => BoundarySide.West
    };

    private static BoundarySide EndSide(RampDirection direction) => direction switch
    {
        RampDirection.East => BoundarySide.East,
        RampDirection.West => BoundarySide.West,
        RampDirection.North => BoundarySide.North,
        RampDirection.South => BoundarySide.South,
        _ => BoundarySide.East
    };

    private static RampDirection Opposite(RampDirection direction) => direction switch
    {
        RampDirection.East => RampDirection.West,
        RampDirection.West => RampDirection.East,
        RampDirection.North => RampDirection.South,
        RampDirection.South => RampDirection.North,
        _ => direction
    };

    private static Bounds GetBounds(Sector sector)
    {
        var minX = sector.Vertices.Min(v => v.X);
        var maxX = sector.Vertices.Max(v => v.X);
        var minY = sector.Vertices.Min(v => v.Y);
        var maxY = sector.Vertices.Max(v => v.Y);
        return new Bounds(minX, minY, maxX, maxY);
    }

    private static Bounds GetBounds(IReadOnlyList<Sector> sectors)
    {
        var vertices = sectors.SelectMany(s => s.Vertices).ToList();
        return new Bounds(
            vertices.Min(v => v.X),
            vertices.Min(v => v.Y),
            vertices.Max(v => v.X),
            vertices.Max(v => v.Y));
    }

    private static bool RangesOverlap(int a0, int a1, int b0, int b1)
        => Math.Min(a1, b1) - Math.Max(a0, b0) > 0;
}
