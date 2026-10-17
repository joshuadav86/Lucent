// Name: Burning Ship
// Gliding past the Burning Ship fractal's armada: towering ships of fire rise from a dark sea that mirrors their flames, heat bands rolling up with the beat.

// Fire colour by heat: deep red -> orange -> gold -> white.
float3 shipFire(float heat)
{
    float t = saturate(heat);
    return float3(1.6 * t, 1.2 * t * t, 0.85 * t * t * t * t);
}

// Rotates a colour's hue around the grey axis, keeping its brightness.
float3 shipHueTurn(float3 colour, float angle)
{
    const float3 axis = float3(0.57735, 0.57735, 0.57735);
    float s, c;
    sincos(angle, s, c);
    return max(colour * c + cross(axis, colour) * s + axis * dot(axis, colour) * (1.0 - c), 0.0);
}

// Camera gliding along the antenna: x pans slowly between the ships, the view height breathes in and out.
float3 shipCamera(float t)
{
    float x = -1.735 + 0.115 * sin(t * 0.043) + 0.02 * sin(t * 0.11);
    float h = 0.075 * exp2(0.6 * sin(t * 0.083 + 1.3));
    return float3(x, -0.22 * h, h);
}

float3 Render(float2 uv, float2 p)
{
    float3 camera = shipCamera(Time);
    float height = camera.z;
    float pixel = height / Resolution.y;
    float2 world = camera.xy + p * (height * 0.5);

    // Below the antenna line lies the sea: it mirrors the fleet, rippled by the bass.
    bool sea = world.y > 0.0;
    float depth = world.y / height;
    float2 c = world;
    if (sea)
    {
        float wave = sin(depth * 70.0 / (depth + 0.05) - Time * 2.5 + sin(world.x / height * 9.0) * 1.5);
        c.x += wave * height * (0.003 + 0.01 * Bass) * (0.3 + depth * 5.0);
        c.y = -world.y * (1.0 + depth * 0.6);
    }

    float2 z = 0.0;
    float2 dz = 0.0;
    float n = 0.0;
    bool escaped = false;
    int maxIterations = qualitySteps(300);
    [loop]
    for (int i = 0; i < maxIterations; i++)
    {
        float2 flip = float2(z.x < 0.0 ? -1.0 : 1.0, z.y < 0.0 ? -1.0 : 1.0);
        float2 w = abs(z);
        float2 dw = dz * flip;
        dz = 2.0 * float2(w.x * dw.x - w.y * dw.y, w.x * dw.y + w.y * dw.x) + float2(1.0, 0.0);
        z = float2(w.x * w.x - w.y * w.y, 2.0 * w.x * w.y) + c;
        if (dot(z, z) > 1.0e6)
        {
            escaped = true;
            break;
        }
        n += 1.0;
    }

    float3 colour = 0.0;
    if (escaped)
    {
        float logR = 0.5 * log(dot(z, z));
        float smoothN = n + 1.0 - log2(logR);
        float de = length(z) * logR / max(length(dz), 1.0e-20);
        float dpx = de / pixel;

        // The distance to the set, on a log scale, reveals the cathedral architecture of the ships: dense lace
        // burns white-hot, its windows and arches glow deep red, and heat contours ripple outward into the night.
        float logD = log2(max(dpx, 1.0e-6));
        float lace = saturate(-logD / 6.5);
        float ripple = 0.5 + 0.5 * cos(2.0 * PI * (logD * 0.55 + Time * 0.45 + (BeatCount - Beat) * 0.15));
        float outer = exp2(-max(logD, 0.0) * (0.9 - Bass * 0.3));       // glow falling off with distance

        // A gentle flicker drifting up through the lace.
        float2 flameUV = float2(world.x * 55.0, world.y * 28.0 + Time * 1.1);
        float flicker = noise(flameUV) * 0.6 + noise(flameUV * 2.3 + 5.1) * 0.4;

        float heat = lace * (0.75 + 0.35 * flicker) + 0.12 * ripple * outer;
        colour = shipFire(0.25 + 0.62 * heat) * (0.06 + 0.55 * lace * lace * (0.75 + 0.35 * flicker)) * (0.9 + Bass * 0.35 + Beat * 0.2) * saturate(1.0 - logD * 0.5)
               + float3(0.9, 0.12, 0.25) * outer * (0.06 + 0.2 * ripple) * (0.6 + Mid * 0.5);
    }
    else if (length(dz) * pixel > 2.0)
    {
        colour = shipFire(0.9) * 0.4;
    }

    // The flames swing slowly through chemical colours, nudged on every beat.
    colour = shipHueTurn(colour, -0.75 + 0.75 * sin(Time * 0.041 + (BeatCount - Beat) * 0.08));

    if (sea)
    {
        // The reflection is darker and cooler, fading into the deep.
        colour = colour * float3(0.75, 0.7, 0.95) * 0.55 * exp(-depth * 2.2);
        colour += float3(0.01, 0.006, 0.02);
    }

    // Embers rising from the fleet, brighter with the treble.
    float2 emberCell = float2(p.x * 8.0, p.y * 8.0 + Time * 0.5);
    float2 cellId = floor(emberCell);
    float2 offset = float2(hash(cellId + 3.1), hash(cellId + 7.7)) - 0.5;
    float ember = glow(length(frac(emberCell) - 0.5 - offset * 0.7), 70.0) * step(0.82, hash(cellId));
    colour += shipFire(0.75) * ember * (0.1 + Treble * 1.2) * (sea ? 0.0 : 1.0);

    // Reproject the last frame along the camera path: temporal smoothing for the finest lace.
    float3 last = shipCamera(Time - DeltaTime);
    float2 lastP = (world - last.xy) / (last.z * 0.5);
    return lerp(colour, previous(toUv(lastP)), 0.3);
}
