using System.ComponentModel;
using NaverRelay.Application.Players;
using NaverRelay.Application.Queries;
using NaverRelay.Application.Statistics;
using NaverRelay.Application.Teams;
using NaverRelay.Gui.Services;
using NaverRelay.Infrastructure.Sqlite;

namespace NaverRelay.Gui;

/// <summary>
/// 시즌/통산/팀/연도별 상수로 나눈 기록실 중심 메인 화면입니다.
/// 데이터 적재와 진단 기능은 기존 MainForm을 "데이터 관리" 창으로 분리합니다.
/// </summary>
internal sealed class RecordRoomMainForm : Form
{
    private static readonly Color Accent = Color.FromArgb(232, 24, 92);
    private static readonly Color Surface = Color.White;
    private static readonly Color Background = Color.FromArgb(246, 247, 249);
    private static readonly Color Border = Color.FromArgb(222, 225, 230);
    private static readonly Color Muted = Color.FromArgb(95, 101, 112);

    private readonly DatabaseCacheService _database = new();
    private readonly DatabaseAnalyticsService _analytics;
    private readonly DatabaseBatterRecordRoomService _batterDetails;
    private readonly DatabasePitcherRecordRoomService _pitcherDetails;
    private DatabasePlayerPageService? _playerService;
    private DatabaseTeamPageService? _teamService;

    private DatabaseCatalog? _catalog;
    private LeagueReference? _league;
    private PitcherWarDiagnosticBundle? _warDiagnostics;
    private ParkFactorDiagnosticBundle? _parkDiagnostics;
    private ParkFactorV2ExperimentBundle? _parkV2Experiment;
    private ParkFactorSensitivityBundle? _parkSensitivity;
    private ReplacementSensitivityBundle? _replacementSensitivity;
    private WarDistributionDiagnosticBundle? _warDistribution;
    private AnalyticsSnapshot? _snapshot;
    private string? _snapshotKey;
    private CancellationTokenSource? _loadCts;
    private bool _initialized;
    private bool _suppressEvents;
    private bool _advancedVisible;

    private RecordRoomSection _section = RecordRoomSection.Season;
    private RecordRoomRole _role = RecordRoomRole.Batter;
    private string _subView = "기본";

