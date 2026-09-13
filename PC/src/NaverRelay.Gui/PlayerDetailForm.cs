using System.Drawing.Drawing2D;
using NaverRelay.Application.Players;
using NaverRelay.Gui.Services;

namespace NaverRelay.Gui;

internal sealed class PlayerDetailForm : Form
{
    private readonly IPlayerPageService _service;
    private readonly string _pcode;
    private PlayerPageData? _data;
    private int? _selectedYear;
    private string _selectedPage = "종합";

    private readonly Label _titleLabel = new() { AutoSize = true, Text = "선수정보", Font = new Font("맑은 고딕", 18F, FontStyle.Bold) };
    private readonly FlowLayoutPanel _navBar = new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 8) };
    private readonly FlowLayoutPanel _roleBar = new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 4) };
    private readonly FlowLayoutPanel _seasonBar = new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 8) };
    private readonly Panel _contentHost = new() { Dock = DockStyle.Fill, BackColor = Color.Transparent };
    private readonly StatusStrip _statusStrip = new();
    private readonly ToolStripStatusLabel _status = new("선수 데이터를 읽는 중입니다...");

    private readonly Dictionary<string, UnderlineNavigationButton> _navButtons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Button> _roleButtons = new(StringComparer.Ordinal);
    private const int CareerSeasonKey = int.MinValue;
    private readonly Dictionary<int, Button> _seasonButtons = new();
    private readonly Dictionary<string, Control> _pages = new(StringComparer.Ordinal);

    private readonly PlayerHeroCard _heroCard = new();
    private readonly MetricBulletPanel _summaryMetrics = new();
    private readonly RollingWrcChart _summaryRollingChart = new() { Dock = DockStyle.Fill, BackColor = Color.White };
    private readonly PitchUsageRadarPanel _summaryPitchRadar = new() { Dock = DockStyle.Fill, BackColor = Color.White };

    private readonly DataGridView _yearlyGrid = CreateGrid();
    private readonly DataGridView _summaryTableGrid = CreateGrid();
    private readonly DataGridView _opponentGrid = CreateGrid();
    private readonly DataGridView _situationGrid = CreateGrid();
    private readonly DataGridView _gameLogGrid = CreateGrid();
    private readonly DataGridView _paGrid = CreateGrid();
    private readonly DataGridView _pitchGrid = CreateGrid();
    private readonly DataGridView _valueGrid = CreateGrid();
    private readonly DataGridView _batPitchTypeGrid = CreateGrid();
    private readonly DataGridView _pitPitchTypeGrid = CreateGrid();

    private readonly Label _yearlyTitle = new() { AutoSize = true, Font = new Font("맑은 고딕", 10F, FontStyle.Bold), Text = "주요 기록" };
    private readonly FlowLayoutPanel _yearlyTabs = new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
    private readonly Dictionary<string, Button> _yearlyTabButtons = new(StringComparer.Ordinal);
    private string _selectedYearlyTab = "기본";

    private readonly Label _graphTitle = new() { AutoSize = true, Font = new Font("맑은 고딕", 10F, FontStyle.Bold), Text = "그래프" };
    private readonly RollingWrcChart _graphRollingChart = new() { Dock = DockStyle.Fill, BackColor = Color.White };
    private readonly DataGridView _graphGrid = CreateGrid();

    private readonly ComboBox _situationCategory = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    private readonly TabControl _playLogTabs = new() { Dock = DockStyle.Fill };
    private readonly TabControl _analysisTabs = new() { Dock = DockStyle.Fill };
    private readonly RichTextBox _formula = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        WordWrap = false,
        Font = new Font("Consolas", 10F),
        BackColor = Color.White,
    };

    public PlayerDetailForm(IPlayerPageService service, string pcode)
    {
        _service = service;
        _pcode = pcode;
        Text = "선수 상세";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1200, 820);
        ClientSize = new Size(1620, 960);
        Font = new Font("맑은 고딕", 9F);
        KeyPreview = true;
        BackColor = Color.FromArgb(243, 244, 246);

        BuildLayout();
        ConfigureGrids();
        ConfigureEvents();
        Shown += async (_, _) => await LoadPlayerAsync();
    }

    private void BuildLayout()
    {
        _statusStrip.Items.Add(_status);

        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(16, 12, 16, 0),
            BackColor = BackColor,
        };
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

        outer.Controls.Add(_titleLabel, 0, 0);
        outer.Controls.Add(BuildNavHost(), 0, 1);
        outer.Controls.Add(_roleBar, 0, 2);
        outer.Controls.Add(_seasonBar, 0, 3);
        outer.Controls.Add(_contentHost, 0, 4);
        outer.Controls.Add(_statusStrip, 0, 5);

        Controls.Add(outer);
        BuildNavigationButtons();
        BuildRoleButtons();
        BuildPages();
    }

    private Control BuildNavHost()
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 0, 6) };
        panel.Controls.Add(_navBar);
        return panel;
    }

    private void BuildNavigationButtons()
    {
        foreach (var title in new[] { "종합", "연도별", "그래프", "날짜별", "상황별", "상대별", "Playlog", "상세분석", "연봉" })
        {
            var button = new UnderlineNavigationButton();
            button.Text = title;
            button.Width = 86;
            button.Height = 54;
            button.Margin = new Padding(0, 0, 8, 0);
            button.FlatStyle = FlatStyle.Flat;
            button.BackColor = Color.White;
            button.ForeColor = Color.FromArgb(80, 80, 80);
            button.AccentColor = Color.FromArgb(232, 24, 92);
            button.Font = new Font("맑은 고딕", 9F, FontStyle.Bold);
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.FlatAppearance.BorderSize = 0;
            button.Click += (_, _) => SelectPage(title);
            _navButtons[title] = button;
            _navBar.Controls.Add(button);
        }
    }

    private void BuildRoleButtons()
    {
        foreach (var role in new[] { "타격", "투구", "수비", "정규", "포스트", "올스타", "없음" })
        {
            var button = CreateChipButton(role, 42);
            button.Enabled = role is "타격" or "투구" or "정규";
            _roleButtons[role] = button;
            _roleBar.Controls.Add(button);
        }
    }

    private void BuildSeasonButtons(IEnumerable<int>? years = null)
    {
        _seasonBar.SuspendLayout();
        _seasonBar.Controls.Clear();
        _seasonButtons.Clear();

        AddSeasonButton(null, "통산");
        foreach (var year in years ?? Enumerable.Empty<int>())
            AddSeasonButton(year, $"{year}G");

        _seasonBar.ResumeLayout();
        HighlightSeasonButtons();
    }

    private void AddSeasonButton(int? year, string text)
    {
        var button = CreateChipButton(text, 42);
        button.Click += (_, _) =>
        {
            _selectedYear = year;
            HighlightSeasonButtons();
            RefreshAllViews();
        };
        _seasonButtons[year ?? CareerSeasonKey] = button;
        _seasonBar.Controls.Add(button);
    }

    private Button CreateChipButton(string text, int height)
    {
        var button = new Button
        {
            AutoSize = true,
            Height = height,
            MinimumSize = new Size(42, height),
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(64, 67, 73),
            ForeColor = Color.White,
            Margin = new Padding(0, 0, 6, 0),
            Padding = new Padding(10, 0, 10, 0),
            Font = new Font("맑은 고딕", 9F, FontStyle.Bold),
        };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }

    private void BuildPages()
    {
        _contentHost.Controls.Clear();
        _pages.Clear();

        _pages["종합"] = BuildOverviewPage();
        _pages["연도별"] = BuildYearlyPage();
        _pages["그래프"] = BuildGraphPage();
        _pages["날짜별"] = WrapInCard("날짜별 로그", _gameLogGrid);
        _pages["상황별"] = BuildSituationPage();
        _pages["상대별"] = WrapInCard("상대별 기록", _opponentGrid);
        _pages["Playlog"] = BuildPlayLogPage();
        _pages["상세분석"] = BuildAnalysisPage();
        _pages["연봉"] = BuildUnsupportedPage("연봉 데이터는 현재 원본 JSON에 없어 표시하지 않습니다.");

        foreach (var page in _pages.Values)
        {
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            _contentHost.Controls.Add(page);
        }
    }

    private Control BuildOverviewPage()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, BackColor = BackColor };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 63));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 37));

        layout.Controls.Add(WrapInCard(null, _heroCard), 0, 0);
        layout.Controls.Add(WrapInCard("주요 지표", _summaryMetrics), 1, 0);
        layout.Controls.Add(WrapInCard("비주얼 요약", BuildOverviewRightPanel()), 2, 0);
        layout.Controls.Add(WrapInCard("주요 기록", _summaryTableGrid), 0, 1);
        layout.SetColumnSpan(layout.GetControlFromPosition(0, 1)!, 3);
        return layout;
    }

    private Control BuildOverviewRightPanel()
    {
        var host = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        _summaryRollingChart.Visible = true;
        _summaryPitchRadar.Visible = false;
        host.Controls.Add(_summaryRollingChart);
        host.Controls.Add(_summaryPitchRadar);
        return host;
    }

    private Control BuildYearlyPage()
    {
        var outer = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, BackColor = BackColor };
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var titlePanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        titlePanel.Controls.Add(_yearlyTitle);
        _yearlyTitle.Location = new Point(10, 8);
        outer.Controls.Add(titlePanel, 0, 0);

        foreach (var tab in new[] { "기본", "가치", "상대별", "상황별", "Playlog", "구종", "계산식" })
        {
            var button = CreateSectionButton(tab);
            button.Click += (_, _) => { _selectedYearlyTab = tab; HighlightYearlyTabs(); ApplyYearlyTab(); };
            _yearlyTabButtons[tab] = button;
            _yearlyTabs.Controls.Add(button);
        }
        outer.Controls.Add(_yearlyTabs, 0, 1);
        outer.Controls.Add(WrapInCard(null, _yearlyGrid), 0, 2);
        HighlightYearlyTabs();
        return outer;
    }

    private Control BuildGraphPage()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = BackColor };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
        var titlePanel = new Panel { Dock = DockStyle.Fill };
        titlePanel.Controls.Add(_graphTitle);
        _graphTitle.Location = new Point(10, 8);
        layout.Controls.Add(titlePanel, 0, 0);
        layout.Controls.Add(WrapInCard("Rolling 추이", _graphRollingChart), 0, 1);
        layout.Controls.Add(WrapInCard("데이터", _graphGrid), 0, 2);
        return layout;
    }

    private Control BuildSituationPage()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = BackColor };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _situationCategory.Items.AddRange(new object[] { "전체", "주자", "아웃", "이닝", "점수차", "득점권", "클러치" });
        if (_situationCategory.Items.Count > 0) _situationCategory.SelectedIndex = 0;
        var tool = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(8, 6, 8, 0) };
        tool.Controls.Add(new Label { Text = "상황 구분", AutoSize = true, Margin = new Padding(0, 9, 6, 0), Font = new Font("맑은 고딕", 9F, FontStyle.Bold) });
        tool.Controls.Add(_situationCategory);
        layout.Controls.Add(tool, 0, 0);
        layout.Controls.Add(WrapInCard(null, _situationGrid), 0, 1);
        return layout;
    }

    private Control BuildPlayLogPage()
    {
        _playLogTabs.TabPages.Clear();
        var tab1 = new TabPage("경기 로그") { Padding = new Padding(4) };
        var tab2 = new TabPage("타석 로그") { Padding = new Padding(4) };
        var tab3 = new TabPage("투구 로그") { Padding = new Padding(4) };
        tab1.Controls.Add(_gameLogGrid);
        tab2.Controls.Add(_paGrid);
        tab3.Controls.Add(_pitchGrid);
        _playLogTabs.TabPages.Add(tab1);
        _playLogTabs.TabPages.Add(tab2);
        _playLogTabs.TabPages.Add(tab3);
        return WrapInCard(null, _playLogTabs);
    }

    private Control BuildAnalysisPage()
    {
        _analysisTabs.TabPages.Clear();
        var tab1 = new TabPage("타자 구종별") { Padding = new Padding(4) };
        var tab2 = new TabPage("투수 구종별") { Padding = new Padding(4) };
        var tab3 = new TabPage("Value·WAR") { Padding = new Padding(4) };
        var tab4 = new TabPage("계산식") { Padding = new Padding(4) };
        tab1.Controls.Add(_batPitchTypeGrid);
        tab2.Controls.Add(_pitPitchTypeGrid);
        tab3.Controls.Add(_valueGrid);
        tab4.Controls.Add(_formula);
        _analysisTabs.TabPages.Add(tab1);
        _analysisTabs.TabPages.Add(tab2);
        _analysisTabs.TabPages.Add(tab3);
        _analysisTabs.TabPages.Add(tab4);
        return WrapInCard(null, _analysisTabs);
    }

    private Control BuildUnsupportedPage(string message)
    {
        var panel = WrapInCard(null, new Panel { Dock = DockStyle.Fill, BackColor = Color.White });
        var label = new Label
        {
            AutoSize = true,
            Text = message,
            Font = new Font("맑은 고딕", 11F),
            ForeColor = Color.DimGray,
            Location = new Point(20, 20),
        };
        panel.Controls[0].Controls.Add(label);
        return panel;
    }

    private Panel WrapInCard(string? title, Control child)
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1), BackColor = Color.FromArgb(216, 218, 222), Margin = new Padding(4) };
        var card = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(0) };
        panel.Controls.Add(card);

        if (!string.IsNullOrWhiteSpace(title))
        {
            var titleLabel = new Label
            {
                Text = title,
                Dock = DockStyle.Top,
                Height = 34,
                Padding = new Padding(12, 8, 0, 0),
                Font = new Font("맑은 고딕", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(39, 39, 42),
                BackColor = Color.White,
            };
            card.Controls.Add(titleLabel);
            child.Dock = DockStyle.Fill;
            child.Margin = new Padding(0);
            card.Controls.Add(child);
            child.BringToFront();
        }
        else
        {
            child.Dock = DockStyle.Fill;
            card.Controls.Add(child);
        }

        return panel;
    }

    private Button CreateSectionButton(string text)
    {
        var button = new Button
        {
            AutoSize = true,
            Text = text,
            Height = 34,
            Margin = new Padding(0, 0, 6, 0),
            Padding = new Padding(10, 0, 10, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(248, 248, 249),
            ForeColor = Color.FromArgb(80, 80, 80),
            Font = new Font("맑은 고딕", 9F, FontStyle.Bold),
        };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }

    private void ConfigureGrids()
    {
        foreach (var grid in new[] { _yearlyGrid, _summaryTableGrid, _opponentGrid, _situationGrid, _gameLogGrid, _paGrid, _pitchGrid, _valueGrid, _batPitchTypeGrid, _pitPitchTypeGrid, _graphGrid })
        {
            grid.DataBindingComplete += (_, _) =>
            {
                foreach (DataGridViewColumn column in grid.Columns)
                    column.SortMode = DataGridViewColumnSortMode.Automatic;
                GridNumberFormatter.Apply(grid);
            };
        }
    }

    private void ConfigureEvents()
    {
        KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode == Keys.Escape) Close();
        };
        _situationCategory.SelectedIndexChanged += (_, _) => ApplySituationGrid();
    }

    private async Task LoadPlayerAsync()
    {
        try
        {
            UseWaitCursor = true;
            Enabled = false;
            _status.Text = "SQLite에서 해당 선수 경기만 읽어 집계하고 있습니다...";
            _data = await Task.Run(() => _service.GetPlayerPage(_pcode));
            if (_data is null)
            {
                MessageBox.Show(this, "선수 정보를 찾지 못했습니다.", "선수 없음", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Close();
                return;
            }

            var years = _data.BattingSeasons.Select(x => x.Year)
                .Concat(_data.PitchingSeasons.Select(x => x.Year))
                .Distinct()
                .OrderBy(year => year)
                .ToList();
            _selectedYear = years.Count > 0 ? years[^1] : null;
            BuildSeasonButtons(years);
            _heroCard.SetProfile(_data.Profile, _selectedYear);
            _formula.Text = _data.FormulaDocumentation + "\n\n[상황별 기준]\n클러치: 7회 이후, 절대 점수차 3점 이내\n구종별 타격: 해당 타석의 최종 투구 구종 기준";
            UpdateRoleButtons();
            SelectPage("종합");
            RefreshAllViews();
            Text = $"{_data.Profile.Name} - 선수 상세";
        }
        catch (Exception ex)
        {
            _status.Text = "선수 페이지 생성 실패";
            MessageBox.Show(this, ex.ToString(), "선수 페이지 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Enabled = true;
            UseWaitCursor = false;
        }
    }

    private void UpdateRoleButtons()
    {
        if (_data is null) return;
        SetRoleSelected("타격", _data.HasBatting);
        SetRoleSelected("투구", _data.HasPitching);
        SetRoleSelected("정규", true);
        SetRoleSelected("수비", false);
        SetRoleSelected("포스트", false);
        SetRoleSelected("올스타", false);
        SetRoleSelected("없음", !_data.HasBatting && !_data.HasPitching);
    }

    private void SetRoleSelected(string role, bool selected)
    {
        if (!_roleButtons.TryGetValue(role, out var button)) return;
        button.BackColor = selected ? Color.FromArgb(42, 45, 52) : Color.FromArgb(189, 192, 198);
        button.ForeColor = Color.White;
    }

    private void SelectPage(string title)
    {
        _selectedPage = title;
        foreach (var pair in _navButtons)
        {
            pair.Value.Selected = string.Equals(pair.Key, title, StringComparison.Ordinal);
            pair.Value.FillSelected = true;
            pair.Value.BackColor = pair.Value.Selected ? Color.FromArgb(232, 24, 92) : Color.FromArgb(232, 232, 234);
            pair.Value.ForeColor = pair.Value.Selected ? Color.White : Color.FromArgb(118, 118, 125);
            pair.Value.Invalidate();
        }

        foreach (var pair in _pages)
            pair.Value.Visible = string.Equals(pair.Key, title, StringComparison.Ordinal);

        _pages[title].BringToFront();
        RefreshAllViews();
    }

    private void HighlightSeasonButtons()
    {
        foreach (var pair in _seasonButtons)
        {
            var selected = pair.Key == (_selectedYear ?? CareerSeasonKey);
            pair.Value.BackColor = selected ? Color.FromArgb(42, 45, 52) : Color.FromArgb(189, 192, 198);
            pair.Value.ForeColor = Color.White;
        }
    }

    private void HighlightYearlyTabs()
    {
        foreach (var pair in _yearlyTabButtons)
        {
            var selected = string.Equals(pair.Key, _selectedYearlyTab, StringComparison.Ordinal);
            pair.Value.BackColor = selected ? Color.White : Color.FromArgb(248, 248, 249);
            pair.Value.ForeColor = selected ? Color.FromArgb(232, 24, 92) : Color.FromArgb(80, 80, 80);
        }
    }

    private void RefreshAllViews()
    {
        if (_data is null) return;
        _heroCard.SetProfile(_data.Profile, _selectedYear);
        BindOverview();
        ApplyYearlyTab();
        BindGraph();
        ApplySituationGrid();
        Bind(_opponentGrid, FilterByYear(_data.OpponentSplits));
        Bind(_gameLogGrid, FilterByYear(_data.GameLogs, row => (int)(row.Year ?? 0)));
        Bind(_paGrid, FilterByYearByDate(_data.PlateAppearances));
        Bind(_pitchGrid, FilterByYearByDate(_data.Pitches));
        Bind(_valueGrid, FilterByYear(_data.ValueSeasons, row => row.Year));
        Bind(_batPitchTypeGrid, FilterByYear(_data.BattingByPitchType, row => row.Year));
        Bind(_pitPitchTypeGrid, FilterByYear(_data.PitchingByPitchType, row => row.Year));
        _status.Text = $"{_data.Profile.Name} · {(_selectedYear.HasValue ? _selectedYear.Value.ToString() : "통산")} · {_selectedPage}";
    }

    private void BindOverview()
    {
        if (_data is null) return;
        if (_data.HasPitching && !_data.HasBatting)
        {
            var rows = FilterByYear(_data.PitchingSeasons, row => row.Year).ToList();
            var row = rows.LastOrDefault();
            _summaryMetrics.SetMetrics(new[]
            {
                new MetricBullet("ERA", row?.ERA, 8),
                new MetricBullet("WHIP", row?.WHIP, 2.4, inverse:true),
                new MetricBullet("FIP", row?.Fip, 8, inverse:true),
                new MetricBullet("K/9", row?.StrikeoutsPerNine, 14),
                new MetricBullet("BB/9", row?.WalksPerNine, 8, inverse:true),
                new MetricBullet("WAR", row?.War, 8),
            });
            _summaryPitchRadar.Visible = true;
            _summaryRollingChart.Visible = false;
            _summaryPitchRadar.SetPitchTypes(FilterByYear(_data.PitchingByPitchType, row => row.Year).ToList());
            Bind(_summaryTableGrid, rows.Any() ? rows : _data.PitchingSeasons);
        }
        else
        {
            var rows = FilterByYear(_data.BattingSeasons, row => row.Year).ToList();
            var row = rows.LastOrDefault();
            _summaryMetrics.SetMetrics(new[]
            {
                new MetricBullet("AVG", row?.AVG, 0.400),
                new MetricBullet("OBP", row?.OBP, 0.500),
                new MetricBullet("SLG", row?.SLG, 0.800),
                new MetricBullet("OPS", row?.OPS, 1.200),
                new MetricBullet("wRC+", row?.WrcPlus, 200),
                new MetricBullet("WAR", row?.War, 10),
            });
            _summaryPitchRadar.Visible = false;
            _summaryRollingChart.Visible = true;
            _summaryRollingChart.SetPoints(GetRollingPoints());
            Bind(_summaryTableGrid, rows.Any() ? rows : _data.BattingSeasons);
        }
    }

    private void ApplyYearlyTab()
    {
        if (_data is null) return;
        _yearlyTitle.Text = _selectedYearlyTab switch
        {
            "기본" => "주요 기록",
            "가치" => "Value·WAR",
            "상대별" => "상대별 기록",
            "상황별" => "상황별 기록",
            "Playlog" => "날짜별 로그",
            "구종" => "구종별 기록",
            "계산식" => "계산식",
            _ => "주요 기록",
        };

        switch (_selectedYearlyTab)
        {
            case "가치":
                Bind(_yearlyGrid, FilterByYear(_data.ValueSeasons, row => row.Year));
                break;
            case "상대별":
                Bind(_yearlyGrid, FilterByYear(_data.OpponentSplits));
                break;
            case "상황별":
                Bind(_yearlyGrid, FilterByYear(_data.SituationSplits));
                break;
            case "Playlog":
                Bind(_yearlyGrid, FilterByYear(_data.GameLogs, row => (int)(row.Year ?? 0)));
                break;
            case "구종":
                if (_data.HasPitching && !_data.HasBatting)
                    Bind(_yearlyGrid, FilterByYear(_data.PitchingByPitchType, row => row.Year));
                else
                    Bind(_yearlyGrid, FilterByYear(_data.BattingByPitchType, row => row.Year));
                break;
            case "계산식":
                Bind(_yearlyGrid, new[] { new FormulaGridRow { 설명 = "Formula", 내용 = "상세 계산식은 [상세분석] → [계산식] 탭에서 확인" } });
                break;
            default:
                if (_data.HasPitching && !_data.HasBatting)
                    Bind(_yearlyGrid, FilterByYear(_data.PitchingSeasons, row => row.Year));
                else
                    Bind(_yearlyGrid, FilterByYear(_data.BattingSeasons, row => row.Year));
                break;
        }
    }

    private void BindGraph()
    {
        if (_data is null) return;
        if (_data.HasPitching && !_data.HasBatting)
        {
            var pitchRows = FilterByYear(_data.PitchingByPitchType, row => row.Year).ToList();
            Bind(_graphGrid, pitchRows);
            _graphTitle.Text = "구종별 사용 추이(표)";
            _graphRollingChart.SetPoints(Array.Empty<RollingMetricPoint>());
        }
        else
        {
            var points = GetRollingPoints();
            _graphRollingChart.SetPoints(points);
            Bind(_graphGrid, points);
            _graphTitle.Text = "Rolling wRC+";
        }
    }

    private IReadOnlyList<RollingMetricPoint> GetRollingPoints()
    {
        if (_data is null) return Array.Empty<RollingMetricPoint>();
        if (_data.RollingWrcPlus.TryGetValue(15, out var points))
        {
            if (_selectedYear.HasValue)
                return points.Where(point => point.Date.StartsWith(_selectedYear.Value.ToString(), StringComparison.Ordinal)).ToList();
            return points;
        }
        return Array.Empty<RollingMetricPoint>();
    }

    private void ApplySituationGrid()
    {
        if (_data is null) return;
        var category = _situationCategory.SelectedItem?.ToString() ?? "전체";
        var rows = FilterByYear(_data.SituationSplits).Where(row => category == "전체" || row.Category == category);
        Bind(_situationGrid, rows);
    }

    private IEnumerable<T> FilterByYear<T>(IEnumerable<T> rows, Func<T, int>? yearSelector = null)
    {
        yearSelector ??= InferYearSelector<T>();
        if (!_selectedYear.HasValue || yearSelector is null) return rows;
        return rows.Where(row => yearSelector(row) == _selectedYear.Value);
    }

    private IEnumerable<T> FilterByYearByDate<T>(IEnumerable<T> rows) where T : class
    {
        if (!_selectedYear.HasValue) return rows;
        var yearText = _selectedYear.Value.ToString();
        return rows.Where(row =>
        {
            var prop = row.GetType().GetProperty("Date");
            var value = prop?.GetValue(row)?.ToString();
            return !string.IsNullOrWhiteSpace(value) && value.StartsWith(yearText, StringComparison.Ordinal);
        });
    }

    private static Func<T, int>? InferYearSelector<T>()
    {
        var prop = typeof(T).GetProperty("Year");
        if (prop is null) return null;
        return row => Convert.ToInt32(prop.GetValue(row) ?? 0);
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
        BackgroundColor = Color.White,
        BorderStyle = BorderStyle.None,
        MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        RowHeadersVisible = false,
        ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText,
        EnableHeadersVisualStyles = false,
        ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(245, 245, 247),
            ForeColor = Color.FromArgb(75, 75, 80),
            Font = new Font("맑은 고딕", 9F, FontStyle.Bold),
        },
    };

    private sealed class FormulaGridRow
    {
        public string 설명 { get; init; } = string.Empty;
        public string 내용 { get; init; } = string.Empty;
    }
}

