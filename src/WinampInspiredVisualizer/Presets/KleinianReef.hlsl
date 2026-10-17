// Name: Kleinian Reef
// Drifting through bioluminescent coral caves grown from a Kleinian fractal, with caustics, plankton and pulses of light.
// Family: Fly-through
// Tags: Colourful, Water, 3D

// The reef: sphere inversions folded into a repeating lattice (an Apollonian / Kleinian limit set).
// shape is the inversion strength, which slowly breathes so the coral keeps growing and shrinking.
// trap returns how deep into the folds the point sat (used for colour and glowing polyps).
float reefDE(float3 position, float shape, out float trap)
{
    float3 p = position;
    float scale = 1.0;
    trap = 10.0;
    [unroll]
    for (int i = 0; i < 4; i++)
    {
        p = -1.0 + 2.0 * frac(0.5 * p + 0.5);
        float r2 = dot(p, p);
        trap = min(trap, r2);
        float k = shape / max(r2, 1e-6);
        p *= k;
        scale *= k;
    }
    // Higher quality settings add finer folds.
    if (Quality > 1.5)
    {
        int extra = qualitySteps(4) - 4;
        [loop]
        for (int j = 0; j < extra; j++)
        {
            p = -1.0 + 2.0 * frac(0.5 * p + 0.5);
            float k = shape / max(dot(p, p), 1e-6);
            p *= k;
            scale *= k;
        }
    }
    return 0.25 * abs(p.y) / scale;
}

// A meandering lane through the open layer of the reef, clear of the coral pillars.
float3 reefPath(float z)
{
    return float3(-1.0 + 0.3 * sin(z * 0.37) + 0.1 * sin(z * 0.83), -1.0 + 0.22 * sin(z * 0.29 + 1.0), z);
}

// Coral tones: hot pink, orange, violet, teal and gold.
float3 reefCoral(float t)
{
    return saturate(float3(0.62, 0.38, 0.55) + float3(0.45, 0.38, 0.45) * cos(2.0 * PI * (t + float3(0.0, 0.18, 0.4))));
}

