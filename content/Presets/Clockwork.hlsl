// Name: Clockwork
// The inside of a watch, seen from above: brass, gold, and steel wheels on a dark plate, every one meshed
// with its neighbours so that the whole train turns together, with a ruby at each hub and blued screws
// between. The train ticks forward on the beat, and each ruby glows with its own part of the music.
// Family: Geometric
// Tags: Geometric, Retro, Dark
// Added: 2026-11-15 21:05

static const float Teeth = 12.0;

// One wheel at the origin: teeth round the rim, windows cut between the spokes. Negative inside the metal.
float cwWheel(float2 q, float angle, float spokes)
{
    float r = length(q);
    float a = atan2(q.y, q.x + 1e-6) - angle;
    float rim = 0.44 + 0.1 * smoothstep(0.0, 0.45, cos(a * Teeth));
    float sector = 2.0 * PI / spokes;
    float fromSpoke = abs(wrap(a + sector * 0.5, sector) - sector * 0.5) * r;
    float window = max(max(0.15 - r, r - 0.33), 0.05 - fromSpoke);
    return max(r - rim, -window);
}

// The nearest wheel of the train to g, and which wheel that is. Next-door wheels turn opposite ways, half a
// tooth apart, so the teeth of one always sit in the gaps of the other.
float cwTrain(float2 g, float spin, out float2 cell)
{
    float2 home = floor(g + 0.5);
    float2 local = g - home;
    float2 side = float2(local.x > 0.0 ? 1.0 : -1.0, local.y > 0.0 ? 1.0 : -1.0);
    float best = 1e9;
    cell = home;

    [unroll]
    for (int k = 0; k < 3; k++)
    {
        float2 c = home + (k == 1 ? float2(side.x, 0.0) : k == 2 ? float2(0.0, side.y) : float2(0.0, 0.0));
        bool odd = wrap(c.x + c.y, 2.0) > 0.5;
        float angle = odd ? PI / Teeth - spin : spin;
        float spokes = 3.0 + floor(hash(wrap(c, 64.0) + 0.37) * 3.99);
        float d = cwWheel(g - c, angle, spokes);
        if (d < best)
        {
            best = d;
            cell = c;
        }
    }

    return best;
}

float3 cwMetal(float pick)
{
    if (pick < 0.4) return float3(1.3, 0.95, 0.36);
    if (pick < 0.62) return float3(1.5, 1.12, 0.26);
    if (pick < 0.82) return float3(0.86, 0.92, 1.02);
    return float3(1.4, 0.72, 0.48);
}

float3 Render(float2 uv, float2 p)
{
    float4 heard = float4(spectrum(0.03), spectrum(0.12), spectrum(0.3), spectrum(0.55));
    float spin = Time * 0.1 + BeatGlide * 0.26;
    float2 g = rotate(p, 0.3 + 0.06 * sin(Time * 0.07)) * 1.7 + float2(wrap(Time * 0.035, 64.0), 0.3);
    float2 lamp = normalize(float2(-0.6, -0.8));

    // The plate: dark steel, turned in small overlapping circles, with a blued screw between every four wheels.
    float2 turned = frac(g * 3.0) - 0.5;
    float3 colour = float3(0.085, 0.1, 0.13) * (0.9 + 0.12 * sin(length(turned) * 60.0) + 0.1 * sin(atan2(turned.y, turned.x + 1e-6) * 3.0));
    float2 corner = floor(g) + 0.5;
    float2 fromScrew = g - corner;
    float screw = length(fromScrew);
    float slotTurn = hash(wrap(corner, 64.0) + 5.5) * PI;
    float slot = abs(dot(fromScrew, float2(cos(slotTurn), sin(slotTurn))));
    float3 blued = float3(0.1, 0.24, 0.85) * (0.75 - 3.0 * dot(fromScrew, lamp)) * smoothstep(0.008, 0.018, slot);
    colour = lerp(colour, blued, smoothstep(0.092, 0.084, screw));

    // The wheels throw a shadow on the plate.
    float2 cell;
    float shadow = cwTrain(g - lamp * -0.045, spin, cell);
    colour *= 1.0 - 0.6 * smoothstep(0.04, -0.03, shadow);

    float d = cwTrain(g, spin, cell);
    float2 unused;
    float e = 0.004;
    float2 slope = float2(cwTrain(g + float2(e, 0.0), spin, unused) - d, cwTrain(g + float2(0.0, e), spin, unused) - d) / e;
    slope /= max(length(slope), 1e-3);

    float2 q = g - cell;
    float r = length(q);
    float pick = hash(wrap(cell, 64.0) + 1.9);
    float band = frac(pick * 7.0);
    float loud = band < 0.25 ? heard.x : band < 0.5 ? heard.y : band < 0.75 ? heard.z : heard.w;

    // Brass, brushed in circles, with a bright bevel on the edges that face the lamp.
    float brushed = 0.88 + 0.07 * sin(r * 330.0) + 0.05 * sin(atan2(q.y, q.x + 1e-6) * 48.0 + r * 30.0);
    float bevel = smoothstep(-0.03, 0.0, d) * dot(slope, lamp);
    float sweep = 0.75 + 0.35 * saturate(0.5 - dot(q, lamp) * 1.1);
    float3 wheel = cwMetal(pick) * brushed * sweep * (1.0 + 0.9 * bevel);
    colour = lerp(colour, wheel, smoothstep(0.004, -0.004, d));

    // The ruby in its gold setting.
    float3 setting = float3(1.5, 1.1, 0.3) * (0.8 - 2.5 * dot(q, lamp));
    colour = lerp(colour, setting, smoothstep(0.094, 0.086, r));
    float3 ruby = float3(1.5, 0.06, 0.2) * (0.55 + 1.3 * loud + 0.3 * BeatSwell) + float3(1.0, 0.9, 0.9) * smoothstep(0.022, 0.008, length(q - lamp * 0.022));
    colour = lerp(colour, ruby, smoothstep(0.062, 0.054, r));

    return colour * (0.94 + 0.08 * Level) * (1.0 - dot(uv - 0.5, uv - 0.5) * 0.6);
}
