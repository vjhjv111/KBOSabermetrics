using NaverRelay.Application.Players;
using NaverRelay.Application.Teams;
using NaverRelay.Gui.Services;

namespace NaverRelay.Gui;

internal sealed class TeamDetailForm : Form
{
    private readonly ITeamPageService _teamService;
    private readonly IPlayerPageService _playerService;
    private readonly string _teamCode;
    private TeamPageData? _data;

    private readonly Label _name = new() { AutoSize = true, Font = new Font("맑은 고딕", 18F, FontStyle.Bold) };
    private readonly Label _meta = new() { AutoSize = true, Font = new Font("맑은 고딕", 10F) };
    private readonly Label _career = new() { AutoSize = true, Font = new Font("맑은 고딕", 10F, FontStyle.Bold) };
    private readonly ComboBox _seasonFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _battingGrid = CreateGrid();
    private readonly DataGridView _pitchingGrid = CreateGrid();
    private readonly DataGridView _valueGrid = CreateGrid();
    private readonly DataGridView _playerBattingGrid = CreateGrid();
    private readonly DataGridView _playerPitchingGrid = CreateGrid();
    private readonly DataGridView _opponentGrid = CreateGrid();
    private readonly DataGridView _situationGrid = CreateGrid();
    private readonly DataGridView _battingPitchTypeGrid = CreateGrid();
    private readonly DataGridView _pitchingPitchTypeGrid = CreateGrid();
    private readonly DataGridView _gameLogGrid = CreateGrid();
    private readonly ComboBox _situationCategory = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 135 };
    private readonly RichTextBox _formula = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        WordWrap = false,
        Font = new Font("Consolas", 10F),
        BackColor = SystemColors.Window,
    };
    private readonly ToolStripStatusLabel _status = new("팀 데이터를 읽는 중입니다...");

    public TeamDetailForm(
        ITeamPageService teamService,
        IPlayerPageService playerService,
        string teamCode)
    {
        _teamService = teamService;
        _playerService = playerService;
        _teamCode = teamCode;
        Text = "팀 상세";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1100, 720);
        ClientSize = new Size(1500, 880);
        Font = new Font("맑은 고딕", 9F);
        KeyPreview = true;

        BuildLayout();
        ConfigureGrids();
        Shown += async (_, _) => await LoadTeamAsync();
        _seasonFilter.SelectedIndexChanged += (_, _) => ApplySeasonFilter();
        _situationCategory.SelectedIndexChanged += (_, _) => ApplySeasonFilter();
        KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode == Keys.Escape) Close();
        };
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.FromArgb(245, 247, 250),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 135));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20, 12, 20, 10),
            BackColor = Color.White,
            ColumnCount = 2,
            RowCount = 3,
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        header.Controls.Add(_name, 0, 0);

        var filters = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Margin = new Padding(0, 5, 0, 0),
        };
        filters.Controls.Add(new Label { Text = "표시 시즌:", AutoSize = true, Margin = new Padding(0, 6, 5, 0) });
        filters.Controls.Add(_seasonFilter);
        header.Controls.Add(filters, 1, 0);
        header.Controls.Add(_meta, 0, 1);
        header.SetColumnSpan(_meta, 2);
        header.Controls.Add(_career, 0, 2);
        header.SetColumnSpan(_career, 2);

        AddTab("연도별 타격", _battingGrid);
        AddTab("연도별 투구", _pitchingGrid);
        AddTab("Value·WAR", _valueGrid);
        AddTab("선수별 타격", _playerBattingGrid);
        AddTab("선수별 투구", _playerPitchingGrid);
        AddTab("상대전적", _opponentGrid);
        AddSituationTab();
        AddPitchTypeTab();
        AddTab("경기 로그", _gameLogGrid);
        AddTab("계산 근거", _formula);

        var statusStrip = new StatusStrip();
        statusStrip.Items.Add(_status);
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(_tabs, 0, 1);
        root.Controls.Add(statusStrip, 0, 2);
        Controls.Add(root);
    }

    private TabPage AddTab(string title, Control control)
    {
        var tab = new TabPage(title) { Padding = new Padding(3) };
        tab.Controls.Add(control);
        _tabs.TabPages.Add(tab);
        return tab;
    }

    private void AddSituationTab()
    {
        var tab = new TabPage("상황별") { Padding = new Padding(4) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        tools.Controls.Add(new Label { Text = "상황 구분:", AutoSize = true, Margin = new Padding(4, 8, 3, 0) });
        _situationCategory.Items.AddRange([
            "전체", "주자", "아웃", "이닝", "점수차", "득점권", "클러치", "장소", "선두타자", "2아웃"
        ]);
        _situationCategory.SelectedIndex = 0;
        tools.Controls.Add(_situationCategory);
        layout.Controls.Add(tools, 0, 0);
        layout.Controls.Add(_situationGrid, 0, 1);
        tab.Controls.Add(layout);
        _tabs.TabPages.Add(tab);
    }

    private void AddPitchTypeTab()
    {
        var tab = new TabPage("구종별") { Padding = new Padding(4) };
        var inner = new TabControl { Dock = DockStyle.Fill };
        var batting = new TabPage("팀 타격(최종구 기준)");
        batting.Controls.Add(_battingPitchTypeGrid);
        var pitching = new TabPage("팀 투구 성과");
        pitching.Controls.Add(_pitchingPitchTypeGrid);
        inner.TabPages.Add(batting);
        inner.TabPages.Add(pitching);
        tab.Controls.Add(inner);
        _tabs.TabPages.Add(tab);
    }

    private void ConfigureGrids()
    {
        foreach (var grid in new[]
                 {
                     _battingGrid, _pitchingGrid, _valueGrid, _playerBattingGrid,
                     _playerPitchingGrid, _opponentGrid, _situationGrid,
                     _battingPitchTypeGrid, _pitchingPitchTypeGrid, _gameLogGrid,
                 })
        {
            grid.DataBindingComplete += (_, _) =>
            {
                foreach (DataGridViewColumn column in grid.Columns)
                    column.SortMode = DataGridViewColumnSortMode.Automatic;
                GridNumberFormatter.Apply(grid);
            };
        }

        _playerBattingGrid.CellDoubleClick += (_, eventArgs) => OpenPlayerFromGrid(_playerBattingGrid, eventArgs.RowIndex);
        _playerPitchingGrid.CellDoubleClick += (_, eventArgs) => OpenPlayerFromGrid(_playerPitchingGrid, eventArgs.RowIndex);
        _opponentGrid.CellDoubleClick += (_, eventArgs) => OpenOpponentTeam(eventArgs.RowIndex);
    }

    private async Task LoadTeamAsync()
    {
        try
        {
            UseWaitCursor = true;
            Enabled = false;
            _status.Text = "관계형 SQLite에서 팀 통계를 집계하고 있습니다...";
            _data = await Task.Run(() => _teamService.GetTeamPage(_teamCode));
            if (_data is null)
            {
                MessageBox.Show(this, "정규시즌 팀 정보를 찾지 못했습니다.", "팀 없음",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                Close();
                return;
            }

            var profile = _data.Profile;
            _name.Text = $"{profile.LatestName}  [{profile.TeamCode}]";
            _meta.Text = $"팀명 이력 {profile.NameHistory} · 사용 구장 {profile.StadiumHistory} · 활동 시즌 {profile.FirstSeason}~{profile.LastSeason}";
            var winPct = profile.WinningPercentage.HasValue ? profile.WinningPercentage.Value.ToString("0.000") : "-";
            _career.Text = $"정규시즌  G {profile.Games:N0}   {profile.Wins:N0}승 {profile.Losses:N0}패 {profile.Ties:N0}무   승률 {winPct}   득실차 {profile.RunDifferential:+#;-#;0}";
            Text = $"{profile.LatestName} - 팀 상세";

            var years = _data.BattingSeasons.Select(row => row.Year)
                .Concat(_data.PitchingSeasons.Select(row => row.Year))
                .Concat(_data.GameLogs.Where(row => row.Year.HasValue).Select(row => row.Year!.Value))
                .Distinct()
                .OrderByDescending(year => year)
                .ToList();
            _seasonFilter.Items.Clear();
            _seasonFilter.Items.Add("전체 시즌");
            foreach (var year in years) _seasonFilter.Items.Add(year.ToString());
            _seasonFilter.SelectedIndex = 0;
            _formula.Text = _data.FormulaDocumentation;
            _status.Text = $"연도별 {years.Count:N0}시즌, 상대전적 {_data.OpponentRecords.Count:N0}행, 구종별 타격 {_data.BattingByPitchType.Count:N0}행";
        }
        catch (Exception ex)
        {
            _status.Text = "팀 페이지 생성 실패";
            MessageBox.Show(this, ex.ToString(), "팀 페이지 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Enabled = true;
            UseWaitCursor = false;
        }
    }

    private void ApplySeasonFilter()
    {
        if (_data is null) return;
        var selected = _seasonFilter.SelectedItem?.ToString() ?? "전체 시즌";
        var hasYear = int.TryParse(selected, out var year);
        var category = _situationCategory.SelectedItem?.ToString() ?? "전체";

        Bind(_battingGrid, hasYear ? _data.BattingSeasons.Where(row => row.Year == year) : _data.BattingSeasons);
        Bind(_pitchingGrid, hasYear ? _data.PitchingSeasons.Where(row => row.Year == year) : _data.PitchingSeasons);
        Bind(_valueGrid, hasYear ? _data.ValueSeasons.Where(row => row.Year == year) : _data.ValueSeasons);
        Bind(_playerBattingGrid, hasYear ? _data.PlayerBatting.Where(row => row.Year == year) : _data.PlayerBatting);
        Bind(_playerPitchingGrid, hasYear ? _data.PlayerPitching.Where(row => row.Year == year) : _data.PlayerPitching);
        Bind(_opponentGrid, hasYear ? _data.OpponentRecords.Where(row => row.Year == year) : _data.OpponentRecords);
        Bind(_situationGrid, _data.SituationSplits.Where(row =>
            (!hasYear || row.Year == year) && (category == "전체" || row.Category == category)));
        Bind(_battingPitchTypeGrid, hasYear
            ? _data.BattingByPitchType.Where(row => row.Year == year)
            : _data.BattingByPitchType);
        Bind(_pitchingPitchTypeGrid, hasYear
            ? _data.PitchingByPitchType.Where(row => row.Year == year)
            : _data.PitchingByPitchType);
        Bind(_gameLogGrid, hasYear ? _data.GameLogs.Where(row => row.Year == year) : _data.GameLogs);
    }

    private void OpenPlayerFromGrid(DataGridView grid, int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= grid.Rows.Count) return;
        var item = grid.Rows[rowIndex].DataBoundItem;
        var pcode = item?.GetType().GetProperty("Pcode")?.GetValue(item)?.ToString();
        if (string.IsNullOrWhiteSpace(pcode)) return;
        new PlayerDetailForm(_playerService, pcode).Show(this);
    }

    private void OpenOpponentTeam(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _opponentGrid.Rows.Count) return;
        if (_opponentGrid.Rows[rowIndex].DataBoundItem is not TeamOpponentRecordRow row ||
            string.IsNullOrWhiteSpace(row.OpponentCode) ||
            string.Equals(row.OpponentCode, _teamCode, StringComparison.OrdinalIgnoreCase))
            return;
        new TeamDetailForm(_teamService, _playerService, row.OpponentCode).Show(this);
    }

    private static void Bind<T>(DataGridView grid, IEnumerable<T> rows)
    {
        var list = rows.ToList();
        grid.DataSource = null;
        grid.DataSource = new SortableBindingList<T>(list);
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
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
        BackgroundColor = SystemColors.Window,
        BorderStyle = BorderStyle.FixedSingle,
        MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        RowHeadersVisible = false,
        ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText,
    };
}
