using NaverRelayUI.Collection;
using NaverRelayUI.Models;
using NaverRelayUI.Parsing;

namespace NaverRelayUI
{
    public class MainForm : Form
    {
        // ---- 수집 탭 ----
        private readonly DateTimePicker _fromDatePicker;
        private readonly DateTimePicker _toDatePicker;
        private readonly TextBox _outputDirBox;
        private readonly Button _collectStartButton;
        private readonly Button _collectCancelButton;
        private readonly ProgressBar _collectProgressBar;
        private readonly Label _collectStatusLabel;
        private readonly TextBox _collectLogBox;
        private CancellationTokenSource? _collectCts;

        // ---- 파싱 탭 ----
        private readonly TextBox _parseInputDirBox;
        private readonly TextBox _parseDbPathBox;
        private readonly Button _parseStartButton;
        private readonly Button _parseCancelButton;
        private readonly ProgressBar _parseProgressBar;
        private readonly Label _parseStatusLabel;
        private readonly TextBox _parseLogBox;
        private CancellationTokenSource? _parseCts;

        public MainForm()
        {
            Text = "네이버 + KBO 공식 문자중계 통합 수집기";
            Width = 740;
            Height = 620;
            MinimumSize = new Size(740, 620);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Malgun Gothic", 9F);

            var tabControl = new TabControl { Dock = DockStyle.Fill };
            var tabCollect = new TabPage("날짜별 통합 수집");
            var tabParse = new TabPage("네이버 파싱 (기존 기능)");
            tabControl.TabPages.Add(tabCollect);
            tabControl.TabPages.Add(tabParse);
            Controls.Add(tabControl);

            // ===================== 수집 탭 =====================
            var fromLabel = new Label { Text = "시작일", Left = 12, Top = 15, Width = 50 };
            _fromDatePicker = new DateTimePicker
            {
                Left = 70, Top = 12, Width = 130, Format = DateTimePickerFormat.Short,
                Value = new DateTime(2026, 3, 28),
            };

            var toLabel = new Label { Text = "종료일", Left = 215, Top = 15, Width = 50 };
            _toDatePicker = new DateTimePicker
            {
                Left = 270, Top = 12, Width = 130, Format = DateTimePickerFormat.Short,
                Value = DateTime.Today,
            };

            var outDirLabel = new Label { Text = "저장 폴더", Left = 12, Top = 48, Width = 60 };
            _outputDirBox = new TextBox
            {
                Left = 80, Top = 45, Width = 480,
                Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NaverKboCombined"),
            };
            var collectBrowseButton = new Button { Text = "찾아보기...", Left = 570, Top = 44, Width = 90 };
            collectBrowseButton.Click += (_, _) =>
            {
                using var dlg = new FolderBrowserDialog { SelectedPath = _outputDirBox.Text };
                if (dlg.ShowDialog(this) == DialogResult.OK) _outputDirBox.Text = dlg.SelectedPath;
            };

            _collectStartButton = new Button { Text = "통합 수집 시작", Left = 12, Top = 82, Width = 108, Height = 30 };
            _collectStartButton.Click += CollectStartButton_Click;

            _collectCancelButton = new Button
            {
                Text = "취소", Left = 124, Top = 82, Width = 96, Height = 30, Enabled = false,
            };
            _collectCancelButton.Click += (_, _) => _collectCts?.Cancel();

            _collectStatusLabel = new Label { Text = "대기 중", Left = 232, Top = 88, Width = 420 };
            _collectProgressBar = new ProgressBar { Left = 12, Top = 124, Width = 680, Height = 20 };

            var collectHelp = new Label { Text = "경기별 JSON: KBO 공식 플레이로그·박스스코어 + 네이버 원본 + 선수 ID 연결", Left = 12, Top = 151, Width = 680, Height = 25 };
            _collectLogBox = new TextBox
            {
                Left = 12, Top = 179, Width = 680, Height = 375,
                Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Font = new Font("Consolas", 9F),
            };

            tabCollect.Controls.AddRange(new Control[]
            {
                fromLabel, _fromDatePicker, toLabel, _toDatePicker,
                outDirLabel, _outputDirBox, collectBrowseButton,
                _collectStartButton, _collectCancelButton, _collectStatusLabel,
                _collectProgressBar, collectHelp, _collectLogBox,
            });

            // ===================== 파싱 탭 =====================
            var inDirLabel = new Label { Text = "원본 폴더", Left = 12, Top = 15, Width = 70 };
            _parseInputDirBox = new TextBox
            {
                Left = 90, Top = 12, Width = 470,
                Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NaverKboCombined"),
            };
            var parseBrowseInButton = new Button { Text = "찾아보기...", Left = 570, Top = 11, Width = 90 };
            parseBrowseInButton.Click += (_, _) =>
            {
                using var dlg = new FolderBrowserDialog { SelectedPath = _parseInputDirBox.Text };
                if (dlg.ShowDialog(this) == DialogResult.OK) _parseInputDirBox.Text = dlg.SelectedPath;
            };

            var dbLabel = new Label { Text = "저장할 DB 파일", Left = 12, Top = 50, Width = 90 };
            _parseDbPathBox = new TextBox
            {
                Left = 110, Top = 47, Width = 450,
                Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NaverKboCombined", "naver_parsed.db"),
            };
            var parseBrowseOutButton = new Button { Text = "찾아보기...", Left = 570, Top = 46, Width = 90 };
            parseBrowseOutButton.Click += (_, _) =>
            {
                using var dlg = new SaveFileDialog
                {
                    Filter = "SQLite DB (*.db)|*.db|모든 파일 (*.*)|*.*",
                    FileName = _parseDbPathBox.Text,
                    OverwritePrompt = false, // it's fine if it already exists — we append/replace rows into it
                };
                if (dlg.ShowDialog(this) == DialogResult.OK) _parseDbPathBox.Text = dlg.FileName;
            };

            _parseStartButton = new Button { Text = "파싱 시작", Left = 12, Top = 84, Width = 100, Height = 30 };
            _parseStartButton.Click += ParseStartButton_Click;

            _parseCancelButton = new Button
            {
                Text = "취소", Left = 120, Top = 84, Width = 100, Height = 30, Enabled = false,
            };
            _parseCancelButton.Click += (_, _) => _parseCts?.Cancel();

            _parseStatusLabel = new Label { Text = "대기 중", Left = 232, Top = 90, Width = 420 };
            _parseProgressBar = new ProgressBar { Left = 12, Top = 126, Width = 680, Height = 20 };

            var parseHelp = new Label { Text = "이 탭은 네이버 타석 결과를 파싱합니다. KBO 공식 기록으로 교정하는 기능은 아닙니다.", Left = 12, Top = 151, Width = 680, Height = 25 };
            _parseLogBox = new TextBox
            {
                Left = 12, Top = 180, Width = 680, Height = 376,
                Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Font = new Font("Consolas", 9F),
            };

            tabParse.Controls.AddRange(new Control[]
            {
                inDirLabel, _parseInputDirBox, parseBrowseInButton,
                dbLabel, _parseDbPathBox, parseBrowseOutButton,
                _parseStartButton, _parseCancelButton, _parseStatusLabel,
                _parseProgressBar, parseHelp, _parseLogBox,
            });
            FormClosing += (_, e) =>
            {
                if (_collectCts is null && _parseCts is null) return;
                _collectCts?.Cancel(); _parseCts?.Cancel(); e.Cancel = true;
                AppendLog(_collectLogBox, "취소 중입니다. 작업이 멈춘 뒤 창을 닫아주세요.");
            };
        }

