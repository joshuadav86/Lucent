// Name: Enamel
// Five jets of glossy enamel paint leap from below the frame in slow motion against a white studio wall: coral,
// cobalt, saffron, jade, and plum. Each swells where it slows at the top of its arc, then parts into round
// falling drops, and all of them throw soft shadows on the wall. The music plays in the paint: each jet runs milky
// when its part of the music is quiet and floods with colour when it is loud, tinting its shadow, and each beat
// sends a gleam up every jet.
// Family: Ray traced
// Tags: 3D, Colourful, Calm
// Added: 2026-11-16 05:10

static const int EnJets = 5;
static const float EnFall = 1.2;        // how hard the paint is pulled down
static const float EnFoot = -1.3;       // the height the jets start from, just below the frame
static const float EnWall = 1.0;        // how far behind the jets the wall stands
static const float EnShed = 0.5;        // how long between one drop parting from a jet and the next

float enLoud(float x)
{
    return saturate((spectrum(x) - 0.42 + 0.5 * x) / 0.7);
}

float enSmoothMin(float a, float b, float k)
{
    float h = max(k - abs(a - b), 0.0) / k;
    return min(a, b) - h * h * k * 0.25;
}

// Where one jet starts (how far across, and how deep into the room) and how fast it leaves (across and up) just
// now; the throws wander slowly, so the arcs lean and sway. Worked out where it's needed, one jet at a time, which
// keeps the shader small: with all five jets written out in full, an Intel Mac took nine seconds to draw a frame.
void enThrown(int n, out float2 from, out float2 thrown)
{
    float h1 = hash(float2(n, 2.3));
    float h2 = hash(float2(n, 8.6));
    float side = (n % 2 == 0) ? 1.0 : -1.0;
    float across = ((n + 0.5) / EnJets * 2.0 - 1.0) * 0.62 * Aspect - side * 0.42;
    from = float2(across, (h2 - 0.5) * 0.9);
    thrown = float2(side * (0.42 + 0.2 * h1 + 0.1 * sin(Time * 0.19 + n * 2.1)), 1.9 + 0.3 * h2 + 0.1 * sin(Time * 0.23 + n * 1.3));
}

// How far a spot is outside one jet; also how long the paint nearest it has been in the air.
float enJet(float3 p, int n, out float aloft)
{
    float2 from;
    float2 thrown;
    enThrown(n, from, thrown);
    aloft = (p.x - from.x) / thrown.x;

    // The unbroken stream, from the foot to a little past the top of its arc. Paint thickens where it slows.
    float crest = thrown.y / EnFall;
    float parting = crest * 1.4;
    float air = clamp(aloft, 0.0, parting);
    float rise = thrown.y - EnFall * air;
    float height = EnFoot + (thrown.y - 0.5 * EnFall * air) * air;
    float speed = sqrt(thrown.x * thrown.x + rise * rise);
    float off = (p.y - height) * abs(thrown.x) / speed;
    float born = Time + n * 0.37;
    float wide = sqrt(0.0011 * length(thrown) / speed);
    wide *= 1.0 + 0.4 * smoothstep(crest * 0.75, parting, air) * sin(6.2832 * (born - air) / EnShed);
    wide *= lerp(0.72, 1.0, smoothstep(parting, parting - 0.35, air)) * smoothstep(0.0, 0.05, aloft);
    float stream = length(float3(off, (aloft - air) * thrown.x, p.z - from.y)) - wide;

    // Past that it parts into drops, one shed every so often, each falling on along the same arc.
    float drops = 1e3;
    float nearest = floor((born - aloft) / EnShed + 0.5);
    [unroll]
    for (int j = -1; j <= 1; j++)
    {
        float airDrop = born - (nearest + j) * EnShed;
        float3 middle = float3(from.x + thrown.x * airDrop, EnFoot + (thrown.y - 0.5 * EnFall * airDrop) * airDrop, from.y);
        float size = 0.082 * smoothstep(parting - 0.55 * EnShed, parting + 0.1 * EnShed, airDrop);
        drops = min(drops, length(p - middle) - size + 0.2 * (1.0 - step(0.001, size)));
    }

    return enSmoothMin(stream, drops, 0.06);
}

// How far a spot is outside the paint, and which jet is nearest.
float enPaint(float3 p, out int which, out float aloft)
{
    float d = 1e3;
    which = 0;
    aloft = 0.0;
    // One jet at a time, rather than all five written out (see enThrown).
    [loop]
    for (int n = 0; n < EnJets; n++)
    {
        float air;
        float dn = enJet(p, n, air);
        if (dn < d)
        {
            d = dn;
            which = n;
            aloft = air;
        }
    }

    return d * 0.7;
}

