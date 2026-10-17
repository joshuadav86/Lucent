// Name: Kaleido Cathedral
// Racing down the nave of an infinite fractal cathedral that folds itself like a kaleidoscope.
// Family: Kaleidoscope
// Tags: Intense, Colourful, 3D

float block(float3 p, float3 size)
{
    float3 q = abs(p) - size;
    return length(max(q, 0.0)) + min(max(q.x, max(q.y, q.z)), 0.0);
}

float cathedral(float3 position)
{
    float3 p = position;
    p.xy = rotate(p.xy, p.z * 0.09);
    p.xy = kaleidoscope(p.xy, 8.0);
    p.z = wrap(p.z, 3.0) - 1.5;

    float scale = 1.0;
    float twist = 0.45 + Mid * 0.15;
    [unroll]
    for (int i = 0; i < 5; i++)
    {
        p = abs(p) - float3(1.1, 0.75, 0.55);
        p.xy = rotate(p.xy, twist);
        p.xz = rotate(p.xz, 0.32);
        p *= 1.5;
        scale *= 1.5;
    }

    float structure = block(p, float3(0.9, 0.25, 0.9)) / scale;
    // An open nave down the middle to fly through.
    float nave = 0.55 - length(position.xy);
    return max(structure, nave);
}

float3 cathedralNormal(float3 p)
{
    const float2 e = float2(0.0015, -0.0015);
    return normalize(e.xyy * cathedral(p + e.xyy) + e.yyx * cathedral(p + e.yyx)
                   + e.yxy * cathedral(p + e.yxy) + e.xxx * cathedral(p + e.xxx));
}

float3 Render(float2 uv, float2 p)
{
    float z = Time * 0.9;
    float3 eye = float3(sin(Time * 0.3) * 0.15, cos(Time * 0.23) * 0.15, z);
    float3 direction = cameraRay(eye, float3(0.0, 0.0, z + 3.0), p, 0.9 - Beat * 0.1, Time * 0.07);

    const float farPlane = 18.0;
    float travelled = 0.0;
    float steps = 0.0;
    float haze = 0.0;
    [loop]
    for (int i = 0; i < 110; i++)
    {
        float d = cathedral(eye + direction * travelled);
        haze += exp(-d * 60.0);
        if (d < 0.0005 * travelled || travelled > farPlane)
        {
            break;
        }
        travelled += d * 0.8;
        steps += 1.0;
    }

    float3 colour = 0.0;
    if (travelled < farPlane)
    {
        float3 hit = eye + direction * travelled;
        float3 normal = cathedralNormal(hit);
        float3 light = normalize(float3(0.3, 0.5, -1.0));
        float diffuse = saturate(dot(normal, light));
        float rim = pow(1.0 - saturate(dot(normal, -direction)), 3.0);
        float occlusion = 1.0 - steps / 110.0;

        float band = spectrum(frac(length(hit.xy) * 0.25));
        float3 stone = palette(length(hit.xy) * 0.18 + hit.z * 0.02 + Time * 0.02);
        colour = stone * (0.1 + diffuse) * occlusion * occlusion
               + neon(hit.z * 0.05) * rim * (0.5 + band * 2.0);
        colour *= exp(-travelled * 0.14);
    }

    colour += float3(1.0, 0.45, 0.85) * haze * 0.006 * (0.5 + Bass * 1.8);
    return colour + previous(uv) * 0.1;
}
