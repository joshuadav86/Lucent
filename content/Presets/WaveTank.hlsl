// Name: Wave Tank
// A glass tank of bright blue water on a table, and the music is the water: the surface heaps up into a wave
// for every part of the sound, deep bass at the left and treble at the right, and falls flat when the music
// stops. Every beat rocks the tank and throws drops into the air, highest where the music is loudest.
// Family: Music-shaped
// Tags: Water, Colourful, Minimal
// Added: 2026-11-16 00:02

// How loud the music is at x (0 deep bass, 1 high treble), stretched so that ordinary music swings it most of
// the way between 0 and 1 instead of sitting near the top.
float wtLoud(float x)
{
    return saturate((spectrum(x) - 0.56 + 0.5 * x) / 0.4);
}

// How high the music lifts the water at x (-1.2 to 1.2 across the tank), 0..1, read at ten places and
// blended smoothly between them.
float wtLift(float x)
{
    float t = clamp((x / 2.4 + 0.5) * 9.0, 0.0, 8.999);
    float i = floor(t);
    float f = t - i;
    f = f * f * (3.0 - 2.0 * f);
    float a = wtLoud(0.02 + 0.065 * i);
    float b = wtLoud(0.02 + 0.065 * (i + 1.0));
    return saturate(lerp(a, b, f));
}

// The height of the water's front edge on the glass at x (smaller is higher up the screen).
float wtSurface(float x, float rest, float rock)
{
    float ripple = 0.012 * sin(x * 13.0 - Time * 3.1) + 0.008 * sin(x * 23.0 + Time * 4.3);
    return rest - 0.62 * wtLift(x) + rock * x + ripple * (0.4 + 0.6 * Level);
}

