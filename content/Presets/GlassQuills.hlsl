// Name: Glass Quills
// A stream of glossy quills pouring out of one whorl and winding round into another: gold and coral where they
// spring up, sea green and blue as they sweep across the picture, violet where they dive back in. The quills
// lean over in a slow swirl while crests of longer quills ride steadily along the stream. The music plays in
// their light: each beat sends a gleam racing from one whorl to the other, and each ring of quills glows and
// sparks at its tips with its own part of the music, bass at the spring and treble at the dive.
// Family: Organic
// Tags: Colourful, 3D
// Added: 2026-11-16 02:23

static const float GqRound = 30.0;      // quills in each ring
static const float GqGrow = 0.2094;     // how much wider each ring is than the last, as a natural logarithm
static const float GqRings = 18.0;      // rings from the edge of one whorl to the edge of the other

float gqLoud(float x)
{
    return saturate((spectrum(x) - 0.42 + 0.5 * x) / 0.7);
}

// The colour of a quill rooted in ring k: gold and coral in the whorl the quills spring from, sea green and blue
// across the picture, some of those amber, and violet in the whorl they dive into.
float3 gqTint(float k, float pick)
{
    float3 c;
    if (k < 0.0)
    {
        float t = saturate(-k / 12.0);
        c = lerp(float3(1.45, 0.16, 0.2), float3(1.6, 0.5, 0.1), smoothstep(0.0, 0.25, t));
        c = lerp(c, float3(1.5, 1.1, 0.12), smoothstep(0.25, 0.6, t));
        c = lerp(c, float3(1.4, 1.35, 0.9), smoothstep(0.6, 1.0, t));
        if (pick > 0.8)
            c = lerp(c, float3(1.5, 0.3, 0.45), 0.6);
    }
    else
    {
        float t = saturate(k / GqRings);
        c = lerp(float3(0.05, 0.5, 0.35), float3(0.08, 1.2, 0.75), smoothstep(0.0, 0.3, t));
        c = lerp(c, float3(0.08, 0.6, 1.5), smoothstep(0.3, 0.65, t));
        c = lerp(c, float3(0.5, 0.15, 1.4), smoothstep(0.65, 0.95, t));
        c = lerp(c, float3(1.2, 0.2, 1.1), saturate((k - GqRings) / 8.0));
        if (pick > 0.84)
            c = lerp(c, float3(1.5, 0.75, 0.08), 0.85);
        c *= 0.3 + 0.7 * smoothstep(0.0, 3.0, k);
    }

    return c;
}

float3 Render(float2 uv, float2 p)
{
    float since = frac(BeatGlide);
    float flash = smoothstep(0.0, 0.1, since) * (1.0 - since) * (1.0 - since);

    // The two whorls lie either side of the middle, and the pair turns slowly. Rings run round each whorl, and
    // the quills grow largest midway between them.
    float2 q = rotate(p, 0.5 + 0.35 * sin(Time * 0.06));
    float apart = clamp(0.47 * Aspect, 0.45, 0.82);
    float2 fromSpring = q - float2(apart, 0.0);
    float2 fromDive = q + float2(apart, 0.0);
    float nearSpring = max(length(fromSpring), 1e-4);
    float nearDive = max(length(fromDive), 1e-4);
    float ring = log(nearSpring / nearDive) / GqGrow + 0.5 * GqRings;
    float turn = atan2(fromSpring.y, fromSpring.x + 1e-7) - atan2(fromDive.y, fromDive.x + 1e-7);
    turn = (turn - 2.0 * PI * floor(turn / (2.0 * PI) + 0.5)) * GqRound / (2.0 * PI);
    float cell = nearSpring * nearDive / (2.0 * apart) * GqGrow;
    float e = pixelSize();

    // The quills rooted in this ring and the eight inside it can reach this spot. Those rooted nearer the middle
    // stand nearer the eye, so they are looked at first.
    float3 colour = float3(0.0, 0.0, 0.0);
    float left = 1.0;
    [unroll]
    for (int i = 0; i < 9; i++)
    {
        float k = floor(ring) - (8.0 - i);
        float root = k + 0.5;
        float outer = step(0.0, k);

        // How this ring leans, and how long its quills are.
        float lean = 0.9 * sin(Time * 0.3 - k * 0.24) + 0.25 * sin(Time * 0.17 + k * 0.7);
        float2 along = normalize(float2(lean, 1.0));
        float2 across = float2(along.y, -along.x);
        float loud = gqLoud(0.08 + 0.6 * saturate((k + 8.0) / (GqRings + 16.0)));
        float crest = 0.5 + 0.5 * cos((k - Time * 0.55) * 2.0 * PI / 11.0);

        // Each beat sends a wave of light, not of movement, from one whorl to the other.
        float pulse = flash * exp(-(k + 8.0 - since * 36.0) * (k + 8.0 - since * 36.0) * 0.04);
        float reach = lerp(2.2, 3.6, outer) * (0.8 + 0.34 * crest * crest);

        float gap = ring - root;
        float offset = 0.5 * wrap(k, 2.0);
        float first = floor(turn - offset - along.x / along.y * gap);
        [unroll]
        for (int n = 0; n < 3; n++)
        {
            float j = first + n - 1.0;
            float2 key = float2(wrap(j, GqRound), k + 60.0);
            float h1 = hash(key * 1.7 + 3.1);
            float h2 = hash(key * 2.3 + 17.9);
            float2 w = float2(turn - (j + 0.5 + offset + 0.3 * (h1 - 0.5)), gap);
            float len = reach * (0.8 + 0.4 * h2);
            float a = dot(w, along);
            float b = dot(w, across);
            float u = saturate(a / len);
            float wide = 0.33 * (0.4 + 0.6 * u);
            float away = length(float2(a - clamp(a, 0.0, len), b)) - wide;
            float cover = saturate(0.5 - away * cell / e) * left;
            if (cover > 0.0)
            {
                // A quill is round across and glossy: darkest at its root and its edges, with a streak of
                // light along one side and a spark at its tip.
                float v = saturate(-away / wide);
                float body = sqrt(v * (2.0 - v));
                float side = b / wide;
                float3 tint = gqTint(k, h2);
                float3 quill = tint * (0.1 + 0.9 * body) * (0.3 + 0.7 * u * u);
                quill += tint * 0.6 * exp(-(side + 0.4) * (side + 0.4) * 14.0) * smoothstep(0.15, 0.6, u) * body;
                float2 tip = float2(a - (len - 0.1), b + 0.25 * wide);
                quill += float3(1.4, 1.4, 1.5) * exp(-dot(tip, tip) / (0.05 * wide * wide + 1e-4)) * (0.5 + 0.6 * loud + 0.4 * crest + 1.5 * pulse);
                colour += quill * (0.72 + 0.6 * loud + 0.6 * pulse) * cover;
                left -= cover;
            }
        }
    }

    // Below the quills, the dark of deep water, and at the heart of each whorl, where the quills grow too fine to
    // see, its glow.
    colour += left * float3(0.01, 0.03, 0.06);
    float3 heart = ring < 0.5 * GqRings ? float3(1.4, 1.3, 0.9) : float3(0.9, 0.3, 1.3);
    colour = lerp(colour, heart, smoothstep(4.5, 2.0, cell / e) * 0.8);
    return colour;
}
