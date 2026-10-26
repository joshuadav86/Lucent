// Name: Hologram Billboards
// Drifting past floating holographic billboards that show the music as bars, waves and spinning shapes, glitching on the beat in front of a downpour of glowing data.
// Family: Cyberpunk
// Tags: Neon, Intense, Geometric
// Added: 2026-10-17 01:16

static const float HoloLens = 1.2;
static const float FloorLevel = -1.2;

float holoHash(float2 p)
{
    float3 q = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
    q += dot(q, q.yzx + 33.33);
    return frac((q.x + q.y) * q.z);
}

// A made-up glyph: a 4 x 6 block of strokes inside a glyph cell. local is 0..1 across the cell.
float holoGlyph(float2 local, float seed)
{
    float2 grid = (local - float2(0.15, 0.1)) / float2(0.7, 0.8) * float2(4.0, 6.0);
    float2 cell = floor(grid);
    float inside = step(0.0, grid.x) * step(grid.x, 4.0) * step(0.0, grid.y) * step(grid.y, 6.0);
    float2 inCell = abs(frac(grid) - 0.5);
    return inside * step(0.5, holoHash(cell + seed * 17.0)) * step(max(inCell.x, inCell.y), 0.42);
}

// One layer of falling data: columns of glyphs with bright heads. Returns brightness (x) and how cyan vs pink (y).
float2 holoDataRain(float2 world, float layer, float pixel)
{
    float columnWidth = 0.14;
    float glyphHeight = 0.19;
    float column = floor(world.x / columnWidth);
    float columnSeed = holoHash(float2(column, layer * 13.0));
    if (columnSeed < 0.55)
    {
        return 0.0;
    }
    float speed = (0.8 + columnSeed * 2.2) * (0.6 + Level * 0.9);
    float fallen = world.y / glyphHeight + Time * speed * 2.0 + columnSeed * 40.0;
    float row = floor(fallen);
    float2 local = float2(frac(world.x / columnWidth), frac(fallen));
    // Glyphs change now and then.
    float glyphSeed = holoHash(float2(column, row + floor(Time * (1.0 + columnSeed * 3.0) + columnSeed * 9.0)));
    float shape = holoGlyph(local, glyphSeed);
    // Far away the glyphs blur into a soft glow.
    float fine = saturate(columnWidth / 4.0 / max(pixel, 1e-4) - 0.5);
    shape = lerp(0.35, shape, fine);
    // A trail of fading glyphs behind each bright head.
    float trailLength = 8.0 + columnSeed * 16.0;
    float sinceHead = frac((row + columnSeed * 50.0) / (trailLength + 30.0)) * (trailLength + 30.0);
    float trail = saturate(1.0 - sinceHead / trailLength);
    float head = step(sinceHead, 1.0);
    return float2(shape * (trail * trail * 0.6 + head * (1.2 + Treble * 2.0)), step(0.82, columnSeed));
}

// Distance from p to the segment a-b.
float holoSegment(float2 p, float2 a, float2 b)
{
    float2 pa = p - a;
    float2 ba = b - a;
    return length(pa - ba * saturate(dot(pa, ba) / dot(ba, ba)));
}

