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
        V   += Vel * dt;
    }

    public bool Rest(float pos, float vel) => MathF.Abs(T - V) < pos && MathF.Abs(Vel) < vel;
    public void Snap() { V = T; Vel = 0f; }
}

/// <summary>
/// The whole island as a small physical system.
///
///   Reach   - how far the liquid has been pulled out of the screen edge (0..1).
///   Tether  - thickness of the neck connecting the body to the edge. 1=attached, 0=snapped.
///   X,Y     - body centre chasing its target.
///   Wd, Ht  - body dimensions (Wd/Ht diverge during squash-stretch landing).
///   Rad     - corner radius of body.
///   Sway    - lateral pendulum offset of the drop (lags the cursor).
///   Sat*    - satellite bead spawned at pinch-off.
///
/// Phase 1 additions:
///   WdDraw / HtDraw  - actual draw dimensions (teardrop elongation applied).
///   Stretch          - 0..1 how elongated the hanging drop is (drives sigma, elongation).
///   PinchFactor      - 0..1 how far through the pre-pinch window we are.
///   SatActive, SatX, SatY, SatRad, SatVy - satellite bead state.
/// </summary>
internal sealed class Droplet
{
    public const float W = 500f, H = 260f, Cx = W / 2f;
    public const float PillW = 124f, PillH = 36f, PillY = 44f;
    public const float PanelW = 440f, PanelH = 200f, PanelTop = 18f, PanelR = 38f;

    // ---- feel constants (original public API; named for readability) ----
    const float EnterR         = 230f;  // cursor distance at which liquid starts to react
    const float DetachReach    = 0.78f; // reach threshold that allows detach
    const float DetachDist     = 30f;   // cursor proximity that triggers detach

    public Phase Phase { get; private set; } = Phase.Hidden;

    // Core springs (public, used by MainWindow and LiquidRenderer)
    public readonly Spring Reach  = new(0f);
    public readonly Spring Tether = new(1f);
    public readonly Spring X      = new(Cx);
    public readonly Spring Y      = new(0f);
    public readonly Spring Wd     = new(0f);
    public readonly Spring Ht     = new(0f);
    public readonly Spring Rad    = new(0f);

    // Phase 1 additions
    readonly Spring _sway     = new(0f);  // lateral pendulum of the drop
    readonly Spring _squashWd = new(1f);  // multiplier for Wd during squash (target=1 normally)
    readonly Spring _squashHt = new(1f);  // multiplier for Ht during squash (target=1 normally)

    // Satellite bead
    public bool  SatActive;
    public float SatX, SatY, SatRad, SatVy;

    // Pinch-off timing
    float _prePinchTimer;  // counts up during last ~PinchWindow seconds before Detach

    float _reachTarget, _swayTarget, _away;
    float _kp = 700f, _zp = 0.7f, _ks = 700f, _zs = 0.7f;

    // ---------- derived values consumed by LiquidRenderer ----------

    /// <summary>How elongated the hanging drop is, 0..1.</summary>
    public float Stretch
    {
        get
        {
            float restY = PillY;  // approximate rest position
            return Math.Clamp((Y.V - restY * 0.5f) / 40f, 0f, 1f);
        }
    }

    /// <summary>Actual draw width with squash applied.</summary>
    public float WdDraw => Wd.V * _squashWd.V;

    /// <summary>Actual draw height with squash applied.</summary>
    public float HtDraw => Ht.V * _squashHt.V;

    /// <summary>0=no pinch, 1=fully pinched (drives neck collapse in renderer).</summary>
    public float PinchFactor => Math.Clamp(_prePinchTimer / LiquidLook.PinchWindow, 0f, 1f);

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
        DistToRect(px, py, X.V, Y.V, WdDraw, HtDraw) <= inflate;

    public bool Settled =>
        Reach.Rest(0.002f, 0.02f)   && Tether.Rest(0.002f, 0.02f) &&
        X.Rest(0.05f, 0.6f)         && Y.Rest(0.05f, 0.6f)        &&
        Wd.Rest(0.05f, 0.6f)        && Ht.Rest(0.05f, 0.6f)       &&
        Rad.Rest(0.05f, 0.6f)       && _sway.Rest(0.05f, 0.6f)    &&
        _squashWd.Rest(0.002f, 0.02f) && _squashHt.Rest(0.002f, 0.02f) &&
        !SatActive;

    // ---------- commands ----------
    public void Expand()      { if (Phase == Phase.Pill)     { Phase = Phase.Expanded; _away = 0f; } }
    public void ForceExpand() {                                 Phase = Phase.Expanded; _away = 0f;   }
    public void Collapse()    { if (Phase == Phase.Expanded) { Phase = Phase.Pill;     _away = 0f; } }

    void Detach()
    {
        Phase = Phase.Pill;
        Y.Vel += 160f;             // drop falls with more energy (bigger drop)
        Reach.Vel += LiquidLook.RecoilKick;  // stub recoils back into the edge

        // Spawn satellite bead at the pinch point
        if (LiquidLook.Satellite)
        {
            SatActive = true;
            SatX = X.V;
            // Pinch point is at f=PinchF along the neck
            float ay = -6f;
            float hy = Y.V - HtDraw * 0.5f;
            SatY  = ay + (hy - ay) * LiquidLook.PinchF;
            SatRad = Lerp(LiquidLook.SatRadMin, LiquidLook.SatRadMax,
                         Math.Clamp(Reach.V, 0f, 1f));
            SatVy = 60f;  // initial downward velocity
        }

        _prePinchTimer = 0f;
        _away = 0f;
        // Squash-and-stretch landing: start with compressed height, wide width
        _squashWd.V   = 1.4f; _squashWd.Vel = 0f; _squashWd.T = 1f;
        _squashHt.V   = 0.65f; _squashHt.Vel = 0f; _squashHt.T = 1f;
    }

