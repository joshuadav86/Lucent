// Name: Neon Maze
// Wander an endless block maze built of light: every wall is a dark slab outlined in neon, the floor is black glass that
// mirrors it, and each wall panel holds a level line that rises and falls with its own band of the spectrum. Every beat
// surges the walk forward and sends a ring of light racing down the corridors, the floor grid glows with the bass, the
// corners sparkle with the treble, and a drop flares the whole maze. Spinning wireframe solids hover in the halls, and
// walking through one swaps the maze's colours.
// Family: Fly-through
// Tags: Neon, 3D, Geometric, Retro
// Added: 2026-11-01 01:40

// The maze is a grid of unit blocks on the floor plane (x, z), one unit tall. Blocks with both coordinates odd are rooms
// and always open, blocks with both even are pillars and always solid, and the rest are passages between two rooms.
// The walk follows a path of rooms that only ever steps +x or +z, so it never crosses itself, and is open along its
// whole length; the other passages are opened at random, which makes the side halls and dead ends.

// Run lengths (in rooms) of the walk's straight stretches: even entries step +x, odd ones +z. A stretch of the walk
// is these 16 runs, rotated by an even amount picked per stretch, so each stretch moves the same 20 rooms in x and
// 15 in z and any room's place along the walk can be found without walking from the start.
static const int MZ_RUNS[16] = { 3, 1, 2, 4, 1, 2, 5, 1, 2, 3, 1, 1, 4, 2, 2, 1 };
static const int MZ_STRETCH = 35;
static const int2 MZ_STRETCH_MOVE = int2(20, 15);

// The rooms of the walk near the camera, MzPath[i] being the room the walk reaches after MzFirst + i rooms.
static const int MZ_KEPT = 48;
static int2 MzPath[MZ_KEPT];
static int MzFirst;

// Icosahedron corners and edges, for the solids hovering in the halls.
static const float3 MZ_ICO[12] = {
    float3(0.000000, -0.525731, -0.850651), float3(-0.525731, -0.850651, 0.000000),
    float3(-0.850651, 0.000000, -0.525731), float3(0.000000, -0.525731, 0.850651),
    float3(-0.525731, 0.850651, 0.000000), float3(0.850651, 0.000000, -0.525731),
    float3(0.000000, 0.525731, -0.850651), float3(0.525731, -0.850651, 0.000000),
    float3(-0.850651, 0.000000, 0.525731), float3(0.000000, 0.525731, 0.850651),
    float3(0.525731, 0.850651, 0.000000), float3(0.850651, 0.000000, 0.525731)
};
static const int2 MZ_ICO_EDGES[30] = {
    int2(0, 1), int2(0, 2), int2(0, 5), int2(0, 6), int2(0, 7), int2(1, 2),
    int2(1, 3), int2(1, 7), int2(1, 8), int2(2, 4), int2(2, 6), int2(2, 8),
    int2(3, 7), int2(3, 8), int2(3, 9), int2(3, 11), int2(4, 6), int2(4, 8),
    int2(4, 9), int2(4, 10), int2(5, 6), int2(5, 7), int2(5, 10), int2(5, 11),
    int2(6, 10), int2(7, 11), int2(8, 9), int2(9, 10), int2(9, 11), int2(10, 11)
};
static const float3 MZ_OCT[6] = {
    float3(1.0, 0.0, 0.0), float3(-1.0, 0.0, 0.0), float3(0.0, 1.0, 0.0),
    float3(0.0, -1.0, 0.0), float3(0.0, 0.0, 1.0), float3(0.0, 0.0, -1.0)
};
static const int2 MZ_OCT_EDGES[12] = {
    int2(0, 2), int2(0, 3), int2(0, 4), int2(0, 5), int2(1, 2), int2(1, 3),
    int2(1, 4), int2(1, 5), int2(2, 4), int2(2, 5), int2(3, 4), int2(3, 5)
};

// A solid hovers in every room whose place along the walk is a multiple of this, plus MZ_SOLID_AT.
static const int MZ_SOLID_EVERY = 10;
static const int MZ_SOLID_AT = 5;

// How far each stretch of the walk rotates its runs: an even number, 0..14.
int mzShift(int stretch)
{
    return 2 * min((int)(hash(float2((float)(stretch & 1023) * 0.7311 + 0.17, 3.71)) * 8.0), 7);
}

int2 mzRunStep(int run)
{
    return (run & 1) == 0 ? int2(1, 0) : int2(0, 1);
}

