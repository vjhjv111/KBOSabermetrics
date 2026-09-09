namespace NaverRelay.Gui;

partial class MainForm
{
    private System.ComponentModel.IContainer? components = null;

    private MenuStrip menuStripMain = null!;
    private ToolStripMenuItem menuFile = null!;
    private ToolStripMenuItem menuOpenFile = null!;
    private ToolStripMenuItem menuOpenFolder = null!;
    private ToolStripMenuItem menuOpenSample = null!;
    private ToolStripMenuItem menuSelectOutput = null!;
    private ToolStripMenuItem menuExit = null!;
    private ToolStripMenuItem menuParse = null!;
    private ToolStripMenuItem menuStart = null!;
    private ToolStripMenuItem menuCancel = null!;
    private ToolStripMenuItem menuSaveResults = null!;
    private ToolStripMenuItem menuExportCsv = null!;
    private ToolStripMenuItem menuHelp = null!;
    private ToolStripMenuItem menuUsage = null!;
    private ToolStripMenuItem menuAbout = null!;

    private ToolStrip toolStripMain = null!;
    private ToolStripButton toolOpenFile = null!;
    private ToolStripButton toolOpenFolder = null!;
    private ToolStripButton toolOpenSample = null!;
    private ToolStripButton toolStart = null!;
    private ToolStripButton toolCancel = null!;
    private ToolStripButton toolSave = null!;
    private ToolStripButton toolExport = null!;
    private ToolStripButton toolOpenOutput = null!;

    private SplitContainer splitMain = null!;
    private TableLayoutPanel leftLayout = null!;
    private GroupBox groupInput = null!;
    private TextBox txtInputPath = null!;
    private Button btnOpenFile = null!;
    private Button btnOpenFolder = null!;
    private Button btnOpenSample = null!;
    private GroupBox groupDocuments = null!;
    private ListView listDocuments = null!;
    private ColumnHeader columnDocumentName = null!;
    private ColumnHeader columnDocumentSource = null!;
    private ColumnHeader columnDocumentSize = null!;
    private ColumnHeader columnDocumentStatus = null!;
    private Label lblDocumentCount = null!;
    private Button btnCheckAll = null!;
    private Button btnUncheckAll = null!;
    private GroupBox groupOutput = null!;
    private TextBox txtOutputPath = null!;
    private Button btnOutputBrowse = null!;
    private CheckBox chkAutoSave = null!;
    private Button btnStart = null!;
    private Button btnCancel = null!;

