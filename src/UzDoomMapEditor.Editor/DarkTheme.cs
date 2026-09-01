using System.Runtime.InteropServices;

namespace UzDoomMapEditor.Editor;

internal static class DarkTheme
{
    public static readonly Color Window = Color.FromArgb(24, 25, 28);
    public static readonly Color Panel = Color.FromArgb(30, 32, 36);
    public static readonly Color Surface = Color.FromArgb(37, 39, 44);
    public static readonly Color SurfaceRaised = Color.FromArgb(45, 48, 54);
    public static readonly Color Border = Color.FromArgb(64, 68, 76);
    public static readonly Color Text = Color.FromArgb(226, 229, 234);
    public static readonly Color MutedText = Color.FromArgb(170, 176, 186);
    public static readonly Color Accent = Color.FromArgb(72, 158, 214);
    public static readonly Color AccentHover = Color.FromArgb(58, 122, 165);

    public static void Apply(Form form)
    {
        form.BackColor = Window;
        form.ForeColor = Text;

        var menu = FindControl<MenuStrip>(form);
        var status = FindControl<StatusStrip>(form);
        var properties = FindControl<PropertyGrid>(form);
        var toolbar = FindControls<ToolStrip>(form)
            .FirstOrDefault(strip => strip is not MenuStrip && strip is not StatusStrip);

        var renderer = new ToolStripProfessionalRenderer(new DarkColorTable());

        if (menu is not null)
        {
            menu.Renderer = renderer;
            StyleToolStrip(menu);
        }

        if (toolbar is not null)
        {
            toolbar.Renderer = renderer;
            StyleToolStrip(toolbar);
        }

        if (status is not null)
        {
            status.Renderer = renderer;
            StyleToolStrip(status);
        }

        if (properties is not null)
            StylePropertyGrid(properties);

        ApplyRecursive(form);
        TryEnableDarkTitleBar(form);
    }

    private static void StylePropertyGrid(PropertyGrid properties)
    {
        properties.BackColor = Panel;
        properties.ViewBackColor = Panel;
        properties.ViewForeColor = Text;
        properties.CategoryForeColor = Color.FromArgb(178, 205, 224);
        properties.HelpBackColor = Surface;
        properties.HelpForeColor = Text;
        properties.CommandsBackColor = Surface;
        properties.CommandsForeColor = Text;
        properties.LineColor = Border;
    }

    private static T? FindControl<T>(Control parent) where T : Control
        => FindControls<T>(parent).FirstOrDefault();

    private static IEnumerable<T> FindControls<T>(Control parent) where T : Control
    {
        foreach (Control child in parent.Controls)
        {
            if (child is T match)
                yield return match;

            foreach (var nested in FindControls<T>(child))
                yield return nested;
        }
    }

    private static void ApplyRecursive(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            switch (control)
            {
                // These already paint their own dark editor backgrounds.
                case MapCanvas:
                case Map3DPreview:
                    break;

                case PropertyGrid:
                    break;

                case SplitContainer split:
                    split.BackColor = Border;
                    split.Panel1.BackColor = Window;
                    split.Panel2.BackColor = Panel;
                    break;

                case MenuStrip:
                case ToolStrip:
                case StatusStrip:
                    break;

                case TextBoxBase textBox:
                    textBox.BackColor = Surface;
                    textBox.ForeColor = Text;
                    break;

                case ListView listView:
                    listView.BackColor = Panel;
                    listView.ForeColor = Text;
                    break;

                case TreeView treeView:
                    treeView.BackColor = Panel;
                    treeView.ForeColor = Text;
                    treeView.LineColor = Border;
                    break;

                default:
                    control.BackColor = Panel;
                    control.ForeColor = Text;
                    break;
            }

            if (control.HasChildren)
                ApplyRecursive(control);
        }
    }

    private static void StyleToolStrip(ToolStrip strip)
    {
        strip.BackColor = Surface;
        strip.ForeColor = Text;

        foreach (ToolStripItem item in strip.Items)
            StyleToolStripItem(item);
    }

    private static void StyleToolStripItem(ToolStripItem item)
    {
        item.BackColor = Surface;
        item.ForeColor = Text;

        if (item is ToolStripComboBox combo)
        {
            combo.ComboBox.BackColor = SurfaceRaised;
            combo.ComboBox.ForeColor = Text;
            combo.ComboBox.FlatStyle = FlatStyle.Flat;
        }

        if (item is ToolStripDropDownItem dropDown)
        {
            dropDown.DropDown.BackColor = Surface;
            dropDown.DropDown.ForeColor = Text;
            dropDown.DropDown.Renderer = new ToolStripProfessionalRenderer(new DarkColorTable());

            foreach (ToolStripItem child in dropDown.DropDownItems)
                StyleToolStripItem(child);
        }
    }

    private sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color ToolStripGradientBegin => Surface;
        public override Color ToolStripGradientMiddle => Surface;
        public override Color ToolStripGradientEnd => Surface;
        public override Color MenuStripGradientBegin => Surface;
        public override Color MenuStripGradientEnd => Surface;
        public override Color StatusStripGradientBegin => Surface;
        public override Color StatusStripGradientEnd => Surface;
        public override Color ToolStripBorder => Border;
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => AccentHover;
        public override Color MenuItemSelected => SurfaceRaised;
        public override Color MenuItemSelectedGradientBegin => SurfaceRaised;
        public override Color MenuItemSelectedGradientEnd => SurfaceRaised;
        public override Color MenuItemPressedGradientBegin => SurfaceRaised;
        public override Color MenuItemPressedGradientMiddle => SurfaceRaised;
        public override Color MenuItemPressedGradientEnd => SurfaceRaised;
        public override Color ButtonSelectedBorder => Accent;
        public override Color ButtonSelectedGradientBegin => SurfaceRaised;
        public override Color ButtonSelectedGradientMiddle => SurfaceRaised;
        public override Color ButtonSelectedGradientEnd => SurfaceRaised;
        public override Color ButtonPressedBorder => Accent;
        public override Color ButtonPressedGradientBegin => AccentHover;
        public override Color ButtonPressedGradientMiddle => AccentHover;
        public override Color ButtonPressedGradientEnd => AccentHover;
        public override Color ButtonCheckedGradientBegin => AccentHover;
        public override Color ButtonCheckedGradientMiddle => AccentHover;
        public override Color ButtonCheckedGradientEnd => AccentHover;
        public override Color SeparatorDark => Color.FromArgb(22, 23, 26);
        public override Color SeparatorLight => Border;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color CheckBackground => AccentHover;
        public override Color CheckSelectedBackground => Accent;
        public override Color CheckPressedBackground => Accent;
    }

    private static void TryEnableDarkTitleBar(Form form)
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            // DWMWA_USE_IMMERSIVE_DARK_MODE. 20 is supported on current Windows 10/11.
            var enabled = 1;
            _ = DwmSetWindowAttribute(form.Handle, 20, ref enabled, sizeof(int));
        }
        catch
        {
            // Cosmetic only. The editor remains usable if DWM refuses the hint.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