// Fills MzPath with the rooms from MzFirst on, stepping along the walk one room at a time.
void mzBuildPath(int first)
{
    MzFirst = first;
    int stretch = (int)floor((float)first / (float)MZ_STRETCH);
    int left = first - stretch * MZ_STRETCH;
    int shift = mzShift(stretch);
    int2 at = MZ_STRETCH_MOVE * stretch;
    int run = 0;
    [loop]
    for (int r = 0; r < 16; r++)
    {
        int len = MZ_RUNS[(run + shift) & 15];
        if (left < len)
            break;
        at += mzRunStep(run) * len;
        left -= len;
        run++;
    }
    at += mzRunStep(run) * left;
    int taken = left;

    [loop]
    for (int i = 0; i < MZ_KEPT; i++)
    {
        MzPath[i] = at;
        at += mzRunStep(run);
        taken++;
        if (taken >= MZ_RUNS[(run + shift) & 15])
        {
            taken = 0;
            run++;
            if (run >= 16)
            {
                run = 0;
                stretch++;
                shift = mzShift(stretch);
            }
        }
    }
}

bool mzOnPath(int2 room)
{
    int i = room.x + room.y - MzFirst;
    if (i < 0 || i >= MZ_KEPT)
        return false;
    return MzPath[i].x == room.x && MzPath[i].y == room.y;
}

// Whether the block at b is solid.
bool mzSolid(int2 b)
{
    bool oddX = (b.x & 1) == 1;
    bool oddZ = (b.y & 1) == 1;
    if (!oddX && !oddZ)
        return true;
    if (oddX && oddZ)
        return false;
    // A passage: the two rooms either side of it.
    int2 roomA = oddX ? int2(b.x >> 1, (b.y >> 1) - 1) : int2((b.x >> 1) - 1, b.y >> 1);
    int2 roomB = oddX ? int2(b.x >> 1, b.y >> 1) : int2(b.x >> 1, b.y >> 1);
    bool onA = mzOnPath(roomA);
    bool onB = mzOnPath(roomB);
    if (onA && onB)
        return false;
    // Hashed on the block's place within a 1024-block square, so the hash stays sharp however far the walk goes.
    float roll = hash(float2(b & 1023) * 0.7319 + float2(3.1, 7.7));
    // Beside the walk most passages stay shut so the walk reads as one corridor; elsewhere it's a coin toss.
    return roll > ((onA || onB) ? 0.42 : 0.5);
}

// Where the walk is (on the floor plane) after s rooms: a curve through the middle of each passage that rounds each
// room's corner, with its heading.
float2 mzRoomCentre(int index)
{
    return float2(MzPath[index]) * 2.0 + 1.5;
}

void mzWalk(float s, out float2 position, out float2 heading)
{
    float k = floor(s);
    float f = s - k;
    int i = (int)k - MzFirst;
    float2 c0 = mzRoomCentre(i);
    float2 c1 = mzRoomCentre(i + 1);
    float2 c2 = mzRoomCentre(i + 2);
    float2 m0 = (c0 + c1) * 0.5;
    float2 m1 = (c1 + c2) * 0.5;
    position = lerp(lerp(m0, c1, f), lerp(c1, m1, f), f);
    heading = normalize((c1 - m0) * (1.0 - f) + (m1 - c1) * f);
}

// How bright a line looks dist away (in world units), where one pixel covers footprint world units across it.
float mzLine(float dist, float footprint)
{
    float d = dist / max(footprint, 1e-5);
    return saturate(1.25 - d) + exp(-d * 0.22) * 0.13;
}

// How much world one pixel covers, at distance t, across a line whose sideways direction (in the surface) is across.
float mzFootprint(float t, float pixelAngle, float3 rd, float3 across)
{
    float a = dot(rd, across);
    return t * pixelAngle / max(sqrt(saturate(1.0 - a * a)), 0.08);
}

struct MzColours
{
    float3 wall;
    float3 floorGrid;
    float3 panel;
    float3 corner;
};

