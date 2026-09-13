using NaverRelay.Application.Players;

namespace NaverRelay.Gui;

/// <summary>
/// 외부 차트 패키지 없이 Rolling wRC+를 보여주는 경량 WinForms 차트입니다.
/// </summary>
internal sealed class RollingWrcChart : Control
{
    private IReadOnlyList<RollingMetricPoint> _points = Array.Empty<RollingMetricPoint>();

    public RollingWrcChart()
    {
        DoubleBuffered = true;
        BackColor = Color.White;
        ForeColor = Color.FromArgb(45, 52, 64);
        MinimumSize = new Size(300, 180);
        ResizeRedraw = true;
    }

    public void SetPoints(IEnumerable<RollingMetricPoint> points)
    {
        _points = points.Where(point => point.WrcPlus.HasValue).ToList();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        graphics.Clear(BackColor);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var bounds = ClientRectangle;
        if (bounds.Width < 80 || bounds.Height < 80) return;

        var plot = RectangleF.FromLTRB(62, 24, bounds.Width - 22, bounds.Height - 42);
        using var axisPen = new Pen(Color.FromArgb(170, 176, 186), 1F);
        using var gridPen = new Pen(Color.FromArgb(226, 229, 234), 1F) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
        using var averagePen = new Pen(Color.FromArgb(186, 75, 69), 1.5F) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
        using var linePen = new Pen(Color.FromArgb(39, 99, 173), 2.2F);
        using var labelBrush = new SolidBrush(Color.FromArgb(90, 96, 106));
        using var titleBrush = new SolidBrush(ForeColor);
        using var pointBrush = new SolidBrush(Color.FromArgb(39, 99, 173));
        using var font = new Font(Font.FontFamily, Math.Max(8F, Font.Size - 1F));
        using var titleFont = new Font(Font.FontFamily, Font.Size + 1F, FontStyle.Bold);

        graphics.DrawString("Rolling wRC+", titleFont, titleBrush, plot.Left, 2);
        graphics.DrawLine(axisPen, plot.Left, plot.Top, plot.Left, plot.Bottom);
        graphics.DrawLine(axisPen, plot.Left, plot.Bottom, plot.Right, plot.Bottom);

        if (_points.Count == 0)
        {
            const string message = "표시할 Rolling wRC+ 데이터가 없습니다.";
            var size = graphics.MeasureString(message, Font);
            graphics.DrawString(message, Font, labelBrush,
                plot.Left + (plot.Width - size.Width) / 2,
                plot.Top + (plot.Height - size.Height) / 2);
            return;
        }

        var values = _points.Select(point => point.WrcPlus!.Value).ToList();
        var min = Math.Min(50.0, Math.Floor(values.Min() / 10.0) * 10.0 - 10.0);
        var max = Math.Max(150.0, Math.Ceiling(values.Max() / 10.0) * 10.0 + 10.0);
        if (max - min < 40.0)
        {
            min -= 20.0;
            max += 20.0;
        }

        const int tickCount = 5;
        for (var index = 0; index <= tickCount; index++)
        {
            var value = min + (max - min) * index / tickCount;
            var y = MapY(value, min, max, plot);
            graphics.DrawLine(gridPen, plot.Left, y, plot.Right, y);
            var text = value.ToString("0.0");
            var size = graphics.MeasureString(text, font);
            graphics.DrawString(text, font, labelBrush, plot.Left - size.Width - 8, y - size.Height / 2);
        }

        if (100.0 >= min && 100.0 <= max)
        {
            var averageY = MapY(100.0, min, max, plot);
            graphics.DrawLine(averagePen, plot.Left, averageY, plot.Right, averageY);
            var averageText = 100.0.ToString("0.0");
            var averageSize = graphics.MeasureString(averageText, font);
            graphics.DrawString(averageText, font, Brushes.IndianRed, plot.Right - averageSize.Width, averageY - 17);
        }

        var chartPoints = new PointF[_points.Count];
        for (var index = 0; index < _points.Count; index++)
        {
            var x = _points.Count == 1
                ? plot.Left + plot.Width / 2
                : plot.Left + plot.Width * index / (_points.Count - 1);
            var y = MapY(_points[index].WrcPlus!.Value, min, max, plot);
            chartPoints[index] = new PointF(x, y);
        }

        if (chartPoints.Length > 1) graphics.DrawLines(linePen, chartPoints);
        foreach (var point in chartPoints)
            graphics.FillEllipse(pointBrush, point.X - 2.3F, point.Y - 2.3F, 4.6F, 4.6F);

        var labelIndexes = new HashSet<int> { 0, _points.Count - 1 };
        if (_points.Count > 2) labelIndexes.Add(_points.Count / 2);
        foreach (var index in labelIndexes.OrderBy(value => value))
        {
            var date = _points[index].Date;
            if (date.Length >= 10) date = date[5..10];
            var size = graphics.MeasureString(date, font);
            graphics.DrawString(date, font, labelBrush,
                Math.Clamp(chartPoints[index].X - size.Width / 2, plot.Left, plot.Right - size.Width),
                plot.Bottom + 7);
        }
    }

    private static float MapY(double value, double min, double max, RectangleF plot)
    {
        var ratio = (value - min) / Math.Max(0.0001, max - min);
        return plot.Bottom - (float)(ratio * plot.Height);
    }
}
