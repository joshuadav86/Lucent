// Name: Oil Bloom
// A pool of black oil on a white table, glossy as glass, with a rim that splits the light into amber and
// violet. Eight slow lobes swim inside it and bulge the edge wherever they press. Each lobe swells with its
// own part of the spectrum, the bass spreads the whole pool, and the beat rocks the highlights across it.
// Family: Organic
// Tags: Calm, Minimal
// Added: 2026-11-15 20:48

static const int Lobes = 8;

// How much oil is at a point: above 1 is inside the pool.
float obOil(float2 p, float flow)
{
    // The table isn't quite level, so the edge wanders.
    float2 q = p + 0.16 * (float2(fbm(p * 1.2 + float2(0.0, flow * 0.2)), fbm(p * 1.2 + float2(5.2, -flow * 0.17))) - 0.5);
    float oil = 0.0;

    [unroll]
    for (int i = 0; i < Lobes; i++)
    {
        float n = (float)i;
        float a = hash(float2(n, 2.2));
        float b = hash(float2(n, 6.1));
        float reach = 0.25 + 0.4 * a + 0.06 * Bass + 0.2 * DropPulse;
        float turn = flow * (0.35 + 0.5 * b) * (a > 0.5 ? 1.0 : -1.0) + n * 2.4;
        float2 centre = reach * float2(cos(turn) * 1.35, sin(turn * 1.3 + b * 3.0) * 0.85);
        float size = 0.17 + 0.1 * b + 0.13 * spectrum(0.04 + n * 0.09) + 0.015 * BeatSwell;
        oil += size * size / (dot(q - centre, q - centre) + 0.003);
    }

    return oil;
}

// The height of the oil's surface: a low dome that meets the table steeply.
float obHeight(float oil)
{
    float over = max(oil - 1.0, 0.0);
    return 1.0 - exp(-over * 1.7) + 0.16 * log(1.0 + over);
}

float3 Render(float2 uv, float2 p)
{
    float flow = Time * 0.3 + BeatGlide * 0.3;
    float oil = obOil(p, flow);
    float e = 0.006;
    float right = obOil(p + float2(e, 0.0), flow);
    float below = obOil(p + float2(0.0, e), flow);

    float h = obHeight(oil);
    float2 slope = float2(obHeight(right) - h, obHeight(below) - h) / e;
    float3 n = normalize(float3(-slope * 0.3, 1.0));

    // The table: white under a lamp, falling to grey at the corners.
    float3 table = float3(1.0, 0.99, 0.97) * (1.0 - 0.42 * smoothstep(0.3, 1.5, length(p * float2(0.75, 1.0))));

    // The oil: black, with the lamp reflected in it.
    float rock = 0.25 * sin(BeatGlide * PI);
    float3 lamp = normalize(float3(-0.35 + rock, -0.55, 0.75));
    float glint = pow(saturate(dot(n, lamp)), 90.0) + 0.25 * pow(saturate(dot(n, lamp)), 12.0);
    float3 body = float3(0.01, 0.01, 0.014) + float3(0.2, 0.22, 0.27) * (1.0 - n.z) * (1.0 - n.z);
    body += float3(1.2, 1.2, 1.15) * glint;

    // The thin rim lets light through and splits it: amber on one side of the pool, violet on the other.
    float rim = smoothstep(0.45, 0.0, h);
    float side = 0.5 + 0.5 * dot(normalize(slope + 1e-5), float2(0.8, 0.6));
    float3 split = lerp(float3(1.5, 0.6, 0.12), float3(0.55, 0.3, 1.5), smoothstep(0.2, 0.8, side));
    body = lerp(body, split, rim * (0.75 + 0.2 * Treble));

    // A soft shadow round the pool, then the pool itself.
    float nearEdge = (oil - 1.0) / max(length(float2(right - oil, below - oil)) / e, 1e-4);
    table *= 0.8 + 0.2 * smoothstep(0.0, 0.07, -nearEdge);
    float edge = pixelSize();
    float3 colour = lerp(table, body, smoothstep(-edge, edge, nearEdge));
    return colour * (0.95 + 0.07 * Level);
}
