// version 7
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ClaudeUsageTray;

internal static class IconFactory
{
    private const int IconSize = 64;
    private const string ClaudeIconResourceName = "ClaudeUsageTray.Assets.claude.ico";

    private static readonly Color ClaudeOrange = Color.FromArgb(217, 119, 87);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    public static Icon CreateClaudeLogoIcon()
    {
        Assembly assembly = typeof(IconFactory).Assembly;

        using Stream? stream = assembly.GetManifestResourceStream(ClaudeIconResourceName);
        if (stream is not null)
        {
            using var sourceIcon = new Icon(stream, new Size(IconSize, IconSize));
            return (Icon)sourceIcon.Clone();
        }

        return CreateFallbackClaudeIcon();
    }

    public static Icon CreatePercentageIcon(decimal percentage, int fontSize)
    {
        int rounded = Math.Clamp(
            (int)Math.Round(percentage, MidpointRounding.AwayFromZero),
            0,
            999);

        string value = string.Concat(
            rounded.ToString(CultureInfo.InvariantCulture),
            "%");

        return CreateFittedTextIcon(value, fontSize, ClaudeOrange);
    }

    public static Icon CreateUnavailablePercentageIcon(int fontSize)
    {
        return CreateFittedTextIcon("--%", fontSize, ClaudeOrange);
    }

    public static Icon CreateSpinnerIcon(int frame)
    {
        using var bitmap = CreateTransparentBitmap(out Graphics graphics);

        using (graphics)
        {
            const int spokeCount = 12;
            const float centre = IconSize / 2f;
            const float innerRadius = 13f;
            const float outerRadius = 30f;

            for (int spoke = 0; spoke < spokeCount; spoke++)
            {
                int distanceFromLead = (spoke - frame + spokeCount) % spokeCount;
                int alpha = Math.Clamp(255 - (distanceFromLead * 18), 55, 255);
                double angle = (Math.PI * 2d * spoke / spokeCount) - (Math.PI / 2d);

                float startX = centre + (float)Math.Cos(angle) * innerRadius;
                float startY = centre + (float)Math.Sin(angle) * innerRadius;
                float endX = centre + (float)Math.Cos(angle) * outerRadius;
                float endY = centre + (float)Math.Sin(angle) * outerRadius;

                using var pen = new Pen(Color.FromArgb(alpha, ClaudeOrange), 7f)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round
                };

                graphics.DrawLine(pen, startX, startY, endX, endY);
            }
        }

        return ConvertBitmapToIcon(bitmap);
    }

    private static Icon CreateFittedTextIcon(string text, int fontSize, Color textColor)
    {
        using var bitmap = CreateTransparentBitmap(out Graphics graphics);

        using (graphics)
        using (var fontFamily = new FontFamily("Segoe UI"))
        using (var textPath = new GraphicsPath())
        using (var textBrush = new SolidBrush(textColor))
        using (var format = (StringFormat)StringFormat.GenericTypographic.Clone())
        {
            format.FormatFlags |= StringFormatFlags.NoWrap;

            textPath.AddString(
                text,
                fontFamily,
                (int)FontStyle.Bold,
                100f,
                PointF.Empty,
                format);

            RectangleF sourceBounds = textPath.GetBounds();
            if (sourceBounds.Width <= 0f || sourceBounds.Height <= 0f)
            {
                return ConvertBitmapToIcon(bitmap);
            }

            float targetHeight = Math.Clamp(fontSize, 24, 62);
            float targetWidth = IconSize - 1f;
            float targetLeft = (IconSize - targetWidth) / 2f;
            float targetTop = (IconSize - targetHeight) / 2f;

            // Independent scaling deliberately condenses the percentage horizontally.
            // This keeps all digits and the % symbol readable in Windows' square icon slot.
            float scaleX = targetWidth / sourceBounds.Width;
            float scaleY = targetHeight / sourceBounds.Height;

            using var transform = new Matrix(
                scaleX,
                0f,
                0f,
                scaleY,
                targetLeft - (sourceBounds.Left * scaleX),
                targetTop - (sourceBounds.Top * scaleY));

            textPath.Transform(transform);
            graphics.FillPath(textBrush, textPath);
        }

        return ConvertBitmapToIcon(bitmap);
    }

    private static Icon CreateFallbackClaudeIcon()
    {
        using var bitmap = CreateTransparentBitmap(out Graphics graphics);

        using (graphics)
        {
            const int spokeCount = 14;
            const float centre = IconSize / 2f;
            const float innerRadius = 3f;
            const float outerRadius = 28f;

            for (int spoke = 0; spoke < spokeCount; spoke++)
            {
                double angle = Math.PI * 2d * spoke / spokeCount;

                float startX = centre + (float)Math.Cos(angle) * innerRadius;
                float startY = centre + (float)Math.Sin(angle) * innerRadius;
                float endX = centre + (float)Math.Cos(angle) * outerRadius;
                float endY = centre + (float)Math.Sin(angle) * outerRadius;

                using var pen = new Pen(ClaudeOrange, 7f)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round
                };

                graphics.DrawLine(pen, startX, startY, endX, endY);
            }
        }

        return ConvertBitmapToIcon(bitmap);
    }

    private static Bitmap CreateTransparentBitmap(out Graphics graphics)
    {
        var bitmap = new Bitmap(
            IconSize,
            IconSize,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.Clear(Color.Transparent);

        return bitmap;
    }

    private static Icon ConvertBitmapToIcon(Bitmap bitmap)
    {
        IntPtr iconHandle = bitmap.GetHicon();

        try
        {
            using Icon icon = Icon.FromHandle(iconHandle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(iconHandle);
        }
    }
}
