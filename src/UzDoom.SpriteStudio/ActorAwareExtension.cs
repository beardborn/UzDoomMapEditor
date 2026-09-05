using System.Reflection;
using UzDoom.Core;

namespace UzDoom.SpriteStudio;

internal static class ActorAwareExtension
{
    private static readonly FieldInfo? WadField = typeof(MainForm).GetField("_wad", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? PaletteField = typeof(MainForm).GetField("_palette", BindingFlags.Instance | BindingFlags.NonPublic);

    public static void Attach(MainForm form)
    {
        ArgumentNullException.ThrowIfNull(form);

        var changingTitle = false;
        void FixVersionTitle()
        {
            if (changingTitle || !form.Text.Contains("v0.3", StringComparison.Ordinal))
                return;
            changingTitle = true;
            form.Text = form.Text.Replace("v0.3", "v0.4", StringComparison.Ordinal);
            changingTitle = false;
        }

        FixVersionTitle();
        form.TextChanged += (_, _) => FixVersionTitle();

        var menu = form.Controls.OfType<MenuStrip>().FirstOrDefault();
        if (menu is not null)
        {
            var actors = new ToolStripMenuItem("Actors");
            actors.DropDownItems.Add("Actor / State Browser...", null, (_, _) => ShowActorBrowser(form));
            menu.Items.Add(actors);
        }

        var toolbar = form.Controls.OfType<ToolStrip>()
            .FirstOrDefault(control => control.GetType() == typeof(ToolStrip));
        if (toolbar is not null)
        {
            toolbar.Items.Add(new ToolStripSeparator());
            toolbar.Items.Add(new ToolStripButton("Actors", null, (_, _) => ShowActorBrowser(form)));
        }
    }

    private static void ShowActorBrowser(MainForm owner)
    {
        var wad = WadField?.GetValue(owner) as WadFile;
        var palette = PaletteField?.GetValue(owner) as DoomPalette;
        if (wad is null || palette is null)
        {
            MessageBox.Show(owner, "Open a WAD first.", "Actor States", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            var catalog = ActorDefinitionCatalog.FromWad(wad);
            if (catalog.Actors.Count == 0)
            {
                MessageBox.Show(
                    owner,
                    "No DECORATE/ZSCRIPT actors were found and no classic Doom sprite profiles matched this WAD.",
                    "Actor States",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            var resolver = new ActorSpriteResolver(wad);
            using var browser = new ActorBrowserForm(
                catalog,
                palette,
                resolver.Resolve,
                (family, frame) => NavigateToSprite(owner, family, frame));
            browser.ShowDialog(owner);
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, ex.Message, "Could not read actor states", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void NavigateToSprite(MainForm owner, string family, char frame)
    {
        var familyTree = Descendants<TreeView>(owner)
            .FirstOrDefault(tree => tree.Nodes.Cast<TreeNode>().Any(node => node.Tag is string));
        if (familyTree is null)
            return;

        var familyNode = familyTree.Nodes.Cast<TreeNode>()
            .FirstOrDefault(node => string.Equals(node.Tag as string, family, StringComparison.OrdinalIgnoreCase));
        if (familyNode is null)
            return;

        familyTree.SelectedNode = familyNode;
        familyNode.EnsureVisible();

        var spriteList = Descendants<ListView>(owner).FirstOrDefault();
        if (spriteList is null)
            return;

        foreach (ListViewItem item in spriteList.Items)
        {
            var info = SpriteNameParser.Parse(item.Text);
            if (!string.Equals(info.Family, family, StringComparison.OrdinalIgnoreCase)
                || !info.Slots.Any(slot => slot.Frame == frame))
            {
                continue;
            }

            item.Selected = true;
            item.Focused = true;
            item.EnsureVisible();
            spriteList.Focus();
            return;
        }
    }

    private static IEnumerable<T> Descendants<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match)
                yield return match;
            foreach (var descendant in Descendants<T>(child))
                yield return descendant;
        }
    }

    private sealed class ActorSpriteResolver
    {
        private readonly WadFile _wad;
        private readonly Dictionary<(string Family, char Frame, int Rotation), ActorPreviewFrame?> _cache = new();

        public ActorSpriteResolver(WadFile wad)
        {
            _wad = wad;
        }

        public ActorPreviewFrame? Resolve(string family, char frame, int rotation)
        {
            var key = (family.ToUpperInvariant(), char.ToUpperInvariant(frame), rotation);
            if (_cache.TryGetValue(key, out var cached))
                return cached;

            ActorPreviewFrame? fallback = null;
            foreach (var index in _wad.GetSpriteLumpIndices())
            {
                var lump = _wad.Lumps[index];
                var info = SpriteNameParser.Parse(lump.Name);
                if (!string.Equals(info.Family, key.Item1, StringComparison.OrdinalIgnoreCase))
                    continue;

                var slot = SpriteNameParser.FindSlot(info, key.Item2, rotation);
                if (slot is null)
                    continue;

                DoomPatchImage image;
                try
                {
                    image = DoomPatchCodec.Decode(lump.Data.Span);
                }
                catch (InvalidDataException)
                {
                    continue;
                }

                var candidate = new ActorPreviewFrame(lump.Name, image, slot.Value.Mirrored);
                if (slot.Value.Rotation == rotation)
                {
                    _cache[key] = candidate;
                    return candidate;
                }

                fallback ??= candidate;
            }

            _cache[key] = fallback;
            return fallback;
        }
    }
}
