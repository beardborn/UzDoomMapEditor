namespace UzDoom.SpriteStudio;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        try
        {
            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, "SpriteStudio-startup-error.txt");
            try
            {
                File.WriteAllText(logPath, ex.ToString());
            }
            catch
            {
                // If logging itself fails, still show the original startup error.
            }

            MessageBox.Show(
                $"Sprite Studio could not start.\r\n\r\n{ex.Message}\r\n\r\nA diagnostic log was written beside the application when possible:\r\n{logPath}",
                "UzDoom Sprite Studio startup error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