// What a billboard shows, at panel position c (x: -aspect..aspect, y: -1..1).
// kind 0 spectrum bars, 1 oscilloscope, 2 spinning wireframe cube, 3 scrolling text, 4 ring meter.
float holoContent(float2 c, float aspect, float kind, float seed, float pixel)
{
    float stroke = pixel * 1.2;
    if (kind < 0.5)
    {
        float bars = 24.0;
        float x = (c.x / aspect * 0.5 + 0.5) * bars;
        float bar = floor(x);
        float level = spectrum(0.02 + bar / bars * 0.75) * 1.7 - 0.85;
        float inBar = step(abs(frac(x) - 0.5), 0.36) * step(0.0, x) * step(x, bars);
        float segments = step(0.25, frac((c.y + 1.0) * 10.0));
        float lit = step(c.y, level) * segments;
        float cap = glow(c.y - level - 0.06, 1.0 / max(stroke, 0.01)) * 0.8;
        return inBar * (lit * (0.5 + (c.y + 1.0) * 0.4) + cap);
    }
    if (kind < 1.5)
    {
        float x = c.x / aspect * 0.5 + 0.5;
        float wave = waveform(x) * (0.55 + Bass * 0.3);
        float trace = glow(c.y - wave, 1.0 / max(stroke * 2.0, 0.015));
        float2 gridCell = abs(frac(float2(c.x * 2.5, c.y * 2.5)) - 0.5);
        float grid = step(0.46, max(gridCell.x, gridCell.y)) * 0.12;
        return trace * 1.2 + grid;
    }
    if (kind < 2.5)
    {
        // A wireframe cube turning in space, swelling with the bass.
        float spin = Time * (0.5 + seed) + Beat * 0.3;
        float size = 0.5 + Bass * 0.15;
        float d = 1e3;
        float2 corners[8];
        [unroll]
        for (int i = 0; i < 8; i++)
        {
            float3 v = float3((i & 1) ? 1.0 : -1.0, (i & 2) ? 1.0 : -1.0, (i & 4) ? 1.0 : -1.0) * size;
            v.xz = rotate(v.xz, spin);
            v.yz = rotate(v.yz, spin * 0.7 + 0.5);
            corners[i] = v.xy / (2.5 + v.z * 0.4) * 2.2;
        }
        [unroll]
        for (int j = 0; j < 8; j++)
        {
            if ((j & 1) == 0) d = min(d, holoSegment(c, corners[j], corners[j + 1]));
            if ((j & 2) == 0) d = min(d, holoSegment(c, corners[j], corners[j + 2]));
            if ((j & 4) == 0) d = min(d, holoSegment(c, corners[j], corners[j + 4]));
        }
        float ring = abs(length(c) - 0.92) ;
        return glow(d, 1.0 / max(stroke * 1.5, 0.012)) * 1.3 + glow(ring, 1.0 / max(stroke, 0.01)) * 0.35;
    }
    if (kind < 3.5)
    {
        // Lines of text scrolling sideways, each at its own speed.
        float rows = 3.0;
        float rowCoord = (c.y * 0.5 + 0.5) * rows;
        float row = floor(rowCoord);
        float scroll = Time * (0.6 + holoHash(float2(row, seed)) * 1.2) * (row % 2.0 < 0.5 ? 1.0 : -1.0);
        float glyphCoord = (c.x + aspect) * 2.2 + scroll * 2.0;
        float2 local = float2(frac(glyphCoord), frac(rowCoord));
        float glyphSeed = holoHash(float2(floor(glyphCoord), row + seed * 31.0));
        float gap = step(0.18, holoHash(float2(floor(glyphCoord) * 0.37, row)));
        return holoGlyph(local, glyphSeed) * gap * (0.7 + spectrum(0.1 + row * 0.12) * 1.2);
    }
    // A ring meter: the spectrum wrapped around a circle.
    float angle = atan2(c.y, c.x);
    float around = (angle / (2.0 * PI) + 0.5);
    float slice = floor(around * 48.0);
    float level = spectrum(0.02 + abs(slice / 48.0 - 0.5) * 1.4);
    float radius = length(c);
    float spoke = step(abs(frac(around * 48.0) - 0.5), 0.3);
    float lit = step(0.45, radius) * step(radius, 0.45 + level * 0.5) * spoke;
    float core = glow(radius - 0.3 - Beat * 0.05, 1.0 / max(stroke * 2.0, 0.012));
    return lit * 0.9 + core;
}