        // ===================== 수집 탭 로직 =====================
        private async void CollectStartButton_Click(object? sender, EventArgs e)
        {
            if (_fromDatePicker.Value.Date > _toDatePicker.Value.Date)
            { MessageBox.Show(this, "시작일이 종료일보다 늦습니다."); return; }
            if (string.IsNullOrWhiteSpace(_outputDirBox.Text))
            { MessageBox.Show(this, "저장 폴더를 선택하세요."); return; }
            _collectStartButton.Enabled = false;
            _parseStartButton.Enabled = false;
            _collectCancelButton.Enabled = true;
            _collectLogBox.Clear();
            _collectProgressBar.Value = 0;
            _collectCts = new CancellationTokenSource();

            var fromDate = _fromDatePicker.Value.ToString("yyyy-MM-dd");
            var toDate = _toDatePicker.Value.ToString("yyyy-MM-dd");
            var outputDir = _outputDirBox.Text;

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Mozilla/5.0 (compatible; personal-stats-project/0.1)");

                _collectStatusLabel.Text = "경기 목록 수집 중...";
                AppendLog(_collectLogBox, $"{fromDate} ~ {toDate} 사이 KBO 경기 목록을 가져옵니다...");

                var idProgress = new Progress<(int done, int total)>(p =>
                {
                    _collectProgressBar.Maximum = Math.Max(p.total, 1);
                    _collectProgressBar.Value = Math.Min(p.done, _collectProgressBar.Maximum);
                    _collectStatusLabel.Text = $"경기 목록 수집 중... ({p.done}/{p.total})";
                });

                var games = await GameIdCollector.CollectAllKboGamesAsync(
                    http, fromDate, toDate, delayMs: 300, log: m => AppendLog(_collectLogBox, m),
                    progress: idProgress, ct: _collectCts.Token);

                AppendLog(_collectLogBox, $"KBO 경기 {games.Count}건 확인됨. 중계 데이터를 수집합니다...");
                _collectStatusLabel.Text = $"중계 데이터 수집 중... (0/{games.Count})";
                _collectProgressBar.Value = 0;

                var relayProgress = new Progress<(int done, int total)>(p =>
                {
                    _collectProgressBar.Maximum = Math.Max(p.total, 1);
                    _collectProgressBar.Value = Math.Min(p.done, _collectProgressBar.Maximum);
                    _collectStatusLabel.Text = $"중계 데이터 수집 중... ({p.done}/{p.total})";
                });

                var summary = await RelayCollector.CollectAllAsync(
                    http, games, outputDir, delayMs: 500, log: m => AppendLog(_collectLogBox, m),
                    progress: relayProgress, ct: _collectCts.Token);

                _collectStatusLabel.Text = $"완료 {summary.Complete} / 부분 {summary.Partial} / 실패 {summary.Failed}";
                AppendLog(_collectLogBox, $"통합 완료 {summary.Complete}, 부분 저장 {summary.Partial}, 기존/취소 건너뜀 {summary.Skipped}, 정규시즌 외 제외 {summary.Excluded}, 실패 {summary.Failed}");
                if (summary.Partial + summary.Failed > 0)
                    AppendLog(_collectLogBox, "부분 저장/실패 경기는 같은 날짜로 다시 실행하면 재시도합니다.");
            }
            catch (OperationCanceledException)
            {
                _collectStatusLabel.Text = "취소됨";
                AppendLog(_collectLogBox, "사용자가 취소했습니다. 다시 실행하면 이어서 받습니다.");
            }
            catch (Exception ex)
            {
                _collectStatusLabel.Text = "오류 발생";
                AppendLog(_collectLogBox, $"오류: {ex.Message}");
                MessageBox.Show(this, ex.Message, "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _collectStartButton.Enabled = true;
                _parseStartButton.Enabled = true;
                _collectCancelButton.Enabled = false;
                _collectCts?.Dispose();
                _collectCts = null;
            }
        }

