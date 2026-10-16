// Name: Kaleidoscope Bloom
// Mirrored petals that fold the last frame back into itself and open wider with the bass.

float3 Render(float2 uv, float2 p)
{
    float2 folded = kaleidoscope(rotate(p, Time * 0.1), 8.0);

    // Reading the last frame through the same mirrors keeps everything symmetrical,
    // and reading slightly closer to the centre makes it bloom outward.
    float2 source = rotate(folded, 0.02 + Mid * 0.03) * (0.97 - Bass * 0.04);
    float3 trail = previous(toUv(source)) * 0.92;

    float radius = length(p);
    float petal = spectrum(saturate(folded.y * 1.5 + 0.1));
    float shape = glow(folded.x - (0.25 + petal * 0.55), 60.0) * smoothstep(0.0, 0.08, folded.y);
    float3 colour = hsv2rgb(0.8 + radius * 0.3 + Time * 0.03, 0.75, 1.0) * shape * (0.25 + petal);

    float core = glow(radius, 10.0) * (0.05 + Beat * 0.4);
    return trail + colour * 0.5 + hsv2rgb(Time * 0.05, 0.4, core);
}