// One billboard seen along a ray, at position world on its slot's plane. Returns colour; alpha is
// how much it dims what lies behind. reflection skips the colour split, for the mirror in the floor.
float3 holoBillboard(float3 world, float travelled, float slot, out float alpha)
{
    alpha = 0.0;
    float side = world.x >= 0.0 ? 1.0 : -1.0;
    float seed = holoHash(float2(slot * 0.71, side * 3.3 + 1.0));
    if (seed < 0.15)
    {
        return 0.0;
    }
    float height = 0.75 + frac(seed * 5.3) * 0.65;
    float aspect = 1.3 + frac(seed * 3.7) * 0.6;
    float2 centre = float2(side * (2.4 + aspect * height * 0.7 + frac(seed * 9.1) * 1.4), 0.6 + frac(seed * 2.9) * 2.4);
    // Each billboard bobs gently in the air.
    centre.y += sin(Time * 0.6 + seed * 20.0) * 0.12;
    float2 local = (world.xy - centre) / height;   // y: -1..1

    // Glitch: on the beat, slices of the panel jump sideways.
    float band = floor(local.y * 6.0);
    float tick = floor(Time * 14.0);
    float jump = (holoHash(float2(band + seed * 11.0, tick)) - 0.5) * step(0.7, holoHash(float2(band, tick + seed))) * smoothstep(0.3, 1.0, Beat) * 0.4;
    local.x += jump;
    float fade = smoothstep(42.0, 26.0, travelled);
    if (abs(local.x) > aspect + 0.1 || abs(local.y) > 1.1)
    {
        // Below the panel, a faint beam rises from its projector on the floor.
        float drop = (centre.y - FloorLevel) / height;
        float rise = saturate((local.y + 1.0 + drop) / drop);   // 0 at the floor, 1 at the panel
        float beamWidth = lerp(0.05, aspect, rise);
        float beam = step(local.y, -1.0) * step(0.0, rise) * saturate(1.0 - abs(local.x) / beamWidth) * rise * rise;
        float pick = frac(seed * 21.1);
        float3 beamTint = pick < 0.5 ? float3(0.2, 0.85, 1.0) : pick < 0.8 ? float3(1.0, 0.25, 0.75) : float3(0.6, 0.4, 1.0);
        return beamTint * beam * 0.16 * fade * (0.6 + Bass);
    }

    float kind = floor(frac(seed * 13.7) * 5.0);
    float panelPixel = travelled * pixelSize() / HoloLens / height;
    // The colours split apart a little, more on the beat.
    float split = (0.012 + Beat * 0.035) * (kind == 3.0 ? 0.0 : 1.0);
    float3 content;
    content.r = holoContent(local + float2(split, 0.0), aspect, kind, seed, panelPixel);
    content.g = holoContent(local, aspect, kind, seed, panelPixel);
    content.b = holoContent(local - float2(split, 0.0), aspect, kind, seed, panelPixel);

    // Hologram colour: mostly cyan, some magenta or violet panels.
    float tintPick = frac(seed * 21.1);
    float3 tint = tintPick < 0.5 ? float3(0.2, 0.85, 1.0) : tintPick < 0.8 ? float3(1.0, 0.25, 0.75) : float3(0.6, 0.4, 1.0);
    float inside = step(abs(local.x), aspect) * step(abs(local.y), 1.0);
    // A thin frame with bright corner brackets.
    float2 fromCorner = abs(local) - float2(aspect, 1.0);
    float edge = abs(max(fromCorner.x, fromCorner.y) + 0.03);
    float frameLine = glow(edge, 1.0 / max(panelPixel * 1.5, 0.008));
    float frame = frameLine * (0.3 + step(aspect - 0.3, abs(local.x)) * step(0.7, abs(local.y)) * 0.9);
    // Scanlines rolling down, a gradient fill and the odd flicker.
    float scan = 0.7 + 0.3 * sin(local.y * 70.0 + Time * 12.0);
    float fill = (0.06 + 0.05 * (1.0 - local.y)) * inside;
    float flicker = 0.8 + 0.2 * step(0.08, holoHash(float2(seed, floor(Time * 24.0))));
    float3 colour = (content * lerp(tint, 1.0, 0.25) * 1.2 + tint * fill) * scan + tint * frame;
    // Billboards fade in out of the haze.
    alpha = inside * 0.55 * fade;
    return colour * flicker * fade * (0.75 + Mid * 0.5);
}

static const float SlotSpacing = 5.0;   // a pair of billboards every this far along the avenue

// All the billboards along a ray, far to near.
float3 holoBoards(float3 eye, float3 dir, float maxDistance)
{
    float3 colour = 0.0;
    float firstSlot = floor(eye.z / SlotSpacing) + 1.0;
    float alpha;
    [unroll]
    for (int k = 8; k >= 0; k--)
    {
        float slot = firstSlot + k;
        float travelled = (slot * SlotSpacing - eye.z) / dir.z;
        if (travelled > 0.0 && travelled < maxDistance)
        {
            float3 board = holoBillboard(eye + dir * travelled, travelled, slot, alpha);
            colour = colour * (1.0 - alpha) + board;
        }
    }
    return colour;
}

