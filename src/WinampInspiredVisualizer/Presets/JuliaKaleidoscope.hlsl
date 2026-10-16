// Name: Julia Kaleidoscope
// A Julia set fractal folded into five mirrored petals. Its shape drifts along the edge
// of the Mandelbrot set, so endless spirals and seahorse tails unfold.

float3 Render(float2 uv, float2 p)
{
    float2 z = kaleidoscope(rotate(p, Time * 0.03), 5.0) * (1.25 - Bass * 0.15);
    float2 c = float2(-0.745, 0.11)
        + 0.045 * float2(cos(Time * 0.21), sin(Time * 0.17))
        + float2(Mid, Treble) * 0.02;

    // Higher Detail settings iterate longer, revealing finer filaments; colours stay scaled to Normal's 110.
    const float normalIterations = 110.0;
    int maxIterations = qualitySteps(110);
    float iterations = 0.0;
    float trap = 1000.0;
    float ringRadius = 0.5 + Beat * 0.25;
    [loop]
    for (int i = 0; i < maxIterations; i++)
    {
        z = float2(z.x * z.x - z.y * z.y, 2.0 * z.x * z.y) + c;
        trap = min(trap, abs(length(z) - ringRadius));
        if (dot(z, z) > 256.0)
        {
            break;
        }
        iterations += 1.0;
    }

    float3 colour = 0.0;
    if (iterations < maxIterations)
    {
        // Smooth colouring: no visible bands between iteration counts.
        float smoothed = iterations - log2(log2(dot(z, z))) + 4.0;
        float t = smoothed / normalIterations;
        colour = palette(t * 4.0 + Time * 0.04) * pow(saturate(t * 2.2), 0.7);
    }

    // Glowing rings where the orbit passes near the trap circle.
    colour += neon(0.3 + trap * 1.5 + Time * 0.03) * exp(-trap * 35.0) * (0.35 + Level * 1.2);
    return colour + previous(uv) * 0.2;
}
