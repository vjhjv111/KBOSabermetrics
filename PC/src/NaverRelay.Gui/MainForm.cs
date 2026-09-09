using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using NaverRelay.Application.Players;
using NaverRelay.Application.Importing;
using NaverRelay.Application.Queries;
using NaverRelay.Application.Statistics;
using NaverRelay.Application.Teams;
using NaverRelay.Gui.Models;
using NaverRelay.Gui.Services;
using NaverRelay.Infrastructure.Sqlite;
using NaverRelay.Parsing;

namespace NaverRelay.Gui;

public partial class MainForm : Form
{
    private const int RawPageSize = 5_000;

    private readonly List<InputDocument> _documents = new();
    private readonly List<ParsingFailure> _failures = new();
    private readonly Dictionary<string, ListViewItem> _documentItems = new(StringComparer.Ordinal);
    private readonly Stopwatch _stopwatch = new();
    private readonly DatabaseCacheService _databaseCache = new();
    private readonly DatabaseAnalyticsService _databaseAnalytics;
    private IPlayerPageService? _playerPageService;
    private ITeamPageService? _teamPageService;
    private DatabaseCatalog? _databaseCatalog;
    private DatabaseFilterOptions? _databaseFilterOptions;
    private LeagueReference? _leagueReference;
    private AnalyticsSnapshot? _activeAnalytics;
    private string? _activeAnalyticsKey;
    private CancellationTokenSource? _viewLoadCts;
    private int _databaseGameCount;
    private int _rawPageIndex;
    private bool _suppressViewRefresh;
    private readonly TabPage _tabParkFactors = new("파크 팩터");
    private readonly Button _btnRawPrevious = new() { Text = "◀ 이전", Size = new Size(72, 27), Visible = false };
    private readonly Button _btnRawNext = new() { Text = "다음 ▶", Size = new Size(72, 27), Visible = false };
    private readonly Label _lblRawPage = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Size = new Size(150, 27), Visible = false };
    private readonly ToolStripTextBox _toolPlayerSearch = new()
    {
        AutoSize = false,
        Width = 180,
        ToolTipText = "선수 이름 또는 선수 코드 검색 (Ctrl+F)",
    };
    private readonly ToolStripButton _toolPlayerSearchButton = new("선수 페이지");
    private readonly ToolStripButton _toolTeamPageButton = new("팀 페이지")
    {
        ToolTipText = "선택한 팀 또는 팀 목록에서 팀 상세 페이지 열기 (Ctrl+T)",
    };
    private bool _databaseLoaded;
    private readonly System.Windows.Forms.Timer _elapsedTimer;

    private CancellationTokenSource? _operationCts;
    private bool _isBusy;
    private bool _isClosing;
    private bool _isPopulatingDocuments;
    private bool _isUpdatingYearFilter;
    private bool _isUpdatingDimensionFilters;
    private int _previewVersion;
    private readonly DataGridView gridParkFactors = new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AutoGenerateColumns = true, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells };
    private readonly ComboBox cboPeriodFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 115 };
    private readonly DateTimePicker dtStartFilter = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd", Width = 105 };
    private readonly DateTimePicker dtEndFilter = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd", Width = 105 };
    private readonly ComboBox cboOpponentFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
    private readonly ComboBox cboVenueFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 85 };
    private readonly ComboBox cboWeekdayFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80 };
    private readonly ComboBox cboStadiumFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
    private bool _isUpdatingPeriodFilters;

    public MainForm()
    {
        InitializeComponent();
        _databaseAnalytics = new DatabaseAnalyticsService(_databaseCache);
        txtSqliteDatabasePath.Text = _databaseCache.DatabasePath;
        UpdateSqliteDatabaseInfo();
        InitializeSummaryCards();
        _tabParkFactors.Controls.Add(gridParkFactors);
        tabResults.TabPages.Add(_tabParkFactors);
        ConfigureGrids();
        InitializeAdvancedFilters();
        InitializeRawPager();
        InitializePlayerSearch();
        InitializeTeamNavigation();
        WireEvents();

        // SplitContainer는 디자이너 초기화 시점에 실제 너비가 아직 확정되지 않아
        // SplitterDistance 예외가 날 수 있으므로 화면 표시 후 안전하게 설정합니다.
        Shown += async (_, _) =>
        {
            ApplySafeMainSplitterLayout();
            await LoadDatabaseCacheAsync();
        };
        splitMain.SizeChanged += (_, _) => ApplySafeMainSplitterLayout();

        _elapsedTimer = new System.Windows.Forms.Timer(components)
        {
            Interval = 500,
        };
        _elapsedTimer.Tick += (_, _) =>
            statusElapsed.Text = $"경과 시간: {_stopwatch.Elapsed:hh\\:mm\\:ss}";

        txtOutputPath.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "NaverRelayNormalized",
            DateTime.Now.ToString("yyyyMMdd_HHmmss"));

        ResetResultViews();
        UpdateDocumentCount();
        UpdateCommandStates();
        AppendLog("GUI 준비 완료. JSON/ZIP 파일 또는 폴더를 선택하세요.");
    }


    private async Task LoadDatabaseCacheAsync()
    {
        if (_databaseLoaded || _isClosing) return;

        BeginOperation("SQLite DB 준비 중", trackElapsed: true);
        try
        {
            statusLabel.Text = "SQLite 스키마를 확인하는 중입니다...";
            lblProgressDetail.Text = _databaseCache.DatabasePath;
            await _databaseCache.InitializeAsync(_operationCts!.Token);

            var indexProgress = new Progress<DatabaseIndexProgress>(value =>
            {
                if (_isClosing) return;
                var percent = value.Total <= 0 ? 0 : (int)Math.Round(value.Current * 100.0 / value.Total);
                progressBar.Value = Math.Clamp(percent, 0, 100);
                lblCurrentFile.Text = "현재 작업: SQLite 읽기 인덱스 생성";
                lblProgressDetail.Text = value.Message;
                statusLabel.Text = value.Message;
            });

            // 구버전 DB도 전체 경기 객체를 한꺼번에 만들지 않고 소량 배치로 인덱스만 보강합니다.
            await _databaseCache.EnsureReadIndexesAsync(indexProgress, _operationCts.Token);
            if (_isClosing) return;

            _databaseLoaded = true;
            _playerPageService = new DatabasePlayerPageService(_databaseCache);
            _teamPageService = new DatabaseTeamPageService(_databaseCache);
            await ReloadDatabaseCatalogAsync(selectLatestYear: true, resetDates: true, cancellationToken: _operationCts.Token);
            UpdateSqliteDatabaseInfo();

            progressBar.Value = 100;
            lblProgressDetail.Text = $"DB: {_databaseCache.DatabasePath}";
            if (_databaseGameCount > 0)
            {
                AppendLog($"SQLite 관계형 데이터 로드 완료: {_databaseGameCount:N0}경기 (JSON 재조회 없음)");
                statusLabel.Text = $"DB 준비 완료: {_databaseGameCount:N0}경기";
                await RefreshCurrentViewAsync(forceAnalytics: true, externalToken: _operationCts.Token);
            }
            else
            {
                ResetResultViews();
                AppendLog($"SQLite 캐시 준비 완료: {_databaseCache.DatabasePath}");
                statusLabel.Text = "SQLite 캐시 준비 완료";
            }
        }
        catch (OperationCanceledException)
        {
            statusLabel.Text = "DB 준비 취소";
            AppendLog("SQLite DB 준비가 취소되었습니다.");
        }
        catch (Exception ex)
        {
            AppendLog($"SQLite 캐시 준비 실패: {ex.Message}");
            statusLabel.Text = "캐시 준비 실패 - JSON 파싱은 계속 사용할 수 있습니다.";
        }
        finally
        {
            EndOperation();
        }
    }



    private void InitializeRawPager()
    {
        _btnRawPrevious.Location = new Point(955, 37);
        _lblRawPage.Location = new Point(1_032, 37);
        _btnRawNext.Location = new Point(1_187, 37);
        _btnRawPrevious.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _lblRawPage.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _btnRawNext.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        panelGridTools.Controls.AddRange([_btnRawPrevious, _lblRawPage, _btnRawNext]);

        panelGridTools.Resize += (_, _) =>
        {
            var right = panelGridTools.ClientSize.Width - 8;
            _btnRawNext.Left = Math.Max(0, right - _btnRawNext.Width);
            _lblRawPage.Left = Math.Max(0, _btnRawNext.Left - 6 - _lblRawPage.Width);
            _btnRawPrevious.Left = Math.Max(0, _lblRawPage.Left - 6 - _btnRawPrevious.Width);
        };
        _btnRawPrevious.Click += async (_, _) =>
        {
            if (_rawPageIndex <= 0) return;
            _rawPageIndex--;
            await RefreshCurrentViewAsync();
        };
        _btnRawNext.Click += async (_, _) =>
        {
            _rawPageIndex++;
            await RefreshCurrentViewAsync();
        };
    }

    private void InitializePlayerSearch()
    {
        toolStripMain.Items.Add(new ToolStripSeparator());
        toolStripMain.Items.Add(new ToolStripLabel("선수 검색:"));
        toolStripMain.Items.Add(_toolPlayerSearch);
        toolStripMain.Items.Add(_toolPlayerSearchButton);

        _toolPlayerSearch.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode != Keys.Enter) return;
            eventArgs.SuppressKeyPress = true;
            OpenPlayerSearch(_toolPlayerSearch.Text);
        };
        _toolPlayerSearchButton.Click += (_, _) => OpenPlayerSearch(_toolPlayerSearch.Text);

        foreach (var grid in new[]
        {
            gridBatterStats, gridPitcherStats, gridBatterSabermetrics, gridPitcherSabermetrics,
            gridBatterDiscipline, gridPitcherDiscipline, gridBatterValue, gridPitcherValue,
        })
        {
            grid.CellDoubleClick += (_, eventArgs) =>
            {
                if (eventArgs.RowIndex >= 0) OpenPlayerFromGrid(grid, eventArgs.RowIndex);
            };
        }
    }

    private void OpenPlayerSearch(string? initialQuery = null)
    {
        if (_databaseGameCount == 0)
        {
            MessageBox.Show(this, "먼저 SQLite 캐시를 불러오거나 경기 JSON을 파싱해 주세요.",
                "선수 데이터 없음", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var service = GetPlayerPageService();
        var query = initialQuery?.Trim();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var exact = service.SearchPlayers(query, 200)
                .Where(item => string.Equals(item.Name, query, StringComparison.CurrentCultureIgnoreCase)
                            || string.Equals(item.Pcode, query, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (exact.Count == 1)
            {
                OpenPlayerPage(exact[0].Pcode);
                return;
            }
        }

        using var dialog = new PlayerSearchDialog(service, query);
        if (dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPcode))
        {
            OpenPlayerPage(dialog.SelectedPcode);
        }
    }

    private void OpenPlayerFromGrid(DataGridView grid, int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= grid.Rows.Count) return;
        var item = grid.Rows[rowIndex].DataBoundItem;
        var property = item?.GetType().GetProperty("Pcode");
        var pcode = property?.GetValue(item)?.ToString();
        if (!string.IsNullOrWhiteSpace(pcode)) OpenPlayerPage(pcode);
    }

    private void OpenPlayerPage(string pcode)
    {
        var form = new PlayerDetailForm(GetPlayerPageService(), pcode);
        form.Show(this);
    }

    private IPlayerPageService GetPlayerPageService() =>
        _playerPageService ??= new DatabasePlayerPageService(_databaseCache);

    private void InvalidatePlayerPageService() =>
        _playerPageService = _databaseLoaded ? new DatabasePlayerPageService(_databaseCache) : null;

    private void InitializeTeamNavigation()
    {
        toolStripMain.Items.Add(new ToolStripSeparator());
        toolStripMain.Items.Add(_toolTeamPageButton);
        _toolTeamPageButton.Click += (_, _) => OpenTeamPage();
    }

    private void OpenTeamPage()
    {
        if (_databaseGameCount == 0)
        {
            MessageBox.Show(this, "먼저 정규시즌 경기 JSON을 관계형 SQLite DB에 적재해 주세요.",
                "팀 데이터 없음", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var selected = cboTeamFilter.SelectedItem?.ToString();
        if (!string.IsNullOrWhiteSpace(selected) &&
            !string.Equals(selected, "전체 팀", StringComparison.Ordinal))
        {
            OpenTeamPage(selected);
            return;
        }

        using var dialog = new TeamSelectionDialog(GetTeamPageService());
        if (dialog.ShowDialog(this) == DialogResult.OK &&
            !string.IsNullOrWhiteSpace(dialog.SelectedTeamCode))
        {
            OpenTeamPage(dialog.SelectedTeamCode);
        }
    }

    private void OpenTeamPage(string teamCode)
    {
        var form = new TeamDetailForm(GetTeamPageService(), GetPlayerPageService(), teamCode);
        form.Show(this);
    }

    private ITeamPageService GetTeamPageService() =>
        _teamPageService ??= new DatabaseTeamPageService(_databaseCache);

    private void InvalidateTeamPageService() =>
        _teamPageService = _databaseLoaded ? new DatabaseTeamPageService(_databaseCache) : null;


    private void InitializeAdvancedFilters()
    {
        // 기존 검색 행 아래에 기간/상대/홈원정/요일/구장 필터를 추가합니다.
        if (rightLayout.RowStyles.Count > 2)
            rightLayout.RowStyles[2].Height = 112;

        cboPeriodFilter.Items.AddRange(["전체 기간", "직접 지정", "최근 7일", "최근 14일", "최근 30일", "최근 60일", "최근 90일", "전반기", "후반기", "최근 5경기", "최근 10경기", "최근 20경기", "최근 30경기"]);
        cboPeriodFilter.SelectedIndex = 0;
        cboOpponentFilter.Items.Add("전체 상대");
        cboOpponentFilter.SelectedIndex = 0;
        cboVenueFilter.Items.AddRange(["전체 장소", "홈", "원정"]);
        cboVenueFilter.SelectedIndex = 0;
        cboWeekdayFilter.Items.AddRange(["전체 요일", "월", "화", "수", "목", "금", "토", "일"]);
        cboWeekdayFilter.SelectedIndex = 0;
        cboStadiumFilter.Items.Add("전체 구장");
        cboStadiumFilter.SelectedIndex = 0;

        var min = new DateTime(2000, 1, 1);
        var max = new DateTime(2100, 12, 31);
        dtStartFilter.MinDate = min; dtStartFilter.MaxDate = max;
        dtEndFilter.MinDate = min; dtEndFilter.MaxDate = max;
        dtStartFilter.Value = DateTime.Today.AddMonths(-1);
        dtEndFilter.Value = DateTime.Today;
        dtStartFilter.Enabled = dtEndFilter.Enabled = false;

        AddFilterControl(new Label { Text = "기간:", AutoSize = true }, 5, 78);
        AddFilterControl(cboPeriodFilter, 45, 72);
        AddFilterControl(dtStartFilter, 166, 72);
        AddFilterControl(new Label { Text = "~", AutoSize = true }, 274, 78);
        AddFilterControl(dtEndFilter, 290, 72);
        AddFilterControl(new Label { Text = "상대:", AutoSize = true }, 403, 78);
        AddFilterControl(cboOpponentFilter, 445, 72);
        AddFilterControl(new Label { Text = "장소:", AutoSize = true }, 542, 78);
        AddFilterControl(cboVenueFilter, 584, 72);
        AddFilterControl(new Label { Text = "요일:", AutoSize = true }, 676, 78);
        AddFilterControl(cboWeekdayFilter, 718, 72);
        AddFilterControl(new Label { Text = "구장:", AutoSize = true }, 805, 78);
        AddFilterControl(cboStadiumFilter, 847, 72);
    }

    private void AddFilterControl(Control control, int x, int y)
    {
        control.Location = new Point(x, y);
        if (control is ComboBox or DateTimePicker) control.Height = 25;
        panelGridTools.Controls.Add(control);
    }

    private void ApplySafeMainSplitterLayout()
    {
        if (splitMain.IsDisposed || splitMain.ClientSize.Width <= 0)
        {
            return;
        }

        var totalSize = splitMain.Orientation == Orientation.Vertical
            ? splitMain.ClientSize.Width
            : splitMain.ClientSize.Height;

        if (totalSize <= splitMain.SplitterWidth)
        {
            return;
        }

        // 정상 크기에서는 기존 의도대로 좌측 340px, 우측 700px 이상을 확보합니다.
        // 창이 더 작아졌을 때는 현재 크기에 맞춰 최소값을 자동 축소합니다.
        const int preferredPanel1Min = 340;
        const int preferredPanel2Min = 700;
        const int preferredDistance = 410;

        var available = totalSize - splitMain.SplitterWidth;
        var panel1Min = Math.Min(preferredPanel1Min, Math.Max(0, available / 3));
        var panel2Min = Math.Min(preferredPanel2Min, Math.Max(0, available - panel1Min));

        // 두 최소 크기의 합이 가용 영역을 넘지 않도록 마지막으로 보정합니다.
        if (panel1Min + panel2Min > available)
        {
            panel2Min = Math.Max(0, available - panel1Min);
        }

        splitMain.Panel1MinSize = panel1Min;
        splitMain.Panel2MinSize = panel2Min;

        var maxDistance = available - panel2Min;
        if (maxDistance < panel1Min)
        {
            return;
        }

        var desiredDistance = Math.Clamp(preferredDistance, panel1Min, maxDistance);
        if (splitMain.SplitterDistance != desiredDistance)
        {
            splitMain.SplitterDistance = desiredDistance;
        }
    }

    private void InitializeSummaryCards()
    {
        flowSummary.Controls.Clear();
        lblGameCountValue = AddSummaryCard("경기", "0");
        lblPaCountValue = AddSummaryCard("완료 타석", "0");
        lblPitchCountValue = AddSummaryCard("실제 투구", "0");
        lblRunnerCountValue = AddSummaryCard("주루 이벤트", "0");
        lblChangeCountValue = AddSummaryCard("선수 교체", "0");
        lblWarningCountValue = AddSummaryCard("경고", "0");
        lblErrorCountValue = AddSummaryCard("오류", "0");
    }

    private Label AddSummaryCard(string title, string initialValue)
    {
        var panel = new Panel
        {
            Width = 126,
            Height = 74,
            Margin = new Padding(0, 0, 7, 0),
            BorderStyle = BorderStyle.FixedSingle,
        };
        var titleLabel = new Label
        {
            Text = title,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            Height = 28,
        };
        var valueLabel = new Label
        {
            Text = initialValue,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Fill,
            Font = new Font(Font.FontFamily, 16F, FontStyle.Bold),
        };
        panel.Controls.Add(valueLabel);
        panel.Controls.Add(titleLabel);
        flowSummary.Controls.Add(panel);
        return valueLabel;
    }

    private void ConfigureGrids()
    {
        foreach (var grid in GetAllGrids())
        {
            grid.DataBindingComplete += (_, _) => FormatGrid(grid);
            grid.CellFormatting += (_, eventArgs) => GridNumberFormatter.FormatCell(grid, eventArgs);
            grid.CellDoubleClick += (_, eventArgs) =>
            {
                if (eventArgs.RowIndex >= 0)
                {
                    grid.Rows[eventArgs.RowIndex].Selected = true;
                }
            };
        }
    }

    private void WireEvents()
    {
        menuOpenFile.ShortcutKeys = Keys.Control | Keys.O;
        menuOpenFolder.ShortcutKeys = Keys.Control | Keys.Shift | Keys.O;
        menuStart.ShortcutKeys = Keys.F5;
        menuCancel.ShortcutKeyDisplayString = "Esc";
        menuSaveResults.ShortcutKeys = Keys.Control | Keys.S;
        menuExportCsv.ShortcutKeys = Keys.Control | Keys.Shift | Keys.S;

        cboYearFilter.SelectedIndexChanged += async (_, _) =>
        {
            if (_suppressViewRefresh || _isUpdatingYearFilter || _databaseGameCount == 0) return;
            _rawPageIndex = 0;
            InvalidateActiveAnalytics();
            await ReloadDatabaseFilterOptionsAsync(resetDates: true);
            await RefreshCurrentViewAsync();
            AppendLog($"연도 필터 적용: {cboYearFilter.SelectedItem}");
        };

        cboCompetitionFilter.SelectedIndexChanged += async (_, _) =>
        {
            if (_suppressViewRefresh || _isUpdatingDimensionFilters || _databaseGameCount == 0) return;
            _rawPageIndex = 0;
            InvalidateActiveAnalytics();
            await ReloadDatabaseFilterOptionsAsync(resetDates: true);
            await RefreshCurrentViewAsync();
            AppendLog($"경기 구분 필터 적용: {cboCompetitionFilter.SelectedItem}");
        };

        cboTeamFilter.SelectedIndexChanged += async (_, _) =>
        {
            if (_suppressViewRefresh || _isUpdatingDimensionFilters || _databaseGameCount == 0) return;
            _rawPageIndex = 0;
            InvalidateActiveAnalytics();
            PopulateOpponentItems();
            await RefreshCurrentViewAsync();
            AppendLog($"팀 필터 적용: {cboTeamFilter.SelectedItem}");
        };

        cboPeriodFilter.SelectedIndexChanged += async (_, _) =>
        {
            if (_suppressViewRefresh || _isUpdatingPeriodFilters || _databaseGameCount == 0) return;
            var custom = string.Equals(cboPeriodFilter.SelectedItem?.ToString(), "직접 지정", StringComparison.Ordinal);
            dtStartFilter.Enabled = dtEndFilter.Enabled = custom;
            _rawPageIndex = 0;
            InvalidateActiveAnalytics();
            await RefreshCurrentViewAsync();
            AppendLog($"기간 필터 적용: {cboPeriodFilter.SelectedItem}");
        };
        dtStartFilter.ValueChanged += async (_, _) =>
        {
            if (_suppressViewRefresh || _isUpdatingPeriodFilters || !dtStartFilter.Enabled || _databaseGameCount == 0) return;
            _rawPageIndex = 0;
            InvalidateActiveAnalytics();
            await RefreshCurrentViewAsync();
        };
        dtEndFilter.ValueChanged += async (_, _) =>
        {
            if (_suppressViewRefresh || _isUpdatingPeriodFilters || !dtEndFilter.Enabled || _databaseGameCount == 0) return;
            _rawPageIndex = 0;
            InvalidateActiveAnalytics();
            await RefreshCurrentViewAsync();
        };
        cboOpponentFilter.SelectedIndexChanged += async (_, _) =>
        {
            if (_suppressViewRefresh || _isUpdatingPeriodFilters || _databaseGameCount == 0) return;
            _rawPageIndex = 0;
            InvalidateActiveAnalytics();
            await RefreshCurrentViewAsync();
        };
        cboVenueFilter.SelectedIndexChanged += async (_, _) =>
        {
            if (_suppressViewRefresh || _databaseGameCount == 0) return;
            _rawPageIndex = 0;
            InvalidateActiveAnalytics();
            await RefreshCurrentViewAsync();
        };
        cboWeekdayFilter.SelectedIndexChanged += async (_, _) =>
        {
            if (_suppressViewRefresh || _databaseGameCount == 0) return;
            _rawPageIndex = 0;
            InvalidateActiveAnalytics();
            await RefreshCurrentViewAsync();
        };
        cboStadiumFilter.SelectedIndexChanged += async (_, _) =>
        {
            if (_suppressViewRefresh || _isUpdatingPeriodFilters || _databaseGameCount == 0) return;
            _rawPageIndex = 0;
            InvalidateActiveAnalytics();
            await RefreshCurrentViewAsync();
        };

        cboPositionFilter.SelectedIndexChanged += async (_, _) =>
        {
            if (_suppressViewRefresh || _databaseGameCount == 0) return;
            await RefreshCurrentViewAsync();
            AppendLog($"포지션 필터 적용: {cboPositionFilter.SelectedItem}");
        };
        cboPaQualificationFilter.SelectedIndexChanged += async (_, _) =>
        {
            if (_suppressViewRefresh || _databaseGameCount == 0) return;
            await RefreshCurrentViewAsync();
            AppendLog($"규정타석 필터 적용: {cboPaQualificationFilter.SelectedItem}");
        };
        cboIpQualificationFilter.SelectedIndexChanged += async (_, _) =>
        {
            if (_suppressViewRefresh || _databaseGameCount == 0) return;
            await RefreshCurrentViewAsync();
            AppendLog($"규정이닝 필터 적용: {cboIpQualificationFilter.SelectedItem}");
        };

        btnOpenFile.Click += async (_, _) => await SelectInputFileAsync();
        toolOpenFile.Click += async (_, _) => await SelectInputFileAsync();
        menuOpenFile.Click += async (_, _) => await SelectInputFileAsync();

        btnOpenFolder.Click += async (_, _) => await SelectInputFolderAsync();
        toolOpenFolder.Click += async (_, _) => await SelectInputFolderAsync();
        menuOpenFolder.Click += async (_, _) => await SelectInputFolderAsync();

        btnOpenSample.Click += async (_, _) => await LoadSampleAsync();
        toolOpenSample.Click += async (_, _) => await LoadSampleAsync();
        menuOpenSample.Click += async (_, _) => await LoadSampleAsync();

        btnOutputBrowse.Click += (_, _) => SelectOutputFolder();
        menuSelectOutput.Click += (_, _) => SelectOutputFolder();

        btnOpenSqliteFolder.Click += (_, _) => OpenSqliteDatabaseFolder();
        btnOptimizeSqlite.Click += async (_, _) => await OptimizeSqliteDatabaseAsync();

        btnStart.Click += async (_, _) => await ParseSelectedAsync();
        toolStart.Click += async (_, _) => await ParseSelectedAsync();
        menuStart.Click += async (_, _) => await ParseSelectedAsync();

        btnCancel.Click += (_, _) => CancelCurrentOperation();
        toolCancel.Click += (_, _) => CancelCurrentOperation();
        menuCancel.Click += (_, _) => CancelCurrentOperation();

        toolSave.Click += async (_, _) => await SaveCurrentResultsAsync();
        menuSaveResults.Click += async (_, _) => await SaveCurrentResultsAsync();

        toolExport.Click += async (_, _) => await ExportCurrentGridAsync();
        menuExportCsv.Click += async (_, _) => await ExportCurrentGridAsync();
        btnExportCurrentCsv.Click += async (_, _) => await ExportCurrentGridAsync();

        toolOpenOutput.Click += (_, _) => OpenOutputFolder();
        menuExit.Click += (_, _) => Close();
        menuUsage.Click += (_, _) => ShowUsage();
        menuAbout.Click += (_, _) => ShowAbout();

        btnCheckAll.Click += (_, _) => SetAllDocumentChecks(true);
        btnUncheckAll.Click += (_, _) => SetAllDocumentChecks(false);
        listDocuments.ItemChecked += (_, _) =>
        {
            if (_isPopulatingDocuments || IsDisposed) return;
            BeginInvoke(new Action(() =>
            {
                UpdateDocumentCount();
                UpdateCommandStates();
            }));
        };
        listDocuments.SelectedIndexChanged += async (_, _) => await LoadSelectedRawPreviewAsync();
        listDocuments.DoubleClick += (_, _) => tabResults.SelectedTab = tabRawPreview;

        btnGridSearch.Click += (_, _) => ApplyGridSearch();
        btnGridSearchClear.Click += (_, _) => ClearGridSearch();
        txtGridSearch.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode != Keys.Enter) return;
            eventArgs.SuppressKeyPress = true;
            ApplyGridSearch();
        };
        tabResults.SelectedIndexChanged += async (_, _) =>
        {
            if (_suppressViewRefresh) return;
            _rawPageIndex = 0;
            ClearGridSearch();
            await RefreshCurrentViewAsync();
            UpdateCommandStates();
        };

        btnClearLog.Click += (_, _) => txtLog.Clear();
        DragEnter += MainForm_DragEnter;
        DragDrop += MainForm_DragDrop;
        FormClosing += MainForm_FormClosing;
    }


    private void UpdateSqliteDatabaseInfo()
    {
        if (txtSqliteDatabasePath.IsDisposed) return;
        txtSqliteDatabasePath.Text = _databaseCache.DatabasePath;
        var file = new FileInfo(_databaseCache.DatabasePath);
        var wal = new FileInfo(_databaseCache.DatabasePath + "-wal");
        var totalBytes = (file.Exists ? file.Length : 0L) + (wal.Exists ? wal.Length : 0L);
        lblSqliteDatabaseSize.Text = totalBytes > 0
            ? $"{FormatFileSize(totalBytes)} (DB + WAL)"
            : "아직 생성되지 않음";
        lblSqliteDatabaseStatus.Text = _databaseLoaded
            ? $"준비 완료 · {_databaseGameCount:N0}경기 · 관계형 조회 전용"
            : "초기화 대기";
    }

    private void OpenSqliteDatabaseFolder()
    {
        try
        {
            var directory = Path.GetDirectoryName(_databaseCache.DatabasePath);
            if (string.IsNullOrWhiteSpace(directory)) return;
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo
            {
                FileName = directory,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "DB 폴더 열기 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task OptimizeSqliteDatabaseAsync()
    {
        if (_isBusy) return;
        var result = MessageBox.Show(
            this,
            "ANALYZE, PRAGMA optimize, WAL 정리와 VACUUM을 실행합니다.\r\n" +
            "DB가 크면 몇 분 걸릴 수 있고 작업 중에는 프로그램을 종료하면 안 됩니다. 계속할까요?",
            "SQLite DB 최적화",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (result != DialogResult.Yes) return;

        BeginOperation("SQLite DB 최적화 중", trackElapsed: true);
        try
        {
            statusLabel.Text = "SQLite DB를 최적화하는 중입니다...";
            lblProgressDetail.Text = "ANALYZE / optimize / WAL checkpoint / VACUUM";
            await _databaseCache.OptimizeAsync(_operationCts!.Token);
            UpdateSqliteDatabaseInfo();
            statusLabel.Text = "SQLite DB 최적화 완료";
            AppendLog($"SQLite DB 최적화 완료: {_databaseCache.DatabasePath}");
        }
        catch (OperationCanceledException)
        {
            statusLabel.Text = "SQLite DB 최적화 취소";
        }
        catch (Exception ex)
        {
            statusLabel.Text = "SQLite DB 최적화 실패";
            AppendLog($"SQLite DB 최적화 실패: {ex.Message}");
            MessageBox.Show(this, ex.Message, "DB 최적화 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task SelectInputFileAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "네이버 릴레이 JSON 또는 ZIP 선택",
            Filter = "지원 파일 (*.json;*.zip)|*.json;*.zip|JSON 파일 (*.json)|*.json|ZIP 파일 (*.zip)|*.zip|모든 파일 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            await LoadInputPathAsync(dialog.FileName);
        }
    }

    private async Task SelectInputFolderAsync()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "JSON 또는 ZIP 파일이 들어 있는 폴더를 선택하세요.",
            ShowNewFolderButton = false,
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            await LoadInputPathAsync(dialog.SelectedPath);
        }
    }

    private async Task LoadSampleAsync()
    {
        var samplePath = FindSamplePath();
        if (samplePath == null)
        {
            MessageBox.Show(
                this,
                "SampleData\\2026.zip 파일을 찾을 수 없습니다.",
                "샘플 파일 없음",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        await LoadInputPathAsync(samplePath);
    }

    private string? FindSamplePath()
    {
        var direct = Path.Combine(AppContext.BaseDirectory, "SampleData", "2026.zip");
        if (File.Exists(direct))
        {
            return direct;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 6 && directory != null; depth++, directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "SampleData", "2026.zip");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private async Task LoadInputPathAsync(string inputPath)
    {
        if (_isBusy)
        {
            return;
        }

        BeginOperation("입력 자료 검색 중", trackElapsed: false);
        try
        {
            txtInputPath.Text = Path.GetFullPath(inputPath);
            statusLabel.Text = "입력 자료를 검색하고 있습니다...";
            AppendLog($"입력 경로 검색: {txtInputPath.Text}");

            IProgress<string> warningProgress = new Progress<string>(warning => AppendLog($"경고: {warning}"));
            var documents = await InputDiscoveryService.DiscoverAsync(
                inputPath,
                warning => warningProgress.Report(warning),
                _operationCts!.Token);

            if (_isClosing)
            {
                return;
            }

            _documents.Clear();
            _documents.AddRange(documents);
            // 입력 경로를 바꿔도 SQLite에서 이미 불러온 경기 데이터는 유지합니다.
            // 신규/변경 문서만 증분 파싱한 뒤 기존 목록에 합칩니다.
            _failures.Clear();
            PopulateDocumentList();
            if (_databaseGameCount == 0) ResetResultViews();

            txtOutputPath.Text = CreateDefaultOutputPath(inputPath);
            statusLabel.Text = documents.Count > 0
                ? $"파싱 대상 {documents.Count:N0}개 준비"
                : "지원되는 JSON 자료가 없습니다.";
            lblProgressDetail.Text = documents.Count > 0
                ? "체크된 파일을 확인한 뒤 '파싱 시작'을 누르세요."
                : "JSON 파일 또는 JSON이 포함된 ZIP을 선택해 주세요.";
            AppendLog($"검색 완료: JSON 문서 {documents.Count:N0}개");

            if (documents.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "선택한 경로에서 파싱할 JSON 문서를 찾지 못했습니다.",
                    "자료 없음",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
        catch (OperationCanceledException)
        {
            AppendLog("입력 자료 검색이 취소되었습니다.");
            statusLabel.Text = "검색 취소";
        }
        catch (Exception ex)
        {
            AppendLog($"입력 자료 검색 실패: {ex.Message}");
            statusLabel.Text = "입력 자료 검색 실패";
            MessageBox.Show(this, ex.Message, "입력 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            EndOperation();
        }
    }

    private void PopulateDocumentList()
    {
        _isPopulatingDocuments = true;
        listDocuments.BeginUpdate();
        try
        {
            listDocuments.Items.Clear();
            _documentItems.Clear();

            foreach (var document in _documents)
            {
                var source = document.Kind == InputDocumentKind.JsonFile
                    ? Path.GetDirectoryName(document.ContainerPath) ?? document.ContainerPath
                    : $"{Path.GetFileName(document.ContainerPath)} > {document.EntryName}";
                var item = new ListViewItem(document.DisplayName)
                {
                    Tag = document,
                    Checked = true,
                    ToolTipText = document.SourceDisplay,
                };
                item.SubItems.Add(source);
                item.SubItems.Add(FormatFileSize(document.Length));
                item.SubItems.Add("대기");
                listDocuments.Items.Add(item);
                _documentItems[document.Id] = item;
            }

            if (listDocuments.Items.Count > 0)
            {
                listDocuments.Items[0].Selected = true;
                listDocuments.Items[0].Focused = true;
            }
        }
        finally
        {
            listDocuments.EndUpdate();
            _isPopulatingDocuments = false;
        }

        UpdateDocumentCount();
    }

    private async Task ParseSelectedAsync()
    {
        if (_isBusy) return;

        var selectedDocuments = GetCheckedDocuments();
        if (selectedDocuments.Count == 0)
        {
            MessageBox.Show(this, "파싱할 파일을 하나 이상 체크해 주세요.", "선택 필요",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var saveAsYouGo = chkAutoSave.Checked;
        if (saveAsYouGo && string.IsNullOrWhiteSpace(txtOutputPath.Text))
        {
            SelectOutputFolder();
            if (string.IsNullOrWhiteSpace(txtOutputPath.Text)) return;
        }

        try
        {
            if (saveAsYouGo) Directory.CreateDirectory(txtOutputPath.Text);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "출력 폴더 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        await _databaseCache.InitializeAsync();
        var unchangedIds = await _databaseCache.GetUnchangedSourceKeysAsync(selectedDocuments);
        var documentsToParse = selectedDocuments.Where(document => !unchangedIds.Contains(document.Id)).ToList();

        _failures.Clear();
        ResetDocumentStatuses();
        foreach (var cachedDocument in selectedDocuments.Where(document => unchangedIds.Contains(document.Id)))
        {
            if (!_documentItems.TryGetValue(cachedDocument.Id, out var item)) continue;
            item.SubItems[3].Text = "캐시됨";
            item.ForeColor = Color.DimGray;
        }

        if (documentsToParse.Count == 0)
        {
            await ReloadDatabaseCatalogAsync(selectLatestYear: _databaseGameCount == 0, resetDates: false);
            await RefreshCurrentViewAsync(forceAnalytics: false);
            statusLabel.Text = $"모든 문서가 캐시에 있습니다. {_databaseGameCount:N0}경기 즉시 사용";
            lblProgressDetail.Text = $"DB: {_databaseCache.DatabasePath}";
            AppendLog($"증분 파싱: 신규/변경 문서 없음, {selectedDocuments.Count:N0}개 건너뜀");
            return;
        }

        BeginOperation("신규/변경 문서 파싱 중", trackElapsed: true);
        progressBar.Value = 0;
        AppendLog($"증분 파싱 시작: 신규/변경 {documentsToParse.Count:N0}개, 캐시 건너뜀 {unchangedIds.Count:N0}개");

        try
        {
            var progress = new Progress<WorkflowProgress>(OnWorkflowProgress);
            var result = await ParsingWorkflowService.RunAsync(
                documentsToParse,
                saveAsYouGo ? txtOutputPath.Text : null,
                saveAsYouGo,
                progress,
                _operationCts!.Token,
                _databaseCache);

            if (_isClosing) return;

            _failures.AddRange(result.Failures);
            foreach (var failure in _failures)
                AppendLog($"실패: {failure.SourceName} - {failure.Error}");

            // 7경기 샘플처럼 소규모 입력만 메모리에 남으므로 이 경우에만 즉시 검증합니다.
            if (KnownSampleValidationService.IsKnownSample(result.Games))
            {
                var validationFailures = KnownSampleValidationService.Validate(result.Games);
                AppendLog(validationFailures.Count == 0 ? "7경기 기준 샘플 검증: PASS" : "7경기 기준 샘플 검증: FAIL");
                foreach (var failure in validationFailures) AppendLog($"  - {failure}");
            }

            _leagueReference = null;
            InvalidateActiveAnalytics();
            InvalidatePlayerPageService();
            InvalidateTeamPageService();
            await ReloadDatabaseCatalogAsync(selectLatestYear: false, resetDates: false, cancellationToken: _operationCts.Token);
            UpdateSqliteDatabaseInfo();
            await RefreshCurrentViewAsync(forceAnalytics: true, externalToken: _operationCts.Token);
            progressBar.Value = 100;

            var allSummary = await _databaseCache.GetSummaryAsync(
                new GameQuery { Competition = "전체 경기" }, _operationCts.Token);
            var successMessage = $"완료: DB 전체 {allSummary.Games:N0}경기, 이번 파싱 {result.ParsedGameCount:N0}, " +
                                 $"캐시 건너뜀 {unchangedIds.Count:N0}, 실패 {_failures.Count:N0}, " +
                                 $"이번 타석 {result.CompletedPlateAppearanceCount:N0}, 이번 투구 {result.PitchCount:N0}";
            statusLabel.Text = successMessage;
            lblProgressDetail.Text = saveAsYouGo
                ? $"정규화 JSON 저장 위치: {txtOutputPath.Text} | DB: {_databaseCache.DatabasePath}"
                : $"SQLite DB에 자동 저장됨: {_databaseCache.DatabasePath}";
            AppendLog(successMessage);
        }
        catch (OperationCanceledException)
        {
            statusLabel.Text = "파싱 취소";
            lblProgressDetail.Text = "사용자가 작업을 취소했습니다.";
            AppendLog("파싱 작업이 취소되었습니다.");
        }
        catch (Exception ex)
        {
            statusLabel.Text = "파싱 실패";
            lblProgressDetail.Text = ex.Message;
            AppendLog($"파싱 작업 실패: {ex}");
            MessageBox.Show(this, ex.Message, "파싱 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            EndOperation();
        }
    }


    private void OnWorkflowProgress(WorkflowProgress progress)
    {
        if (_isClosing || IsDisposed)
        {
            return;
        }

        var stageFraction = progress.Stage switch
        {
            WorkflowStage.Reading => 0.10,
            WorkflowStage.Parsing => 0.55,
            WorkflowStage.Saving => 0.85,
            WorkflowStage.Completed => 1.00,
            WorkflowStage.Failed => 1.00,
            _ => 0.00,
        };
        var percent = progress.TotalCount <= 0
            ? 0
            : (int)Math.Round(((progress.CurrentIndex - 1 + stageFraction) / progress.TotalCount) * 100);
        progressBar.Value = Math.Clamp(percent, 0, 100);
        lblCurrentFile.Text = $"현재 파일: [{progress.CurrentIndex:N0}/{progress.TotalCount:N0}] {progress.DocumentName}";
        lblProgressDetail.Text = progress.Message ?? progress.Stage.ToString();
        statusLabel.Text = progress.Message ?? progress.Stage.ToString();

        var statusText = progress.Stage switch
        {
            WorkflowStage.Reading => "읽는 중",
            WorkflowStage.Parsing => "파싱 중",
            WorkflowStage.Saving => "저장 중",
            WorkflowStage.Completed => "완료",
            WorkflowStage.Failed => "실패",
            _ => "대기",
        };
        SetDocumentStatus(progress.DocumentId, statusText);

        AppendLog($"[{progress.CurrentIndex}/{progress.TotalCount}] {progress.DocumentName}: {progress.Message}");
    }

    private void BindResultViews() => _ = RefreshCurrentViewAsync();

    private async Task ReloadDatabaseCatalogAsync(
        bool selectLatestYear,
        bool resetDates,
        CancellationToken cancellationToken = default)
    {
        _databaseCatalog = await _databaseCache.GetCatalogAsync(cancellationToken);
        _databaseGameCount = _databaseCatalog.GameCount;

        var previousYear = cboYearFilter.SelectedItem?.ToString();
        _suppressViewRefresh = true;
        _isUpdatingYearFilter = true;
        try
        {
            cboYearFilter.BeginUpdate();
            cboYearFilter.Items.Clear();
            cboYearFilter.Items.Add("전체 연도");
            foreach (var year in _databaseCatalog.Years) cboYearFilter.Items.Add(year.ToString());

            var previousIndex = !string.IsNullOrWhiteSpace(previousYear)
                ? cboYearFilter.Items.IndexOf(previousYear)
                : -1;
            if (selectLatestYear && _databaseCatalog.Years.Count > 0 &&
                (string.IsNullOrWhiteSpace(previousYear) || previousYear == "전체 연도"))
                cboYearFilter.SelectedIndex = 1;
            else if (previousIndex >= 0)
                cboYearFilter.SelectedIndex = previousIndex;
            else
                cboYearFilter.SelectedIndex = 0;
        }
        finally
        {
            cboYearFilter.EndUpdate();
            _isUpdatingYearFilter = false;
            _suppressViewRefresh = false;
        }

        await ReloadDatabaseFilterOptionsAsync(resetDates, cancellationToken);
        UpdateCommandStates();
    }

    private async Task ReloadDatabaseFilterOptionsAsync(
        bool resetDates,
        CancellationToken cancellationToken = default)
    {
        if (_databaseGameCount <= 0) return;
        var query = new GameQuery
        {
            SeasonYear = int.TryParse(cboYearFilter.SelectedItem?.ToString(), out var year) ? year : null,
            Competition = cboCompetitionFilter.SelectedItem?.ToString() ?? "정규시즌",
        };
        _databaseFilterOptions = await _databaseCache.GetFilterOptionsAsync(query, cancellationToken);

        var previousTeam = cboTeamFilter.SelectedItem?.ToString() ?? "전체 팀";
        var previousStadium = cboStadiumFilter.SelectedItem?.ToString() ?? "전체 구장";
        _suppressViewRefresh = true;
        _isUpdatingDimensionFilters = true;
        _isUpdatingPeriodFilters = true;
        try
        {
            cboTeamFilter.BeginUpdate();
            cboTeamFilter.Items.Clear();
            cboTeamFilter.Items.Add("전체 팀");
            foreach (var team in _databaseFilterOptions.Teams) cboTeamFilter.Items.Add(team);
            cboTeamFilter.SelectedIndex = Math.Max(0, cboTeamFilter.Items.IndexOf(previousTeam));
            cboTeamFilter.EndUpdate();

            PopulateOpponentItems();

            cboStadiumFilter.BeginUpdate();
            cboStadiumFilter.Items.Clear();
            cboStadiumFilter.Items.Add("전체 구장");
            foreach (var stadium in _databaseFilterOptions.Stadiums) cboStadiumFilter.Items.Add(stadium);
            cboStadiumFilter.SelectedIndex = Math.Max(0, cboStadiumFilter.Items.IndexOf(previousStadium));
            cboStadiumFilter.EndUpdate();

            if (resetDates)
            {
                var min = _databaseFilterOptions.MinGameDate ?? _databaseCatalog?.MinGameDate;
                var max = _databaseFilterOptions.MaxGameDate ?? _databaseCatalog?.MaxGameDate;
                if (min.HasValue) SetDatePickerValue(dtStartFilter, min.Value);
                if (max.HasValue) SetDatePickerValue(dtEndFilter, max.Value);
            }
        }
        finally
        {
            _isUpdatingPeriodFilters = false;
            _isUpdatingDimensionFilters = false;
            _suppressViewRefresh = false;
        }
    }

    private void PopulateOpponentItems()
    {
        var previous = cboOpponentFilter.SelectedItem?.ToString() ?? "전체 상대";
        var selectedTeam = cboTeamFilter.SelectedItem?.ToString() ?? "전체 팀";
        var teams = _databaseFilterOptions?.Teams ?? _databaseCatalog?.Teams ?? Array.Empty<string>();
        cboOpponentFilter.BeginUpdate();
        cboOpponentFilter.Items.Clear();
        cboOpponentFilter.Items.Add("전체 상대");
        foreach (var team in teams)
        {
            if (selectedTeam != "전체 팀" && string.Equals(team, selectedTeam, StringComparison.Ordinal)) continue;
            cboOpponentFilter.Items.Add(team);
        }
        cboOpponentFilter.SelectedIndex = Math.Max(0, cboOpponentFilter.Items.IndexOf(previous));
        cboOpponentFilter.EndUpdate();
    }

    private static void SetDatePickerValue(DateTimePicker picker, DateTime value) =>
        picker.Value = value < picker.MinDate ? picker.MinDate : value > picker.MaxDate ? picker.MaxDate : value;

    private GameQuery BuildCurrentQuery()
    {
        var period = cboPeriodFilter.SelectedItem?.ToString() ?? "전체 기간";
        var selectedYear = int.TryParse(cboYearFilter.SelectedItem?.ToString(), out var year) ? year : (int?)null;
        var team = cboTeamFilter.SelectedItem?.ToString();
        var opponent = cboOpponentFilter.SelectedItem?.ToString();
        var venue = cboVenueFilter.SelectedItem?.ToString();
        var stadium = cboStadiumFilter.SelectedItem?.ToString();
        var weekday = cboWeekdayFilter.SelectedItem?.ToString();
        DateTime? start = null;
        DateTime? end = null;
        int? recentGames = null;

        var maxDate = _databaseFilterOptions?.MaxGameDate ?? _databaseCatalog?.MaxGameDate;
        if (period.StartsWith("최근 ", StringComparison.Ordinal) && period.EndsWith("경기", StringComparison.Ordinal))
        {
            var digits = new string(period.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var count) && count > 0) recentGames = count;
        }
        else if (period == "직접 지정")
        {
            start = dtStartFilter.Value.Date;
            end = dtEndFilter.Value.Date;
        }
        else if (maxDate.HasValue)
        {
            var last = maxDate.Value.Date;
            switch (period)
            {
                case "최근 7일": start = last.AddDays(-6); end = last; break;
                case "최근 14일": start = last.AddDays(-13); end = last; break;
                case "최근 30일": start = last.AddDays(-29); end = last; break;
                case "최근 60일": start = last.AddDays(-59); end = last; break;
                case "최근 90일": start = last.AddDays(-89); end = last; break;
                case "전반기":
                    var firstYear = selectedYear ?? last.Year;
                    start = new DateTime(firstYear, 1, 1);
                    end = new DateTime(firstYear, 7, 15);
                    break;
                case "후반기":
                    var secondYear = selectedYear ?? last.Year;
                    start = new DateTime(secondYear, 7, 16);
                    end = new DateTime(secondYear, 12, 31);
                    break;
            }
        }
        if (start > end) (start, end) = (end, start);

        return new GameQuery
        {
            SeasonYear = selectedYear,
            Competition = cboCompetitionFilter.SelectedItem?.ToString() ?? "정규시즌",
            TeamCode = string.IsNullOrWhiteSpace(team) || team == "전체 팀" ? null : team,
            OpponentCode = string.IsNullOrWhiteSpace(opponent) || opponent == "전체 상대" ? null : opponent,
            Venue = string.IsNullOrWhiteSpace(team) || team == "전체 팀" ||
                    string.IsNullOrWhiteSpace(venue) || venue == "전체 장소" ? null : venue,
            Stadium = string.IsNullOrWhiteSpace(stadium) || stadium == "전체 구장" ? null : stadium,
            Weekday = string.IsNullOrWhiteSpace(weekday) || weekday == "전체 요일" ? null : weekday,
            StartDate = start,
            EndDate = end,
            RecentGameCount = recentGames,
        };
    }

    private void InvalidateActiveAnalytics()
    {
        _activeAnalytics = null;
        _activeAnalyticsKey = null;
    }

    private async Task RefreshCurrentViewAsync(
        bool forceAnalytics = false,
        CancellationToken externalToken = default)
    {
        if (!_databaseLoaded || _databaseGameCount <= 0 || _isClosing) return;

        _viewLoadCts?.Cancel();
        _viewLoadCts?.Dispose();
        _viewLoadCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        var cancellationToken = _viewLoadCts.Token;
        var query = BuildCurrentQuery();
        if (forceAnalytics) InvalidateActiveAnalytics();

        try
        {
            Cursor = Cursors.WaitCursor;
            statusLabel.Text = "SQLite에서 현재 화면을 조회하는 중입니다...";
            var summary = await _databaseCache.GetSummaryAsync(query, cancellationToken);
            UpdateSummaryCards(summary);

            if (tabResults.SelectedTab == tabGames)
            {
                var headers = await _databaseCache.GetGameHeadersAsync(query, cancellationToken);
                SetGridData(gridGames, headers.Select(ToGameGridRow).ToList());
                HideRawPager();
            }
            else if (tabResults.SelectedTab == tabLeagueConstants)
            {
                var league = await GetLeagueReferenceAsync(cancellationToken);
                SetGridData(gridLeagueConstants, league.Constants);
                HideRawPager();
            }
            else if (tabResults.SelectedTab == _tabParkFactors)
            {
                var league = await GetLeagueReferenceAsync(cancellationToken);
                SetGridData(gridParkFactors, league.ParkFactors);
                HideRawPager();
            }
            else if (IsAnalyticsTab(tabResults.SelectedTab))
            {
                var analytics = await GetAnalyticsSnapshotAsync(query, cancellationToken);
                BindSelectedAnalyticsTab(analytics);
                HideRawPager();
            }
            else if (TryGetRawDataKind(tabResults.SelectedTab, out var kind))
            {
                await LoadRawPageAsync(query, kind, cancellationToken);
            }
            else
            {
                HideRawPager();
            }

            statusLabel.Text = $"조회 완료: {summary.Games:N0}경기";
            lblProgressDetail.Text = $"JSON 역직렬화 없이 관계형 SQLite 조회 | DB: {_databaseCache.DatabasePath}";
            ClearGridSearch();
            UpdateCommandStates();
        }
        catch (OperationCanceledException)
        {
            // 더 최신 필터/탭 요청이 시작되면 이전 조회는 조용히 취소합니다.
        }
        catch (Exception ex)
        {
            AppendLog($"DB 화면 조회 실패: {ex.Message}");
            statusLabel.Text = "DB 화면 조회 실패";
        }
        finally
        {
            if (!_isBusy) Cursor = Cursors.Default;
        }
    }

    private async Task<LeagueReference> GetLeagueReferenceAsync(CancellationToken cancellationToken)
    {
        if (_leagueReference is not null) return _leagueReference;
        var progress = new Progress<DatabaseLoadProgress>(value =>
        {
            if (_isClosing) return;
            statusLabel.Text = "전체 kbo_r 리그 상수 계산 중";
            lblProgressDetail.Text = value.Message;
        });
        _leagueReference = await _databaseCache.GetLeagueReferenceAsync(progress, cancellationToken);
        return _leagueReference;
    }

    private async Task<AnalyticsSnapshot> GetAnalyticsSnapshotAsync(GameQuery query, CancellationToken cancellationToken)
    {
        if (_activeAnalytics is not null && string.Equals(_activeAnalyticsKey, query.CacheKey, StringComparison.Ordinal))
            return _activeAnalytics;

        var league = await GetLeagueReferenceAsync(cancellationToken);
        var progress = new Progress<DatabaseLoadProgress>(value =>
        {
            if (_isClosing) return;
            statusLabel.Text = "선택 범위 통계 계산 중";
            lblProgressDetail.Text = value.Message;
        });
        _activeAnalytics = await _databaseAnalytics.GetSnapshotAsync(query, league, progress, cancellationToken);
        _activeAnalyticsKey = query.CacheKey;
        return _activeAnalytics;
    }

    private static bool IsAnalyticsTab(TabPage? tab) => tab is not null &&
        (tab.Text is "타자 클래식" or "투수 클래식" or "타자 세이버" or "투수 세이버" or
         "타자 선구·컨택" or "투수 존·컨택" or "타자 Value·WAR" or "투수 Value·WAR");

    private void BindSelectedAnalyticsTab(AnalyticsSnapshot snapshot)
    {
        var context = new QualificationContext(snapshot.TeamGames, snapshot.BatterPa, snapshot.PitcherIp, snapshot.PrimaryPositions);
        if (tabResults.SelectedTab == tabBatterStats)
            SetGridData(gridBatterStats, ApplyBatterFilters(snapshot.BatterClassic, x => x.TeamCode, x => x.Pcode, x => x.PA, context));
        else if (tabResults.SelectedTab == tabPitcherStats)
            SetGridData(gridPitcherStats, ApplyPitcherFilters(snapshot.PitcherClassic, x => x.TeamCode, x => x.Pcode, context));
        else if (tabResults.SelectedTab == tabBatterSabermetrics)
            SetGridData(gridBatterSabermetrics, ApplyBatterFilters(snapshot.BatterSabermetrics, x => x.TeamCode, x => x.Pcode, x => x.PA, context));
        else if (tabResults.SelectedTab == tabPitcherSabermetrics)
            SetGridData(gridPitcherSabermetrics, ApplyPitcherFilters(snapshot.PitcherSabermetrics, x => x.TeamCode, x => x.Pcode, context));
        else if (tabResults.SelectedTab == tabBatterDiscipline)
            SetGridData(gridBatterDiscipline, ApplyBatterFilters(snapshot.BatterDiscipline, x => x.TeamCode, x => x.Pcode,
                x => snapshot.BatterPa.GetValueOrDefault(PlayerKey(x.Pcode, x.TeamCode)), context));
        else if (tabResults.SelectedTab == tabPitcherDiscipline)
            SetGridData(gridPitcherDiscipline, ApplyPitcherFilters(snapshot.PitcherDiscipline, x => x.TeamCode, x => x.Pcode, context));
        else if (tabResults.SelectedTab == tabBatterValue)
            SetGridData(gridBatterValue, ApplyBatterFilters(snapshot.BatterValues, x => x.TeamCode, x => x.Pcode, x => x.PA, context));
        else if (tabResults.SelectedTab == tabPitcherValue)
            SetGridData(gridPitcherValue, ApplyPitcherFilters(snapshot.PitcherValues, x => x.TeamCode, x => x.Pcode, context));
    }

    private static bool TryGetRawDataKind(TabPage? tab, out RawDataKind kind)
    {
        kind = RawDataKind.PlateAppearances;
        if (tab is null) return false;
        kind = tab.Text switch
        {
            "타석" => RawDataKind.PlateAppearances,
            "투구" => RawDataKind.Pitches,
            "주루" => RawDataKind.RunnerEvents,
            "선수 교체" => RawDataKind.PlayerChanges,
            "관리 이벤트" => RawDataKind.AdministrativeEvents,
            "진단" => RawDataKind.Diagnostics,
            _ => kind,
        };
        return tab.Text is "타석" or "투구" or "주루" or "선수 교체" or "관리 이벤트" or "진단";
    }

    private async Task LoadRawPageAsync(GameQuery query, RawDataKind kind, CancellationToken cancellationToken)
    {
        switch (kind)
        {
            case RawDataKind.PlateAppearances:
            {
                var page = await _databaseCache.GetPlateAppearancePageAsync(query, _rawPageIndex, RawPageSize, cancellationToken);
                _rawPageIndex = page.PageIndex;
                SetGridData(gridPlateAppearances, page.Items);
                ShowRawPager(page.TotalCount, page.PageIndex, page.PageCount);
                break;
            }
            case RawDataKind.Pitches:
            {
                var page = await _databaseCache.GetPitchPageAsync(query, _rawPageIndex, RawPageSize, cancellationToken);
                _rawPageIndex = page.PageIndex;
                SetGridData(gridPitches, page.Items);
                ShowRawPager(page.TotalCount, page.PageIndex, page.PageCount);
                break;
            }
            case RawDataKind.RunnerEvents:
            {
                var page = await _databaseCache.GetRunnerPageAsync(query, _rawPageIndex, RawPageSize, cancellationToken);
                _rawPageIndex = page.PageIndex;
                SetGridData(gridRunners, page.Items);
                ShowRawPager(page.TotalCount, page.PageIndex, page.PageCount);
                break;
            }
            case RawDataKind.PlayerChanges:
            {
                var page = await _databaseCache.GetPlayerChangePageAsync(query, _rawPageIndex, RawPageSize, cancellationToken);
                _rawPageIndex = page.PageIndex;
                SetGridData(gridPlayerChanges, page.Items);
                ShowRawPager(page.TotalCount, page.PageIndex, page.PageCount);
                break;
            }
            case RawDataKind.AdministrativeEvents:
            {
                var page = await _databaseCache.GetAdministrativePageAsync(query, _rawPageIndex, RawPageSize, cancellationToken);
                _rawPageIndex = page.PageIndex;
                SetGridData(gridAdministrative, page.Items);
                ShowRawPager(page.TotalCount, page.PageIndex, page.PageCount);
                break;
            }
            case RawDataKind.Diagnostics:
            {
                var page = await _databaseCache.GetDiagnosticPageAsync(query, _rawPageIndex, RawPageSize, cancellationToken);
                _rawPageIndex = page.PageIndex;
                var rows = page.Items.ToList();
                if (page.PageIndex == 0)
                {
                    rows.AddRange(_failures.Select(failure => new DiagnosticGridRow
                    {
                        Severity = "오류", Code = "SOURCE_PARSE_FAILURE", Message = failure.Error,
                        GameId = null, EventId = failure.SourceName,
                    }));
                }
                SetGridData(gridDiagnostics, rows);
                ShowRawPager(page.TotalCount + (_rawPageIndex == 0 ? _failures.Count : 0), page.PageIndex, page.PageCount);
                break;
            }
        }
    }

    private void ShowRawPager(long totalRows, int pageIndex, int pageCount)
    {
        _btnRawPrevious.Visible = _btnRawNext.Visible = _lblRawPage.Visible = true;
        _btnRawPrevious.Enabled = pageIndex > 0;
        _btnRawNext.Enabled = pageIndex + 1 < pageCount;
        _lblRawPage.Text = $"{pageIndex + 1:N0} / {pageCount:N0} 페이지\n전체 {totalRows:N0}건";
    }

    private void HideRawPager()
    {
        _btnRawPrevious.Visible = _btnRawNext.Visible = _lblRawPage.Visible = false;
    }

    private static GameGridRow ToGameGridRow(DatabaseGameHeader game) => new()
    {
        GameId = game.GameId,
        SeasonYear = game.SeasonYear,
        CompetitionType = string.Equals(game.RoundCode?.Trim(), "kbo_r", StringComparison.OrdinalIgnoreCase)
            ? "정규시즌"
            : game.CompetitionType switch
            {
                GameCompetitionType.Preseason => "시범경기",
                GameCompetitionType.Postseason => "포스트시즌",
                GameCompetitionType.AllStar => "올스타전",
                GameCompetitionType.Futures => "퓨처스리그",
                _ => "기타",
            },
        RoundCode = game.RoundCode,
        IsRegularSeason = string.Equals(game.RoundCode?.Trim(), "kbo_r", StringComparison.OrdinalIgnoreCase) ? "예" : "아니오",
        GameDate = game.GameDate,
        AwayTeam = game.AwayTeamName ?? game.AwayTeamCode,
        AwayScore = game.AwayScore,
        HomeScore = game.HomeScore,
        HomeTeam = game.HomeTeamName ?? game.HomeTeamCode,
        Stadium = game.Stadium,
        PlateAppearances = game.PlateAppearances,
        Pitches = game.Pitches,
        MissingPts = 0,
        RunnerEvents = game.RunnerEvents,
        PlayerChanges = game.PlayerChanges,
        Warnings = game.Warnings,
        Errors = game.Errors,
    };

    private void UpdateSummaryCards(DatabaseSummary summary)
    {
        lblGameCountValue.Text = summary.Games.ToString("N0");
        lblPaCountValue.Text = summary.PlateAppearances.ToString("N0");
        lblPitchCountValue.Text = summary.Pitches.ToString("N0");
        lblRunnerCountValue.Text = summary.RunnerEvents.ToString("N0");
        lblChangeCountValue.Text = summary.PlayerChanges.ToString("N0");
        lblWarningCountValue.Text = summary.Warnings.ToString("N0");
        lblErrorCountValue.Text = (summary.Errors + _failures.Count).ToString("N0");
    }


    private sealed record QualificationContext(
        IReadOnlyDictionary<string, int> TeamGames,
        IReadOnlyDictionary<string, int> BatterPa,
        IReadOnlyDictionary<string, double> PitcherIp,
        IReadOnlyDictionary<string, string> PrimaryPositions);

    private IReadOnlyList<T> ApplyBatterFilters<T>(IEnumerable<T> rows, Func<T, string?> team, Func<T, string?> pcode, Func<T, int> pa, QualificationContext context)
    {
        var selectedTeam = cboTeamFilter.SelectedItem?.ToString() ?? "전체 팀";
        var selectedPosition = cboPositionFilter.SelectedItem?.ToString() ?? "전체 포지션";
        var percentage = SelectedPercentage(cboPaQualificationFilter);

        return rows.Where(row =>
        {
            var rowTeam = team(row);
            if (selectedTeam != "전체 팀" && !string.Equals(rowTeam, selectedTeam, StringComparison.Ordinal)) return false;
            if (selectedPosition != "전체 포지션" &&
                !string.Equals(context.PrimaryPositions.GetValueOrDefault(PlayerKey(pcode(row), rowTeam), "-"), selectedPosition, StringComparison.OrdinalIgnoreCase)) return false;
            if (percentage > 0)
            {
                var games = rowTeam is not null ? context.TeamGames.GetValueOrDefault(rowTeam) : 0;
                var required = games * 3.1 * percentage / 100.0;
                if (pa(row) + 1e-9 < required) return false;
            }
            return true;
        }).ToList();
    }

    private IReadOnlyList<T> ApplyPitcherFilters<T>(IEnumerable<T> rows, Func<T, string?> team, Func<T, string?> pcode, QualificationContext context)
    {
        var selectedTeam = cboTeamFilter.SelectedItem?.ToString() ?? "전체 팀";
        var percentage = SelectedPercentage(cboIpQualificationFilter);
        return rows.Where(row =>
        {
            var rowTeam = team(row);
            if (selectedTeam != "전체 팀" && !string.Equals(rowTeam, selectedTeam, StringComparison.Ordinal)) return false;
            if (percentage > 0)
            {
                var games = rowTeam is not null ? context.TeamGames.GetValueOrDefault(rowTeam) : 0;
                var required = games * percentage / 100.0;
                var ip = context.PitcherIp.GetValueOrDefault(PlayerKey(pcode(row), rowTeam));
                if (ip + 1e-9 < required) return false;
            }
            return true;
        }).ToList();
    }

    private static int SelectedPercentage(ComboBox combo)
    {
        var text = combo.SelectedItem?.ToString();
        return text is not null && text.EndsWith('%') && int.TryParse(text.TrimEnd('%'), out var value) ? value : 0;
    }

    private static string PlayerKey(string? pcode, string? teamCode) => $"{pcode ?? ""}|{teamCode ?? ""}";

    private static void SetGridData<T>(DataGridView grid, IReadOnlyList<T> rows)
    {
        grid.DataSource = null;
        grid.DataSource = new SortableBindingList<T>(rows);

        // 자동 생성 열도 머리글 클릭 정렬이 가능하도록 명시합니다.
        foreach (DataGridViewColumn column in grid.Columns)
        {
            column.SortMode = DataGridViewColumnSortMode.Automatic;
        }
    }

    private void ResetResultViews()
    {
        foreach (var grid in GetAllGrids())
        {
            grid.DataSource = null;
        }

        HideRawPager();
        UpdateSummaryCards();
        txtGridSearch.Clear();
        UpdateCurrentGridCount();
    }

    private void UpdateSummaryCards()
    {
        lblGameCountValue.Text = "0";
        lblPaCountValue.Text = "0";
        lblPitchCountValue.Text = "0";
        lblRunnerCountValue.Text = "0";
        lblChangeCountValue.Text = "0";
        lblWarningCountValue.Text = "0";
        lblErrorCountValue.Text = _failures.Count.ToString("N0");
    }

    private void FormatGrid(DataGridView grid)
    {
        GridNumberFormatter.Apply(grid);

        foreach (DataGridViewColumn column in grid.Columns)
        {
            column.SortMode = DataGridViewColumnSortMode.Automatic;
            var property = column.DataPropertyName;
            if (property is "RawText" or "ResultText" or "Message" or "EventId")
            {
                column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                column.Width = property == "Message" ? 420 : 320;
            }
        }
    }

    private void ApplyGridSearch()
    {
        var grid = GetCurrentGrid();
        if (grid == null)
        {
            return;
        }

        var search = txtGridSearch.Text.Trim();
        grid.CurrentCell = null;
        grid.SuspendLayout();
        try
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                row.Visible = string.IsNullOrWhiteSpace(search)
                              || row.Cells.Cast<DataGridViewCell>().Any(cell =>
                                  (cell.FormattedValue?.ToString() ?? string.Empty)
                                  .Contains(search, StringComparison.CurrentCultureIgnoreCase));
            }
        }
        finally
        {
            grid.ResumeLayout();
        }

        UpdateCurrentGridCount();
    }

    private void ClearGridSearch()
    {
        txtGridSearch.Clear();
        var grid = GetCurrentGrid();
        if (grid != null)
        {
            grid.CurrentCell = null;
            foreach (DataGridViewRow row in grid.Rows)
            {
                row.Visible = true;
            }
        }

        UpdateCurrentGridCount();
    }

    private void UpdateCurrentGridCount()
    {
        var grid = GetCurrentGrid();
        if (grid == null)
        {
            lblGridCount.Text = "표 없음";
            return;
        }

        var total = grid.Rows.Count;
        var visible = grid.Rows.Cast<DataGridViewRow>().Count(row => row.Visible);
        lblGridCount.Text = visible == total
            ? $"표시 {total:N0}건"
            : $"표시 {visible:N0} / 전체 {total:N0}건";
    }

    private DataGridView? GetCurrentGrid()
    {
        if (tabResults.SelectedTab == tabGames) return gridGames;
        if (tabResults.SelectedTab == tabBatterStats) return gridBatterStats;
        if (tabResults.SelectedTab == tabPitcherStats) return gridPitcherStats;
        if (tabResults.SelectedTab == tabBatterSabermetrics) return gridBatterSabermetrics;
        if (tabResults.SelectedTab == tabPitcherSabermetrics) return gridPitcherSabermetrics;
        if (tabResults.SelectedTab == tabBatterDiscipline) return gridBatterDiscipline;
        if (tabResults.SelectedTab == tabPitcherDiscipline) return gridPitcherDiscipline;
        if (tabResults.SelectedTab == tabLeagueConstants) return gridLeagueConstants;
        if (tabResults.SelectedTab == _tabParkFactors) return gridParkFactors;
        if (tabResults.SelectedTab == tabBatterValue) return gridBatterValue;
        if (tabResults.SelectedTab == tabPitcherValue) return gridPitcherValue;
        if (tabResults.SelectedTab == tabPlateAppearances) return gridPlateAppearances;
        if (tabResults.SelectedTab == tabPitches) return gridPitches;
        if (tabResults.SelectedTab == tabRunners) return gridRunners;
        if (tabResults.SelectedTab == tabPlayerChanges) return gridPlayerChanges;
        if (tabResults.SelectedTab == tabAdministrative) return gridAdministrative;
        if (tabResults.SelectedTab == tabDiagnostics) return gridDiagnostics;
        return null;
    }

    private IEnumerable<DataGridView> GetAllGrids()
    {
        yield return gridGames;
        yield return gridBatterStats;
        yield return gridPitcherStats;
        yield return gridBatterSabermetrics;
        yield return gridPitcherSabermetrics;
        yield return gridBatterDiscipline;
        yield return gridPitcherDiscipline;
        yield return gridLeagueConstants;
        yield return gridParkFactors;
        yield return gridBatterValue;
        yield return gridPitcherValue;
        yield return gridPlateAppearances;
        yield return gridPitches;
        yield return gridRunners;
        yield return gridPlayerChanges;
        yield return gridAdministrative;
        yield return gridDiagnostics;
    }

    private async Task ExportCurrentGridAsync()
    {
        var grid = GetCurrentGrid();
        if (grid == null || grid.Rows.Count == 0)
        {
            MessageBox.Show(this, "현재 탭에는 내보낼 표가 없습니다.", "CSV", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "현재 표 CSV 내보내기",
            Filter = "CSV 파일 (*.csv)|*.csv",
            FileName = $"{SanitizeFileName(tabResults.SelectedTab?.Text ?? "result")}_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            AddExtension = true,
            DefaultExt = "csv",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            await CsvExporter.ExportAsync(grid, dialog.FileName);
            AppendLog($"CSV 저장 완료: {dialog.FileName}");
            statusLabel.Text = "CSV 저장 완료";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "CSV 저장 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task SaveCurrentResultsAsync()
    {
        if (_isBusy || _databaseGameCount == 0) return;

        using var dialog = new FolderBrowserDialog
        {
            Description = "현재 필터 범위의 정규화 JSON을 저장할 폴더를 선택하세요.",
            SelectedPath = Directory.Exists(txtOutputPath.Text) ? txtOutputPath.Text : string.Empty,
            ShowNewFolderButton = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        txtOutputPath.Text = dialog.SelectedPath;
        BeginOperation("결과 JSON 저장 중", trackElapsed: true);
        try
        {
            var query = BuildCurrentQuery();
            var summary = await _databaseCache.GetSummaryAsync(query, _operationCts!.Token);
            var lightweight = new List<LightweightParsedGameSummary>(Math.Max(0, summary.Games));
            var progress = new Progress<DatabaseLoadProgress>(value =>
            {
                var percent = value.Total <= 0 ? 0 : (int)Math.Round(value.Current * 100.0 / value.Total);
                progressBar.Value = Math.Clamp(percent, 0, 100);
                lblCurrentFile.Text = $"현재 작업: 필터 결과 JSON 저장 [{value.Current:N0}/{value.Total:N0}]";
                lblProgressDetail.Text = value.Message;
            });

            await _databaseCache.ForEachGameAsync(
                query,
                GameDataProjection.Full,
                async (game, token) =>
                {
                    await NormalizedOutputWriter.SaveGameAsync(game, dialog.SelectedPath, game.GameId, token);
                    lightweight.Add(new LightweightParsedGameSummary
                    {
                        GameId = game.GameId,
                        GameDate = game.GameDate,
                        AwayTeamCode = game.AwayTeam.TeamCode,
                        HomeTeamCode = game.HomeTeam.TeamCode,
                        Summary = game.Summary,
                    });
                },
                progress,
                _operationCts.Token);

            await NormalizedOutputWriter.SaveAggregateSummaryAsync(
                lightweight,
                _failures.Select(failure => (failure.SourceName, failure.Error)).ToList(),
                dialog.SelectedPath,
                _operationCts.Token);
            progressBar.Value = 100;
            AppendLog($"정규화 결과 저장 완료: {lightweight.Count:N0}경기, {dialog.SelectedPath}");
            statusLabel.Text = "결과 저장 완료";
            lblProgressDetail.Text = dialog.SelectedPath;
        }
        catch (OperationCanceledException)
        {
            AppendLog("결과 저장이 취소되었습니다.");
            statusLabel.Text = "저장 취소";
        }
        catch (Exception ex)
        {
            AppendLog($"결과 저장 실패: {ex.Message}");
            MessageBox.Show(this, ex.Message, "저장 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            EndOperation();
        }
    }


    private async Task LoadSelectedRawPreviewAsync()
    {
        if (listDocuments.SelectedItems.Count == 0
            || listDocuments.SelectedItems[0].Tag is not InputDocument document)
        {
            txtRawPreview.Clear();
            return;
        }

        var requestVersion = ++_previewVersion;
        txtRawPreview.Text = "JSON을 읽는 중입니다...";

        try
        {
            var rawJson = await document.ReadJsonAsync(CancellationToken.None);
            var preview = await Task.Run(() => PrettyPrintJson(rawJson));
            if (requestVersion != _previewVersion || _isClosing || IsDisposed)
            {
                return;
            }

            const int maximumPreviewLength = 3_000_000;
            if (preview.Length > maximumPreviewLength)
            {
                txtRawPreview.Text = preview[..maximumPreviewLength]
                                         + "\r\n\r\n--- 미리보기 크기 제한으로 이후 내용은 생략되었습니다. ---";
            }
            else
            {
                txtRawPreview.Text = preview;
            }
            txtRawPreview.SelectionStart = 0;
            txtRawPreview.ScrollToCaret();
        }
        catch (Exception ex)
        {
            if (requestVersion == _previewVersion && !_isClosing && !IsDisposed)
            {
                txtRawPreview.Text = $"원본 JSON을 읽지 못했습니다.\r\n\r\n{ex}";
            }
        }
    }

    private static string PrettyPrintJson(string rawJson)
    {
        try
        {
            using var jsonDocument = JsonDocument.Parse(rawJson);
            return JsonSerializer.Serialize(
                jsonDocument.RootElement,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                });
        }
        catch (JsonException)
        {
            return rawJson;
        }
    }

    private void SelectOutputFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "정규화 JSON 출력 폴더를 선택하세요.",
            SelectedPath = Directory.Exists(txtOutputPath.Text) ? txtOutputPath.Text : string.Empty,
            ShowNewFolderButton = true,
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            txtOutputPath.Text = dialog.SelectedPath;
            UpdateCommandStates();
        }
    }

    private void OpenOutputFolder()
    {
        var path = txtOutputPath.Text;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            MessageBox.Show(this, "출력 폴더가 아직 생성되지 않았습니다.", "출력 폴더", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "폴더 열기 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SetAllDocumentChecks(bool value)
    {
        _isPopulatingDocuments = true;
        try
        {
            foreach (ListViewItem item in listDocuments.Items)
            {
                item.Checked = value;
            }
        }
        finally
        {
            _isPopulatingDocuments = false;
        }

        UpdateDocumentCount();
        UpdateCommandStates();
    }

    private List<InputDocument> GetCheckedDocuments()
    {
        return listDocuments.CheckedItems
            .Cast<ListViewItem>()
            .Select(item => item.Tag)
            .OfType<InputDocument>()
            .ToList();
    }

    private void UpdateDocumentCount()
    {
        var checkedCount = listDocuments.CheckedItems.Count;
        lblDocumentCount.Text = $"전체 {listDocuments.Items.Count:N0}개 / 선택 {checkedCount:N0}개";
    }

    private void ResetDocumentStatuses()
    {
        foreach (ListViewItem item in listDocuments.Items)
        {
            if (item.SubItems.Count >= 4)
            {
                item.SubItems[3].Text = item.Checked ? "대기" : "제외";
            }
        }
    }

    private void SetDocumentStatus(string documentId, string status)
    {
        if (_documentItems.TryGetValue(documentId, out var item) && item.SubItems.Count >= 4)
        {
            item.SubItems[3].Text = status;
            item.EnsureVisible();
        }
    }

    private void BeginOperation(string status, bool trackElapsed)
    {
        _operationCts?.Dispose();
        _operationCts = new CancellationTokenSource();
        _isBusy = true;
        statusLabel.Text = status;

        if (trackElapsed)
        {
            _stopwatch.Restart();
            _elapsedTimer.Start();
        }
        else
        {
            _stopwatch.Reset();
            statusElapsed.Text = "경과 시간: 00:00:00";
        }

        UpdateCommandStates();
    }

    private void EndOperation()
    {
        _elapsedTimer.Stop();
        if (_stopwatch.IsRunning)
        {
            _stopwatch.Stop();
            statusElapsed.Text = $"경과 시간: {_stopwatch.Elapsed:hh\\:mm\\:ss}";
        }

        _operationCts?.Dispose();
        _operationCts = null;
        _isBusy = false;
        if (!_isClosing && !IsDisposed)
        {
            UpdateCommandStates();
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.F))
        {
            _toolPlayerSearch.Focus();
            _toolPlayerSearch.SelectAll();
            return true;
        }

        if (keyData == (Keys.Control | Keys.T))
        {
            OpenTeamPage();
            return true;
        }

        if (keyData == Keys.Escape)
        {
            CancelCurrentOperation();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void CancelCurrentOperation()
    {
        if (_isBusy && _operationCts is { IsCancellationRequested: false })
        {
            statusLabel.Text = "취소 요청 중...";
            AppendLog("사용자가 작업 취소를 요청했습니다.");
            _operationCts.Cancel();
            return;
        }

        if (_viewLoadCts is { IsCancellationRequested: false })
        {
            statusLabel.Text = "화면 조회 취소 요청 중...";
            AppendLog("사용자가 현재 화면 조회 취소를 요청했습니다.");
            _viewLoadCts.Cancel();
        }
    }

    private void UpdateCommandStates()
    {
        var hasCheckedDocuments = listDocuments.CheckedItems.Count > 0;
        var hasResults = _databaseGameCount > 0;
        var currentGrid = GetCurrentGrid();
        var hasCurrentGrid = currentGrid != null && currentGrid.Rows.Count > 0;

        _toolPlayerSearch.Enabled = !_isBusy && hasResults;
        _toolPlayerSearchButton.Enabled = !_isBusy && hasResults;
        _toolTeamPageButton.Enabled = !_isBusy && hasResults;
        btnOpenFile.Enabled = !_isBusy;
        btnOpenFolder.Enabled = !_isBusy;
        btnOpenSample.Enabled = !_isBusy;
        btnOutputBrowse.Enabled = !_isBusy;
        btnCheckAll.Enabled = !_isBusy && listDocuments.Items.Count > 0;
        btnUncheckAll.Enabled = !_isBusy && listDocuments.Items.Count > 0;
        listDocuments.Enabled = !_isBusy;
        chkAutoSave.Enabled = !_isBusy;
        btnOpenSqliteFolder.Enabled = !_isBusy;
        btnOptimizeSqlite.Enabled = !_isBusy && File.Exists(_databaseCache.DatabasePath);

        btnStart.Enabled = !_isBusy && hasCheckedDocuments;
        toolStart.Enabled = btnStart.Enabled;
        menuStart.Enabled = btnStart.Enabled;

        btnCancel.Enabled = _isBusy;
        toolCancel.Enabled = _isBusy;
        menuCancel.Enabled = _isBusy;

        toolOpenFile.Enabled = !_isBusy;
        toolOpenFolder.Enabled = !_isBusy;
        toolOpenSample.Enabled = !_isBusy;
        menuOpenFile.Enabled = !_isBusy;
        menuOpenFolder.Enabled = !_isBusy;
        menuOpenSample.Enabled = !_isBusy;
        menuSelectOutput.Enabled = !_isBusy;

        toolSave.Enabled = !_isBusy && hasResults;
        menuSaveResults.Enabled = toolSave.Enabled;
        toolExport.Enabled = !_isBusy && hasCurrentGrid;
        menuExportCsv.Enabled = toolExport.Enabled;
        btnExportCurrentCsv.Enabled = toolExport.Enabled;
        btnGridSearch.Enabled = !_isBusy && hasCurrentGrid;
        btnGridSearchClear.Enabled = !_isBusy && hasCurrentGrid;
        txtGridSearch.Enabled = !_isBusy && hasCurrentGrid;
        toolOpenOutput.Enabled = !_isBusy && Directory.Exists(txtOutputPath.Text);

        Cursor = _isBusy ? Cursors.WaitCursor : Cursors.Default;
    }

    private void AppendLog(string message)
    {
        if (_isClosing || IsDisposed)
        {
            return;
        }

        var line = $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
        txtLog.AppendText(line);
        txtLog.SelectionStart = txtLog.TextLength;
        txtLog.ScrollToCaret();
    }

    private string CreateDefaultOutputPath(string inputPath)
    {
        var baseDirectory = Directory.Exists(inputPath)
            ? Path.GetFullPath(inputPath)
            : Path.GetDirectoryName(Path.GetFullPath(inputPath))
              ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(baseDirectory, "normalized_output");
    }

    private static string FormatFileSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)Math.Max(0, bytes);
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
    }

    private void ShowUsage()
    {
        MessageBox.Show(
            this,
            "1. '파일 선택', '폴더 선택' 또는 '7경기 샘플'로 입력 자료를 불러옵니다.\r\n" +
            "2. 파싱할 JSON 문서를 체크합니다. ZIP 내부 JSON도 문서별로 표시됩니다.\r\n" +
            "3. 출력 폴더와 자동 저장 여부를 확인한 뒤 '파싱 시작'을 누릅니다.\r\n" +
            "4. 경기/타자/투수/타석/투구/주루/교체/진단 탭에서 결과를 확인합니다.\r\n" +
            "5. 선수 검색 또는 Ctrl+F로 선수 페이지를, 팀 페이지 또는 Ctrl+T로 팀 상세 창을 엽니다.\r\n" +
            "6. 현재 표는 CSV로, 전체 정규화 결과는 JSON으로 저장할 수 있습니다.\r\n\r\n" +
            "원본 목록을 더블클릭하면 '원본 JSON' 탭으로 이동합니다. 파일이나 폴더를 창에 끌어다 놓아도 됩니다.",
            "사용 방법",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void ShowAbout()
    {
        MessageBox.Show(
            this,
            "Naver Relay 세이버매트릭스 데이터 관리 도구\r\n" +
            ".NET 8 WinForms / V2 관계형 SQLite\r\n\r\n" +
            "네이버 경기 릴레이 JSON을 타석, 투구, 주루, 선수 교체, 관리 이벤트로 정규화하고 검증합니다.",
            "프로그램 정보",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void MainForm_DragEnter(object? sender, DragEventArgs eventArgs)
    {
        eventArgs.Effect = eventArgs.Data?.GetDataPresent(DataFormats.FileDrop) == true
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private async void MainForm_DragDrop(object? sender, DragEventArgs eventArgs)
    {
        if (_isBusy || eventArgs.Data?.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
        {
            return;
        }

        await LoadInputPathAsync(paths[0]);
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (_isBusy)
        {
            var answer = MessageBox.Show(
                this,
                "진행 중인 작업을 취소하고 프로그램을 종료할까요?",
                "종료 확인",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (answer != DialogResult.Yes)
            {
                eventArgs.Cancel = true;
                return;
            }

            _isClosing = true;
            _operationCts?.Cancel();
            _viewLoadCts?.Cancel();
        }
        else
        {
            _isClosing = true;
            _viewLoadCts?.Cancel();
        }
    }
}
