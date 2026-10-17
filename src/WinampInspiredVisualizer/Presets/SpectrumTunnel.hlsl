// Name: Spectrum Tunnel
// A ring of frequencies whose trail spins and falls away into a tunnel.
// Family: Music-shaped
// Tags: Colourful, Dark

float3 Render(float2 uv, float2 p)
{
    // Read the last frame from slightly further out and rotated, so it zooms inward.
    float2 source = rotate(p, 0.012 + Bass * 0.03) * (1.035 + Beat * 0.04);
    float3 trail = previous(toUv(source)) * 0.93;

    float radius = length(p);
    float angle = atan2(p.y, p.x);
    float x = abs(angle) / PI;            // mirror the spectrum around the ring
    float level = spectrum(x);

    float ringRadius = 0.28 + Bass * 0.12;
    float ring = glow(radius - (ringRadius + level * 0.32), 140.0);
    float3 colour = hsv2rgb(x * 0.5 + Time * 0.04, 0.8, 1.0) * ring * (0.35 + level);

    // A soft flash in the centre on each beat.
    float flash = Beat * glow(radius, 6.0) * 0.25;

    return trail + colour * 0.6 + hsv2rgb(Time * 0.04 + 0.5, 0.6, flash);
}
