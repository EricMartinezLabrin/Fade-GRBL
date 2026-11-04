using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace LaserGRBL.UserControls
{
    internal sealed class FadeBrandHeader : Control
    {
        private readonly Bitmap logo;
        private readonly Rectangle logoContent;
        private string project = Layering.LayerText.Get("DesignUntitled");
        private string status = Layering.LayerText.Get("DesignDisconnected");
        private bool connected, alarm;

        public FadeBrandHeader()
        {
            DoubleBuffered = true; Height = 56; Dock = DockStyle.Fill;
            using (Stream stream = typeof(FadeBrandHeader).Assembly.GetManifestResourceStream("LaserGRBL.Grafica.FadeTechLogo.png"))
            using (Image image = Image.FromStream(stream)) logo = new Bitmap(image);
            int left = logo.Width, top = logo.Height, right = 0, bottom = 0;
            for (int y = 0; y < logo.Height; y++)
                for (int x = 0; x < logo.Width; x++)
                {
                    Color color = logo.GetPixel(x, y);
                    if (color.A > 0 && (color.R < 235 || color.G < 235 || color.B < 235))
                    { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y); }
                }
            logoContent = Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
            AccessibleName = "FadeGRBL";
        }
        public void SetProject(string filename)
        { string value = string.IsNullOrEmpty(filename) ? Layering.LayerText.Get("DesignUntitled") : Path.GetFileName(filename); if (project != value) { project = value; Invalidate(); } }
        public void SetStatus(string value, bool isConnected, bool isAlarm)
        { if (value != status || connected != isConnected || alarm != isAlarm) { status = value; connected = isConnected; alarm = isAlarm; Invalidate(); } }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); var g = e.Graphics; g.Clear(ColorScheme.FormBackColor);
            float scale = g.DpiX / 96f;
            int logoWidth = (int)(150 * scale), logoHeight = Math.Max(1, (int)(logoWidth * logoContent.Height / (double)logoContent.Width));
            Rectangle destination = new Rectangle((int)(12 * scale), (Height - logoHeight) / 2, logoWidth, logoHeight);
            using (var white = new SolidBrush(Color.White)) g.FillRectangle(white, Rectangle.Inflate(destination, 4, 4));
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(logo, destination, logoContent, GraphicsUnit.Pixel);
            int titleX = destination.Right + (int)(20 * scale), badgeWidth = Math.Min((int)(230 * scale), Width / 3);
            TextRenderer.DrawText(g, "FadeGRBL", FadeTheme.HeadingFont, new Rectangle(titleX, 0, (int)(110 * scale), Height), ColorScheme.FormForeColor, TextFormatFlags.VerticalCenter);
            int projectX = titleX + (int)(120 * scale);
            TextRenderer.DrawText(g, project, FadeTheme.BodyFont, new Rectangle(projectX, 0, Math.Max(0, Width - projectX - badgeWidth - 16), Height), ColorScheme.TextSecondary,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            Rectangle badge = new Rectangle(Width - badgeWidth - 12, 10, badgeWidth, Math.Max(24, Height - 20));
            using (var background = new SolidBrush(ColorScheme.SurfaceSubtle)) g.FillRectangle(background, badge);
            Color stateColor = alarm ? ColorScheme.PreviewCommandKO : connected ? ColorScheme.PreviewCommandOK : ColorScheme.TextSecondary;
            using (var dot = new SolidBrush(stateColor)) g.FillEllipse(dot, badge.Left + 10, Height / 2 - 4, 8, 8);
            TextRenderer.DrawText(g, status, FadeTheme.CaptionFont, new Rectangle(badge.Left + 26, badge.Top, badge.Width - 30, badge.Height), ColorScheme.FormForeColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            using (var border = new Pen(ColorScheme.BorderSubtle)) g.DrawLine(border, 0, Height - 1, Width, Height - 1);
        }
        protected override void Dispose(bool disposing) { if (disposing) logo.Dispose(); base.Dispose(disposing); }
    }
}
