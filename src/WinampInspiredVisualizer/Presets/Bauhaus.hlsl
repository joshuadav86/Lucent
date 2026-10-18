// Name: Bauhaus
// A Bauhaus poster comes to life: circles, half moons, squares and bars in red, yellow, blue and black slide,
// turn and rearrange themselves on the beat across warm off-white paper, with bars that measure the music.
// Family: Geometric
// Tags: Retro, Colourful, Geometric, Minimal
// Added: 2026-10-17 21:37

static const float3 BhPaper = float3(0.66, 0.6, 0.5);
static const float3 BhRed = float3(0.62, 0.07, 0.04);
static const float3 BhYellow = float3(0.78, 0.52, 0.04);
static const float3 BhBlue = float3(0.035, 0.12, 0.42);
static const float3 BhBlack = float3(0.025, 0.022, 0.02);

float3 bhInk(float index)
{
    float3 colour = BhBlack;
    colour = index > 0.5 ? BhRed : colour;
    colour = index > 1.5 ? BhYellow : colour;
    colour = index > 2.5 ? BhBlue : colour;
    colour = index > 3.5 ? BhPaper : colour;
    return colour;
}

float bhRandom(float2 cell, float epoch, float salt)
{
    return hash(cell * 1.37 + float2(epoch * 7.13 + salt * 3.1, epoch * 1.91 - salt * 5.7));
}

// One cell's layout for an epoch: x rotation (quarter turns), y roundness (0 square .. 1 circle),
// z size, w how much of it is cut away (0 whole, 1 half).
float4 bhShape(float2 cell, float epoch)
{
    float turn = floor(bhRandom(cell, epoch, 1.0) * 4.0);
    float roundness = bhRandom(cell, epoch, 2.0) < 0.62 ? 1.0 : 0.0;
    float size = 0.11 + 0.055 * floor(bhRandom(cell, epoch, 3.0) * 3.0);
    size *= bhRandom(cell, epoch, 9.0) < 0.2 ? 0.0 : 1.0;
    float cut = bhRandom(cell, epoch, 4.0) < 0.5 ? 1.0 : 0.0;
    return float4(turn, roundness, size, cut);
}

// Signed distance to a shape that blends from a square to a circle and can be sliced in half.
float bhShapeDistance(float2 q, float4 shape, float px)
{
    float2 r = rotate(q, shape.x * PI * 0.5);
    float circle = length(r) - shape.z;
    float2 b = abs(r) - shape.z * 0.88;
    float square = length(max(b, 0.0)) + min(max(b.x, b.y), 0.0);
    float d = lerp(square, circle, shape.y);
    // The slice plane slides in from beyond the shape to its middle.
    float slice = lerp(shape.z * 1.2, 0.0, shape.w);
    return max(d, r.y - slice);
}

