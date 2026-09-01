namespace UzDoomMapEditor.Editor;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var mainForm = new MainForm();
        DarkTheme.Apply(mainForm);
        Application.Run(mainForm);
    }
}
