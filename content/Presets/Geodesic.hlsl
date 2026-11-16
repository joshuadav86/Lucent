// Name: Geodesic
// A white ball tiled all over with hexagons that lift off and float above it, their sides painted in cyan, magenta,
// and yellow. The tiles dance in time: a wave of lifting tiles runs round the ball from a wandering pole once a
// beat, and each tile bobs with the bar in its own step. The music is wrapped round the ball from bass at the
// pole to treble at the far side: each ring of tiles takes on its colour and the ball under it glows as its part
// sounds. Every eight beats the tiles fly out together and settle back as a new tiling, coarse or fine.
// Family: Ray traced
// Tags: Geometric, 3D, Colourful
// Added: 2026-11-15 19:40

// The tiling is a geodesic one: each of an icosahedron's twenty triangles is cut into a grid of smaller
// triangles, the grid's corners are pushed out onto the ball, and every corner becomes the middle of a tile
// (a hexagon, or a pentagon at the icosahedron's own twelve corners). A point in space belongs to whichever
// of its three nearest tile middles is closest, so only three tiles ever need looking at.
static const float Golden = 1.6180339887;
static const float Ball = 0.92;                // radius of the ball when every tile is down
static float Splits = 4.0;                     // tiles along each edge of the icosahedron: it changes every eight beats
static float GeSound;                          // how loud the music is just now, taken across all its parts
static float GeBurst;                          // 1 as the tiles fly out between one tiling and the next
static float GeSince;                          // how far through the beat, 0 on the beat
static const float Gap = 0.014;                // half the slit between two tiles
static const float Thick = 0.075;              // how thick a tile is
static const float Highest = 0.5;              // how far a tile can lift

// One part of the music, with the high notes made up to stand level with the low ones.
float gePart(float x)
{
    return spectrum(x) * (1.0 + 0.9 * x);
}

// How loud one part of the music is, judged against the music as a whole, so that a quiet song shows as plainly
// as a loud one.
float geLoud(float x)
{
    float heard = (gePart(x - 0.03) + gePart(x) + gePart(x + 0.03)) / 3.0;
    return smoothstep(0.0, 1.0, saturate((heard - 0.55 * GeSound) / (0.8 * GeSound + 0.12)));
}

// How far the tile whose middle points along c has lifted. A wave of lifting runs round the ball from the pole
// once a beat, each tile bobs with the bar in its own step, and all of them fly out between tilings.
float geLift(float3 c, float3 pole)
{
    float round0 = acos(clamp(dot(c, pole), -1.0, 1.0)) / PI;
    float own = hash(c.xy * 37.0 + c.z * 91.0);
    float wave = 0.5 + 0.5 * cos(6.2832 * (round0 * 1.6 - BeatPhase) - wrap(Time * 0.45, 6.2832));
    float bob = 0.5 + 0.5 * sin(6.2832 * (BarPhase + own) + Time * 0.3);
    float lively = 0.45 + 0.75 * Intensity;
    float lift = 0.012 + wave * wave * wave * (0.1 + 0.16 * own) * lively + bob * 0.07 * lively;
    lift += BeatSwell * 0.035 + DropPulse * 0.1 * (0.4 + own) + GeBurst * (0.3 + 0.2 * own);
    return min(lift, Highest);
}

// The three tile middles nearest to the direction of x, and how far each one has lifted.
void geTiles(float3 x, float3 pole, out float3 ca, out float3 cb, out float3 cc, out float3 lift)
{
    // Mirror into the corner of space where every coordinate is positive: four of the twenty faces reach it.
    float3 flip = float3(x.x < 0.0 ? -1.0 : 1.0, x.y < 0.0 ? -1.0 : 1.0, x.z < 0.0 ? -1.0 : 1.0);
    float3 a = abs(x);
    float k0 = a.x + a.y + a.z;
    float k1 = a.x / Golden + a.z * Golden;
    float k2 = a.x * Golden + a.y / Golden;
    float k3 = a.y * Golden + a.z / Golden;
    float3 v0 = float3(0.0, 1.0, Golden);
    float3 v1 = float3(Golden, 0.0, 1.0);
    float3 v2 = float3(1.0, Golden, 0.0);
    if (k1 > k0 && k1 >= k2 && k1 >= k3)
    {
        v0 = float3(0.0, 1.0, Golden);
        v1 = float3(0.0, -1.0, Golden);
        v2 = float3(Golden, 0.0, 1.0);
    }
    else if (k2 > k0 && k2 >= k3)
    {
        v0 = float3(Golden, 0.0, 1.0);
        v1 = float3(Golden, 0.0, -1.0);
        v2 = float3(1.0, Golden, 0.0);
    }
    else if (k3 > k0)
    {
        v0 = float3(1.0, Golden, 0.0);
        v1 = float3(-1.0, Golden, 0.0);
        v2 = float3(0.0, 1.0, Golden);
    }

    // Where the direction meets that face, in the face's own grid.
    float3 middle = v0 + v1 + v2;
    float3 on = a * (dot(v0, middle) / max(dot(a, middle), 1e-5));
    float3 e1 = v1 - v0;
    float3 e2 = v2 - v0;
    float3 w = on - v0;
    float2 at = float2(dot(w, 2.0 * e1 - e2), dot(w, 2.0 * e2 - e1)) * (Splits / 6.0);
    float2 cell = floor(at);
    float2 within = at - cell;
    float upper = (within.x + within.y) > 1.0 ? 1.0 : 0.0;
    float2 ia = cell + upper;
    float2 ib = cell + float2(1.0, 0.0);
    float2 ic = cell + float2(0.0, 1.0);
    ca = normalize(v0 + (ia.x * e1 + ia.y * e2) / Splits) * flip;
    cb = normalize(v0 + (ib.x * e1 + ib.y * e2) / Splits) * flip;
    cc = normalize(v0 + (ic.x * e1 + ic.y * e2) / Splits) * flip;
    lift = float3(geLift(ca, pole), geLift(cb, pole), geLift(cc, pole));
}

