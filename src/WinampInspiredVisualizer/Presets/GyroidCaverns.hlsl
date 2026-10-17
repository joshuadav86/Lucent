// Name: Gyroid Caverns
// Gliding down a winding tunnel through endlessly curving neon walls.
// Family: Fly-through
// Tags: Colourful, 3D

float2 cavePath(float z)
{
    return float2(sin(z * 0.31) * 1.4, cos(z * 0.22) * 1.1);
}

float cave(float3 position)
{
    float3 q = position * 1.7;
    float sheet = abs(dot(sin(q), cos(q.zxy))) / 1.7 - 0.035 - Bass * 0.03;
    // Keep a clear tube around the flight path so the camera never hits a wall.
    float tunnel = 0.8 - length(position.xy - cavePath(position.z));
    return max(sheet, tunnel) * 0.7;
}

float3 caveNormal(float3 p)
{
    const float2 e = float2(0.002, -0.002);
    return normalize(e.xyy * cave(p + e.xyy) + e.yyx * cave(p + e.yyx)
                   + e.yxy * cave(p + e.yxy) + e.xxx * cave(p + e.xxx));
}

float3 Render(float2 uv, float2 p)
{
    float z = Time * 1.1;
    float3 eye = float3(cavePath(z), z);
    float3 target = float3(cavePath(z + 2.0), z + 2.0);
    float3 direction = cameraRay(eye, target, p, 1.0 - Beat * 0.12, sin(Time * 0.17) * 0.5);

    const float farPlane = 16.0;
    float travelled = 0.0;
    float haze = 0.0;
    int maxSteps = qualitySteps(100);
    [loop]
    for (int i = 0; i < maxSteps; i++)
    {
        float d = cave(eye + direction * travelled);
        haze += exp(-d * 40.0) * 0.012;
        if (d < 0.0006 * travelled || travelled > farPlane)
        {
            break;
        }
        travelled += d;
    }

    float3 colour = 0.0;
    if (travelled < farPlane)
    {
        float3 hit = eye + direction * travelled;
        float3 normal = caveNormal(hit);
        float facing = saturate(dot(normal, -direction));
        float rim = pow(1.0 - facing, 4.0);

        // Fine glowing contour lines etched into the walls, pulsing with the music.
        float contour = abs(frac(dot(hit, float3(0.7, 0.5, 1.3)) * 2.0 - Time * 0.4) - 0.5);
        float lines = glow(contour, 30.0) * (0.4 + spectrum(frac(hit.z * 0.07)) * 2.5);

        float3 surface = palette(hit.z * 0.05 + Time * 0.02) * (0.08 + facing * 0.5);
        colour = surface + neon(hit.z * 0.04 + 0.3) * (lines + rim * (0.6 + Treble * 1.5));
        colour *= exp(-travelled * 0.16);
    }

    colour += neon(Time * 0.03 + 0.6) * haze * (0.6 + Bass * 1.6);
    return colour + previous(uv) * 0.12;
}