    private readonly TableLayoutPanel _root = new();
    private readonly FlowLayoutPanel _roomNavigation = new();
    private readonly FlowLayoutPanel _roleNavigation = new();
    private readonly FlowLayoutPanel _subNavigation = new();
    private readonly Panel _filterContainer = new();
    private readonly FlowLayoutPanel _mainFilters = new();
    private readonly FlowLayoutPanel _advancedFilters = new();
    private readonly DataGridView _grid = CreateGrid();
    private readonly Panel _leagueOverview = new() { Dock = DockStyle.Fill, Visible = false, BackColor = Color.White, Padding = new Padding(4) };
    private readonly TableLayoutPanel _gridLayout = new()
    {
        Dock = DockStyle.Fill,
        ColumnCount = 1,
        RowCount = 2,
        BackColor = Surface,
        Padding = new Padding(10, 8, 10, 6),
    };
    private readonly Label _leagueOverviewTitle = new() { AutoSize = false, Dock = DockStyle.Left, Width = 100, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("맑은 고딕", 10F, FontStyle.Bold) };
    private readonly FlowLayoutPanel _leagueOverviewMetrics = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoScroll = true, Padding = new Padding(4, 4, 4, 4) };
    private readonly Label _roomTitle = new() { AutoSize = true, Font = new Font("맑은 고딕", 18F, FontStyle.Bold) };
    private readonly Label _roomDescription = new() { AutoSize = true, ForeColor = Muted, Margin = new Padding(8, 7, 0, 0) };
    private readonly Label _status = new() { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Muted };
    private readonly Label _rowCount = new() { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, ForeColor = Muted };
    private readonly ProgressBar _progress = new() { Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 24, Visible = false, Width = 120, Height = 18 };

    private readonly ComboBox _year = Combo(84);
    private readonly ComboBox _competition = Combo(100);
    private readonly ComboBox _team = Combo(88);
    private readonly ComboBox _position = Combo(75);
    private readonly Label _positionLabel = FilterLabel("포지션");
    private readonly ComboBox _qualification = Combo(90);
    private readonly Label _qualificationLabel = FilterLabel("규정타석");
    private readonly ComboBox _period = Combo(105);
    private readonly DateTimePicker _startDate = DatePicker();
    private readonly DateTimePicker _endDate = DatePicker();
    private readonly ComboBox _opponent = Combo(86);
    private readonly ComboBox _venue = Combo(75);
    private readonly ComboBox _weekday = Combo(70);
    private readonly ComboBox _stadium = Combo(95);
    private readonly ComboBox _inning = Combo(82);
    private readonly ComboBox _outs = Combo(72);
    private readonly ComboBox _runners = Combo(92);
    private readonly ComboBox _scoreSituation = Combo(108);
    private readonly ComboBox _count = Combo(80);
    private readonly ComboBox _batOrder = Combo(72);
    private readonly TextBox _gridSearch = new() { Width = 150, PlaceholderText = "현재 표 검색" };
    private readonly TextBox _playerNameFilter = new() { Width = 105, PlaceholderText = "선수명" };
    private readonly ComboBox _resultLimit = Combo(72);
    private readonly ComboBox _stat1 = Combo(112);
    private readonly ComboBox _stat1Operator = Combo(70);
    private readonly TextBox _stat1Value = new() { Width = 72, PlaceholderText = "값" };
    private readonly ComboBox _stat2 = Combo(112);
    private readonly ComboBox _stat2Operator = Combo(70);
    private readonly TextBox _stat2Value = new() { Width = 72, PlaceholderText = "값" };
    private readonly Button _filterResetButton = ActionButton("초기화");

    private readonly Button _advancedButton = ActionButton("상세 필터");
    private readonly Button _refreshButton = ActionButton("조회");
    private readonly Button _csvButton = ActionButton("CSV");
    private readonly Button _playerButton = ActionButton("선수 검색");
    private readonly Button _dataManagerButton = ActionButton("데이터 관리");

    private readonly Dictionary<RecordRoomSection, Button> _roomButtons = new();
    private readonly Dictionary<RecordRoomRole, Button> _roleButtons = new();
    private readonly Dictionary<string, Button> _subButtons = new(StringComparer.Ordinal);

    public RecordRoomMainForm()
    {
        _analytics = new DatabaseAnalyticsService(_database);
        _batterDetails = new DatabaseBatterRecordRoomService(_database);
        _pitcherDetails = new DatabasePitcherRecordRoomService(_database);

        Text = "Naver Relay 세이버매트릭스 기록실";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1180, 720);
        ClientSize = new Size(1580, 900);
        Font = new Font("맑은 고딕", 9F);
        BackColor = Background;
        KeyPreview = true;

        BuildLayout();
        InitializeFilterItems();
        WireEvents();
        ApplySectionState();

        Shown += async (_, _) => await InitializeAsync();
        FormClosed += (_, _) => _loadCts?.Cancel();
    }

    private void BuildLayout()
    {
        _root.Dock = DockStyle.Fill;
        _root.ColumnCount = 1;
        _root.RowCount = 6;
        _root.BackColor = Background;
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

        _root.Controls.Add(BuildHeader(), 0, 0);
        _root.Controls.Add(BuildRoleBar(), 0, 1);
        _root.Controls.Add(BuildFilterPanel(), 0, 2);
        _root.Controls.Add(BuildSubNavigation(), 0, 3);
        _root.Controls.Add(BuildGridPanel(), 0, 4);
        _root.Controls.Add(BuildStatusBar(), 0, 5);
        Controls.Add(_root);
    }

    private Control BuildHeader()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Surface,
            Padding = new Padding(14, 10, 14, 8),
            ColumnCount = 3,
            RowCount = 1,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        panel.Controls.Add(new Label
        {
            Text = "기록실",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("맑은 고딕", 20F, FontStyle.Bold),
            ForeColor = Color.FromArgb(35, 38, 44),
        }, 0, 0);

        _roomNavigation.Dock = DockStyle.Fill;
        _roomNavigation.FlowDirection = FlowDirection.LeftToRight;
        _roomNavigation.WrapContents = false;
        _roomNavigation.Padding = new Padding(0, 7, 0, 0);
        AddRoomButton(RecordRoomSection.Season, "시즌기록실");
        AddRoomButton(RecordRoomSection.Career, "통산기록실");
        AddRoomButton(RecordRoomSection.Team, "팀기록실");
        AddRoomButton(RecordRoomSection.Constants, "연도별 상수");
        panel.Controls.Add(_roomNavigation, 1, 0);

        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Anchor = AnchorStyles.Right,
            Padding = new Padding(0, 6, 0, 0),
        };
        actions.Controls.Add(_playerButton);
        actions.Controls.Add(_dataManagerButton);
        panel.Controls.Add(actions, 2, 0);
        return panel;
    }

    private Control BuildRoleBar()
    {
        var container = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Background,
            Padding = new Padding(14, 4, 14, 3),
            ColumnCount = 2,
            RowCount = 1,
        };
        container.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        container.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _roleNavigation.AutoSize = true;
        _roleNavigation.FlowDirection = FlowDirection.LeftToRight;
        _roleNavigation.WrapContents = false;
        AddRoleButton(RecordRoomRole.Batter, "타자");
        AddRoleButton(RecordRoomRole.Pitcher, "투수");
        container.Controls.Add(_roleNavigation, 0, 0);

        var titlePanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(14, 1, 0, 0),
        };
        titlePanel.Controls.Add(_roomTitle);
        titlePanel.Controls.Add(_roomDescription);
        container.Controls.Add(titlePanel, 1, 0);
        return container;
    }

    private Control BuildFilterPanel()
    {
        _filterContainer.Dock = DockStyle.Fill;
        _filterContainer.BackColor = Surface;
        _filterContainer.Padding = new Padding(12, 5, 12, 5);

        _mainFilters.Dock = DockStyle.Top;
        _mainFilters.Height = 42;
        _mainFilters.FlowDirection = FlowDirection.LeftToRight;
        _mainFilters.WrapContents = false;
        _mainFilters.Controls.Add(FilterLabel("연도"));
        _mainFilters.Controls.Add(_year);
        _mainFilters.Controls.Add(FilterLabel("경기"));
        _mainFilters.Controls.Add(_competition);
        _mainFilters.Controls.Add(FilterLabel("팀"));
        _mainFilters.Controls.Add(_team);
        _mainFilters.Controls.Add(_positionLabel);
        _mainFilters.Controls.Add(_position);
        _mainFilters.Controls.Add(_qualificationLabel);
        _mainFilters.Controls.Add(_qualification);
        _mainFilters.Controls.Add(_advancedButton);
        _mainFilters.Controls.Add(_refreshButton);

        _advancedFilters.Dock = DockStyle.Bottom;
        _advancedFilters.Height = 160;
        _advancedFilters.FlowDirection = FlowDirection.LeftToRight;
        _advancedFilters.WrapContents = true;
        _advancedFilters.AutoScroll = true;
        _advancedFilters.Visible = false;
        _advancedFilters.Controls.Add(FilterLabel("기간"));
        _advancedFilters.Controls.Add(_period);
        _advancedFilters.Controls.Add(_startDate);
        _advancedFilters.Controls.Add(new Label { Text = "~", AutoSize = true, Margin = new Padding(3, 8, 3, 0) });
        _advancedFilters.Controls.Add(_endDate);
        _advancedFilters.Controls.Add(FilterLabel("요일"));
        _advancedFilters.Controls.Add(_weekday);
        _advancedFilters.Controls.Add(FilterLabel("홈/원정"));
        _advancedFilters.Controls.Add(_venue);
        _advancedFilters.Controls.Add(FilterLabel("VS"));
        _advancedFilters.Controls.Add(_opponent);
        _advancedFilters.Controls.Add(FilterLabel("구장"));
        _advancedFilters.Controls.Add(_stadium);
        _advancedFilters.Controls.Add(FilterLabel("선수"));
        _advancedFilters.Controls.Add(_playerNameFilter);
        _advancedFilters.Controls.Add(FilterLabel("출력"));
        _advancedFilters.Controls.Add(_resultLimit);
        _advancedFilters.SetFlowBreak(_resultLimit, true);

        _advancedFilters.Controls.Add(new Label
        {
            Text = "상황 조건",
            AutoSize = true,
            Font = new Font("맑은 고딕", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(45, 48, 55),
            Margin = new Padding(2, 8, 10, 0),
        });
        _advancedFilters.Controls.Add(FilterLabel("이닝"));
        _advancedFilters.Controls.Add(_inning);
        _advancedFilters.Controls.Add(FilterLabel("아웃"));
        _advancedFilters.Controls.Add(_outs);
        _advancedFilters.Controls.Add(FilterLabel("주자"));
        _advancedFilters.Controls.Add(_runners);
        _advancedFilters.Controls.Add(FilterLabel("점수"));
        _advancedFilters.Controls.Add(_scoreSituation);
        _advancedFilters.Controls.Add(FilterLabel("카운트"));
        _advancedFilters.Controls.Add(_count);
        _advancedFilters.Controls.Add(FilterLabel("타순"));
        _advancedFilters.Controls.Add(_batOrder);
        _advancedFilters.SetFlowBreak(_batOrder, true);

        _advancedFilters.Controls.Add(new Label
        {
            Text = "상세 옵션",
            AutoSize = true,
            Font = new Font("맑은 고딕", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(45, 48, 55),
            Margin = new Padding(2, 8, 10, 0),
        });
        _advancedFilters.Controls.Add(FilterLabel("조건1"));
        _advancedFilters.Controls.Add(_stat1);
        _advancedFilters.Controls.Add(_stat1Value);
        _advancedFilters.Controls.Add(_stat1Operator);
        _advancedFilters.Controls.Add(FilterLabel("조건2"));
        _advancedFilters.Controls.Add(_stat2);
        _advancedFilters.Controls.Add(_stat2Value);
        _advancedFilters.Controls.Add(_stat2Operator);
        _advancedFilters.Controls.Add(_filterResetButton);

        _filterContainer.Controls.Add(_advancedFilters);
        _filterContainer.Controls.Add(_mainFilters);
        return _filterContainer;
    }

    private Control BuildSubNavigation()
    {
        var container = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(238, 239, 242),
            Padding = new Padding(10, 3, 10, 3),
            ColumnCount = 2,
            RowCount = 1,
        };
        container.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        container.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _subNavigation.Dock = DockStyle.Fill;
        _subNavigation.FlowDirection = FlowDirection.LeftToRight;
        _subNavigation.WrapContents = false;
        container.Controls.Add(_subNavigation, 0, 0);

        var tools = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
        };
        tools.Controls.Add(_gridSearch);
        tools.Controls.Add(_csvButton);
        container.Controls.Add(tools, 1, 0);
        return container;
    }

    private Control BuildGridPanel()
    {
        _gridLayout.RowStyles.Clear();
        _gridLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0F));
        _gridLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        _leagueOverview.Controls.Clear();
        _leagueOverview.Controls.Add(_leagueOverviewMetrics);
        _leagueOverview.Controls.Add(_leagueOverviewTitle);

        _gridLayout.Controls.Clear();
        _gridLayout.Controls.Add(_leagueOverview, 0, 0);
        _gridLayout.Controls.Add(_grid, 0, 1);
        return _gridLayout;
    }

    private void SetLeagueOverviewVisible(bool visible)
    {
        _leagueOverview.Visible = visible;
        if (_gridLayout.RowStyles.Count >= 2)
        {
            _gridLayout.RowStyles[0].SizeType = SizeType.Absolute;
            _gridLayout.RowStyles[0].Height = visible ? 66F : 0F;
            _gridLayout.RowStyles[1].SizeType = SizeType.Percent;
            _gridLayout.RowStyles[1].Height = 100F;
        }
    }

    private Control BuildStatusBar()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Surface,
            Padding = new Padding(10, 2, 10, 2),
            ColumnCount = 3,
            RowCount = 1,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        panel.Controls.Add(_status, 0, 0);
        panel.Controls.Add(_progress, 1, 0);
        panel.Controls.Add(_rowCount, 2, 0);
        return panel;
    }

    private void InitializeFilterItems()
    {
        ReplaceItems(_resultLimit, new[] { "50", "100", "200", "500", "전체" }, "50");
        ReplaceItems(_stat1Operator, new[] { "이상", "초과", "이하", "미만", "같음" }, "이상");
        ReplaceItems(_stat2Operator, new[] { "이상", "초과", "이하", "미만", "같음" }, "이상");
        ReplaceItems(_stat1, new[] { "없음" }, "없음");
        ReplaceItems(_stat2, new[] { "없음" }, "없음");
        AddItems(_competition, "정규시즌", "전체 경기", "시범경기", "포스트시즌", "올스타전", "퓨처스리그", "기타");
        AddItems(_position, "전체", "C", "1B", "2B", "3B", "SS", "LF", "CF", "RF", "DH");
        AddItems(_qualification, "전체", "10%", "25%", "50%", "75%", "100%");
        AddItems(_period, "전체 기간", "직접 지정", "최근 7일", "최근 14일", "최근 30일", "최근 60일", "최근 90일", "최근 5경기", "최근 10경기", "최근 20경기", "최근 30경기");
        AddItems(_venue, "전체", "홈", "원정");
        AddItems(_weekday, "전체", "월", "화", "수", "목", "금", "토", "일");
        AddItems(_inning, "전체", "1~3회", "4~6회", "7~9회", "연장", "1회", "2회", "3회", "4회", "5회", "6회", "7회", "8회", "9회");
        AddItems(_outs, "전체", "0아웃", "1아웃", "2아웃");
        AddItems(_runners, "전체", "주자 없음", "1루", "2루", "3루", "1·2루", "1·3루", "2·3루", "만루", "득점권");
        AddItems(_scoreSituation, "전체", "동점", "리드", "열세", "1점 리드", "2점 리드", "3점 이상 리드", "1점 열세", "2점 열세", "3점 이상 열세", "1점차 이내", "2점차 이내", "3점차 이내");
        AddItems(_count, "전체", "0-0", "0-1", "0-2", "1-0", "1-1", "1-2", "2-0", "2-1", "2-2", "3-0", "3-1", "3-2");
        AddItems(_batOrder, "전체", "1번", "2번", "3번", "4번", "5번", "6번", "7번", "8번", "9번");
        AddItems(_team, "전체");
        AddItems(_opponent, "전체");
        AddItems(_stadium, "전체");
        AddItems(_year, "전체");

        _competition.SelectedIndex = 0;
        _position.SelectedIndex = 0;
        _qualification.SelectedIndex = 0;
        _period.SelectedIndex = 0;
        _venue.SelectedIndex = 0;
        _weekday.SelectedIndex = 0;
        _inning.SelectedIndex = 0;
        _outs.SelectedIndex = 0;
        _runners.SelectedIndex = 0;
        _scoreSituation.SelectedIndex = 0;
        _count.SelectedIndex = 0;
        _batOrder.SelectedIndex = 0;
        _team.SelectedIndex = 0;
        _opponent.SelectedIndex = 0;
        _stadium.SelectedIndex = 0;
        _year.SelectedIndex = 0;
        _startDate.Enabled = false;
        _endDate.Enabled = false;
    }

    private void WireEvents()
    {
        _filterResetButton.Click += async (_, _) => { ResetDetailedFilters(); await RefreshViewAsync(force: true); };
        _playerNameFilter.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await RefreshViewAsync(force: true); } };
        _stat1Value.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await RefreshViewAsync(force: true); } };
        _stat2Value.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await RefreshViewAsync(force: true); } };
        _advancedButton.Click += (_, _) => ToggleAdvancedFilters();
        _refreshButton.Click += async (_, _) => await RefreshViewAsync(force: true);
        _playerButton.Click += (_, _) => OpenPlayerSearch();
        _dataManagerButton.Click += async (_, _) => await OpenDataManagerAsync();
        _csvButton.Click += async (_, _) => await ExportCurrentGridAsync();
        _gridSearch.TextChanged += (_, _) => ApplyGridSearch();
        _grid.CellDoubleClick += (_, eventArgs) =>
        {
            if (eventArgs.RowIndex >= 0) OpenGridEntity(eventArgs.RowIndex);
        };
        _grid.CellFormatting += (_, eventArgs) =>
        {
            if (GridFilterInfoDecorator.TryFormat(_grid, eventArgs)) return;
            GridNumberFormatter.FormatCell(_grid, eventArgs);
        };
        _grid.Sorted += (_, _) =>
        {
            GridFilterInfoDecorator.UpdateHeader(_grid);
            if (_grid.Columns.Contains(GridFilterInfoDecorator.ColumnName))
                _grid.InvalidateColumn(_grid.Columns[GridFilterInfoDecorator.ColumnName].Index);
        };

        _year.SelectedIndexChanged += BaseFilterChanged;
        _competition.SelectedIndexChanged += BaseFilterChanged;
        _team.SelectedIndexChanged += FilterChanged;
        _position.SelectedIndexChanged += FilterChanged;
        _qualification.SelectedIndexChanged += FilterChanged;
        _period.SelectedIndexChanged += PeriodChanged;
        _startDate.ValueChanged += FilterChanged;
        _endDate.ValueChanged += FilterChanged;
        _opponent.SelectedIndexChanged += FilterChanged;
        _venue.SelectedIndexChanged += FilterChanged;
        _weekday.SelectedIndexChanged += FilterChanged;
        _stadium.SelectedIndexChanged += FilterChanged;

        KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.Control && eventArgs.KeyCode == Keys.F)
            {
                eventArgs.SuppressKeyPress = true;
                OpenPlayerSearch();
            }
            else if (eventArgs.KeyCode == Keys.F5)
            {
                eventArgs.SuppressKeyPress = true;
                _ = RefreshViewAsync(force: true);
            }
            else if (eventArgs.KeyCode == Keys.Escape)
            {
                _loadCts?.Cancel();
            }
        };
    }

    private async Task InitializeAsync()
    {
        SetBusy(true, "SQLite 관계형 DB를 여는 중입니다...");
        try
        {
            await _database.InitializeAsync();
            _catalog = await _database.GetCatalogAsync();
            _playerService = new DatabasePlayerPageService(_database);
            _teamService = new DatabaseTeamPageService(_database);
            PopulateCatalogFilters(selectLatestYear: true);
            _initialized = true;
            await ReloadDimensionFiltersAsync();
            await RefreshViewAsync(force: true);
        }
        catch (Exception ex)
        {
            _status.Text = $"DB 준비 실패: {ex.Message}";
            MessageBox.Show(this, ex.Message, "기록실 초기화 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void PopulateCatalogFilters(bool selectLatestYear)
    {
        if (_catalog is null) return;
        _suppressEvents = true;
        try
        {
            var years = new List<string> { "전체" };
            years.AddRange(_catalog.Years.Select(year => year.ToString()));
            ReplaceItems(_year, years, selectLatestYear && _catalog.Years.Count > 0 ? _catalog.Years[0].ToString() : "전체");
            var teams = new List<string> { "전체" };
            teams.AddRange(_catalog.Teams);
            ReplaceItems(_team, teams, SelectedText(_team));
            ReplaceItems(_opponent, teams, SelectedText(_opponent));
            var stadiums = new List<string> { "전체" };
            stadiums.AddRange(_catalog.Stadiums);
            ReplaceItems(_stadium, stadiums, SelectedText(_stadium));

            var min = _catalog.MinGameDate ?? DateTime.Today.AddYears(-1);
            var max = _catalog.MaxGameDate ?? DateTime.Today;
            if (max < min) max = min;
            _startDate.MinDate = DateTimePicker.MinimumDateTime;
            _startDate.MaxDate = DateTimePicker.MaximumDateTime;
            _endDate.MinDate = DateTimePicker.MinimumDateTime;
            _endDate.MaxDate = DateTimePicker.MaximumDateTime;
            _startDate.Value = min;
            _endDate.Value = max;
            _startDate.MinDate = min;
            _startDate.MaxDate = max;
            _endDate.MinDate = min;
            _endDate.MaxDate = max;
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private async Task ReloadDimensionFiltersAsync()
    {
        if (!_initialized || _section == RecordRoomSection.Constants) return;
        try
        {
            var options = await _database.GetFilterOptionsAsync(BuildBaseQueryForOptions());
            _suppressEvents = true;
            var currentTeam = SelectedText(_team);
            var currentOpponent = SelectedText(_opponent);
            var currentStadium = SelectedText(_stadium);
            var teams = new List<string> { "전체" };
            teams.AddRange(options.Teams);
            ReplaceItems(_team, teams, currentTeam);
            ReplaceItems(_opponent, teams, currentOpponent);
            var stadiums = new List<string> { "전체" };
            stadiums.AddRange(options.Stadiums);
            ReplaceItems(_stadium, stadiums, currentStadium);
            if (options.MinGameDate.HasValue && options.MaxGameDate.HasValue && SelectedText(_period) == "전체 기간")
            {
                SetDateRange(options.MinGameDate.Value, options.MaxGameDate.Value);
            }
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private void AddRoomButton(RecordRoomSection section, string text)
    {
        var button = NavigationButton(text, 112);
        button.Click += async (_, _) =>
        {
            if (_section == section) return;
            _section = section;
            if (_section == RecordRoomSection.Career)
            {
                _suppressEvents = true;
                SelectText(_year, "전체");
                _suppressEvents = false;
            }
            else if ((_section == RecordRoomSection.Season || _section == RecordRoomSection.Team) && SelectedText(_year) == "전체" && _catalog?.Years.Count > 0)
            {
                _suppressEvents = true;
                SelectText(_year, _catalog.Years[0].ToString());
                _suppressEvents = false;
            }
            ApplySectionState();
            await ReloadDimensionFiltersAsync();
            await RefreshViewAsync(force: true);
        };
        _roomButtons[section] = button;
        _roomNavigation.Controls.Add(button);
    }

    private void AddRoleButton(RecordRoomRole role, string text)
    {
        var button = NavigationButton(text, 70);
        button.Click += async (_, _) =>
        {
            if (_role == role) return;
            _role = role;
            _subView = "기본";
            ApplySectionState();
            await RefreshViewAsync(force: true);
        };
        _roleButtons[role] = button;
        _roleNavigation.Controls.Add(button);
    }

    private void ApplySectionState()
    {
        foreach (var item in _roomButtons)
            StyleNavigationButton(item.Value, item.Key == _section, topLevel: true);
        foreach (var item in _roleButtons)
            StyleNavigationButton(item.Value, item.Key == _role, topLevel: false);

        var constants = _section == RecordRoomSection.Constants;
        _roleNavigation.Visible = !constants;
        _filterContainer.Visible = !constants;
        _root.RowStyles[1].Height = constants ? 42 : 42;
        _root.RowStyles[2].Height = constants ? 0 : (_advancedVisible ? 218 : 58);

        _year.Enabled = _section is RecordRoomSection.Season or RecordRoomSection.Team;
        _position.Visible = !constants && _role == RecordRoomRole.Batter && _section != RecordRoomSection.Team;
        _positionLabel.Visible = _position.Visible;
        _qualification.Visible = !constants && _section == RecordRoomSection.Season;
        _qualificationLabel.Visible = _qualification.Visible;
        _qualificationLabel.Text = _role == RecordRoomRole.Batter ? "규정타석" : "규정이닝";

        _roomTitle.Text = _section switch
        {
            RecordRoomSection.Season => $"시즌 {RoleText()}",
            RecordRoomSection.Career => $"통산 {RoleText()}",
            RecordRoomSection.Team => $"팀 {RoleText()}",
            _ => "연도별 상수",
        };
        _roomDescription.Text = _section switch
        {
            RecordRoomSection.Season => "선택 시즌의 선수별 기록",
            RecordRoomSection.Career => "전체 시즌을 선수 코드 기준으로 합산",
            RecordRoomSection.Team => "팀 단위 기록과 순위",
            _ => "읽어온 전체 kbo_r 데이터 기반",
        };

        RebuildSubNavigation();
    }

    private void RebuildSubNavigation()
    {
        _subNavigation.SuspendLayout();
        _subNavigation.Controls.Clear();
        _subButtons.Clear();
        var labels = _section == RecordRoomSection.Constants
            ? new[] { "리그 상수", "파크 팩터", "파크팩터 진단", "파크팩터 상세", "KBO PF v2 실험", "WAR A/B", "PF 민감도 요약", "PF 민감도 상세", "Replacement 민감도 요약", "Replacement 민감도 상세", "WAR 분포 요약", "WAR 분포 선수", "투수 WAR 진단", "대체후보" }
            : _role == RecordRoomRole.Batter
                ? new[] { "기본", "심화", "가치", "확장", "클러치", "파워", "팀배팅", "도루", "주루", "타구", "타구방향", "투구", "구종" }
                : new[] { "기본", "심화", "가치", "확장", "WP", "주자", "선발", "구원", "타구", "타구방향", "투구", "구종" };
        if (!labels.Contains(_subView, StringComparer.Ordinal)) _subView = labels[0];
        foreach (var label in labels)
        {
            var button = SubNavigationButton(label);
            button.Click += async (_, _) =>
            {
                if (_subView == label) return;
                _subView = label;
                StyleSubButtons();
                await RefreshViewAsync();
            };
            _subButtons[label] = button;
            _subNavigation.Controls.Add(button);
        }
        StyleSubButtons();
        _subNavigation.ResumeLayout();
    }

    private void StyleSubButtons()
    {
        foreach (var item in _subButtons)
        {
            var selected = item.Key == _subView;
            item.Value.ForeColor = selected ? Accent : Color.FromArgb(55, 58, 65);
            item.Value.Font = new Font(item.Value.Font, selected ? FontStyle.Bold : FontStyle.Regular);
            item.Value.FlatAppearance.BorderSize = 0;
            item.Value.BackColor = Color.Transparent;
            item.Value.Padding = new Padding(4, 0, 4, selected ? 2 : 0);
            if (item.Value is UnderlineNavigationButton underline)
            {
                underline.Selected = selected;
                underline.FillSelected = false;
                underline.Invalidate();
            }
        }
    }

    private async void BaseFilterChanged(object? sender, EventArgs eventArgs)
    {
        if (_suppressEvents || !_initialized) return;
        await ReloadDimensionFiltersAsync();
        await RefreshViewAsync(force: true);
    }

    private async void FilterChanged(object? sender, EventArgs eventArgs)
    {
        if (_suppressEvents || !_initialized) return;
        await RefreshViewAsync(force: true);
    }

    private async void PeriodChanged(object? sender, EventArgs eventArgs)
    {
        if (_suppressEvents || !_initialized) return;
        var direct = SelectedText(_period) == "직접 지정";
        _startDate.Enabled = direct;
        _endDate.Enabled = direct;
        if (!direct) ApplyPeriodPreset();
        await RefreshViewAsync(force: true);
    }

    private void ApplyPeriodPreset()
    {
        if (_catalog is null) return;
        var max = _catalog.MaxGameDate ?? DateTime.Today;
        var min = _catalog.MinGameDate ?? max.AddYears(-1);
        var preset = SelectedText(_period);
        var start = preset switch
        {
            "최근 7일" => max.AddDays(-6),
            "최근 14일" => max.AddDays(-13),
            "최근 30일" => max.AddDays(-29),
            "최근 60일" => max.AddDays(-59),
            "최근 90일" => max.AddDays(-89),
            _ => min,
        };
        SetDateRange(start < min ? min : start, max);
    }

    private void ToggleAdvancedFilters()
    {
        _advancedVisible = !_advancedVisible;
        _advancedFilters.Visible = _advancedVisible;
        _root.RowStyles[2].Height = _section == RecordRoomSection.Constants ? 0 : (_advancedVisible ? 218 : 58);
        _advancedButton.Text = _advancedVisible ? "상세 닫기" : "상세 필터";
    }

    private async Task RefreshViewAsync(bool force = false)
    {
        if (!_initialized || _catalog is null) return;
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;
        SetBusy(true, $"{_roomTitle.Text} · {_subView} 조회 중...");

        try
        {
            if (_section == RecordRoomSection.Constants)
            {
                SetLeagueOverviewVisible(false);
                var league = await GetLeagueAsync(token);
                if (_subView == "파크 팩터")
                {
                    BindRows(league.ParkFactors, playerGrid: false, filterDescription: string.Empty);
                }
                else if (_subView == "파크팩터 진단")
                {
                    var diagnostics = await GetParkDiagnosticsAsync(token);
                    BindRows(diagnostics.Seasons, playerGrid: false, filterDescription: string.Empty);
                    _status.Text = "연도별 파크팩터 분포 진단 · WAR 공식에는 아직 반영하지 않는 관찰용 값입니다.";
                }
                else if (_subView == "파크팩터 상세")
                {
                    var diagnostics = await GetParkDiagnosticsAsync(token);
                    BindRows(diagnostics.Stadiums, playerGrid: false, filterDescription: string.Empty);
                    _status.Text = $"연도·구장별 파크팩터 상세 {diagnostics.Stadiums.Count:N0}건 · FIP PF와 단순 득점 PF를 비교합니다.";
                }
                else if (_subView == "KBO PF v2 실험")
                {
                    var experiment = await GetParkV2ExperimentAsync(token);
                    BindRows(experiment.Stadiums, playerGrid: false, filterDescription: string.Empty);
                    _status.Text = "KBO PF v2 실험 · 5년 득점 PF + 100 회귀 + 85~115 제한 + 시즌 이닝가중 100 재중앙화 · WAR v3에는 미적용";
                }
                else if (_subView == "WAR A/B")
                {
                    var experiment = await GetParkV2ExperimentAsync(token);
                    BindRows(experiment.WarComparisons, playerGrid: false, filterDescription: string.Empty);
                    _status.Text = "Legacy PF vs KBO PF v2 투수 WAR A/B · Replacement와 Pre-fWAR/WARIP 변화를 비교하는 실험값입니다.";
                }
                else if (_subView == "PF 민감도 요약")
                {
                    var sensitivity = await GetParkSensitivityAsync(token);
                    BindRows(sensitivity.Summaries, playerGrid: false, filterDescription: string.Empty);
                    _status.Text = "PF 민감도 요약 · Prior 4 × Clamp 3 × Weight 2 = 24개 정책 · 완료시즌 기준 |WARIP| 순";
                }
                else if (_subView == "PF 민감도 상세")
                {
                    var sensitivity = await GetParkSensitivityAsync(token);
                    BindRows(sensitivity.Details, playerGrid: false, filterDescription: string.Empty);
                    _status.Text = $"PF 민감도 상세 {sensitivity.Details.Count:N0}건 · 24개 정책의 연도별 결과";
                }
                else if (_subView == "Replacement 민감도 요약")
                {
                    var sensitivity = await GetReplacementSensitivityAsync(token);
                    BindRows(sensitivity.Summaries, playerGrid: false, filterDescription: string.Empty);
                    _status.Text = "Replacement 민감도 요약 · KBO PF v2 고정 · SP 120/125/130 × RP 105/110/115/120/125";
                }
                else if (_subView == "Replacement 민감도 상세")
                {
                    var sensitivity = await GetReplacementSensitivityAsync(token);
                    BindRows(sensitivity.Details, playerGrid: false, filterDescription: string.Empty);
                    _status.Text = $"Replacement 민감도 상세 {sensitivity.Details.Count:N0}건 · 시즌별 SP/RP WAR 배분과 WARIP 비교";
                }
                else if (_subView == "WAR 분포 요약")
                {
                    var distribution = await GetWarDistributionAsync(token);
                    BindRows(distribution.Summaries, playerGrid: false, filterDescription: string.Empty);
                    _status.Text = "SP120 / RP115 + KBO PF v2 기준 WAR 분포 · 후보군이 0 WAR 근처에 모이는지 검증";
                }
                else if (_subView == "WAR 분포 선수")
                {
                    var distribution = await GetWarDistributionAsync(token);
                    BindRows(distribution.Players, playerGrid: false, filterDescription: string.Empty);
                    _status.Text = $"WAR 분포 선수 {distribution.Players.Count:N0}건 · 후보=Y는 기존 대체후보 선정 로직 포함 선수";
                }
                else if (_subView == "투수 WAR 진단")
                {
                    var diagnostics = await GetWarDiagnosticsAsync(token);
                    BindRows(diagnostics.Seasons, playerGrid: false, filterDescription: string.Empty);
                    _status.Text = "연도별 KBO 투수 WAR 진단 · 현재 WAR 공식은 변경하지 않은 관찰용 값입니다.";
                }
                else if (_subView == "대체후보")
                {
                    var diagnostics = await GetWarDiagnosticsAsync(token);
                    BindRows(diagnostics.Candidates, playerGrid: false, filterDescription: string.Empty);
                    _status.Text = $"연도별 SP/RP 대체후보 전체 {diagnostics.Candidates.Count:N0}건 · CSV는 화면 상태와 무관하게 전체 후보를 저장합니다.";
                }
                else
                {
                    BindRows(league.Constants, playerGrid: false, filterDescription: string.Empty);
                }
                return;
            }

            var query = BuildQuery();
            var leagueReference = await GetLeagueAsync(token);
            var snapshot = await GetSnapshotAsync(query, leagueReference, force, token);
            var filterDescription = BuildFilterDescription();
            UpdateLeagueOverview(snapshot, query);

            if (_role == RecordRoomRole.Batter)
            {
                var eligible = BuildEligibleBatterKeys(snapshot);
                await BindBatterViewAsync(query, leagueReference, snapshot, eligible, filterDescription, token);
            }
            else
            {
                var eligible = BuildEligiblePitcherKeys(snapshot);
                await BindPitcherViewAsync(query, leagueReference, snapshot, eligible, filterDescription, token);
            }
        }
        catch (OperationCanceledException)
        {
            _status.Text = "조회가 취소되었습니다.";
        }
        catch (Exception ex)
        {
            _status.Text = $"조회 오류: {ex.Message}";
            MessageBox.Show(this, ex.Message, "기록 조회 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void UpdateLeagueOverview(AnalyticsSnapshot snapshot, GameQuery query)
    {
        if (_section != RecordRoomSection.Team)
        {
            SetLeagueOverviewVisible(false);
            return;
        }

        var fullLeague = string.IsNullOrWhiteSpace(query.TeamCode);
        var overview = _role == RecordRoomRole.Pitcher
            ? LeagueOverviewBuilder.FromPitchers(PitcherRecordRoomRowFactory.BuildBasic(snapshot), fullLeague)
            : LeagueOverviewBuilder.FromBatters(RecordRoomRowFactory.BuildBasic(snapshot), fullLeague);

        _leagueOverviewTitle.Text = overview.Title;
        _leagueOverviewTitle.BackColor = Color.FromArgb(255, 244, 239);
        _leagueOverviewTitle.ForeColor = Color.FromArgb(185, 70, 34);
        _leagueOverviewMetrics.SuspendLayout();
        _leagueOverviewMetrics.Controls.Clear();
        foreach (var metric in overview.Metrics)
        {
            var card = new Panel { Width = 78, Height = 50, Margin = new Padding(0, 0, 1, 0), BackColor = Color.White };
            var label = new Label { Text = metric.Label, Dock = DockStyle.Top, Height = 20, TextAlign = ContentAlignment.BottomCenter, ForeColor = Muted, Font = new Font("맑은 고딕", 8F) };
            var value = new Label { Text = metric.Value, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = metric.Emphasis ? Accent : Color.FromArgb(42, 59, 82), Font = new Font("맑은 고딕", 10F, FontStyle.Bold) };
            card.Controls.Add(value); card.Controls.Add(label);
            _leagueOverviewMetrics.Controls.Add(card);
        }
        _leagueOverviewMetrics.ResumeLayout();
        SetLeagueOverviewVisible(true);
        _leagueOverview.BringToFront();
    }

    private async Task BindBatterViewAsync(
        GameQuery query,
        LeagueReference league,
        AnalyticsSnapshot snapshot,
        IReadOnlySet<string>? eligible,
        string filterDescription,
        CancellationToken cancellationToken)
    {
        switch (_subView)
        {
            case "심화":
                BindRows(RecordRoomRowFactory.BuildAdvanced(snapshot, eligible), IsPlayerGrid(), filterDescription);
                break;
            case "가치":
                BindRows(RecordRoomRowFactory.BuildValue(snapshot, eligible), IsPlayerGrid(), filterDescription);
                break;
            case "확장":
                BindRows(RecordRoomRowFactory.BuildExtended(snapshot, eligible), IsPlayerGrid(), filterDescription);
                break;
            case "클러치":
            {
                var rows = await _batterDetails.GetClutchAsync(query, league, cancellationToken);
                BindRows(FilterSpecialRows(rows, eligible), IsPlayerGrid(), filterDescription);
                break;
            }
            case "파워":
                BindRows(RecordRoomRowFactory.BuildPower(snapshot, eligible), IsPlayerGrid(), filterDescription);
                break;
            case "팀배팅":
                BindRows(RecordRoomRowFactory.BuildTeamBatting(snapshot, eligible), IsPlayerGrid(), filterDescription);
                break;
            case "도루":
                BindRows(RecordRoomRowFactory.BuildSteal(snapshot, eligible), IsPlayerGrid(), filterDescription);
                break;
            case "주루":
                BindRows(RecordRoomRowFactory.BuildBaserunning(snapshot, eligible), IsPlayerGrid(), filterDescription);
                break;
            case "타구":
            {
                var rows = await _batterDetails.GetBattedBallAsync(query, cancellationToken);
                BindRows(FilterSpecialRows(rows, eligible), IsPlayerGrid(), filterDescription);
                break;
            }
            case "타구방향":
            {
                var rows = await _batterDetails.GetDirectionAsync(query, cancellationToken);
                BindRows(FilterSpecialRows(rows, eligible), IsPlayerGrid(), filterDescription);
                break;
            }
            case "투구":
                BindRows(RecordRoomRowFactory.BuildPitchProfile(snapshot, eligible), IsPlayerGrid(), filterDescription);
                break;
            case "구종":
            {
                var rows = await _batterDetails.GetPitchTypesAsync(query, cancellationToken);
                var filtered = FilterSpecialRows(rows, eligible);
                var matrix = BatterPitchTypeMatrixFactory.Build(filtered);
                BindRows(matrix, IsPlayerGrid(), filterDescription);
                break;
            }
            default:
                BindRows(RecordRoomRowFactory.BuildBasic(snapshot, eligible), IsPlayerGrid(), filterDescription);
                break;
        }
    }

    private async Task BindPitcherViewAsync(
        GameQuery query,
        LeagueReference league,
        AnalyticsSnapshot snapshot,
        IReadOnlySet<string>? eligible,
        string filterDescription,
        CancellationToken cancellationToken)
    {
        switch (_subView)
        {
            case "심화":
                BindRows(PitcherRecordRoomRowFactory.BuildAdvanced(snapshot, eligible), IsPlayerGrid(), filterDescription);
                break;
            case "가치":
                BindRows(PitcherRecordRoomRowFactory.BuildValue(snapshot, league, eligible), IsPlayerGrid(), filterDescription);
                break;
            case "확장":
            {
                var rows = await _pitcherDetails.GetExtendedAsync(query, cancellationToken);
                BindRows(FilterSpecialRows(rows, eligible), IsPlayerGrid(), filterDescription);
                break;
            }
            case "WP":
            {
                var rows = await _pitcherDetails.GetWinProbabilityAsync(query, league, cancellationToken);
                BindRows(FilterSpecialRows(rows, eligible), IsPlayerGrid(), filterDescription);
                break;
            }
            case "주자":
            {
                var rows = await _pitcherDetails.GetRunnerAsync(query, cancellationToken);
                BindRows(FilterSpecialRows(rows, eligible), IsPlayerGrid(), filterDescription);
                break;
            }
            case "선발":
            {
                var rows = (await _pitcherDetails.GetStarterAsync(query, cancellationToken)).ToList();
                ApplyPitcherRoleWar(rows, PitcherRecordRoomRowFactory.BuildValue(snapshot, league, eligible));
                BindRows(FilterSpecialRows(rows, eligible), IsPlayerGrid(), filterDescription);
                break;
            }
            case "구원":
            {
                var rows = (await _pitcherDetails.GetRelieverAsync(query, league, cancellationToken)).ToList();
                ApplyPitcherRoleWar(rows, PitcherRecordRoomRowFactory.BuildValue(snapshot, league, eligible));
                BindRows(FilterSpecialRows(rows, eligible), IsPlayerGrid(), filterDescription);
                break;
            }
            case "타구":
            {
                var rows = await _pitcherDetails.GetBattedBallAsync(query, cancellationToken);
                BindRows(FilterSpecialRows(rows, eligible), IsPlayerGrid(), filterDescription);
                break;
            }
            case "타구방향":
            {
                var rows = await _pitcherDetails.GetDirectionAsync(query, cancellationToken);
                BindRows(FilterSpecialRows(rows, eligible), IsPlayerGrid(), filterDescription);
                break;
            }
            case "투구":
            {
                var rows = await _pitcherDetails.GetPitchProfileAsync(query, cancellationToken);
                BindRows(FilterSpecialRows(rows, eligible), IsPlayerGrid(), filterDescription);
                break;
            }
            case "구종":
            {
                var rows = await _pitcherDetails.GetPitchTypesAsync(query, cancellationToken);
                BindRows(FilterSpecialRows(rows, eligible), IsPlayerGrid(), filterDescription);
                break;
            }
            default:
                BindRows(PitcherRecordRoomRowFactory.BuildBasic(snapshot, eligible), IsPlayerGrid(), filterDescription);
                break;
        }
    }

    private static void ApplyPitcherRoleWar(
        IReadOnlyList<PitcherStarterRecordRow> rows,
        IReadOnlyList<PitcherDetailedValueRecordRow> values)
    {
        var lookup = values.ToDictionary(
            row => PitcherRecordRoomRowFactory.Key(row.Pcode, row.TeamCode),
            StringComparer.Ordinal);
        foreach (var row in rows)
            if (lookup.TryGetValue(PitcherRecordRoomRowFactory.Key(row.Pcode, row.TeamCode), out var value))
                row.StarterWar = value.StarterWar;
    }

    private static void ApplyPitcherRoleWar(
        IReadOnlyList<PitcherRelieverRecordRow> rows,
        IReadOnlyList<PitcherDetailedValueRecordRow> values)
    {
        var lookup = values.ToDictionary(
            row => PitcherRecordRoomRowFactory.Key(row.Pcode, row.TeamCode),
            StringComparer.Ordinal);
        foreach (var row in rows)
            if (lookup.TryGetValue(PitcherRecordRoomRowFactory.Key(row.Pcode, row.TeamCode), out var value))
                row.ReliefWar = value.ReliefWar;
    }

    private async Task<LeagueReference> GetLeagueAsync(CancellationToken cancellationToken)
    {
        if (_league is not null) return _league;
        _status.Text = "전체 kbo_r 리그 상수 확인 중...";
        _league = await _database.GetLeagueReferenceAsync(cancellationToken: cancellationToken);
        return _league;
    }

    private async Task<PitcherWarDiagnosticBundle> GetWarDiagnosticsAsync(CancellationToken cancellationToken)
    {
        if (_warDiagnostics is not null) return _warDiagnostics;
        _status.Text = "연도별 대체선수 후보와 FIP- 분포 계산 중...";
        _warDiagnostics = await _database.GetPitcherWarDiagnosticsAsync(cancellationToken);
        return _warDiagnostics;
    }

    private async Task<ParkFactorDiagnosticBundle> GetParkDiagnosticsAsync(CancellationToken cancellationToken)
    {
        if (_parkDiagnostics is not null) return _parkDiagnostics;
        _status.Text = "연도·구장별 파크팩터 구성요소 계산 중...";
        _parkDiagnostics = await _database.GetParkFactorDiagnosticsAsync(cancellationToken);
        return _parkDiagnostics;
    }

    private async Task<ParkFactorV2ExperimentBundle> GetParkV2ExperimentAsync(CancellationToken cancellationToken)
    {
        if (_parkV2Experiment is not null) return _parkV2Experiment;
        _status.Text = "KBO PF v2 및 Legacy WAR A/B 계산 중...";
        _parkV2Experiment = await _database.GetParkFactorV2ExperimentAsync(cancellationToken);
        return _parkV2Experiment;
    }

    private async Task<ParkFactorSensitivityBundle> GetParkSensitivityAsync(CancellationToken cancellationToken)
    {
        if (_parkSensitivity is not null) return _parkSensitivity;
        _status.Text = "PF 정책 24개 × 시즌별 WAR 민감도 계산 중...";
        _parkSensitivity = await _database.GetParkFactorSensitivityAsync(cancellationToken);
        return _parkSensitivity;
    }

    private async Task<ReplacementSensitivityBundle> GetReplacementSensitivityAsync(CancellationToken cancellationToken)
    {
        if (_replacementSensitivity is not null) return _replacementSensitivity;
        _status.Text = "KBO PF v2 고정 · SP/RP Replacement FIP- 15개 조합 계산 중...";
        _replacementSensitivity = await _database.GetReplacementSensitivityAsync(cancellationToken);
        return _replacementSensitivity;
    }

    private async Task<WarDistributionDiagnosticBundle> GetWarDistributionAsync(CancellationToken cancellationToken)
    {
        if (_warDistribution is not null) return _warDistribution;
        _status.Text = "SP120 / RP115 기준 WAR 분포와 대체후보 WAR 계산 중...";
        _warDistribution = await _database.GetWarDistributionDiagnosticsAsync(cancellationToken);
        return _warDistribution;
    }

    private async Task<AnalyticsSnapshot> GetSnapshotAsync(
        GameQuery query,
        LeagueReference league,
        bool force,
        CancellationToken cancellationToken)
    {
        if (!force && _snapshot is not null && string.Equals(_snapshotKey, query.CacheKey, StringComparison.Ordinal))
            return _snapshot;
        _status.Text = "관계형 경기 집계 테이블 조회 중...";
        _snapshot = await _analytics.GetSnapshotAsync(query, league, cancellationToken: cancellationToken);
        _snapshotKey = query.CacheKey;
        return _snapshot;
    }

    private GameQuery BuildBaseQueryForOptions() => new()
    {
        Grouping = CurrentGrouping(),
        SeasonYear = _section == RecordRoomSection.Career ? null : SelectedYear(),
        Competition = SelectedText(_competition),
    };

    private GameQuery BuildQuery()
    {
        var (start, end, recent) = SelectedPeriod();
        return new GameQuery
        {
            Grouping = CurrentGrouping(),
            SeasonYear = _section == RecordRoomSection.Career ? null : SelectedYear(),
            Competition = SelectedText(_competition),
            TeamCode = EmptySelection(_team),
            OpponentCode = EmptySelection(_opponent),
            Venue = EmptySelection(_venue),
            Stadium = EmptySelection(_stadium),
            Weekday = EmptySelection(_weekday),
            StartDate = start,
            EndDate = end,
            RecentGameCount = recent,
            InningFilter = EmptySelection(_inning),
            OutsBefore = ParseOutsFilter(),
            RunnerState = EmptySelection(_runners),
            ScoreSituation = EmptySelection(_scoreSituation),
            BallsBefore = ParseCount().Balls,
            StrikesBefore = ParseCount().Strikes,
            BatOrder = ParseBatOrder(),
        };
    }

    private int? ParseOutsFilter()
    {
        var text = SelectedText(_outs);
        if (text == "전체") return null;
        return int.TryParse(text.Replace("아웃", string.Empty, StringComparison.Ordinal), out var value) ? value : null;
    }

    private (int? Balls, int? Strikes) ParseCount()
    {
        var text = SelectedText(_count);
        if (text == "전체" || string.IsNullOrWhiteSpace(text)) return (null, null);
        var parts = text.Split('-');
        return parts.Length == 2 && int.TryParse(parts[0], out var balls) && int.TryParse(parts[1], out var strikes)
            ? (balls, strikes)
            : (null, null);
    }

    private int? ParseBatOrder()
    {
        var text = SelectedText(_batOrder);
        if (text == "전체") return null;
        return int.TryParse(text.Replace("번", string.Empty, StringComparison.Ordinal), out var value) ? value : null;
    }

    private AnalyticsGrouping CurrentGrouping() => _section switch
    {
        RecordRoomSection.Career => AnalyticsGrouping.PlayerCareer,
        RecordRoomSection.Team => AnalyticsGrouping.Team,
        _ => AnalyticsGrouping.PlayerByTeam,
    };

    private (DateTime? Start, DateTime? End, int? Recent) SelectedPeriod()
    {
        var period = SelectedText(_period);
        if (period.StartsWith("최근 ", StringComparison.Ordinal) && period.EndsWith("경기", StringComparison.Ordinal))
        {
            var numberText = period.Replace("최근 ", string.Empty, StringComparison.Ordinal)
                .Replace("경기", string.Empty, StringComparison.Ordinal);
            return int.TryParse(numberText, out var count) ? (null, null, count) : (null, null, null);
        }
        if (period == "전체 기간") return (null, null, null);
        return (_startDate.Value.Date, _endDate.Value.Date, null);
    }

    private IReadOnlySet<string>? BuildEligibleBatterKeys(AnalyticsSnapshot snapshot)
    {
        if (_section == RecordRoomSection.Team) return null;
        var position = SelectedText(_position);
        var fraction = _section == RecordRoomSection.Season ? SelectedQualificationFraction() : 0.0;
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in snapshot.BatterClassic)
        {
            var key = RecordRoomRowFactory.Key(row.Pcode, row.TeamCode);
            if (position != "전체")
            {
                var primary = snapshot.PrimaryPositions.GetValueOrDefault(key, "-");
                if (!string.Equals(primary, position, StringComparison.OrdinalIgnoreCase)) continue;
            }
            if (fraction > 0)
            {
                var teamGames = snapshot.TeamGames.GetValueOrDefault(row.TeamCode ?? string.Empty, row.Games);
                if (row.PA + 0.0001 < teamGames * 3.1 * fraction) continue;
            }
            keys.Add(key);
        }
        return keys;
    }

    private IReadOnlySet<string>? BuildEligiblePitcherKeys(AnalyticsSnapshot snapshot)
    {
        if (_section == RecordRoomSection.Team) return null;
        var fraction = _section == RecordRoomSection.Season ? SelectedQualificationFraction() : 0.0;
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in snapshot.PitcherClassic)
        {
            var key = RecordRoomRowFactory.Key(row.Pcode, row.TeamCode);
            if (fraction > 0)
            {
                var teamGames = snapshot.TeamGames.GetValueOrDefault(row.TeamCode ?? string.Empty, row.Games);
                var innings = snapshot.PitcherIp.GetValueOrDefault(key, 0.0);
                if (innings + 0.0001 < teamGames * fraction) continue;
            }
            keys.Add(key);
        }
        return keys;
    }

    private static IReadOnlyList<T> FilterRows<T>(IEnumerable<T> rows, IReadOnlySet<string>? eligible)
    {
        if (eligible is null) return rows.ToList();
        var pcode = TypeDescriptor.GetProperties(typeof(T))["Pcode"];
        var team = TypeDescriptor.GetProperties(typeof(T))["TeamCode"];
        if (pcode is null || team is null) return rows.ToList();
        return rows.Where(row => eligible.Contains(RecordRoomRowFactory.Key(
                Convert.ToString(pcode.GetValue(row)),
                Convert.ToString(team.GetValue(row)))))
            .ToList();
    }

    private static IReadOnlyList<T> FilterSpecialRows<T>(IEnumerable<T> rows, IReadOnlySet<string>? eligible) where T : class
    {
        var filtered = FilterRows(rows, eligible).ToList();
        var rank = TypeDescriptor.GetProperties(typeof(T))["Rank"];
        if (rank is not null && !rank.IsReadOnly)
        {
            for (var index = 0; index < filtered.Count; index++) rank.SetValue(filtered[index], index + 1);
        }
        return filtered;
    }

    private void BindRows<T>(IEnumerable<T> rows, bool playerGrid, string filterDescription)
    {
        UpdateStatFilterOptions<T>();
        if (_section != RecordRoomSection.Constants) filterDescription = BuildFilterDescription();
        var list = ApplyDetailedRowFilters(rows).ToList();
        ReRankRows(list);
        _grid.SuspendLayout();
        _grid.DataSource = null;
        _grid.Columns.Clear();
        _grid.DataSource = new SortableBindingList<T>(list);
        foreach (DataGridViewColumn column in _grid.Columns)
            column.SortMode = DataGridViewColumnSortMode.Automatic;
        if (_grid.Columns.Contains("Pcode")) _grid.Columns["Pcode"].Visible = false;
        if (_section == RecordRoomSection.Team && _grid.Columns.Contains("PrimaryPosition"))
            _grid.Columns["PrimaryPosition"].Visible = false;
        GridNumberFormatter.Apply(_grid);
        GridFilterInfoDecorator.Apply(_grid, filterDescription, playerGrid);
        _grid.ResumeLayout();
        _rowCount.Text = $"표시 {list.Count:N0}건";
        _status.Text = $"{_roomTitle.Text} · {_subView} 조회 완료";
        ApplyGridSearch();
    }

    private bool IsPlayerGrid() => _section is RecordRoomSection.Season or RecordRoomSection.Career;

    private string BuildFilterDescription()
    {
        var parts = new List<string>();
        if (_section == RecordRoomSection.Career) parts.Add("통산");
        else if (SelectedYear().HasValue) parts.Add(SelectedYear()!.Value.ToString());
        parts.Add(SelectedText(_competition));
        AddIfSelected(parts, _team, "팀 ");
        if (_role == RecordRoomRole.Batter && _section != RecordRoomSection.Team)
            AddIfSelected(parts, _position, "Pos ");
        if (_section == RecordRoomSection.Season && SelectedText(_qualification) != "전체")
            parts.Add($"규정 {SelectedText(_qualification)}");
        if (SelectedText(_period) != "전체 기간") parts.Add(SelectedText(_period));
        AddIfSelected(parts, _opponent, "vs ");
        AddIfSelected(parts, _venue, string.Empty);
        AddIfSelected(parts, _weekday, string.Empty, "요일");
        AddIfSelected(parts, _stadium, "구장 ");
        AddIfSelected(parts, _inning, string.Empty);
        if (ParseOutsFilter() is { } outs) parts.Add($"{outs}아웃");
        AddIfSelected(parts, _runners, "주자 ");
        AddIfSelected(parts, _scoreSituation, "점수 ");
        AddIfSelected(parts, _count, "카운트 ");
        if (ParseBatOrder() is { } order) parts.Add($"{order}번 타순");
        if (_role == RecordRoomRole.Pitcher && BuildQuery().HasSituationFilters)
            parts.Add("상황재집계(ER·WAR 등 최종라인 지표 제외)");
        if (!string.IsNullOrWhiteSpace(_playerNameFilter.Text)) parts.Add($"선수 {_playerNameFilter.Text.Trim()}");
        AddStatFilterDescription(parts, _stat1, _stat1Operator, _stat1Value);
        AddStatFilterDescription(parts, _stat2, _stat2Operator, _stat2Value);
        if (_section != RecordRoomSection.Constants && SelectedText(_resultLimit) != "전체")
            parts.Add($"출력 {SelectedText(_resultLimit)}");
        return string.Join(" · ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private IEnumerable<T> ApplyDetailedRowFilters<T>(IEnumerable<T> rows)
    {
        IEnumerable<T> filtered = rows;
        var nameText = _playerNameFilter.Text.Trim();
        if (nameText.Length > 0)
        {
            var name = TypeDescriptor.GetProperties(typeof(T))["Name"];
            if (name is not null)
                filtered = filtered.Where(row => Convert.ToString(name.GetValue(row))?.Contains(nameText, StringComparison.CurrentCultureIgnoreCase) == true);
        }

        filtered = ApplyStatCondition(filtered, _stat1, _stat1Operator, _stat1Value);
        filtered = ApplyStatCondition(filtered, _stat2, _stat2Operator, _stat2Value);

        if (_section != RecordRoomSection.Constants)
        {
            var limitText = SelectedText(_resultLimit);
            if (int.TryParse(limitText, out var limit) && limit > 0)
                filtered = filtered.Take(limit);
        }
        return filtered;
    }

    private static IEnumerable<T> ApplyStatCondition<T>(IEnumerable<T> rows, ComboBox stat, ComboBox op, TextBox valueBox)
    {
        var statText = SelectedText(stat);
        if (statText is "" or "없음" || !double.TryParse(valueBox.Text.Trim(), out var threshold)) return rows;

        var descriptor = TypeDescriptor.GetProperties(typeof(T)).Cast<PropertyDescriptor>()
            .FirstOrDefault(p => string.Equals(p.DisplayName, statText, StringComparison.CurrentCultureIgnoreCase));
        if (descriptor is null) return rows;

        if (statText.Contains('%') && Math.Abs(threshold) > 1.0) threshold /= 100.0;
        var operation = SelectedText(op);
        return rows.Where(row =>
        {
            var raw = descriptor.GetValue(row);
            if (raw is null) return false;
            if (!TryConvertDouble(raw, out var number)) return false;
            return operation switch
            {
                "초과" => number > threshold,
                "이하" => number <= threshold,
                "미만" => number < threshold,
                "같음" => Math.Abs(number - threshold) < 0.0000001,
                _ => number >= threshold,
            };
        });
    }

    private static bool TryConvertDouble(object value, out double result)
    {
        try
        {
            result = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
            return !double.IsNaN(result) && !double.IsInfinity(result);
        }
        catch
        {
            result = 0;
            return false;
        }
    }

    private void UpdateStatFilterOptions<T>()
    {
        var properties = TypeDescriptor.GetProperties(typeof(T)).Cast<PropertyDescriptor>()
            .Where(p => IsNumericType(p.PropertyType))
            .Where(p => p.DisplayName is not "Rank" and not "선수 코드")
            .Select(p => p.DisplayName)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        properties.Insert(0, "없음");

        var old1 = SelectedText(_stat1);
        var old2 = SelectedText(_stat2);
        _suppressEvents = true;
        try
        {
            ReplaceItems(_stat1, properties, properties.Contains(old1) ? old1 : "없음");
            ReplaceItems(_stat2, properties, properties.Contains(old2) ? old2 : "없음");
        }
        finally { _suppressEvents = false; }
    }

    private static bool IsNumericType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type == typeof(byte) || type == typeof(short) || type == typeof(int) || type == typeof(long) ||
               type == typeof(float) || type == typeof(double) || type == typeof(decimal);
    }

    private static void ReRankRows<T>(IReadOnlyList<T> rows)
    {
        if (rows.Count == 0) return;
        var rank = TypeDescriptor.GetProperties(typeof(T))["Rank"];
        if (rank is null || rank.IsReadOnly) return;
        for (var i = 0; i < rows.Count; i++) rank.SetValue(rows[i], i + 1);
    }

    private static void AddStatFilterDescription(List<string> parts, ComboBox stat, ComboBox op, TextBox value)
    {
        var statText = SelectedText(stat);
        var valueText = value.Text.Trim();
        if (statText is "" or "없음" || valueText.Length == 0) return;
        parts.Add($"{statText} {SelectedText(op)} {valueText}");
    }

    private void ResetDetailedFilters()
    {
        _playerNameFilter.Clear();
        if (_inning.Items.Count > 0) _inning.SelectedIndex = 0;
        if (_outs.Items.Count > 0) _outs.SelectedIndex = 0;
        if (_runners.Items.Count > 0) _runners.SelectedIndex = 0;
        if (_scoreSituation.Items.Count > 0) _scoreSituation.SelectedIndex = 0;
        if (_count.Items.Count > 0) _count.SelectedIndex = 0;
        if (_batOrder.Items.Count > 0) _batOrder.SelectedIndex = 0;
        if (_resultLimit.Items.Count > 0) _resultLimit.SelectedItem = "50";
        if (_stat1.Items.Count > 0) _stat1.SelectedItem = "없음";
        if (_stat2.Items.Count > 0) _stat2.SelectedItem = "없음";
        if (_stat1Operator.Items.Count > 0) _stat1Operator.SelectedItem = "이상";
        if (_stat2Operator.Items.Count > 0) _stat2Operator.SelectedItem = "이상";
        _stat1Value.Clear();
        _stat2Value.Clear();
    }

    private void ApplyGridSearch()
    {
        var query = _gridSearch.Text.Trim();
        _grid.CurrentCell = null;
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.IsNewRow) continue;
            row.Visible = query.Length == 0 || row.Cells
                .Cast<DataGridViewCell>()
                .Any(cell => Convert.ToString(cell.FormattedValue)?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true);
        }
    }

    private void OpenPlayerSearch()
    {
        if (_playerService is null) return;
        using var dialog = new PlayerSearchDialog(_playerService);
        if (dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPcode))
        {
            using var form = new PlayerDetailForm(_playerService, dialog.SelectedPcode);
            form.ShowDialog(this);
        }
    }

    private void OpenGridEntity(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _grid.Rows.Count) return;
        var item = _grid.Rows[rowIndex].DataBoundItem;
        if (item is null) return;
        var properties = TypeDescriptor.GetProperties(item);
        if (_section == RecordRoomSection.Team)
        {
            var teamCode = Convert.ToString(properties["TeamCode"]?.GetValue(item))
                ?? Convert.ToString(properties["Pcode"]?.GetValue(item));
            if (!string.IsNullOrWhiteSpace(teamCode) && _teamService is not null && _playerService is not null)
            {
                using var form = new TeamDetailForm(_teamService, _playerService, teamCode);
                form.ShowDialog(this);
            }
            return;
        }

        var pcode = Convert.ToString(properties["Pcode"]?.GetValue(item));
        if (!string.IsNullOrWhiteSpace(pcode) && _playerService is not null)
        {
            using var form = new PlayerDetailForm(_playerService, pcode);
            form.ShowDialog(this);
        }
    }

    private async Task OpenDataManagerAsync()
    {
        using (var manager = new MainForm())
            manager.ShowDialog(this);
        _league = null;
        _warDiagnostics = null;
        _parkDiagnostics = null;
        _parkV2Experiment = null;
        _snapshot = null;
        _snapshotKey = null;
        _catalog = await _database.GetCatalogAsync();
        PopulateCatalogFilters(selectLatestYear: false);
        await ReloadDimensionFiltersAsync();
        await RefreshViewAsync(force: true);
    }

    private async Task ExportCurrentGridAsync()
    {
        if (_grid.Rows.Count == 0) return;
        using var dialog = new SaveFileDialog
        {
            Filter = "CSV 파일 (*.csv)|*.csv",
            FileName = $"{_section}_{_role}_{_subView}_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            if (_section == RecordRoomSection.Constants && _subView == "대체후보")
            {
                var diagnostics = await GetWarDiagnosticsAsync(CancellationToken.None);
                await CsvExporter.ExportRowsAsync(diagnostics.Candidates, dialog.FileName);
                _status.Text = $"대체후보 전체 {diagnostics.Candidates.Count:N0}건 CSV 저장 완료: {dialog.FileName}";
            }
            else if (_section == RecordRoomSection.Constants && _subView == "파크팩터 진단")
            {
                var diagnostics = await GetParkDiagnosticsAsync(CancellationToken.None);
                await CsvExporter.ExportRowsAsync(diagnostics.Seasons, dialog.FileName);
                _status.Text = $"파크팩터 연도 진단 {diagnostics.Seasons.Count:N0}건 CSV 저장 완료: {dialog.FileName}";
            }
            else if (_section == RecordRoomSection.Constants && _subView == "파크팩터 상세")
            {
                var diagnostics = await GetParkDiagnosticsAsync(CancellationToken.None);
                await CsvExporter.ExportRowsAsync(diagnostics.Stadiums, dialog.FileName);
                _status.Text = $"파크팩터 상세 전체 {diagnostics.Stadiums.Count:N0}건 CSV 저장 완료: {dialog.FileName}";
            }
            else if (_section == RecordRoomSection.Constants && _subView == "KBO PF v2 실험")
            {
                var experiment = await GetParkV2ExperimentAsync(CancellationToken.None);
                await CsvExporter.ExportRowsAsync(experiment.Stadiums, dialog.FileName);
                _status.Text = $"KBO PF v2 실험 전체 {experiment.Stadiums.Count:N0}건 CSV 저장 완료: {dialog.FileName}";
            }
            else if (_section == RecordRoomSection.Constants && _subView == "WAR A/B")
            {
                var experiment = await GetParkV2ExperimentAsync(CancellationToken.None);
                await CsvExporter.ExportRowsAsync(experiment.WarComparisons, dialog.FileName);
                _status.Text = $"WAR A/B {experiment.WarComparisons.Count:N0}시즌 CSV 저장 완료: {dialog.FileName}";
            }
            else if (_section == RecordRoomSection.Constants && _subView == "PF 민감도 요약")
            {
                var sensitivity = await GetParkSensitivityAsync(CancellationToken.None);
                await CsvExporter.ExportRowsAsync(sensitivity.Summaries, dialog.FileName);
                _status.Text = $"PF 민감도 요약 {sensitivity.Summaries.Count:N0}정책 CSV 저장 완료: {dialog.FileName}";
            }
            else if (_section == RecordRoomSection.Constants && _subView == "PF 민감도 상세")
            {
                var sensitivity = await GetParkSensitivityAsync(CancellationToken.None);
                await CsvExporter.ExportRowsAsync(sensitivity.Details, dialog.FileName);
                _status.Text = $"PF 민감도 상세 {sensitivity.Details.Count:N0}건 CSV 저장 완료: {dialog.FileName}";
            }
            else if (_section == RecordRoomSection.Constants && _subView == "Replacement 민감도 요약")
            {
                var sensitivity = await GetReplacementSensitivityAsync(CancellationToken.None);
                await CsvExporter.ExportRowsAsync(sensitivity.Summaries, dialog.FileName);
                _status.Text = $"Replacement 민감도 요약 {sensitivity.Summaries.Count:N0}조합 CSV 저장 완료: {dialog.FileName}";
            }
            else if (_section == RecordRoomSection.Constants && _subView == "Replacement 민감도 상세")
            {
                var sensitivity = await GetReplacementSensitivityAsync(CancellationToken.None);
                await CsvExporter.ExportRowsAsync(sensitivity.Details, dialog.FileName);
                _status.Text = $"Replacement 민감도 상세 {sensitivity.Details.Count:N0}건 CSV 저장 완료: {dialog.FileName}";
            }
            else if (_section == RecordRoomSection.Constants && _subView == "WAR 분포 요약")
            {
                var distribution = await GetWarDistributionAsync(CancellationToken.None);
                await CsvExporter.ExportRowsAsync(distribution.Summaries, dialog.FileName);
                _status.Text = $"WAR 분포 요약 {distribution.Summaries.Count:N0}건 CSV 저장 완료: {dialog.FileName}";
            }
            else if (_section == RecordRoomSection.Constants && _subView == "WAR 분포 선수")
            {
                var distribution = await GetWarDistributionAsync(CancellationToken.None);
                await CsvExporter.ExportRowsAsync(distribution.Players, dialog.FileName);
                _status.Text = $"WAR 분포 선수 {distribution.Players.Count:N0}건 CSV 저장 완료: {dialog.FileName}";
            }
            else
            {
                await CsvExporter.ExportAsync(_grid, dialog.FileName);
                _status.Text = $"CSV 저장 완료: {dialog.FileName}";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "CSV 저장 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SetBusy(bool busy, string? message = null)
    {
        _progress.Visible = busy;
        _refreshButton.Enabled = !busy;
        if (!string.IsNullOrWhiteSpace(message)) _status.Text = message;
        UseWaitCursor = busy;
    }

    private void SetDateRange(DateTime start, DateTime end)
    {
        _suppressEvents = true;
        try
        {
            var safeStart = start < _startDate.MinDate ? _startDate.MinDate : start > _startDate.MaxDate ? _startDate.MaxDate : start;
            var safeEnd = end < _endDate.MinDate ? _endDate.MinDate : end > _endDate.MaxDate ? _endDate.MaxDate : end;
            _startDate.Value = safeStart;
            _endDate.Value = safeEnd < safeStart ? safeStart : safeEnd;
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private int? SelectedYear() => int.TryParse(SelectedText(_year), out var year) ? year : null;

    private double SelectedQualificationFraction()
    {
        var text = SelectedText(_qualification).TrimEnd('%');
        return double.TryParse(text, out var percent) ? percent / 100.0 : 0.0;
    }

    private static string SelectedText(ComboBox combo) => Convert.ToString(combo.SelectedItem) ?? string.Empty;
    private static string? EmptySelection(ComboBox combo) => SelectedText(combo) is "" or "전체" ? null : SelectedText(combo);

    private static void AddIfSelected(List<string> parts, ComboBox combo, string prefix, string suffix = "")
    {
        var text = SelectedText(combo);
        if (text is "" or "전체") return;
        parts.Add($"{prefix}{text}{suffix}");
    }

    private static void ReplaceItems(ComboBox combo, IEnumerable<string> items, string? selected)
    {
        var values = items.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        combo.BeginUpdate();
        combo.Items.Clear();
        combo.Items.AddRange(values.Cast<object>().ToArray());
        combo.EndUpdate();
        SelectText(combo, selected);
        if (combo.SelectedIndex < 0 && combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private static void SelectText(ComboBox combo, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) value = "전체";
        for (var index = 0; index < combo.Items.Count; index++)
        {
            if (!string.Equals(Convert.ToString(combo.Items[index]), value, StringComparison.OrdinalIgnoreCase)) continue;
            combo.SelectedIndex = index;
            return;
        }
    }

    private static void AddItems(ComboBox combo, params string[] values) => combo.Items.AddRange(values.Cast<object>().ToArray());

    private static ComboBox Combo(int width) => new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = width,
        Height = 28,
        Margin = new Padding(3, 3, 8, 3),
    };

    private static DateTimePicker DatePicker() => new()
    {
        Format = DateTimePickerFormat.Custom,
        CustomFormat = "yyyy-MM-dd",
        Width = 105,
        Height = 28,
        Margin = new Padding(3, 3, 3, 3),
    };

    private static Label FilterLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(4, 8, 2, 0),
        ForeColor = Muted,
    };

    private static Button ActionButton(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Height = 29,
        FlatStyle = FlatStyle.Flat,
        BackColor = Color.White,
        ForeColor = Color.FromArgb(55, 58, 65),
        Margin = new Padding(4, 2, 4, 2),
        Padding = new Padding(8, 0, 8, 0),
        UseVisualStyleBackColor = false,
    };

    private static Button NavigationButton(string text, int width)
    {
        var button = new UnderlineNavigationButton();
        button.Text = text;
        button.Width = width;
        button.Height = 42;
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = Color.Transparent;
        button.ForeColor = Color.FromArgb(55, 58, 65);
        button.Margin = new Padding(1, 0, 1, 0);
        button.UseVisualStyleBackColor = false;
        button.Cursor = Cursors.Hand;
        return button;
    }

    private static Button SubNavigationButton(string text)
    {
        var button = new UnderlineNavigationButton();
        button.Text = text;
        button.AutoSize = true;
        button.Height = 34;
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = Color.Transparent;
        button.ForeColor = Color.FromArgb(55, 58, 65);
        button.Margin = new Padding(1, 0, 1, 0);
        button.UseVisualStyleBackColor = false;
        button.Cursor = Cursors.Hand;
        return button;
    }

    private static void StyleNavigationButton(Button button, bool selected, bool topLevel)
    {
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = selected && !topLevel ? Accent : Color.Transparent;
        button.ForeColor = selected ? (topLevel ? Accent : Color.White) : Color.FromArgb(55, 58, 65);
        button.Font = new Font(button.Font, selected ? FontStyle.Bold : FontStyle.Regular);
        if (button is UnderlineNavigationButton underline)
        {
            underline.Selected = selected;
            underline.FillSelected = !topLevel;
            underline.Invalidate();
        }
    }

    private static DataGridView CreateGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToOrderColumns = true,
            AllowUserToResizeRows = false,
            AutoGenerateColumns = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
            BackgroundColor = Surface,
            BorderStyle = BorderStyle.None,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            EnableHeadersVisualStyles = false,
            ColumnHeadersHeight = 34,
            RowTemplate = { Height = 29 },
        };
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(247, 247, 248),
            ForeColor = Color.FromArgb(85, 88, 94),
            Font = new Font("맑은 고딕", 8.5F, FontStyle.Bold),
            Alignment = DataGridViewContentAlignment.MiddleCenter,
            SelectionBackColor = Color.FromArgb(247, 247, 248),
            SelectionForeColor = Color.FromArgb(85, 88, 94),
        };
        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Surface,
            ForeColor = Color.FromArgb(40, 43, 48),
            SelectionBackColor = Color.FromArgb(255, 228, 238),
            SelectionForeColor = Color.FromArgb(40, 43, 48),
            Padding = new Padding(2, 0, 2, 0),
        };
        grid.GridColor = Border;
        return grid;
    }

    private enum RecordRoomSection
    {
        Season,
        Career,
        Team,
        Constants,
    }

    private string RoleText() => _role == RecordRoomRole.Batter ? "타자" : "투수";

    private enum RecordRoomRole
    {
        Batter,
        Pitcher,
    }
}