// One tile: side is how far outside its walls the point is, up how far above the middle of its thickness.
float geTile(float side, float up)
{
    float2 d = float2(side, abs(up) - Thick * 0.5);
    return min(max(d.x, d.y), 0.0) + length(max(d, 0.0)) - 0.006;
}

// Distance to the nearest tile, or to the ball inside them.
float geMap(float3 x, float3 pole)
{
    float r = length(x);
    float reach = (Ball + Highest) * 1.03;
    if (r > reach + 0.04)
    {
        return r - reach;
    }

    float3 ca, cb, cc, lift;
    geTiles(x, pole, ca, cb, cc, lift);
    float ab = dot(x, normalize(ca - cb));
    float bc = dot(x, normalize(cb - cc));
    float ca0 = dot(x, normalize(cc - ca));
    float rest = Ball - Thick * 0.5;
    float da = geTile(Gap - min(ab, -ca0), dot(x, ca) - rest - lift.x);
    float db = geTile(Gap - min(bc, -ab), dot(x, cb) - rest - lift.y);
    float dc = geTile(Gap - min(ca0, -bc), dot(x, cc) - rest - lift.z);
    // Tiles further off are at least this far away: past the edge of the tile the point is over.
    float own = max(min(ab, -ca0), max(min(bc, -ab), min(ca0, -bc)));
    float others = max(own + Gap, r - reach);
    return min(min(min(da, db), min(dc, others)), r - (Ball - Thick - 0.03));
}

// Printer's inks: cyan, magenta and yellow, running into each other.
float3 geInk(float t)
{
    return saturate(1.12 - (0.5 + 0.5 * cos(2.0 * PI * (t + float3(0.0, 0.33, 0.67)))));
}

