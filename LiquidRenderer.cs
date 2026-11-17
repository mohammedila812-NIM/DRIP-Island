using System;
using SkiaSharp;

namespace IslandWin;

/// <summary>
/// All tunable liquid-look constants live here.
/// Phase 3 will migrate these into AppSettings/Tuning.
/// </summary>
internal static class LiquidLook
{
    // ---- blur / field ----
    public const float SigmaMin   = 6.0f;   // blur at zero stretch
    public const float SigmaMax   = 8.0f;   // blur at full stretch
    public const float SigmaStep  = 0.5f;   // quantisation step for filter caching
    public const float Threshold  = 0.50f;  // alpha threshold for the black island
    public const float FieldScale = 1.0f;   // keep full-res (half-res path noted in plan, left for Phase 3)

    // ---- reservoir ----
    public const float ResRxMin   = 40f;    // reservoir half-width at Reach=0
    public const float ResRxMax   = 70f;    // reservoir half-width at Reach=1
    public const float ResDepthMax= 24f;    // how far the reservoir bulges below the edge
    public const float ResCy      = -6f;    // y-centre of reservoir oval

    // ---- neck profile  r(f) = rMin + (rBase-rMin)*(1-f)^p ----
    public const float NeckRBaseMin = 18f;  // radius at screen edge, Reach=0
    public const float NeckRBaseMax = 34f;  // radius at screen edge, Reach=1
    public const float NeckRMinLo   = 11f;  // thinnest waist, low stretch
    public const float NeckRMinHi   = 3.5f; // thinnest waist, full stretch
    public const float NeckP        = 2.6f; // profile exponent
    public const float NeckStep     = 2.5f; // sample spacing in DIPs (≤3 as required)
    public const float NeckBlurComp = 0.35f;// radius += sigma*comp for thin parts
    public const float NeckRMinClamp= 2.5f; // hard minimum radius

    // ---- drop ----
    public const float DropDiaMin   = 22f;  // diameter at zero reach (original)
    public const float DropDiaMax   = 34f;  // diameter at full reach
    public const float DropElongK   = 0.35f;// elongation: k = 1 + DropElongK * stretch

    // ---- pinch-off ----
    public const float PinchWindow  = 0.12f;// seconds before Detach where pinch starts
    public const float PinchF       = 0.75f;// fractional position along neck that collapses
    public const float PinchRMin    = 1.0f; // neck radius at pinch point
    public const float RecoilKick   = -40f; // velocity added to Reach at Detach (retract stub)

    // ---- satellite bead ----
    public const bool  Satellite    = true;
    public const float SatRadMin    = 3f;
    public const float SatRadMax    = 5f;
    public const float SatGravity   = 280f; // pixels/s² downward

    // ---- rim ----
    public const float RimSigmaX   = 0.8f;
    public const float RimSigmaY   = 0.8f;
    public const byte  RimAlpha    = 50;
}

public enum RimMode { Off, Subtle, Strong }

/// <summary>
/// Draws the island as a metaball field: shapes are drawn, blurred, then alpha-thresholded.
/// Phase 1: wider meniscus, flared neck, variable sigma, rim removed by default.
/// </summary>
internal sealed class LiquidRenderer : IDisposable
{
    const float Pad = 60f;   // DIPs of padding above y=0 (increased for wider reservoir)

    // Filter cache keyed by quantised sigma (int = sigma/SigmaStep)
    readonly System.Collections.Generic.Dictionary<int, SKImageFilter> _filterCache = new();

    readonly SKPaint _shape = new() { IsAntialias = true, Style = SKPaintStyle.Fill };

    SKSurface _surface;
    int _sw, _sh;

    public static RimMode Rim { get; set; } = RimMode.Off;

    // ------------------------------------------------------------------ filter cache
    SKImageFilter GetMainFilter(float sigma)
    {
        int key = (int)MathF.Round(sigma / LiquidLook.SigmaStep);
        if (!_filterCache.TryGetValue(key, out var fx))
        {
            float s = key * LiquidLook.SigmaStep;
            fx = SKImageFilter.CreateColorFilter(Threshold(LiquidLook.Threshold),
                     SKImageFilter.CreateBlur(s, s));
            _filterCache[key] = fx;
        }
        return fx;
    }

