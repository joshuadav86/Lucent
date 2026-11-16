// Name: Eightfold
// A flight through eight screens of glowing latticework, one behind another, each a field of eight-pointed stars
// of coloured glass edged in light, with a jewel set in the heart of every star. The stars turn against their neighbours like
// gears, rocking with each bar, and swell until their points touch in a wave that runs through the screens once
// a beat. Each screen listens to its own part of the music, bass on the nearest and treble on the furthest: its
// lines brighten and its jewels fill with light as that part sounds. Every beat sends a flare from the far end
// toward you, and every eight beats the colours change, from sapphire to amethyst, emerald, and gold.
// Family: Kaleidoscope
// Tags: Geometric, Neon, Colourful, Intense
// Added: 2026-11-16 16:25

static const int Screens = 8;

static float EfSound;                   // how loud the music is just now, taken across all its parts

// One part of the music, with the high notes made up to stand level with the low ones.
float efPart(float x)
{
    return spectrum(x) * (1.0 + 0.9 * x);
}

// How loud one part of the music is, judged against the music as a whole, so that a quiet song shows as plainly
// as a loud one. It listens to a broad part and answers gently, so the light swells and never jitters.
float efLoud(float x)
{
    float heard = (efPart(x - 0.03) + efPart(x) + efPart(x + 0.03)) / 3.0;
    return smoothstep(0.0, 1.0, saturate((heard - 0.55 * EfSound) / (0.8 * EfSound + 0.12)));
}

// The colour of a screen, t running 0 at the nearest to 1 at the furthest, in one of four sets of jewels.
float3 efJewel(float t, float which)
{
    float k = wrap(which, 4.0);
    float hue = k < 0.5 ? 0.5 + 0.3 * t : (k < 1.5 ? 0.7 + 0.32 * t : (k < 2.5 ? 0.52 - 0.3 * t : 0.17 - 0.3 * t));
    float3 c = hsv2rgb(frac(hue + 1.0), 0.88, 1.0);
    return c * c * 1.6 + 0.02;
}

// How far a spot is from the outline of an eight-pointed star of this size (two squares, one turned an eighth
// of a turn), and from the outline of the eight-sided jewel the two squares share. Negative inside.
float2 efStar(float2 c, float size)
{
    float2 a = abs(c);
    float square = max(a.x, a.y) - size;
    float diamond = (a.x + a.y) * 0.70711 - size;
    return float2(min(square, diamond), max(square, diamond));
}

float3 Render(float2 uv, float2 p)
{
    EfSound = 0.0;
    [unroll]
    for (int part = 0; part < 6; part++)
        EfSound += efPart(0.08 + 0.12 * part) / 6.0;

    float px = pixelSize();
    float barTurn = 6.2832 * BarPhase;
    float wave = 6.2832 * BeatPhase + wrap(Time * 0.5, 6.2832);
    float since = SongMapped > 0.25 ? BeatPhase : saturate(-log(max(Beat, 0.004)) / 2.75);
    float flare = smoothstep(0.0, 0.08, since) * (1.0 - since);

    // The colours change every eight beats.
    float phrase = wrap(BeatGlide, 4096.0) / 8.0;
    float changed = smoothstep(0.0, 0.12, frac(phrase));

    float flight = wrap(Time * 0.07, 1.0);
    float3 colour = float3(0.0, 0.0, 0.0);

    [loop]
    for (int i = 0; i < Screens; i++)
    {
        // How far off this screen is: 1 at the far end, 0 as it passes the eye. Each keeps its own part of the
        // music and its own way of turning all the way in.
        float depth = frac((i + 0.5) / Screens - flight);
        float shown = smoothstep(0.0, 0.1, depth) * smoothstep(1.0, 0.8, depth);
        float across = 0.3 + 3.0 * depth * (0.45 + 0.55 * depth);
        float way = (i & 1) == 0 ? 1.0 : -1.0;
        float2 q = rotate(p, 0.3927 * i + way * wrap(Time * 0.021, 6.2832)) * across + 0.5 * (i & 2) * 0.5;

        // Each star sits in its own square of the screen and turns against its four neighbours, like gears.
        float2 square = floor(q + 0.5);
        float gear = wrap(square.x + square.y, 2.0) < 0.5 ? 1.0 : -1.0;
        float2 c = rotate(q - square, gear * (0.32 * sin(barTurn + 0.7 * i) + wrap(Time * 0.045, 6.2832)));

        // The stars swell until their points touch, in a wave that runs through the screens once a beat.
        float size = 0.3536 * (0.8 + 0.2 * cos(wave - 0.8 * i));
        float2 star = efStar(c, size);
        float2 small = efStar(rotate(c, 0.3927), size * 0.42);

        // Lines stay at least a pixel or so wide, however far off the screen is.
        float drawn = 0.006 + 0.007 * depth;
        float wide = max(drawn, 1.3 * px * across);
        float thin = drawn / wide;
        float near = (1.0 - depth) * (1.0 - depth);
        float lines = exp(-pow(star.x / wide, 2.0)) + near * (0.6 * exp(-pow(small.x / wide, 2.0))
                    + 0.35 * exp(-pow(star.y / wide, 2.0)));
        float halo = exp(-abs(star.x) / (wide * 4.0));

        float place = i / (Screens - 1.0);
        float loud = efLoud(0.07 + 0.52 * place);
        // The flare of each beat reaches the far end first and the nearest screen last.
        float reached = depth - (1.0 - since * 1.15);
        float lit = exp(-reached * reached * 30.0) * flare;

        float3 jewel = lerp(efJewel(depth, floor(phrase) - 1.0), efJewel(depth, floor(phrase)), changed);
        // Each star is a pane of coloured glass, with a brighter jewel at its heart that fills with light as the
        // screen's part of the music sounds.
        float pane = smoothstep(0.0, -0.012, star.x) * (0.05 + 0.2 * loud + 0.25 * lit) * (0.2 + 0.8 * near);
        float heart = smoothstep(0.0, -0.03, small.y) * (0.06 + 0.94 * loud) * (0.25 + 0.75 * near);
        float glowing = lines * thin * (0.45 + 1.2 * loud + 1.3 * lit) + halo * (0.015 + 0.07 * loud + 0.12 * lit) + heart * 0.4 + pane;
        float dim = 0.08 + 0.92 * pow(1.0 - depth, 1.4);
        colour += jewel * glowing * shown * dim;
        colour += float3(1.0, 0.98, 0.95) * lines * thin * shown * (0.06 + 0.5 * lit + 0.3 * loud) * dim;
    }

    // A light at the far end, and the brightest crossings eased down so they keep their colour.
    float3 farLight = lerp(efJewel(1.0, floor(phrase) - 1.0), efJewel(1.0, floor(phrase)), changed);
    colour += farLight * 0.3 * exp(-dot(p, p) * 5.0) * (0.5 + 0.7 * BeatSwell);
    colour = colour / (1.0 + 0.22 * max(max(colour.r, colour.g), colour.b));
    return colour * (0.85 + 0.15 * Intensity + 0.2 * DropPulse);
}
