// Name: Mixing Desk
// A wall of white studio hardware drifting slowly past: knobs that turn, faders that ride, meters, pads, speaker
// grilles and little screens, every one of them wired to the music. Each control follows its own band of the
// spectrum, the pads step through the beats, the grilles thump with the bass and the screens draw the waveform.
// Family: Music-shaped
// Tags: Geometric, Minimal, Retro
// Added: 2026-11-15 19:50

// The wall is a grid of square panels, some of them four squares big. Each panel is given one kind of control
// at random. Everything is drawn flat, but anything that stands out from its panel (a knob, a fader cap, a pad,
// a button) is drawn three times over: once where it is, once nudged towards the lamp to find which edges
// catch the light, and once nudged further to lay its shadow on the panel.
static const float2 Lamp = float2(-0.6, -0.8);         // the way to the light, across the wall: up and to the left
static const float3 Amber = float3(1.7, 1.05, 0.15);
static const float3 Yellow = float3(1.8, 1.35, 0.2);
static const float3 Red = float3(1.8, 0.3, 0.2);
static const float3 Mint = float3(0.3, 1.5, 0.9);

float mdBox(float2 q, float2 half0, float corner)
{
    float2 a = abs(q) - half0 + corner;
    return length(max(a, 0.0)) + min(max(a.x, a.y), 0.0) - corner;
}

// The parts of a panel that stand out from it. kind 0 is a knob, 1 three faders, 2 pads, 3 two buttons;
// the other kinds are flat.
float mdRaised(float kind, float2 q, float3 v, float pads)
{
    if (kind < 0.5)
    {
        return length(q) - 0.5;
    }

    if (kind < 1.5)
    {
        float a = mdBox(q - float2(-0.5, 0.55 - 1.1 * v.x), float2(0.15, 0.08), 0.03);
        float b = mdBox(q - float2(0.0, 0.55 - 1.1 * v.y), float2(0.15, 0.08), 0.03);
        float c = mdBox(q - float2(0.5, 0.55 - 1.1 * v.z), float2(0.15, 0.08), 0.03);
        return min(a, min(b, c));
    }

    if (kind < 2.5)
    {
        float2 u = (q / 0.88 * 0.5 + 0.5) * pads;
        float2 id = clamp(floor(u), 0.0, pads - 1.0);
        float2 local = (u - id - 0.5) * (1.76 / pads);
        return mdBox(local, 0.7 / pads, 0.16 / pads);
    }

    if (kind < 3.5)
    {
        return length(float2(abs(q.x) - 0.42, q.y - 0.3)) - 0.24;
    }

    return 1.0;
}

// How loud the music is at x (0 deep bass, 1 high treble), stretched so that ordinary music swings it most of
// the way between 0 and 1 instead of sitting near the top.
float mdLoud(float x)
{
    return saturate((spectrum(x) - 0.56 + 0.5 * x) / 0.4);
}

