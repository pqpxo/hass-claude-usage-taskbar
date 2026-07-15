// version 7
using System.Drawing.Drawing2D;

namespace ClaudeUsageTray;

internal sealed class HoverDetailsForm : Form
{
    private const int HorizontalPadding = 14;
    private const int VerticalPadding = 8;
    private const int CornerRadius = 10;
    private const int ScreenGap = 8;

    private static readonly Color ClaudeOrange =
        Color.FromArgb(217, 119, 87);

    private static readonly Color PopupBackground =
        Color.FromArgb(35, 37, 42);

    private static readonly Color SecondaryText =
        Color.FromArgb(238, 238, 238);

    private static readonly Color SeparatorText =
        Color.FromArgb(145, 149, 158);

    private readonly Font _usageFont = new(
        "Segoe UI",
        11f,
        FontStyle.Bold,
        GraphicsUnit.Point);

    private readonly Font _countdownFont = new(
        "Segoe UI",
        10f,
        FontStyle.Regular,
        GraphicsUnit.Point);

    private readonly System.Windows.Forms.Timer _countdownTimer;

    private UsageSnapshot? _snapshot;
    private string _usageText = string.Empty;
    private string _countdownText = string.Empty;

    public HoverDetailsForm()
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = PopupBackground;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        DoubleBuffered = true;
        Opacity = 0.97d;

        _countdownTimer = new System.Windows.Forms.Timer
        {
            Interval = 1000
        };

        _countdownTimer.Tick += (_, _) => UpdateDisplayText();

        UpdateDisplayText();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WsExToolWindow = 0x00000080;
            const int WsExTransparent = 0x00000020;
            const int WsExNoActivate = 0x08000000;

            CreateParams parameters = base.CreateParams;
            parameters.ExStyle |=
                WsExToolWindow
                | WsExTransparent
                | WsExNoActivate;

