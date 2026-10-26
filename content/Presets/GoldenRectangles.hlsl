// Name: Golden Rectangles
// The golden spiral drawn in fine threads of light: squares within golden rectangles within squares, falling
// forever inward towards the spiral's eye, with pulses of light running down the curve in time with the music.
// Family: Geometric
// Tags: Calm, Minimal, Geometric
// Added: 2026-10-17 21:44

static const float GR_PHI = 1.61803399;
static const float GR_LOG_PHI = 0.48121183;
// The eye of the spiral, inside the base golden rectangle [0, phi] x [0, 1].
static const float2 GR_EYE = float2(1.17082039, 0.27639320);

// Distance to the edge of the unit square [0,1]^2.
float grSquare(float2 u)
{
    float2 d = abs(u - 0.5) - 0.5;
    return abs(length(max(d, 0.0)) + min(max(d.x, d.y), 0.0));
}

// Distance to the quarter circle of radius 1 about (1, 0), running from (0, 0) to (1, 1); along is 0..1 on it.
float grArc(float2 u, out float along)
{
    float2 v = u - float2(1.0, 0.0);
    float angle = atan2(v.y, v.x);
    along = saturate((PI - angle) / (PI * 0.5));
    if (angle >= PI * 0.5 && v.y >= 0.0)
        return abs(length(v) - 1.0);
    return min(length(u), length(u - float2(1.0, 1.0)));
}

float3 Render(float2 uv, float2 p)
{
    float px = pixelSize();

    // The whole figure is unchanged by a quarter turn and a shrink by phi about its eye, so zooming
    // by exactly that much each cycle loops seamlessly forever.
    float travel = Time * 0.055 + 0.04 * sin(Time * 0.11);
    float cycles = floor(travel);
    float s = travel - cycles;
    float shrink = pow(GR_PHI, -s);
    const float baseScale = 1.25;
    float2 eyeOnScreen = float2(0.18 * sin(Time * 0.031), 0.08 * cos(Time * 0.027));
    float2 q = rotate((p - eyeOnScreen) * baseScale * shrink, -s * PI * 0.5 + Time * 0.012);
    float pxQ = px * baseScale * shrink;

    float radius = max(length(q), 1e-6);
    int centreLevel = (int)round(-log(radius) / GR_LOG_PHI);

    float3 gold = float3(1.0, 0.78, 0.45);
    float3 ivory = float3(0.85, 0.88, 1.0);
    float3 colour = float3(0.008, 0.009, 0.02) + float3(0.02, 0.018, 0.04) * exp(-dot(p - eyeOnScreen, p - eyeOnScreen) * 2.0);
    float beatPhase = BeatCount - saturate(Beat);

    [loop]
    for (int k = centreLevel - 3; k <= centreLevel + 3; k++)
    {
        // Map this point into level k's copy of the base square.
        float grow = pow(GR_PHI, (float)k);
        float2 u = rotate(q * grow, k * PI * 0.5) + GR_EYE;
        float pxU = pxQ * grow;
        // Levels too small to see fade out smoothly instead of shimmering.
        float sizeInPixels = 1.0 / pxU;
        float visible = smoothstep(3.0, 14.0, sizeInPixels);
        if (visible <= 0.0)
            continue;

        float level = k + cycles;
        float band = spectrum(frac(level * 0.137) * 0.8 + 0.05);
        // On each beat a flash runs inward through the squares, one level at a time.
        float wave = exp(-pow(frac((level - beatPhase * 1.0) / 7.0) * 7.0 - 3.5, 2.0) * 1.2) * (0.3 + Beat * 0.7);

        float inside = (all(u >= 0.0) && all(u <= 1.0)) ? 1.0 : 0.0;
        colour += inside * float3(0.012, 0.011, 0.02) * (fmod(abs(level), 2.0) < 0.5 ? 1.0 : 0.4) * (1.0 + wave * 2.0);

        float edge = grSquare(u) / pxU;
        float frame = saturate(0.9 - edge * 0.9) + exp(-edge * 0.6) * 0.06;
        colour += ivory * frame * visible * (0.12 + band * 0.35 + wave * 0.3);

        float along = 0.0;
        float arc = grArc(u, along) / pxU;
        float curve = saturate(1.1 - arc * 0.8) + exp(-arc * 0.35) * 0.12;
        // Pulses of light spiralling inward along the curve.
        float spiralPos = level + along;
        float pulse = pow(0.5 + 0.5 * cos((spiralPos * 0.5 - Time * 0.9) * 2.0 * PI), 10.0);
        colour += gold * curve * visible * (0.4 + Mid * 0.4 + pulse * (0.6 + Bass * 0.8));
    }

    // Keep the brightest overlaps soft so they glow without hard sparkles.
    return min(colour, 1.5);
}
