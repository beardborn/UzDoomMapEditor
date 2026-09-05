using System.Text;
using System.Text.RegularExpressions;

namespace UzDoom.Core;

public sealed record ActorSpriteFrame(string Family, char Frame, int Tics, bool Bright);

public sealed record ActorStateDefinition(
    string Label,
    IReadOnlyList<ActorSpriteFrame> Frames,
    string? FlowControl = null)
{
    public string FriendlyName => ActorStateNames.GetFriendlyName(Label);
}

public sealed record ActorDefinition(
    string Name,
    string? Parent,
    string Source,
    IReadOnlyList<ActorStateDefinition> States)
{
    public IReadOnlyList<string> SpriteFamilies => States
        .SelectMany(state => state.Frames)
        .Select(frame => frame.Family)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

public sealed class ActorDefinitionCatalog
{
    public ActorDefinitionCatalog(
        IReadOnlyList<ActorDefinition> actors,
        IReadOnlyList<string> sources,
        bool usedClassicFallback)
    {
        Actors = actors;
        Sources = sources;
        UsedClassicFallback = usedClassicFallback;
    }

    public IReadOnlyList<ActorDefinition> Actors { get; }
    public IReadOnlyList<string> Sources { get; }
    public bool UsedClassicFallback { get; }

    public static ActorDefinitionCatalog FromWad(WadFile wad, bool includeClassicFallback = true)
    {
        ArgumentNullException.ThrowIfNull(wad);

        var actors = new List<ActorDefinition>();
        var sources = new List<string>();

        foreach (var lump in wad.Lumps)
        {
            if (!IsActorDefinitionLump(lump.Name) || lump.Data.Length == 0)
                continue;

            var text = Encoding.UTF8.GetString(lump.Data.Span);
            var parsed = ActorDefinitionParser.Parse(text, lump.Name);
            if (parsed.Count == 0)
                continue;

            actors.AddRange(parsed);
            sources.Add(lump.Name);
        }

        if (actors.Count > 0)
        {
            var normalized = actors
                .GroupBy(actor => actor.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .OrderBy(actor => actor.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new ActorDefinitionCatalog(
                normalized,
                sources.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                usedClassicFallback: false);
        }

        if (!includeClassicFallback)
            return new ActorDefinitionCatalog(Array.Empty<ActorDefinition>(), Array.Empty<string>(), false);

        var fallback = ClassicDoomActorCatalog.Build(wad);
        return new ActorDefinitionCatalog(
            fallback,
            fallback.Count == 0 ? Array.Empty<string>() : new[] { "Classic Doom fallback profiles" },
            fallback.Count > 0);
    }

    private static bool IsActorDefinitionLump(string name)
        => name.Equals("DECORATE", StringComparison.OrdinalIgnoreCase)
           || name.Equals("ZSCRIPT", StringComparison.OrdinalIgnoreCase);
}

public static class ActorDefinitionParser
{
    private static readonly Regex ActorHeader = new(
        @"\b(?<kind>actor|class)\s+(?<name>[A-Za-z_][A-Za-z0-9_.]*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ParentHeader = new(
        @":\s*(?<parent>[A-Za-z_][A-Za-z0-9_.]*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex StatesKeyword = new(
        @"\bstates\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LabelPattern = new(
        @"^(?<label>[A-Za-z_][A-Za-z0-9_.]*)\s*:\s*(?<rest>.*)$",
        RegexOptions.Compiled);

    private static readonly Regex SpritePattern = new(
        "^\\s*\\\"?(?<family>[A-Za-z0-9_]{4})\\\"?\\s+(?<frames>[A-Za-z]+)\\s+(?<tics>-?\\d+)(?<rest>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex FlowPattern = new(
        @"^(?<flow>goto|loop|stop|wait|fail)\b(?<target>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IReadOnlyList<ActorDefinition> Parse(string sourceText, string sourceName = "script")
    {
        if (string.IsNullOrWhiteSpace(sourceText))
            return Array.Empty<ActorDefinition>();

        var text = StripComments(sourceText);
        var actors = new List<ActorDefinition>();
        var scan = 0;

        while (scan < text.Length)
        {
            var header = ActorHeader.Match(text, scan);
            if (!header.Success)
                break;

            var openBrace = FindNextStructuralBrace(text, header.Index + header.Length);
            if (openBrace < 0)
            {
                scan = header.Index + header.Length;
                continue;
            }

            var closeBrace = FindMatchingBrace(text, openBrace);
            if (closeBrace < 0)
            {
                scan = header.Index + header.Length;
                continue;
            }

            var between = text[(header.Index + header.Length)..openBrace];
            var parentMatch = ParentHeader.Match(between);
            var parent = parentMatch.Success ? parentMatch.Groups["parent"].Value : null;
            var body = text[(openBrace + 1)..closeBrace];
            var states = ParseStates(body);

            if (states.Count > 0)
            {
                actors.Add(new ActorDefinition(
                    header.Groups["name"].Value,
                    parent,
                    sourceName,
                    states));
            }

            scan = closeBrace + 1;
        }

        return actors;
    }

    private static IReadOnlyList<ActorStateDefinition> ParseStates(string actorBody)
    {
        var statesMatch = StatesKeyword.Match(actorBody);
        if (!statesMatch.Success)
            return Array.Empty<ActorStateDefinition>();

        var openBrace = FindNextStructuralBrace(actorBody, statesMatch.Index + statesMatch.Length);
        if (openBrace < 0)
            return Array.Empty<ActorStateDefinition>();

        var closeBrace = FindMatchingBrace(actorBody, openBrace);
        if (closeBrace < 0)
            return Array.Empty<ActorStateDefinition>();

        var stateBody = actorBody[(openBrace + 1)..closeBrace];
        var result = new List<ActorStateDefinition>();
        StateBuilder? current = null;

        foreach (var rawStatement in Regex.Split(stateBody, @"\r?\n|;"))
        {
            var statement = rawStatement.Trim();
            if (statement.Length == 0 || statement is "{" or "}")
                continue;

            var labelMatch = LabelPattern.Match(statement);
            if (labelMatch.Success)
            {
                if (current is not null)
                    result.Add(current.Build());

                current = new StateBuilder(labelMatch.Groups["label"].Value);
                statement = labelMatch.Groups["rest"].Value.Trim();
                if (statement.Length == 0)
                    continue;
            }

            if (current is null)
                continue;

            var flowMatch = FlowPattern.Match(statement);
            if (flowMatch.Success)
            {
                var target = flowMatch.Groups["target"].Value.Trim();
                current.FlowControl = target.Length == 0
                    ? flowMatch.Groups["flow"].Value
                    : $"{flowMatch.Groups["flow"].Value} {target}";
                continue;
            }

            var spriteMatch = SpritePattern.Match(statement);
            if (!spriteMatch.Success)
                continue;

            var family = spriteMatch.Groups["family"].Value.ToUpperInvariant();
            var frameText = spriteMatch.Groups["frames"].Value.ToUpperInvariant();
            if (!int.TryParse(spriteMatch.Groups["tics"].Value, out var tics))
                continue;

            var bright = Regex.IsMatch(spriteMatch.Groups["rest"].Value, @"\bbright\b", RegexOptions.IgnoreCase);
            foreach (var frame in frameText)
            {
                if (frame is < 'A' or > 'Z')
                    continue;
                current.Frames.Add(new ActorSpriteFrame(family, frame, tics, bright));
            }
        }

        if (current is not null)
            result.Add(current.Build());

        return result
            .Where(state => state.Frames.Count > 0 || !string.IsNullOrWhiteSpace(state.FlowControl))
            .ToArray();
    }

    private static int FindNextStructuralBrace(string text, int start)
    {
        var inString = false;
        var escaped = false;

        for (var i = Math.Max(0, start); i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                    inString = false;
                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c == '{')
                return i;

            if (c == ';')
                return -1;
        }

        return -1;
    }

    private static int FindMatchingBrace(string text, int openBrace)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = openBrace; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                    inString = false;
                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c == '{')
                depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }

        return -1;
    }

    private static string StripComments(string source)
    {
        var output = new StringBuilder(source.Length);
        var inLineComment = false;
        var inBlockComment = false;
        var inString = false;
        var escaped = false;

        for (var i = 0; i < source.Length; i++)
        {
            var c = source[i];
            var next = i + 1 < source.Length ? source[i + 1] : '\0';

            if (inLineComment)
            {
                if (c == '\n')
                {
                    inLineComment = false;
                    output.Append(c);
                }
                else
                {
                    output.Append(' ');
                }
                continue;
            }

            if (inBlockComment)
            {
                if (c == '*' && next == '/')
                {
                    output.Append("  ");
                    i++;
                    inBlockComment = false;
                }
                else
                {
                    output.Append(c is '\r' or '\n' ? c : ' ');
                }
                continue;
            }

            if (inString)
            {
                output.Append(c);
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (c == '\\')
                    escaped = true;
                else if (c == '"')
                    inString = false;
                continue;
            }

            if (c == '"')
            {
                inString = true;
                output.Append(c);
                continue;
            }

            if (c == '/' && next == '/')
            {
                output.Append("  ");
                i++;
                inLineComment = true;
                continue;
            }

            if (c == '/' && next == '*')
            {
                output.Append("  ");
                i++;
                inBlockComment = true;
                continue;
            }

            output.Append(c);
        }

        return output.ToString();
    }

    private sealed class StateBuilder(string label)
    {
        public string Label { get; } = label;
        public List<ActorSpriteFrame> Frames { get; } = new();
        public string? FlowControl { get; set; }

        public ActorStateDefinition Build() => new(Label, Frames.ToArray(), FlowControl);
    }
}

