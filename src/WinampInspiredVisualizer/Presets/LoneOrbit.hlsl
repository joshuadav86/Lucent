// Name: Lone Orbit
// A single bright star swings round a faint centre on a slowly turning ellipse, its long fading trail tracing a
// rosette in the dark; every beat gives it a push, so it races round faster when the music is busy.
// Family: Minimal
// Tags: Minimal, Space, Calm, Dark
// Added: 2026-10-17 21:37

static const float LoEccentricity = 0.62;
static const float LoAxis = 0.5;

// How far round the orbit the star is. Time drives it steadily and each beat adds a shove;
// the beat clock only ever moves forwards, so the star never jumps back.
float loPhase()
{
    return Time * 1.4 + max(BeatCount - Beat, 0.0) * 0.45;
}

// The star's position for an orbital phase: an ellipse with the centre at one focus, swept faster close in and
// slower far out (a cheap stand-in for Kepler's law), whose long axis slowly turns.
float2 loPosition(float phase)
{
    float e = LoEccentricity;
    float s1, c1, s2, c2;
    sincos(phase, s1, c1);
    sincos(2.0 * phase, s2, c2);
    float anomaly = phase + e * s1 + 0.5 * e * e * s2;
    float sa, ca;
    sincos(anomaly, sa, ca);
    float b = LoAxis * sqrt(1.0 - e * e);
    float2 orbit = float2(LoAxis * (ca - e), b * sa);
    // The ellipse precesses a little each lap, which draws the rosette.
    return rotate(orbit, phase * 0.12 + 0.4);
}

float loSegment(float2 p, float2 a, float2 b)
{
    float2 ab = b - a;
    float h = saturate(dot(p - a, ab) / max(dot(ab, ab), 1e-8));
    return length(p - a - ab * h);
}

float3 Render(float2 uv, float2 p)
{
    float px = pixelSize();
    float2 q = p * 0.95;

    // A dark backdrop with a faint glow at the centre the star orbits.
    float3 colour = float3(0.004, 0.005, 0.012) + float3(0.02, 0.025, 0.05) * exp(-length(q) * 2.0);
    float centre = exp(-length(q) / 0.012) * (0.25 + Bass * 0.15);
    colour += float3(0.6, 0.65, 0.9) * centre;

    // The trail: the last several laps, walked back from the star in short segments. Each point of the path
    // keeps its age, so the line dims and cools from gold to blue the further back it was drawn.
    float phase = loPhase();
    float laps = 6.0;
    int steps = qualitySteps(300);
    float stepSize = laps * 2.0 * PI / steps;
    float best = 0.0;
    float bestAge = 1.0;
    float width = 1.0 * px;
    float reachMargin = px * 12.0;

    // Every lap stays between the closest and farthest points of the ellipse from its focus (the
    // segments cut a hair inside at the closest point), so pixels outside that ring skip the walk.
    float radius = length(q);
    float nearest = LoAxis * (1.0 - LoEccentricity) - 0.02 - reachMargin;
    float farthest = LoAxis * (1.0 + LoEccentricity) + reachMargin;
    if (radius > nearest && radius < farthest)
    {
        // loPosition, unrolled for even steps back: the angles that move by a fixed amount each step
        // (phase, twice the phase, the slow turn of the ellipse) are turned on by a rotation rather than
        // worked out afresh, which leaves one sincos per step instead of four.
        float e = LoEccentricity;
        float b = LoAxis * sqrt(1.0 - e * e);
        float s1, c1, s2, c2, sTurn, cTurn;
        sincos(phase, s1, c1);
        sincos(2.0 * phase, s2, c2);
        sincos(phase * 0.12 + 0.4, sTurn, cTurn);
        float sStep, cStep, sStep2, cStep2, sTurnStep, cTurnStep;
        sincos(stepSize, sStep, cStep);
        sincos(2.0 * stepSize, sStep2, cStep2);
        sincos(stepSize * 0.12, sTurnStep, cTurnStep);
        float stepPhase = phase;

        float2 last = loPosition(phase);
        [loop]
        for (int i = 1; i <= steps; i++)
        {
            // Step every angle back by one step.
            float t = s1 * cStep - c1 * sStep;
            c1 = c1 * cStep + s1 * sStep;
            s1 = t;
            t = s2 * cStep2 - c2 * sStep2;
            c2 = c2 * cStep2 + s2 * sStep2;
            s2 = t;
            t = sTurn * cTurnStep - cTurn * sTurnStep;
            cTurn = cTurn * cTurnStep + sTurn * sTurnStep;
            sTurn = t;
            stepPhase -= stepSize;

            float sa, ca;
            sincos(stepPhase + e * s1 + 0.5 * e * e * s2, sa, ca);
            float2 orbit = float2(LoAxis * (ca - e), b * sa);
            float2 next = float2(cTurn * orbit.x - sTurn * orbit.y, sTurn * orbit.x + cTurn * orbit.y);

            // Cheap rejection: skip segments that are nowhere near this pixel.
            float2 mid = (last + next) * 0.5;
            float reach = length(next - last) * 0.5 + reachMargin;
            if (dot(q - mid, q - mid) < reach * reach)
            {
                float d = loSegment(q, last, next);
                float age = (i - 0.5) / steps;
                float strength = exp(-age * 3.2);
                float profile = (1.0 - smoothstep(width * 0.5, width * 1.6, d)) * 0.8 + exp(-d / (px * 3.0)) * 0.22;
                float value = strength * profile;
                if (value > best)
                {
                    best = value;
                    bestAge = age;
                }
            }
            last = next;
        }
    }
    float3 young = float3(1.0, 0.8, 0.55);
    float3 old = float3(0.35, 0.5, 1.0);
    float3 ink = lerp(young, old, saturate(bestAge * 2.2));
    colour += ink * best * (0.8 + Level * 0.4);

    // The star: a soft round glow with a modest peak, flaring a touch on the beat.
    float2 star = loPosition(phase);
    float toStar = length(q - star);
    colour += float3(1.0, 0.92, 0.8) * (exp(-toStar / (px * 2.5 + 0.004)) * (0.9 + Beat * 0.4)
            + exp(-toStar / 0.06) * (0.1 + Bass * 0.15));
    return colour;
}
