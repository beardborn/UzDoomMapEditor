using UzDoomMapEditor.Core;

namespace UzDoomMapEditor.Editor;

/// <summary>
/// Editor-facing UDMF export shim.
///
/// The core exporter originally emitted "repeatable = true" for action lines.
/// In ZDoom/UZDoom UDMF the actual field name is "repeatspecial". Unknown UDMF
/// fields are ignored, which made each side of a generated door behave like a
/// one-shot trigger. Keep the correction here so every editor export/test path
/// is fixed without duplicating the exporter.
/// </summary>
internal static class UdmfExporter
{
    public static string BuildText(EditorProject project)
    {
        return UzDoomMapEditor.Core.UdmfExporter
            .BuildText(project)
            .Replace("    repeatable = true;", "    repeatspecial = true;", StringComparison.Ordinal);
    }
}
