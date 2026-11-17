// Name: Lidar Street
// A car drives slowly down a city street at night, seen from behind and above, and all there is of the street is what its spinning laser scanner has mapped: buildings, parked cars and lamp posts drawn in fine dots, coloured by height. Round the car the scanner's live rings of laser points run out across the road and climb the walls, brightest just behind its turning head and missing in the shadows of whatever they strike. On every beat a wave of light runs out through the points from the scanner, the bass swells the live points, the mids brighten the lines up the walls, the highs set single dots glinting, and every eight beats the colours change. On the bar the scan glitches: lasers drop out and strips of the picture slip sideways.
// Family: Cyberpunk
// Tags: 3D, Dark, Neon
// Added: 2026-11-16 20:40

static const float LsBlock = 7.0;         // buildings, parked cars and posts repeat along the street every this far, in metres
static const float LsBlocks = 256.0;      // and the whole street repeats after this many stretches, so distances stay small
static const float LsSpeed = 2.6;         // how fast the car drives, metres a second
static const float LsLowest = -0.52;      // the scanner's lowest laser, radians below level
static const float LsLasers = 40.0;        // lasers in the scanner, one above another
static const float LsLaserGap = 0.03;     // radians between neighbouring lasers
static const float LsPerTurn = 720.0;     // points each laser takes in one turn of the head
static const float LsMapGap = 0.3;        // spacing of the mapped dots on every surface, in metres

static float LsCarZ;
static float3 LsSensor;
static float3 LsEye;

float lsBox(float3 p, float3 size)
{
    float3 q = abs(p) - size;
    return length(max(q, 0.0)) + min(max(q.x, max(q.y, q.z)), 0.0);
}

// Distance to the street. what: 0 road, 1 pavement, 2 building, 3 parked car, 4 lamp post, 5 our car.
float lsDE(float3 p, out float what)
{
    float ax = abs(p.x);
    float block = floor(p.z / LsBlock);
    float along = p.z - (block + 0.5) * LsBlock;
    float id = wrap(block, LsBlocks) + (p.x >= 0.0 ? 300.0 : 0.0);

    // The road, and the pavements raised from 5 m out.
    float d = p.y;
    what = 0.0;
    float kerb = max(p.y - 0.15, 5.0 - ax);
    if (kerb < d)
    {
        d = kerb;
        what = 1.0;
    }

    // The building on this stretch, set back a little, with recessed windows on every floor; now and then an empty lot.
    float front = 9.0 + 1.5 * hash(float2(id, 1.0));
    float tall = 4.5 + 13.0 * pow(hash(float2(id, 2.0)), 1.5);
    float building = lsBox(float3(ax - front - 6.0, p.y - tall * 0.5, along), float3(6.0, tall * 0.5, LsBlock * 0.5 - 0.3 - 0.5 * hash(float2(id, 3.0))));
    float floorY = (frac(p.y / 3.2) - 0.5) * 3.2;
    float bay = (frac(along / 1.6 + 0.5) - 0.5) * 1.6;
    float window = lsBox(float3(ax - front, floorY + 0.15, bay), float3(0.2, 0.8, 0.5));
    // (Only near enough to see: far off the recesses would only flicker.)
    if (p.y > 1.2 && p.y < tall - 0.9 && abs(p.z - LsEye.z) < 45.0)
        building = max(building, -window);
    if (hash(float2(id, 4.0)) < 0.12)
        building = 1e3;
    if (building < d)
    {
        d = building;
        what = 2.0;
    }

    // Taller buildings in the row behind, showing over the roofs and through the empty lots.
    float tallBehind = 9.0 + 18.0 * hash(float2(id, 5.0));
    float behind = lsBox(float3(ax - 30.0, p.y - tallBehind * 0.5, along), float3(6.0, tallBehind * 0.5, LsBlock * 0.5 - 0.4));
    if (behind < d)
    {
        d = behind;
        what = 2.0;
    }

    // A car parked at the kerb on some stretches.
    if (hash(float2(id, 6.0)) > 0.45)
    {
        float3 c = float3(ax - 3.5, p.y, along - (hash(float2(id, 7.0)) - 0.5) * 1.6);
        float body = lsBox(c - float3(0.0, 0.62, 0.0), float3(0.72, 0.3, 1.95)) - 0.18;
        float cabin = lsBox(c - float3(0.0, 1.18, -0.25), float3(0.6, 0.22, 1.0)) - 0.14;
        float car = min(body, cabin);
        if (car < d)
        {
            d = car;
            what = 3.0;
        }
    }

    // A lamp post on every other stretch, its arm reaching out over the road.
    if (wrap(block, 2.0) < 0.5)
    {
        float post = max(length(float2(ax - 5.6, along + 2.8)) - 0.08, p.y - 6.6);
        float arm = lsBox(float3(ax - 4.9, p.y - 6.55, along + 2.8), float3(0.7, 0.05, 0.05));
        float lamp = lsBox(float3(ax - 4.25, p.y - 6.45, along + 2.8), float3(0.3, 0.06, 0.14));
        float pole = min(post, min(arm, lamp));
        if (pole < d)
        {
            d = pole;
            what = 4.0;
        }
    }

    // Don't step past the end of the stretch: the next one's building or car may be just beyond it. Nothing of the
    // next stretch comes nearer the middle of the road than 2.6 m.
    d = min(d, max(max(LsBlock * 0.5 - abs(along), 0.0) + 0.05, 2.5 - ax));

    // Our car, with the scanner on its roof.
    float3 e = float3(p.x, p.y, p.z - LsCarZ);
    float body = lsBox(e - float3(0.0, 0.62, 0.0), float3(0.75, 0.3, 2.0)) - 0.18;
    float cabin = lsBox(e - float3(0.0, 1.2, -0.2), float3(0.62, 0.24, 1.05)) - 0.14;
    float rack = lsBox(e - float3(0.0, 1.88, 0.1), float3(0.05, 0.12, 0.05));
    float puck = max(length(e.xz - float2(0.0, 0.1)) - 0.2, abs(e.y - 2.1) - 0.1);
    puck = min(puck, rack);
    float own = min(min(body, cabin), puck);
    if (own < d)
    {
        d = own;
        what = 5.0;
    }
    return d;
}

