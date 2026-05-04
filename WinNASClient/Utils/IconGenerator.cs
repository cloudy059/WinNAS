using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace WinNASClient.Utils;

public static class IconGenerator
{
    public static Icon CreateAppIcon()
    {
        using var bmp = CreateIconBitmap(256);
        return Icon.FromHandle(bmp.GetHicon());
    }

    public static void SaveIcon(string path)
    {
        var sizes = new[] { 16, 32, 48, 64, 128, 256 };
        using var stream = new FileStream(path, FileMode.Create);
        using var writer = new BinaryWriter(stream);

        writer.Write((short)0);
        writer.Write((short)1);
        writer.Write((short)sizes.Length);

        var imageDataList = new List<byte[]>();
        var offset = 6 + sizes.Length * 16;

        foreach (var size in sizes)
        {
            using var bmp = CreateIconBitmap(size);
            var imageData = BitmapToPng(bmp);
            imageDataList.Add(imageData);

            writer.Write((byte)size);
            writer.Write((byte)size);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((short)1);
            writer.Write((short)32);
            writer.Write(imageData.Length);
            writer.Write(offset);

            offset += imageData.Length;
        }

        foreach (var data in imageDataList)
        {
            writer.Write(data);
        }
    }

    private static Bitmap CreateIconBitmap(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.Clear(Color.Transparent);

        var margin = size * 0.05f;
        var cornerRadius = size * 0.15f;
        var rect = new RectangleF(margin, margin, size - 2 * margin, size - 2 * margin);

        using (var bgPath = CreateRoundedRect(rect, cornerRadius))
        {
            using var bgBrush = new LinearGradientBrush(
                rect,
                Color.FromArgb(55, 71, 133),
                Color.FromArgb(118, 75, 162),
                LinearGradientMode.ForwardDiagonal);
            g.FillPath(bgBrush, bgPath);
        }

        var nasMargin = size * 0.18f;
        var nasWidth = size - 2 * nasMargin;
        var nasHeight = nasWidth * 0.7f;
        var nasX = nasMargin;
        var nasY = size * 0.5f - nasHeight * 0.5f;
        var nasRect = new RectangleF(nasX, nasY, nasWidth, nasHeight);
        var nasCornerRadius = size * 0.06f;

        using (var nasPath = CreateRoundedRect(nasRect, nasCornerRadius))
        {
            using var nasBrush = new LinearGradientBrush(
                nasRect,
                Color.FromArgb(240, 240, 245),
                Color.FromArgb(220, 220, 230),
                LinearGradientMode.Vertical);
            g.FillPath(nasBrush, nasPath);

            using var nasBorder = new Pen(Color.FromArgb(180, 180, 200), size * 0.01f);
            g.DrawPath(nasBorder, nasPath);
        }

        var slotMargin = nasWidth * 0.1f;
        var slotHeight = nasHeight * 0.12f;
        var slotY = nasY + nasHeight * 0.2f;
        var slotWidth = nasWidth - 2 * slotMargin;

        for (int i = 0; i < 3; i++)
        {
            var sy = slotY + i * (slotHeight + nasHeight * 0.08f);
            var slotRect = new RectangleF(nasX + slotMargin, sy, slotWidth, slotHeight);
            using var slotPath = CreateRoundedRect(slotRect, size * 0.015f);
            using var slotBrush = new LinearGradientBrush(
                slotRect,
                Color.FromArgb(70, 130, 220),
                Color.FromArgb(50, 110, 200),
                LinearGradientMode.Horizontal);
            g.FillPath(slotBrush, slotPath);

            var ledX = nasX + slotMargin + slotWidth * 0.85f;
            var ledY = sy + slotHeight * 0.5f;
            var ledR = size * 0.012f;
            using var ledBrush = new SolidBrush(Color.FromArgb(80, 230, 80));
            g.FillEllipse(ledBrush, ledX - ledR, ledY - ledR, ledR * 2, ledR * 2);
        }

        var cloudX = nasX + nasWidth * 0.65f;
        var cloudY = nasY - nasHeight * 0.35f;
        var cloudSize = nasWidth * 0.35f;
        DrawCloud(g, cloudX, cloudY, cloudSize);

        return bmp;
    }

    private static void DrawCloud(Graphics g, float x, float y, float size)
    {
        using var cloudPath = new GraphicsPath();
        var r = size * 0.2f;

        cloudPath.AddEllipse(x + size * 0.15f, y, size * 0.4f, size * 0.4f);
        cloudPath.AddEllipse(x + size * 0.4f, y - size * 0.05f, size * 0.35f, size * 0.35f);
        cloudPath.AddEllipse(x, y + size * 0.15f, size * 0.35f, size * 0.3f);
        cloudPath.AddEllipse(x + size * 0.55f, y + size * 0.1f, size * 0.3f, size * 0.3f);
        cloudPath.AddRectangle(new RectangleF(x + size * 0.05f, y + size * 0.25f, size * 0.75f, size * 0.2f));

        using var cloudBrush = new SolidBrush(Color.FromArgb(230, 240, 255));
        g.FillPath(cloudBrush, cloudPath);

        using var cloudPen = new Pen(Color.FromArgb(180, 200, 240), size * 0.02f);
        g.DrawPath(cloudPen, cloudPath);
    }

    private static GraphicsPath CreateRoundedRect(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        var r = Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2);

        path.AddArc(rect.X, rect.Y, r * 2, r * 2, 180, 90);
        path.AddArc(rect.X + rect.Width - r * 2, rect.Y, r * 2, r * 2, 270, 90);
        path.AddArc(rect.X + rect.Width - r * 2, rect.Y + rect.Height - r * 2, r * 2, r * 2, 0, 90);
        path.AddArc(rect.X, rect.Y + rect.Height - r * 2, r * 2, r * 2, 90, 90);
        path.CloseFigure();

        return path;
    }

    private static byte[] BitmapToPng(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }
}
