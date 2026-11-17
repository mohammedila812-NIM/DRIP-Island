using System;
using System.Collections.Generic;
using System.IO;
using SkiaSharp;

namespace IslandWin;

public static class Snapshot
{
    const int BaseW = 1000;
    const int BaseH = 520;
    const int DualW = BaseW * 2;
    const int DualH = BaseH;

    public static void Run(string outDir)
    {
        Log.Info($"Snapshot.Run starting, output directory: {outDir}");
        Directory.CreateDirectory(outDir);

        var frames = new List<(string Name, SKImage Image)>();

        using var renderer = new LiquidRenderer();
        float dt = 1f / 240f;

        // 1. hanging_reach_{20,40,60,80,100}.png
        int[] reaches = { 20, 40, 60, 80, 100 };
        foreach (int r in reaches)
        {
            var sim = new Droplet();
            float targetR = r / 100f;
            float p = MathF.Pow(targetR, 1f / 1.2f) / 1.15f;
            p = Math.Clamp(p, 0f, 1f);
            float d = (1f - p) * 230f;
            if (r == 100) d = 30.1f; // Prevent touching detachment

            float mx = Droplet.Cx + d;
            float my = 0f;

            int maxSteps = (int)(3.0f / dt);
            for (int s = 0; s < maxSteps; s++)
            {
                sim.Update(dt, mx, my, false);
                sim.Step(dt);
                if (s > 20 && sim.Settled) break;
            }

            string name = $"hanging_reach_{r}";
            var img = RenderDualFrame(sim, renderer);
            SavePng(img, Path.Combine(outDir, $"{name}.png"));
            frames.Add((name, img));
            Log.Info($"Generated snapshot {name}.png (Reach: {sim.Reach.V:F2}, Phase: {sim.Phase})");
        }

        // 2. detach_{00..12}.png every 30 ms starting at the frame Detach() fires
        {
            var sim = new Droplet();
            // Pull down until Reach > 0.85
            while (sim.Reach.V < 0.85f)
            {
                sim.Update(dt, Droplet.Cx, 40f, false);
                sim.Step(dt);
            }

            // Move cursor to touch the drop and trigger Detach()
            sim.Update(dt, Droplet.Cx, 15f, false);
            sim.Step(dt);

            for (int i = 0; i <= 12; i++)
            {
                string name = $"detach_{i:D2}";
                var img = RenderDualFrame(sim, renderer);
                SavePng(img, Path.Combine(outDir, $"{name}.png"));
                frames.Add(($"{name} ({sim.Phase})", img));
                Log.Info($"Generated snapshot {name}.png (Phase: {sim.Phase}, Y: {sim.Y.V:F1})");

                // Step 30 ms
                int subSteps = (int)MathF.Round(0.030f / dt);
                for (int s = 0; s < subSteps; s++)
                {
                    sim.Update(dt, Droplet.Cx, 15f, false);
                    sim.Step(dt);
                }
            }

            // 3. pill.png, expanded.png (settled)
            // Settle to Pill
            int pillSteps = (int)(3.0f / dt);
            for (int s = 0; s < pillSteps; s++)
            {
                sim.Update(dt, Droplet.Cx, Droplet.PillY, false);
                sim.Step(dt);
                if (sim.Settled && sim.Phase == Phase.Pill) break;
            }
            {
                string name = "pill";
                var img = RenderDualFrame(sim, renderer);
                SavePng(img, Path.Combine(outDir, $"{name}.png"));
                frames.Add((name, img));
                Log.Info($"Generated snapshot {name}.png (Phase: {sim.Phase})");
            }

            // Settle to Expanded
            sim.Expand();
            int expSteps = (int)(3.0f / dt);
            for (int s = 0; s < expSteps; s++)
            {
                sim.Update(dt, Droplet.Cx, Droplet.PanelTop + Droplet.PanelH * 0.5f, false);
                sim.Step(dt);
                if (sim.Settled && sim.Phase == Phase.Expanded) break;
            }
            {
                string name = "expanded";
                var img = RenderDualFrame(sim, renderer);
                SavePng(img, Path.Combine(outDir, $"{name}.png"));
                frames.Add((name, img));
                Log.Info($"Generated snapshot {name}.png (Phase: {sim.Phase})");
            }
        }

        // 4. Contact sheet sheet.png
        GenerateContactSheet(frames, Path.Combine(outDir, "sheet.png"));

        // Dispose snapshots
        foreach (var (_, img) in frames)
            img.Dispose();

        Log.Info($"Snapshot.Run completed successfully. Total frames: {frames.Count}");
    }