float3 Render(float2 uv, float2 p)
{
    float2 q = float2(p.x, -p.y);

    // On strong beats a few strips of the whole picture tear sideways, like a bad video signal.
    float strip = floor(uv.y * 24.0);
    float tick = floor(Time * 12.0);
    float tear = step(0.88, holoHash(float2(strip, tick))) * (holoHash(float2(strip + 3.0, tick)) - 0.5) * smoothstep(0.6, 1.0, Beat) * 0.1;
    q.x += tear;

    float z = Time * 2.2 + (BeatCount - Beat) * 0.7;
    float3 eye = float3(sin(Time * 0.19) * 0.6, 0.9 + sin(Time * 0.27) * 0.3, z);
    float3 forward = normalize(float3(sin(Time * 0.13) * 0.1, 0.05, 1.0));
    float3 right = normalize(cross(float3(0.0, 1.0, 0.0), forward));
    float3 up = cross(forward, right);
    float3 dir = normalize(forward * HoloLens + q.x * right + q.y * up);

    // Night sky: a glow over the far end of the avenue, and data raining down far away.
    float3 colour = lerp(float3(0.22, 0.04, 0.26), float3(0.0, 0.0, 0.02), saturate(dir.y * 4.0 + 0.1)) * (0.8 + Bass * 0.4);
    float2 farWorld = float2(atan2(dir.x, dir.z) * 5.0 + eye.x * 0.05, dir.y * 5.0);
    [unroll]
    for (int layer = 0; layer < 2; layer++)
    {
        float2 rain = holoDataRain(farWorld * (1.0 + layer * 0.6) + float2(layer * 3.7, 0.0), layer, pixelSize() * 5.0 / HoloLens * (1.0 + layer * 0.6));
        colour += lerp(float3(0.1, 0.9, 0.8), float3(1.0, 0.2, 0.7), rain.y) * rain.x * (0.22 - layer * 0.08);
    }

    // A far skyline of dark towers against the glow, with a few lit windows.
    float2 panorama = float2(atan2(dir.x, dir.z) * 8.0 + eye.x * 0.02, dir.y * 8.0 + 0.3);
    float towerIndex = floor(panorama.x / 0.55);
    float towerSeed = holoHash(float2(towerIndex, 9.0));
    float towerTop = 0.35 + (0.3 + towerSeed * towerSeed * 2.4) * smoothstep(0.2, 1.6, abs(panorama.x - eye.x * 0.02));
    float inTower = step(panorama.y, towerTop) * step(abs(frac(panorama.x / 0.55) - 0.5), 0.44);
    float2 windowGrid = float2(panorama.x / 0.07, panorama.y / 0.09);
    float2 inWindow = abs(frac(windowGrid) - 0.5);
    float windowLit = step(0.8, holoHash(floor(windowGrid) + towerSeed * 13.0)) * step(max(inWindow.x, inWindow.y), 0.3);
    float3 windowTint = towerSeed > 0.6 ? float3(1.0, 0.3, 0.7) : float3(0.3, 0.7, 1.0);
    float beacon = glow(length(float2((frac(panorama.x / 0.55) - 0.5) * 0.55, panorama.y - towerTop - 0.03)), 120.0) * step(0.5, towerSeed);
    colour = lerp(colour, float3(0.01, 0.005, 0.02) + windowTint * windowLit * 0.35, inTower);
    colour += float3(1.0, 0.15, 0.3) * beacon * (0.5 + Bass * 1.5);

    float floorDistance = dir.y < 0.0 ? (FloorLevel - eye.y) / dir.y : 1e3;
    if (dir.y < 0.0)
    {
        // A glossy black floor with streams of data racing along it, mirroring the billboards.
        float3 ground = eye + dir * floorDistance;
        float laneWidth = 0.5;
        float lane = floor(ground.x / laneWidth);
        float laneSeed = holoHash(float2(lane, 2.0));
        float pixel = floorDistance * pixelSize() / HoloLens;
        float wire = saturate(1.0 - (abs(frac(ground.x / laneWidth) - 0.5) * laneWidth - 0.008) / max(pixel, 1e-4)) * saturate(0.03 / pixel);
        float direction = laneSeed > 0.5 ? 1.0 : -1.0;
        float packets = frac(ground.z * 0.08 - Time * (0.4 + laneSeed * 0.8) * direction + laneSeed * 10.0);
        float packet = smoothstep(0.75, 1.0, packets) * smoothstep(1.0, 0.98, packets);
        float energy = spectrum(0.03 + frac(laneSeed * 7.0) * 0.6);
        float3 streamColour = laneSeed > 0.7 ? float3(1.0, 0.2, 0.7) : float3(0.1, 0.8, 1.0);
        float3 floorColour = streamColour * wire * (0.12 + packet * (1.0 + energy * 2.5));
        float3 mirrored = float3(dir.x, -dir.y, dir.z);
        float3 mirror = holoBoards(ground, mirrored, 45.0);
        floorColour += mirror * 0.14 + float3(0.08, 0.02, 0.12) * 0.3;
        colour = floorColour * exp(-floorDistance * 0.04) + float3(0.08, 0.02, 0.12) * (1.0 - exp(-floorDistance * 0.04));
    }

    colour += holoBoards(eye, dir, min(floorDistance, 45.0));

    // Soft scanlines over everything.
    float scanline = 0.5 + 0.5 * sin(uv.y * Resolution.y / max(RenderScale, 1.0) * (2.0 * PI / 3.0));
    colour *= 1.0 - (0.05 + Beat * 0.05) * scanline;
    return colour;
}
