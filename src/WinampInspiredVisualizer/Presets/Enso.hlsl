// Name: Enso
// A single zen circle is swept onto rice paper in one breath of a dry ink brush, pressing harder when the bass swells;
// it rests for a while, fades into the paper, and a new circle is painted every few bars.
// Family: Minimal
// Tags: Calm, Minimal

static const float EnCycle = 8.0;       // seconds per circle
static const float EnDrawTime = 1.9;    // seconds the brush takes to sweep round
static const float3 EnInk = float3(0.018, 0.02, 0.03);

// Rice paper: warm, uneven, with long fibres. It must be the same every frame, because the ink is read back
// from the last frame as the fraction of the paper's light it has taken away.
float3 enPaper(float2 p)
{
    float cloud = fbm(p * 2.5) * 0.6 + fbm(p * 11.0 + 4.0) * 0.4;
    float2 f = rotate(p, 0.5);
    float fibres = noise(float2(f.x * 6.0, f.y * 160.0) + fbm(p * 4.0) * 3.0) * noise(float2(f.x * 90.0, f.y * 7.0));
    float3 paper = float3(0.58, 0.52, 0.42) * (0.9 + 0.12 * cloud) - fibres * 0.03;
    paper *= 1.0 - 0.18 * dot(p, p) / (Aspect * Aspect);
    return paper;
}

float enEase(float t)
{
    float x = saturate(t / EnDrawTime);
    return x * x * (3.0 - 2.0 * x) * 0.6 + x * 0.4;
}

// The red seal stamped in the corner: a rounded square with carved white strokes.
float enSeal(float2 p, out float carve)
{
    float2 c = float2(Aspect - 0.32, 0.62);
    float2 d = abs(p - c) - 0.05;
    float box = length(max(d, 0.0)) + min(max(d.x, d.y), 0.0) - 0.008;
    float2 g = (p - c) / 0.05;
    float strokes = max(step(abs(g.x + 0.35), 0.1) * step(abs(g.y), 0.7),
                    max(step(abs(g.y - 0.1), 0.09) * step(abs(g.x - 0.2), 0.45),
                        step(abs(g.x - 0.45), 0.09) * step(abs(g.y + 0.25), 0.45)));
    carve = strokes * step(box, -0.012);
    return 1.0 - smoothstep(-0.002, 0.002, box);
}

float3 Render(float2 uv, float2 p)
{
    float px = pixelSize();
    float3 paper = enPaper(p);

    // How much ink this spot already holds, read back from the last frame.
    float3 last = previous(uv);
    float ink = saturate((paper.g - last.g) / max(paper.g - EnInk.g, 1e-3) / 0.95);
    if (FrameCount < 2.0)
    {
        ink = 0.0;
    }

    // Where we are in this circle's life: drawing, resting, then fading back into the paper.
    float cycleIndex = floor(Time / EnCycle);
    float t = Time - cycleIndex * EnCycle;
    float washRate = t > 5.6 ? 1.6 : 0.015;
    ink *= exp(-max(DeltaTime, 0.0) * washRate);
    if (t < 0.05)
    {
        ink = 0.0;
    }

    // This circle's character: where it starts, its size, how far round it goes.
    float seedA = hash(float2(cycleIndex, 1.7));
    float seedB = hash(float2(cycleIndex, 5.3));
    float seedC = hash(float2(cycleIndex, 9.1));
    float2 centre = float2(-0.12 + 0.1 * (seedA - 0.5), -0.03 + 0.06 * (seedB - 0.5));
    float radius = 0.5 + 0.1 * seedB;
    float start = seedA * 2.0 * PI;
    float sweep = 2.0 * PI * (0.84 + 0.11 * seedC);

    // Stroke coordinates: how far along the circle (0 at the start, 1 at the end) and how far across the brush.
    float2 d = p - centre;
    float r = length(d);
    float angle = atan2(d.y, d.x);
    float along = wrap(start - angle, 2.0 * PI) / sweep;   // the brush travels clockwise on screen
    float wobble = 1.0 + 0.025 * sin(along * 5.0 + seedC * 6.0) - 0.05 * along;
    float centreLine = radius * wobble;

    // The brush lands heavy, carries pressure from the music, and lifts off into a thin dry tail.
    float pressure = 0.72 + Bass * 0.5;
    float taper = (0.75 + 0.25 * smoothstep(0.0, 0.06, along)) * (1.0 - 0.55 * smoothstep(0.6, 1.0, along));
    float halfWidth = 0.068 * pressure * taper;
    float across = (r - centreLine) / max(halfWidth, 1e-4);

    // Dry brush: ink runs out along the stroke, so bristle streaks open into gaps, raggedly at the edges.
    float bristles = noise(float2(across * 7.0 + seedC * 20.0, along * 4.0)) * 0.6
                   + noise(float2(across * 19.0, along * 25.0 + seedA * 9.0)) * 0.4;
    float grain = noise(p * 220.0);
    float dryness = smoothstep(0.2, 1.0, along) * 0.62 + (1.0 - saturate(pressure - 0.4)) * 0.1;
    float density = smoothstep(dryness - 0.05, dryness + 0.2, bristles + 0.35 - grain * 0.15);
    // Even where the brush is wet the ink lies unevenly, darker along the bristle grooves.
    density *= 0.9 + 0.1 * smoothstep(0.3, 0.7, bristles);
    float edge = 1.0 - smoothstep(0.82, 1.0, abs(across) + (noise(float2(along * 60.0, across)) - 0.5) * 0.25);
    float stroke = density * edge;

    // Only paint the part of the stroke the brush crossed since the last frame.
    float headNow = enEase(t);
    float headBefore = enEase(t - max(DeltaTime, 0.0) * 1.5);
    float painted = (along >= headBefore - 0.004 && along <= headNow) ? 1.0 : 0.0;
    painted *= step(t, EnDrawTime + 0.2);
    ink = max(ink, stroke * painted);

    // The brush lands with a round, heavy press where the stroke begins.
    float2 startPoint = centre + float2(cos(start), sin(start)) * radius * (1.0 + 0.025 * sin(seedC * 6.0));
    float landing = 1.0 - smoothstep(0.85, 1.0, length(p - startPoint) / (0.062 * pressure) + (grain - 0.5) * 0.15);
    ink = max(ink, landing * (0.9 + 0.1 * smoothstep(0.3, 0.7, bristles)) * step(t, 0.15));

    // A strong beat while drawing flicks a few drops of ink from the brush.
    float2 head = centre + float2(cos(start - headNow * sweep), sin(start - headNow * sweep)) * radius;
    float2 dropCell = floor(p * 70.0);
    float dropPick = hash(dropCell + floor(BeatCount) * 3.1);
    float2 dropCentre = (dropCell + 0.5) / 70.0;
    float drop = step(0.985, dropPick) * (1.0 - smoothstep(0.003, 0.0045, length(p - dropCentre)));
    drop *= step(length(dropCentre - head), 0.12) * step(0.75, Beat) * step(t, EnDrawTime);
    ink = max(ink, drop);

    // Ink is a deep, slightly blue black; thin wash shows a little paper through.
    float carve = 0.0;
    float seal = enSeal(p, carve);
    ink *= 1.0 - seal;
    float3 colour = lerp(paper, EnInk, 0.95 * ink);

    // The seal sits on top, never fading.
    float3 vermilion = float3(0.5, 0.06, 0.035) * (0.92 + 0.1 * grain);
    colour = lerp(colour, lerp(vermilion, paper, carve), seal);
    return colour;
}
