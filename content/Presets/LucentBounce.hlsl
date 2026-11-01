// Name: Lucent Bounce
// The Lucent logo and name glide around the screen and bounce off its edges like an old screensaver, taking a new neon
// colour with every hit. Beats kick it forward, the wave inside the logo is the song's live waveform, each letter lights
// with its own band of the spectrum, and every wall it touches lights up and throws a spray of sparks. Now and then,
// purely by chance, it lands exactly in a corner, and the whole screen celebrates with rainbow fireworks.
// Family: Geometric
// Tags: Neon, Geometric, Colourful, Retro
// Added: 2026-11-01 02:30

// Half the size of the logo (mark above, name below), edges of its glow included. It bounces when these touch a wall.
static const float2 Half = float2(0.27, 0.235);
// How fast it travels across and down, in screen heights per second of the clock below. The two speeds don't divide
// evenly, so its path never repeats and a corner hit comes when it comes, like the original.
static const float2 Speed = float2(0.43, 0.31);
// Where along its path it starts, as a share of one crossing.
static const float2 Start = float2(0.37, 0.71);
// How close together the two walls must be touched to count as a corner, in seconds of the clock: about as many
// pixels as the logo moves in two or three frames.
static const float CornerTolerance = 0.07;
// Sparks thrown on each wall hit, and on a corner hit.
static const int HitSparks = 40;
static const int CornerSparks = 120;
static const int Fireworks = 5;
static const int FireworkSparks = 44;
// Where each letter of the name starts, in cap heights.
static const float LetterStarts[6] = { 0.0, 1.04, 2.18, 3.52, 4.56, 5.7 };

// Seconds, kicked forward by every beat. Everything the logo does runs on this, so it surges on the beat
// and drifts at a steady pace in silence.
float bounceClock()
{
    return Time + (BeatCount - Beat) * 0.25;
}

float rand(float n)
{
    return frac(sin(n * 12.9898 + 4.1414) * 43758.5453);
}

// How far it can travel each way from the centre.
float2 travel()
{
    return float2(Aspect, 1.0) - Half;
}

// Where along an axis the logo is at clock time s.
float axisPosition(int axis, float s)
{
    float reach = travel()[axis];
    float k = s * Speed[axis] / (2.0 * reach) + Start[axis];
    float n = floor(k);
    float f = k - n;
    bool forward = wrap(n, 2.0) < 0.5;
    return -reach + 2.0 * reach * (forward ? f : 1.0 - f);
}

float2 logoPosition(float s)
{
    return float2(axisPosition(0, s), axisPosition(1, s));
}

// How many times the logo has hit this axis's walls by clock time s.
float hitsSoFar(int axis, float s)
{
    float reach = travel()[axis];
    return floor(s * Speed[axis] / (2.0 * reach) + Start[axis]);
}

// The clock time of hit number j on this axis.
float hitTime(int axis, float j)
{
    float reach = travel()[axis];
    return (j - Start[axis]) * 2.0 * reach / Speed[axis];
}

// Which wall hit number j touches: +1 for the right or bottom, -1 for the left or top.
float hitSide(float j)
{
    return wrap(j, 2.0) > 0.5 ? 1.0 : -1.0;
}

// The logo's colour after a given number of hits in all: a new hue every time, never close to the last one.
float hitHue(float hits)
{
    return frac(hits * 0.618034 + 0.58);
}

float segmentDistance(float2 p, float2 a, float2 b)
{
    float2 pa = p - a;
    float2 ba = b - a;
    float h = saturate(dot(pa, ba) / dot(ba, ba));
    return length(pa - ba * h);
}

// Distance to an arc of a circle, centred on angle mid and reaching half either side (y points down).
float arcDistance(float2 p, float2 centre, float radius, float mid, float half)
{
    float2 d = p - centre;
    float angle = atan2(d.y, d.x + 1e-6);
    float rel = wrap(angle - mid + PI, 2.0 * PI) - PI;
    if (abs(rel) <= half)
        return abs(length(d) - radius);
    float2 a = centre + radius * float2(cos(mid - half), sin(mid - half));
    float2 b = centre + radius * float2(cos(mid + half), sin(mid + half));
    return min(length(p - a), length(p - b));
}

