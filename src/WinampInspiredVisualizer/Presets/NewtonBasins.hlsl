// Name: Newton Basins
// Newton's method hunting five drifting roots: spiralling chains of glowing beads mark the borders where the basins meet.

float2 nbMul(float2 a, float2 b)
{
    return float2(a.x * b.x - a.y * b.y, a.x * b.y + a.y * b.x);
}

float2 nbInv(float2 a)
{
    return float2(a.x, -a.y) / max(dot(a, a), 1.0e-20);
}

float2 nbRoot(int k, float t)
{
    float fk = (float)k;
    float angle = fk * (2.0 * PI / 5.0) + t * 0.05 + 0.35 * sin(t * 0.13 + fk * 1.7);
    float radius = 1.0 + 0.32 * sin(t * (0.11 + fk * 0.023) + fk * 2.1);
    return float2(cos(angle), sin(angle)) * radius;
}

float3 nbHue(float h)
{
    return 0.5 + 0.5 * cos(2.0 * PI * (h + float3(0.0, 0.33, 0.67)));
}

float3 Render(float2 uv, float2 p)
{
    float zoom = 1.6 * exp2(-1.1 * (0.5 + 0.5 * sin(Time * 0.071)));
    float2 z = rotate(p, -Time * 0.03) * zoom + 0.35 * float2(sin(Time * 0.05), cos(Time * 0.037));
    float pixel = 2.0 * zoom / min(Resolution.y, 540.0);   // glow and detail scale: 1/540ths of the screen at high resolutions
    float linePixel = 2.0 * zoom * pixelSize();            // line widths: two screen pixels, so they stay crisp at 4K

    float2 roots[5];
    [unroll]
    for (int r = 0; r < 5; r++)
    {
        roots[r] = nbRoot(r, Time);
    }

    // Relaxed Newton: z -= a f/f'. Near each root the step rotates by 1 - a, which coils the borders into spirals.
    float twist = 0.55 * sin(Time * 0.083) + Mid * 0.25;
    float2 relax = float2(1.0, 0.0) - 0.42 * float2(cos(twist), sin(twist));
    float contraction = length(float2(1.0, 0.0) - relax);

    float2 dz = float2(1.0, 0.0);
    float stepSize = 1.0;
    float n = 0.0;
    const float tolerance = 1.0e-4;
    int iterations = qualitySteps(60);
    [loop]
    for (int i = 0; i < iterations; i++)
    {
        float2 s1 = 0.0;
        float2 s2 = 0.0;
        [unroll]
        for (int j = 0; j < 5; j++)
        {
            float2 inv = nbInv(z - roots[j]);
            s1 += inv;
            s2 += nbMul(inv, inv);
        }
        float2 newtonStep = nbMul(relax, nbInv(s1));
        dz = nbMul(dz, float2(1.0, 0.0) - nbMul(relax, nbMul(s2, nbInv(nbMul(s1, s1)))));
        z -= newtonStep;
        stepSize = length(newtonStep);
        if (stepSize < tolerance)
        {
            break;
        }
        n += 1.0;
    }

    // Which root did we land on?
    float best = 1.0e9;
    float rootIndex = 0.0;
    float2 rootPos = roots[0];
    [unroll]
    for (int m = 0; m < 5; m++)
    {
        float d = dot(z - roots[m], z - roots[m]);
        if (d < best)
        {
            best = d;
            rootIndex = (float)m;
            rootPos = roots[m];
        }
    }

    // Koenigs coordinates: log(z_n - root) - n log(1 - a) is continuous across the basin and turns it into
    // a log-polar plane, so straight lines drawn there coil into spirals that wind into the fractal border.
    float2 w = z - rootPos;
    float2 multiplier = float2(1.0, 0.0) - relax;
    float2 logW = float2(0.5 * log(max(dot(w, w), 1.0e-30)), atan2(w.y, w.x));
    float2 logM = float2(log(contraction), atan2(multiplier.y, multiplier.x));
    float2 koenigs = logW - n * logM;
    float cellsAround = 6.0;
    float2 grid = koenigs * (cellsAround / (2.0 * PI));
    grid.x -= Time * 0.35;
    float cellsPerPixel = length(dz) / max(length(w), 1.0e-30) * (cellsAround / (2.0 * PI)) * pixel;

    // A web of log-spirals in those coordinates: thin lines, antialiased by how many cells fit in a pixel.
    float2 spiral = float2(grid.x + grid.y * 0.5, grid.y - grid.x * 0.5);
    float2 toLine = abs(frac(spiral) - 0.5);
    float cellsPerLinePixel = cellsPerPixel * (linePixel / pixel);
    float2 linePx = (0.5 - toLine) / max(cellsPerLinePixel, 1.0e-6);
    float web = saturate(1.2 - linePx.x * 0.9) + saturate(1.2 - linePx.y * 0.9) * 0.6;
    web *= saturate(1.2 - cellsPerPixel * 4.0);

    float de = 1.0 / max(cellsPerPixel, 1.0e-12);           // in pixels: one cell, roughly the distance to the border
    float hueShift = (BeatCount - Beat) * 0.03 + Time * 0.01;
    float3 basin = nbHue(rootIndex / 5.0 + hueShift + grid.x * 0.015);

    float depthToBorder = saturate(1.0 - log2(max(de, 1.0)) / 9.0);   // 1 at the border, 0 deep in the basin
    float border = saturate(1.3 - 0.7 / max(cellsPerLinePixel, 1.0e-12));
    float3 colour = basin * (0.02 + 0.12 * depthToBorder * depthToBorder + web * (0.2 + 0.7 * depthToBorder))
                  + lerp(basin, 1.0, 0.25) * border * (0.8 + Bass * 0.6 + Beat * 0.3)
                  + basin * exp(-de * 0.35) * 0.3 * (0.5 + Bass)
                  + float3(0.6, 0.8, 1.0) * border * Treble * 0.4;
    return colour + previous(uv) * 0.12;
}