// Traces one ray through the maze. Returns what it sees and how far it went; hitFloor says it ended on the floor.
// travelled is how far the ray already came (for a reflection), so lines keep their width and fade.
float3 mzTrace(float3 ro, float3 rd, float travelled, float pixelAngle, MzColours tint, float ring, out float tHit, out bool hitFloor)
{
    hitFloor = false;
    float tPlane = rd.y > 1e-4 ? (1.0 - ro.y) / rd.y : (rd.y < -1e-4 ? -ro.y / rd.y : 1e4);
    float2 v = rd.xz;
    v = float2(abs(v.x) < 1e-5 ? 1e-5 : v.x, abs(v.y) < 1e-5 ? 1e-5 : v.y);
    int2 cell = int2(floor(ro.xz));
    int2 stepDir = int2(v.x > 0.0 ? 1 : -1, v.y > 0.0 ? 1 : -1);
    float2 tDelta = abs(1.0 / v);
    float2 tMax = (float2(cell) + float2(v.x > 0.0 ? 1.0 : 0.0, v.y > 0.0 ? 1.0 : 0.0) - ro.xz) / v;

    float t = 0.0;
    int axis = -1;
    bool wall = false;
    int steps = qualitySteps(40);
    [loop]
    for (int i = 0; i < steps; i++)
    {
        if (tMax.x < tMax.y)
        {
            t = tMax.x;
            if (t > tPlane)
                break;
            cell.x += stepDir.x;
            tMax.x += tDelta.x;
            axis = 0;
        }
        else
        {
            t = tMax.y;
            if (t > tPlane)
                break;
            cell.y += stepDir.y;
            tMax.y += tDelta.y;
            axis = 1;
        }
        if (mzSolid(cell))
        {
            wall = true;
            break;
        }
    }

    float3 colour = float3(0.0, 0.0, 0.0);
    if (!wall)
    {
        if (tPlane > 1e3 || t < tPlane)
        {
            tHit = 1e4;
            return colour;
        }
        t = tPlane;
    }
    tHit = t;
    float3 pos = ro + rd * t;
    float seen = t + travelled;
    float fade = exp(-seen * 0.1) * smoothstep(26.0, 14.0, seen);
    // A ring of light racing out from the camera on each beat.
    float ringGlow = 1.0 + ring * 1.6 * exp(-abs(seen - (1.0 - ring) * 11.0) * 1.4);

    if (wall)
    {
        int2 normal = axis == 0 ? int2(-stepDir.x, 0) : int2(0, -stepDir.y);
        int2 along = axis == 0 ? int2(0, 1) : int2(1, 0);
        float3 along3 = float3((float)along.x, 0.0, (float)along.y);
        float u = dot(pos.xz, float2(along)) - dot(float2(cell), float2(along));
        float y = pos.y;
        float acrossFlat = mzFootprint(seen, pixelAngle, rd, float3(0.0, 1.0, 0.0));
        float acrossUp = mzFootprint(seen, pixelAngle, rd, along3);

        // Top and bottom edges run along every wall.
        float topEdge = mzLine(1.0 - y, acrossFlat);
        float bottomEdge = mzLine(y, acrossFlat);
        // Upright edges only where the wall turns a corner, either way.
        int2 nextUp = cell + along;
        int2 nextDown = cell - along;
        bool cornerUp = !mzSolid(nextUp) || mzSolid(nextUp + normal);
        bool cornerDown = !mzSolid(nextDown) || mzSolid(nextDown + normal);
        float corner = 0.0;
        if (cornerUp)
            corner = max(corner, mzLine(1.0 - u, acrossUp));
        if (cornerDown)
            corner = max(corner, mzLine(u, acrossUp));

        // A panel inset in each face, with a level line playing its own band of the spectrum.
        float faceId = hash(float2(cell & 1023) * 1.37 + float2(normal) * 0.53 + 0.29);
        float level = spectrum(0.04 + faceId * 0.8);
        float2 q = abs(float2(u, y) - 0.5);
        float inset = 0.38;
        float panel = 0.0;
        if (q.x < inset)
            panel = max(panel, mzLine(abs(q.y - inset), acrossFlat));
        if (q.y < inset)
            panel = max(panel, mzLine(abs(q.x - inset), acrossUp));
        float levelY = 0.5 - inset + level * inset * 2.0;
        float levelLine = q.x < inset - 0.04 ? mzLine(abs(y - levelY), acrossFlat) : 0.0;
        float levelFill = (q.x < inset && y < levelY && y > 0.5 - inset) ? 0.05 : 0.0;

        colour = tint.wall * topEdge + tint.floorGrid * bottomEdge * (0.8 + Bass * 0.5)
               + tint.corner * corner * (1.0 + Treble * 0.9)
               + tint.panel * (panel * 0.14 + levelLine * (0.35 + level * 0.9) + levelFill * level);
        colour *= ringGlow;
        // A whisper of fill so near walls hide what's behind them without going flat black.
        colour += tint.wall * 0.012 * (0.4 + y * 0.6);
        // The floor grid's light washing up the foot of each wall.
        colour += tint.floorGrid * exp(-y * 7.0) * (0.03 + Bass * 0.04) * ringGlow;
    }
    else
    {
        bool floorHit = rd.y < 0.0;
        hitFloor = floorHit;
        float2 cellPos = pos.xz;
        float2 nearest = abs(cellPos - round(cellPos));
        float gx = mzLine(nearest.x, mzFootprint(seen, pixelAngle, rd, float3(1.0, 0.0, 0.0)));
        float gz = mzLine(nearest.y, mzFootprint(seen, pixelAngle, rd, float3(0.0, 0.0, 1.0)));
        float grid = max(gx, gz);
        if (floorHit)
            colour = tint.floorGrid * grid * (0.55 + Bass * 0.8) * ringGlow;
        else
            colour = tint.wall * grid * (0.2 + Mid * 0.25) * ringGlow;
    }
    return colour * fade * (1.0 + DropPulse * 0.8);
}

