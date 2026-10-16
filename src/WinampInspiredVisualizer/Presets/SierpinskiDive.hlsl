// Name: Sierpinski Dive
// Weaving between floating towers of crystal pyramids that branch forever into smaller copies.

float pyramid(float3 position)
{
    const float spacing = 3.0;
    float3 p = wrap(position, spacing) - spacing * 0.5;

    float scale = 1.0;
    [unroll]
    for (int i = 0; i < 7; i++)
    {
        // Fold across the tetrahedron's symmetry planes, then shrink towards a corner.
        if (p.x + p.y < 0.0) p.xy = -p.yx;
        if (p.x + p.z < 0.0) p.xz = -p.zx;
        if (p.y + p.z < 0.0) p.yz = -p.zy;
        p = p * 2.0 - 0.9;
        scale *= 2.0;
    }
    return (length(p) - 1.2) / scale;
}

float3 pyramidNormal(float3 p)
{
    const float2 e = float2(0.0015, -0.0015);
    return normalize(e.xyy * pyramid(p + e.xyy) + e.yyx * pyramid(p + e.yyx)
                   + e.yxy * pyramid(p + e.yxy) + e.xxx * pyramid(p + e.xxx));
}

float2 divePath(float z)
{
    // Stay in the open lanes between the towers.
    return float2(sin(z * 0.2) * 0.35, cos(z * 0.15) * 0.35);
}

float3 Render(float2 uv, float2 p)
{
    float z = Time * 0.9;
    float3 eye = float3(divePath(z), z);
    float3 direction = cameraRay(eye, float3(divePath(z + 2.0), z + 2.0), p, 0.95 - Beat * 0.1, sin(Time * 0.13) * 0.6);

    const float farPlane = 16.0;
    float travelled = 0.0;
    float steps = 0.0;
    [loop]
    for (int i = 0; i < 100; i++)
    {
        float d = pyramid(eye + direction * travelled);
        if (d < 0.0005 * travelled || travelled > farPlane)
        {
            break;
        }
        travelled += d;
        steps += 1.0;
    }

    float3 colour = 0.0;
    if (travelled < farPlane)
    {
        float3 hit = eye + direction * travelled;
        float3 normal = pyramidNormal(hit);
        float3 light = normalize(float3(-0.4, 0.7, -0.6));
        float diffuse = saturate(dot(normal, light));
        float specular = pow(saturate(dot(reflect(direction, normal), light)), 24.0);
        float rim = pow(1.0 - saturate(dot(normal, -direction)), 3.0);
        float occlusion = 1.0 - steps / 100.0;

        float3 crystal = palette(dot(normal, float3(0.3, 0.5, 0.2)) + hit.z * 0.04 + Time * 0.03);
        colour = crystal * (0.1 + diffuse * 0.9) * occlusion
               + specular * (0.6 + Treble * 1.5)
               + neon(hit.z * 0.06) * rim * (0.4 + Mid * 1.5);
        colour *= exp(-travelled * 0.15);
    }

    colour += float3(0.3, 0.6, 1.0) * (steps / 100.0) * 0.15 * (0.5 + Bass * 1.5);
    return colour + previous(uv) * 0.12;
}