    private TableLayoutPanel rightLayout = null!;
    private FlowLayoutPanel flowSummary = null!;
    private Label lblGameCountValue = null!;
    private Label lblPaCountValue = null!;
    private Label lblPitchCountValue = null!;
    private Label lblRunnerCountValue = null!;
    private Label lblChangeCountValue = null!;
    private Label lblWarningCountValue = null!;
    private Label lblErrorCountValue = null!;
    private Panel panelProgress = null!;
    private Label lblCurrentFile = null!;
    private Label lblProgressDetail = null!;
    private ProgressBar progressBar = null!;
    private Panel panelGridTools = null!;
    private ComboBox cboYearFilter = null!;
    private ComboBox cboCompetitionFilter = null!;
    private ComboBox cboTeamFilter = null!;
    private ComboBox cboPositionFilter = null!;
    private ComboBox cboPaQualificationFilter = null!;
    private ComboBox cboIpQualificationFilter = null!;
    private TextBox txtGridSearch = null!;
    private Button btnGridSearch = null!;
    private Button btnGridSearchClear = null!;
    private Button btnExportCurrentCsv = null!;
    private Label lblGridCount = null!;
    private TabControl tabResults = null!;
    private TabPage tabGames = null!;
    private TabPage tabBatterStats = null!;
    private TabPage tabPitcherStats = null!;
    private TabPage tabBatterSabermetrics = null!;
    private TabPage tabPitcherSabermetrics = null!;
    private TabPage tabBatterDiscipline = null!;
    private TabPage tabPitcherDiscipline = null!;
    private TabPage tabLeagueConstants = null!;
    private TabPage tabBatterValue = null!;
    private TabPage tabPitcherValue = null!;
    private TabPage tabFormula = null!;
    private TabPage tabPlateAppearances = null!;
    private TabPage tabPitches = null!;
    private TabPage tabRunners = null!;
    private TabPage tabPlayerChanges = null!;
    private TabPage tabAdministrative = null!;
    private TabPage tabDiagnostics = null!;
    private TabPage tabRawPreview = null!;
    private TabPage tabDatabase = null!;
    private TextBox txtSqliteDatabasePath = null!;
    private Label lblSqliteDatabaseStatus = null!;
    private Label lblSqliteDatabaseSize = null!;
    private Button btnOpenSqliteFolder = null!;
    private Button btnOptimizeSqlite = null!;
    private DataGridView gridGames = null!;
    private DataGridView gridBatterStats = null!;
    private DataGridView gridPitcherStats = null!;
    private DataGridView gridBatterSabermetrics = null!;
    private DataGridView gridPitcherSabermetrics = null!;
    private DataGridView gridBatterDiscipline = null!;
    private DataGridView gridPitcherDiscipline = null!;
    private DataGridView gridLeagueConstants = null!;
    private DataGridView gridBatterValue = null!;
    private DataGridView gridPitcherValue = null!;
    private FormulaViewControl formulaView = null!;
    private DataGridView gridPlateAppearances = null!;
    private DataGridView gridPitches = null!;
    private DataGridView gridRunners = null!;
    private DataGridView gridPlayerChanges = null!;
    private DataGridView gridAdministrative = null!;
    private DataGridView gridDiagnostics = null!;
    private RichTextBox txtRawPreview = null!;
    private GroupBox groupLog = null!;
    private RichTextBox txtLog = null!;
    private Button btnClearLog = null!;

    private StatusStrip statusStripMain = null!;
    private ToolStripStatusLabel statusLabel = null!;
    private ToolStripStatusLabel statusSpacer = null!;
    private ToolStripStatusLabel statusElapsed = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        menuStripMain = new MenuStrip();
        menuFile = new ToolStripMenuItem("파일(&F)");
        menuOpenFile = new ToolStripMenuItem("JSON/ZIP 파일 열기...");
        menuOpenFolder = new ToolStripMenuItem("폴더 열기...");
        menuOpenSample = new ToolStripMenuItem("포함된 7경기 샘플 열기");
        menuSelectOutput = new ToolStripMenuItem("출력 폴더 선택...");
        menuExit = new ToolStripMenuItem("끝내기");
        menuParse = new ToolStripMenuItem("파싱(&P)");
        menuStart = new ToolStripMenuItem("파싱 시작");
        menuCancel = new ToolStripMenuItem("취소");
        menuSaveResults = new ToolStripMenuItem("현재 결과 JSON 저장...");
        menuExportCsv = new ToolStripMenuItem("현재 표 CSV 내보내기...");
        menuHelp = new ToolStripMenuItem("도움말(&H)");
        menuUsage = new ToolStripMenuItem("사용 방법");
        menuAbout = new ToolStripMenuItem("프로그램 정보");

        toolStripMain = new ToolStrip();
        toolOpenFile = new ToolStripButton("파일 열기");
        toolOpenFolder = new ToolStripButton("폴더 열기");
        toolOpenSample = new ToolStripButton("샘플 열기");
        toolStart = new ToolStripButton("▶ 파싱 시작");
        toolCancel = new ToolStripButton("■ 취소");
        toolSave = new ToolStripButton("결과 저장");
        toolExport = new ToolStripButton("CSV 내보내기");
        toolOpenOutput = new ToolStripButton("출력 폴더 열기");

