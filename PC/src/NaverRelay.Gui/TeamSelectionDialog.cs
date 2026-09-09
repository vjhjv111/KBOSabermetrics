using NaverRelay.Application.Teams;
using NaverRelay.Gui.Services;

namespace NaverRelay.Gui;

internal sealed class TeamSelectionDialog : Form
{
    private readonly ITeamPageService _service;
    private readonly TextBox _query = new() { Dock = DockStyle.Fill, PlaceholderText = "팀명 또는 팀 코드를 입력하세요." };
    private readonly Button _search = new() { Text = "검색", Width = 72, Dock = DockStyle.Fill };
    private readonly DataGridView _grid = CreateGrid();
    private readonly Label _count = new() { AutoSize = true, Text = "0팀", Anchor = AnchorStyles.Left };
    private readonly Button _open = new() { Text = "팀 페이지 열기", Width = 125, Enabled = false };
    private readonly Button _cancel = new() { Text = "닫기", Width = 80, DialogResult = DialogResult.Cancel };
    private IReadOnlyList<TeamSearchItem> _teams = Array.Empty<TeamSearchItem>();

    public string? SelectedTeamCode { get; private set; }

    public TeamSelectionDialog(ITeamPageService service, string? initialQuery = null)
    {
        _service = service;
        Text = "팀 선택";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(640, 420);
        ClientSize = new Size(760, 520);
        Font = new Font("맑은 고딕", 9F);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 3,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        var searchPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        searchPanel.Controls.Add(_query, 0, 0);
        searchPanel.Controls.Add(_search, 1, 0);

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.Controls.Add(_count, 0, 0);
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        buttons.Controls.Add(_open);
        buttons.Controls.Add(_cancel);
        footer.Controls.Add(buttons, 1, 0);

        layout.Controls.Add(searchPanel, 0, 0);
        layout.Controls.Add(_grid, 0, 1);
        layout.Controls.Add(footer, 0, 2);
        Controls.Add(layout);

        AcceptButton = _search;
        CancelButton = _cancel;
        _search.Click += (_, _) => ApplyFilter();
        _query.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode != Keys.Enter) return;
            ApplyFilter();
            eventArgs.SuppressKeyPress = true;
        };
        _grid.SelectionChanged += (_, _) => _open.Enabled = SelectedItem() is not null;
        _grid.CellDoubleClick += (_, eventArgs) => { if (eventArgs.RowIndex >= 0) OpenSelected(); };
        _grid.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode != Keys.Enter) return;
            OpenSelected();
            eventArgs.SuppressKeyPress = true;
        };
        _open.Click += (_, _) => OpenSelected();

        _query.Text = initialQuery?.Trim() ?? string.Empty;
        Shown += (_, _) =>
        {
            _teams = _service.GetTeams();
            ApplyFilter();
            _query.Focus();
            _query.SelectAll();
        };
    }

    private void ApplyFilter()
    {
        var query = _query.Text.Trim();
        var rows = string.IsNullOrWhiteSpace(query)
            ? _teams
            : _teams.Where(item =>
                    item.TeamCode.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    item.LatestName.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                .ToList();

        _grid.DataSource = null;
        _grid.DataSource = new SortableBindingList<TeamSearchItem>(rows);
        foreach (DataGridViewColumn column in _grid.Columns)
            column.SortMode = DataGridViewColumnSortMode.Automatic;
        GridNumberFormatter.Apply(_grid);
        _count.Text = $"검색 결과 {rows.Count:N0}팀";
        _open.Enabled = rows.Count > 0;
        if (rows.Count > 0)
        {
            _grid.ClearSelection();
            _grid.Rows[0].Selected = true;
            _grid.CurrentCell = _grid.Rows[0].Cells[0];
        }
    }

    private TeamSearchItem? SelectedItem() =>
        _grid.CurrentRow?.DataBoundItem as TeamSearchItem;

    private void OpenSelected()
    {
        var item = SelectedItem();
        if (item is null) return;
        SelectedTeamCode = item.TeamCode;
        DialogResult = DialogResult.OK;
        Close();
    }

    private static DataGridView CreateGrid() => new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToOrderColumns = true,
        AllowUserToResizeRows = false,
        AutoGenerateColumns = true,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        BackgroundColor = SystemColors.Window,
        BorderStyle = BorderStyle.FixedSingle,
        MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        RowHeadersVisible = false,
    };
}
