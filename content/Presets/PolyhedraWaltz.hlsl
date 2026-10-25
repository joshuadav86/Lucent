// Name: Polyhedra Waltz
// Three wireframe solids of light nest inside one another, each turning its own way: every few beats one
// of them melts into its mirror-twin (the cube into the octahedron, the icosahedron into the dodecahedron), lit by the spectrum.
// Family: Geometric
// Tags: Neon, 3D, Geometric
// Added: 2026-10-17 21:44

// Corners of six solids on the unit sphere: icosahedron, dodecahedron, cube, octahedron and two opposite tetrahedra.
static const float3 PW_VERTS[54] = {
    float3(0.000000, -0.525731, -0.850651),
    float3(-0.525731, -0.850651, 0.000000),
    float3(-0.850651, 0.000000, -0.525731),
    float3(0.000000, -0.525731, 0.850651),
    float3(-0.525731, 0.850651, 0.000000),
    float3(0.850651, 0.000000, -0.525731),
    float3(0.000000, 0.525731, -0.850651),
    float3(0.525731, -0.850651, 0.000000),
    float3(-0.850651, 0.000000, 0.525731),
    float3(0.000000, 0.525731, 0.850651),
    float3(0.525731, 0.850651, 0.000000),
    float3(0.850651, 0.000000, 0.525731),
    float3(-0.577350, -0.577350, -0.577350),
    float3(-0.577350, -0.577350, 0.577350),
    float3(-0.577350, 0.577350, -0.577350),
    float3(-0.577350, 0.577350, 0.577350),
    float3(0.577350, -0.577350, -0.577350),
    float3(0.577350, -0.577350, 0.577350),
    float3(0.577350, 0.577350, -0.577350),
    float3(0.577350, 0.577350, 0.577350),
    float3(0.000000, -0.356822, -0.934172),
    float3(-0.356822, -0.934172, 0.000000),
    float3(-0.934172, 0.000000, -0.356822),
    float3(0.000000, -0.356822, 0.934172),
    float3(-0.356822, 0.934172, 0.000000),
    float3(0.934172, 0.000000, -0.356822),
    float3(0.000000, 0.356822, -0.934172),
    float3(0.356822, -0.934172, 0.000000),
    float3(-0.934172, 0.000000, 0.356822),
    float3(0.000000, 0.356822, 0.934172),
    float3(0.356822, 0.934172, 0.000000),
    float3(0.934172, 0.000000, 0.356822),
    float3(-0.577350, -0.577350, -0.577350),
    float3(-0.577350, -0.577350, 0.577350),
    float3(-0.577350, 0.577350, -0.577350),
    float3(-0.577350, 0.577350, 0.577350),
    float3(0.577350, -0.577350, -0.577350),
    float3(0.577350, -0.577350, 0.577350),
    float3(0.577350, 0.577350, -0.577350),
    float3(0.577350, 0.577350, 0.577350),
    float3(1.000000, 0.000000, 0.000000),
    float3(-1.000000, 0.000000, 0.000000),
    float3(0.000000, 1.000000, 0.000000),
    float3(0.000000, -1.000000, 0.000000),
    float3(0.000000, 0.000000, 1.000000),
    float3(0.000000, 0.000000, -1.000000),
    float3(0.577350, 0.577350, 0.577350),
    float3(0.577350, -0.577350, -0.577350),
    float3(-0.577350, 0.577350, -0.577350),
    float3(-0.577350, -0.577350, 0.577350),
    float3(-0.577350, -0.577350, -0.577350),
    float3(-0.577350, 0.577350, 0.577350),
    float3(0.577350, -0.577350, 0.577350),
    float3(0.577350, 0.577350, -0.577350)
};
// Each edge: the two vertex numbers; edges are grouped by solid (see PW_FIRST_EDGE).
static const int2 PW_EDGES[96] = {
    int2(0, 1), int2(0, 2), int2(0, 5), int2(0, 6), int2(0, 7), int2(1, 2),
    int2(1, 3), int2(1, 7), int2(1, 8), int2(2, 4), int2(2, 6), int2(2, 8),
    int2(3, 7), int2(3, 8), int2(3, 9), int2(3, 11), int2(4, 6), int2(4, 8),
    int2(4, 9), int2(4, 10), int2(5, 6), int2(5, 7), int2(5, 10), int2(5, 11),
    int2(6, 10), int2(7, 11), int2(8, 9), int2(9, 10), int2(9, 11), int2(10, 11),
    int2(12, 20), int2(12, 21), int2(12, 22), int2(13, 21), int2(13, 23), int2(13, 28),
    int2(14, 22), int2(14, 24), int2(14, 26), int2(15, 24), int2(15, 28), int2(15, 29),
    int2(16, 20), int2(16, 25), int2(16, 27), int2(17, 23), int2(17, 27), int2(17, 31),
    int2(18, 25), int2(18, 26), int2(18, 30), int2(19, 29), int2(19, 30), int2(19, 31),
    int2(20, 26), int2(21, 27), int2(22, 28), int2(23, 29), int2(24, 30), int2(25, 31),
    int2(32, 33), int2(32, 34), int2(32, 36), int2(33, 35), int2(33, 37), int2(34, 35),
    int2(34, 38), int2(35, 39), int2(36, 37), int2(36, 38), int2(37, 39), int2(38, 39),
    int2(40, 42), int2(40, 43), int2(40, 44), int2(40, 45), int2(41, 42), int2(41, 43),
    int2(41, 44), int2(41, 45), int2(42, 44), int2(42, 45), int2(43, 44), int2(43, 45),
    int2(46, 47), int2(46, 48), int2(46, 49), int2(47, 48), int2(47, 49), int2(48, 49),
    int2(50, 51), int2(50, 52), int2(50, 53), int2(51, 52), int2(51, 53), int2(52, 53)
};

