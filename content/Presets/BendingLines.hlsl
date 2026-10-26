// Name: Bending Lines
// Fine parallel lines run across a dark page and bend smoothly around invisible weights that drift through them;
// the weights grow heavier with the bass, and the treble sends the faintest shiver along every line.
// Family: Minimal
// Tags: Minimal, Calm, Dark
// Added: 2026-10-17 21:41

static const float LineSpacing = 0.034;

// Where one invisible weight is at time t.
float2 blMass(float i, float t)
{
    float a = t * (0.07 + i * 0.023) + i * 2.1;
    float b = t * (0.05 + i * 0.017) + i * 4.3;
    return float2(sin(a) * Aspect * 0.75, sin(b) * 0.62);
}

// The radius of each weight's invisible core, which grows with the bass.
float blRadius(float i)
{
    return (0.07 + 0.025 * i) * (0.75 + 0.45 * Bass + 0.2 * Beat);
}

// Lines flow round each weight the way water streams round a stone (potential flow past a cylinder).
// Returns how far the page is displaced here; 'inside' is 1 within a core, where no line is drawn.
float blBend(float2 p, out float inside)
{
    float shift = 0.0;
    inside = 0.0;
    [unroll]
    for (int i = 0; i < 3; i++)
    {
        float2 d = p - blMass(i, Time);
        float radius = blRadius(i);
        float r2 = max(dot(d, d), 1e-4);
        shift += radius * radius * d.y / r2;
        inside = max(inside, 1.0 - smoothstep(radius * radius * 0.92, radius * radius * 1.0, r2));
    }
    return shift;
}

float3 Render(float2 uv, float2 p)
{
    float px = pixelSize();
    float inside = 0.0;
    float bend = blBend(p, inside);
    float y = p.y - bend;

    // A barely visible shiver from the treble and the waveform, strongest in the middle of the page.
    float x01 = p.x / (2.0 * Aspect) + 0.5;
    float centreWeight = exp(-p.x * p.x * 0.8);
    y += (waveform(x01) * 0.006 * (0.3 + Mid) + sin(p.x * 70.0 - Time * 5.0) * 0.0012 * Treble) * centreWeight;

    // The page scrolls slowly upwards so the lines are always moving.
    float phase = (y + Time * 0.01) / LineSpacing;
    float distanceToLine = abs(frac(phase) - 0.5) * LineSpacing;
    // How far apart the lines are on screen here, so each stays a crisp hairline however much it is squeezed.
    float squeeze = max(length(float2(ddx(phase), ddy(phase))) / px * LineSpacing, 1e-3);
    float width = px * 0.55;
    float cover = saturate((width - (distanceToLine / squeeze) * 1.0) / px + 0.5);
    // Where lines crowd together they fade to a soft grey rather than shimmer.
    float crowd = saturate(squeeze * px / LineSpacing * 3.0);
    cover = lerp(cover, 0.35, smoothstep(0.35, 1.0, crowd));

    // Lines brighten a little where they are stretched round a weight, as if under tension.
    float tension = saturate(abs(bend) * 6.0);
    float3 ink = lerp(float3(0.78, 0.80, 0.84), float3(1.0, 0.95, 0.88), tension) * (0.7 + 0.3 * Level + tension * 0.35);

    float3 page = float3(0.010, 0.011, 0.014) + float3(0.012, 0.012, 0.016) * (1.0 - smoothstep(0.4, 1.6, length(p)));
    // The cores themselves stay empty, with only a faint warm rim of light where the lines hug them.
    cover *= 1.0 - inside;
    return lerp(page, ink, cover) + float3(1.0, 0.8, 0.6) * 0.04 * inside * (0.5 + Bass);
}
