using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace ZapretVN
{
    // Иконка-щит с буквой Z: для трея, окна и exe
    static class Shield
    {
        public static readonly Color On = Color.FromArgb(34, 163, 90);
        public static readonly Color Off = Color.FromArgb(138, 143, 152);
        public static readonly Color Busy = Color.FromArgb(224, 160, 32);

        public static Bitmap Draw(int s, Color c)
        {
            var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                float m = s <= 20 ? 0.5f : s * 0.05f;
                float w = s;
                using (var p = new GraphicsPath())
                {
                    p.AddLine(w / 2, m, w - m, m + s * 0.15f);
                    p.AddLine(w - m, m + s * 0.15f, w - m, s * 0.48f);
                    p.AddBezier(w - m, s * 0.48f, w - m, s * 0.74f, w * 0.74f, s * 0.88f, w / 2, s - m);
                    p.AddBezier(w / 2, s - m, w * 0.26f, s * 0.88f, m, s * 0.74f, m, s * 0.48f);
                    p.AddLine(m, s * 0.48f, m, m + s * 0.15f);
                    p.CloseFigure();
                    using (var br = new LinearGradientBrush(new RectangleF(0, 0, s, s), Mix(c, Color.White, 0.25f), Mix(c, Color.Black, 0.15f), 90f))
                        g.FillPath(br, p);
                    if (s >= 32)
                        using (var pen = new Pen(Mix(c, Color.Black, 0.3f), s / 40f))
                            g.DrawPath(pen, p);
                }
                using (var f = new Font("Segoe UI", s * 0.56f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString("Z", f, Brushes.White, new RectangleF(0, -s * 0.03f, s, s * 0.98f), sf);
            }
            return bmp;
        }

        static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }
    }
}