// The letters of the name, drawn in single neon strokes. q is in cap heights, 0..1 down the letter.
float letterDistance(int index, float2 q)
{
    float d = 1e3;
    if (index == 0)        // L
    {
        d = min(segmentDistance(q, float2(0.0, 0.0), float2(0.0, 1.0)), segmentDistance(q, float2(0.0, 1.0), float2(0.7, 1.0)));
    }
    else if (index == 1)   // U
    {
        d = min(segmentDistance(q, float2(0.0, 0.0), float2(0.0, 0.6)), segmentDistance(q, float2(0.8, 0.0), float2(0.8, 0.6)));
        d = min(d, arcDistance(q, float2(0.4, 0.6), 0.4, 0.5 * PI, 0.5 * PI));
    }
    else if (index == 2)   // C
    {
        d = arcDistance(q, float2(0.5, 0.5), 0.5, PI, 0.75 * PI);
    }
    else if (index == 3)   // E
    {
        d = min(segmentDistance(q, float2(0.0, 0.0), float2(0.0, 1.0)), segmentDistance(q, float2(0.0, 0.0), float2(0.7, 0.0)));
        d = min(d, min(segmentDistance(q, float2(0.0, 0.5), float2(0.58, 0.5)), segmentDistance(q, float2(0.0, 1.0), float2(0.7, 1.0))));
    }
    else if (index == 4)   // N
    {
        d = min(segmentDistance(q, float2(0.0, 1.0), float2(0.0, 0.0)), segmentDistance(q, float2(0.0, 0.0), float2(0.8, 1.0)));
        d = min(d, segmentDistance(q, float2(0.8, 1.0), float2(0.8, 0.0)));
    }
    else                   // T
    {
        d = min(segmentDistance(q, float2(0.0, 0.0), float2(0.8, 0.0)), segmentDistance(q, float2(0.4, 0.0), float2(0.4, 1.0)));
    }
    return d;
}

// Distance to a curve y = f(x) across the mark, from its value and slope; past the ends, to the shared tip.
float curveDistance(float2 q, float y, float slope, float halfWidth)
{
    if (abs(q.x) > halfWidth)
        return length(float2(abs(q.x) - halfWidth, q.y));
    return abs(q.y - y) / sqrt(1.0 + slope * slope);
}

// A neon tube: a bright core a few pixels wide with a soft halo around it.
float tube(float d, float width, float px)
{
    float core = saturate(1.0 - (d - width) / (1.5 * px));
    return core + 0.3 * exp(-max(d - width, 0.0) * 70.0);
}

// One spark: a short streak from where it was a moment ago to where it is now.
float spark(float2 p, float2 origin, float2 dir, float speed, float age, float px)
{
    const float drag = 2.6;
    float2 head = origin + dir * speed * (1.0 - exp(-drag * age)) / drag;
    float2 tail = origin + dir * speed * (1.0 - exp(-drag * max(age - 0.05, 0.0))) / drag;
    float d = segmentDistance(p, tail, head);
    float w = 1.6 * px;
    return exp(-(d * d) / (w * w)) + 0.15 * exp(-d / (6.0 * px));
}

