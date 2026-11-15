// Name: Impasto
// Wet paint poured thick, one colour over another: green, pink, yellow, cream, and teal, each pour standing
// proud of the one beneath with a glossy lip. The paint keeps creeping, the bass pushes the pours into each
// other, and the lamp that glints off the ridges swings a little with every beat.
// Family: Organic
// Tags: Colourful, Calm
// Added: 2026-11-15 20:55

// Which pour lies on top at a point, as a number from 0 to 1 that changes smoothly across the picture.
float imPour(float2 p, float flow)
{
    float2 q = p * 0.9;
    float push = 1.5 + 0.7 * Bass;
    float2 first = float2(fbm(q + float2(0.0, flow)), fbm(q + float2(5.2, 1.3 - flow)));
    float2 second = float2(fbm(q + push * first + float2(1.7, 9.2) + flow * 0.5), fbm(q + push * first + float2(8.3, 2.8) - flow * 0.4));
    return fbm(q + 2.2 * second);
}

// The paint's thickness: each pour sits on the last, with a rounded lip and brush marks along the top.
float imThickness(float pour, float2 p)
{
    float h = 0.0;
    [unroll]
    for (int k = 0; k < 4; k++)
    {
        float lip = 0.34 + 0.07 * (float)k;
        h += smoothstep(lip - 0.02, lip + 0.02, pour);
    }

    return h * 0.3 + 0.35 * pour + 0.025 * noise(p * 55.0) + 0.02 * noise(p * float2(14.0, 120.0));
}

float3 imPaint(float pour)
{
    float3 colour = float3(0.05, 0.85, 0.35);
    colour = lerp(colour, float3(1.5, 0.28, 0.6), smoothstep(0.335, 0.345, pour));
    colour = lerp(colour, float3(1.6, 1.22, 0.12), smoothstep(0.405, 0.415, pour));
    colour = lerp(colour, float3(1.2, 1.14, 0.98), smoothstep(0.475, 0.485, pour));
    colour = lerp(colour, float3(0.05, 0.75, 0.9), smoothstep(0.545, 0.555, pour));
    return colour;
}

float3 Render(float2 uv, float2 p)
{
    float flow = Time * 0.035 + BeatGlide * 0.03;
    float e = 0.006;
    float pour = imPour(p, flow);
    float h = imThickness(pour, p);
    float right = imThickness(imPour(p + float2(e, 0.0), flow), p + float2(e, 0.0));
    float below = imThickness(imPour(p + float2(0.0, e), flow), p + float2(0.0, e));
    float3 n = normalize(float3((h - right) / e * 0.11, (h - below) / e * 0.11, 1.0));

    float rock = 0.2 * sin(BeatGlide * PI);
    float3 lamp = normalize(float3(-0.5 + rock, -0.6, 0.62));
    float lit = saturate(dot(n, lamp));
    float glint = pow(saturate(dot(n, normalize(lamp + float3(0.0, 0.0, 1.0)))), 70.0);

    float3 colour = imPaint(pour) * (0.3 + 0.95 * lit);
    colour += float3(1.2, 1.2, 1.15) * glint * (0.8 + 0.4 * BeatSwell);
    return colour * (0.92 + 0.1 * Level) * (1.0 - dot(uv - 0.5, uv - 0.5) * 0.45);
}