public static class ActorStateNames
{
    public static string GetFriendlyName(string label)
    {
        return label.ToUpperInvariant() switch
        {
            "SPAWN" => "Idle / Spawn",
            "SEE" => "Walk / See",
            "MISSILE" => "Ranged Attack / Missile",
            "MELEE" => "Melee Attack",
            "PAIN" => "Pain",
            "DEATH" => "Death",
            "XDEATH" => "Extreme Death",
            "RAISE" => "Raise / Resurrect",
            "READY" => "Idle / Ready",
            "SELECT" => "Raise / Select",
            "DESELECT" => "Lower / Deselect",
            "FIRE" => "Fire",
            "HOLD" => "Hold Fire",
            "ALTFIRE" => "Alternate Fire",
            "ALTHOLD" => "Hold Alternate Fire",
            "FLASH" => "Muzzle Flash",
            "RELOAD" => "Reload",
            "INACTIVE" => "Inactive",
            "ACTIVE" => "Active",
            _ => label
        };
    }
}

internal static class ClassicDoomActorCatalog
{
    public static IReadOnlyList<ActorDefinition> Build(WadFile wad)
    {
        var familyFrames = GetAvailableFrames(wad);
        var actors = new List<ActorDefinition>();

        AddMonster(actors, familyFrames, "ZombieMan", "POSS",
            ("Spawn", "AB", 10), ("See", "AABBCCDD", 4), ("Missile", "EFE", 8),
            ("Pain", "GG", 3), ("Death", "HIJKL", 5), ("XDeath", "MNOPQRS", 5));
        AddMonster(actors, familyFrames, "ShotgunGuy", "SPOS",
            ("Spawn", "AB", 10), ("See", "AABBCCDD", 3), ("Missile", "EFE", 10),
            ("Pain", "GG", 3), ("Death", "HIJKL", 5), ("XDeath", "MNOPQRS", 5));
        AddMonster(actors, familyFrames, "ChaingunGuy", "CPOS",
            ("Spawn", "AB", 10), ("See", "AABBCCDD", 3), ("Missile", "EFEF", 4),
            ("Pain", "GG", 3), ("Death", "HIJKLMN", 5), ("XDeath", "OPQRSTU", 5));
        AddMonster(actors, familyFrames, "DoomImp", "TROO",
            ("Spawn", "AB", 10), ("See", "AABBCCDD", 3), ("Melee", "EFG", 8),
            ("Missile", "EFG", 8), ("Pain", "HH", 2), ("Death", "IJKLM", 6),
            ("XDeath", "NOPQRSTU", 5));
        AddMonster(actors, familyFrames, "Demon", "SARG",
            ("Spawn", "AB", 10), ("See", "AABBCCDD", 2), ("Melee", "EFG", 8),
            ("Pain", "HH", 2), ("Death", "IJKLMN", 5));
        AddMonster(actors, familyFrames, "Cacodemon", "HEAD",
            ("Spawn", "A", 10), ("See", "A", 3), ("Missile", "BCD", 5),
            ("Pain", "EE", 3), ("Death", "FGHIJKL", 8));
        AddMonster(actors, familyFrames, "BaronOfHell", "BOSS",
            ("Spawn", "AB", 10), ("See", "AABBCCDD", 3), ("Melee", "EFG", 8),
            ("Missile", "EFG", 8), ("Pain", "HH", 2), ("Death", "IJKLMN", 8));
        AddMonster(actors, familyFrames, "HellKnight", "BOS2",
            ("Spawn", "AB", 10), ("See", "AABBCCDD", 3), ("Melee", "EFG", 8),
            ("Missile", "EFG", 8), ("Pain", "HH", 2), ("Death", "IJKLMN", 8));
        AddMonster(actors, familyFrames, "Revenant", "SKEL",
            ("Spawn", "AB", 10), ("See", "AABBCCDD", 2), ("Melee", "EFG", 6),
            ("Missile", "JKLM", 6), ("Pain", "NN", 5), ("Death", "OPQRST", 5));
        AddMonster(actors, familyFrames, "Mancubus", "FATT",
            ("Spawn", "AB", 15), ("See", "AABBCCDD", 4), ("Missile", "EFGHIJKLMN", 5),
            ("Pain", "OO", 3), ("Death", "PQRSTUVW", 5));
        AddMonster(actors, familyFrames, "Arachnotron", "BSPI",
            ("Spawn", "AB", 10), ("See", "AABBCCDD", 3), ("Missile", "AB", 4),
            ("Pain", "II", 3), ("Death", "JKLMNOP", 5));
        AddMonster(actors, familyFrames, "Archvile", "VILE",
            ("Spawn", "AB", 10), ("See", "AABBCCDD", 2), ("Missile", "EFGHIJKLMNOP", 5),
            ("Pain", "QQ", 5), ("Death", "RSTUVWXY", 7));
        AddMonster(actors, familyFrames, "Cyberdemon", "CYBR",
            ("Spawn", "AB", 10), ("See", "AABBCCDD", 3), ("Missile", "EFEFEF", 8),
            ("Pain", "G", 10), ("Death", "HIJKLMNOP", 10));
        AddMonster(actors, familyFrames, "SpiderMastermind", "SPID",
            ("Spawn", "AB", 10), ("See", "AABBCCDD", 3), ("Missile", "ABCD", 4),
            ("Pain", "HH", 3), ("Death", "IJKLMNOPQRST", 8));
        AddMonster(actors, familyFrames, "LostSoul", "SKUL",
            ("Spawn", "AB", 10), ("See", "AB", 6), ("Missile", "CD", 6),
            ("Pain", "E", 3), ("Death", "FGHIJK", 6));
        AddMonster(actors, familyFrames, "PainElemental", "PAIN",
            ("Spawn", "A", 10), ("See", "AABBCCDD", 3), ("Missile", "DEFG", 5),
            ("Pain", "HH", 3), ("Death", "IJKLMN", 8));
        AddMonster(actors, familyFrames, "Player", "PLAY",
            ("Spawn", "A", 10), ("See", "ABCD", 4), ("Missile", "E", 12),
            ("Pain", "G", 4), ("Death", "HIJKLMN", 6), ("XDeath", "OPQRSTUV", 5));

        AddWeapon(actors, familyFrames, "Fist", "PUNG", null);
        AddWeapon(actors, familyFrames, "Pistol", "PISG", "PISF");
        AddWeapon(actors, familyFrames, "Shotgun", "SHTG", "SHTF");
        AddWeapon(actors, familyFrames, "SuperShotgun", "SHT2", "SHT2");
        AddWeapon(actors, familyFrames, "Chaingun", "CHGG", "CHGF");
        AddWeapon(actors, familyFrames, "RocketLauncher", "MISG", "MISF");
        AddWeapon(actors, familyFrames, "PlasmaRifle", "PLSG", "PLSF");
        AddWeapon(actors, familyFrames, "BFG9000", "BFGG", "BFGF");
        AddWeapon(actors, familyFrames, "Chainsaw", "SAWG", null);

        return actors.OrderBy(actor => actor.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static Dictionary<string, SortedSet<char>> GetAvailableFrames(WadFile wad)
    {
        var result = new Dictionary<string, SortedSet<char>>(StringComparer.OrdinalIgnoreCase);
        foreach (var index in wad.GetSpriteLumpIndices())
        {
            var name = wad.Lumps[index].Name.ToUpperInvariant();
            if (name.Length < 5)
                continue;

            var family = name[..4];
            if (!result.TryGetValue(family, out var frames))
            {
                frames = new SortedSet<char>();
                result[family] = frames;
            }

            if (name[4] is >= 'A' and <= 'Z')
                frames.Add(name[4]);
            if (name.Length >= 7 && name[6] is >= 'A' and <= 'Z')
                frames.Add(name[6]);
        }

        return result;
    }

    private static void AddMonster(
        List<ActorDefinition> actors,
        IReadOnlyDictionary<string, SortedSet<char>> familyFrames,
        string name,
        string family,
        params (string Label, string Frames, int Tics)[] specs)
    {
        if (!familyFrames.TryGetValue(family, out var available) || available.Count == 0)
            return;

        var states = new List<ActorStateDefinition>();
        foreach (var spec in specs)
        {
            var frames = spec.Frames
                .Where(available.Contains)
                .Select(frame => new ActorSpriteFrame(family, frame, spec.Tics, false))
                .ToArray();
            if (frames.Length > 0)
                states.Add(new ActorStateDefinition(spec.Label, frames));
        }

        if (states.Count > 0)
            actors.Add(new ActorDefinition(name, "Actor", "Classic Doom fallback", states));
    }

    private static void AddWeapon(
        List<ActorDefinition> actors,
        IReadOnlyDictionary<string, SortedSet<char>> familyFrames,
        string name,
        string family,
        string? flashFamily)
    {
        if (!familyFrames.TryGetValue(family, out var available) || available.Count == 0)
            return;

        var ordered = available.ToArray();
        var ready = new[] { new ActorSpriteFrame(family, ordered[0], 1, false) };
        var fireFrames = ordered.Length > 1 ? ordered[1..] : ordered;
        var states = new List<ActorStateDefinition>
        {
            new("Ready", ready),
            new("Select", ready),
            new("Deselect", ready),
            new("Fire", fireFrames.Select(frame => new ActorSpriteFrame(family, frame, 4, false)).ToArray())
        };

        if (flashFamily is not null && familyFrames.TryGetValue(flashFamily, out var flashes) && flashes.Count > 0)
        {
            states.Add(new ActorStateDefinition(
                "Flash",
                flashes.Select(frame => new ActorSpriteFrame(flashFamily, frame, 4, true)).ToArray()));
        }

        actors.Add(new ActorDefinition(name, "Weapon", "Classic Doom fallback", states));
    }
}
