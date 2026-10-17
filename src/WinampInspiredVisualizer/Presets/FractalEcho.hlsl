// Name: Fractal Echo
// A hall of mirrors: each corner holds a smaller, spinning copy of the whole picture.
// Family: Kaleidoscope
// Tags: Dark, Neon, Retro

float3 Render(float2 uv, float2 p)
{
    float spin = Time * 0.15 + Bass * 0.4;

    // Fold the screen into quadrants and map each one onto the whole last frame.
    float2 folded = abs(rotate(p, spin));
    float2 source = (folded - float2(Aspect * 0.5, 0.5)) * 2.0;
    float3 echo = previous(toUv(rotate(source, -spin * 0.5)));
    echo = lerp(echo, echo.gbr, 0.05) * (0.88 + Mid * 0.06);

    // Something new to echo: a spectrum ring and the waveform through the middle.
    float radius = length(p);
    float level = spectrum(frac(atan2(p.y, p.x) / (2.0 * PI) + 0.5));
    float ring = glow(radius - 0.3 - level * 0.25, 120.0);
    float wave = glow(p.y - waveform(uv.x) * 0.25, 160.0) * smoothstep(1.2, 0.0, abs(p.x));
    float3 colour = hsv2rgb(Time * 0.05 + radius * 0.4, 0.8, 1.0) * (ring * (0.3 + level) + wave * Level * 1.5);

    return echo + colour * 0.6;
}