internal sealed class PlayerHeroCard : Panel
{
    private readonly Label _name = new() { AutoSize = true, Font = new Font("맑은 고딕", 16F, FontStyle.Bold), ForeColor = Color.White };
    private readonly Label _team = new() { AutoSize = true, Font = new Font("맑은 고딕", 10F, FontStyle.Bold), ForeColor = Color.White };
    private readonly Label _meta = new() { AutoSize = true, Font = new Font("맑은 고딕", 9F), ForeColor = Color.White };
    private readonly TableLayoutPanel _infoTable = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(18, 14, 18, 14), BackColor = Color.White };
    private readonly Panel _hero = new() { Dock = DockStyle.Top, Height = 120, BackColor = Color.FromArgb(243, 114, 29) };
    private readonly Label _career = new() { AutoSize = true, Font = new Font("맑은 고딕", 10F, FontStyle.Bold), ForeColor = Color.FromArgb(40, 40, 40) };
    private readonly Label _detail1 = new() { AutoSize = true, Font = new Font("맑은 고딕", 9.5F), ForeColor = Color.FromArgb(65, 65, 70) };
    private readonly Label _detail2 = new() { AutoSize = true, Font = new Font("맑은 고딕", 9.5F), ForeColor = Color.FromArgb(65, 65, 70) };
    private readonly Label _detail3 = new() { AutoSize = true, Font = new Font("맑은 고딕", 9.5F), ForeColor = Color.FromArgb(65, 65, 70) };
    private readonly Label _detail4 = new() { AutoSize = true, Font = new Font("맑은 고딕", 9.5F), ForeColor = Color.FromArgb(65, 65, 70) };

    public PlayerHeroCard()
    {
        BackColor = Color.White;
        Dock = DockStyle.Fill;
        _hero.Padding = new Padding(16, 14, 16, 10);
        _hero.Controls.Add(_name);
        _hero.Controls.Add(_team);
        _hero.Controls.Add(_meta);
        _name.Location = new Point(16, 16);
        _team.Location = new Point(18, 52);
        _meta.Location = new Point(18, 78);

        _infoTable.Controls.Add(_career, 0, 0);
        _infoTable.Controls.Add(_detail1, 0, 1);
        _infoTable.Controls.Add(_detail2, 0, 2);
        _infoTable.Controls.Add(_detail3, 0, 3);
        _infoTable.Controls.Add(_detail4, 0, 4);

        Controls.Add(_infoTable);
        Controls.Add(_hero);
    }

    public void SetProfile(PlayerProfile profile, int? selectedYear)
    {
        _name.Text = $"{profile.Name} ({profile.Pcode})";
        _team.Text = $"{profile.LatestTeam}, {profile.PrimaryPosition}";
        _meta.Text = $"{profile.Role} · {profile.BatsThrows}";
        _career.Text = selectedYear.HasValue ? $"선택 시즌: {selectedYear.Value}" : "통산";
        _detail1.Text = $"생년월일: {profile.BirthDate}";
        _detail2.Text = $"소속 이력: {profile.TeamHistory}";
        _detail3.Text = $"활동 연도: {profile.FirstSeason?.ToString() ?? "-"} ~ {profile.LastSeason?.ToString() ?? "-"}";
        var warText = profile.CareerWar.HasValue ? profile.CareerWar.Value.ToString("0.00") : "-";
        _detail4.Text = $"Career G {profile.CareerGames:N0} / PA {profile.CareerPlateAppearances:N0} / IP {profile.CareerInnings:0.0} / WAR {warText}";
    }
}

