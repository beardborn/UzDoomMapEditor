namespace UzDoom.SpriteStudio;

internal readonly record struct SpriteSlot(char Frame, int Rotation, bool Mirrored);

internal sealed record SpriteNameInfo(string Family, IReadOnlyList<SpriteSlot> Slots)
{
    public string FrameText => Slots.Count == 0
        ? "?"
        : string.Join(", ", Slots.Select(s => s.Frame));

    public string RotationText => Slots.Count == 0
        ? "?"
        : string.Join(", ", Slots.Select(s => s.Rotation == 0 ? "All" : s.Rotation.ToString()));
}

internal static class SpriteNameParser
{
    public static SpriteNameInfo Parse(string lumpName)
    {
        var name = (lumpName ?? string.Empty).Trim().ToUpperInvariant();
        if (name.Length < 4)
            return new SpriteNameInfo(name, Array.Empty<SpriteSlot>());

        var family = name[..4];
        var slots = new List<SpriteSlot>(2);

        if (name.Length >= 6 && IsFrame(name[4]) && IsRotation(name[5]))
            slots.Add(new SpriteSlot(name[4], name[5] - '0', false));
        else if (name.Length >= 5 && IsFrame(name[4]))
            slots.Add(new SpriteSlot(name[4], 0, false));

        // Doom commonly stores two rotations in one lump, with the second pair mirrored.
        // Example: POSSA2A8 means frame A rotation 2 plus mirrored frame A rotation 8.
        if (name.Length >= 8 && IsFrame(name[6]) && IsRotation(name[7]))
            slots.Add(new SpriteSlot(name[6], name[7] - '0', true));

        return new SpriteNameInfo(family, slots);
    }

    public static bool SupportsRotation(SpriteNameInfo info, int rotation)
        => info.Slots.Any(slot => slot.Rotation == 0 || slot.Rotation == rotation);

    public static SpriteSlot? FindSlot(SpriteNameInfo info, char frame, int rotation)
    {
        var exact = info.Slots.FirstOrDefault(slot => slot.Frame == frame && slot.Rotation == rotation);
        if (exact != default)
            return exact;

        var allRotations = info.Slots.FirstOrDefault(slot => slot.Frame == frame && slot.Rotation == 0);
        return allRotations == default ? null : allRotations;
    }

    private static bool IsFrame(char c) => c is >= 'A' and <= 'Z';
    private static bool IsRotation(char c) => c is >= '0' and <= '8';
}
