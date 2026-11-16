// Name: Filament
// Nine old-fashioned glass bulbs hang on their cords in a dark room, swaying a little, each with a coiled filament
// turning slowly inside it. Every bulb mirrors its neighbours as tiny sparks on its glass and throws a pool of warm
// light on the plaster wall behind and a long streak on the polished floor, with more bulbs out of focus further off. The music plays in the
// filaments: each bulb burns with its own part of the music, bass at the left and treble at the right, from a dull
// red glow up to white heat, and each beat sends a wave of brightness along the row.
// Family: Ray traced
// Tags: 3D, Dark, Calm
// Added: 2026-11-16 05:22

static const int FlBulbs = 9;
static const float FlGlobe = 0.17;      // how big a bulb's glass is
static const float FlWall = 1.9;        // how far behind the bulbs the wall stands

static float3 FlAt[FlBulbs];            // where each bulb hangs just now
static float FlHeat[FlBulbs];           // how hot each filament burns
static float FlHook[FlBulbs];           // how far across the ceiling each cord is hooked

float flLoud(float x)
{
    return saturate((spectrum(x) - 0.42 + 0.5 * x) / 0.7);
}

// The light of a filament at one heat: dull red, through orange, to white.
float3 flHot(float heat)
{
    float3 c = lerp(float3(0.5, 0.04, 0.0), float3(1.5, 0.42, 0.04), smoothstep(0.05, 0.45, heat));
    c = lerp(c, float3(1.9, 1.15, 0.5), smoothstep(0.4, 0.8, heat));
    return lerp(c, float3(2.1, 1.8, 1.4), smoothstep(0.75, 1.1, heat)) * (0.25 + 1.2 * heat);
}

// How far a spot on the picture is from a straight wire between two others.
float flWire(float2 q, float2 a, float2 b)
{
    float2 along = b - a;
    float h = saturate(dot(q - a, along) / max(dot(along, along), 1e-6));
    return length(q - a - along * h);
}

