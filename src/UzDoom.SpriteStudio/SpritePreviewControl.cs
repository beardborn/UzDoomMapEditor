using System.Drawing.Drawing2D;

namespace UzDoom.SpriteStudio;

internal sealed class SpritePreviewControl : Control
{
    private Bitmap? _image;

    public SpritePreviewControl()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(24, 26, 30);
        ResizeRedraw = true;
    }

    public int OriginX { get; private set; }
    public int OriginY { get; private set; }

    public void SetSprite(Bitmap? image, int originX = 0, int originY = 0)
    {
        _image?.Dispose();
        _image = image;
        OriginX = originX;
        OriginY = originY;
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _image?.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        DrawCheckerboard(e.Graphics, ClientRectangle);

        if (_image is null)
            return;

        const int margin = 32;
        var availableWidth = Math.Max(1, ClientSize.Width - margin * 2);
        var availableHeight = Math.Max(1, ClientSize.Height - margin * 2);
        var rawScale = Math.Min((float)availableWidth / _image.Width, (float)availableHeight / _image.Height);
        var scale = rawScale >= 1f ? Math.Max(1f, (float)Math.Floor(rawScale)) : rawScale;

        var drawWidth = Math.Max(1, (int)Math.Round(_image.Width * scale));
        var drawHeight = Math.Max(1, (int)Math.Round(_image.Height * scale));
        var left = (ClientSize.Width - drawWidth) / 2;
        var top = (ClientSize.Height - drawHeight) / 2;

        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        e.Graphics.DrawImage(_image, new Rectangle(left, top, drawWidth, drawHeight), 0, 0, _image.Width, _image.Height, GraphicsUnit.Pixel);

        using var originPen = new Pen(Color.FromArgb(190, 255, 120, 120), 1f);
        var originScreenX = left + OriginX * scale;
        var originScreenY = top + OriginY * scale;
        e.Graphics.DrawLine(originPen, originScreenX, top - 12, originScreenX, top + drawHeight + 12);
        e.Graphics.DrawLine(originPen, left - 12, originScreenY, left + drawWidth + 12, originScreenY);
    }

    private static void DrawCheckerboard(Graphics graphics, Rectangle area)
    {
        const int cell = 16;
        using var a = new SolidBrush(Color.FromArgb(33, 35, 40));
        using var b = new SolidBrush(Color.FromArgb(43, 46, 52));

        for (var y = 0; y < area.Height; y += cell)
        {
            for (var x = 0; x < area.Width; x += cell)
            {
                var alternate = ((x / cell) + (y / cell)) % 2 != 0;
                graphics.FillRectangle(alternate ? b : a, x, y, cell, cell);
            }
        }
    }
}
