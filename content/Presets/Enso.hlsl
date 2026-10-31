// Name: Enso
// A single zen circle is swept across the dark in one breath of a brush made of light, every bristle a fine neon thread
// from teal through violet to magenta, pressing harder when the bass swells. Each thread glows with its own band of the
// spectrum, beats send light running round the circle and echo rings rippling out from it, and a new circle is drawn every few bars.
// Family: Minimal
// Tags: Neon, Minimal
// Added: 2026-10-17 21:37

static const float EnCycle = 8.0;       // seconds per circle
static const float EnDrawTime = 1.9;    // seconds the brush takes to sweep round

float enEase(float t)
{
    float x = saturate(t / EnDrawTime);
    return x * x * (3.0 - 2.0 * x) * 0.6 + x * 0.4;
}

// The seal in the corner, drawn as a neon outline with its carved strokes lit inside.
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
    return exp(-box * box / 0.000004);
}

float3 enNeon(float t)
{
    return hsv2rgb(frac(0.47 + t * 0.45), 0.85, 1.0);
}

float sinceBeat()
{
    return -log(max(Beat, 1e-4)) / 5.0;
}

float3 Render(float2 uv, float2 p)
{
    float px = pixelSize();

    // Where we are in this circle's life: drawing, resting, then fading back into the dark.
    float cycleIndex = floor(Time / EnCycle);
    float t = Time - cycleIndex * EnCycle;
    float life = (t > 5.6 ? exp(-(t - 5.6) * 1.6) : 1.0) * smoothstep(0.0, 0.05, t);

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

    // The bristles: fine threads of light side by side across the brush, a little uneven, thinning
    // out into gaps as the brush runs dry towards the tail.
    float threads = 11.0;
    float lane = (across * 0.5 + 0.5) * threads + (noise(float2(along * 3.0, seedC * 9.0)) - 0.5) * 0.6;
    float index = floor(lane);
    float f = frac(lane);
    float thread = exp(-pow((f - 0.5) / 0.16, 2.0));
    float dryness = smoothstep(0.2, 1.0, along) * 0.62;
    float wet = smoothstep(dryness - 0.05, dryness + 0.2, noise(float2(index * 3.1 + seedC * 20.0, along * 6.0)) + 0.35);
    float edge = 1.0 - smoothstep(0.85, 1.0, abs(across));

    // Only the part of the stroke the brush has crossed so far shows, with a hot tip at the brush.
    float headNow = enEase(t);
    float drawn = step(along, headNow);
    float tip = exp(-pow((along - headNow) / 0.015, 2.0)) * step(t, EnDrawTime + 0.1);

    // Each thread glows with its own band of the spectrum, and a pulse of light runs round the
    // circle over each beat (with no beat to follow, just as it lands).
    float place = saturate(index / (threads - 1.0));
    float level = spectrum(0.03 + place * 0.6);
    float at = (along - BeatPhase) * 9.0;
    float pulse = exp(-at * at) * (SongMapped > 0.0 ? 0.35 + Beat * 0.65 : Beat);
    float strength = thread * wet * edge * drawn * life;
    float3 colour = enNeon(place + along * 0.12) * strength * (0.35 + level * 1.4 + pulse * 1.2 + tip * 2.0) * (0.8 + Intensity * 0.4) * 1.6;

    // A soft halo of light round the whole stroke.
    float halo = exp(-max(abs(r - centreLine) - halfWidth, 0.0) * 30.0) * drawn * life;
    colour += enNeon(0.5) * halo * 0.04 * (0.6 + Bass);

    // Every beat sends an echo of the circle rippling outward as a thin neon ring.
    float echoRadius = radius + sinceBeat() * 0.5;
    float echo = exp(-pow((r - echoRadius) / max(px * 1.5, 0.002), 2.0)) * Beat * life;
    colour += enNeon(0.85) * echo * 0.6;

    // The landing: a round, bright press where the stroke begins.
    float2 startPoint = centre + float2(cos(start), sin(start)) * radius * (1.0 + 0.025 * sin(seedC * 6.0));
    float landing = exp(-pow(length(p - startPoint) / (0.05 * pressure), 2.0));
    colour += enNeon(0.2) * landing * 0.25 * life;

    // The seal sits on top, never fading.
    float carve = 0.0;
    float seal = enSeal(p, carve);
    colour += enNeon(0.9) * (seal + carve * 0.35) * (0.6 + Treble * 0.6);

    // A faint glow in the middle of the dark, and a little of the last frame for soft trails.
    colour += float3(0.02, 0.015, 0.04) * exp(-dot(p, p) * 1.5);
    return colour + previous(uv) * 0.25;
}
