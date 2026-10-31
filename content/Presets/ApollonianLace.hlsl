// Name: Apollonian Lace
// An Apollonian gasket, circles packed inside circles forever, drawn in glowing neon: razor-thin lace
// round every circle with rings of light nested inside each one like contour lines, slowly zooming and
// spinning in fourfold symmetry. Each ring plays its own band of the spectrum, the packing swells with
// the bass, and every beat sends a pulse of light inward through the rings.
// Family: Fractal
// Tags: Neon, Geometric
// Added: 2026-10-16 20:19

float gasket(float2 p, out float trap)
{
    float scale = 1.0;
    trap = 1000.0;
    float strength = 1.12 + Bass * 0.12 + Beat * 0.05;
    [unroll]
    for (int i = 0; i < 9; i++)
    {
        p = -1.0 + 2.0 * frac(0.5 * p + 0.5);
        float r2 = dot(p, p);
        trap = min(trap, r2);
        float k = strength / max(r2, 0.0001);
        p *= k;
        scale *= k;
    }
    return 0.25 * abs(p.y) / scale;
}

// Neon from teal through blue and violet to magenta and gold.
float3 laceNeon(float t)
{
    return hsv2rgb(frac(0.47 + saturate(t) * 0.62 + sin(Time * 0.02) * 0.03), 0.85, 1.0);
}

float3 Render(float2 uv, float2 p)
{
    float zoom = 1.6 + 0.4 * sin(Time * 0.07);
    float2 q = kaleidoscope(rotate(p, Time * 0.04), 4.0) * zoom + float2(0.3, 0.1);

    float trap;
    float laceDistance = gasket(q, trap) / zoom;

    // The lace itself: a hot fine line round every circle, white at its core.
    float lace = exp(-laceDistance * (2200.0 - Treble * 800.0));
    float3 colour = lerp(laceNeon(sqrt(trap) * 1.1), 1.0, lace * lace * 0.5) * lace * (0.7 + Level * 0.8);

    // Rings of light nested inside each circle like contour lines, round the point the fold
    // keeps closest to its centre.
    float ring = sqrt(trap) * 14.0;
    float index = floor(ring);
    float width = max(fwidth(ring), 1e-4);
    float f = frac(ring);
    float contour = 1.0 - smoothstep(0.0, width * 1.4, min(f, 1.0 - f));
    float depth = saturate(index / 13.0);
    // Each ring plays its own band, bass at the lace and treble deep inside the circles.
    float level = spectrum(0.03 + depth * 0.6);
    // A pulse runs inward through the rings over each beat. With a beat to follow it rides through
    // the whole beat; without one it shows just as it lands.
    float at = (depth - BeatPhase) * 7.0;
    float pulse = exp(-at * at) * (SongMapped > 0.0 ? 0.35 + Beat * 0.65 : Beat);
    float fadeIn = 1.0 - lace;
    colour += laceNeon(depth + sqrt(trap) * 0.15) * contour * fadeIn * (0.12 + level * 0.9 + pulse * 0.8) * (0.8 + Intensity * 0.4);

    // A faint glow inside each circle, deepest at its heart.
    colour += laceNeon(depth) * 0.012 * (0.5 + Mid) * fadeIn;
    return colour + previous(toUv(p * 0.995)) * 0.3;
}
