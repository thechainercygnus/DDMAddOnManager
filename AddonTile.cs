using System;
using System.Drawing;
using System.Linq;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

internal class RoundedButton : Button
{
    private bool hovered;
    internal Color BorderColor;
    internal Color HoverColor;
    internal Color DisabledSurface;
    internal Color DisabledText;

    internal RoundedButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
    }

    internal static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        int diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
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

    protected void DrawSurface(Graphics canvas, Color border, int thickness)
    {
        canvas.Clear(Parent == null ? SystemColors.Control : Parent.BackColor);
        SmoothingMode previous = canvas.SmoothingMode;
        canvas.SmoothingMode = SmoothingMode.AntiAlias;
        int radius = Math.Max(2, (int)Math.Round(4 * canvas.DpiX / 96));
        using (var shape = RoundedRectangle(new Rectangle(1, 1, Width - 3, Height - 3), radius))
        using (var fill = new SolidBrush(!Enabled ? DisabledSurface : hovered ? HoverColor : BackColor))
        using (var outline = new Pen(border, thickness))
        {
            canvas.FillPath(fill, shape);
            canvas.DrawPath(outline, shape);
        }
        canvas.SmoothingMode = previous;
    }

    protected void DrawKeyboardFocus(Graphics canvas)
    {
        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(canvas, new Rectangle(5, 5, Width - 10, Height - 10),
                ForeColor, BackColor);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        DrawSurface(e.Graphics, BorderColor, 1);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? ForeColor : DisabledText,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        DrawKeyboardFocus(e.Graphics);
    }
}


internal sealed class AddonTile : RoundedButton
{
    private readonly Addon addon;
    internal bool Selected;
    internal Color SecondaryText;
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

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics canvas = e.Graphics;
        DrawSurface(canvas, Selected ? Accent : BorderColor, Selected ? 2 : 1);
        if (Selected)
        {
            using (var check = new Pen(Accent, 2))
                canvas.DrawLines(check, new[] { new Point(Width - 25, 18),
                    new Point(Width - 21, 22), new Point(Width - 14, 14) });
        }

        int artSize = Height / 2;
        var art = new Rectangle((Width - artSize) / 2, 12, artSize, artSize);
        DrawArtwork(canvas, art);

        var name = new Rectangle(8, art.Bottom + 5, Width - 16, Height - art.Bottom - 29);
        TextRenderer.DrawText(canvas, Text, Font, name, ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(canvas, State, Font, new Rectangle(8, Height - 26, Width - 16, 20),
            SecondaryText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        DrawKeyboardFocus(canvas);
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
                using (var shape = RoundedRectangle(new Rectangle(0, 0, 80, 80), 4))
                    canvas.FillPath(background, shape);
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