float3 Render(float2 uv, float2 p)
{
    float since = frac(BeatGlide);
    float flash = smoothstep(0.0, 0.1, since) * (1.0 - since) * (1.0 - since);
    float2 q = float2(p.x, -p.y);

    // Where the bulbs hang and how hot they burn. Each swings gently on its cord at its own pace.
    [unroll]
    for (int n = 0; n < FlBulbs; n++)
    {
        float h1 = hash(float2(n, 5.3));
        float h2 = hash(float2(n, 11.9));
        float h3 = hash(float2(n, 17.1));
        float across = ((n + 0.5) / FlBulbs * 2.0 - 1.0) * 0.88 * Aspect + 0.1 * (h1 - 0.5);
        float drop = 3.02 - (h2 - 0.5) * 0.95 + 0.22 * sin(n * 2.4);
        float swing = 0.035 * sin(Time * 0.43 + 6.2832 * h1) + 0.02 * sin(Time * 0.29 + n * 1.7);
        FlAt[n] = float3(across + drop * sin(swing), 3.0 - drop * cos(swing), (h3 - 0.5) * 1.5);
        FlHook[n] = across;

        float loud = flLoud(0.08 + 0.6 * (n + 0.5) / FlBulbs);
        float wave = flash * exp(-(n / (FlBulbs - 1.0) - since * 1.7 + 0.2) * (n / (FlBulbs - 1.0) - since * 1.7 + 0.2) * 9.0);
        FlHeat[n] = 0.14 + 0.7 * loud + 0.2 * wave;
    }

    // The plaster wall, lit by a pool of light from every bulb.
    float3 onWall = float3(q * (4.0 + FlWall) / 4.0, FlWall);
    float plaster = 0.75 + 0.25 * fbm(onWall.xy * 3.0) + 0.06 * noise(onWall.xy * 40.0);
    float3 wallLight = float3(0.012, 0.01, 0.014);
    float3 halo = float3(0.0, 0.0, 0.0);
    [unroll]
    for (int m = 0; m < FlBulbs; m++)
    {
        float3 away = onWall - FlAt[m];
        wallLight += flHot(FlHeat[m]) * 0.13 / (0.6 + dot(away, away) * 1.6);

        // The soft glow round each bulb.
        float scale = 4.0 / (4.0 + FlAt[m].z);
        float off = length(q - FlAt[m].xy * scale) / (FlGlobe * scale);
        halo += flHot(FlHeat[m]) * 0.08 * exp(-off * 0.95) * FlHeat[m];
    }

    float3 colour = float3(0.5, 0.42, 0.36) * plaster * wallLight;

    // A polished floor at the foot of the wall mirrors every bulb as a long soft streak.
    float floorLine = -0.7;
    float onFloor = smoothstep(floorLine + 0.004, floorLine - 0.004, q.y);
    if (onFloor > 0.0)
    {
        float3 streaks = float3(0.0, 0.0, 0.0);
        [unroll]
        for (int r = 0; r < FlBulbs; r++)
        {
            float scale = 4.0 / (4.0 + FlAt[r].z);
            float2 mirrored = float2(FlAt[r].x * scale, 2.0 * floorLine - FlAt[r].y * scale);
            float2 from = q - mirrored;
            streaks += flHot(FlHeat[r]) * FlHeat[r] * exp(-from.x * from.x / (0.004 + 0.02 * (floorLine - q.y))) * exp(-abs(from.y) * 1.6) * 0.3;
        }

        float3 boards = float3(0.03, 0.022, 0.02) * (0.7 + 0.3 * noise(float2(q.x * 3.0, q.y * 60.0))) * (wallLight * 13.0 + 0.02);
        colour = lerp(colour, boards + streaks * (0.75 + 0.25 * noise(float2(q.x * 40.0, q.y * 9.0))), onFloor);
    }

    // More bulbs further down the room, out of focus.
    [unroll]
    for (int b = 0; b < 7; b++)
    {
        float2 spot = float2((hash(float2(b, 31.7)) * 2.0 - 1.0) * Aspect * 0.95, (hash(float2(b, 47.3)) - 0.5) * 1.1 + 0.15)
                    + 0.012 * float2(sin(Time * 0.3 + b), cos(Time * 0.23 + b * 2.0));
        float burn = 0.12 + 0.75 * flLoud(0.1 + 0.07 * b);
        float disc = smoothstep(0.075, 0.055, length(q - spot));
        colour += flHot(burn) * 0.09 * disc * (0.6 + 0.4 * smoothstep(0.02, 0.07, length(q - spot)));
    }

    // The cords, then the bulbs themselves, nearest last.
    float covered = 1e3;
    int nearest = -1;
    [unroll]
    for (int c = 0; c < FlBulbs; c++)
    {
        float scale = 4.0 / (4.0 + FlAt[c].z);
        float2 middle = FlAt[c].xy * scale;
        float2 top = middle + float2(0.0, FlGlobe * 1.55) * scale;
        float cord = flWire(q, top, float2(FlHook[c], 3.0) * scale);
        colour = lerp(colour, float3(0.006, 0.005, 0.005), smoothstep(0.006 * scale, 0.003 * scale, cord));

        float off = length(q - middle) / (FlGlobe * scale);
        float2 capAt = (q - middle) / scale - float2(0.0, FlGlobe * 1.25);
        float onCap = step(abs(capAt.x), 0.062) * step(abs(capAt.y), FlGlobe * 0.33);
        if ((off < 1.0 || onCap > 0.5) && FlAt[c].z < covered)
        {
            covered = FlAt[c].z;
            nearest = c;
        }
    }

    if (nearest >= 0)
    {
        float3 at = FlAt[nearest];
        float heat = FlHeat[nearest];
        float3 hot = flHot(heat);
        float scale = 4.0 / (4.0 + at.z);
        float2 d = (q - at.xy * scale) / scale;
        float off = length(d) / FlGlobe;

        // The brass cap above the glass, ridged with its screw thread and lit from below by its own filament.
        float2 capAt = d - float2(0.0, FlGlobe * 1.25);
        if (abs(capAt.x) < 0.062 && abs(capAt.y) < FlGlobe * 0.33 && off >= 1.0)
        {
            float bend = capAt.x / 0.062;
            float thread = 0.75 + 0.25 * sin(capAt.y * 230.0 + bend * 2.0);
            float3 brass = float3(0.75, 0.5, 0.18) * thread * (0.05 + 0.5 * pow(saturate(1.0 - abs(bend + 0.35)), 3.0));
            colour = brass + float3(0.9, 0.55, 0.2) * hot * 0.12 * thread * saturate(0.5 - capAt.y / (FlGlobe * 0.66));
        }
        else if (off < 1.0)
        {
            float3 n = float3(d / FlGlobe, -sqrt(saturate(1.0 - off * off)));

            // Seen through the glass: the wall behind, a little tinted, and the filament's light filling the bulb.
            float3 inside = colour * float3(0.9, 0.84, 0.74) + hot * 0.06 * heat * sqrt(saturate(1.0 - off * off));

            // The filament: a coil turning slowly, held on two thin wires from the cap.
            float turn = Time * 0.25 + nearest * 1.9;
            float fromCoil = 1.0;
            float2 last = float2(0.0, 0.0);
            [unroll]
            for (int k = -5; k <= 5; k++)
            {
                float coil = clamp(d.y + k * 0.0045, -0.075, 0.06);
                float2 onCoil = float2(0.052 * cos(coil * 150.0 + turn), coil);
                if (k > -5)
                    fromCoil = min(fromCoil, flWire(d, last, onCoil));
                last = onCoil;
            }

            float posts = min(flWire(d, float2(0.052 * cos(0.06 * 150.0 + turn), 0.06), float2(0.02, FlGlobe * 0.95)),
                              flWire(d, float2(0.052 * cos(-0.075 * 150.0 + turn), -0.075), float2(-0.02, FlGlobe * 0.95)));
            inside = lerp(inside, float3(0.1, 0.08, 0.07) + hot * 0.05, smoothstep(0.004, 0.002, posts));
            inside += hot * (1.7 * smoothstep(0.0055, 0.0015, fromCoil) + 0.5 * exp(-fromCoil * 85.0) * heat + 0.2 * heat * exp(-dot(d, d) / 0.006));

            // The glass mirrors two tall windows, and each of the other bulbs as a spark.
            float3 bounce = reflect(float3(0.0, 0.0, 1.0), n);
            float mirror = 0.05 + 0.95 * pow(saturate(1.0 + n.z), 4.0);
            float windows = smoothstep(0.1, 0.02, abs(bounce.x + 0.55)) * smoothstep(0.42, 0.3, abs(bounce.y - 0.25))
                          + 0.5 * smoothstep(0.07, 0.02, abs(bounce.x - 0.62)) * smoothstep(0.35, 0.25, abs(bounce.y - 0.1));
            float3 sparks = float3(0.0, 0.0, 0.0);
            [unroll]
            for (int j = 0; j < FlBulbs; j++)
            {
                float3 toOther = FlAt[j] - at;
                float apart = dot(toOther, toOther);
                sparks += flHot(FlHeat[j]) * FlHeat[j] * pow(saturate(dot(bounce, toOther / sqrt(apart + 1e-4))), 260.0) * step(0.01, apart);
            }

            colour = inside * (1.0 - 0.5 * mirror) + float3(0.5, 0.56, 0.7) * windows * 0.6 + sparks * 1.4
                   + (wallLight * 10.0 + float3(0.03, 0.03, 0.045)) * mirror * 0.6;
        }
    }

    colour += halo;
    return colour * (1.0 - 0.18 * dot(p, p) / (1.0 + Aspect * Aspect));
}
