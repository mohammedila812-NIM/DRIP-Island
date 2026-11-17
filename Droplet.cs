using System;

namespace IslandWin;

internal enum Phase { Hidden, Hanging, Pill, Expanded, Retract }

/// <summary>Damped spring. Low damping ratio = liquid wobble.</summary>
internal sealed class Spring
{
    public float V, Vel, T;
    public Spring(float v = 0f) { V = T = v; }

    public void Step(float dt, float k, float zeta)
    {
        float c = 2f * zeta * MathF.Sqrt(k);
        Vel += (k * (T - V) - c * Vel) * dt;
        V += Vel * dt;
    }

    public bool Rest(float pos, float vel) => MathF.Abs(T - V) < pos && MathF.Abs(Vel) < vel;
    public void Snap() { V = T; Vel = 0f; }
}

/// <summary>
/// The whole island as a small physical system.
///
///   Reach   - how far the liquid has been pulled out of the screen edge (0..1). Drives the
///             bulge, the neck and the drop. Very under-damped, so it wobbles like a liquid.
///   Tether  - thickness of the neck connecting the body to the edge. 1 = attached, 0 = snapped.
///   X,Y,Wd,Ht,Rad - the "body" (circle -> pill -> panel), each chasing its target by spring.
///
/// Coordinates are DIPs, origin = top-left of the window, y = 0 is the screen edge.
/// </summary>
internal sealed class Droplet
{
    public const float W = 500f, H = 260f, Cx = W / 2f;
    public const float PillW = 124f, PillH = 36f, PillY = 44f;
    public const float PanelW = 440f, PanelH = 200f, PanelTop = 18f, PanelR = 38f;
    const float EnterR = 230f;                 // cursor distance at which the liquid starts to react

    public Phase Phase { get; private set; } = Phase.Hidden;

    public readonly Spring Reach = new(0f), Tether = new(1f);
    public readonly Spring X = new(Cx), Y = new(0f), Wd = new(0f), Ht = new(0f), Rad = new(0f);

    float _reachTarget, _sway, _away;
    float _kp = 700f, _zp = 0.7f, _ks = 700f, _zs = 0.7f;

