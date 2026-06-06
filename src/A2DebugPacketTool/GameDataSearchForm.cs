using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace A2DebugPacketTool;

internal sealed record GameDataSearchItem(int Id, string Name, string Detail, int? DungeonId = null)
{
    public string SearchText => $"{Id} {Name} {Detail}".ToLowerInvariant();
}

internal sealed class GameDataSearchForm : Form
{
    private readonly List<GameDataSearchItem> _items;
    private readonly TextBox _search = new() { Dock = DockStyle.Top };
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        AutoGenerateColumns = false,
        MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        RowHeadersVisible = false,
    };
    private readonly Button _ok = new() { Text = "적용", DialogResult = DialogResult.OK, Width = 90 };
    private readonly Button _cancel = new() { Text = "취소", DialogResult = DialogResult.Cancel, Width = 90 };

    public GameDataSearchForm(string title, List<GameDataSearchItem> items)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(780, 520);
        MinimumSize = new Size(620, 420);
        _items = items;
        SelectedItem = null;

        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ID", DataPropertyName = nameof(GameDataSearchItem.Id), Width = 100 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Name", DataPropertyName = nameof(GameDataSearchItem.Name), Width = 240 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Detail", DataPropertyName = nameof(GameDataSearchItem.Detail), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 42,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8),
        };
        buttons.Controls.Add(_ok);
        buttons.Controls.Add(_cancel);

        Controls.Add(_grid);
        Controls.Add(buttons);
        Controls.Add(_search);

        AcceptButton = _ok;
        CancelButton = _cancel;

        _search.TextChanged += (_, _) => ApplyFilter();
        _grid.CellDoubleClick += (_, _) => AcceptCurrent();
        _ok.Click += (_, _) => SelectCurrent();
        Shown += (_, _) =>
        {
            ApplyFilter();
            _search.Focus();
        };
    }

    public GameDataSearchItem? SelectedItem { get; private set; }

    private void ApplyFilter()
    {
        string q = _search.Text.Trim().ToLowerInvariant();
        var filtered = string.IsNullOrWhiteSpace(q)
            ? _items
            : _items.Where(item => item.SearchText.Contains(q)).ToList();
        _grid.DataSource = filtered;
        if (_grid.Rows.Count > 0)
            _grid.Rows[0].Selected = true;
    }

    private void AcceptCurrent()
    {
        SelectCurrent();
        if (SelectedItem is null) return;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void SelectCurrent()
    {
        if (_grid.CurrentRow?.DataBoundItem is GameDataSearchItem item)
            SelectedItem = item;
    }
}
