// Name: Lissajous Laser
// A laser traces a slowly turning 3D Lissajous knot that shivers with the waveform, leaving phosphor ghosts behind.
// Family: Music-shaped
// Tags: Dark, Neon, Retro

float laserSegment(float2 p, float2 a, float2 b)
{
    float2 pa = p - a;
    float2 ba = b - a;
    float h = saturate(dot(pa, ba) / max(dot(ba, ba), 1e-7));
    float2 d = pa - ba * h;
    return dot(d, d);
}

float3 Render(float2 uv, float2 p)
{
    float px = pixelSize();

    // The knot's frequency ratios step through a few shapes; their phases drift so it keeps morphing.
    float era = floor(Time / 24.0);
    float eraPhase = frac(Time / 24.0);
    float pick = frac(era * 0.381 + 0.1);
    float3 ratios = pick < 0.25 ? float3(3.0, 2.0, 5.0)
                  : pick < 0.5 ? float3(5.0, 4.0, 3.0)
                  : pick < 0.75 ? float3(2.0, 3.0, 7.0)
                  : float3(4.0, 5.0, 3.0);
    float3 phases = float3(Time * 0.13, Time * 0.07 + 1.0, Time * 0.05 + 2.0);
    float spinA = Time * 0.21;
    float spinB = sin(Time * 0.13) * 0.8;
    float2 rotA = float2(cos(spinA), sin(spinA));
    float2 rotB = float2(cos(spinB), sin(spinB));
    float wobble = 0.1 + Mid * 0.22;
    // Between shapes the knot collapses to a point and blooms again as the next one.
    float bloom = smoothstep(0.0, 0.05, eraPhase) * smoothstep(1.0, 0.97, eraPhase);
    float size = (0.6 + Bass * 0.08) * bloom;

    float3 colour = 0.0;
    float radius = length(p);
    if (radius < 1.05)
    {
        int segments = qualitySteps(96);
        float stepAngle = 2.0 * PI / (float)segments;
        // Each axis is a sine; advance them by rotating (cos, sin) pairs instead of calling sin per point.
        float3 c = cos(phases);
        float3 s = sin(phases);
        float3 dc = cos(ratios * stepAngle);
        float3 ds = sin(ratios * stepAngle);

        // The waveform is read every fourth point and blended in between: a smooth, cheap shiver.
        float waveBefore = waveform(0.05);
        float waveAfter = waveBefore;
        float2 a = 0.0;
        float nearest = 1e3;
        float nearestT = 0.0;
        float nearestDepth = 0.0;
        float beam = 0.0;
        [loop]
        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / (float)segments;
            if (i % 4 == 0)
            {
                waveBefore = waveAfter;
                float ahead = saturate((float)(i + 4) / (float)segments);
                waveAfter = waveform(0.05 + 0.9 * (1.0 - abs(2.0 * ahead - 1.0)));
            }
            float wave = lerp(waveBefore, waveAfter, (float)(i % 4) * 0.25);
            float3 q = s * size * (1.0 + wave * wobble);
            // Turn the knot: around the vertical axis, then tip it towards the viewer.
            q.xz = float2(rotA.x * q.x - rotA.y * q.z, rotA.y * q.x + rotA.x * q.z);
            q.yz = float2(rotB.x * q.y - rotB.y * q.z, rotB.y * q.y + rotB.x * q.z);
            float2 b = q.xy * (2.5 / (q.z + 3.0));
            if (i > 0)
            {
                float d2 = laserSegment(p, a, b);
                beam += 1.0 / (1.0 + d2 / (px * px * 0.6));
                if (d2 < nearest)
                {
                    nearest = d2;
                    nearestT = t;
                    nearestDepth = q.z;
                }
            }
            a = b;
            float3 nc = c * dc - s * ds;
            s = s * dc + c * ds;
            c = nc;
        }

        // Saturated laser colour that flows along the beam; nearer parts burn brighter.
        float3 hue = 0.5 + 0.5 * cos(2.0 * PI * (nearestT * 2.0 + Time * 0.06 + float3(0.0, 0.33, 0.67)));
        hue = hue * hue;
        float depth = saturate(0.55 - nearestDepth * 0.5);
        float energy = (0.55 + Level * 0.9 + Beat * 0.35) * (0.4 + depth);
        float core = saturate(beam * beam * 0.08);
        float halo = exp(-sqrt(nearest) * 60.0);
        colour = (lerp(hue, 1.0, 0.45) * core * 1.4 + hue * (halo * 0.5 + saturate(beam * 0.05) * 0.6)) * energy;
    }

    // Phosphor persistence: the last frame fades and turns slightly, so motion leaves ghost trails.
    float3 ghost = previous(toUv(rotate(p, 0.003) * 0.997));
    ghost = lerp(ghost, ghost.gbr, 0.02) * exp(-DeltaTime * 6.0);
    return colour + ghost;
}
