// Name: Menger Voyage
// Flying through the endless corridors of a 3D fractal sponge, ray-marched on the GPU.

float box(float3 p, float3 size)
{
    float3 q = abs(p) - size;
    return length(max(q, 0.0)) + min(max(q.x, max(q.y, q.z)), 0.0);
}

// Distance to a Menger sponge that repeats forever in every direction.
float sponge(float3 position)
{
    float3 p = position;
    p.xy = rotate(p.xy, p.z * 0.12 + Time * 0.05);
    p = wrap(p + 1.0, 2.0) - 1.0;

    float dist = box(p, float3(1.0, 1.0, 1.0));
    float scale = 1.0;
    int depth = fractalDepth(4);
    [loop]
    for (int level = 0; level < depth; level++)
    {
        float3 a = wrap(p * scale, 2.0) - 1.0;
        scale *= 3.0;
        float3 r = abs(1.0 - 3.0 * abs(a));
        float holes = (min(max(r.x, r.y), min(max(r.y, r.z), max(r.z, r.x))) - 1.0) / scale;
        dist = max(dist, holes);
    }
    return dist;
}

float3 spongeNormal(float3 p)
{
    float2 e = float2(0.0015, -0.0015) * surfacePrecision();
    return normalize(e.xyy * sponge(p + e.xyy) + e.yyx * sponge(p + e.yyx)
                   + e.yxy * sponge(p + e.yxy) + e.xxx * sponge(p + e.xxx));
}

float3 Render(float2 uv, float2 p)
{
    float3 origin = float3(0.0, 0.0, Time * 0.45);
    float3 direction = normalize(float3(p * (0.75 + Beat * 0.12), 1.0));
    direction.xy = rotate(direction.xy, sin(Time * 0.1) * 0.4);

    // Detail allows more steps and a closer approach to the walls; shading stays scaled to Normal's 96.
    const float normalSteps = 96.0;
    int maxSteps = qualitySteps(96);
    float hitPrecision = 0.0004 * surfacePrecision();
    const float farPlane = 14.0;
    float travelled = 0.0;
    float steps = 0.0;
    [loop]
    for (int i = 0; i < maxSteps; i++)
    {
        float d = sponge(origin + direction * travelled);
        if (d < hitPrecision * travelled || travelled > farPlane)
        {
            break;
        }
        travelled += d;
        steps += 1.0;
    }

    float3 colour = 0.0;
    if (travelled < farPlane)
    {
        float3 hit = origin + direction * travelled;
        float3 normal = spongeNormal(hit);
        float3 light = normalize(float3(0.4, 0.6, -0.7));
        float diffuse = saturate(dot(normal, light));
        float rim = pow(1.0 - saturate(dot(normal, -direction)), 3.0);
        float occlusion = saturate(1.0 - steps / normalSteps);
        float3 surface = palette(hit.z * 0.08 + Time * 0.03 + spectrum(frac(hit.z * 0.1)) * 0.3);
        colour = surface * (0.12 + diffuse * 0.95) * occlusion
               + neon(0.6 + hit.z * 0.05) * rim * (0.4 + Treble * 1.5);
        colour *= exp(-travelled * 0.17);
    }

    // Rays that skimmed close to the walls glow, brighter with the bass.
    colour += float3(0.6, 0.3, 1.0) * min(steps / normalSteps, 1.0) * 0.18 * (0.5 + Bass * 1.5);
    return colour + previous(uv) * 0.15;
}