    static SKImage RenderDualFrame(Droplet sim, LiquidRenderer renderer)
    {
        using var liquidSurface = SKSurface.Create(new SKImageInfo(BaseW, BaseH, SKColorType.Rgba8888, SKAlphaType.Premul));
        var lc = liquidSurface.Canvas;
        lc.Clear(SKColors.Transparent);
        renderer.Render(lc, new SKImageInfo(BaseW, BaseH), 2f, sim);
        using var liquidImg = liquidSurface.Snapshot();

        var dualInfo = new SKImageInfo(DualW, DualH, SKColorType.Rgba8888, SKAlphaType.Premul);
        var dualSurface = SKSurface.Create(dualInfo);
        var dc = dualSurface.Canvas;

        // Dark background (#0B0B0D)
        using var darkPaint = new SKPaint { Color = SKColor.Parse("#0B0B0D"), Style = SKPaintStyle.Fill };
        dc.DrawRect(SKRect.Create(0, 0, BaseW, BaseH), darkPaint);
        dc.DrawImage(liquidImg, 0, 0);

        // Light background (#D9D9DE)
        using var lightPaint = new SKPaint { Color = SKColor.Parse("#D9D9DE"), Style = SKPaintStyle.Fill };
        dc.DrawRect(SKRect.Create(BaseW, 0, BaseW, BaseH), lightPaint);
        dc.DrawImage(liquidImg, BaseW, 0);

        // Subtle center separator line
        using var divPaint = new SKPaint { Color = new SKColor(128, 128, 128, 60), StrokeWidth = 1f };
        dc.DrawLine(BaseW, 0, BaseW, BaseH, divPaint);

        return dualSurface.Snapshot();
    }

    static void GenerateContactSheet(List<(string Name, SKImage Image)> frames, string outPath)
    {
        int cols = 4;
        int rows = (int)MathF.Ceiling(frames.Count / (float)cols);

        int tileW = 560;
        int tileH = (int)(tileW * (DualH / (float)DualW)); // aspect ratio 2000x520 -> 560x145.6 -> 146
        int labelH = 26;
        int margin = 16;
        int pad = 24;

        int sheetW = pad * 2 + cols * tileW + (cols - 1) * margin;
        int sheetH = pad * 2 + rows * (tileH + labelH) + (rows - 1) * margin;

        using var sheetSurface = SKSurface.Create(new SKImageInfo(sheetW, sheetH, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = sheetSurface.Canvas;

        // Canvas background
        using var bgPaint = new SKPaint { Color = SKColor.Parse("#161618"), Style = SKPaintStyle.Fill };
        canvas.DrawRect(SKRect.Create(0, 0, sheetW, sheetH), bgPaint);

        using var labelPaint = new SKPaint
        {
            Color = SKColors.White,
            TextSize = 13f,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Arial") ?? SKTypeface.Default
        };

        using var borderPaint = new SKPaint
        {
            Color = new SKColor(255, 255, 255, 30),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f
        };

        for (int i = 0; i < frames.Count; i++)
        {
            int c = i % cols;
            int r = i / cols;

            int x = pad + c * (tileW + margin);
            int y = pad + r * (tileH + labelH + margin);

            // Draw label
            canvas.DrawText(frames[i].Name, x + 2, y + 16, labelPaint);

            // Draw tile image
            var dstRect = SKRect.Create(x, y + labelH, tileW, tileH);
            canvas.DrawImage(frames[i].Image, dstRect);
            canvas.DrawRect(dstRect, borderPaint);
        }

        using var img = sheetSurface.Snapshot();
        SavePng(img, outPath);
        Log.Info($"Generated contact sheet: {outPath} ({sheetW}x{sheetH})");
    }

    static void SavePng(SKImage img, string path)
    {
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }
}
