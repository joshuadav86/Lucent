// Name: Crystal Kaleidoscope
// A folding crystal fractal: each fold mirrors, spins and shrinks the pattern again,
// so fine glowing lines nest inside each other. The bass pumps the folds open.

float3 Render(float2 uv, float2 p)
{
    float2 z = kaleidoscope(rotate(p, Time * 0.05), 6.0) * (1.1 - Beat * 0.08);
    float scale = 1.65 + Bass * 0.25;
    float angle = Time * 0.11 + Mid * 0.4;
    float2 centre = float2(0.7 + 0.15 * sin(Time * 0.23), 0.35 + 0.1 * cos(Time * 0.31));
    float sharpness = 900.0 - Treble * 300.0;

    float lines = 0.0;
    float trap = 1000.0;
    float shrink = 1.0;
    [loop]
    for (int i = 0; i < 9; i++)
    {
        z = abs(z);
        if (z.x < z.y)
        {
            z = z.yx;
        }
        z = rotate(z, angle);
        z = z * scale - centre * (scale - 1.0);
        shrink *= scale;

        // The fold line, measured back in screen space so every level stays equally thin.
        lines += exp(-abs(z.y) / shrink * sharpness) / (1.0 + i * 0.2);
        trap = min(trap, length(z) / shrink);
    }

    float3 colour = neon(trap * 4.0 + Time * 0.05) * saturate(lines) * (0.7 + Level * 1.2);
    colour += palette(0.6 + trap * 2.0 + Time * 0.02) * exp(-trap * 45.0) * (0.25 + Beat * 0.8);

    // A short echo for depth, without smearing the detail.
    return colour + previous(toUv(p * 0.985)) * 0.45;
}
