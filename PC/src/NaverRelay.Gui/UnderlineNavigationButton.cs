namespace NaverRelay.Gui;

internal sealed class UnderlineNavigationButton : Button
{
    public bool Selected { get; set; }
    public bool FillSelected { get; set; }
    public Color AccentColor { get; set; } = Color.FromArgb(232, 24, 92);

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        if (!Selected || FillSelected) return;
        using var pen = new Pen(AccentColor, 3F);
        eventArgs.Graphics.DrawLine(pen, 5, Height - 2, Math.Max(5, Width - 6), Height - 2);
    }
}