float3 Render(float2 uv, float2 p)
{
    GeSound = 0.0;
    [unroll]
    for (int part = 0; part < 6; part++)
        GeSound += gePart(0.08 + 0.12 * part) / 6.0;
    GeSince = SongMapped > 0.25 ? BeatPhase : saturate(-log(max(Beat, 0.004)) / 2.75);

    // Every eight beats the tiles fly out together and come back as a new tiling: four, two, six, or three
    // tiles along each edge.
    float phrase = wrap(BeatGlide, 4096.0) / 8.0;
    float tiling = wrap(floor(phrase), 4.0);
    Splits = tiling < 0.5 ? 4.0 : (tiling < 1.5 ? 2.0 : (tiling < 2.5 ? 6.0 : 3.0));
    GeBurst = smoothstep(0.93, 1.0, frac(phrase)) + 1.0 - smoothstep(0.0, 0.07, frac(phrase));

    float turn = wrap(Time * 0.09, 6.2832) + 0.14 * sin(6.2832 * BarPhase);
    float tilt = 0.45 * sin(Time * 0.061);
    float3 eye = float3(sin(turn) * cos(tilt), sin(tilt), cos(turn) * cos(tilt)) * 3.8;
    float3 ray = cameraRay(eye, 0.0, float2(p.x, -p.y), 2.45 - DropPulse * 0.12, 0.0);
    float3 pole = normalize(float3(sin(Time * 0.11), 0.75 * sin(Time * 0.077 + 1.0), cos(Time * 0.11)));
    float gain = 0.9 + Intensity * 0.2;

    // A deep blue studio backdrop, a little lighter behind the ball.
    float3 colour = lerp(float3(0.012, 0.07, 0.2), float3(0.0, 0.025, 0.09), smoothstep(0.2, 1.8, length(p)));
    colour *= 1.0 + Level * 0.2;

    float reach = (Ball + Highest) * 1.03 + 0.04;
    float b = dot(eye, ray);
    float reached = b * b - (dot(eye, eye) - reach * reach);
    if (reached <= 0.0)
    {
        return colour * gain;
    }

    float t = max(-b - sqrt(reached), 0.0);
    float end = -b + sqrt(reached);
    float touch = 0.0016 * surfacePrecision();
    int steps = qualitySteps(100);
    bool hit = false;
    [loop]
    for (int i = 0; i < steps; i++)
    {
        float d = geMap(eye + ray * t, pole);
        if (d < touch)
        {
            hit = true;
            break;
        }

        t += d;
        if (t > end)
        {
            break;
        }
    }

    if (!hit)
    {
        return colour * gain;
    }

    float3 x = eye + ray * t;
    float3 forward = normalize(-eye);
    float3 side = normalize(float3(forward.z, 0.0, -forward.x));
    float3 up = cross(forward, side);
    float3 light = normalize(-forward * 0.6 + side * 0.45 + up * 0.65);
    float beatLight = BeatSwell;
    float r = length(x);

    float3 ca, cb, cc, lift;
    geTiles(x, pole, ca, cb, cc, lift);
    float3 nab = normalize(ca - cb);
    float3 nbc = normalize(cb - cc);
    float3 nca = normalize(cc - ca);
    float ab = dot(x, nab);
    float bc = dot(x, nbc);
    float ca0 = dot(x, nca);

    // Which of the three tiles the point is over, with its two neighbours here: how far inside each shared
    // wall the point is, which way that wall faces, and how far the neighbour has lifted.
    float3 tile = ca;
    float lifted = lift.x;
    float2 inside = float2(ab, -ca0);
    float3 wallA = -nab;
    float3 wallB = nca;
    float2 next = lift.yz;
    float ea = min(ab, -ca0);
    float eb = min(bc, -ab);
    float ec = min(ca0, -bc);
    if (eb > ea && eb >= ec)
    {
        tile = cb;
        lifted = lift.y;
        inside = float2(bc, -ab);
        wallA = -nbc;
        wallB = nab;
        next = lift.zx;
    }
    else if (ec > ea)
    {
        tile = cc;
        lifted = lift.z;
        inside = float2(ca0, -bc);
        wallA = -nca;
        wallB = nbc;
        next = lift.xy;
    }

    float round0 = acos(clamp(dot(tile, pole), -1.0, 1.0)) / PI;
    float3 paint = geInk(dot(tile, float3(0.41, 0.63, 0.29)) * 1.3 + round0 * 0.5 + Time * 0.03 + 0.27 * floor(phrase));
    float loud = geLoud(0.06 + round0 * 0.56);
    // The light each beat sends round the ball from the pole.
    float passing = round0 - GeSince * 1.05;
    float beam = exp(-passing * passing * 60.0) * smoothstep(0.0, 0.08, GeSince) * (1.0 - GeSince);

    if (r < Ball - Thick - 0.03 + touch * 3.0)
    {
        // The ball inside, seen between lifted tiles: lit by the colour of the tile above it.
        float under = saturate(1.0 + dot(normalize(x), ray));
        float3 inner = lerp(float3(0.05, 0.06, 0.12), paint, 0.35) * (0.35 + 0.5 * saturate(dot(normalize(x), light)));
        inner += paint * (0.2 + 1.3 * loud + 0.3 * beatLight + 0.8 * beam) * (0.35 + 0.65 * exp(-lifted * 9.0)) * 0.7;
        return (inner + under * 0.02) * gain;
    }

    float up0 = dot(x, tile) - (Ball - Thick * 0.5) - lifted;
    float sideways = Gap - min(inside.x, inside.y);
    bool top = up0 - Thick * 0.5 > sideways - 0.004;
    bool bottom = -up0 - Thick * 0.5 > sideways - 0.004;
    float3 normal = top ? tile : (bottom ? -tile : (inside.x < inside.y ? wallA : wallB));

    float lit = saturate((dot(normal, light) + 0.35) / 1.35);
    float sky = 0.5 + 0.5 * dot(normal, up);
    float3 shown;
    if (top)
    {
        // White faces. A neighbour that has lifted further leans its shadow onto the edge of this one.
        float2 taller = saturate((next - lifted) / 0.06);
        float2 close = exp(-max(inside - Gap, 0.0) / 0.04);
        float shade = 1.0 - 0.45 * max(taller.x * close.x, taller.y * close.y);
        float rim = smoothstep(0.0, 0.016, min(inside.x, inside.y) - Gap);
        shown = float3(0.93, 0.95, 1.0) * (0.46 + 0.2 * sky + 0.56 * lit) * shade;
        shown = lerp(shown * 0.8 + paint * 0.25, shown, rim);
        // A ring of tiles takes on its colour as its part of the music sounds and as the beat's light passes.
        shown = lerp(shown, paint * (0.75 + 0.5 * lit), saturate(0.36 * loud + 0.45 * beam));
    }
    else if (bottom)
    {
        shown = paint * 0.3 + float3(0.02, 0.03, 0.08);
    }
    else
    {
        // Painted sides, brightest on the tiles that have lifted furthest.
        float out0 = saturate(lifted / 0.2);
        shown = paint * (0.5 + 0.45 * lit + 0.25 * sky) * (0.75 + 0.5 * out0 + 0.6 * loud + 0.5 * beam);
        shown = lerp(shown, float3(1.0, 1.0, 1.0), 0.12);
    }

    float3 half0 = normalize(light - ray);
    shown += pow(saturate(dot(normal, half0)), 50.0) * 0.18;
    return shown * gain;
}
