// Name: Puzzle Ball
// A ball cut like a two-by-two puzzle cube, and the music is solving it. Every beat one half of the ball makes a
// quarter turn: sixteen beats scramble its six colours, the next sixteen put them back. The outer shell opens with
// the bass to show the ball inside, which follows each turn a moment late.
// Family: Ray traced
// Tags: Geometric, 3D, Colourful
// Added: 2026-11-15 19:41

// Three cuts through the middle make eight pieces. A turn spins the four pieces on one side of one cut about
// that cut's axis, so at any moment the ball is two halves: one still, one part-way through its quarter turn.
// To colour a point, the turns made so far are undone one by one, newest first, which says where on the
// solved ball that bit of plastic began; its colour is the colour of the solved ball there.
static const float Shell = 1.0;                // outer radius of the shell
static const float Lining = 0.8;               // inner radius of the shell
static const float Core = 0.7;                 // radius of the ball inside
static const float Slit = 0.016;               // half the width of a cut
static const float Scramble = 16.0;            // turns before it starts solving itself again

float3 pbTurn(float3 q, float axis, float angle)
{
    if (axis < 0.5)
    {
        q.yz = rotate(q.yz, angle);
    }
    else if (axis < 1.5)
    {
        q.zx = rotate(q.zx, angle);
    }
    else
    {
        q.xy = rotate(q.xy, angle);
    }

    return q;
}

float pbAlong(float3 q, float axis)
{
    return axis < 0.5 ? q.x : (axis < 1.5 ? q.y : q.z);
}

// Turn number n of the routine: x is its axis (0, 1 or 2), y which half turns (-1 or 1), z which way (-1 or 1).
// The second half of the routine is the first half backwards, so the ball ends up solved.
float3 pbMove(float n, float routine)
{
    float back = n >= Scramble ? 1.0 : 0.0;
    float k = back > 0.5 ? 2.0 * Scramble - 1.0 - n : n;
    float axis = floor(hash(float2(k * 0.731 + 3.3, routine * 1.37 + 0.9)) * 3.0);
    // Never the same axis twice running, so every turn can be seen.
    float before = floor(hash(float2((k - 1.0) * 0.731 + 3.3, routine * 1.37 + 0.9)) * 3.0);
    axis = (k > 0.5 && axis == before) ? wrap(axis + 1.0, 3.0) : axis;
    float half0 = hash(float2(k * 0.417 + 7.1, routine * 0.77 + 2.2)) < 0.5 ? -1.0 : 1.0;
    float way = hash(float2(k * 0.593 + 1.9, routine * 0.91 + 5.4)) < 0.5 ? -1.0 : 1.0;
    return float3(axis, half0, back > 0.5 ? -way : way);
}

// Rounded meeting of two shapes.
float pbBoth(float a, float b, float r)
{
    float2 u = max(float2(r + a, r + b), 0.0);
    return min(-r, max(a, b)) + length(u);
}

// The eight pieces of a shell between two radii, each pushed out from the middle by open.
float pbPieces(float3 q, float outer, float inner, float open)
{
    float3 a = abs(q) - open * 0.57735;
    float r = length(a);
    float3 cut = Slit - a;
    float cuts = max(cut.x, max(cut.y, cut.z));
    float shell = max(r - outer, inner - r);
    return pbBoth(shell, cuts, 0.03);
}

// Outer shell and inner ball, with one half of each part-way through a turn.
float pbMap(float3 x, float3 move, float outerAngle, float innerAngle, float open)
{
    float side = pbAlong(x, move.x) * move.y;
    float3 turnedOuter = pbTurn(x, move.x, -outerAngle);
    float3 turnedInner = pbTurn(x, move.x, -innerAngle);
    float still = min(pbPieces(x, Shell, Lining, open), pbPieces(x, Core, -1.0, 0.0));
    float turning = min(pbPieces(turnedOuter, Shell, Lining, open), pbPieces(turnedInner, Core, -1.0, 0.0));
    return min(max(still, side), max(turning, -side));
}

// The colour of the solved ball in direction n: six colours, one to a face of the cube it was cut from.
float3 pbPaint(float3 n, float inner)
{
    float3 w = pow(abs(n), 5.0);
    w /= w.x + w.y + w.z;
    float3 px = n.x > 0.0 ? float3(1.0, 0.16, 0.62) : float3(0.1, 0.78, 1.0);
    float3 py = n.y > 0.0 ? float3(1.0, 0.86, 0.14) : float3(0.45, 0.3, 1.0);
    float3 pz = n.z > 0.0 ? float3(1.0, 0.45, 0.2) : float3(0.2, 0.95, 0.62);
    float3 paint = px * w.x + py * w.y + pz * w.z;
    // The ball inside wears the same colours a third of the way round.
    return inner > 0.5 ? paint.gbr * 0.85 + 0.15 : paint;
}

// How loud the music is at x (0 deep bass, 1 high treble), stretched so that ordinary music swings it most of
// the way between 0 and 1 instead of sitting near the top.
float pbLoud(float x)
{
    return saturate((spectrum(x) - 0.56 + 0.5 * x) / 0.4);
}

