// Name: Dendrite
// A Julia set with no inside at all, only branches: a magenta tree of lightning on a field of bright cyan,
// printed like a poster. Its shape wanders round the point where the branches are finest, the bass and the
// beat thicken its limbs, rings of shade breathe away from it, and the whole tree turns slowly as it grows.
// Family: Fractal
// Tags: Colourful, Geometric
// Added: 2026-11-15 20:49

// Distance from z to the Julia set of z * z + c, by following z and the stretch of the plane around it.
float dnDistance(float2 z, float2 c)
{
    float2 dz = float2(1.0, 0.0);
    float m = dot(z, z);

    [loop]
    for (int i = 0; i < 56; i++)
    {
        if (m > 1e4) break;
        dz = 2.0 * float2(z.x * dz.x - z.y * dz.y, z.x * dz.y + z.y * dz.x);
        z = float2(z.x * z.x - z.y * z.y, 2.0 * z.x * z.y) + c;
        m = dot(z, z);
    }

    if (m < 4.0) return 0.0;
    return 0.5 * sqrt(m / max(dot(dz, dz), 1e-12)) * log(m) * 0.5;
}

float3 Render(float2 uv, float2 p)
{
    // The tree is finest when c sits at i. It circles close by, pushed a little further out by the bass.
    float around = Time * 0.19;
    float2 c = float2(0.0, 1.0) + (0.004 + 0.006 * Bass) * float2(cos(around), sin(around * 1.3));

    float zoom = 0.98 - 0.16 * sin(Time * 0.07) - 0.05 * DropPulse;
    float2 z = rotate(p, 1.2 + Time * 0.05 + 0.2 * sin(BeatGlide * 0.25)) * zoom;
    float d = dnDistance(z, c) / zoom;

    // The field: cyan, shaded close to the tree and ringed faintly further out.
    float3 field = float3(0.08, 1.05, 1.3) * (0.62 + 0.38 * smoothstep(0.0, 0.3, d));
    field *= 0.94 + 0.06 * cos(sqrt(d) * 26.0 - BeatGlide * PI);
    field = lerp(field, float3(0.02, 0.35, 0.95), 0.5 * smoothstep(0.5, 1.8, d));

    // The tree: magenta limbs with a white-hot heart.
    float limb = 0.013 + 0.008 * Bass + 0.006 * BeatSwell;
    float edge = pixelSize();
    // A blush of pink around the limbs, then the limbs themselves.
    field = lerp(field, float3(1.3, 0.5, 1.0), 0.55 * smoothstep(limb * 3.5, limb, d));
    float3 colour = lerp(field, float3(1.5, 0.1, 0.72), smoothstep(limb + edge, limb - edge, d));
    colour = lerp(colour, float3(1.5, 1.2, 1.4), smoothstep(limb * 0.45, 0.0, d) * (0.55 + 0.3 * Treble));
    return colour * (0.92 + 0.1 * Intensity) * (1.0 - dot(uv - 0.5, uv - 0.5) * 0.55);
}