        splitMain = new SplitContainer();
        leftLayout = new TableLayoutPanel();
        groupInput = new GroupBox();
        txtInputPath = new TextBox();
        btnOpenFile = new Button();
        btnOpenFolder = new Button();
        btnOpenSample = new Button();
        groupDocuments = new GroupBox();
        listDocuments = new ListView();
        columnDocumentName = new ColumnHeader();
        columnDocumentSource = new ColumnHeader();
        columnDocumentSize = new ColumnHeader();
        columnDocumentStatus = new ColumnHeader();
        lblDocumentCount = new Label();
        btnCheckAll = new Button();
        btnUncheckAll = new Button();
        groupOutput = new GroupBox();
        txtOutputPath = new TextBox();
        btnOutputBrowse = new Button();
        chkAutoSave = new CheckBox();
        btnStart = new Button();
        btnCancel = new Button();

        rightLayout = new TableLayoutPanel();
        flowSummary = new FlowLayoutPanel();
        panelProgress = new Panel();
        lblCurrentFile = new Label();
        lblProgressDetail = new Label();
        progressBar = new ProgressBar();
        panelGridTools = new Panel();
        cboYearFilter = new ComboBox();
        cboCompetitionFilter = new ComboBox();
        cboTeamFilter = new ComboBox();
        cboPositionFilter = new ComboBox();
        cboPaQualificationFilter = new ComboBox();
        cboIpQualificationFilter = new ComboBox();
        txtGridSearch = new TextBox();
        btnGridSearch = new Button();
        btnGridSearchClear = new Button();
        btnExportCurrentCsv = new Button();
        lblGridCount = new Label();
        tabResults = new TabControl();
        tabGames = new TabPage("경기");
        tabBatterStats = new TabPage("타자 클래식");
        tabPitcherStats = new TabPage("투수 클래식");
        tabBatterSabermetrics = new TabPage("타자 세이버");
        tabPitcherSabermetrics = new TabPage("투수 세이버");
        tabBatterDiscipline = new TabPage("타자 선구·컨택");
        tabPitcherDiscipline = new TabPage("투수 존·컨택");
        tabLeagueConstants = new TabPage("리그 상수");
        tabBatterValue = new TabPage("타자 Value·WAR");
        tabPitcherValue = new TabPage("투수 Value·WAR");
        tabFormula = new TabPage("공식·계산기");
        tabPlateAppearances = new TabPage("타석");
        tabPitches = new TabPage("투구");
        tabRunners = new TabPage("주루");
        tabPlayerChanges = new TabPage("선수 교체");
        tabAdministrative = new TabPage("관리 이벤트");
        tabDiagnostics = new TabPage("진단");
        tabRawPreview = new TabPage("원본 JSON");
        tabDatabase = new TabPage("SQLite DB");
        gridGames = CreateGrid();
        gridBatterStats = CreateGrid();
        gridPitcherStats = CreateGrid();
        gridBatterSabermetrics = CreateGrid();
        gridPitcherSabermetrics = CreateGrid();
        gridBatterDiscipline = CreateGrid();
        gridPitcherDiscipline = CreateGrid();
        gridLeagueConstants = CreateGrid();
        gridBatterValue = CreateGrid();
        gridPitcherValue = CreateGrid();
        formulaView = new FormulaViewControl();
        gridPlateAppearances = CreateGrid();
        gridPitches = CreateGrid();
        gridRunners = CreateGrid();
        gridPlayerChanges = CreateGrid();
        gridAdministrative = CreateGrid();
        gridDiagnostics = CreateGrid();
        txtRawPreview = new RichTextBox();
        groupLog = new GroupBox();
        txtLog = new RichTextBox();
        btnClearLog = new Button();

        statusStripMain = new StatusStrip();
        statusLabel = new ToolStripStatusLabel("준비");
        statusSpacer = new ToolStripStatusLabel { Spring = true };
        statusElapsed = new ToolStripStatusLabel("경과 시간: 00:00:00");

        SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)splitMain).BeginInit();
        splitMain.Panel1.SuspendLayout();
        splitMain.Panel2.SuspendLayout();
        splitMain.SuspendLayout();

        menuFile.DropDownItems.AddRange([
            menuOpenFile,
            menuOpenFolder,
            menuOpenSample,
            new ToolStripSeparator(),
            menuSelectOutput,
            new ToolStripSeparator(),
            menuExit,
        ]);
        menuParse.DropDownItems.AddRange([
            menuStart,
            menuCancel,
            new ToolStripSeparator(),
            menuSaveResults,
            menuExportCsv,
        ]);
        menuHelp.DropDownItems.AddRange([menuUsage, menuAbout]);
        menuStripMain.Items.AddRange([menuFile, menuParse, menuHelp]);
        menuStripMain.Dock = DockStyle.Top;

        toolStripMain.Items.AddRange([
            toolOpenFile,
            toolOpenFolder,
            toolOpenSample,
            new ToolStripSeparator(),
            toolStart,
            toolCancel,
            new ToolStripSeparator(),
            toolSave,
            toolExport,
            toolOpenOutput,
        ]);
        toolStripMain.Dock = DockStyle.Top;
        toolStripMain.GripStyle = ToolStripGripStyle.Hidden;
        toolStripMain.Padding = new Padding(6, 2, 6, 2);

        splitMain.Dock = DockStyle.Fill;
        splitMain.FixedPanel = FixedPanel.Panel1;
        // SplitterDistance와 최소 패널 크기는 폼의 실제 크기가 확정된 뒤
        // MainForm에서 안전하게 적용합니다. 디자이너 초기화 중 설정하면
        // 고해상도 배율/DPI 환경에서 InvalidOperationException이 발생할 수 있습니다.
        splitMain.SplitterWidth = 6;
        splitMain.Panel1MinSize = 0;
        splitMain.Panel2MinSize = 0;

        leftLayout.Dock = DockStyle.Fill;
        leftLayout.ColumnCount = 1;
        leftLayout.RowCount = 4;
        leftLayout.Padding = new Padding(8);
        leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 105));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

        BuildInputGroup();
        BuildDocumentsGroup();
        BuildOutputGroup();
        BuildActionPanel();

        leftLayout.Controls.Add(groupInput, 0, 0);
        leftLayout.Controls.Add(groupDocuments, 0, 1);
        leftLayout.Controls.Add(groupOutput, 0, 2);
        splitMain.Panel1.Controls.Add(leftLayout);

        rightLayout.Dock = DockStyle.Fill;
        rightLayout.ColumnCount = 1;
        rightLayout.RowCount = 5;
        rightLayout.Padding = new Padding(6, 8, 8, 6);
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 155));

        flowSummary.Dock = DockStyle.Fill;
        flowSummary.AutoScroll = true;
        flowSummary.WrapContents = false;
        flowSummary.Padding = new Padding(0, 2, 0, 2);

        panelProgress.Dock = DockStyle.Fill;
        panelProgress.Padding = new Padding(4);
        lblCurrentFile.AutoEllipsis = true;
        lblCurrentFile.Font = new Font(Font, FontStyle.Bold);
        lblCurrentFile.Location = new Point(6, 4);
        lblCurrentFile.Size = new Size(780, 20);
        lblCurrentFile.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        lblCurrentFile.Text = "현재 파일: -";
        lblProgressDetail.AutoEllipsis = true;
        lblProgressDetail.Location = new Point(6, 44);
        lblProgressDetail.Size = new Size(780, 18);
        lblProgressDetail.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        lblProgressDetail.Text = "입력 파일을 선택해 주세요.";
        progressBar.Location = new Point(6, 26);
        progressBar.Size = new Size(780, 15);
        progressBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        panelProgress.Controls.AddRange([lblCurrentFile, progressBar, lblProgressDetail]);

        panelGridTools.Dock = DockStyle.Fill;
        panelGridTools.Padding = new Padding(2, 4, 2, 3);

        var lblYear = new Label { Text = "연도:", AutoSize = true, Location = new Point(5, 9) };
        cboYearFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        cboYearFilter.Location = new Point(45, 4);
        cboYearFilter.Size = new Size(85, 25);
        cboYearFilter.Items.Add("전체 연도");
        cboYearFilter.SelectedIndex = 0;

        var lblCompetition = new Label { Text = "경기 구분:", AutoSize = true, Location = new Point(140, 9) };
        cboCompetitionFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        cboCompetitionFilter.Location = new Point(205, 4);
        cboCompetitionFilter.Size = new Size(115, 25);
        cboCompetitionFilter.Items.AddRange(["정규시즌", "전체 경기", "시범경기", "포스트시즌", "올스타전", "퓨처스리그", "기타"]);
        cboCompetitionFilter.SelectedIndex = 0;

        var lblTeam = new Label { Text = "팀:", AutoSize = true, Location = new Point(332, 9) };
        cboTeamFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        cboTeamFilter.Location = new Point(360, 4);
        cboTeamFilter.Size = new Size(90, 25);
        cboTeamFilter.Items.Add("전체 팀");
        cboTeamFilter.SelectedIndex = 0;

        var lblPosition = new Label { Text = "포지션:", AutoSize = true, Location = new Point(462, 9) };
        cboPositionFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        cboPositionFilter.Location = new Point(515, 4);
        cboPositionFilter.Size = new Size(85, 25);
        cboPositionFilter.Items.AddRange(["전체 포지션", "C", "1B", "2B", "3B", "SS", "LF", "CF", "RF", "DH"]);
        cboPositionFilter.SelectedIndex = 0;

        var lblPaQual = new Label { Text = "규정타석:", AutoSize = true, Location = new Point(612, 9) };
        cboPaQualificationFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        cboPaQualificationFilter.Location = new Point(675, 4);
        cboPaQualificationFilter.Size = new Size(90, 25);
        cboPaQualificationFilter.Items.AddRange(["전체", "10%", "20%", "30%", "40%", "50%", "60%", "70%", "80%", "90%", "100%"]);
        cboPaQualificationFilter.SelectedIndex = 0;

        var lblIpQual = new Label { Text = "규정이닝:", AutoSize = true, Location = new Point(777, 9) };
        cboIpQualificationFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        cboIpQualificationFilter.Location = new Point(840, 4);
        cboIpQualificationFilter.Size = new Size(90, 25);
        cboIpQualificationFilter.Items.AddRange(["전체", "10%", "20%", "30%", "40%", "50%", "60%", "70%", "80%", "90%", "100%"]);
        cboIpQualificationFilter.SelectedIndex = 0;

        var lblSearch = new Label { Text = "현재 탭 검색:", AutoSize = true, Location = new Point(5, 43) };
        txtGridSearch.Location = new Point(90, 38);
        txtGridSearch.Size = new Size(250, 25);
        btnGridSearch.Text = "검색";
        btnGridSearch.Location = new Point(346, 37);
        btnGridSearch.Size = new Size(64, 28);
        btnGridSearchClear.Text = "해제";
        btnGridSearchClear.Location = new Point(415, 37);
        btnGridSearchClear.Size = new Size(64, 28);
        btnExportCurrentCsv.Text = "현재 표 CSV";
        btnExportCurrentCsv.Location = new Point(486, 37);
        btnExportCurrentCsv.Size = new Size(105, 28);
        lblGridCount.AutoSize = true;
        lblGridCount.Location = new Point(600, 43);
        lblGridCount.Text = "표시 0건";
        panelGridTools.Controls.AddRange([
            lblYear, cboYearFilter, lblCompetition, cboCompetitionFilter,
            lblTeam, cboTeamFilter, lblPosition, cboPositionFilter,
            lblPaQual, cboPaQualificationFilter, lblIpQual, cboIpQualificationFilter,
            lblSearch, txtGridSearch, btnGridSearch, btnGridSearchClear,
            btnExportCurrentCsv, lblGridCount
        ]);

        tabResults.Dock = DockStyle.Fill;
        tabResults.Multiline = false;
        tabGames.Controls.Add(gridGames);
        tabBatterStats.Controls.Add(gridBatterStats);
        tabPitcherStats.Controls.Add(gridPitcherStats);
        tabBatterSabermetrics.Controls.Add(gridBatterSabermetrics);
        tabPitcherSabermetrics.Controls.Add(gridPitcherSabermetrics);
        tabBatterDiscipline.Controls.Add(gridBatterDiscipline);
        tabPitcherDiscipline.Controls.Add(gridPitcherDiscipline);
        tabLeagueConstants.Controls.Add(gridLeagueConstants);
        tabBatterValue.Controls.Add(gridBatterValue);
        tabPitcherValue.Controls.Add(gridPitcherValue);
        tabFormula.Controls.Add(formulaView);
        tabPlateAppearances.Controls.Add(gridPlateAppearances);
        tabPitches.Controls.Add(gridPitches);
        tabRunners.Controls.Add(gridRunners);
        tabPlayerChanges.Controls.Add(gridPlayerChanges);
        tabAdministrative.Controls.Add(gridAdministrative);
        tabDiagnostics.Controls.Add(gridDiagnostics);

        txtRawPreview.Dock = DockStyle.Fill;
        txtRawPreview.ReadOnly = true;
        txtRawPreview.WordWrap = false;
        txtRawPreview.Font = new Font("Consolas", 9F);
        txtRawPreview.BackColor = SystemColors.Window;
        tabRawPreview.Controls.Add(txtRawPreview);
        BuildDatabaseTab();

        tabResults.TabPages.AddRange([
            tabGames,
            tabBatterStats,
            tabPitcherStats,
            tabBatterSabermetrics,
            tabPitcherSabermetrics,
            tabBatterDiscipline,
            tabPitcherDiscipline,
            tabLeagueConstants,
            tabBatterValue,
            tabPitcherValue,
            tabFormula,
            tabPlateAppearances,
            tabPitches,
            tabRunners,
            tabPlayerChanges,
            tabAdministrative,
            tabDiagnostics,
            tabRawPreview,
            tabDatabase,
        ]);

        groupLog.Dock = DockStyle.Fill;
        groupLog.Text = "작업 로그";
        groupLog.Padding = new Padding(8, 22, 8, 8);
        txtLog.Dock = DockStyle.Fill;
        txtLog.ReadOnly = true;
        txtLog.WordWrap = false;
        txtLog.Font = new Font("Consolas", 9F);
        txtLog.BackColor = SystemColors.Window;
        btnClearLog.Text = "지우기";
        btnClearLog.Size = new Size(65, 24);
        btnClearLog.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnClearLog.Location = new Point(groupLog.Width - 78, 0);
        groupLog.Controls.Add(txtLog);
        groupLog.Controls.Add(btnClearLog);
        groupLog.Resize += (_, _) => btnClearLog.Location = new Point(Math.Max(8, groupLog.ClientSize.Width - 75), 0);

        rightLayout.Controls.Add(flowSummary, 0, 0);
        rightLayout.Controls.Add(panelProgress, 0, 1);
        rightLayout.Controls.Add(panelGridTools, 0, 2);
        rightLayout.Controls.Add(tabResults, 0, 3);
        rightLayout.Controls.Add(groupLog, 0, 4);
        splitMain.Panel2.Controls.Add(rightLayout);

        statusStripMain.Items.AddRange([statusLabel, statusSpacer, statusElapsed]);
        statusStripMain.Dock = DockStyle.Bottom;

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1580, 920);
        MinimumSize = new Size(1180, 720);
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Naver Relay 세이버매트릭스 데이터 관리 도구";
        Font = new Font("맑은 고딕", 9F);
        MainMenuStrip = menuStripMain;
        AllowDrop = true;

        Controls.Add(splitMain);
        Controls.Add(toolStripMain);
        Controls.Add(menuStripMain);
        Controls.Add(statusStripMain);

        splitMain.Panel1.ResumeLayout(false);
        splitMain.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)splitMain).EndInit();
        splitMain.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
    }

    private void BuildInputGroup()
    {
        groupInput.Dock = DockStyle.Fill;
        groupInput.Text = "1. 입력 자료";
        groupInput.Padding = new Padding(8, 22, 8, 8);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        txtInputPath.Dock = DockStyle.Fill;
        txtInputPath.ReadOnly = true;
        txtInputPath.PlaceholderText = "JSON, ZIP 또는 폴더를 선택하세요.";

        var buttons = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Padding = new Padding(0, 5, 0, 0),
        };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        btnOpenFile.Text = "파일 선택";
        btnOpenFolder.Text = "폴더 선택";
        btnOpenSample.Text = "7경기 샘플";
        btnOpenFile.Dock = DockStyle.Fill;
        btnOpenFolder.Dock = DockStyle.Fill;
        btnOpenSample.Dock = DockStyle.Fill;
        buttons.Controls.Add(btnOpenFile, 0, 0);
        buttons.Controls.Add(btnOpenFolder, 1, 0);
        buttons.Controls.Add(btnOpenSample, 2, 0);

        layout.Controls.Add(txtInputPath, 0, 0);
        layout.Controls.Add(buttons, 0, 1);
        groupInput.Controls.Add(layout);
    }

    private void BuildDocumentsGroup()
    {
        groupDocuments.Dock = DockStyle.Fill;
        groupDocuments.Text = "2. 파싱 대상";
        groupDocuments.Padding = new Padding(8, 22, 8, 8);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new Panel { Dock = DockStyle.Fill };
        lblDocumentCount.AutoSize = true;
        lblDocumentCount.Location = new Point(1, 7);
        lblDocumentCount.Text = "0개";
        btnCheckAll.Text = "전체 선택";
        btnCheckAll.Size = new Size(72, 26);
        btnCheckAll.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnUncheckAll.Text = "전체 해제";
        btnUncheckAll.Size = new Size(72, 26);
        btnUncheckAll.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        header.Controls.AddRange([lblDocumentCount, btnCheckAll, btnUncheckAll]);
        header.Resize += (_, _) =>
        {
            btnUncheckAll.Location = new Point(Math.Max(150, header.ClientSize.Width - 74), 1);
            btnCheckAll.Location = new Point(Math.Max(75, header.ClientSize.Width - 150), 1);
        };

        columnDocumentName.Text = "파일";
        columnDocumentName.Width = 135;
        columnDocumentSource.Text = "출처";
        columnDocumentSource.Width = 175;
        columnDocumentSize.Text = "크기";
        columnDocumentSize.Width = 72;
        columnDocumentStatus.Text = "상태";
        columnDocumentStatus.Width = 105;
        listDocuments.Columns.AddRange([
            columnDocumentName,
            columnDocumentSource,
            columnDocumentSize,
            columnDocumentStatus,
        ]);
        listDocuments.Dock = DockStyle.Fill;
        listDocuments.View = View.Details;
        listDocuments.FullRowSelect = true;
        listDocuments.GridLines = true;
        listDocuments.CheckBoxes = true;
        listDocuments.HideSelection = false;
        listDocuments.ShowItemToolTips = true;

        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(listDocuments, 0, 1);
        groupDocuments.Controls.Add(layout);
    }

    private void BuildOutputGroup()
    {
        groupOutput.Dock = DockStyle.Fill;
        groupOutput.Text = "3. 출력";
        groupOutput.Padding = new Padding(8, 22, 8, 8);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        txtOutputPath.Dock = DockStyle.Fill;
        txtOutputPath.ReadOnly = true;
        btnOutputBrowse.Text = "찾기...";
        btnOutputBrowse.Dock = DockStyle.Fill;
        chkAutoSave.Text = "파싱과 동시에 정규화 JSON 저장";
        chkAutoSave.Checked = false;
        chkAutoSave.AutoSize = true;
        chkAutoSave.Anchor = AnchorStyles.Left;

        layout.Controls.Add(txtOutputPath, 0, 0);
        layout.Controls.Add(btnOutputBrowse, 1, 0);
        layout.Controls.Add(chkAutoSave, 0, 1);
        layout.SetColumnSpan(chkAutoSave, 2);
        groupOutput.Controls.Add(layout);
    }

    private void BuildActionPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0, 5, 0, 0),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        btnStart.Text = "파싱 시작";
        btnStart.Dock = DockStyle.Fill;
        btnStart.Font = new Font(Font, FontStyle.Bold);
        btnCancel.Text = "취소";
        btnCancel.Dock = DockStyle.Fill;
        btnCancel.Enabled = false;
        panel.Controls.Add(btnStart, 0, 0);
        panel.Controls.Add(btnCancel, 1, 0);
        leftLayout.Controls.Add(panel, 0, 3);
    }

    private void BuildDatabaseTab()
    {
        var outer = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = SystemColors.Window,
        };
        var title = new Label
        {
            Text = "SQLite 관계형 통계 데이터베이스",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Location = new Point(24, 24),
        };
        var description = new Label
        {
            Text = "원본 JSON은 신규·변경 경기를 가져올 때 한 번만 읽습니다.\r\n" +
                   "이후 경기 목록, 통계, 기간별 wRC+, WAR, 선수 페이지와 상세 로그는 아래 SQLite 관계형 테이블만 조회합니다.",
            AutoSize = true,
            Location = new Point(24, 56),
        };

        var group = new GroupBox
        {
            Text = "현재 데이터베이스",
            Location = new Point(24, 118),
            Size = new Size(820, 210),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 2,
            RowCount = 5,
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        txtSqliteDatabasePath = new TextBox
        {
            ReadOnly = true,
            Dock = DockStyle.Fill,
            BackColor = SystemColors.Window,
        };
        lblSqliteDatabaseStatus = new Label
        {
            Text = "준비 중",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        lblSqliteDatabaseSize = new Label
        {
            Text = "-",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        var storageMode = new Label
        {
            Text = "RelationalWarehouse v1 · 원본/정규화 JSON 열 없음",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
        };
        btnOpenSqliteFolder = new Button { Text = "DB 폴더 열기", Width = 120, Height = 28 };
        btnOptimizeSqlite = new Button { Text = "DB 최적화", Width = 110, Height = 28 };
        buttonPanel.Controls.Add(btnOpenSqliteFolder);
        buttonPanel.Controls.Add(btnOptimizeSqlite);
        buttonPanel.Controls.Add(new Label
        {
            Text = "새 DB를 만들려면 프로그램 종료 후 이 파일을 삭제하면 됩니다.",
            AutoSize = true,
            Margin = new Padding(12, 7, 0, 0),
            ForeColor = Color.DimGray,
        });

        AddDbRow(table, 0, "DB 파일", txtSqliteDatabasePath);
        AddDbRow(table, 1, "상태", lblSqliteDatabaseStatus);
        AddDbRow(table, 2, "파일 크기", lblSqliteDatabaseSize);
        AddDbRow(table, 3, "저장 방식", storageMode);
        AddDbRow(table, 4, string.Empty, buttonPanel);
        group.Controls.Add(table);

        var details = new Label
        {
            Text = "주요 조회 테이블: Games, Players, BatterGameStats, PitcherGameStats, PlateAppearances, Pitches, RunnerEvents\r\n" +
                   "ComputedCache에는 화면용 최종 집계 결과만 저장되며, 경기 원본 JSON은 저장하거나 다시 읽지 않습니다.",
            AutoSize = true,
            Location = new Point(24, 350),
            ForeColor = Color.DimGray,
        };

        outer.Controls.Add(title);
        outer.Controls.Add(description);
        outer.Controls.Add(group);
        outer.Controls.Add(details);
        tabDatabase.Controls.Add(outer);
    }

    private static void AddDbRow(TableLayoutPanel table, int row, string labelText, Control control)
    {
        table.Controls.Add(new Label
        {
            Text = labelText,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, row);
        table.Controls.Add(control, 1, row);
    }

    private static DataGridView CreateGrid()
    {
        return new DataGridView
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
            BorderStyle = BorderStyle.Fixed3D,
            ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText,
            MultiSelect = true,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        };
    }
}