internal readonly record struct MetricBullet(string Name, double? Value, double Max, bool inverse = false);

internal sealed class MetricBulletPanel : Panel
{
    private IReadOnlyList<MetricBullet> _metrics = Array.Empty<MetricBullet>();

    public MetricBulletPanel()
    {
        DoubleBuffered = true;
        Dock = DockStyle.Fill;
        BackColor = Color.White;
        Padding = new Padding(18, 18, 18, 18);
    }

    public void SetMetrics(IReadOnlyList<MetricBullet> metrics)
    {
        _metrics = metrics;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var labelBrush = new SolidBrush(Color.FromArgb(55, 55, 60));
        using var valueBrush = new SolidBrush(Color.FromArgb(232, 24, 92));
        using var mutedBrush = new SolidBrush(Color.FromArgb(130, 130, 136));
        using var barPen = new Pen(Color.FromArgb(205, 207, 212), 3F);
        using var fillPen = new Pen(Color.FromArgb(232, 24, 92), 5F);
        using var titleFont = new Font("맑은 고딕", 9F, FontStyle.Bold);
        using var valueFont = new Font("맑은 고딕", 9F, FontStyle.Bold);
        var top = 26;
        var left = 26;
        var right = Math.Max(left + 120, Width - 26);
        foreach (var metric in _metrics)
        {
            e.Graphics.DrawString(metric.Name, titleFont, labelBrush, left, top - 12);
            var format = GridNumberFormatter.GetMetricPrecisionFormat(metric.Name) ?? (metric.Max <= 1.5 ? "0.000" : "0.0");
            var valueText = metric.Value.HasValue ? metric.Value.Value.ToString(format) : "-";
            var measured = e.Graphics.MeasureString(valueText, valueFont);
            e.Graphics.DrawString(valueText, valueFont, valueBrush, right - measured.Width, top - 14);
            var y = top + 18;
            e.Graphics.DrawLine(barPen, left, y, right, y);
            e.Graphics.FillEllipse(Brushes.Gainsboro, left + (right - left) / 2 - 3, y - 3, 6, 6);
            if (metric.Value.HasValue && metric.Max > 0)
            {
                var ratio = (float)Math.Clamp(metric.Value.Value / metric.Max, 0d, 1d);
                if (metric.inverse) ratio = 1F - ratio;
                var x = left + ratio * (right - left);
                e.Graphics.DrawLine(fillPen, left, y, x, y);
                e.Graphics.FillEllipse(Brushes.White, x - 9, y - 9, 18, 18);
                e.Graphics.DrawEllipse(Pens.Black, x - 9, y - 9, 18, 18);
            }
            top += 48;
        }
        e.Graphics.DrawString("Low", titleFont, mutedBrush, left, Height - 24);
        e.Graphics.DrawString("Avg", titleFont, mutedBrush, left + 80, Height - 24);
        e.Graphics.DrawString("High", titleFont, valueBrush, left + 160, Height - 24);
    }
}

