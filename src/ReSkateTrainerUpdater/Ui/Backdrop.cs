using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ReSkateTrainerUpdater.Ui;

/// <summary>A window that paints a backdrop which its see-through child controls draw over.</summary>
internal interface IBackdropHost
{
    /// <summary>Paints the part of the backdrop that lies under a child control at <paramref name="childBounds"/>, at (0, 0) of <paramref name="g"/>.</summary>
    void DrawBackdrop(Graphics g, Rectangle childBounds);
}

/// <summary>
/// The picture behind the window. The launcher has a photo here and lets it show faintly through its cards. That photo is
/// game artwork that isn't ours to ship, so this is an original stand-in painted in code: a dusk sky, a warm glow on
/// the horizon, layers of hills fading into haze, darker edges where the text sits, and a little film grain.
/// It is drawn once at the window's size and cached.
/// </summary>
internal static class Backdrop
{
    public static Bitmap Create(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);

        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Sky(g, width, height);
            Hills(g, width, height);
            Shade(g, width, height);
        }

        AddGrain(bitmap);
        return bitmap;
    }

    // A dusk sky: deep blue overhead, lighter toward the horizon, with a warm glow where the sun has just gone.
    private static void Sky(Graphics g, int width, int height)
    {
        var rect = new Rectangle(0, 0, width, height);
        using (var sky = new LinearGradientBrush(Rectangle.Inflate(rect, 2, 2), Color.Black, Color.Black, LinearGradientMode.Vertical))
        {
            sky.InterpolationColors = new ColorBlend
            {
                Colors = new[] { Color.FromArgb(24, 36, 58), Color.FromArgb(48, 72, 108), Color.FromArgb(88, 112, 140), Color.FromArgb(40, 52, 70) },
                Positions = new[] { 0f, 0.38f, 0.58f, 1f },
            };
            g.FillRectangle(sky, rect);
        }

        Glow(g, width * 0.60f, height * 0.56f, width * 0.46f, height * 0.34f, Color.FromArgb(255, 176, 108), 120);
        Glow(g, width * 0.60f, height * 0.56f, width * 0.20f, height * 0.16f, Color.FromArgb(255, 214, 160), 110);
        Glow(g, width * 0.86f, height * 0.92f, width * 0.32f, height * 0.30f, Color.FromArgb(1, 131, 255), 60);
    }

    // Four ridges, from far (pale and bluish) to near (dark), each with a band of haze above it.
    private static void Hills(Graphics g, int width, int height)
    {
        var layers = new (float Base, float Amp, Color Top, Color Bottom, int Haze)[]
        {
            (0.60f, 0.045f, Color.FromArgb(86, 104, 132), Color.FromArgb(58, 72, 96), 70),
            (0.67f, 0.060f, Color.FromArgb(58, 74, 100), Color.FromArgb(36, 46, 66), 70),
            (0.76f, 0.075f, Color.FromArgb(34, 46, 66), Color.FromArgb(20, 27, 40), 60),
            (0.88f, 0.080f, Color.FromArgb(18, 25, 36), Color.FromArgb(9, 12, 18), 0),
        };

        var rough = new Random(3);
        for (int i = 0; i < layers.Length; i++)
        {
            var (baseline, amplitude, top, bottom, haze) = layers[i];
            float p1 = (float)(rough.NextDouble() * 6.28), p2 = (float)(rough.NextDouble() * 6.28), p3 = (float)(rough.NextDouble() * 6.28);
            float f1 = 2.2f + i * 0.5f, f2 = 5.1f + i * 0.9f, f3 = 11f + i * 1.7f;

            var points = new List<PointF>();
            for (int x = 0; x <= width; x += 3)
            {
                float t = x / (float)width;
                float ridge = 0.60f * MathF.Sin(t * f1 + p1) + 0.28f * MathF.Sin(t * f2 + p2) + 0.12f * MathF.Sin(t * f3 + p3);

                // Near the front the edge is ragged, like treetops.
                float ragged = i >= 2 ? (float)(rough.NextDouble() - 0.5) * height * 0.012f * (i - 1) : 0f;
                points.Add(new PointF(x, height * baseline + ridge * height * amplitude + ragged));
            }
            points.Add(new PointF(width, height));
            points.Add(new PointF(0, height));

            float lowest = points.Min(p => p.Y);
            using var fill = new LinearGradientBrush(new RectangleF(-2, lowest - 3, width + 4, height - lowest + 6), top, bottom, LinearGradientMode.Vertical);
            g.FillPolygon(fill, points.ToArray());

            // Haze: a soft pale band where this ridge meets the sky, which is what makes distant hills look distant.
            if (haze > 0)
            {
                var band = new RectangleF(0, lowest - height * 0.07f, width, height * 0.09f);
                using var mist = new LinearGradientBrush(RectangleF.Inflate(band, 2, 2), Color.FromArgb(0, 150, 170, 200), Color.FromArgb(haze, 150, 170, 200), LinearGradientMode.Vertical);
                var state = g.Save();
                using var clip = new GraphicsPath();
                clip.AddPolygon(points.ToArray());
                g.SetClip(clip);
                g.FillRectangle(mist, band);
                g.Restore(state);
            }
        }
    }

    // Darker edges, strongest on the left and top where the text sits, as in the launcher, so white text always reads.
    private static void Shade(Graphics g, int width, int height)
    {
        EdgeFade(g, new Rectangle(0, 0, (int)(width * 0.50f), height), LinearGradientMode.Horizontal, 190, 0);
        EdgeFade(g, new Rectangle(0, 0, width, (int)(height * 0.30f)), LinearGradientMode.Vertical, 150, 0);
        EdgeFade(g, new Rectangle(0, (int)(height * 0.72f), width, height - (int)(height * 0.72f)), LinearGradientMode.Vertical, 0, 120);
        EdgeFade(g, new Rectangle((int)(width * 0.80f), 0, width - (int)(width * 0.80f), height), LinearGradientMode.Horizontal, 0, 110);
    }

    // A soft round pool of light: bright in the middle, fading smoothly to nothing.
    private static void Glow(Graphics g, float cx, float cy, float rx, float ry, Color colour, int alpha)
    {
        using var path = new GraphicsPath();
        path.AddEllipse(cx - rx, cy - ry, rx * 2, ry * 2);
        using var brush = new PathGradientBrush(path)
        {
            CenterPoint = new PointF(cx, cy),
            InterpolationColors = new ColorBlend
            {
                Colors = new[] { Color.FromArgb(0, colour), Color.FromArgb(alpha / 4, colour), Color.FromArgb(alpha, colour) },
                Positions = new[] { 0f, 0.45f, 1f },
            },
        };
        g.FillPath(brush, path);
    }

    // Black that fades from one alpha to another across exactly this rectangle (so the gradient never tiles).
    private static void EdgeFade(Graphics g, Rectangle area, LinearGradientMode direction, int fromAlpha, int toAlpha)
    {
        if (area.Width <= 0 || area.Height <= 0) return;

        // GDI+ gives the first and last pixel row of a gradient the opposite end's colour, which shows as a hairline.
        // Making the brush's rectangle a pixel bigger than the area it fills keeps that row outside what is painted.
        using var brush = new LinearGradientBrush(Rectangle.Inflate(area, 2, 2), Color.FromArgb(fromAlpha, 0, 0, 0), Color.FromArgb(toAlpha, 0, 0, 0), direction);
        g.FillRectangle(brush, area);
    }

    // A faint film grain, so the gradients don't band on a dark screen.
    private static void AddGrain(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
        try
        {
            var bytes = new byte[Math.Abs(data.Stride) * data.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            var noise = new Random(7);
            for (int i = 0; i < bytes.Length; i += 4)
            {
                int n = noise.Next(-4, 5);
                bytes[i] = (byte)Math.Clamp(bytes[i] + n, 0, 255);
                bytes[i + 1] = (byte)Math.Clamp(bytes[i + 1] + n, 0, 255);
                bytes[i + 2] = (byte)Math.Clamp(bytes[i + 2] + n, 0, 255);
            }
            Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
