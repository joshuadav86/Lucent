// Name: Billow
// A flight through towering cloud at sunset: walls of billowing vapour, gold where the sun reaches them and
// deep amber where it doesn't, with blue sky showing through the gaps. The beat
// carries you forward, the bass thickens the cloud, and its heart glows from inside as the music swells.
// Family: Fly-through
// Tags: 3D, Calm, Fire
// Added: 2026-11-15 21:14

static const float Loop = 128.0;        // the passage repeats after this far

float bwCorner(float3 c, float cells)
{
    c.z = wrap(c.z, cells);
    return hash(float2(c.x + c.z * 37.17, c.y - c.z * 11.71) * 0.731 + 3.1);
}

// Smooth noise in space that repeats every `cells` along the passage.
float bwNoise(float3 x, float cells)
{
    float3 c = floor(x);
    float3 f = x - c;
    f = f * f * (3.0 - 2.0 * f);
    float near = lerp(lerp(bwCorner(c, cells), bwCorner(c + float3(1.0, 0.0, 0.0), cells), f.x),
                      lerp(bwCorner(c + float3(0.0, 1.0, 0.0), cells), bwCorner(c + float3(1.0, 1.0, 0.0), cells), f.x), f.y);
    float far = lerp(lerp(bwCorner(c + float3(0.0, 0.0, 1.0), cells), bwCorner(c + float3(1.0, 0.0, 1.0), cells), f.x),
                     lerp(bwCorner(c + float3(0.0, 1.0, 1.0), cells), bwCorner(c + float3(1.0, 1.0, 1.0), cells), f.x), f.y);
    return lerp(near, far, f.z);
}

float2 bwPath(float z)
{
    return float2(1.1 * sin(z * 0.19635), 0.6 * cos(z * 0.14726));
}

// How thick the cloud is at x, from 0 (clear air) to 1.
float bwCloud(float3 x, float churn, float thick)
{
    float3 q = x * 0.5;
    float n = 0.5 * bwNoise(q + float3(0.0, churn, 0.0), 64.0)
            + 0.27 * bwNoise(q * 2.0 + float3(churn * 1.7, 0.0, 0.0), 128.0)
            + 0.15 * bwNoise(q * 4.0 - float3(0.0, churn * 2.6, 0.0), 256.0)
            + 0.08 * bwNoise(q * 8.0 + float3(churn * 4.0, 0.0, 0.0), 512.0);
    float fromPath = length(x.xy - bwPath(x.z));
    return saturate((n + 0.1 * smoothstep(1.0, 3.0, fromPath) - 0.3 * (1.0 - smoothstep(0.3, 1.3, fromPath)) - 0.55 + thick) * 7.0);
}

float3 Render(float2 uv, float2 p)
{
    float travel = wrap(Time * 0.5 + BeatGlide * 0.55, Loop);
    float churn = wrap(Time * 0.03, 64.0);
    float thick = 0.03 * Bass + 0.012 * BeatSwell;

    float3 eye = float3(bwPath(travel), travel);
    float3 ahead = float3(bwPath(travel + 2.5), travel + 2.5);
    float3 ray = cameraRay(eye, ahead, float2(p.x, -p.y), 0.9, 0.12 * sin(Time * 0.09));
    float3 sun = normalize(float3(0.35, 0.55, 0.75));

    // The open sky at the far end: bright towards the sun.
    float towardSun = saturate(dot(ray, sun));
    float3 sky = lerp(float3(0.12, 0.4, 1.25), float3(0.55, 0.75, 1.3), saturate(0.5 - ray.y));
    sky = lerp(sky, float3(1.6, 1.1, 0.55), pow(towardSun, 4.0)) * (0.85 + 0.2 * Intensity + 0.2 * DropPulse);
    float3 haze = float3(1.45, 0.8, 0.42);

    float3 colour = float3(0.0, 0.0, 0.0);
    float covered = 0.0;
    float t = 0.15 + 0.12 * hash(p * 197.0 + 3.0);
    int steps = qualitySteps(60);

    [loop]
    for (int i = 0; i < steps; i++)
    {
        if (covered > 0.97 || t > 18.0) break;
        float3 x = eye + ray * t;
        float pace = (0.1 + 0.03 * t) / detailScale();
        float cloud = bwCloud(x, churn, thick);
        if (cloud > 0.01)
        {
            // Cloud between here and the sun puts this part in shade.
            float shade = bwCloud(x + sun * 0.3, churn, thick) + 0.7 * bwCloud(x + sun * 0.9, churn, thick);
            float sunlit = exp(-shade * 2.2);
            float3 vapour = lerp(float3(0.2, 0.05, 0.05), float3(1.7, 1.22, 0.6), sunlit);
            vapour += float3(1.3, 0.75, 0.4) * cloud * (0.08 * Level + 0.12 * BeatSwell);
            vapour = lerp(vapour, haze, 1.0 - exp(-t * 0.05));
            float cover = (1.0 - exp(-cloud * pace * 5.0)) * (1.0 - covered);
            colour += vapour * cover;
            covered += cover;
        }

        t += pace;
    }

    colour += sky * (1.0 - covered);
    return colour * (0.94 + 0.08 * Level) * (1.0 - dot(uv - 0.5, uv - 0.5) * 0.4);
}