internal sealed class PitchUsageRadarPanel : Panel
{
    private IReadOnlyList<PlayerPitchTypePitchingRow> _rows = Array.Empty<PlayerPitchTypePitchingRow>();
    public PitchUsageRadarPanel()
    {
        DoubleBuffered = true;
        Dock = DockStyle.Fill;
        BackColor = Color.White;
        Padding = new Padding(12);
    }

    public void SetPitchTypes(IReadOnlyList<PlayerPitchTypePitchingRow> rows)
    {
        _rows = rows.OrderByDescending(row => row.Pitches).Take(5).ToList();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var center = new PointF(Width / 2F, Height / 2F + 10F);
        var radius = Math.Min(Width, Height) * 0.28F;
        if (_rows.Count == 0)
        {
            TextRenderer.DrawText(e.Graphics, "표시할 구종 데이터가 없습니다.", Font, ClientRectangle, Color.DimGray,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }
        using var gridPen = new Pen(Color.FromArgb(225, 226, 229), 1F);
        for (var layer = 1; layer <= 4; layer++)
        {
            var r = radius * layer / 4F;
            DrawPolygon(e.Graphics, gridPen, center, r, _rows.Count);
        }
        using var axisPen = new Pen(Color.FromArgb(200, 202, 207), 1F);
        for (var i = 0; i < _rows.Count; i++)
        {
            var p = Polar(center, radius, i, _rows.Count);
            e.Graphics.DrawLine(axisPen, center, p);
            var labelRect = new RectangleF(p.X - 40, p.Y - 10, 80, 20);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            e.Graphics.DrawString(_rows[i].PitchType, new Font("맑은 고딕", 8.5F, FontStyle.Bold), Brushes.DimGray, labelRect, sf);
        }
        var points = new List<PointF>();
        foreach (var (row, index) in _rows.Select((row, index) => (row, index)))
        {
            var ratio = (float)Math.Clamp((row.UsageRate ?? 0d) / 100d, 0d, 1d);
            points.Add(Polar(center, radius * ratio, index, _rows.Count));
        }
        using var brush = new SolidBrush(Color.FromArgb(110, 91, 193, 97));
        using var pen = new Pen(Color.FromArgb(76, 175, 80), 2F);
        e.Graphics.FillPolygon(brush, points.ToArray());
        e.Graphics.DrawPolygon(pen, points.ToArray());
    }

    private static void DrawPolygon(Graphics g, Pen pen, PointF center, float radius, int sides)
    {
        var pts = Enumerable.Range(0, sides).Select(i => Polar(center, radius, i, sides)).ToArray();
        g.DrawPolygon(pen, pts);
    }

    private static PointF Polar(PointF center, float radius, int index, int total)
    {
        var angle = -Math.PI / 2 + (Math.PI * 2 * index / total);
        return new PointF(center.X + (float)(Math.Cos(angle) * radius), center.Y + (float)(Math.Sin(angle) * radius));
    }
}