// The jets never run into one another, so the surface at a spot is the nearest jet's alone.
float enJetOnly(float3 p, int n)
{
    float aloft;
    return enJet(p, n, aloft);
}

float3 enNormal(float3 p, int n)
{
    float2 h = float2(0.003, 0.0);
    return normalize(float3(enJetOnly(p + h.xyy, n) - enJetOnly(p - h.xyy, n),
                            enJetOnly(p + h.yxy, n) - enJetOnly(p - h.yxy, n),
                            enJetOnly(p + h.yyx, n) - enJetOnly(p - h.yyx, n)));
}

// The white studio the wet paint mirrors: a bright lamp box up and to the left, a paler one to the right, and a
// floor that darkens toward the feet.
float3 enStudio(float3 dir)
{
    float box = smoothstep(0.86, 0.9, dot(dir, normalize(float3(-0.45, 0.62, -0.64))));
    float side = smoothstep(0.9, 0.93, dot(dir, normalize(float3(0.8, 0.15, -0.58))));
    float3 room = lerp(float3(0.28, 0.27, 0.27), float3(0.78, 0.78, 0.8), smoothstep(-0.5, 0.4, dir.y));
    return room + float3(2.4, 2.35, 2.25) * box + float3(0.8, 0.85, 0.95) * side;
}

float3 enPigment(int n)
{
    if (n == 0)
        return float3(1.5, 0.2, 0.16);
    if (n == 1)
        return float3(0.06, 0.2, 1.35);
    if (n == 2)
        return float3(1.55, 0.8, 0.04);
    if (n == 3)
        return float3(0.02, 0.85, 0.5);
    return float3(0.62, 0.07, 0.75);
}

float3 Render(float2 uv, float2 p)
{
    float since = frac(BeatGlide);
    float flash = smoothstep(0.0, 0.1, since) * (1.0 - since) * (1.0 - since);

    float3 eye = float3(0.0, 0.0, -5.0);
    float3 ray = normalize(float3(p.x, -p.y, 5.0));
    float3 lamp = normalize(float3(-0.45, 0.62, -0.64));

    float t = 4.2;
    float struck = 0.0;
    int which = 0;
    float aloft = 0.0;
    int steps = qualitySteps(90);
    [loop]
    for (int i = 0; i < steps; i++)
    {
        float d = enPaint(eye + ray * t, which, aloft);
        if (d < 0.0015)
        {
            struck = 1.0;
            break;
        }

        t += d;
        if (t > 5.9)
            break;
    }

    float3 colour;
    if (struck > 0.5)
    {
        float3 at = eye + ray * t;
        float3 n = enNormal(at, which);

        // Each jet's paint runs pale when its part of the music is quiet and rich when it is loud.
        float loud = enLoud(0.08 + 0.6 * (which + 0.5) / EnJets);
        float3 pigment = enPigment(which);
        float3 paint = lerp(lerp(float3(0.95, 0.94, 0.9), pigment, 0.22), pigment, smoothstep(0.0, 0.85, loud));

        // Each beat sends a gleam up the jet with the paint.
        float gleam = flash * exp(-(aloft - since * 3.4) * (aloft - since * 3.4) * 9.0);
        paint = lerp(paint, float3(1.3, 1.25, 1.15), 0.7 * gleam);

        float facing = saturate(-dot(ray, n));
        float mirror = 0.05 + 0.95 * pow(1.0 - facing, 5.0);
        float lit = saturate(0.45 + 0.55 * dot(n, lamp));
        colour = paint * (0.25 + 0.8 * lit * lit) * (1.0 - mirror) + enStudio(reflect(ray, n)) * (mirror + 0.035);
    }
    else
    {
        // The wall, with the soft shadows the jets throw on it.
        float3 at = eye + ray * ((EnWall - eye.z) / ray.z);
        float shade = 1.0;
        float along = 0.25;
        int caster = 0;
        [loop]
        for (int s = 0; s < 22; s++)
        {
            int nearest;
            float air;
            float d = enPaint(at + lamp * along, nearest, air);
            if (9.0 * d / along < shade)
            {
                shade = 9.0 * d / along;
                caster = nearest;
            }

            along += clamp(d, 0.03, 0.2);
            if (shade < 0.02 || along > 2.6)
                break;
        }

        // Each shadow takes a little of its jet's colour as that jet grows loud.
        float3 wall = float3(0.97, 0.96, 0.94) * (1.0 - 0.1 * dot(p, p) / (1.0 + Aspect * Aspect));
        float3 dim = lerp(float3(0.6, 0.62, 0.7), 0.45 + 0.4 * enPigment(caster), 0.6 * enLoud(0.08 + 0.6 * (caster + 0.5) / EnJets));
        colour = wall * lerp(dim, float3(1.0, 1.0, 1.0), saturate(shade));
    }

    return colour;
}
