// Name: Hilbert Curve
// One unbroken glowing line folds itself into a maze that fills a square, growing finer and finer with each phrase
// before starting over, while bright pulses race along its whole length and the square breathes with the bass.
// Family: Geometric
// Tags: Geometric, Neon, Minimal

static const int HcMaxOrder = 7;

// Position along the Hilbert curve of grid cell (x, y) on an n x n grid (n a power of two).
// At each scale, pick the quadrant, add the cells the curve visits before it, then turn the quadrant to match.
int hcIndex(int n, int2 cell)
{
    int x = cell.x;
    int y = cell.y;
    int d = 0;
    int s = n / 2;
    [loop]
    for (int i = 0; i < HcMaxOrder; i++)
    {
        if (s < 1)
        {
            break;
        }
        int rx = (x & s) > 0 ? 1 : 0;
        int ry = (y & s) > 0 ? 1 : 0;
        d += s * s * ((3 * rx) ^ ry);
        if (ry == 0)
        {
            if (rx == 1)
            {
                x = n - 1 - x;
                y = n - 1 - y;
            }
            int t = x;
            x = y;
            y = t;
        }
        s /= 2;
    }
    return d;
}

// Grid cell of position d along the Hilbert curve on an n x n grid.
int2 hcCell(int n, int d)
{
    int x = 0;
    int y = 0;
    int t = d;
    int s = 1;
    [loop]
    for (int i = 0; i < HcMaxOrder; i++)
    {
        if (s >= n)
        {
            break;
        }
        int rx = 1 & (t / 2);
        int ry = 1 & (t ^ rx);
        if (ry == 0)
        {
            if (rx == 1)
            {
                x = s - 1 - x;
                y = s - 1 - y;
            }
            int tmp = x;
            x = y;
            y = tmp;
        }
        x += s * rx;
        y += s * ry;
        t /= 4;
        s *= 2;
    }
    return int2(x, y);
}

// Distance from p to segment a-b, and how far along it the closest point lies.
float2 hcSegment(float2 p, float2 a, float2 b)
{
    float2 ab = b - a;
    float h = saturate(dot(p - a, ab) / max(dot(ab, ab), 1e-6));
    return float2(length(p - a - ab * h), h);
}

// Distance to the curve of the given order near grid point g, and the curve position (0..1) of the closest point.
float2 hcCurve(float2 g, float n)
{
    int ni = (int)(n + 0.5);
    float best = 1e9;
    float along = 0.0;
    float total = n * n;
    float2 nearest = floor(g);
    float2 lean = sign(frac(g) - 0.5);
    // Check this cell and its three nearest neighbours, so glow crosses cell borders cleanly.
    for (int k = 0; k < 4; k++)
    {
        float2 cell = nearest + float2(k & 1, k >> 1) * lean;
        if (cell.x < 0.0 || cell.y < 0.0 || cell.x >= n || cell.y >= n)
        {
            continue;
        }
        int d = hcIndex(ni, int2(cell));
        float2 centre = cell + 0.5;
        if (d > 0)
        {
            float2 seg = hcSegment(g, centre, float2(hcCell(ni, d - 1)) + 0.5);
            if (seg.x < best)
            {
                best = seg.x;
                along = (float)d - seg.y * 0.5;
            }
        }
        if (d < ni * ni - 1)
        {
            float2 seg = hcSegment(g, centre, float2(hcCell(ni, d + 1)) + 0.5);
            if (seg.x < best)
            {
                best = seg.x;
                along = (float)d + seg.y * 0.5;
            }
        }
    }
    return float2(best, along / max(total - 1.0, 1.0));
}

float3 Render(float2 uv, float2 p)
{
    float px = pixelSize();
    float clock = max(BeatCount - Beat, 0.0);

    // One order per bar: 2, 3 ... 6, then back to 2. Each new order draws itself in along the curve.
    float bar = clock / 4.0;
    float order = 2.0 + fmod(floor(bar), 5.0);
    float reveal = smoothstep(0.0, 0.75, frac(bar));

    // The square slowly turns and zooms with the music.
    float size = 1.55 * (1.0 + Bass * 0.06 + Beat * 0.015);
    float2 q = rotate(p, 0.08 * sin(Time * 0.09)) / size + 0.5;
    float3 colour = float3(0.004, 0.006, 0.012) + float3(0.01, 0.015, 0.03) * (1.0 - length(p) * 0.5);

    float n = exp2(order);
    float2 hit = hcCurve(q * n, n);
    float distance = hit.x / n * size;
    float along = hit.y;

    // The previous order lingers under the parts the new one hasn't reached yet.
    float nOld = exp2(order > 2.5 ? order - 1.0 : 6.0);
    float2 hitOld = hcCurve(q * nOld, nOld);
    float distanceOld = hitOld.x / nOld * size;

    float inside = step(abs(q.x - 0.5), 0.5 + 0.5 / n) * step(abs(q.y - 0.5), 0.5 + 0.5 / n);
    float shown = step(along, reveal) * inside;
    float shownOld = (1.0 - reveal) * step(abs(q.x - 0.5), 0.5) * step(abs(q.y - 0.5), 0.5);

    // Colour flows along the curve from teal through blue to violet.
    float hue = 0.48 + 0.3 * along + 0.04 * sin(Time * 0.2);
    float3 tint = hsv2rgb(hue, 0.75, 1.0);

    // Line width shrinks as the curve gets finer, but never below a pixel and a half.
    float width = max(0.012 / exp2(order * 0.6), 1.5 * px);
    float core = 1.0 - smoothstep(width - px, width + px, distance);
    float cellSize = size / n;
    float halo = exp(-distance / min(width * 3.0 + 0.004, cellSize * 0.12)) * 0.25;
    float stroke = core * 0.55 + halo;

    // Pulses race along the curve: one sweeps steadily, others are launched by the beats.
    float pulse = exp(-abs(along - frac(Time * 0.07)) * 60.0);
    float beatPulse = exp(-abs(along - frac(clock * 0.125 + 0.5)) * 40.0) * (0.4 + Beat);
    float spark = pulse + beatPulse;
    // The band at this point of the curve lifts the line a little.
    float band = spectrum(along * 0.85 + 0.02);

    colour += tint * stroke * shown * (0.55 + band * 0.7 + Level * 0.3);
    colour += float3(1.0, 0.95, 0.9) * core * shown * spark * 1.2;
    colour += tint * exp(-distance / (width * 1.5 + px)) * shown * spark * 0.8;
    colour += hsv2rgb(0.62, 0.6, 1.0) * (1.0 - smoothstep(width - px, width + px, distanceOld)) * shownOld * 0.25;

    // A drawing head glows where the new order is being laid down.
    if (reveal < 0.999)
    {
        colour += tint * exp(-abs(along - reveal) * 120.0) * core * shown * 2.0;
    }
    return colour;
}
