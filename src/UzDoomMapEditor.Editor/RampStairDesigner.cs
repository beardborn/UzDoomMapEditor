using UzDoomMapEditor.Core;

namespace UzDoomMapEditor.Editor;

internal static class RampStairDesigner
{
    public const int DefaultDoorWidth = 128;
    public const int DefaultDoorDepth = 64;
    public const int DefaultRampRise = 64;
    public const int DefaultStairRise = 8;
    public const int DefaultTread = 32;

    public static Sector CreateRamp(string name, int x, int y, int width, int depth, PointF dragStart, PointF dragEnd)
    {
        var sector = Sector.Rectangle(name, x, y, width, depth);
        sector.FloorShape = SectorFloorShape.Ramp;
        sector.RampDirection = GetDirection(width, depth, dragStart, dragEnd);
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
        var direction = GetDirection(width, depth, dragStart, dragEnd);
        var horizontal = direction is RampDirection.East or RampDirection.West;
        var run = horizontal ? width : depth;
        var count = Math.Clamp((int)Math.Round(run / (double)DefaultTread), 2, 24);
        var result = new List<Sector>(count);

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
                var a = x + (int)Math.Round(width * t0);
                var b = x + (int)Math.Round(width * t1);
                if (direction == RampDirection.West)
                {
                    a = x + width - (int)Math.Round(width * t1);
                    b = x + width - (int)Math.Round(width * t0);
                }

                sx = Math.Min(a, b);
                sy = y;
                sw = Math.Max(1, Math.Abs(b - a));
                sd = depth;
            }
            else
            {
                var a = y + (int)Math.Round(depth * t0);
                var b = y + (int)Math.Round(depth * t1);
                if (direction == RampDirection.South)
                {
                    a = y + depth - (int)Math.Round(depth * t1);
                    b = y + depth - (int)Math.Round(depth * t0);
                }

                sx = x;
                sy = Math.Min(a, b);
                sw = width;
                sd = Math.Max(1, Math.Abs(b - a));
            }

            var step = sectorFactory($"{baseName} Step {i + 1}", sx, sy, sw, sd);
            step.FloorHeight = i * DefaultStairRise;
            step.CeilingHeight = Math.Max(step.FloorHeight + 64, 128);
            result.Add(step);
        }

        return result;
    }

    public static void MatchRampToNeighbours(Sector ramp, IEnumerable<Sector> sectors)
    {
        var bounds = GetBounds(ramp);
        var horizontal = ramp.RampDirection is RampDirection.East or RampDirection.West;
        var positive = ramp.RampDirection is RampDirection.East or RampDirection.North;
        Sector? startSector = null;
        Sector? endSector = null;

        foreach (var sector in sectors.Where(s => !ReferenceEquals(s, ramp)))
        {
            var other = GetBounds(sector);
            if (horizontal && RangesOverlap(bounds.Top, bounds.Bottom, other.Top, other.Bottom))
            {
                if (Math.Abs(other.Right - bounds.Left) <= 1)
                {
                    if (positive) startSector = sector;
                    else endSector = sector;
                }

                if (Math.Abs(other.Left - bounds.Right) <= 1)
                {
                    if (positive) endSector = sector;
                    else startSector = sector;
                }
            }
            else if (!horizontal && RangesOverlap(bounds.Left, bounds.Right, other.Left, other.Right))
            {
                if (Math.Abs(other.Bottom - bounds.Top) <= 1)
                {
                    if (positive) startSector = sector;
                    else endSector = sector;
                }

                if (Math.Abs(other.Top - bounds.Bottom) <= 1)
                {
                    if (positive) endSector = sector;
                    else startSector = sector;
                }
            }
        }

        if (startSector is not null)
            ramp.FloorHeight = startSector.FloorHeight;
        if (endSector is not null)
            ramp.RampEndHeight = endSector.FloorHeight;
        if (ramp.RampEndHeight == ramp.FloorHeight)
            ramp.RampEndHeight = ramp.FloorHeight + DefaultRampRise;
    }

    public static void MatchStairsToNeighbours(IReadOnlyList<Sector> stairs, IEnumerable<Sector> sectors)
    {
        if (stairs.Count == 0) return;

        var others = sectors.Except(stairs).ToList();
        var startHeight = FindTouchingFloor(stairs[0], others) ?? 0;
        var endHeight = FindTouchingFloor(stairs[^1], others) ?? startHeight + DefaultStairRise * stairs.Count;
        if (endHeight == startHeight)
            endHeight = startHeight + DefaultStairRise * stairs.Count;

        for (var i = 0; i < stairs.Count; i++)
        {
            var height = stairs.Count == 1
                ? startHeight
                : (int)Math.Round(startHeight + (endHeight - startHeight) * (i / (double)(stairs.Count - 1)));
            stairs[i].FloorHeight = height;
            stairs[i].CeilingHeight = Math.Max(height + 64, 128);
        }
    }

    private static int? FindTouchingFloor(Sector target, IEnumerable<Sector> sectors)
    {
        var a = GetBounds(target);
        foreach (var sector in sectors)
        {
            var b = GetBounds(sector);
            var touchesX = (Math.Abs(a.Right - b.Left) <= 1 || Math.Abs(a.Left - b.Right) <= 1) &&
                           RangesOverlap(a.Top, a.Bottom, b.Top, b.Bottom);
            var touchesY = (Math.Abs(a.Bottom - b.Top) <= 1 || Math.Abs(a.Top - b.Bottom) <= 1) &&
                           RangesOverlap(a.Left, a.Right, b.Left, b.Right);
            if (touchesX || touchesY)
                return sector.FloorHeight;
        }

        return null;
    }

    private static RampDirection GetDirection(int width, int depth, PointF dragStart, PointF dragEnd)
    {
        if (width >= depth)
            return dragEnd.X >= dragStart.X ? RampDirection.East : RampDirection.West;
        return dragEnd.Y >= dragStart.Y ? RampDirection.North : RampDirection.South;
    }

    private static Rectangle GetBounds(Sector sector)
    {
        var minX = sector.Vertices.Min(v => v.X);
        var maxX = sector.Vertices.Max(v => v.X);
        var minY = sector.Vertices.Min(v => v.Y);
        var maxY = sector.Vertices.Max(v => v.Y);
        return Rectangle.FromLTRB(minX, minY, maxX, maxY);
    }

    private static bool RangesOverlap(int a0, int a1, int b0, int b1)
        => Math.Min(a1, b1) - Math.Max(a0, b0) > 0;
}
