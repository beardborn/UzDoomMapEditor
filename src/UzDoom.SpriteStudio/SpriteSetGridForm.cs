using UzDoom.Core;

namespace UzDoom.SpriteStudio;

internal sealed record SpriteGridItem(string Name, DoomPatchImage Image, SpriteNameInfo NameInfo);

internal sealed class SpriteSetGridForm : Form
{
    private readonly DataGridView _grid = new();
    private readonly IReadOnlyList<SpriteGridItem> _items;
    private readonly Action<string> _selectSprite;

    public SpriteSetGridForm(string family, IReadOnlyList<SpriteGridItem> items, Action<string> selectSprite)
    {
        _items = items;
        _selectSprite = selectSprite;

        Text = $"Sprite Set Grid - {family}";
        StartPosition = FormStartPosition.CenterParent;
        Width = 1180;
        Height = 680;
        MinimumSize = new Size(820, 480);
        BackColor = Color.FromArgb(30, 32, 36);
        ForeColor = Color.Gainsboro;

        var help = new Label
        {
            Dock = DockStyle.Top,
            Height = 48,
            Padding = new Padding(10, 12, 10, 0),
            Text = "Rows are animation frames. Columns are Doom rotations. Double-click a cell to jump to that sprite.  ↔ means the stored lump is mirrored for that rotation.",
            ForeColor = Color.Silver
        };

        ConfigureGrid();
        Controls.Add(_grid);
        Controls.Add(help);
        Populate();
    }

    private void ConfigureGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.ReadOnly = true;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _grid.RowHeadersVisible = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.BackgroundColor = Color.FromArgb(25, 27, 31);
        _grid.BorderStyle = BorderStyle.None;
        _grid.GridColor = Color.FromArgb(55, 58, 64);
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(43, 45, 50);
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        _grid.DefaultCellStyle.BackColor = Color.FromArgb(31, 33, 38);
        _grid.DefaultCellStyle.ForeColor = Color.Gainsboro;
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(66, 83, 116);
        _grid.DefaultCellStyle.SelectionForeColor = Color.White;
        _grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _grid.RowTemplate.Height = 58;
        _grid.CellDoubleClick += (_, e) => JumpToCell(e.RowIndex, e.ColumnIndex);
    }

    private void Populate()
    {
        _grid.Columns.Clear();
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Frame", HeaderText = "Frame", FillWeight = 45 });
        for (var rotation = 1; rotation <= 8; rotation++)
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = $"R{rotation}", HeaderText = $"Rotation {rotation}" });

        var frames = _items
            .SelectMany(item => item.NameInfo.Slots.Select(slot => slot.Frame))
            .Distinct()
            .OrderBy(frame => frame)
            .ToList();

        foreach (var frame in frames)
        {
            var row = new object?[9];
            row[0] = frame.ToString();
            for (var rotation = 1; rotation <= 8; rotation++)
            {
                var match = Find(frame, rotation);
                row[rotation] = match is null
                    ? ""
                    : match.Value.Slot.Mirrored ? match.Value.Item.Name + "  ↔" : match.Value.Item.Name;
            }
            _grid.Rows.Add(row);
        }
    }

    private (SpriteGridItem Item, SpriteSlot Slot)? Find(char frame, int rotation)
    {
        foreach (var item in _items)
        {
            var slot = SpriteNameParser.FindSlot(item.NameInfo, frame, rotation);
            if (slot is not null)
                return (item, slot.Value);
        }
        return null;
    }

    private void JumpToCell(int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || columnIndex <= 0 || rowIndex >= _grid.Rows.Count)
            return;

        var frameText = _grid.Rows[rowIndex].Cells[0].Value?.ToString();
        if (string.IsNullOrWhiteSpace(frameText))
            return;

        var frame = frameText[0];
        var match = Find(frame, columnIndex);
        if (match is null)
            return;

        _selectSprite(match.Value.Item.Name);
        DialogResult = DialogResult.OK;
        Close();
    }
}
