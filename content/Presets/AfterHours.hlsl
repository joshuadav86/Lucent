// Name: After Hours
// A slow walk down a club corridor at closing time: walls, floor, and ceiling of glossy black boxes, set in
// and out like drawers, with a few of them lit red or white and mirrored in all the rest. Each light is tied
// to one part of the music, a wave of brightness runs down the corridor on the beat, and the film is grainy.
// Family: Cyberpunk
// Tags: 3D, Dark, Geometric
// Added: 2026-11-15 21:08

static const float Box = 0.5;           // the boxes are this big
static const float Wide = 1.5;          // half the width of the corridor, wall to wall
static const float High = 1.0;          // half its height
static const float Repeat = 64.0;       // the corridor repeats after this many boxes

// One box in a wall: x is how far it stands out from the wall, y is 1 for a red light and 2 for a white one,
// z picks its part of the music.
float3 ahBox(float wall, float2 cell)
{
    float2 id = float2(cell.x + wall * 13.0, wrap(cell.y, Repeat));
    float pick = hash(id * float2(0.731, 0.377) + 1.3);
    float kind = pick > 0.93 ? 2.0 : pick > 0.84 ? 1.0 : 0.0;
    float stand = kind > 0.5 ? 0.04 : 0.06 + 0.34 * hash(id * float2(0.37, 0.613) + 4.4);
    return float3(stand, kind, frac(pick * 23.0));
}

// How far x is from the boxes of the nearest walls. `box` describes the nearest box, `safe` is how far a
// ray may go before it could enter another box's space.
float ahMap(float3 x, out float3 box, out float safe)
{
    float side = x.x > 0.0 ? 1.0 : 2.0;
    float2 sideCell = floor(float2(x.y, x.z) / Box);
    float3 sideBox = ahBox(side, sideCell);
    float fromSide = Wide - abs(x.x) - sideBox.x;
    float2 sideIn = abs(frac(float2(x.y, x.z) / Box) - 0.5);

    float flat = x.y > 0.0 ? 3.0 : 4.0;
    float2 flatCell = floor(float2(x.x, x.z) / Box);
    float3 flatBox = ahBox(flat, flatCell);
    float fromFlat = High - abs(x.y) - flatBox.x;
    float2 flatIn = abs(frac(float2(x.x, x.z) / Box) - 0.5);

    safe = (0.5 - max(max(sideIn.x, sideIn.y), max(flatIn.x, flatIn.y))) * Box + 0.004;
    box = fromSide < fromFlat ? sideBox : flatBox;
    return min(fromSide, fromFlat);
}

// The light a box gives off: nothing for a plain box.
float3 ahLight(float3 box, float cellZ, float4 heard, float beat)
{
    if (box.y < 0.5) return float3(0.0, 0.0, 0.0);
    float loud = box.z < 0.25 ? heard.x : box.z < 0.5 ? heard.y : box.z < 0.75 ? heard.z : heard.w;
    float ahead = wrap(cellZ - beat, 24.0) - 12.0;
    float bright = 0.3 + 1.5 * loud + 0.7 * exp(-ahead * ahead * 0.12);
    return (box.y > 1.5 ? float3(1.15, 1.1, 1.0) : float3(1.7, 0.07, 0.05)) * bright;
}

float3 Render(float2 uv, float2 p)
{
    float4 heard = float4(spectrum(0.03), spectrum(0.12), spectrum(0.3), spectrum(0.55));
    float travel = wrap(Time * 0.35 + BeatGlide * 0.3, Box * Repeat);
    float beat = travel / Box + wrap(BeatGlide * 6.0, 24.0);
    float3 eye = float3(0.25 * sin(Time * 0.13), -0.15 + 0.1 * sin(Time * 0.17), travel);
    float3 ray = cameraRay(eye, eye + float3(0.2 * sin(Time * 0.07), 0.05, 1.0), float2(p.x, -p.y), 0.8, 0.38 + 0.08 * sin(Time * 0.09));

    float t = 0.0;
    float3 box = float3(0.0, 0.0, 0.0);
    float3 glowing = float3(0.0, 0.0, 0.0);
    bool hit = false;
    int steps = qualitySteps(120);

    [loop]
    for (int i = 0; i < steps; i++)
    {
        float3 x = eye + ray * t;
        float safe;
        float d = ahMap(x, box, safe);
        if (d < 0.002 * (1.0 + t))
        {
            hit = true;
            break;
        }

        float pace = min(d, safe);
        glowing += ahLight(box, floor(x.z / Box), heard, beat) * pace * exp(-d * 5.0) * exp(-t * 0.16);
        t += pace;
        if (t > 20.0) break;
    }

    float3 colour = float3(0.05, 0.004, 0.006);
    if (hit)
    {
        float3 at = eye + ray * t;
        float3 unusedBox;
        float unusedSafe;
        float2 e = float2(0.004, 0.0);
        float3 n = normalize(float3(
            ahMap(at + e.xyy, unusedBox, unusedSafe) - ahMap(at - e.xyy, unusedBox, unusedSafe),
            ahMap(at + e.yxy, unusedBox, unusedSafe) - ahMap(at - e.yxy, unusedBox, unusedSafe),
            ahMap(at + e.yyx, unusedBox, unusedSafe) - ahMap(at - e.yyx, unusedBox, unusedSafe)));

        float3 own = ahLight(box, floor(at.z / Box), heard, beat);

        // Gloss: follow the mirrored ray to the wall it meets and see whether a light is there.
        float3 back = reflect(ray, n);
        back = float3(abs(back.x) < 1e-3 ? 1e-3 : back.x, abs(back.y) < 1e-3 ? 1e-3 : back.y, back.z);
        float toSide = ((back.x > 0.0 ? Wide - 0.05 : -Wide + 0.05) - at.x) / back.x;
        float toFlat = ((back.y > 0.0 ? High - 0.05 : -High + 0.05) - at.y) / back.y;
        float far = max(min(toSide, toFlat), 0.0);
        float3 there = at + back * far;
        float3 seen = toSide < toFlat
            ? ahBox(back.x > 0.0 ? 1.0 : 2.0, floor(float2(there.y, there.z) / Box))
            : ahBox(back.y > 0.0 ? 3.0 : 4.0, floor(float2(there.x, there.z) / Box));
        float3 mirrored = ahLight(seen, floor(there.z / Box), heard, beat);
        float facing = saturate(-dot(n, ray));
        float gloss = 0.22 + 0.6 * pow(1.0 - facing, 3.0);

        float3 surface = own + float3(0.03, 0.028, 0.034) * (0.4 + 0.6 * facing) + mirrored * gloss / (1.0 + far * far * 0.25);
        colour = lerp(surface, colour, 1.0 - exp(-t * 0.13));
    }

    colour += glowing * 0.55;

    // Film grain, new each frame of a 24-a-second reel.
    float frame = floor(Time * 24.0);
    colour += (hash(p * 173.0 + wrap(frame, 97.0) * 0.61) - 0.5) * 0.035;
    return max(colour, 0.0) * (0.94 + 0.08 * Level) * (1.0 - dot(uv - 0.5, uv - 0.5) * 0.7);
}