float3 lsNormal(float3 p)
{
    float ignore;
    float2 e = float2(0.003, -0.003);
    return normalize(e.xyy * lsDE(p + e.xyy, ignore) + e.yyx * lsDE(p + e.yyx, ignore)
                   + e.yxy * lsDE(p + e.yxy, ignore) + e.xxx * lsDE(p + e.xxx, ignore));
}

// Whether the scanner can see the point at `from`, or something stands in the way.
float lsSeen(float3 from, int steps)
{
    float3 dir = LsSensor - from;
    float len = length(dir);
    dir /= len;
    float t = 0.06;
    [loop]
    for (int i = 0; i < steps; i++)
    {
        float what;
        float d = lsDE(from + dir * t, what);
        if (d < 0.008)
            return 0.0;
        t += d;
        if (t > len - 0.35)
            return 1.0;
    }
    return 1.0;
}

// How much of a pixel thin lines at the whole numbers of u cover, about `width` pixels across, given du, how much u
// changes from one pixel to the next. Where the lines crowd closer than a few pixels they fade to an even glow, so they
// never shimmer; far off, where they'd crowd closer still, the glow stays as it is, as if the lines kept their size.
float lsLines(float u, float du, float width)
{
    float apart = abs(frac(u + 0.5) - 0.5) / max(du, 0.00001);
    float sharp = saturate(1.0 - apart / width);
    float average = min(width * du, 0.5 * width);
    return lerp(sharp, average, smoothstep(0.25, 0.5, du));
}