float3 Render(float2 uv, float2 p)
{
    float px = pixelSize();
    float clock = max(BeatCount - Beat, 0.0);

    // The poster recomposes every two beats; each change eases in over the first half beat.
    float beatTime = clock / 2.0;
    float epoch = floor(beatTime);
    float ease = smoothstep(0.0, 0.5, frac(beatTime) * 2.0);

    // A very slow drift of the poster itself.
    float2 q = p + float2(0.02 * sin(Time * 0.07), 0.015 * cos(Time * 0.05));

    // Paper with a faint grain and a frame of black.
    float grain = noise(q * 260.0) * 0.5 + noise(q * 41.0) * 0.5;
    float3 colour = BhPaper * (0.94 + 0.08 * grain);

    // A 5 x 3 grid of modules.
    float module = 0.5;
    float2 grid = q / module + float2(2.5, 1.5);
    float2 cell = floor(grid);
    float2 local = (frac(grid) - 0.5) * module;
    bool onGrid = cell.x >= 0.0 && cell.x <= 4.0 && cell.y >= 0.0 && cell.y <= 2.0;

    if (onGrid)
    {
        // Some modules get a coloured ground.
        float groundOld = bhRandom(cell, epoch - 1.0, 5.0);
        float groundNew = bhRandom(cell, epoch, 5.0);
        float3 groundA = groundOld < 0.24 ? bhInk(1.0 + floor(groundOld * 12.5)) : BhPaper;
        float3 groundB = groundNew < 0.24 ? bhInk(1.0 + floor(groundNew * 12.5)) : BhPaper;
        // The new ground wipes across the module from left to right.
        float wipe = step(frac(grid.x), ease);
        float3 ground = lerp(groundA, groundB, wipe);
        colour = lerp(colour, ground * (0.95 + 0.06 * grain), groundOld < 0.24 || groundNew < 0.24 ? 1.0 : 0.0);

        // The shape glides from its old pose to its new one: turning, rounding, growing and slicing.
        float4 a = bhShape(cell, epoch - 1.0);
        float4 b = bhShape(cell, epoch);
        float4 shape = lerp(a, b, ease);
        if (a.x == 3.0 && b.x == 0.0)
        {
            shape.x = lerp(3.0, 4.0, ease);
        }

        // Each module breathes with its own slice of the spectrum.
        float band = spectrum(0.04 + (cell.x + cell.y * 5.0) / 15.0 * 0.8);
        shape.z *= 0.88 + band * 0.2 + Bass * 0.05;
        // ...and slides to a new spot inside its module.
        float2 slideA = float2(bhRandom(cell, epoch - 1.0, 7.0), bhRandom(cell, epoch - 1.0, 8.0)) - 0.5;
        float2 slideB = float2(bhRandom(cell, epoch, 7.0), bhRandom(cell, epoch, 8.0)) - 0.5;
        float2 slide = lerp(slideA, slideB, ease) * module * (0.5 - shape.z * 0.8);
        float d = bhShapeDistance(local - slide, shape, px);

        float inkOld = floor(bhRandom(cell, epoch - 1.0, 6.0) * 4.6);
        float inkNew = floor(bhRandom(cell, epoch, 6.0) * 4.6);
        float3 ink = lerp(bhInk(inkOld), bhInk(inkNew), ease);
        // Never paint a shape the same colour as its ground.
        if (length(ink - ground) < 0.05)
        {
            ink = BhBlack;
        }
        float fill = 1.0 - smoothstep(-px, px, d);
        colour = lerp(colour, ink * (0.96 + 0.05 * grain), fill);
    }

    // A big circle that rolls across the whole composition, glowing a touch on the bass.
    float2 bigCentre = float2(0.75 * sin(clock * PI / 16.0), 0.18 * cos(clock * PI / 11.0));
    float big = length(q - bigCentre) - (0.33 + Bass * 0.04);
    float bigFill = 1.0 - smoothstep(-px, px, big);
    float3 bigInk = lerp(BhRed, BhYellow, 0.5 + 0.5 * sin(Time * 0.05));
    // Printed over the top like a transparent ink, so what lies beneath shows through.
    colour = lerp(colour, colour * bigInk / BhPaper, bigFill * 0.9);

    // A level meter of thick black bars along the bottom, the classic poster rule.
    float barBase = 0.915;
    if (q.y > 0.78 && q.y < barBase)
    {
        float slot = floor((q.x + Aspect) / (2.0 * Aspect) * 24.0);
        float slotX = frac((q.x + Aspect) / (2.0 * Aspect) * 24.0);
        float height = spectrum(slot / 24.0) * 0.11 + 0.012;
        float bar = step(0.2, slotX) * step(slotX, 0.8) * step(barBase - height, q.y);
        colour = lerp(colour, BhBlack, bar);
    }

    // Thin black rules that slide along with the beat.
    float ruleX = 1.1 * sin(clock * PI / 8.0);
    float rule = 1.0 - smoothstep(px * 1.5, px * 3.0, abs(q.x - ruleX));
    float ruleY = -0.84;
    float ruleSpan = step(abs(q.x - 0.6 * sin(clock * PI / 12.0)), 0.9);
    rule = max(rule * step(abs(q.y), 0.75), (1.0 - smoothstep(0.022 - px, 0.022 + px, abs(q.y - ruleY))) * ruleSpan);
    colour = lerp(colour, BhBlack, rule * 0.9);

    // A black margin round the poster.
    float2 edge = abs(q) - float2(Aspect - 0.05, 0.95);
    float margin = smoothstep(-px, px, max(edge.x, edge.y));
    colour = lerp(colour, BhBlack, margin);

    // The beat warms the paper very slightly, like a lamp brightening.
    colour *= 0.97 + Beat * 0.05 + Level * 0.04;
    return colour;
}