float3 Render(float2 uv, float2 p)
{
    float px = pixelSize();
    float s = bounceClock();

    // Trails: the last frame fades, leaving a short neon streak behind the logo and the sparks.
    float3 colour = previous(uv) * pow(0.5, DeltaTime * 60.0);

    // Faint drifting dust, twinkling with the treble.
    float2 cell = floor(p * 18.0 + float2(Time * 0.15, Time * 0.05));
    float2 inCell = frac(p * 18.0 + float2(Time * 0.15, Time * 0.05)) - 0.5;
    float starRnd = hash(cell);
    if (starRnd > 0.82)
    {
        float2 offset = float2(hash(cell + 7.1), hash(cell + 3.3)) - 0.5;
        float twinkle = 0.4 + 0.6 * sin(Time * (1.0 + starRnd * 3.0) + starRnd * 40.0);
        colour += neon(starRnd * 3.0) * exp(-length(inCell - offset * 0.7) * 60.0) * (0.04 + 0.12 * Treble) * twinkle;
    }

    float2 centre = logoPosition(s);
    float2 hits = float2(hitsSoFar(0, s), hitsSoFar(1, s));
    float2 hitAge = float2(s - hitTime(0, hits.x), s - hitTime(1, hits.y));
    float lastHitAge = min(hitAge.x, hitAge.y);
    float hue = hitHue(hits.x + hits.y);

    // A corner: the most recent time both walls were touched almost at once (checked over the last two hits on each).
    float cornerAge = 1e3;
    float2 cornerSide = float2(1.0, 1.0);
    [unroll] for (int ix = 0; ix < 2; ix++)
    {
        [unroll] for (int iy = 0; iy < 2; iy++)
        {
            float jx = hits.x - ix;
            float jy = hits.y - iy;
            float tx = hitTime(0, jx);
            float ty = hitTime(1, jy);
            if (abs(tx - ty) < CornerTolerance)
            {
                float age = s - max(tx, ty);
                if (age >= 0.0 && age < cornerAge)
                {
                    cornerAge = age;
                    cornerSide = float2(hitSide(jx), hitSide(jy));
                }
            }
        }
    }
    float party = cornerAge < 6.0 ? saturate(1.0 - (cornerAge - 3.5) / 2.5) : 0.0;

    // Each wall the logo touches lights up from the point of contact outward.
    [unroll] for (int wall = 0; wall < 2; wall++)
    {
        float j = hits[wall];
        float age = hitAge[wall];
        float side = hitSide(j);
        float other = axisPosition(1 - wall, hitTime(wall, j));
        float wallDistance = (wall == 0 ? Aspect : 1.0) - side * p[wall];
        float along = abs(p[1 - wall] - other);
        float spread = 0.15 + age * 1.6;
        float lit = exp(-age * 2.5) * exp(-along / spread) * (0.6 + 0.8 * Bass);
        colour += hsv2rgb(hue, 0.8, 1.0) * lit * (exp(-max(wallDistance, 0.0) * 90.0) + 0.3 * exp(-max(wallDistance, 0.0) * 12.0));
    }

    // Sparks off the last two hits on each wall, thrown away from the wall along the logo's edge.
    [loop] for (int h = 0; h < 4; h++)
    {
        int axis = h & 1;
        float j = hits[axis] - (h >> 1);
        float t0 = hitTime(axis, j);
        float age = s - t0;
        if (age < 0.0 || age > 1.8)
            continue;
        float side = hitSide(j);
        float2 contact = logoPosition(t0);
        float2 normal = axis == 0 ? float2(-side, 0.0) : float2(0.0, -side);
        float2 tangent = float2(normal.y, -normal.x);
        float2 wallPoint = contact - normal * Half[axis];
        float burstHue = hitHue(hitsSoFar(0, t0 + 0.001) + hitsSoFar(1, t0 + 0.001));
        float fade = exp(-age * 2.2);
        float burst = 0.0;
        [loop] for (int i = 0; i < HitSparks; i++)
        {
            float seed = j * 17.0 + axis * 101.0 + i;
            float angle = (rand(seed) - 0.5) * 2.4;
            float2 dir = normal * cos(angle) + tangent * sin(angle);
            float2 origin = wallPoint + tangent * (rand(seed + 0.5) - 0.5) * 2.0 * Half[1 - axis];
            float speed = (0.4 + 1.3 * rand(seed + 0.25) * rand(seed + 0.75)) * (1.0 + 0.6 * Bass);
            burst += spark(p, origin, dir, speed, age, px) * (0.5 + rand(seed + 0.9));
        }
        colour += hsv2rgb(burstHue, 0.6, 1.0) * burst * fade * 0.9;
    }

    // The corner celebration: a rainbow blast out of the corner, rings of light, and fireworks across the screen.
    if (party > 0.0)
    {
        float2 corner = cornerSide * float2(Aspect, 1.0);
        float a = cornerAge;

        // Rings rolling out from the corner.
        [unroll] for (int r = 0; r < 4; r++)
        {
            float ringAge = a - r * 0.18;
            if (ringAge > 0.0)
            {
                float radius = ringAge * 1.7;
                float d = abs(length(p - corner) - radius);
                colour += hsv2rgb(frac(r * 0.25 + Time * 0.3), 0.7, 1.0) * exp(-d * 140.0) * exp(-ringAge * 0.9) * 1.4;
            }
        }

        // The rainbow blast.
        float blast = 0.0;
        float3 blastColour = 0.0;
        [loop] for (int i = 0; i < CornerSparks; i++)
        {
            float u = (i + rand(i * 3.1)) / CornerSparks;
            float angle = u * 0.5 * PI;
            float2 dir = -cornerSide * float2(cos(angle), sin(angle));
            float speed = 0.8 + 2.4 * rand(i * 7.7) * rand(i * 1.3 + 9.0);
            float b = spark(p, corner, dir, speed, a, px * 1.4);
            blastColour += hsv2rgb(u + Time * 0.2, 0.75, 1.0) * b;
        }
        colour += blastColour * exp(-a * 0.9) * 1.1;

        // Fireworks bursting one after another.
        [loop] for (int f = 0; f < Fireworks; f++)
        {
            float delay = 0.45 + f * 0.5;
            float fa = a - delay;
            if (fa < 0.0 || fa > 2.5)
                continue;
            float fs = floor(hitTime(0, hits.x)) + f * 13.0;
            float2 at = float2((rand(fs) * 1.6 - 0.8) * Aspect, rand(fs + 2.0) * 1.2 - 0.7);
            float fireHue = rand(fs + 4.0);
            float boom = 0.0;
            [loop] for (int i = 0; i < FireworkSparks; i++)
            {
                float angle = (i + rand(fs + i)) * 2.0 * PI / FireworkSparks;
                float2 dir = float2(cos(angle), sin(angle));
                float speed = 0.9 + 0.3 * rand(fs + i * 0.37);
                // A little gravity once it opens.
                float2 drop = float2(0.0, 0.08 * fa * fa);
                boom += spark(p - drop, at, dir, speed, fa, px);
            }
            colour += hsv2rgb(fireHue + 0.08 * sin(fa * 6.0), 0.65, 1.0) * boom * exp(-fa * 1.5) * 1.2;
            colour += hsv2rgb(fireHue, 0.5, 1.0) * exp(-length(p - at) * 20.0) * exp(-fa * 8.0);
        }

        // A soft flash spilling out of the corner.
        colour += hsv2rgb(frac(Time * 0.5), 0.5, 1.0) * exp(-length(p - corner) * 1.5) * exp(-a * 3.0) * 0.6;
    }

    // The logo itself, squashing a little against each wall it hits and swelling on the beat.
    float2 q = p - centre;
    float2 squash = 0.12 * exp(-hitAge * 9.0) * abs(cos(hitAge * 24.0));
    q /= float2(1.0 - squash.x, 1.0 - squash.y);
    q /= 1.0 + 0.03 * Beat + 0.04 * party * (0.5 + 0.5 * sin(Time * 8.0));

    if (all(abs(q) < Half + 0.08))
    {
        // Colour: the hue of the last hit, flashing white the moment it lands, and cycling the rainbow while it celebrates.
        float flash = exp(-lastHitAge * 9.0);
        float3 tint = hsv2rgb(hue, 0.85, 1.0);
        float3 tint2 = hsv2rgb(hue + 0.09, 0.75, 1.0);

        // The mark: the Lucent logo's slim lens (a vesica, two equal circles each through the other's centre) with a
        // wave through its middle that leaves each tip level, the wave following the song's waveform.
        const float markWidth = 0.2;
        const float lens = markWidth / sqrt(3.0);
        float2 m = q - float2(0.0, -0.065);
        m.y /= 1.0 + 0.08 * Bass;
        float t = saturate((m.x + markWidth) / (2.0 * markWidth));
        float dUpper = arcDistance(m, float2(0.0, lens), 2.0 * lens, -0.5 * PI, PI / 3.0);
        float dLower = arcDistance(m, float2(0.0, -lens), 2.0 * lens, 0.5 * PI, PI / 3.0);
        float arc = sin(PI * t);
        float ease = pow(max(arc, 1e-4), 0.6) / 0.84113;
        float amp = 0.43 * lens * (0.85 + 0.4 * Mid);
        float waveY = -amp * sin(2.0 * PI * t) * ease + 0.025 * waveform(t) * arc;
        float waveSlope = -amp * PI / (2.0 * markWidth) / 0.84113
            * (2.0 * cos(2.0 * PI * t) * pow(max(arc, 1e-4), 0.6) + 0.6 * sin(2.0 * PI * t) * cos(PI * t) * pow(max(arc, 1e-4), -0.4));
        float dWave = curveDistance(m, waveY, waveSlope, markWidth);

        float3 markColour = lerp(tint, tint2, t);
        float3 rainbow = hsv2rgb(t * 0.6 + Time * 0.7, 0.8, 1.0);
        markColour = lerp(markColour, rainbow, party);
        float markLight = tube(min(dUpper, dLower), 0.011, px) + tube(dWave, 0.009, px) * (0.9 + 0.6 * Level);
        colour += lerp(markColour, 1.0, 0.65 * flash) * markLight * (1.0 + 0.5 * flash);

        // The name, each letter lit by its own band of the spectrum.
        const float cap = 0.075;
        float2 w = (q - float2(0.0, 0.17)) / cap + float2(3.25, 0.5);
        float3 nameLight = 0.0;
        [unroll] for (int i = 0; i < 6; i++)
        {
            float d = letterDistance(i, w - float2(LetterStarts[i], 0.0)) * cap;
            float band = spectrum(0.06 + i * 0.12);
            float3 letterColour = lerp(lerp(tint2, tint, i / 5.0), hsv2rgb(i / 6.0 + Time * 0.7, 0.8, 1.0), party);
            nameLight += lerp(letterColour, 1.0, 0.35 + 0.5 * flash) * tube(d, 0.0085, px) * (0.75 + 0.7 * band);
        }
        colour += nameLight;
    }

    return colour;
}