            return parameters;
        }
    }

    public void SetSnapshot(UsageSnapshot? snapshot)
    {
        _snapshot = snapshot;
        UpdateDisplayText();
    }

    public void ShowNear(Point anchor)
    {
        UpdateDisplayText();
        PositionNearTaskbar(anchor);

        if (!Visible)
        {
            Show();
        }

        _countdownTimer.Start();
    }

    public void HideDetails()
    {
        _countdownTimer.Stop();

        if (Visible)
        {
            Hide();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint =
            System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        const TextFormatFlags flags =
            TextFormatFlags.NoPadding
            | TextFormatFlags.SingleLine
            | TextFormatFlags.VerticalCenter;

        Size usageSize = TextRenderer.MeasureText(
            e.Graphics,
            _usageText,
            _usageFont,
            Size.Empty,
            flags);

        Size separatorSize = TextRenderer.MeasureText(
            e.Graphics,
            " | ",
            _countdownFont,
            Size.Empty,
            flags);

        Size countdownSize = TextRenderer.MeasureText(
            e.Graphics,
            _countdownText,
            _countdownFont,
            Size.Empty,
            flags);

        int x = HorizontalPadding;

        TextRenderer.DrawText(
            e.Graphics,
            _usageText,
            _usageFont,
            new Rectangle(
                x,
                0,
                usageSize.Width,
                ClientSize.Height),
            ClaudeOrange,
            flags);

        x += usageSize.Width;

        TextRenderer.DrawText(
            e.Graphics,
            " | ",
            _countdownFont,
            new Rectangle(
                x,
                0,
                separatorSize.Width,
                ClientSize.Height),
            SeparatorText,
            flags);

        x += separatorSize.Width;

        TextRenderer.DrawText(
            e.Graphics,
            _countdownText,
            _countdownFont,
            new Rectangle(
                x,
                0,
                countdownSize.Width,
                ClientSize.Height),
            SecondaryText,
            flags);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        ApplyRoundedRegion();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _countdownTimer.Dispose();
            _usageFont.Dispose();
            _countdownFont.Dispose();
        }

        base.Dispose(disposing);
    }

    private void UpdateDisplayText()
    {
        string usageText = _snapshot is null
            ? "--%"
            : $"{_snapshot.UsagePercentage:0.#}%";

        string countdownText = FormatResetCountdown(_snapshot?.ResetAt);

        if (string.Equals(_usageText, usageText, StringComparison.Ordinal)
            && string.Equals(
                _countdownText,
                countdownText,
                StringComparison.Ordinal))
        {
            return;
        }

        _usageText = usageText;
        _countdownText = countdownText;

        RecalculateSize();
        Invalidate();
    }

    private static string FormatResetCountdown(DateTimeOffset? resetAt)
    {
        if (resetAt is null)
        {
            return "Reset unavailable";
        }

        TimeSpan remaining = resetAt.Value.ToLocalTime() - DateTimeOffset.Now;

        if (remaining <= TimeSpan.Zero)
        {
            return "Reset due";
        }

        int totalMinutes = Math.Max(
            1,
            (int)Math.Ceiling(remaining.TotalMinutes));

        int totalHours = totalMinutes / 60;
        int minutes = totalMinutes % 60;

        return totalHours > 0
            ? $"Reset in {totalHours}h {minutes:D2}m"
            : $"Reset in {minutes}m";
    }

    private void RecalculateSize()
    {
        const TextFormatFlags flags =
            TextFormatFlags.NoPadding
            | TextFormatFlags.SingleLine;

        Size usageSize = TextRenderer.MeasureText(
            _usageText,
            _usageFont,
            Size.Empty,
            flags);

        Size separatorSize = TextRenderer.MeasureText(
            " | ",
            _countdownFont,
            Size.Empty,
            flags);

        Size countdownSize = TextRenderer.MeasureText(
            _countdownText,
            _countdownFont,
            Size.Empty,
            flags);

        int contentWidth =
            usageSize.Width
            + separatorSize.Width
            + countdownSize.Width;

        int contentHeight = Math.Max(
            usageSize.Height,
            Math.Max(separatorSize.Height, countdownSize.Height));

        Size = new Size(
            Math.Max(150, contentWidth + (HorizontalPadding * 2)),
            Math.Max(38, contentHeight + (VerticalPadding * 2)));
    }

    private void PositionNearTaskbar(Point anchor)
    {
        Screen screen = Screen.FromPoint(anchor);
        Rectangle bounds = screen.Bounds;
        Rectangle working = screen.WorkingArea;

        int x;
        int y;

        bool taskbarAtBottom =
            working.Bottom < bounds.Bottom
            && anchor.Y >= working.Bottom;

        bool taskbarAtTop =
            working.Top > bounds.Top
            && anchor.Y <= working.Top;

        bool taskbarAtLeft =
            working.Left > bounds.Left
            && anchor.X <= working.Left;

        bool taskbarAtRight =
            working.Right < bounds.Right
            && anchor.X >= working.Right;

        if (taskbarAtBottom)
        {
            x = anchor.X - (Width / 2);
            y = working.Bottom - Height - ScreenGap;
        }
        else if (taskbarAtTop)
        {
            x = anchor.X - (Width / 2);
            y = working.Top + ScreenGap;
        }
        else if (taskbarAtLeft)
        {
            x = working.Left + ScreenGap;
            y = anchor.Y - (Height / 2);
        }
        else if (taskbarAtRight)
        {
            x = working.Right - Width - ScreenGap;
            y = anchor.Y - (Height / 2);
        }
        else
        {
            x = anchor.X - (Width / 2);
            y = anchor.Y - Height - 14;
        }

        x = Math.Clamp(
            x,
            working.Left + ScreenGap,
            Math.Max(
                working.Left + ScreenGap,
                working.Right - Width - ScreenGap));

        y = Math.Clamp(
            y,
            working.Top + ScreenGap,
            Math.Max(
                working.Top + ScreenGap,
                working.Bottom - Height - ScreenGap));

        Location = new Point(x, y);
    }

    private void ApplyRoundedRegion()
    {
        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        int diameter = CornerRadius * 2;

        using var path = new GraphicsPath();

        path.AddArc(
            0,
            0,
            diameter,
            diameter,
            180,
            90);

        path.AddArc(
            Width - diameter,
            0,
            diameter,
            diameter,
            270,
            90);

        path.AddArc(
            Width - diameter,
            Height - diameter,
            diameter,
            diameter,
            0,
            90);

        path.AddArc(
            0,
            Height - diameter,
            diameter,
            diameter,
            90,
            90);

        path.CloseFigure();

        Region? previous = Region;
        Region = new Region(path);
        previous?.Dispose();
    }
}
