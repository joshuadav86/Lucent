// Name: Neon Oscilloscope
// The live waveform drawn as a glowing line, leaving drifting, colour-shifting echoes.
// Family: Music-shaped
// Tags: Dark, Retro

float3 Render(float2 uv, float2 p)
{
    // Read the last frame from slightly closer in, so echoes drift outward.
    float2 source = p * (0.985 - Level * 0.01);
    source.y += 0.004 * sin(Time * 1.3 + p.x * 3.0);
    float3 trail = previous(toUv(source));
    trail = lerp(trail, trail.brg, 0.04) * 0.9;   // slowly rotate the hue of old echoes

    float wave = waveform(uv.x) * (0.35 + Mid * 0.5);
    float trace = glow(p.y - wave, 110.0 - Treble * 50.0);
    float3 colour = hsv2rgb(0.55 + 0.2 * sin(Time * 0.3) + uv.x * 0.25, 0.7, 1.0);

    return trail + colour * trace * (0.25 + Level * 1.2);
}
