// Name: Apollonian Lace
// An Apollonian gasket: circles packed inside circles forever, drawn as razor-thin
// lace that slowly zooms and spins. Beats ripple through the packing.
// Family: Fractal
// Tags: Calm, Geometric

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

float3 Render(float2 uv, float2 p)
{
    float zoom = 1.6 + 0.4 * sin(Time * 0.07);
    float2 q = kaleidoscope(rotate(p, Time * 0.04), 4.0) * zoom + float2(0.3, 0.1);

    float trap;
    float laceDistance = gasket(q, trap) / zoom;
    float lace = exp(-laceDistance * (2200.0 - Treble * 800.0));

    float3 colour = neon(sqrt(trap) * 0.8 + Time * 0.04) * lace * (0.8 + Level);
    colour += palette(trap * 0.5 + 0.2) * 0.04 * (0.5 + Mid);   // faint fill between the lines
    return colour + previous(toUv(p * 0.995)) * 0.3;
}