// Draws the edges of one wireframe solid; returns the light it adds in front of whatever is at sceneDepth.
float3 mzSolidEdges(float2 q, float3 eye, float3 forward, float3 side, float3 up, float zoom, float3 centre, float3x3 turn,
                    float size, bool ico, float sceneDepth, float3 colour)
{
    float px = pixelSize();
    float nearest = 1e3;
    int count = ico ? 30 : 12;
    [loop]
    for (int e = 0; e < count; e++)
    {
        float3 cornerA, cornerB;
        if (ico)
        {
            int2 ends = MZ_ICO_EDGES[e];
            cornerA = MZ_ICO[ends.x];
            cornerB = MZ_ICO[ends.y];
        }
        else
        {
            int2 ends = MZ_OCT_EDGES[min(e, 11)];
            cornerA = MZ_OCT[ends.x];
            cornerB = MZ_OCT[ends.y];
        }
        float3 a = centre + mul(turn, cornerA) * size - eye;
        float3 b = centre + mul(turn, cornerB) * size - eye;
        float za = dot(a, forward);
        float zb = dot(b, forward);
        if (za < 0.06 || zb < 0.06)
            continue;
        float2 sa = float2(dot(a, side), dot(a, up)) * zoom / za;
        float2 sb = float2(dot(b, side), dot(b, up)) * zoom / zb;
        float2 ab = sb - sa;
        float h = saturate(dot(q - sa, ab) / max(dot(ab, ab), 1e-8));
        float depth = lerp(za, zb, h);
        if (depth > sceneDepth)
            continue;
        nearest = min(nearest, length(q - sa - ab * h));
    }
    float core = saturate((px * 1.1 - nearest) / px + 0.5);
    return colour * (core + exp(-nearest / (px * 5.0)) * 0.18);
}

float3x3 mzRotation(float3 angles)
{
    float sx, cx, sy, cy, sz, cz;
    sincos(angles.x, sx, cx);
    sincos(angles.y, sy, cy);
    sincos(angles.z, sz, cz);
    float3x3 rx = float3x3(1.0, 0.0, 0.0, 0.0, cx, -sx, 0.0, sx, cx);
    float3x3 ry = float3x3(cy, 0.0, sy, 0.0, 1.0, 0.0, -sy, 0.0, cy);
    float3x3 rz = float3x3(cz, -sz, 0.0, sz, cz, 0.0, 0.0, 0.0, 1.0);
    return mul(rz, mul(ry, rx));
}