    // ---------- state machine ----------
    public void Update(float dt, float mx, float my, bool hold)
    {
        float dx = mx - Cx;
        float dy = MathF.Max(my, 0f);
        float d  = MathF.Sqrt(dx * dx + dy * dy);

        switch (Phase)
        {
            case Phase.Hidden:
                if (d < EnterR) Phase = Phase.Hanging;
                break;

            case Phase.Hanging:
            {
                float p = Math.Clamp(1f - d / EnterR, 0f, 1f);
                _reachTarget = MathF.Pow(Math.Clamp(p * 1.15f, 0f, 1f), 1.2f);
                _swayTarget  = Math.Clamp(dx * 0.28f, -20f, 20f);

                // Pre-pinch accumulation
                bool nearlyDetach = Reach.V > DetachReach;
                bool touching     = d < DetachDist || Dist(mx, my, X.V, Y.V) < HtDraw * 0.5f + 16f;

                if (nearlyDetach && touching)
                {
                    _prePinchTimer += dt;
                    if (_prePinchTimer >= LiquidLook.PinchWindow) Detach();
                }
                else
                {
                    _prePinchTimer = Math.Max(0f, _prePinchTimer - dt * 2f); // decay slowly
                    if (Reach.V > DetachReach && touching) Detach();
                }

                if (d > EnterR + 20f && Reach.V < 0.01f && MathF.Abs(Reach.Vel) < 0.05f)
                    Phase = Phase.Hidden;
                break;
            }

            case Phase.Pill:
                _away = DistToRect(mx, my, X.V, Y.V, WdDraw, HtDraw) > 110f ? _away + dt : 0f;
                if (_away > 0.5f && !hold) Phase = Phase.Retract;

                // Satellite bead: falls under gravity, merges when near pill
                if (SatActive)
                {
                    SatVy += LiquidLook.SatGravity * dt;
                    SatY  += SatVy * dt;
                    // Merge into pill when it's close enough
                    float satDist = Dist(SatX, SatY, X.V, Y.V);
                    if (satDist < HtDraw * 0.6f + SatRad)
                    {
                        SatActive = false;
                    }
                }
                break;

            case Phase.Expanded:
                _away = DistToRect(mx, my, X.V, Y.V, WdDraw, HtDraw) > 36f ? _away + dt : 0f;
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
                _sway.T = Phase == Phase.Hanging ? _swayTarget  : 0f;

                float xv  = Math.Clamp(Reach.V, -0.15f, 1.15f);
                float s   = Smooth(0.30f, 1.00f, xv);     // how formed the drop is
                float stretch = Stretch;

                // Bigger drop (22→34 DIP), teardrop elongation
                float dia   = Lerp(LiquidLook.DropDiaMin, LiquidLook.DropDiaMax, s);
                float k     = 1f + LiquidLook.DropElongK * stretch;
                float dropH = dia * MathF.Sqrt(k);
                float dropW = dia / MathF.Sqrt(k);

                float tip = Lerp(14f * Smooth(0f, 0.6f, xv), 60f, s);  // lower tip for bigger drop
                X.T   = Cx + _sway.V * s;
                Y.T   = tip - dropH * 0.5f;
                Wd.T  = dropW;
                Ht.T  = dropH;
                Rad.T = dropW * 0.5f;
                Tether.T  = 1f;
                _kp = 700f; _zp = 0.70f; _ks = 700f; _zs = 0.70f;
                _squashWd.T = 1f; _squashHt.T = 1f;
                break;
            }
            case Phase.Pill:
                Reach.T = 0f; Tether.T = 0f;
                X.T = Cx; Y.T = PillY; Wd.T = PillW; Ht.T = PillH; Rad.T = PillH * 0.5f;
                _kp = 240f; _zp = 0.42f; _ks = 320f; _zs = 0.55f;   // bouncy landing
                _sway.T = 0f;
                // Squash springs settle back to 1
                _squashWd.T = 1f; _squashHt.T = 1f;
                break;

            case Phase.Expanded:
                Reach.T = 0f; Tether.T = 0f;
                X.T = Cx; Y.T = PanelTop + PanelH * 0.5f; Wd.T = PanelW; Ht.T = PanelH; Rad.T = PanelR;
                _kp = 240f; _zp = 0.55f; _ks = 230f; _zs = 0.60f;
                _sway.T = 0f;
                _squashWd.T = 1f; _squashHt.T = 1f;
                break;

            case Phase.Retract:
                Reach.T = 0f; Tether.T = 1f;
                X.T = Cx; Y.T = -4f; Wd.T = 10f; Ht.T = 10f; Rad.T = 5f;
                _kp = 260f; _zp = 0.95f; _ks = 260f; _zs = 0.95f;
                _sway.T = 0f;
                _squashWd.T = 1f; _squashHt.T = 1f;
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
            _sway.Step(h, 90f, 0.55f);     // slower pendulum spring for natural lag
            _squashWd.Step(h, 380f, 0.38f); // springy squash
            _squashHt.Step(h, 380f, 0.38f);
        }
    }

    void SnapAll()
    {
        Reach.Snap(); Tether.Snap();
        X.Snap(); Y.Snap(); Wd.Snap(); Ht.Snap(); Rad.Snap();
        _sway.Snap(); _squashWd.Snap(); _squashHt.Snap();
    }
}