float3 Render(float2 uv, float2 p)
{
    float beatLight = SongMapped > 0.0 ? 0.35 + Beat * 0.65 : Beat;

    // The wall drifts past, tilted a little.
    float2 wall = rotate(p, -0.1) * (1.1 + 0.06 * sin(Time * 0.05)) + float2(Time * 0.03, Time * 0.011);
    float2 g = wall / 0.5;
    float2 block = floor(g * 0.5);
    bool big = hash(block + 3.7) < 0.4;
    float2 cell = big ? block * 2.0 : floor(g);
    float size = big ? 2.0 : 1.0;
    float2 q = (g - cell) / size * 2.0 - 1.0;
    float px = pixelSize() * 1.16 / (0.25 * size);
    float pick = hash(cell + 11.3 + size);
    float kind = big
        ? (pick < 0.3 ? 4.0 : (pick < 0.55 ? 5.0 : (pick < 0.75 ? 2.0 : (pick < 0.9 ? 0.0 : 1.0))))
        : (pick < 0.3 ? 0.0 : (pick < 0.48 ? 1.0 : (pick < 0.62 ? 6.0 : (pick < 0.76 ? 3.0 : (pick < 0.88 ? 2.0 : (pick < 0.94 ? 4.0 : 5.0))))));
    float pads = big ? 4.0 : 2.0;

    // Each panel listens to three bands of its own, and idles a little when the music is quiet.
    float3 bands = float3(hash(cell + 5.1), hash(cell + 9.4), hash(cell + 2.8));
    float3 v = float3(mdLoud(0.07 + bands.x * 0.62), mdLoud(0.07 + bands.y * 0.62), mdLoud(0.07 + bands.z * 0.62));
    v = saturate(0.08 + v * 0.9 + 0.08 * sin(Time * (0.3 + bands * 0.5) + bands * 40.0));

    // The panel: white enamel, a dark seam round it, and a screw in each corner.
    float3 colour = float3(0.98, 0.98, 1.0) * (0.9 + 0.1 * hash(cell + 21.0)) * (1.0 + 0.04 * dot(q, Lamp));
    float border = mdBox(q, 0.965, 0.05);
    float borderLit = mdBox(q + Lamp * 0.03, 0.965, 0.05);
    colour *= 1.0 + 0.12 * clamp((borderLit - border) / 0.03, -1.0, 1.0) * smoothstep(-0.07, 0.0, border);
    colour = lerp(colour, float3(0.22, 0.23, 0.26), smoothstep(-px, px, border));
    float screw = length(abs(q) - 0.84) - 0.035;
    colour = lerp(colour, float3(0.5, 0.51, 0.55), smoothstep(px, -px, screw));

    if (kind < 0.5)
    {
        // Knob: a ring of small lamps round it, lit up to where it points.
        float angle = atan2(q.x, abs(q.y) < 1e-6 ? 1e-6 : -q.y);
        float along = angle / (0.75 * PI) * 0.5 + 0.5;
        float i = clamp(floor(along * 12.0 + 0.5), 0.0, 12.0);
        float at = (i / 12.0 * 2.0 - 1.0) * 0.75 * PI;
        float2 spot = float2(sin(at), -cos(at)) * 0.72;
        float bulb = length(q - spot) - 0.045;
        float lit = saturate(v.x * 12.0 - i + 0.5);
        colour = lerp(colour, lerp(float3(0.6, 0.61, 0.65), Amber, lit), smoothstep(px, -px, bulb));
    }
    else if (kind < 1.5)
    {
        // Faders: a slot for each, with marks beside it.
        float lane = abs(q.x - clamp(floor(q.x / 0.5 + 0.5), -1.0, 1.0) * 0.5);
        float slot = max(lane - 0.03, abs(q.y) - 0.68);
        colour = lerp(colour, float3(0.14, 0.15, 0.18), smoothstep(px, -px, slot));
        float mark = max(abs(lane - 0.215) - 0.03, abs(frac(q.y * 5.0 + 0.5) - 0.5) / 5.0 - 0.012);
        mark = max(mark, abs(q.y) - 0.62);
        colour = lerp(colour, float3(0.55, 0.56, 0.6), smoothstep(px, -px, mark) * 0.9);
    }
    else if (kind > 2.5 && kind < 3.5)
    {
        // Buttons: a small lamp over each, taking turns on the beat.
        float bulb = length(float2(abs(q.x) - 0.42, q.y + 0.42)) - 0.075;
        float turn = wrap(floor(BeatGlide) + (q.x > 0.0 ? 1.0 : 0.0) + floor(bands.x * 2.0), 2.0);
        float lit = (turn < 0.5 ? 1.0 : 0.0) * (1.0 - 0.7 * frac(BeatGlide));
        colour = lerp(colour, lerp(float3(0.42, 0.2, 0.2), Red, lit), smoothstep(px, -px, bulb));
    }
    else if (kind > 3.5 && kind < 4.5)
    {
        // Speaker grille: a sunk disc of holes that swell and glow with the bass.
        float disc = length(q) - 0.8;
        float thump = 0.3 * (mdLoud(0.08) + mdLoud(0.14)) + BeatSwell * 0.4;
        float ripple = 0.5 + 0.5 * sin(length(q) * 9.0 - BeatPhase * 2.0 * PI);
        float2 h = rotate(q, 0.7854) * 7.0;
        float hole = length(frac(h) - 0.5) - (0.26 + 0.07 * thump * ripple);
        float3 mesh = lerp(float3(0.56, 0.57, 0.61), lerp(float3(0.07, 0.07, 0.09), Amber * 0.85, saturate(thump * ripple)), smoothstep(px * 7.0, -px * 7.0, hole));
        mesh *= 1.0 - 0.45 * smoothstep(-0.07, 0.03, length(q - Lamp * 0.07) - 0.8);
        colour = lerp(colour, mesh, smoothstep(px, -px, disc));
        colour = lerp(colour, float3(0.7, 0.71, 0.75), smoothstep(px, -px, abs(disc) - 0.02));
    }
    else if (kind > 4.5 && kind < 5.5)
    {
        // Screen: dark glass with a faint grid, drawing the sound wave.
        float glass = mdBox(q, float2(0.8, 0.6), 0.08);
        float trace = abs(q.y - waveform(saturate(q.x / 1.6 + 0.5)) * 0.45);
        float3 screen = float3(0.04, 0.05, 0.06);
        float grid = min(abs(frac(q.x * 2.5) - 0.5), abs(frac(q.y * 2.5) - 0.5)) / 2.5;
        screen += 0.05 * smoothstep(px * 1.5, 0.0, grid);
        screen += Mint * (smoothstep(px * 2.0 + 0.012, 0.0, trace) + 0.2 * exp(-trace * 18.0));
        screen *= 1.0 - 0.5 * smoothstep(-0.07, 0.03, mdBox(q - Lamp * 0.07, float2(0.8, 0.6), 0.08));
        colour = lerp(colour, screen, smoothstep(px, -px, glass));
    }
    else if (kind > 5.5)
    {
        // Meter: two columns of ten lamps, mint, then amber, then red at the top.
        float row = clamp(floor((0.75 - q.y) / 0.15), 0.0, 9.0);
        float bulb = mdBox(float2(abs(q.x) - 0.3, q.y - (0.75 - (row + 0.5) * 0.15)), float2(0.19, 0.05), 0.02);
        float lit = saturate((q.x < 0.0 ? v.x : v.y) * 10.0 - row);
        float3 hot = row > 7.5 ? Red : (row > 5.5 ? Amber : Mint);
        colour = lerp(colour, lerp(float3(0.66, 0.67, 0.7), hot, lit), smoothstep(px, -px, bulb));
    }

    // Whatever stands out from the panel: its shadow first, then the part itself with its edges catching the light.
    float d = mdRaised(kind, q, v, pads);
    float dLit = mdRaised(kind, q + Lamp * 0.05, v, pads);
    float dShade = mdRaised(kind, q + Lamp * 0.11, v, pads);
    float3 paint = float3(0.84, 0.85, 0.89);
    if (kind < 0.5)
    {
        // The knob's top, its skirt, and the pointer.
        float turned = (v.x * 2.0 - 1.0) * 0.75 * PI;
        float2 way = float2(sin(turned), -cos(turned));
        float top = smoothstep(px, -px, length(q) - 0.4);
        paint = lerp(float3(0.62, 0.63, 0.67), float3(0.95, 0.95, 0.98) * (1.0 + 0.05 * dot(q, Lamp)), top);
        float pointer = length(q - way * clamp(dot(q, way), 0.12, 0.36)) - 0.03;
        paint = lerp(paint, float3(0.1, 0.1, 0.12), smoothstep(px, -px, pointer));
    }
    else if (kind < 1.5)
    {
        // A coloured line across each fader cap.
        float which = clamp(floor(q.x / 0.5 + 0.5), -1.0, 1.0);
        float level0 = which < -0.5 ? v.x : (which < 0.5 ? v.y : v.z);
        float stripe = abs(q.y - (0.55 - 1.1 * level0)) - 0.013;
        paint = lerp(paint, Amber, smoothstep(px, -px, stripe));
    }
    else if (kind < 2.5)
    {
        // Pads light one after another on the beat, with the last one still fading, and glow with their bands.
        float2 u = (q / 0.88 * 0.5 + 0.5) * pads;
        float2 id = clamp(floor(u), 0.0, pads - 1.0);
        float count = pads * pads;
        float k = id.x + id.y * pads;
        float now = wrap(floor(BeatGlide) + floor(bands.x * count), count);
        float before = wrap(now - 1.0 + count, count);
        float through = frac(BeatGlide);
        float glow = (abs(k - now) < 0.5 ? 1.0 - 0.6 * through : 0.0) + (abs(k - before) < 0.5 ? 0.4 * (1.0 - through) : 0.0);
        glow += mdLoud(0.07 + k / count * 0.62) * 0.8;
        paint = lerp(paint, Yellow, saturate(glow));
    }

    colour *= 1.0 - 0.4 * smoothstep(0.1, -0.06, dShade);
    float slope = clamp((dLit - d) / 0.05, -1.0, 1.0);
    float3 body = paint * (1.0 + 0.2 * slope * smoothstep(-0.14, -0.02, d));
    colour = lerp(colour, body, smoothstep(px, -px, d));

    return colour * (0.86 + Intensity * 0.14 + beatLight * 0.03) * (1.0 - dot(uv - 0.5, uv - 0.5) * 0.35);
}
