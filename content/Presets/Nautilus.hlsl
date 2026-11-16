// Name: Nautilus
// A nautilus shell cut in half and laid under a lamp, with no end to it either way: polished white walls of
// mother-of-pearl wind out from a great pearl at the middle, and curved partitions divide the spiral into deep
// chambers with a pearl resting in every one. The shell grows slowly as you watch. The music plays in the pearls:
// each one lights its chamber with its own part of the music, treble in the tiny chambers at the middle and bass
// in the great ones at the rim, and each beat sends light running out along the spiral.
// Family: Ray traced
// Tags: 3D, Colourful, Calm
// Added: 2026-11-16 08:40

static const float NlGrow = 0.833;      // how much wider the shell is after each turn (2.3 times), as a logarithm
static const float NlRooms = 15.0;      // how many chambers lie in one turn
static const float NlDeep = 0.15;       // how deep a chamber is, against how far out it lies
static const float NlBend = 0.55;       // how far the partitions curve
static const float NlPearl = 0.055;     // how big a chamber's pearl is, against how far out it lies

float nlLoud(float x)
{
    return saturate((spectrum(x) - 0.42 + 0.5 * x) / 0.7);
}

// How loud the music is for a chamber this far out, treble at the middle and bass at the rim, read at ten fixed
// places and blended between them.
float nlLoudAt(float out01)
{
    float k = saturate(1.0 - out01) * 9.0;
    float low = floor(k);
    return lerp(nlLoud(0.08 + 0.06 * low), nlLoud(0.14 + 0.06 * low), smoothstep(0.0, 1.0, k - low));
}

// The cut shell at one spot on the table: how far inside a chamber the spot lies from the chamber's rim (below
// nothing on a wall), how far across its turn the spot is, and how far along the spiral, counted in chambers.
// Distances are measured against how far out the spot lies.
float3 nlShell(float2 s, float grown, float turned)
{
    float far = max(length(s), 1e-4);
    float bearing = atan2(s.y, s.x + 1e-7) - turned;
    float wound = (log(far) - grown) / NlGrow - bearing / 6.2832;
    float whorl = floor(wound);
    float within = wound - whorl;                       // 0 at the inner wall of this turn, 1 at the outer
    float swing = 3.1416 * within;
    float along = (bearing + 6.2832 * whorl) * NlRooms / 6.2832 - NlBend * sin(swing);
    float inRoom = along - floor(along);

    // How far the spot is from the nearest wall and the nearest partition, with the walls' thickness taken off.
    float lean = NlBend * 3.1416 * cos(swing);
    float steep = sqrt((NlRooms + lean) * (NlRooms + lean) / 39.478 + lean * lean / (NlGrow * NlGrow));
    float toWall = min(within, 1.0 - within) * 0.826 - 0.03;
    float toPartition = min(inRoom, 1.0 - inRoom) / steep - 0.017;

    // The two meet in a rounded corner.
    float h = saturate(0.5 + 0.5 * (toPartition - toWall) / 0.05);
    return float3(lerp(toPartition, toWall, h) - 0.05 * h * (1.0 - h), within, along);
}

// How far below the cut face the shell's surface lies at one spot: nothing on a wall, and a rounded hollow in
// each chamber.
float nlDepth(float2 s, float grown, float turned)
{
    float u = 1.0 - saturate(nlShell(s, grown, turned).x / 0.12);
    return length(s) * NlDeep * sqrt(saturate(1.0 - u * u));
}

float3 nlGlow(float out01)
{
    float3 c = lerp(float3(0.15, 1.05, 1.25), float3(0.65, 0.4, 1.4), smoothstep(0.0, 0.35, out01));
    c = lerp(c, float3(1.5, 0.32, 0.5), smoothstep(0.3, 0.65, out01));
    return lerp(c, float3(1.65, 0.75, 0.15), smoothstep(0.6, 0.95, out01));
}

// What the polished shell mirrors: a broad soft lamp overhead and a dim room.
float3 nlLamps(float3 dir)
{
    float panel = smoothstep(0.3, 0.9, dir.y) * smoothstep(1.2, 0.1, abs(dir.x + 0.3));
    return float3(0.85, 0.88, 1.0) * panel * 0.9 + float3(0.03, 0.03, 0.045);
}

