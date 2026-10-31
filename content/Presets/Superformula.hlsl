// Name: Superformula
// One glowing neon outline drawn by Gielis's superformula, the single equation behind starfish, flowers, shells and crystals,
// nested inside itself in a dozen echoes that twist into a spiral. Every few bars it grows a new number of points and
// melts into its next shape; each echo plays its own band of the spectrum, the shape breathes with the bass,
// and every beat sends a pulse of light inward through the echoes.
// Family: Minimal
// Tags: Neon, Minimal, Geometric
// Added: 2026-10-31 03:30

static const int Echoes = 12;
static const float Points[6] = { 5.0, 3.0, 6.0, 4.0, 7.0, 8.0 };

// The shape's settings for section s: how many points, and how pinched or puffed the sides are.
float4 sfSettings(float s)
{
    float pick = hash(float2(s, 3.7));
    float m = Points[(uint)s % 6u];
    float n1 = lerp(0.35, 1.6, pick);
    float n2 = lerp(0.5, 1.8, hash(float2(s, 8.1)));
    return float4(m, n1, n2, n2);
}

// The superformula's radius at angle a.
float sfRadius(float a, float4 k)
{
    float t = k.x * a * 0.25;
    return pow(pow(abs(cos(t)), k.z) + pow(abs(sin(t)), k.w), -1.0 / k.y);
}

// The settings now: each section's shape, easing into the next over a couple of beats.
float4 sfNow()
{
    float sections = BeatCount / 16.0 + Time * 0.002;
    float s = floor(sections);
    float blend = smoothstep(0.85, 1.0, frac(sections));
    return lerp(sfSettings(s), sfSettings(s + 1.0), blend);
}

float3 sfNeon(float t)
{
    return hsv2rgb(frac(0.47 + t * 0.45 + sin(Time * 0.02) * 0.04), 0.85, 1.0);
}

float3 Render(float2 uv, float2 p)
{
    float px = pixelSize();
    float4 k = sfNow();

    // Scale the shape so its widest point reaches the same size whatever its settings.
    float widest = 0.0;
    [unroll]
    for (int s = 0; s < 9; s++)
    {
        widest = max(widest, sfRadius(s / 8.0 * 2.0 * PI / max(k.x, 1.0), k));
    }
    float size = 0.82 / max(widest, 1e-3) * (1.0 + Bass * 0.06 + DropPulse * 0.08);

    float r = length(p);
    float sinceBeat = -log(max(Beat, 1e-4)) / 5.0;
    float3 colour = 0.0;
    [unroll]
    for (int e = 0; e < Echoes; e++)
    {
        float depth = e / (Echoes - 1.0);
        float scale = size * pow(0.8, (float)e);
        float a = atan2(p.y, p.x) + Time * 0.05 + e * (0.08 + Mid * 0.04);
        float shape = sfRadius(a, k) * scale;
        // Distance to the outline, corrected by its slope so steep sides keep an even width.
        float slope = (sfRadius(a + 0.002, k) - sfRadius(a - 0.002, k)) * scale / 0.004;
        float d = abs(r - shape) / sqrt(1.0 + slope * slope / max(shape * shape, 1e-4));
        float stroke = exp(-pow(d / (px * 1.3), 2.0)) + exp(-d / (px * 9.0)) * 0.12;
        float level = spectrum(0.03 + depth * 0.65);
        float at = (depth - sinceBeat * 1.4) * 6.0;
        float pulse = exp(-at * at) * Beat;
        colour += sfNeon(depth) * stroke * (0.25 + level * 1.3 + pulse * 1.5) * (1.0 - depth * 0.35);
    }
    colour *= 0.8 + Intensity * 0.4;
    colour += float3(0.03, 0.02, 0.06) * exp(-r * r * 3.0) * (0.5 + Bass);
    return colour + previous(uv) * 0.2;
}