float3 Render(float2 uv, float2 p)
{
    // The walk: a steady stroll, and each beat adds a surge (the beat clock only ever moves forward).
    float beats = BeatCount - saturate(Beat);
    float s = 40.0 + Time * 0.55 + beats * 0.16;
    int here = (int)floor(s);
    mzBuildPath(here - MZ_KEPT / 2);

    float2 walkPos, heading, aheadPos, aheadHeading;
    mzWalk(s, walkPos, heading);
    mzWalk(s + 0.45, aheadPos, aheadHeading);
    float2 look = normalize(heading + aheadHeading * 0.8);
    // Lean a little into each turn.
    float turning = heading.x * aheadHeading.y - heading.y * aheadHeading.x;
    float bob = sin(s * 6.2831853) * 0.012;
    float3 eye = float3(walkPos.x, 0.5 + bob, walkPos.y);
    float3 forward = normalize(float3(look.x, -0.02, look.y));
    float zoom = 1.05;
    float roll = turning * 0.07;
    // Screen y runs down, so it's flipped for the camera.
    float2 screen = float2(p.x, -p.y);
    float3 rd = cameraRay(eye, eye + forward, screen, zoom, roll);
    float3 side = normalize(float3(forward.z, 0.0, -forward.x));
    float3 up = cross(forward, side);
    float pixelAngle = pixelSize() / zoom;

    // Walking through a solid swaps the maze's colours: cyan walls on a magenta floor, then the other way round.
    float solidsPassed = (s + 0.5 - (float)MZ_SOLID_AT) / (float)MZ_SOLID_EVERY;
    float passed = floor(solidsPassed);
    float sincePass = solidsPassed - passed;
    float flip = fmod(abs(passed), 2.0) > 0.5 ? 1.0 : 0.0;
    flip = lerp(1.0 - flip, flip, smoothstep(0.0, 0.035, sincePass));
    float3 cyan = float3(0.15, 0.85, 1.0);
    float3 magenta = float3(1.0, 0.25, 0.75);
    float3 gold = float3(1.0, 0.72, 0.3);
    MzColours tint;
    tint.wall = lerp(cyan, magenta, flip) * (0.75 + BuildUp * 0.35);
    tint.floorGrid = lerp(magenta, cyan, flip);
    tint.panel = lerp(lerp(magenta, gold, 0.35), lerp(cyan, gold, 0.25), flip);
    tint.corner = gold;
    float ring = saturate(Beat) * (0.55 + Intensity * 0.45);

    float tHit;
    bool hitFloor;
    float3 colour = mzTrace(eye, rd, 0.0, pixelAngle, tint, ring, tHit, hitFloor);

    // The floor is black glass: it mirrors the maze, most at a glance.
    if (hitFloor)
    {
        float3 mirrorFrom = eye + rd * tHit + float3(0.0, 1e-3, 0.0);
        float3 mirrorDir = float3(rd.x, -rd.y, rd.z);
        float tMirror;
        bool again;
        float3 mirrored = mzTrace(mirrorFrom, mirrorDir, tHit, pixelAngle, tint, ring, tMirror, again);
        float fresnel = 0.22 + 0.4 * pow(1.0 - abs(rd.y), 4.0);
        colour += mirrored * fresnel;
    }

    // The solids hovering ahead along the walk, each a gold icosahedron around a turning octahedron.
    float sceneDepth = tHit * dot(rd, forward);
    float2 q = rotate(screen, roll);
    int firstSolid = (int)ceil((s - 0.5 - (float)MZ_SOLID_AT) / (float)MZ_SOLID_EVERY);
    [loop]
    for (int k = 0; k < 2; k++)
    {
        int room = (firstSolid + k) * MZ_SOLID_EVERY + MZ_SOLID_AT;
        int index = room - MzFirst;
        if (index < 0 || index >= MZ_KEPT)
            continue;
        float3 centre = float3(mzRoomCentre(index).x, 0.5, mzRoomCentre(index).y);
        float away = length(centre - eye);
        if (away > 22.0)
            continue;
        float closeFade = smoothstep(0.3, 1.1, away);
        float spin = Time * 0.4 + beats * 0.35 + (float)room;
        float3x3 outerTurn = mzRotation(float3(spin * 0.7, spin, spin * 0.3));
        float3x3 innerTurn = mzRotation(float3(-spin * 0.9, spin * 0.4 + 1.0, -spin * 0.6));
        float size = 0.2 * (1.0 + Bass * 0.15);
        float fade = exp(-away * 0.1) * closeFade * (0.9 + Beat * 0.4);
        colour += mzSolidEdges(q, eye, forward, side, up, zoom, centre, outerTurn, size, true, sceneDepth, gold * fade);
        colour += mzSolidEdges(q, eye, forward, side, up, zoom, centre, innerTurn, size * 0.5, false, sceneDepth,
                               lerp(magenta, cyan, flip) * fade * (0.8 + Treble * 0.6));
    }

    // A soft gold bloom as a solid is walked through.
    colour += gold * 0.05 * exp(-sincePass * 60.0) * exp(-dot(p, p) * 1.5);

    colour += float3(0.004, 0.004, 0.012);
    // A faint afterglow, so the lines leave a short wake as the walk turns.
    return max(colour, previous(uv) * 0.22);
}