// First edge of each solid in PW_EDGES, and the end.
static const int PW_FIRST_EDGE[7] = { 0, 30, 60, 72, 84, 90, 96 };

float3x3 pwRotation(float3 angles)
{
    float sx, cx, sy, cy, sz, cz;
    sincos(angles.x, sx, cx);
    sincos(angles.y, sy, cy);
    sincos(angles.z, sz, cz);
    float3x3 rx = float3x3(1.0, 0.0, 0.0, 0.0, cx, -sx, 0.0, sx, cx);
    float3x3 ry = float3x3(cy, 0.0, sy, 0.0, 1.0, 0.0, -sy, 0.0, cy);
    float3x3 rz = float3x3(cz, -sz, 0.0, sz, cz, 0.0, 0.0, 0.0, 1.0);
    return mul(rz, mul(ry, rx));
}

// How far a nest has turned into its dual: 0 or 1 most of the time, gliding across during one beat every
// eight. Each nest takes its turn at a different beat.
float pwMorph(float layer)
{
    float beatPhase = BeatCount - saturate(Beat);
    float g = beatPhase - layer * 2.7 + 8.0;
    float n = floor(g / 4.0);
    float r = g - n * 4.0;
    float glide = smoothstep(3.0, 4.0, r);
    return (fmod(n, 2.0) < 0.5) ? glide : 1.0 - glide;
}

float3 Render(float2 uv, float2 p)
{
    float px = pixelSize();
    const float eyeDistance = 3.4;
    const float focal = 2.9;
    float2 q = rotate(p, 0.05 * sin(Time * 0.03));

    // Each nest turns about its own axis at its own pace.
    float3x3 turn[3];
    turn[0] = pwRotation(float3(Time * 0.11, Time * 0.07 + 1.0, Time * 0.03));
    turn[1] = pwRotation(float3(-Time * 0.09 + 2.0, -Time * 0.13, Time * 0.05 + 0.5));
    turn[2] = pwRotation(float3(Time * 0.17 + 0.3, Time * 0.05, -Time * 0.12));
    float radii[3] = { 0.98, 0.58, 0.31 };
    float bands[3] = { 0.62, 0.3, 0.08 };
    float3 hues[3] = { float3(0.15, 0.85, 1.0), float3(1.0, 0.25, 0.75), float3(1.0, 0.72, 0.3) };
    float morph[3] = { pwMorph(0.0), pwMorph(1.0), pwMorph(2.0) };

    float3 colour = float3(0.0, 0.0, 0.0);
    float3 haloSum = float3(0.0, 0.0, 0.0);

    [loop]
    for (int solid = 0; solid < 6; solid++)
    {
        int layer = solid / 2;
        bool dual = (solid % 2) == 1;
        float m = morph[layer];
        // The solid shrinks and fades as its dual grows through it.
        float weight = dual ? m : 1.0 - m;
        if (weight < 0.003)
            continue;
        float size = radii[layer] * (dual ? lerp(0.62, 1.0, m) : lerp(1.0, 0.62, m)) * (1.0 + Bass * 0.06);
        float3x3 rot = turn[layer];
        float level = spectrum(bands[layer] + (dual ? 0.1 : 0.0));
        float3 tint = lerp(hues[layer], float3(0.75, 0.8, 1.0), 0.15 * dual);
        float bright = (0.55 + level * 1.1 + Beat * 0.25) * weight;

        float nearest = 1e3;
        float nearestDepth = 0.0;
        [loop]
        for (int e = PW_FIRST_EDGE[solid]; e < PW_FIRST_EDGE[solid + 1]; e++)
        {
            int2 ends = PW_EDGES[e];
            float3 a = mul(rot, PW_VERTS[ends.x]) * size;
            float3 b = mul(rot, PW_VERTS[ends.y]) * size;
            float2 sa = a.xy * focal / (eyeDistance - a.z);
            float2 sb = b.xy * focal / (eyeDistance - b.z);
            float2 ab = sb - sa;
            float h = saturate(dot(q - sa, ab) / max(dot(ab, ab), 1e-8));
            float dist = length(q - sa - ab * h);
            if (dist < nearest)
            {
                nearest = dist;
                nearestDepth = lerp(a.z, b.z, h) / size;
            }
        }
        // Near edges are bright and slightly thicker, far ones dim and thin.
        float depthFade = 0.25 + 0.75 * saturate(nearestDepth * 0.5 + 0.5);
        float width = px * (0.7 + 0.6 * depthFade);
        float core = saturate((width - nearest) / px + 0.5);
        colour = max(colour, tint * core * bright * depthFade * 1.15);
        haloSum += tint * exp(-nearest / (px * 5.0)) * 0.16 * bright * depthFade;
    }

    // A soft glow fills the heart of the nest, breathing with the bass.
    float heart = exp(-dot(p, p) * 5.0);
    float3 background = float3(0.006, 0.007, 0.018) + float3(0.03, 0.02, 0.07) * heart * (0.6 + Bass * 0.8);
    colour += haloSum + background;

    // A short afterglow, so the turning lines leave the faintest wake.
    return max(colour, previous(uv) * 0.4);
}
