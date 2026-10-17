// Name: Neon Lattice
// Streaking through an endless grid of glowing beams that light up with the music.
// Family: Cyberpunk
// Tags: Intense, Neon, 3D

float lattice(float3 p, out float3 cell)
{
    cell = floor(p * 0.5);
    float3 q = wrap(p, 2.0) - 1.0;
    float beams = min(min(length(q.yz), length(q.xz)), length(q.xy));
    return beams - 0.035;
}

float3 Render(float2 uv, float2 p)
{
    float z = Time * 2.2;
    float3 eye = float3(sin(Time * 0.21) * 0.45, cos(Time * 0.17) * 0.45, z);
    float3 target = float3(sin(Time * 0.21 + 0.4) * 0.45, cos(Time * 0.17 + 0.4) * 0.45, z + 2.0);
    float3 direction = cameraRay(eye, target, p, 1.0 - Beat * 0.15, sin(Time * 0.11) * 0.7);

    const float farPlane = 24.0;
    float travelled = 0.0;
    float3 colour = 0.0;
    [loop]
    for (int i = 0; i < 90; i++)
    {
        float3 position = eye + direction * travelled;
        float3 cell;
        float d = lattice(position, cell);

        // Each beam picks a frequency band and colour from its cell, so the grid flickers with the music.
        float band = hash(cell.xy + cell.z * 7.31);
        float energy = 0.25 + spectrum(band) * 3.0;
        float3 tint = neon(band + position.z * 0.02 + Time * 0.05);
        colour += tint * energy * 0.0045 / (0.0006 + d * d) * exp(-travelled * 0.12) * 0.02;

        if (d < 0.001 || travelled > farPlane)
        {
            break;
        }
        travelled += max(d * 0.75, 0.02);
    }

    // A pulse that rushes outward from the centre on each beat.
    float ring = glow(length(p) - (1.0 - Beat) * 1.5, 25.0) * Beat * 0.4;
    colour += float3(0.5, 0.8, 1.0) * ring;
    return colour + previous(uv) * 0.2;
}
