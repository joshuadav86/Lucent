// Name: Slot Canyon
// A slow walk through a narrow sandstone canyon carved smooth by water. Daylight falls in from the slit of sky far
// overhead and bounces from wall to wall, so the rock glows gold near the top, orange in the middle, and violet in
// the deep shade, every layer of the stone showing as a fine line. The music plays in that glow, bass in the sand
// underfoot and treble up by the sky, and each beat sends a wave of light down the canyon ahead.
// Family: Fly-through
// Tags: 3D, Colourful, Calm
// Added: 2026-11-16 05:15

float scLoud(float x)
{
    return saturate((spectrum(x) - 0.42 + 0.5 * x) / 0.7);
}

// How loud the music is for rock at this height, bass at the sand and treble by the sky, read at ten fixed places
// and blended between them.
float scLoudAt(float height)
{
    float k = saturate((height + 1.2) / 3.2) * 9.0;
    float low = floor(k);
    return lerp(scLoud(0.08 + 0.06 * low), scLoud(0.14 + 0.06 * low), smoothstep(0.0, 1.0, k - low));
}

// Where the middle of the canyon runs at each distance along it.
float scMiddle(float z)
{
    return 0.55 * sin(z * 0.35) + 0.25 * sin(z * 0.83 + 1.3);
}

// The stone's layers: which layer a spot belongs to, the layers dipping and wavering through the rock.
float scLayer(float3 p)
{
    return p.y + 0.14 * sin(p.z * 0.9 + p.x * 1.5) + 0.07 * sin(p.z * 2.3 - p.x * 2.1 + 1.0);
}

// How far each wall stands from the middle at one height and distance: the left wall, then the right. Each swells
// and hollows in its own rhythm, where the water once swirled; the canyon narrows toward the sand and flares open
// toward the sky.
float2 scWalls(float y, float z)
{
    float leftWall = 0.2 * sin(y * 1.6 + z * 0.6 + 0.8 * sin(z * 0.31)) + 0.09 * sin(y * 3.1 - z * 0.9 + 0.5);
    float rightWall = 0.2 * sin(y * 1.4 - z * 0.5 + 2.1 + 0.8 * sin(z * 0.27 + 1.0)) + 0.09 * sin(y * 2.7 + z * 1.1 + 2.6);
    return 0.62 + 0.1 * y + 0.6 * smoothstep(1.6, 3.4, y) + float2(leftWall, rightWall);
}

// How far a spot is from the rock (the canyon's air counts as outside it).
float scRock(float3 p)
{
    float x = p.x - scMiddle(p.z);
    float2 walls = scWalls(p.y, p.z);
    float air = min(x + walls.x, walls.y - x);
    float ribs = 0.011 * sin(scLayer(p) * 46.0) + 0.006 * sin(scLayer(p) * 113.0 + 1.0);
    float sand = p.y + 1.25 - 0.05 * sin(p.x * 3.0 + p.z * 1.3) - 0.03 * sin(p.z * 2.9 - p.x * 1.1);
    return min(air + ribs, sand) * 0.5;
}

float3 scNormal(float3 p)
{
    float2 h = float2(0.004, 0.0);
    return normalize(float3(scRock(p + h.xyy) - scRock(p - h.xyy),
                            scRock(p + h.yxy) - scRock(p - h.yxy),
                            scRock(p + h.yyx) - scRock(p - h.yyx)));
}

// The colour of rock by how much light reaches it: violet in deep shade, through red and orange, to gold.
float3 scGlow(float light)
{
    float3 c = lerp(float3(0.07, 0.02, 0.13), float3(0.5, 0.06, 0.16), smoothstep(0.0, 0.28, light));
    c = lerp(c, float3(1.25, 0.34, 0.05), smoothstep(0.2, 0.55, light));
    c = lerp(c, float3(1.6, 0.9, 0.22), smoothstep(0.5, 0.85, light));
    return lerp(c, float3(1.7, 1.45, 0.95), smoothstep(0.8, 1.25, light));
}