float3 Render(float2 uv, float2 p)
{
    float beats = wrap(BeatGlide, 4096.0);
    float since = SongMapped > 0.25 ? BeatPhase : saturate(-log(max(Beat, 0.004)) / 2.75);
    float bar = floor(beats / 4.0);
    float intoBar = beats - bar * 4.0;
    float set = floor(beats / 8.0);
    float fault = saturate(0.8 * exp(-intoBar * 5.0) + 0.8 * DropPulse);

    // Two colours a set, near and far (or low and high), eased in from the last over half a beat.
    float3 nears[4] = { float3(0.25, 1.05, 1.5), float3(0.35, 1.4, 0.85), float3(0.95, 0.45, 1.6), float3(1.4, 0.85, 0.3) };
    float3 fars[4] = { float3(1.45, 0.3, 1.0), float3(0.35, 0.45, 1.6), float3(1.5, 0.35, 0.45), float3(0.85, 0.3, 1.45) };
    float ease = smoothstep(0.0, 1.0, saturate((beats - set * 8.0) / 0.5));
    int now = (int)wrap(set, 4.0);
    int before = (int)wrap(set - 1.0, 4.0);
    float3 nearColour = lerp(nears[before], nears[now], ease);
    float3 farColour = lerp(fars[before], fars[now], ease);

    // Strips of the picture slip sideways as the bar turns.
    float2 q = float2(p.x, -p.y);
    float strip = floor(p.y / 0.04);
    float slipSeed = wrap(floor(Time * 12.0), 1024.0);
    if (hash(float2(strip, slipSeed)) > 1.0 - 0.35 * fault)
        q.x += (hash(float2(strip, slipSeed + 3.0)) - 0.5) * 0.12 * fault;

    LsCarZ = wrap(Time * LsSpeed, LsBlock * LsBlocks);
    LsSensor = float3(0.0, 2.16, LsCarZ + 0.1);
    float3 eye = float3(1.6 * sin(Time * 0.043), 6.8, LsCarZ - 9.5);
    LsEye = eye;
    float3 forward = normalize(float3(0.0, 0.0, LsCarZ + 11.0) - eye);
    float3 right = normalize(cross(float3(0.0, 1.0, 0.0), forward));
    float3 up = cross(forward, right);
    float3 dir = normalize(forward * 1.15 + q.x * right + q.y * up);

    const float farPlane = 90.0;
    float t = 0.0;
    float what = -1.0;
    int steps = qualitySteps(110);
    [loop]
    for (int i = 0; i < steps; i++)
    {
        float kind;
        float d = lsDE(eye + dir * t, kind);
        if (d < (0.0015 * t + 0.001) * surfacePrecision())
        {
            what = kind;
            break;
        }
        t += d;
        if (t > farPlane)
            break;
    }
    float hitMask = what >= 0.0 ? 1.0 : 0.0;
    t = min(t, farPlane);
    float3 hit = eye + dir * t;
    float3 n = hitMask > 0.5 ? lsNormal(hit) : float3(0.0, 1.0, 0.0);
    float fog = exp(-t * 0.03) * smoothstep(78.0, 42.0, t) * hitMask;

    // The map: dots on a fixed grid over every surface (over our own car, a grid that rides with it).
    float3 a = abs(n);
    float3 local = what > 4.5 ? hit - float3(0.0, 0.0, LsCarZ) : hit;
    float2 m = (a.y > max(a.x, a.z) ? local.xz : (a.x > a.z ? local.zy : local.xy)) / LsMapGap;
    float mapDots = lsLines(m.x, fwidth(m.x), 0.75) * lsLines(m.y, fwidth(m.y), 0.75);

    // The live scan: the lasers' rings of points round the scanner.
    float3 toHit = hit - LsSensor;
    float horiz = length(toHit.xz);
    float range = length(toHit);
    float laser = (atan2(toHit.y, horiz) - LsLowest) / LsLaserGap;
    float turnA = atan2(toHit.x, toHit.z) / 6.2832;
    float turnB = atan2(-toHit.x, -toHit.z) / 6.2832;
    float around = turnA * LsPerTurn;
    float dAround = min(fwidth(around), fwidth(turnB * LsPerTurn));
    float laserNo = floor(laser + 0.5);
    float on = step(-0.5, laser) * step(laser, LsLasers - 0.5);
    on *= step(hash(float2(laserNo, wrap(bar, 1024.0) + 0.5)), 1.0 - 0.2 * fault);
    float live = lsLines(laser, fwidth(laser), 0.8 + 0.5 * Bass) * lsLines(around, dAround, 0.9) * on * hitMask * step(what, 4.5);
    [branch]
    if (live > 0.003)
        live *= lsSeen(hit + n * 0.03, qualitySteps(40));

    // Brightest just behind the turning head; a wave of light across the ground on every beat (only faint up the walls,
    // which it would light all at once), a bigger one when the drop lands.
    float head = frac(Time * 0.33 - turnA);
    float sweep = 0.4 + 0.6 * exp(-head * 5.0);
    float upWall = 1.0 - saturate(n.y * 2.0);
    float waveAt = horiz - since * 20.0;
    float wave = exp(-waveAt * waveAt / 2.0) * (1.0 - since) * smoothstep(0.0, 0.4, since) * (1.0 - 0.75 * upWall);
    float dropAt = range - (1.0 - DropPulse) * 90.0;
    wave += 1.5 * exp(-dropAt * dropAt / 12.0) * DropPulse;

    float3 colour = float3(0.004, 0.005, 0.01) + float3(0.01, 0.012, 0.025) * exp(-abs(dir.y) * 6.0);

    float height = saturate(hit.y / 16.0);
    float3 mapColour = lerp(nearColour, farColour, height);
    float mapLight = what < 0.5 ? 0.14 : (what > 4.5 ? 0.9 : 0.3);
    float2 cell = floor(m + 0.5);
    float glint = step(0.993, hash(cell + float2(wrap(floor(Time * 6.0), 1024.0) * 7.0, floor(a.x * 2.0 + a.z))));
    colour += mapColour * mapDots * fog * (mapLight * (1.0 + 2.0 * wave) + 2.5 * glint * Treble);

    float3 liveColour = lerp(nearColour, farColour, saturate(range / 35.0));
    float liveLight = (sweep * (0.9 + upWall * (0.6 * Mid - 0.1) + (1.0 - upWall) * (0.45 * Bass - 0.15)) + 2.2 * wave) * smoothstep(42.0, 18.0, range);
    colour += lerp(liveColour, float3(1.1, 1.1, 1.15), 0.15) * live * liveLight * 1.4;
    // The scanner's head: a bright mark on the rim of the puck, turning with the sweep.
    float3 onPuck = hit - LsSensor;
    float rim = step(abs(onPuck.y + 0.06), 0.05) * step(0.17, length(onPuck.xz)) * step(length(onPuck.xz), 0.23) * step(4.5, what);
    float facing = frac(Time * 0.33 - atan2(onPuck.x, onPuck.z) / 6.2832 + 0.5) - 0.5;
    colour += nearColour * rim * (0.6 + 2.0 * exp(-facing * facing / 0.004));
    return max(colour, float3(0.0, 0.0, 0.0));
}