        // ===================== 파싱 탭 로직 =====================
        private async void ParseStartButton_Click(object? sender, EventArgs e)
        {
            _parseStartButton.Enabled = false;
            _collectStartButton.Enabled = false;
            _parseCancelButton.Enabled = true;
            _parseLogBox.Clear();
            _parseProgressBar.Value = 0;
            _parseCts = new CancellationTokenSource();

            var inputDir = _parseInputDirBox.Text;
            var dbPath = _parseDbPathBox.Text;

            try
            {
                _parseStatusLabel.Text = "파싱 중...";
                AppendLog(_parseLogBox, $"네이버 데이터만 파싱: '{inputDir}' → '{dbPath}'. KBO 공식값은 이 파서에서 사용하지 않습니다.");

                var progress = new Progress<(int done, int total)>(p =>
                {
                    _parseProgressBar.Maximum = Math.Max(p.total, 1);
                    _parseProgressBar.Value = Math.Min(p.done, _parseProgressBar.Maximum);
                    _parseStatusLabel.Text = $"파싱 중... ({p.done}/{p.total})";
                });

                await ParsingService.ParseFolderAsync(
                    inputDir, dbPath, log: m => AppendLog(_parseLogBox, m),
                    progress: progress, ct: _parseCts.Token);

                _parseStatusLabel.Text = "완료";
            }
            catch (OperationCanceledException)
            {
                _parseStatusLabel.Text = "취소됨";
                AppendLog(_parseLogBox, "사용자가 취소했습니다. 다시 실행하면 이어서 처리합니다 (기존 DB에 REPLACE로 저장됩니다).");
            }
            catch (Exception ex)
            {
                _parseStatusLabel.Text = "오류 발생";
                AppendLog(_parseLogBox, $"오류: {ex.Message}");
                MessageBox.Show(this, ex.Message, "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _parseStartButton.Enabled = true;
                _collectStartButton.Enabled = true;
                _parseCancelButton.Enabled = false;
                _parseCts?.Dispose();
                _parseCts = null;
            }
        }

        // Safe to call directly (no Invoke needed): everything above awaits
        // HttpClient/Task.Delay/SQLite calls without ConfigureAwait(false), so
        // continuations — including these log/progress callbacks — resume on
        // the UI thread's SynchronizationContext.
        private static void AppendLog(TextBox box, string message)
        {
            box.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        }
    }
}
