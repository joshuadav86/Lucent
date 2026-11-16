// Name: Plumage
// A peacock's tail fanned out like an old print on gold paper: fifteen feathers drawn barb by barb in green and
// bronze ink, each tipped with a blue eye. Every feather listens to its own part of the music and stretches as
// it gets louder, the bass in the middle of the fan and the treble at its edges, while the eyes swell on the
// beat and the whole fan shivers.
// Family: Music-shaped
// Tags: Colourful, Retro, Calm
// Added: 2026-11-16 00:07

static const int Feathers = 15;

// How loud the music is at x (0 deep bass, 1 high treble), stretched so that ordinary music swings it most of
// the way between 0 and 1 instead of sitting near the top.
float plLoud(float x)
{
    return saturate((spectrum(x) - 0.56 + 0.5 * x) / 0.4);
}

float3 Render(float2 uv, float2 p)
{
    // Laid out for a wide screen and squeezed sideways to fit a narrow one.
    float2 q = float2(p.x * max(1.0, 1.6 / max(Aspect, 0.3)), p.y);
    float e = pixelSize() * 1.3;

    float3 colour = float3(1.38, 0.9, 0.22) * (1.04 - 0.2 * uv.y) * (1.0 - dot(uv - 0.5, uv - 0.5) * 0.35);
    colour *= 0.96 + 0.06 * fbm(p * 30.0);

    float3 green = float3(0.02, 0.42, 0.24);
    float3 teal = float3(0.0, 0.62, 0.6);
    float3 blue = float3(0.04, 0.14, 0.95);
    float3 bronze = float3(0.62, 0.3, 0.04);
    float3 navy = float3(0.02, 0.03, 0.2);

    float2 root = float2(0.0, 1.26);
    float2 fromRoot = q - root;

    [unroll]
    for (int k = 0; k < Feathers; k++)
    {
        float fk = (float)k;
        float i = fmod(fk, 2.0) < 0.5 ? fk * 0.5 : 14.0 - (fk - 1.0) * 0.5;        // outer feathers first, the middle one last
        float off = (i - 7.0) / 7.0;                                              // -1 to 1 across the fan
        float heard = plLoud(0.04 + 0.55 * abs(off));
        float lean = off * (1.16 + 0.05 * sin(Time * 0.4)) + 0.025 * sin(Time * 0.9 + i * 1.7) + 0.03 * sin(BeatGlide * PI + i) * Treble;
        float len = 1.12 + 0.95 * heard + 0.15 * DropPulse - 0.16 * abs(off);
        float2 v = rotate(fromRoot, lean);
        float along = -v.y;
        float across = v.x;
        float eyeAt = len - 0.17;

        // Barbs: fine lines leaving the shaft at a slant, longest round the eye.
        float rooted = along - 1.25 * abs(across);
        float reachOut = (0.03 + 0.085 * smoothstep(eyeAt - 0.55, eyeAt, rooted)) * smoothstep(len, len - 0.07, rooted) * smoothstep(0.3, 0.5, rooted);
        float toBarb = abs(frac(rooted / 0.024) - 0.5) * 0.024 / 1.6;
        float barb = smoothstep(0.003 + e, 0.003 - e, toBarb) * smoothstep(reachOut, reachOut - 0.014, abs(across));
        colour = lerp(colour, lerp(green, bronze, saturate(abs(across) / max(reachOut, 0.001))), barb * 0.95);

        // The shaft.
        float shaft = smoothstep(e, -e, abs(across) - 0.0045) * step(0.2, along) * step(along, len - 0.05);
        colour = lerp(colour, bronze * 0.7, shaft);

        // The eye: bronze, then teal, then blue, round a dark heart.
        float pulse = 1.0 + 0.16 * BeatSwell * (0.3 + heard);
        float2 inEye = float2(across, along - eyeAt);
        float re = length(inEye / (float2(0.085, 0.115) * pulse));
        float soft = e / 0.085;
        colour = lerp(colour, navy, smoothstep(soft, -soft, re - 1.0));
        colour = lerp(colour, float3(1.05, 0.6, 0.1), smoothstep(soft, -soft, re - 0.93));
        colour = lerp(colour, teal, smoothstep(soft, -soft, re - 0.76));
        colour = lerp(colour, blue, smoothstep(soft, -soft, re - 0.55));
        float heart = length((inEye + float2(0.0, 0.014)) / (float2(0.036, 0.042) * pulse));
        colour = lerp(colour, navy, smoothstep(soft * 2.0, -soft * 2.0, heart - 1.0));
    }

    // The bird's back, hiding where the feathers join.
    float back = length(fromRoot * float2(0.8, 1.0)) - 0.5;
    colour = lerp(colour, blue * 0.85, smoothstep(e, -e, back));
    float scales = abs(frac(length(fromRoot * float2(0.8, 1.0)) / 0.06) - 0.5) * 0.06;
    colour = lerp(colour, teal, smoothstep(e, -e, scales - 0.004) * smoothstep(e, -e, back + 0.02) * 0.8);
    colour = lerp(colour, navy, smoothstep(e, -e, abs(back) - 0.006));

    return colour * (0.96 + 0.05 * Level);
}
