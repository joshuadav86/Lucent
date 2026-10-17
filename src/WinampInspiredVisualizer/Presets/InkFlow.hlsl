// Name: Ink Flow
// Marbled inks swirl on black water in glossy combed ribbons edged with gold, each ribbon glowing with its own slice of the spectrum.
// Family: Organic
// Tags: Calm, Colourful, Water

// Twists the plane around a drifting vortex. The twist winds up and slowly unwinds again.
float2 inkVortex(float2 q, float seed)
{
    float2 centre = float2(sin(Time * 0.031 + seed * 2.1) * 1.1, cos(Time * 0.027 + seed * 1.3) * 0.6);
    float2 d = q - centre;
    float strength = (1.5 + Bass * 0.8) * sin(Time * 0.045 + seed * 2.7);
    return centre + rotate(d, strength * exp(-dot(d, d) * 2.2));
}

// Smooth turbulence without the finest octaves, so ribbon edges stay clean.
float inkTurbulence(float2 q)
{
    return noise(q) * 0.55 + noise(q * 2.1 + 5.7) * 0.3 + noise(q * 4.3 + 1.9) * 0.15;
}

// The ink's flow field: vortices, then broad waves, then gentle turbulence.
float inkField(float2 p)
{
    float2 q = p * 0.85;
    q = inkVortex(q, 0.0);
    q = inkVortex(q, 1.0);
    q = inkVortex(q, 2.0);
    float t = Time * 0.08;
    [unroll]
    for (int i = 1; i < 5; i++)
    {
        float k = i;
        q.x += 0.5 / k * sin(k * 1.9 * q.y + t * (1.0 + 0.3 * k) + k * 1.3);
        q.y += 0.5 / k * cos(k * 1.6 * q.x - t * (0.8 + 0.2 * k) + k * 2.1);
    }
    q += (float2(inkTurbulence(q * 1.6 + t), inkTurbulence(q * 1.6 + 7.3 - t)) - 0.5) * 0.5;
    return q.x * 0.6 + q.y * 0.3 + inkTurbulence(q * 0.9 + 3.0) * 0.4;
}

// One of three ink sets; black ink is the most common in each.
float3 inkSet(float pick, float set)
{
    float3 a = set < 0.5 ? float3(0.0, 0.4, 0.42) : (set < 1.5 ? float3(0.0, 0.42, 0.22) : float3(0.62, 0.06, 0.04));
    float3 b = set < 0.5 ? float3(0.72, 0.2, 0.1) : (set < 1.5 ? float3(0.06, 0.16, 0.62) : float3(0.85, 0.35, 0.05));
    float3 c = set < 0.5 ? float3(0.82, 0.58, 0.2) : (set < 1.5 ? float3(0.4, 0.1, 0.55) : float3(0.55, 0.08, 0.25));
    float3 d = set < 0.5 ? float3(0.8, 0.77, 0.7) : (set < 1.5 ? float3(0.85, 0.65, 0.25) : float3(0.95, 0.75, 0.35));
    float3 colour = float3(0.0, 0.0, 0.0);
    colour = pick > 0.42 ? a : colour;
    colour = pick > 0.6 ? b : colour;
    colour = pick > 0.76 ? c : colour;
    colour = pick > 0.9 ? d : colour;
    return colour;
}

// The ink colour of a ribbon. The ink sets take turns, cross-fading every half minute or so.
float3 inkColour(float index)
{
    float pick = frac(index * 0.618034 + 0.31);
    float phase = Time / 35.0;
    float set = wrap(floor(phase), 3.0);
    float fade = smoothstep(0.8, 1.0, frac(phase));
    return lerp(inkSet(pick, set), inkSet(pick, wrap(set + 1.0, 3.0)), fade);
}

float3 Render(float2 uv, float2 p)
{
    const float bands = 4.0;
    const float e = 0.003;
    float field = inkField(p);
    float2 gradient = float2(inkField(p + float2(e, 0.0)) - field, inkField(p + float2(0.0, e)) - field) / e;

    // The ribbons travel steadily across the flow, so new ones keep welling up from the vortices.
    float x = field * bands - Time * 0.12;
    float index = floor(x);
    float across = frac(x);
    float pixel = max(length(gradient) * bands * 2.0 / Resolution.y, 1e-4);   // one pixel, in band units

    // Fine combed stripes run along most ribbons; each stripe is a little glossy bead of ink.
    float combs = 5.0 + floor(hash(float2(index, 3.7)) * 4.0) * 2.0;
    float combed = step(0.25, hash(float2(index, 9.1)));
    float comb = frac(x * combs);
    float combEdge = min(comb, 1.0 - comb) / (pixel * combs);
    // Fade the combing out where its stripes would be too close together to draw cleanly.
    combed *= smoothstep(1.3, 2.6, 1.0 / (pixel * combs));
    float hairline = lerp(1.0, smoothstep(0.4, 1.6, combEdge), combed);
    float slope = cos(comb * PI) * PI * combs * combed + cos(across * PI) * PI * 1.5;
    float3 normal = normalize(float3(-gradient * bands * slope * 0.004, 1.0));
    float3 light = normalize(float3(cos(Time * 0.21) * 0.8, sin(Time * 0.17) * 0.6, 1.0));
    float specular = pow(saturate(dot(reflect(-light, normal), float3(0.0, 0.0, 1.0))), 50.0);
    float shade = 0.35 + 0.65 * saturate(dot(normal, light));

    // Every ribbon listens to its own part of the spectrum.
    float level = spectrum(frac(index * 0.137 + 0.03));
    float3 inkBase = inkColour(index);
    float3 ink = inkBase * shade * (0.45 + 0.55 * hairline) * (0.4 + level * 1.5);
    // Black ribbons keep faint blue-violet combing so they never go flat.
    ink += float3(0.03, 0.035, 0.09) * (1.0 - hairline) * step(dot(inkBase, inkBase), 0.0001);

    // Hairline gold veins where ribbons meet.
    float edge = min(across, 1.0 - across) / pixel;
    float vein = exp(-edge * edge * 0.6);
    float glitter = step(0.985, hash(floor(p * Resolution.y * 0.5) + floor(Time * 12.0))) * Treble * 3.0;
    float3 gold = float3(1.0, 0.7, 0.3) * vein * (0.6 + Beat * 1.0 + glitter);

    float wet = 0.25 + 0.75 * step(0.0001, dot(inkBase, inkBase));
    return ink + gold + specular * wet * (0.35 + Mid * 0.6) * float3(1.0, 0.96, 0.9);
}
