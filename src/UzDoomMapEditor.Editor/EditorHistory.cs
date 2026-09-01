using System.Text.Json;
using UzDoomMapEditor.Core;

namespace UzDoomMapEditor.Editor;

internal sealed class EditorHistory
{
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();
    private readonly JsonSerializerOptions _json = new() { WriteIndented = false };
    private string _current = string.Empty;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Reset(EditorProject project)
    {
        _undo.Clear();
        _redo.Clear();
        _current = Serialize(project);
    }

    public void Commit(EditorProject project)
    {
        var next = Serialize(project);
        if (next == _current) return;

        if (!string.IsNullOrEmpty(_current))
            _undo.Push(_current);
        _redo.Clear();
        _current = next;
    }

    public bool TryUndo(out EditorProject project)
    {
        if (_undo.Count == 0)
        {
            project = null!;
            return false;
        }

        _redo.Push(_current);
        _current = _undo.Pop();
        project = Deserialize(_current);
        return true;
    }

    public bool TryRedo(out EditorProject project)
    {
        if (_redo.Count == 0)
        {
            project = null!;
            return false;
        }

        _undo.Push(_current);
        _current = _redo.Pop();
        project = Deserialize(_current);
        return true;
    }

    private string Serialize(EditorProject project) => JsonSerializer.Serialize(project, _json);

    private EditorProject Deserialize(string json)
    {
        var project = JsonSerializer.Deserialize<EditorProject>(json, _json) ?? new EditorProject();
        project.Normalize();
        return project;
    }
}