    // ---------- helpers ----------
    public static float Smooth(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
    public static float Lerp(float a, float b, float t) => a + (b - a) * t;
    static float Dist(float x1, float y1, float x2, float y2)
    {
        float dx = x1 - x2, dy = y1 - y2;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
    static float DistToRect(float px, float py, float cx, float cy, float w, float h)
    {
        float dx = MathF.Max(MathF.Abs(px - cx) - w * 0.5f, 0f);
        float dy = MathF.Max(MathF.Abs(py - cy) - h * 0.5f, 0f);
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    public bool Contains(float px, float py, float inflate) =>
        DistToRect(px, py, X.V, Y.V, Wd.V, Ht.V) <= inflate;

    public bool Settled =>
        Reach.Rest(0.002f, 0.02f) && Tether.Rest(0.002f, 0.02f) &&
        X.Rest(0.05f, 0.6f) && Y.Rest(0.05f, 0.6f) &&
        Wd.Rest(0.05f, 0.6f) && Ht.Rest(0.05f, 0.6f) && Rad.Rest(0.05f, 0.6f);

    // ---------- commands ----------
    public void Expand() { if (Phase == Phase.Pill) { Phase = Phase.Expanded; _away = 0f; } }
    public void ForceExpand() { Phase = Phase.Expanded; _away = 0f; }
    public void Collapse() { if (Phase == Phase.Expanded) { Phase = Phase.Pill; _away = 0f; } }

    void Detach()
    {
        Phase = Phase.Pill;      // the drop lets go of the edge
        Y.Vel += 140f;           // and falls
        _away = 0f;
    }

    // ---------- state machine ----------
    public void Update(float dt, float mx, float my, bool hold)
    {
        float dx = mx - Cx, dy = MathF.Max(my, 0f);
        float d = MathF.Sqrt(dx * dx + dy * dy);       // distance cursor -> anchor point on the edge

        switch (Phase)
        {
            case Phase.Hidden:
                if (d < EnterR) Phase = Phase.Hanging;
                break;

            case Phase.Hanging:
            {
                float p = Math.Clamp(1f - d / EnterR, 0f, 1f);
                _reachTarget = MathF.Pow(Math.Clamp(p * 1.15f, 0f, 1f), 1.2f);
                _sway = Math.Clamp(dx * 0.3f, -16f, 16f);

                bool touching = d < 30f || Dist(mx, my, X.V, Y.V) < Ht.V * 0.5f + 16f;
                if (Reach.V > 0.8f && touching) Detach();
                else if (d > EnterR + 20f && Reach.V < 0.01f && MathF.Abs(Reach.Vel) < 0.05f) Phase = Phase.Hidden;
                break;
            }

            case Phase.Pill:
                _away = DistToRect(mx, my, X.V, Y.V, Wd.V, Ht.V) > 110f ? _away + dt : 0f;
                if (_away > 0.5f && !hold) Phase = Phase.Retract;
                break;

            case Phase.Expanded:
                _away = DistToRect(mx, my, X.V, Y.V, Wd.V, Ht.V) > 36f ? _away + dt : 0f;
                if (_away > 0.8f && !hold) { Phase = Phase.Pill; _away = 0f; }
                break;

            case Phase.Retract:
                if (Wd.V < 16f && Y.V < 3f) Phase = d < EnterR ? Phase.Hanging : Phase.Hidden;
                break;
        }
    }

    void Retarget()
    {
        switch (Phase)
        {
            case Phase.Hidden:
            case Phase.Hanging:
            {
                Reach.T = Phase == Phase.Hanging ? _reachTarget : 0f;
                float x = Math.Clamp(Reach.V, -0.15f, 1.15f);
                float s = Smooth(0.30f, 1.00f, x);                         // how formed the drop is
                float dia = 22f * s;
                float tip = Lerp(14f * Smooth(0f, 0.6f, x), 52f, s);       // lowest point below the edge
                X.T = Cx + _sway * s;
                Y.T = tip - dia * 0.5f;
                Wd.T = dia; Ht.T = dia; Rad.T = dia * 0.5f;
                Tether.T = 1f;
                _kp = 700f; _zp = 0.70f; _ks = 700f; _zs = 0.70f;
                break;
            }
            case Phase.Pill:
                Reach.T = 0f; Tether.T = 0f;
                X.T = Cx; Y.T = PillY; Wd.T = PillW; Ht.T = PillH; Rad.T = PillH * 0.5f;
                _kp = 240f; _zp = 0.42f; _ks = 320f; _zs = 0.55f;          // bouncy landing
                break;
            case Phase.Expanded:
                Reach.T = 0f; Tether.T = 0f;
                X.T = Cx; Y.T = PanelTop + PanelH * 0.5f; Wd.T = PanelW; Ht.T = PanelH; Rad.T = PanelR;
                _kp = 240f; _zp = 0.55f; _ks = 230f; _zs = 0.60f;
                break;
            case Phase.Retract:
                Reach.T = 0f; Tether.T = 1f;
                X.T = Cx; Y.T = -4f; Wd.T = 10f; Ht.T = 10f; Rad.T = 5f;
                _kp = 260f; _zp = 0.95f; _ks = 260f; _zs = 0.95f;
                break;
        }
    }

    public void Step(float dt)
    {
        Retarget();
        if (Settled) { SnapAll(); return; }

        int n = Math.Max(1, (int)MathF.Ceiling(dt * 240f));
        float h = dt / n;
        for (int i = 0; i < n; i++)
        {
            Reach.Step(h, 170f, 0.38f);
            Tether.Step(h, 300f, 1.0f);
            X.Step(h, _kp, _zp); Y.Step(h, _kp, _zp);
            Wd.Step(h, _ks, _zs); Ht.Step(h, _ks, _zs); Rad.Step(h, _ks, _zs);
        }
    }

    void SnapAll()
    {
        Reach.Snap(); Tether.Snap(); X.Snap(); Y.Snap(); Wd.Snap(); Ht.Snap(); Rad.Snap();
    }
}