float3 Render(float2 uv, float2 p)
{
    float count = floor(BeatGlide);
    float through = BeatGlide - count;
    float routine = wrap(floor(count / (2.0 * Scramble)), 61.0);
    float n = wrap(count, 2.0 * Scramble);
    float3 move = pbMove(n, routine);
    // The shell leads the turn and the ball inside follows through.
    float lead = 1.0 - pow(1.0 - through, 2.4);
    float follow = through * through * (3.0 - 2.0 * through);
    follow = follow * follow * (3.0 - 2.0 * follow);
    float outerAngle = move.z * lead * PI * 0.5;
    float innerAngle = move.z * follow * PI * 0.5;
    float open = 0.03 + 0.09 * (pbLoud(0.08) + pbLoud(0.14)) + DropPulse * 0.16 + sin(through * PI) * 0.035;

    float turn = Time * 0.11;
    float tilt = 0.5 + 0.2 * sin(Time * 0.07);
    float3 eye = float3(sin(turn) * cos(tilt), sin(tilt), cos(turn) * cos(tilt)) * 4.4;
    float3 ray = cameraRay(eye, 0.0, float2(p.x, -p.y), 2.8 - DropPulse * 0.12, 0.0);
    float gain = 0.9 + Intensity * 0.2;

    // A dusky backdrop that leans a little with the song.
    float3 dusk = lerp(float3(0.3, 0.2, 0.36), float3(0.2, 0.2, 0.38), 0.5 + 0.5 * sin(Time * 0.045));
    float3 colour = dusk * (1.0 - 0.55 * smoothstep(0.3, 1.9, length(p))) * (1.0 + Level * 0.12);

    float reach = Shell + open + 0.06;
    float b = dot(eye, ray);
    float reached = b * b - (dot(eye, eye) - reach * reach);
    if (reached <= 0.0)
    {
        return colour * gain;
    }

    float t = max(-b - sqrt(reached), 0.0);
    float end = -b + sqrt(reached);
    float touch = 0.0015 * surfacePrecision();
    int steps = qualitySteps(80);
    bool hit = false;
    [loop]
    for (int i = 0; i < steps; i++)
    {
        float d = pbMap(eye + ray * t, move, outerAngle, innerAngle, open);
        if (d < touch)
        {
            hit = true;
            break;
        }

        t += d * 0.9;
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
    float2 e = float2(0.002, -0.002);
    float3 normal = normalize(
        e.xyy * pbMap(x + e.xyy, move, outerAngle, innerAngle, open) +
        e.yyx * pbMap(x + e.yyx, move, outerAngle, innerAngle, open) +
        e.yxy * pbMap(x + e.yxy, move, outerAngle, innerAngle, open) +
        e.xxx * pbMap(x + e.xxx, move, outerAngle, innerAngle, open));

    // Which layer, and which half, the point belongs to.
    float side = pbAlong(x, move.x) * move.y;
    bool turning = side > 0.0;
    float3 qOuter = turning ? pbTurn(x, move.x, -outerAngle) : x;
    float3 qInner = turning ? pbTurn(x, move.x, -innerAngle) : x;
    bool inner = pbPieces(qInner, Core, -1.0, 0.0) < pbPieces(qOuter, Shell, Lining, open);
    float3 q = inner ? qInner : qOuter;
    float3 solvedNormal = turning ? pbTurn(normal, move.x, inner ? -innerAngle : -outerAngle) : normal;
    // Take the piece back to where it sat before it was pushed out, then undo every turn so far.
    float3 home = inner ? q : q - sign(q) * open * 0.57735;
    float done = n >= Scramble ? 2.0 * Scramble - n : n;
    [loop]
    for (int j = 0; j < 16; j++)
    {
        float k = done - 1.0 - (float)j;
        if (k < -0.5)
        {
            break;
        }

        float3 past = pbMove(k, routine);
        if (pbAlong(home, past.x) * past.y > 0.0)
        {
            home = pbTurn(home, past.x, -past.z * PI * 0.5);
            solvedNormal = pbTurn(solvedNormal, past.x, -past.z * PI * 0.5);
        }
    }

    float radius = length(home);
    float skin = inner ? smoothstep(Core - 0.035, Core - 0.01, radius) : smoothstep(Shell - 0.035, Shell - 0.01, radius);
    float3 paint = pbPaint(normalize(home), inner ? 1.0 : 0.0);
    // Cut faces and the shell's lining show the plastic underneath: a paler tint of the same colour.
    float3 plastic = lerp(float3(0.93, 0.9, 0.96), paint, 0.3);
    float3 body = lerp(plastic, paint, skin);

    float3 forward = normalize(-eye);
    float3 right = normalize(float3(forward.z, 0.0, -forward.x));
    float3 up = cross(forward, right);
    float3 light = normalize(-forward * 0.55 + right * 0.5 + up * 0.7);
    float lit = saturate((dot(normal, light) + 0.3) / 1.3);
    float sky = 0.5 + 0.5 * dot(normal, up);
    float crevice = saturate(pbMap(x + normal * 0.09, move, outerAngle, innerAngle, open) / 0.09);
    float shade = 0.45 + 0.55 * crevice;
    float3 shown = body * (0.2 + 0.25 * sky + 0.7 * lit) * shade;
    float3 half0 = normalize(light - ray);
    shown += pow(saturate(dot(normal, half0)), 60.0) * 0.35 * shade;
    float edge = pow(saturate(1.0 + dot(normal, ray)), 3.0);
    shown += lerp(paint, float3(1.0, 0.85, 1.0), 0.5) * edge * 0.3 * shade;
    return shown * gain;
}