float3 Render(float2 uv, float2 p)
{
    float2 q = float2(p.x * 1.6 / max(Aspect, 0.3), p.y);
    float e = pixelSize() * 1.5;

    // The wall, the table, and the tank's shadow on the table.
    float3 colour = float3(1.35, 0.95, 0.24) * (1.02 - 0.16 * uv.y) * (1.0 - dot(uv - 0.5, uv - 0.5) * 0.3);
    float table = smoothstep(-e, e, q.y - 0.56);
    colour = lerp(colour, float3(1.05, 0.42, 0.16) * (1.25 - 0.45 * uv.y), table);
    float2 shadowAt = (q - float2(0.12, 0.75)) * float2(0.72, 7.0);
    colour *= 1.0 - 0.3 * exp(-dot(shadowAt, shadowAt) * 1.2);

    float left = -1.2;
    float right = 1.2;
    float top = -0.6;
    float bottom = 0.7;
    float back = 0.11;                                   // how far up the screen the far side of the tank sits
    float inTank = smoothstep(e, -e, max(abs(q.x) - right, max(top - back - q.y, q.y - bottom)));

    float rest = 0.2;
    float rock = 0.07 * sin(BeatGlide * PI) * (0.3 + Bass);
    float front = wtSurface(q.x, rest, rock);
    float farSide = wtSurface(q.x * 0.94 + 0.31, rest, rock) * 0.8 + 0.2 * rest - back;

    // Glass: the air in the tank is a touch cooler than the wall.
    colour = lerp(colour, colour * float3(0.86, 0.97, 1.0) + float3(0.0, 0.03, 0.08), inTank);

    // The top of the water, seen from a little above, then the water against the front glass.
    float topWater = smoothstep(-e, e, q.y - farSide) * smoothstep(e, -e, q.y - front);
    float3 topColour = lerp(float3(0.5, 1.2, 1.4), float3(0.2, 0.95, 1.4), saturate((q.y - farSide) / back));
    topColour += float3(0.5, 0.5, 0.4) * smoothstep(0.75, 1.0, sin(q.x * 9.0 + q.y * 30.0 - Time * 2.0) * 0.5 + 0.5) * 0.35;
    colour = lerp(colour, topColour, topWater * inTank);

    float deep = saturate((q.y - front) / (bottom - rest + 0.5));
    float3 water = lerp(float3(0.06, 0.95, 1.5), float3(0.02, 0.22, 0.95), pow(deep, 0.8));
    // Bands of light slanting down through the water.
    float shafts = smoothstep(0.55, 1.0, sin((q.x + q.y * 0.35) * 7.0 + 0.6 * sin(Time * 0.7 + q.y * 3.0)) * 0.5 + 0.5);
    water += float3(0.12, 0.3, 0.3) * shafts * (1.0 - deep);
    float inWater = smoothstep(-e, e, q.y - front) * inTank;
    colour = lerp(colour, water, inWater);

    // Bubbles climbing through the water.
    [unroll]
    for (int k = 0; k < 14; k++)
    {
        float f = (float)k;
        float seed = hash(float2(f, 5.1));
        float climb = frac(Time * (0.1 + 0.12 * seed) + seed * 9.0);
        float2 at = float2(left + 0.1 + 2.2 * hash(float2(f, 8.7)) + 0.03 * sin(Time * 2.0 + f), bottom - 0.03 - climb * 0.9);
        float size = 0.012 + 0.02 * seed;
        float ring = abs(length(q - at) - size) - 0.004;
        float under = smoothstep(0.0, 0.03, at.y - wtSurface(at.x, rest, rock));
        colour = lerp(colour, float3(0.8, 1.3, 1.45), smoothstep(e, -e, ring) * under * inWater * 0.8);
    }

    // The bright line where the water meets the glass, and the far edge of the surface.
    colour = lerp(colour, float3(1.25, 1.4, 1.45), smoothstep(e * 1.6, 0.0, abs(q.y - front)) * inTank);
    colour = lerp(colour, float3(0.1, 0.6, 1.1), smoothstep(e * 1.2, 0.0, abs(q.y - farSide)) * smoothstep(-e, e, front - q.y) * inTank * 0.6);

    // Drops thrown up on each beat, higher where the music is louder.
    [unroll]
    for (int d = 0; d < 9; d++)
    {
        float f = (float)d;
        float seed = hash(float2(f, 2.3));
        float flight = frac(BeatGlide * 0.5 + seed);
        float x0 = left + 0.2 + 2.0 * frac(seed * 7.13 + 0.11 * f);
        float loud = wtLift(x0);
        float throwUp = (0.5 + 1.5 * loud) * (flight - flight * flight) * 1.6;
        float2 at = float2(x0 + (seed - 0.5) * 0.3 * flight, wtSurface(x0, rest, rock) - throwUp - 0.01);
        float size = (0.014 + 0.022 * loud) * smoothstep(0.0, 0.1, flight) * smoothstep(1.0, 0.85, flight);
        float2 off = q - at;
        float drop = length(off) - size;
        float seen = smoothstep(e, -e, drop) * step(q.y, front) * step(top - 0.5, q.y);
        colour = lerp(colour, float3(0.1, 0.9, 1.5), seen);
        colour = lerp(colour, float3(1.2, 1.4, 1.45), smoothstep(e, -e, length(off + size * 0.35) - size * 0.3) * seen);
    }

    // The glass itself: edges, the rim round the top, and two slanting reflections.
    float frontEdge = abs(max(abs(q.x) - right, max(top - q.y, q.y - bottom)));
    float backEdge = abs(max(abs(q.x) - right, max(top - back - q.y, q.y - top)));
    float3 glass = float3(0.25, 0.5, 0.6);
    colour = lerp(colour, glass, smoothstep(e * 1.6, 0.0, frontEdge - 0.004) * 0.8);
    colour = lerp(colour, glass, smoothstep(e * 1.2, 0.0, backEdge - 0.002) * 0.45);
    float streak = smoothstep(0.05, 0.0, abs(q.x + q.y * 0.5 + 0.55)) + 0.6 * smoothstep(0.02, 0.0, abs(q.x + q.y * 0.5 + 0.4));
    colour += float3(0.2, 0.2, 0.2) * streak * inTank * smoothstep(bottom, top, q.y);

    return colour * (0.95 + 0.05 * Level);
}
