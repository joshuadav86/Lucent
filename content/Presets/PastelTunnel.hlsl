// Name: Pastel Tunnel
// A flight through a tunnel of thin curved shells in sugar colours: sky blue, cream, pink, mint, and lemon,
// all glowing in a bright haze. Each shell is a ring with a wide gap that turns its own way. The gaps open
// with the music, the shells turn faster on the beat, and the haze brightens as the song builds.
// Family: Fly-through
// Tags: 3D, Colourful, Calm
// Added: 2026-11-15 20:56

static const float Section = 1.1;       // length of one stretch of tunnel
static const float Sections = 61.0;     // the tunnel repeats after this many

float3 ptSugar(float pick)
{
    if (pick < 0.24) return float3(0.2, 0.7, 1.35);
    if (pick < 0.45) return float3(1.35, 1.15, 0.7);
    if (pick < 0.65) return float3(1.5, 0.5, 0.85);
    if (pick < 0.83) return float3(0.35, 1.3, 0.85);
    return float3(1.55, 1.2, 0.25);
}

// Distance to the shells of the stretch of tunnel that x is in. `key` says which shell is nearest.
float ptMap(float3 x, out float key)
{
    float section = floor(x.z / Section);
    float along = x.z - (section + 0.5) * Section;
    float seed = wrap(section, Sections);
    float r = length(x.xy);
    float angle = atan2(x.y, x.x);
    float turning = Time * 0.1 + BeatGlide * 0.22;
    float d = 1e9;
    key = 0.0;

    [unroll]
    for (int s = 0; s < 3; s++)
    {
        float id = seed * 3.0 + (float)s;
        float radius = 0.6 + 0.45 * (float)s + 0.2 * hash(float2(id, 1.0));
        float way = hash(float2(id, 2.0)) > 0.5 ? 1.0 : -1.0;
        float turn = way * turning * (0.5 + hash(float2(id, 3.0))) + id * 2.1;
        float heard = spectrum(0.05 + 0.5 * hash(float2(id, 5.0)));
        float gap = 0.45 + 1.1 * hash(float2(id, 4.0)) + 0.5 * heard;

        float fromGap = abs(wrap(angle - turn + PI, 2.0 * PI) - PI);
        float shell = abs(r - radius) - 0.03;
        float cut = (gap - fromGap) * r;
        float ends = abs(along) - Section * (0.2 + 0.2 * hash(float2(id, 6.0)));
        float piece = max(max(shell, cut), ends);
        if (piece < d)
        {
            d = piece;
            key = id;
        }
    }

    // Never step past the end of this stretch: the next one has shells of its own.
    return min(d, Section * 0.5 - abs(along) + 0.03);
}

float3 Render(float2 uv, float2 p)
{
    float travel = wrap(Time * 0.45 + BeatGlide * 0.5, Section * Sections);
    float3 eye = float3(0.12 * sin(Time * 0.23), 0.1 * cos(Time * 0.19), travel);
    float3 ray = cameraRay(eye, eye + float3(0.1 * sin(Time * 0.11), 0.08 * sin(Time * 0.13), 1.0), float2(p.x, -p.y), 0.85, 0.3 * sin(Time * 0.07));

    float t = 0.02;
    float key = 0.0;
    float glowing = 0.0;
    bool hit = false;
    int steps = qualitySteps(72);

    [loop]
    for (int i = 0; i < steps; i++)
    {
        float d = ptMap(eye + ray * t, key);
        if (d < 0.002 * surfacePrecision() * (1.0 + t))
        {
            hit = true;
            break;
        }

        float pace = max(d * 0.85, 0.01);
        glowing += pace / (1.0 + d * d * 90.0);
        t += pace;
        if (t > 16.0) break;
    }

    float3 haze = float3(0.5, 0.86, 1.35) * (0.9 + 0.2 * Intensity + 0.2 * DropPulse);
    float3 colour = haze;

    if (hit)
    {
        float3 at = eye + ray * t;
        float unused;
        float2 e = float2(0.004, 0.0);
        float3 n = normalize(float3(
            ptMap(at + e.xyy, unused) - ptMap(at - e.xyy, unused),
            ptMap(at + e.yxy, unused) - ptMap(at - e.yxy, unused),
            ptMap(at + e.yyx, unused) - ptMap(at - e.yyx, unused)));

        // The light is the haze at the far end, so the shells are lit softly from ahead and glow at their rims.
        float facing = abs(dot(n, ray));
        float3 sugar = ptSugar(hash(float2(key, 7.0)));
        float3 lit = sugar * (0.5 + 0.5 * facing) * (0.75 + 0.25 * n.y) + float3(1.2, 1.2, 1.25) * pow(1.0 - facing, 3.0) * 0.4;
        colour = lerp(lit, haze, 1.0 - exp(-t * 0.075));
    }

    colour += float3(1.0, 0.95, 0.9) * glowing * (0.03 + 0.02 * BeatSwell);
    return colour * (1.0 - dot(uv - 0.5, uv - 0.5) * 0.3);
}
