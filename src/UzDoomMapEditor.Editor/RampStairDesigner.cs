using System.ComponentModel;
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
        var candidates = sectors.Where(s => !ReferenceEquals(s, ramp)).ToList();
        if (candidates.Count == 0) return;

        var bounds = GetBounds(ramp);
        var horizontal = ramp.RampDirection is RampDirection.East or RampDirection.West;
        var startSide = ramp.RampDirection is RampDirection.East or RampDirection.North ? 0 : 1;

        Sector? low = null;
        Sector? high = null;
        foreach (var sector in candidates)
        {
            var other = GetBounds(sector);
            if (horizontal)
            {
                if (RangesOverlap(bounds.Top, bounds.Bottom, other.Top, other.Bottom))
                {
                    if (Math.Abs(other.Right - bounds.Left) <= 1) low = startSide == 0 ? sector : high;
                    if (Math.Abs(other.Left - bounds.Right) <= 1) high = startSide == 0 ? sector : low;
                }
            }
            else
            {
                if (RangesOverlap(bounds.Left, bounds.Right, other.Left, other.Right))
                {
                    if (Math.Abs(other.Bottom - bounds.Top) <= 1) low = startSide == 0 ? sector : high;
                    if (Math.Abs(other.Top - bounds.Bottom) <= 1) high = startSide == 0 ? sector : low;
                }
            }
        }

        if (low is not null)
            ramp.FloorHeight = low.FloorHeight;
        if (high is not null)
            ramp.RampEndHeight = high.FloorHeight;
        else if (ramp.RampEndHeight == ramp.FloorHeight)
            ramp.RampEndHeight = ramp.FloorHeight + DefaultRampRise;
    }

    public static void MatchStairsToNeighbours(IReadOnlyList<Sector> stairs, IEnumerable<Sector> sectors)
    {
        if (stairs.Count == 0) return;
        var first = stairs[0];
        var last = stairs[^1];
        var all = sectors.Except(stairs).ToList();
        var start = FindTouchingFloor(first, all);
        var end = FindTouchingFloor(last, all);

        var startHeight = start ?? 0;
        var endHeight = end ?? startHeight + DefaultStairRise * stairs.Count;
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