    static SKColorFilter Threshold(float t)
    {
        var a = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            float v = (i / 255f - t) * 12f + 0.5f;   // slightly steeper ramp for crisper edge
            a[i] = (byte)(Math.Clamp(v, 0f, 1f) * 255f);
        }
        return SKColorFilter.CreateTable(a, null, null, null);
    }

    // ------------------------------------------------------------------ render
    public void Render(SKCanvas target, SKImageInfo info, float scale, Droplet d)
    {
        // When the island is completely hidden, do not draw anything to ensure zero leakage below y=0
        if (d.Phase == Phase.Hidden && d.Reach.V <= 0.01f && d.WdDraw <= 1f)
        {
            target.ResetMatrix();
            target.Clear(SKColors.Transparent);
            return;
        }

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

        float stretch = d.Stretch;
        float sigma   = LiquidLook.SigmaMin + (LiquidLook.SigmaMax - LiquidLook.SigmaMin) * stretch;
        var   mainFx  = GetMainFilter(sigma);

        // Optional rim: a subtly offset white copy behind the black island
        if (Rim != RimMode.Off)
        {
            byte rimAlpha = Rim == RimMode.Strong ? (byte)80 : LiquidLook.RimAlpha;
            var rimFilter = SKImageFilter.CreateColorFilter(
                Threshold(LiquidLook.Threshold - 0.04f),
                SKImageFilter.CreateBlur(sigma, sigma));
            using var rimLayer = new SKPaint
            {
                ImageFilter = rimFilter,
                Color = new SKColor(255, 255, 255, rimAlpha)
            };
            c.SaveLayer(rimLayer);
            _shape.Color = SKColors.White;
            DrawShapes(c, d, _shape, sigma);
            c.Restore();
            rimFilter.Dispose();
        }

        // Main black island
        using var mainLayer = new SKPaint { ImageFilter = mainFx, Color = new SKColor(255, 255, 255, 255) };
        c.SaveLayer(mainLayer);
        _shape.Color = SKColors.Black;
        DrawShapes(c, d, _shape, sigma);
        c.Restore();

        c.Restore();

        target.ResetMatrix();
        target.Clear(SKColors.Transparent);
        using var img = _surface.Snapshot();
        target.DrawImage(img, 0, -padPx);
    }

    // ------------------------------------------------------------------ shapes
    static void DrawShapes(SKCanvas c, Droplet d, SKPaint paint, float sigma)
    {
        float x = Math.Clamp(d.Reach.V, -0.15f, 1.15f);

        // 1. Screen-edge strip (liquid reservoir above y=0).
        // Sits at y <= -14 DIPs so its blurred edge never leaks below y=0 on its own.
        c.DrawRect(SKRect.Create(-200f, -Pad - 10f, Droplet.W + 400f, Pad - 4f), paint);

        // 2. Reservoir bulge — emerges only as Reach grows
        float rSmooth = Droplet.Smooth(0.04f, 0.75f, x);
        float resBottom = -14f;
        if (rSmooth > 0.001f)
        {
            float depth = LiquidLook.ResDepthMax * rSmooth;
            float rx = Droplet.Lerp(LiquidLook.ResRxMin, LiquidLook.ResRxMax,
                                    Droplet.Smooth(0.3f, 1f, x));
            resBottom = -14f + (14f + depth) * rSmooth;
            float ry = 6f + depth * 0.5f;
            float cy = resBottom - ry;
            c.DrawOval(Droplet.Cx, cy, rx, ry, paint);
        }

        // 3. Body (drop → pill → panel)
        float bw = MathF.Max(0f, d.WdDraw);
        float bh = MathF.Max(0f, d.HtDraw);
        if (bw < 1f || bh < 1f) return;

        float corner = MathF.Min(MathF.Max(d.Rad.V, 0f), MathF.Min(bw, bh) * 0.5f);
        c.DrawRoundRect(SKRect.Create(d.X.V - bw * 0.5f, d.Y.V - bh * 0.5f, bw, bh),
                        corner, corner, paint);

        float tether = d.Tether.V;
        if (tether <= 0.06f) return;

        // 4. Flared, tapered neck profile
        // rBase (at screen edge) and rMin (at drop) driven by stretch
        float stretch  = d.Stretch;
        float rBase    = Droplet.Lerp(LiquidLook.NeckRBaseMin, LiquidLook.NeckRBaseMax, x);
        float rMin     = Droplet.Lerp(LiquidLook.NeckRMinLo, LiquidLook.NeckRMinHi, stretch);
        rMin           = MathF.Max(rMin, LiquidLook.NeckRMinClamp);

        float ay = resBottom - 2f; // neck starts near bottom of reservoir
        float hx = d.X.V, hy = d.Y.V - bh * 0.5f;  // neck ends at top of drop body
        float len = MathF.Sqrt((hx - Droplet.Cx) * (hx - Droplet.Cx) + (hy - ay) * (hy - ay));
        if (len < 1f) return;

        // pinch factor: near Detach, collapse neck at f≈PinchF
        float pinchFactor = d.PinchFactor;  // 0=no pinch, 1=fully pinched

        int n = Math.Clamp((int)MathF.Ceiling(len / LiquidLook.NeckStep), 2, 60);
        for (int i = 0; i <= n; i++)
        {
            float f  = i / (float)n;
            float px = Droplet.Cx + (hx - Droplet.Cx) * f * f;   // quadratic bend
            float py = ay + (hy - ay) * f;

            // Profile: r(f) = rMin + (rBase - rMin) * (1-f)^p
            float profile = rMin + (rBase - rMin) * MathF.Pow(1f - f, LiquidLook.NeckP);
            profile = profile * tether;

            // Blur compensation: thin parts shrink more under blur, so inflate slightly
            float comp = sigma * LiquidLook.NeckBlurComp * (1f - f);
            profile += comp;
            profile = MathF.Max(profile, LiquidLook.NeckRMinClamp);

            // Apply pinch collapse at f≈PinchF
            if (pinchFactor > 0f)
            {
                float pinchDist = MathF.Abs(f - LiquidLook.PinchF);
                float pinchLocal = MathF.Max(0f, 1f - pinchDist * 8f);  // narrow bell around pinchF
                profile = Droplet.Lerp(profile, LiquidLook.PinchRMin, pinchFactor * pinchLocal);
            }

            if (profile < 0.5f) continue;
            c.DrawCircle(px, py, profile, paint);
        }

        // 5. Satellite bead (if active)
        if (LiquidLook.Satellite && d.SatActive)
        {
            float sr = d.SatRad;
            if (sr > 0.5f)
                c.DrawCircle(d.SatX, d.SatY, sr + sigma * 0.2f, paint);
        }
    }

    // ------------------------------------------------------------------ IDisposable
    public void Dispose()
    {
        _surface?.Dispose();
        _shape.Dispose();
        foreach (var fx in _filterCache.Values)
            fx.Dispose();
        _filterCache.Clear();
    }
}