float reefHash3(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

// Shimmering light patterns cast by the waves far above.
float reefCaustics(float2 p, float t)
{
    float2 q = p;
    float total = 0.0;
    [unroll]
    for (int i = 0; i < 3; i++)
    {
        q = float2(q.x + sin(q.y * 1.7 + t), q.y + cos(q.x * 1.3 - t * 0.8)) * 1.6;
        total += abs(sin(q.x) * sin(q.y));
    }
    return pow(saturate(1.0 - total / 3.0), 4.0);
}

float3 Render(float2 uv, float2 p)
{
    float shape = 1.17 + 0.1 * sin(Time * 0.043);
    float z = Time * 0.32 + (BeatCount - Beat) * 0.06;
    float3 eye = reefPath(z);
    float3 ahead = reefPath(z + 1.2);
    float3 dir = cameraRay(eye, ahead, p, 0.9 - Beat * 0.06, sin(Time * 0.09) * 0.35);

    const float farPlane = 7.0;
    int maxSteps = qualitySteps(64);
    float travelled = 0.02;
    float trap = 0.0;
    bool landed = false;
    [loop]
    for (int i = 0; i < maxSteps; i++)
    {
        float d = reefDE(eye + dir * travelled, shape, trap);
        if (d < 0.0012 * travelled)
        {
            landed = true;
            break;
        }
        travelled += d * 1.25;
        if (travelled > farPlane)
        {
            break;
        }
    }
    travelled = landed ? travelled : farPlane;

    float hueDrift = Time * 0.015;
    // Deep water, a little brighter looking up towards the distant surface.
    float3 water = hsv2rgb(0.62 + hueDrift, 0.85, 0.03 + saturate(-dir.y) * 0.05);
    float3 colour = water;
    if (landed)
    {
        float3 hit = eye + dir * travelled;
        float tr = 0.0;
        float e1 = 0.0006 + 0.0015 * travelled;
        float2 e = float2(e1, -e1);
        float3 normal = normalize(e.xyy * reefDE(hit + e.xyy, shape, tr) + e.yyx * reefDE(hit + e.yyx, shape, tr)
                                + e.yxy * reefDE(hit + e.yxy, shape, tr) + e.xxx * reefDE(hit + e.xxx, shape, tr));
        float occlusion = saturate(1.0 - ((0.04 - reefDE(hit + normal * 0.04, shape, tr))
                                        + (0.1 - reefDE(hit + normal * 0.1, shape, tr)) * 0.5) * 7.0);

        // A diver's lamp travels with the camera; faint moonlit caustics ripple from above.
        float3 lampPos = eye + float3(0.0, 0.15, 0.0);
        float3 toLamp = lampPos - hit;
        float lampDist2 = dot(toLamp, toLamp);
        toLamp *= rsqrt(lampDist2);
        float lamp = saturate(dot(normal, toLamp)) / (1.0 + lampDist2 * 0.35);
        float caustic = reefCaustics(hit.xz * 2.2, Time * 0.6) * saturate(-normal.y);   // world -y is up on screen
        float facing = saturate(dot(normal, -dir));
        float rim = pow(1.0 - facing, 3.0);
        float gloss = pow(saturate(dot(normal, normalize(toLamp - dir))), 40.0) / (1.0 + lampDist2 * 0.35);

        // Colour wanders smoothly across the reef and drifts over time.
        float region = noise(hit.xz * 0.35 + hit.y * 0.3) * 1.6 + sqrt(trap) * 0.25 + hueDrift;
        float3 coral = reefCoral(region);
        float3 litCoral = coral * (0.015 + lamp * 2.2) * occlusion
                        + float3(0.4, 0.8, 1.0) * caustic * 0.35 * occlusion
                        + gloss * 1.5 * occlusion;

        // Glowing polyps in the deepest folds, each tuned to a band of the spectrum,
        // and the rims of every opening lit like lace.
        float band = frac(sqrt(trap) * 3.0 + region);
        float polyps = glow(trap, 12.0) * (0.25 + spectrum(band) * 2.0) * (0.6 + Bass * 0.8);
        float3 glowColour = neon(band * 0.5 + region + 0.4);
        float lace = rim * (0.4 + spectrum(frac(region * 3.0)) * 1.6);

        // After each beat a ripple of light spreads out through the coral.
        float ripple = glow(length(hit - eye) - (1.0 - Beat) * 6.0, 5.0) * Beat;

        colour = litCoral + glowColour * (polyps + ripple * 0.8) * occlusion + neon(region + 0.5) * lace * 0.9 * occlusion;
        colour = lerp(water, colour, exp(-travelled * 0.3));
    }

    // Light shafts and drifting plankton, sampled at a few depths along the ray.
    float3 particles = 0.0;
    [unroll]
    for (int layer = 1; layer <= 3; layer++)
    {
        float depth = layer * 1.3;
        if (depth < travelled)
        {
            float3 spot = eye + dir * depth + float3(0.0, Time * 0.03, Time * 0.05);
            float3 cell = floor(spot * 2.5);
            float3 centre = (cell + 0.5 + (float3(reefHash3(cell), reefHash3(cell + 7.1), reefHash3(cell + 3.7)) - 0.5) * 0.7) / 2.5;
            float3 offset = centre - spot;
            float miss = length(offset - dir * dot(offset, dir));
            float alive = step(0.45, reefHash3(cell + 1.3));
            float twinkle = 0.6 + 0.4 * sin(Time * 3.0 + reefHash3(cell) * 30.0) * (0.5 + Treble);
            particles += neon(reefHash3(cell + 2.2) * 0.4 + 0.45) * alive * twinkle * 0.0004 / (miss * miss + 0.0003)
                       * exp(-depth * 0.35);
        }
    }
    colour += particles * 0.6;
    return colour + previous(uv) * 0.1;
}
