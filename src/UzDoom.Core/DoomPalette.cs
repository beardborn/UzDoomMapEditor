namespace UzDoom.Core;

public readonly record struct DoomRgb(byte R, byte G, byte B);

public sealed class DoomPalette
{
    private readonly DoomRgb[] _colors;

    private DoomPalette(DoomRgb[] colors)
    {
        _colors = colors;
    }

    public IReadOnlyList<DoomRgb> Colors => _colors;

    public static DoomPalette FromPlaypal(ReadOnlySpan<byte> data, int paletteIndex = 0)
    {
        const int colorsPerPalette = 256;
        const int bytesPerPalette = colorsPerPalette * 3;

        if (paletteIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(paletteIndex));

        var start = checked(paletteIndex * bytesPerPalette);
        if (data.Length < start + bytesPerPalette)
            throw new InvalidDataException("PLAYPAL does not contain the requested 256-colour palette.");

        var colors = new DoomRgb[colorsPerPalette];
        for (var i = 0; i < colorsPerPalette; i++)
        {
            var p = start + i * 3;
            colors[i] = new DoomRgb(data[p], data[p + 1], data[p + 2]);
        }

        return new DoomPalette(colors);
    }

    public int FindNearestIndex(byte r, byte g, byte b)
    {
        var bestIndex = 0;
        var bestDistance = int.MaxValue;

        for (var i = 0; i < _colors.Length; i++)
        {
            var color = _colors[i];
            var dr = r - color.R;
            var dg = g - color.G;
            var db = b - color.B;
            var distance = dr * dr + dg * dg + db * db;

            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            bestIndex = i;
            if (distance == 0)
                break;
        }

        return bestIndex;
    }
}
