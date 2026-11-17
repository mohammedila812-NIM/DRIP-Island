using System;
using SkiaSharp;

namespace IslandWin;

/// <summary>
/// Draws the island as a metaball field: shapes are drawn, blurred, then alpha-thresholded.
/// Where two shapes come close, their blurred alphas add up past the threshold and they
/// fuse into one surface - which is what gives the neck, the fillets against the screen edge
/// and the snap when the drop lets go.
///
/// Rendering happens on an off-screen surface that has extra padding ABOVE the screen edge,
/// so the "edge" can be modelled as a strip of liquid that the bulge merges into.
/// </summary>
internal sealed class LiquidRenderer : IDisposable
{
    const float Pad = 48f;       // DIPs of padding above y = 0
    const float Sigma = 4.5f;    // blur radius of the metaball field (smaller = tighter, thinner necks)

    SKSurface _surface;
    int _sw, _sh;
    readonly SKImageFilter _mainFx, _rimFx;
    readonly SKPaint _shape = new() { IsAntialias = true, Style = SKPaintStyle.Fill };

    public LiquidRenderer()
    {
        _mainFx = SKImageFilter.CreateColorFilter(Threshold(0.50f), SKImageFilter.CreateBlur(Sigma, Sigma));
        _rimFx  = SKImageFilter.CreateColorFilter(Threshold(0.38f), SKImageFilter.CreateBlur(Sigma, Sigma));
    }

    /// <summary>Alpha-only lookup table: a steep ramp around the threshold.</summary>
    static SKColorFilter Threshold(float t)
    {
        var a = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            float v = (i / 255f - t) * 10f + 0.5f;
            a[i] = (byte)(Math.Clamp(v, 0f, 1f) * 255f);
        }
        return SKColorFilter.CreateTable(a, null, null, null);
    }

    public void Render(SKCanvas target, SKImageInfo info, float scale, Droplet d)
    {
        int padPx = (int)MathF.Ceiling(Pad * scale);
        int h = info.Height + padPx;
        if (_surface == null || _sw != info.Width || _sh != h)
        {
            _surface?.Dispose();
            _surface = SKSurface.Create(new SKImageInfo(info.Width, h, info.ColorType, SKAlphaType.Premul));
            _sw = info.Width; _sh = h;
        }

        var c = _surface.Canvas;
        c.Clear(SKColors.Transparent);
        c.Save();
        c.Translate(0, padPx);
        c.Scale(scale);

        Pass(c, d, _rimFx, 70, SKColors.White);     // faint 1px rim so the island reads on dark wallpapers
        Pass(c, d, _mainFx, 255, SKColors.Black);   // the island itself, pure black like Apple's

        c.Restore();

        target.ResetMatrix();
        target.Clear(SKColors.Transparent);
        using var img = _surface.Snapshot();
        target.DrawImage(img, 0, -padPx);
    }

    void Pass(SKCanvas c, Droplet d, SKImageFilter fx, byte layerAlpha, SKColor color)
    {
        using var layer = new SKPaint { ImageFilter = fx, Color = new SKColor(255, 255, 255, layerAlpha) };
        c.SaveLayer(layer);
        _shape.Color = color;
        DrawShapes(c, d, _shape);
        c.Restore();
    }

    static void DrawShapes(SKCanvas c, Droplet d, SKPaint paint)
    {
        // 1. The screen edge as a strip of liquid (its lower edge sits just above the visible area).
        c.DrawRect(SKRect.Create(-150f, -Pad - 10f, Droplet.W + 300f, Pad + 3f), paint);

        // 2. The reservoir: the bulge that is pulled out of the edge.
        float x = Math.Clamp(d.Reach.V, -0.15f, 1.15f);
        float depth = 17f * Droplet.Smooth(0f, 0.65f, x);
        float rx = Droplet.Lerp(40f, 22f, Droplet.Smooth(0.45f, 1f, x));
        c.DrawOval(Droplet.Cx, -7f, rx, 3f + depth, paint);

        // 3. The body (drop -> pill -> panel) and the neck holding it to the edge.
        float bw = MathF.Max(0f, d.Wd.V), bh = MathF.Max(0f, d.Ht.V);
        if (bw < 1f || bh < 1f) return;

        float corner = MathF.Min(MathF.Max(d.Rad.V, 0f), MathF.Min(bw, bh) * 0.5f);
        c.DrawRoundRect(SKRect.Create(d.X.V - bw * 0.5f, d.Y.V - bh * 0.5f, bw, bh), corner, corner, paint);

        float tether = d.Tether.V;
        if (tether <= 0.08f) return;

        float nr = 7f * tether
                 * (1f - 0.45f * Droplet.Smooth(0.5f, 1f, MathF.Max(x, 0f)))   // neck thins as it stretches
                 * Droplet.Smooth(3f, 12f, MathF.Min(bw, bh))                   // no neck without a head
                 * Droplet.Smooth(2f, 16f, d.Y.V);                              // neck dissolves into the edge
        if (nr < 1f) return;

        float ay = -3f, hx = d.X.V, hy = d.Y.V;
        float len = MathF.Sqrt((hx - Droplet.Cx) * (hx - Droplet.Cx) + (hy - ay) * (hy - ay));
        int n = Math.Clamp((int)(len / 4f), 3, 24);
        for (int i = 0; i <= n; i++)
        {
            float f = i / (float)n;
            float px = Droplet.Cx + (hx - Droplet.Cx) * f * f;     // bends toward the head
            float py = ay + (hy - ay) * f;
            float waist = 0.85f + 0.30f * (2f * f - 1f) * (2f * f - 1f);
            c.DrawCircle(px, py, nr * waist, paint);
        }
    }

    public void Dispose()
    {
        _surface?.Dispose();
        _mainFx.Dispose();
        _rimFx.Dispose();
        _shape.Dispose();
    }
}
