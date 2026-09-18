using System.Globalization;
using NaverRelay.Application.Queries;

namespace NaverRelay.Gui;

internal sealed class FormulaViewControl : UserControl
{
    private readonly ListBox _metrics = new() { Dock = DockStyle.Fill };
    private readonly RichTextBox _formula = new() { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 10F), BackColor = SystemColors.Window };
    private readonly NumericUpDown[] _wobaInputs;
    private readonly Label _wobaResult = new() { AutoSize = true, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) };
    private readonly NumericUpDown[] _fipInputs;
    private readonly Label _fipResult = new() { AutoSize = true, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) };
    private WobaConstants _woba = new();

    public FormulaViewControl()
    {
        Dock = DockStyle.Fill;
        var main = new SplitContainer { Dock = DockStyle.Fill };
        Controls.Add(main);
        main.SizeChanged += (_, _) =>
        {
            var available = main.ClientSize.Width - main.SplitterWidth;
            if (available <= 0) return;
            main.Panel1MinSize = 0;
            main.Panel2MinSize = 0;
            var desired = Math.Clamp(210, 0, available);
            if (main.SplitterDistance != desired) main.SplitterDistance = desired;
        };

        _metrics.Items.AddRange(new object[]
        {
            "AVG", "OBP", "SLG", "OPS", "ISO", "BABIP", "BB% / K%", "wOBA*", "wRAA*", "wRC*", "wRC+*", "OPS+",
            "FIP*", "xFIP*", "FIP- / xFIP-", "Swing% / Contact%", "CSW%", "RE24", "WPA (예정)",
            "Site WAR v1*", "KBO 투수 대체수준*", "KBO WARIP*", "KBO fWAR v4*",
            "KBO RA9-WAR*", "Blend WAR 70/30*", "파크 팩터*"
        });
        main.Panel1.Controls.Add(_metrics);

        var rightTabs = new TabControl { Dock = DockStyle.Fill };
        var formulaTab = new TabPage("공식 및 설명");
        var calculatorTab = new TabPage("계산기");
        formulaTab.Controls.Add(_formula);
        rightTabs.TabPages.Add(formulaTab);
        rightTabs.TabPages.Add(calculatorTab);
        main.Panel2.Controls.Add(rightTabs);

        var calc = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(12) };
        calculatorTab.Controls.Add(calc);
        _wobaInputs = AddCalculator(calc, "wOBA 계산기", new[] { "AB", "uBB", "HBP", "1B", "2B", "3B", "HR", "SF" }, CalculateWoba, _wobaResult);
        _fipInputs = AddCalculator(calc, "FIP 계산기", new[] { "IP", "HR", "BB", "HBP", "SO", "FIP 상수" }, CalculateFip, _fipResult, decimalPlaces: 2);

        _metrics.SelectedIndexChanged += (_, _) => ShowSelectedFormula();
        _metrics.SelectedIndex = 0;
    }

    public void SetWobaConstants(WobaConstants constants)
    {
        _woba = constants ?? new WobaConstants();
        ShowSelectedFormula();
        CalculateWoba(null, EventArgs.Empty);
    }

    private static NumericUpDown[] AddCalculator(FlowLayoutPanel parent, string title, string[] labels, EventHandler calculate, Label result, int decimalPlaces = 0)
    {
        var box = new GroupBox { Text = title, Width = 620, Height = 150 + labels.Length * 6, Padding = new Padding(10) };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true };
        var inputs = new NumericUpDown[labels.Length];
        for (var i = 0; i < labels.Length; i++)
        {
            flow.Controls.Add(new Label { Text = labels[i], Width = 70, TextAlign = ContentAlignment.MiddleRight, Margin = new Padding(3, 7, 3, 3) });
            inputs[i] = new NumericUpDown { Width = 85, Maximum = 10000, DecimalPlaces = decimalPlaces, Increment = decimalPlaces > 0 ? 0.1M : 1M };
            flow.Controls.Add(inputs[i]);
        }
        var button = new Button { Text = "계산", AutoSize = true };
        button.Click += calculate;
        flow.Controls.Add(button);
        flow.Controls.Add(result);
        box.Controls.Add(flow);
        parent.Controls.Add(box);
        return inputs;
    }

    private void CalculateWoba(object? sender, EventArgs e)
    {
        var ab = (double)_wobaInputs[0].Value; var bb = (double)_wobaInputs[1].Value; var hbp = (double)_wobaInputs[2].Value;
        var s1 = (double)_wobaInputs[3].Value; var d2 = (double)_wobaInputs[4].Value; var t3 = (double)_wobaInputs[5].Value;
        var hr = (double)_wobaInputs[6].Value; var sf = (double)_wobaInputs[7].Value;
        var den = ab + bb + hbp + sf;
        _wobaResult.Text = den > 0
            ? $"wOBA = {(_woba.UnintentionalWalk * bb + _woba.HitByPitch * hbp + _woba.Single * s1 + _woba.Double * d2 + _woba.Triple * t3 + _woba.HomeRun * hr) / den:0.000} ({_woba.SeasonYear?.ToString() ?? "통합"} {_woba.Source})"
            : "분모가 0입니다.";
    }

    private void CalculateFip(object? sender, EventArgs e)
    {
        var ip = (double)_fipInputs[0].Value; var hr = (double)_fipInputs[1].Value; var bb = (double)_fipInputs[2].Value;
        var hbp = (double)_fipInputs[3].Value; var so = (double)_fipInputs[4].Value; var c = (double)_fipInputs[5].Value;
        _fipResult.Text = ip > 0 ? $"FIP = {((13 * hr + 3 * (bb + hbp) - 2 * so) / ip + c):0.00}" : "IP가 0입니다.";
    }

    private void ShowSelectedFormula()
    {
        var name = _metrics.SelectedItem?.ToString() ?? string.Empty;
        _formula.Text = name switch
        {
            "AVG" => "AVG = H / AB\r\n\r\n타수 대비 안타 비율입니다.",
            "OBP" => "OBP = (H + BB + HBP) / (AB + BB + HBP + SF)",
            "SLG" => "SLG = (1B + 2×2B + 3×3B + 4×HR) / AB",
            "OPS" => "OPS = OBP + SLG",
            "ISO" => "ISO = SLG - AVG\r\n\r\n순수 장타력을 나타냅니다.",
            "BABIP" => "BABIP = (H - HR) / (AB - SO - HR + SF)",
            "BB% / K%" => "BB% = BB / PA\r\nK% = SO / PA",
            "wOBA*" => $"wOBA = ({_woba.UnintentionalWalk:0.000}×uBB + {_woba.HitByPitch:0.000}×HBP + {_woba.Single:0.000}×1B + {_woba.Double:0.000}×2B + {_woba.Triple:0.000}×3B + {_woba.HomeRun:0.000}×HR)\r\n       / (AB + uBB + HBP + SF)\r\n\r\n{_woba.SeasonYear?.ToString() ?? "통합"} {_woba.Source}, Scale {_woba.Scale:0.000}",
            "wRAA*" => "wRAA = ((선수 wOBA - 리그 wOBA) / wOBA Scale) × PA",
            "wRC*" => "wRC = wRAA + (리그 R/PA × PA)",
            "wRC+*" => "wRC+ = 100 × (선수 wRC/PA) / 리그 R/PA\r\n\r\n* 현재 버전은 구장 보정이 없습니다.",
            "OPS+" => "OPS+ = 100 × (OBP/lgOBP + SLG/lgSLG - 1)",
            "FIP*" => "FIP = (13×HR + 3×(BB+HBP) - 2×SO) / IP + FIP 상수\r\n\r\n* 상수는 리그 평균 FIP가 리그 RA9와 같아지도록 맞춥니다.",
            "xFIP*" => "xFIP = (13×(FB×lgHR/FB) + 3×(BB+HBP) - 2×SO) / IP + FIP 상수",
            "FIP- / xFIP-" => "FIP- = 100 × 선수 FIP / 리그 RA9\r\nxFIP- = 100 × 선수 xFIP / 리그 RA9\r\n\r\n100보다 낮을수록 좋습니다.",
            "Swing% / Contact%" => "Swing% = 스윙 / 전체 투구\r\nContact% = 컨택 / 스윙",
            "CSW%" => "CSW% = (헛스윙 + 루킹 스트라이크) / 전체 투구",
            "RE24" => "RE24 = 타석 종료 후 기대득점 - 타석 시작 전 기대득점 + 실제 득점\r\n\r\n시즌별 24개 주자·아웃 상태 기대득점표에서 이벤트별 평균 득점가치를 계산하고, 리그 OBP 스케일로 wOBA 계수를 맞춥니다.",
            "WPA (예정)" => "WPA = 타석 종료 후 승리확률 - 타석 시작 전 승리확률",
            "Site WAR v1*" => "타격 Runs = wRAA\r\n주루 Runs = 0.20×SB - 0.40×CS\r\n수비 Runs = 0 (데이터 준비 전)\r\n포지션 보정 = 0 (데이터 준비 전)\r\n대체선수 Runs = PA × 20 / 600\r\nRAR = 위 Runs의 합\r\nSite WAR v1 = RAR / 10\r\n\r\n* 임시 추정 WAR이며 공식 KBO/Statiz WAR와 동일하지 않습니다.",
            "KBO 투수 대체수준*" => "KBO fWAR v4 정책값\r\n\r\n선발 Replacement FIP- = 120\r\n구원 Replacement FIP- = 115\r\n\r\nReplacement FIPR9 = lgFIPR9 × FIP-/100\r\n\r\n2020~2025 완료 시즌과 KBO PF v2로 검증했습니다. 대체후보 WAR 중앙값은 선발 약 -0.008, 구원 약 -0.005였고 후보의 양/음수 비율이 약 50:50에 위치했습니다.",
            "KBO WARIP*" => "공통 전체 WAR = 리그 경기수×2×(0.500-0.294)\r\n타자 목표 = 전체 WAR×0.57\r\n투수 목표 = 전체 WAR×0.43\r\n\r\n투수 WARIP = (투수 목표 WAR-보정 전 리그 투수 WAR 합)/해당 범위 리그 IP\r\n선수 보정 = WARIP×선수 IP\r\n\r\n시즌 진행 중에는 완료 경기수에 따라 목표 총량도 자동 증가합니다.",
            "KBO fWAR v4*" => "ifFIP = [13×HR + 3×(BB+HBP) - 2×(SO+IFFB)] / IP + 상수\r\nFIPR9 = ifFIP + (lgRA9-lgERA)\r\npFIPR9 = FIPR9/(FIP PF/100)\r\ndRPW = ((((18-IP/G)×lgFIPR9 + (IP/G)×pFIPR9)/18)+2)×1.5\r\nRAA 승 = (lgFIPR9-pFIPR9)/dRPW×IP/9\r\n대체 승 = (KBO Repl FIPR9-lgFIPR9)/dRPW×IP/9\r\n구원 LI 배수 = (1+gmLI)/2\r\n보정 전 fWAR = RAA 승+대체 승(구원은 LI 적용)\r\n최종 fWAR = 보정 전 fWAR+KBO WARIP×IP",
            "KBO RA9-WAR*" => "pRA9* = RA9/(FIP PF/100)\r\nRA9 dRPW = ((((18-IP/G)×lgRA9 + (IP/G)×pRA9)/18)+2)×1.5\r\nRA9 RAA 승 = (lgRA9-pRA9)/RA9 dRPW×IP/9\r\nRA9 대체 승 = (KBO Repl RA9-lgRA9)/RA9 dRPW×IP/9\r\n최종 RA9-WAR = 보정 전 RA9-WAR+RA9 WARIP×IP\r\n\r\n* 별도 득점 파크팩터가 없어 현재 FIP PF를 구장 보정 대용값으로 사용합니다. 팀 수비·상대 타선 보정은 아직 없습니다.",
            "Blend WAR 70/30*" => "Blend WAR = 0.70×KBO fWAR + 0.30×KBO RA9-WAR\r\n\r\n삼진·볼넷·홈런 중심의 수비 독립 성과를 주값으로 두고 실제 실점 억제를 보조적으로 반영하는 사이트 보조 지표입니다. 공식 FanGraphs 지표가 아닙니다.",
            "파크 팩터*" => "KBO PF v2\r\n\r\n최근 5년 득점 환경을 30/25/20/15/10% 최근가중으로 결합합니다.\r\nReliability = G/(G+100)\r\n회귀 PF = 100 + (Raw PF-100)×Reliability\r\n안전범위 = 85~115\r\n마지막으로 시즌 투구이닝 가중평균이 100이 되도록 재중앙화합니다.\r\n\r\nLegacy FIP PF는 진단 화면에만 보존합니다.",
            _ => string.Empty,
        };
    }
}
