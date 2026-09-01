namespace UzDoomMapEditor.Editor;

/// <summary>
/// .NET 10 WinForms rejects some perfectly useful keys (notably Home) when they
/// are assigned to ToolStripMenuItem.ShortcutKeys. The editor should still
/// start even when a menu hint uses one of those keys, so this wrapper falls
/// back to display-only text for unsupported shortcuts.
/// </summary>
public class ToolStripMenuItem : System.Windows.Forms.ToolStripMenuItem
{
    public ToolStripMenuItem(string? text)
        : base(text)
    {
    }

    public ToolStripMenuItem(string? text, System.Drawing.Image? image, EventHandler? onClick)
        : base(text, image, onClick)
    {
    }

    public ToolStripMenuItem(string? text, System.Drawing.Image? image, EventHandler? onClick, Keys shortcutKeys)
        : base(text, image, onClick)
    {
        try
        {
            ShortcutKeys = shortcutKeys;
        }
        catch (ArgumentException)
        {
            // Some navigation keys (for example Home) are refused by the
            // WinForms ToolStrip shortcut validator. Keep the menu item and
            // show the intended key instead of crashing the whole editor.
            ShortcutKeys = Keys.None;
            ShortcutKeyDisplayString = shortcutKeys.ToString();
        }
    }
}