float3 Render(float2 uv, float2 p)
{
    float since = frac(BeatGlide);
    float flash = smoothstep(0.0, 0.22, since) * (1.0 - since) * (1.0 - since);
    float grown = wrap(Time * 0.04, NlGrow);
    float turned = -Time * 0.012;

    float3 eye = float3(0.16 * sin(Time * 0.05), 3.6, -1.8 + 0.13 * sin(Time * 0.037));
    float3 ray = cameraRay(eye, float3(0.1, 0.0, 0.08), float2(p.x, -p.y), 2.6, 0.03 * sin(Time * 0.04));
    // The lamp circles slowly overhead, so the shadows in the chambers and the sheen on the shell keep shifting.
    float lampTurn = Time * 0.1;
    float3 lamp = normalize(float3(-0.61 * cos(lampTurn), 0.75, -0.61 * sin(lampTurn)));
    float drift = frac(Time * 0.02);

    // Where the ray meets the cut face, and, in a chamber, where it meets the hollow below.
    float reach = -eye.y / min(ray.y, -0.05);
    float3 top = eye + ray * reach;
    float3 first = nlShell(top.xz, grown, turned);
    float3 at = top;
    if (first.x > 0.0)
    {
        float3 above = top;
        float3 below = top;
        int steps = qualitySteps(36);
        [loop]
        for (int i = 0; i < steps; i++)
        {
            below = above + ray * (0.012 * max(length(above.xz), 0.02));
            if (-below.y > nlDepth(below.xz, grown, turned))
                break;

            above = below;
        }

        [unroll]
        for (int j = 0; j < 4; j++)
        {
            float3 between = 0.5 * (above + below);
            if (-between.y > nlDepth(between.xz, grown, turned))
                below = between;
            else
                above = between;
        }

        at = 0.5 * (above + below);
    }

    // This chamber's pearl: where it rests, how big it is, and how far out it lies.
    float along01 = (floor(first.z) + 0.5 + NlBend) / NlRooms;
    float middle = grown + NlGrow * (0.5 + along01);
    float pearlFar = exp(middle);
    float angle = 6.2832 * frac(along01) + turned;
    float3 pearlAt = float3(pearlFar * cos(angle), -(NlDeep - NlPearl) * pearlFar, pearlFar * sin(angle));
    float pearlSize = NlPearl * pearlFar;

    // Its own part of the music, and the light each beat sends out along the spiral.
    float out01 = saturate((middle + 2.5) / 3.6);
    float loud = nlLoudAt(out01);
    float run = flash * exp(-(out01 - since * 1.3) * (out01 - since * 1.3) * 22.0);
    float3 glow = nlGlow(out01);
    float alight = 0.15 + 0.4 * loud + 0.2 * run;

    // The cut face: polished mother-of-pearl, near white, with a sheen of rose, green, and blue that shifts along
    // the spiral, and the pearls' light seeping through the thin shell at its edges.
    float3 up = float3(0.0, 1.0, 0.0);
    float3 faceSheen = 0.5 + 0.5 * cos(6.2832 * (-ray.y * 1.2 + first.y + first.z / NlRooms + drift + float3(0.0, 0.33, 0.67)));
    float3 facePearl = lerp(float3(0.88, 0.85, 0.8), faceSheen, 0.17);
    float3 faceBounce = reflect(ray, up);
    float faceMirror = 0.05 + 0.95 * pow(1.0 - saturate(-ray.y), 5.0);
    float3 fromPearlTop = (top - pearlAt) / pearlFar;
    float seep = saturate(1.0 + first.x / 0.03);
    float3 colour = facePearl * (0.2 + 0.62 * lamp.y) + nlLamps(faceBounce) * (faceMirror + 0.05)
                  + pow(saturate(dot(faceBounce, lamp)), 60.0) * 0.5
                  + glow * alight * seep * seep * 0.02 / (0.02 + dot(fromPearlTop, fromPearlTop));

    if (first.x > 0.0)
    {
        float far = max(length(at.xz), 1e-4);
        float sunk = saturate(-at.y / (NlDeep * far));  // 0 on the cut face, 1 on a chamber's floor
        float3 shell = nlShell(at.xz, grown, turned);

        // Which way the hollow faces.
        float e = 0.004 * far;
        float here = nlDepth(at.xz, grown, turned);
        float2 slope = float2(nlDepth(at.xz + float2(e, 0.0), grown, turned) - here,
                              nlDepth(at.xz + float2(0.0, e), grown, turned) - here) / e;
        slope = clamp(slope, -5.0, 5.0);
        float3 n = normalize(float3(slope.x, 1.0, slope.y));

        float3 sheen = 0.5 + 0.5 * cos(6.2832 * (dot(n, -ray) * 1.2 + shell.y + shell.z / NlRooms + 0.35 * sunk + drift + float3(0.0, 0.33, 0.67)));
        float3 pearl = lerp(float3(0.8, 0.77, 0.73), sheen, 0.26);
        float3 bounce = reflect(ray, n);
        float mirror = 0.05 + 0.95 * pow(1.0 - saturate(dot(n, -ray)), 5.0);

        // The rim shades the lamp from the hollow; the pearl lights it from within.
        float2 lampward = at.xz + lamp.xz / lamp.y * -at.y;
        float unshaded = smoothstep(-0.01, 0.03, nlShell(lampward, grown, turned).x);
        float3 toPearl = (pearlAt - at) / pearlFar;
        float apart = dot(toPearl, toPearl);
        float3 pearlward = toPearl / sqrt(apart + 1e-5);
        float lantern = (0.25 + 0.75 * saturate(dot(n, pearlward))) * 0.024 / (0.007 + apart);
        float3 chamber = pearl * (0.035 + 0.34 * saturate(dot(n, lamp)) * unshaded) * (1.0 - 0.45 * sunk)
                       + glow * alight * (min(lantern, 1.5) + 0.5 * pow(saturate(dot(bounce, pearlward)), 40.0)) * (0.8 + 0.2 * pearl)
                       + nlLamps(bounce) * mirror * 0.5 + pow(saturate(dot(bounce, lamp)), 60.0) * unshaded * 0.6;

        // The pearl itself, where it shows in front of the hollow.
        float3 back = top - pearlAt;
        float toward = dot(back, ray);
        float miss = sqrt(max(dot(back, back) - toward * toward, 0.0));
        float grain = reach * pixelSize() / 2.6;
        float reached = -toward - sqrt(max(pearlSize * pearlSize - miss * miss, 0.0));
        float shows = smoothstep(pearlSize, pearlSize - 1.5 * grain, miss) * step(reached, length(at - top));
        if (shows > 0.0)
        {
            float3 onPearl = normalize(back + ray * reached);
            float square = saturate(dot(onPearl, -ray));
            float3 off = reflect(ray, onPearl);
            float3 bead = float3(0.8, 0.78, 0.76) * (0.06 + 0.5 * saturate(dot(onPearl, lamp)) * unshaded)
                        + nlLamps(off) * (0.06 + 0.5 * pow(1.0 - square, 3.0)) + pow(saturate(dot(off, lamp)), 50.0) * 0.8 * unshaded
                        + glow * (0.06 + alight) * (0.5 + 1.1 * square * square) + float3(1.0, 0.95, 0.9) * alight * alight * pow(square, 5.0) * 1.2;
            chamber = lerp(chamber, bead, shows);
        }

        // The rim, softened to the grain of the picture.
        colour = lerp(colour, chamber, smoothstep(0.0, 1.5 * grain / (far * max(-ray.y, 0.3)), first.x));
    }

    // The great pearl at the middle.
    float3 fromGreat = eye - float3(0.0, 0.03, 0.0);
    float nearing = dot(fromGreat, ray);
    float under = nearing * nearing - dot(fromGreat, fromGreat) + 0.0081;
    if (under > 0.0)
    {
        float3 onGreat = normalize(fromGreat + ray * (-nearing - sqrt(under)));
        float3 offGreat = reflect(ray, onGreat);
        float rim = pow(1.0 - saturate(dot(onGreat, -ray)), 3.0);
        float3 tint = 0.5 + 0.5 * cos(6.2832 * (dot(onGreat, -ray) * 0.9 + float3(0.0, 0.33, 0.67)));
        colour = lerp(float3(0.84, 0.81, 0.78), tint, 0.16) * (0.16 + 0.6 * saturate(dot(onGreat, lamp)))
               + nlLamps(offGreat) * (0.08 + 0.6 * rim) + pow(saturate(dot(offGreat, lamp)), 50.0) * 0.9
               + nlGlow(0.0) * (0.05 + 0.3 * nlLoud(0.62)) * (0.3 + 0.7 * rim);
    }

    return colour * (1.0 - 0.2 * dot(p, p) / (1.0 + Aspect * Aspect));
}
