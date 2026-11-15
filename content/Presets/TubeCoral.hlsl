// Name: Tube Coral
// Drifting down a lane through a reef of coral tubes, some standing alone and some wound round each other in
// pairs, all swaying in the current. Each tube glows with its own band of the spectrum, the bass swells them,
// and bulges of light climb them in time with the beat.
// Family: Organic
// Tags: 3D, Colourful, Water
// Added: 2026-11-15 19:46

// The tubes stand one to a square on two grids laid over each other, the second shifted half a square, and
// each sways only inside its own square. So a ray only ever has to look at the two tubes whose squares it is
// in, and can always step safely to the edge of a square.
static const float Margin = 0.12;              // the gap every tube keeps from the edge of its square

float2 tcLane(float z)
{
    return float2(sin(z * 0.21) * 1.3 + sin(z * 0.083) * 0.8, 0.0);
}

// The tube in the square the point is in. about comes back with the tube's two random numbers and how
// strongly a bulge is passing this height.
float tcGrid(float3 x, float2 shift, float seed, out float3 about)
{
    float2 g = x.xz + shift;
    float2 cell = floor(g);
    float2 f = g - cell - 0.5;
    float wall = 0.5 - max(abs(f.x), abs(f.y));
    float h = hash(cell + seed);
    float k = hash(cell.yx * 1.31 + seed + 7.7);
    about = float3(h, k, 0.0);
    // The squares along the lane are left empty for the camera.
    float2 middle = cell + 0.5 - shift;
    if (abs(middle.x - tcLane(middle.y).x) < 1.2)
    {
        return wall + Margin;
    }

    float phase = h * 6.2832;
    float2 sway = 0.1 * float2(sin(x.y * (0.8 + k * 0.7) + phase + Time * 0.37), cos(x.y * (0.6 + h * 0.8) + phase * 1.7 - Time * 0.29));
    float2 d = f - sway;
    float pulse = pow(0.5 + 0.5 * sin(x.y * 2.2 - BeatGlide * PI + phase), 6.0);
    about.z = pulse;
    float thick = (0.08 + 0.04 * k) * (1.0 + pulse * (0.2 + Bass * 0.25)) + 0.0025 * sin(x.y * 58.0 + phase);
    float twist = x.y * (2.0 + h) + phase;
    float2 arm = (k > 0.45 ? 0.07 : 0.0) * float2(cos(twist), sin(twist));
    float tube = min(length(d - arm), length(d + arm)) - thick;
    return min(tube * 0.72, wall + Margin);
}

float tcMap(float3 x, out float3 about)
{
    float3 aboutA;
    float3 aboutB;
    float a = tcGrid(x, float2(0.0, 0.0), 0.0, aboutA);
    float b = tcGrid(x, float2(0.5, 0.5), 31.7, aboutB);
    about = a < b ? aboutA : aboutB;
    return min(a, b);
}

float3 Render(float2 uv, float2 p)
{
    float travelled = Time * 0.22 + BeatGlide * 0.07;
    float rise = Time * 0.09;
    float3 eye = float3(tcLane(travelled).x, rise, travelled);
    float3 target = float3(tcLane(travelled + 2.2).x + 0.8 * sin(Time * 0.1), rise + 0.7 + 0.4 * sin(Time * 0.13), travelled + 2.2);
    float3 ray = cameraRay(eye, target, float2(p.x, -p.y), 0.95 - DropPulse * 0.08, sin(Time * 0.09) * 0.2);
    float beatLight = SongMapped > 0.0 ? 0.35 + Beat * 0.65 : Beat;

    // Deep water behind everything: wine red, a little lighter overhead.
    float3 water = lerp(float3(0.1, 0.02, 0.045), float3(0.3, 0.08, 0.09), saturate(0.5 + ray.y * 0.8));
    water *= 0.8 + Level * 0.3;

    float t = 0.02;
    float touch = 0.0;
    bool hit = false;
    float3 about = 0.0;
    int steps = qualitySteps(90);
    [loop]
    for (int i = 0; i < steps; i++)
    {
        float d = tcMap(eye + ray * t, about);
        touch = 0.0012 * surfacePrecision() * (1.0 + t);
        if (d < touch)
        {
            hit = true;
            break;
        }

        t += d;
        if (t > 13.0)
        {
            break;
        }
    }

    if (!hit)
    {
        return water * (0.9 + Intensity * 0.2);
    }

    float3 x = eye + ray * t;
    float3 unused;
    float2 e = float2(0.003, -0.003);
    float3 normal = normalize(
        e.xyy * tcMap(x + e.xyy, unused) +
        e.yyx * tcMap(x + e.yyx, unused) +
        e.yxy * tcMap(x + e.yxy, unused) +
        e.xxx * tcMap(x + e.xxx, unused));

    float band = spectrum(0.03 + about.x * 0.7);
    float3 flesh = lerp(float3(1.0, 0.66, 0.52), float3(0.93, 0.3, 0.3), smoothstep(0.2, 0.9, about.y));
    flesh = lerp(flesh, float3(1.0, 0.84, 0.66), about.x * about.x * 0.6);
    // Fine rings along each tube, like the segments of a worm.
    flesh *= 0.92 + 0.08 * sin(x.y * 58.0 + about.x * 6.2832);

    float3 light = normalize(float3(0.45, 0.8, -0.4));
    float lit = saturate(dot(normal, light) * 0.6 + 0.4);
    float open = saturate(tcMap(x + normal * 0.14, unused) / 0.14);
    float rim = pow(saturate(1.0 + dot(normal, ray)), 2.5);
    float3 colour = flesh * (0.14 + 0.86 * lit * lit) * (0.3 + 0.7 * open);
    // Light soaks a little way into coral, so the shaded side stays warm instead of going grey.
    colour += flesh * float3(1.0, 0.25, 0.18) * (1.0 - lit) * 0.24;
    colour += float3(1.0, 0.78, 0.62) * rim * 0.4 * open;
    float3 half0 = normalize(light - ray);
    colour += pow(saturate(dot(normal, half0)), 40.0) * 0.3 * open;
    // Each tube glows from inside with its band, and the bulges carry the beat up it.
    colour += flesh * float3(1.0, 0.7, 0.5) * (band * 0.55 + about.z * (0.3 + beatLight * 0.45 + DropPulse * 0.4));

    float haze = exp(-t * 0.2);
    colour = lerp(water, colour, haze);
    return colour * (0.9 + Intensity * 0.2);
}