float3 Render(float2 uv, float2 p)
{
    float since = frac(BeatGlide);
    float flash = smoothstep(0.0, 0.22, since) * (1.0 - since) * (1.0 - since);

    // Walk the canyon, looking a little way ahead along its turns and slightly up toward the light.
    float gone = Time * 0.28;
    float3 eye = float3(0.0, -0.35 + 0.05 * sin(Time * 0.21), gone);
    float2 beside = scWalls(eye.y, gone);
    eye.x = scMiddle(gone) + 0.5 * (beside.y - beside.x);
    float2 besideAhead = scWalls(0.1, gone + 2.4);
    float3 ahead = float3(scMiddle(gone + 2.4) + 0.5 * (besideAhead.y - besideAhead.x), 0.1 + 0.1 * sin(Time * 0.09), gone + 2.4);
    float3 ray = cameraRay(eye, ahead, float2(p.x, -p.y), 1.25, 0.04 * sin(Time * 0.11));

    float t = 0.05;
    float struck = 0.0;
    int steps = qualitySteps(110);
    [loop]
    for (int i = 0; i < steps; i++)
    {
        float d = scRock(eye + ray * t);
        if (d < 0.0015 * (1.0 + t))
        {
            struck = 1.0;
            break;
        }

        t += d;
        if (t > 14.0)
            break;
    }

    // A ray still skimming a wall when the walk ends has all but touched it.
    if (t < 14.0 && ray.y < 0.55)
        struck = 1.0;

    // The slit of sky, pale and bright, and the warm haze that fills the canyon's far reaches.
    float3 sky = lerp(float3(1.5, 1.2, 0.8), float3(0.75, 1.0, 1.5), smoothstep(0.1, 0.7, ray.y));
    float3 haze = float3(0.9, 0.36, 0.12);
    float3 colour = sky;
    if (struck > 0.5)
    {
        float3 at = eye + ray * t;
        float3 n = scNormal(at);
        float layer = scLayer(at);

        // The stone's own banding: broad bands of paler and redder rock, and the fine lines between layers.
        float banding = noise(float2(layer * 5.0, 7.3)) * 0.6 + noise(float2(layer * 17.0, 2.1)) * 0.4;
        float lines = 0.5 + 0.5 * sin(layer * 46.0);
        float isSand = step(at.y, -1.14);

        // How much daylight reaches this spot: more near the sky, more on faces turned up to it, less in hollows.
        float open = smoothstep(-1.4, 2.6, at.y);
        float hollow = saturate(0.5 + scRock(at + n * 0.3) / 0.3);
        float skyward = 0.5 + 0.5 * n.y;
        float rake = saturate(dot(n, normalize(float3(0.0, 0.75, 0.66))));
        float light = 0.1 + 0.58 * open * hollow + 0.3 * skyward * open * open + 0.3 * rake * (0.25 + 0.75 * open) + 0.16 * hollow * (banding - 0.5);

        // The music's glow in the stone, and the wave of light each beat sends down the canyon.
        float loud = scLoudAt(at.y);
        float reach = at.z - eye.z - 2.2 - since * 8.0;
        float wave = flash * exp(-reach * reach * 0.8);
        light += (0.13 * loud + 0.2 * wave) * (0.4 + 0.6 * hollow);

        float3 rock = scGlow(light) * (0.8 + 0.3 * banding) * (0.9 + 0.1 * lines);
        rock = lerp(rock, scGlow(light * 1.1) * float3(1.0, 0.9, 0.75), isSand * 0.6);
        colour = lerp(rock, haze * (0.5 + 0.5 * open), 1.0 - exp(-t * 0.11));
    }

    return colour * (1.0 - 0.16 * dot(p, p) / (1.0 + Aspect * Aspect));
}
