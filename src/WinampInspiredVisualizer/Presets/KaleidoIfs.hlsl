// Name: Kaleido IFS
// A crystal sculpture grown by folding space through rotating mirrors again and again: facets, spires and lattices of glassy light that refold themselves with the music, rim-lit in shifting colours.
// Family: Fractal
// Tags: 3D, Geometric
// Added: 2026-10-17 21:50

float3x3 kifsRotation(float3 angles)
{
    float3 s = sin(angles);
    float3 c = cos(angles);
    float3x3 rx = float3x3(1.0, 0.0, 0.0, 0.0, c.x, -s.x, 0.0, s.x, c.x);
    float3x3 ry = float3x3(c.y, 0.0, s.y, 0.0, 1.0, 0.0, -s.y, 0.0, c.y);
    float3x3 rz = float3x3(c.z, -s.z, 0.0, s.z, c.z, 0.0, 0.0, 0.0, 1.0);
    return mul(rz, mul(ry, rx));
}

// Distance to a kaleidoscopic IFS: each step mirrors space across the folding planes of a cube (abs and
// its three diagonal planes), turns it, and scales it twice away from a corner, leaving a small block at
// the end of every branch. Turning between the folds breaks the blocks into clusters of crystal facets.
// The trap records how close the orbit came to the centre and the axes, for colour.
float kifsDistance(float3 position, float3x3 turn, float3 offset, out float3 trap)
{
    float scale = 2.0;
    trap = 1.0e3;
    int levels = fractalDepth(4);
    float factor = 1.0;
    [loop]
    for (int i = 0; i < levels; i++)
    {
        position = abs(position);
        if (position.x < position.y) position.xy = position.yx;
        if (position.x < position.z) position.xz = position.zx;
        if (position.y < position.z) position.yz = position.zy;
        position = mul(turn, position);
        position = position * scale - offset * (scale - 1.0);
        factor *= scale;
        trap = min(trap, float3(length(position), abs(position.y), abs(position.z)));
    }
    // The blocks are a little smaller than their cells, so neighbouring faces never lie in the same plane.
    float3 q = abs(position) - 0.8;
    return (length(max(q, 0.0)) + min(max(q.x, max(q.y, q.z)), 0.0)) / factor;
}

float3 Render(float2 uv, float2 p)
{
    // The fold rotation drifts slowly and leans with the mids; the bass pushes the corner out.
    float3 angles = float3(0.3 * sin(Time * 0.05) + Mid * 0.06,
                           0.25 * sin(Time * 0.037 + 1.0) + 0.1,
                           0.3 * sin(Time * 0.029 + 2.0) + Mid * 0.04);
    float3x3 turn = kifsRotation(angles);
    float3 offset = float3(1.0, 0.9 + 0.05 * sin(Time * 0.043), 0.75) * (1.0 + 0.04 * Bass);

    float orbit = Time * 0.06;
    float3 eye = float3(sin(orbit) * 3.3, 1.0 + 0.6 * sin(Time * 0.041), cos(orbit) * 3.3);
    float3 direction = cameraRay(eye, float3(0.0, 0.0, 0.0), float2(p.x, -p.y), 2.3, 0.1 * sin(Time * 0.03));

    float low = spectrum(0.06);
    float high = spectrum(0.55);
    float3 rimA = hsv2rgb(frac(0.55 + Time * 0.01), 0.7, 1.0) * (0.5 + 1.2 * low);
    float3 rimB = hsv2rgb(frac(0.9 + Time * 0.01), 0.6, 1.0) * (0.5 + 1.2 * high);

    // Bounding sphere.
    float radius = 2.0;
    float b = dot(eye, direction);
    float disc = b * b - (dot(eye, eye) - radius * radius);
    float3 background = lerp(float3(0.004, 0.006, 0.014), float3(0.025, 0.03, 0.05), saturate(0.5 - p.y * 0.5));
    background += rimA * 0.03 * exp(-dot(p, p) * 1.2);
    float3 colour = background;
    float closest = 1.0e3;
    if (disc > 0.0)
    {
        float travelled = max(-b - sqrt(disc), 0.0);
        float exitDistance = -b + sqrt(disc);
        bool hit = false;
        float3 trap = 0.0;
        float steps = 0.0;
        int maxSteps = qualitySteps(100);
        float precision = 0.0006 * surfacePrecision();
        [loop]
        for (int i = 0; i < maxSteps; i++)
        {
            float d = kifsDistance(eye + direction * travelled, turn, offset, trap);
            closest = min(closest, d);
            if (d < precision * travelled)
            {
                hit = true;
                break;
            }
            travelled += d * 0.95;
            steps += 1.0;
            if (travelled > exitDistance)
            {
                break;
            }
        }

        if (hit)
        {
            float3 position = eye + direction * travelled;
            float e = 0.0005 * travelled;
            float2 k = float2(1.0, -1.0);
            float3 unusedTrap;
            float3 normal = normalize(k.xyy * kifsDistance(position + k.xyy * e, turn, offset, unusedTrap)
                                    + k.yyx * kifsDistance(position + k.yyx * e, turn, offset, unusedTrap)
                                    + k.yxy * kifsDistance(position + k.yxy * e, turn, offset, unusedTrap)
                                    + k.xxx * kifsDistance(position + k.xxx * e, turn, offset, unusedTrap));
            float occlusion = saturate(1.0 - steps / (float)maxSteps * 1.4);
            occlusion *= occlusion;

            // Crystal: a pale tinted body, crisp highlights, coloured rims and a mirror of the dark studio.
            // Each crystal cluster gets its own tint from the orbit, like a geode of mixed quartz and amethyst.
            float tone = saturate(trap.x * 0.35);
            float3 body = hsv2rgb(frac(0.5 + trap.y * 1.1 + trap.z * 0.5 + Time * 0.008), 0.45, 0.85);
            float3 keyDir = normalize(float3(0.3, 0.9, 0.4));
            float diffuse = saturate(dot(normal, keyDir));
            float3 halfway = normalize(keyDir - direction);
            float specular = pow(saturate(dot(normal, halfway)), 70.0);
            float facing = saturate(dot(normal, -direction));
            float fresnel = 0.04 + 0.96 * pow(1.0 - facing, 5.0);
            float sideA = saturate(dot(normal, normalize(float3(-0.9, 0.1, 0.4))));
            float sideB = saturate(dot(normal, normalize(float3(0.8, -0.3, -0.5))));
            float3 reflected = reflect(direction, normal);
            float3 studio = lerp(float3(0.01, 0.012, 0.02), float3(0.3, 0.32, 0.36), pow(saturate(reflected.y * 0.5 + 0.5), 3.0));

            colour = body * (0.06 + 0.7 * diffuse) * occlusion
                   + (rimA * sideA * sideA + rimB * sideB * sideB) * (0.35 + 0.65 * fresnel) * 0.8 * occlusion
                   + studio * fresnel * occlusion
                   + float3(1.0, 0.98, 0.95) * specular * (0.5 + 0.5 * Treble + Beat * 0.3) * occlusion;
            // A soft inner glow from the heart of the sculpture.
            colour += lerp(rimA, rimB, tone) * exp(-trap.x * 3.0) * 0.2 * (0.6 + Beat * 0.5) * occlusion;
        }
        else
        {
            colour += lerp(rimA, rimB, 0.5) * exp(-closest * 40.0) * 0.1;
        }
    }
    return lerp(colour, previous(uv), 0.12);
}
