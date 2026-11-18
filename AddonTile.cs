using System;
using System.Drawing;
using System.Linq;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

internal sealed class AddonTile : Button
{
    private readonly Addon addon;
    private bool hovered;
    internal bool Selected;
    internal bool Dark;
    internal Color Accent;
    internal string Installed = "Select folder";
    internal string Release = "";
    internal string State = "";

    internal AddonTile(Addon addon)
    {
        this.addon = addon;
        Text = addon.Name;
        AccessibleName = addon.Name;
        Size = new Size(164, 164);
        Margin = new Padding(0, 0, 12, 12);
        FlatStyle = FlatStyle.Flat;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        hovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        hovered = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics canvas = e.Graphics;
        canvas.Clear(BackColor);
        using (var border = new Pen(Selected || hovered ? Accent : (Dark ? Color.FromArgb(73, 84, 103) :
            Color.FromArgb(197, 208, 224)), Selected ? 2 : 1))
            canvas.DrawRectangle(border, 1, 1, Width - 3, Height - 3);

        int artSize = Height / 2;
        var art = new Rectangle((Width - artSize) / 2, 12, artSize, artSize);
        DrawArtwork(canvas, art);

        var name = new Rectangle(8, art.Bottom + 5, Width - 16, Height - art.Bottom - 29);
        TextRenderer.DrawText(canvas, Text, Font, name, ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(canvas, State, Font, new Rectangle(8, Height - 26, Width - 16, 20),
            ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(canvas, new Rectangle(4, 4, Width - 8, Height - 8));
    }

    private void DrawArtwork(Graphics canvas, Rectangle bounds)
    {
        string key = (addon.Folder ?? "").ToLowerInvariant();
        Color start;
        Color end;
        switch (key)
        {
            case "characterplannerexport":
                start = Color.FromArgb(42, 79, 160);
                end = Color.FromArgb(89, 52, 153);
                break;
            case "ddmactionbarmanager":
                start = Color.FromArgb(147, 64, 37);
                end = Color.FromArgb(115, 42, 68);
                break;
            case "foreverapiexplorer":
                start = Color.FromArgb(12, 103, 110);
                end = Color.FromArgb(21, 66, 111);
                break;
            default:
                start = Color.FromArgb(73, 93, 129);
                end = Color.FromArgb(50, 62, 92);
                break;
        }

        GraphicsState previous = canvas.Save();
        try
        {
            canvas.TranslateTransform(bounds.X, bounds.Y);
            canvas.ScaleTransform(bounds.Width / 80f, bounds.Height / 80f);
            canvas.SmoothingMode = SmoothingMode.AntiAlias;
            using (var background = new LinearGradientBrush(new Rectangle(0, 0, 80, 80),
                start, end, LinearGradientMode.ForwardDiagonal))
            using (var ink = new Pen(Color.White, 2.5f) {
                StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round
            })
            {
                canvas.FillRectangle(background, 0, 0, 80, 80);
                switch (key)
                {
                    case "characterplannerexport":
                        canvas.DrawLines(ink, new[] { new Point(48, 21), new Point(23, 21),
                            new Point(23, 59), new Point(48, 59) });
                        canvas.DrawEllipse(ink, 33, 28, 10, 10);
                        canvas.DrawArc(ink, 29, 41, 18, 13, 190, 160);
                        canvas.DrawLines(ink, new[] { new Point(55, 40), new Point(62, 47),
                            new Point(55, 54) });
                        canvas.DrawLine(ink, 48, 47, 62, 47);
                        break;
                    case "ddmactionbarmanager":
                        for (int y = 0; y < 2; y++)
                            for (int x = 0; x < 3; x++)
                                canvas.DrawRectangle(ink, 19 + x * 15, 26 + y * 15, 11, 11);
                        canvas.FillRectangle(Brushes.White, 34, 27, 11, 9);
                        break;
                    case "foreverapiexplorer":
                        canvas.DrawEllipse(ink, 19, 19, 35, 35);
                        canvas.DrawLine(ink, 49, 49, 62, 62);
                        canvas.DrawLines(ink, new[] { new Point(33, 31), new Point(28, 36), new Point(33, 41) });
                        canvas.DrawLines(ink, new[] { new Point(40, 31), new Point(45, 36), new Point(40, 41) });
                        break;
                    default:
                        string initials = String.Concat(addon.Name.Split(new[] { ' ' },
                            StringSplitOptions.RemoveEmptyEntries).Take(2).Select(word => Char.ToUpperInvariant(word[0]).ToString()));
                        using (var format = new StringFormat { Alignment = StringAlignment.Center,
                            LineAlignment = StringAlignment.Center })
                        using (var font = new Font(Font.FontFamily, 22, FontStyle.Bold, GraphicsUnit.Pixel))
                            canvas.DrawString(initials, font, Brushes.White, new RectangleF(0, 0, 80, 80), format);
                        break;
                }
            }
        }
        finally { canvas.Restore(previous); }
    }
}
