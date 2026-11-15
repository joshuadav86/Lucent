// Name: Gumdrops
// A spectrum made of sweets: rows of red, blue, orange, and cyan drops on white paper. Each column listens to
// one part of the spectrum, bass on the left. As it gets louder the drops nearest the middle swell and run
// together into a bar that climbs both ways, while the quiet ones stay small dots that bob on the beat.
// Family: Music-shaped
// Tags: Colourful, Minimal, Retro
// Added: 2026-11-15 20:44

static const float Cell = 0.3;

float3 gdSweet(float pick)
{
    if (pick < 0.25) return float3(1.45, 0.2, 0.18);
    if (pick < 0.5) return float3(0.14, 0.38, 1.4);
    if (pick < 0.75) return float3(1.6, 0.72, 0.08);
    return float3(0.12, 1.15, 1.3);
}

// The drops near p: returns how far p is from their joined outline (negative inside), and their blended colour.
float gdDrops(float2 p, float slide, out float3 tint)
{
    float2 g = float2(p.x + slide, p.y) / Cell;
    float2 home = floor(g);
    float total = 0.0;
    float claims = 0.0;
    tint = float3(0.0, 0.0, 0.0);

    [unroll]
    for (int j = -1; j <= 1; j++)
    {
        [unroll]
        for (int i = -1; i <= 1; i++)
        {
            float2 id = home + float2(i, j);
            float2 key = float2(wrap(id.x, 97.0), id.y);
            float2 centre = id + 0.5;
            // Where this drop sits on the screen decides what it hears and how soon it joins the bar.
            float2 seat = float2(centre.x * Cell - slide, centre.y * Cell);
            float across = saturate(seat.x / (2.0 * Aspect) + 0.5);
            float heard = (spectrum(across * 0.7 + 0.01) + spectrum(across * 0.7 + 0.03)) * 0.5;
            float joined = saturate((heard * 1.25 + 0.1 * Level - abs(seat.y)) * 3.5 + 0.1);

            float bob = BeatGlide * PI + hash(key) * 6.2832;
            centre += 0.07 * float2(sin(bob), cos(bob * 0.5)) * (1.0 - joined);
            float size = 0.13 + 0.42 * joined + 0.03 * BeatSwell * hash(key + 3.3);
            float d = length(g - centre) - size;

            total += exp(-d * 13.0);
            // Colours meet along a much sharper line than the shapes do.
            float claim = exp(-d * 60.0);
            claims += claim;
            tint += claim * gdSweet(hash(float2(key.x, 4.1)));
        }
    }

    tint /= max(claims, 1e-20);
    return -log(max(total, 1e-6)) / 13.0 * Cell;
}

float3 Render(float2 uv, float2 p)
{
    float slide = Time * 0.02;
    float3 tint;
    float3 unused;
    float d = gdDrops(p, slide, tint);
    float shade = gdDrops(p - float2(0.016, 0.022), slide, unused);
    float edge = pixelSize();

    // Paper, with a soft shadow under each sweet.
    float3 paper = float3(0.97, 0.95, 0.89) * (0.985 + 0.03 * noise(p * 140.0));
    paper *= 0.8 + 0.2 * smoothstep(-0.012, 0.03, shade);

    // The sweets are flat colour, a touch deeper in the middle, with a small shine near the top left.
    float3 sweet = tint * (0.86 + 0.14 * smoothstep(-0.07, 0.0, d));
    float shine = smoothstep(0.03, 0.0, abs(gdDrops(p + float2(0.02, 0.028), slide, unused) + 0.012));
    sweet += 0.25 * shine * smoothstep(0.0, -0.02, d);

    float3 colour = lerp(sweet, paper, smoothstep(-edge, edge, d));
    return colour * (1.0 - dot(uv - 0.5, uv - 0.5) * 0.25);
}
