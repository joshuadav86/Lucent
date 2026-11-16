// Name: Paint Jets
// Two jets of glossy paint, one orange and one blue, fired at each other across a white studio. The bass drives
// the orange jet and the treble the blue: each arcs higher and reaches further as its side of the music gets
// louder, pushing the place where they collide back and forth. Pulses run down the jets in time with the beat,
// the sound wave shakes them, and the splash rains down as a twisting braid into a marbled pool.
// Family: Music-shaped
// Tags: Colourful, Minimal, Water
// Added: 2026-11-16 00:08

// How loud the music is at x (0 deep bass, 1 high treble), stretched so that ordinary music swings it most of
// the way between 0 and 1 instead of sitting near the top.
float pjLoud(float x)
{
    return saturate((spectrum(x) - 0.56 + 0.5 * x) / 0.4);
}

// One jet, fired from the left at `nozzle` to land on `meet`. Returns how far this spot is above (negative) or
// below the middle of the jet, how thick the jet is here, and how far the spot is from the jet as a whole.
float3 pjJet(float2 q, float2 nozzle, float2 meet, float sag, float climb, float power, float waveAt)
{
    float span = meet.x - nozzle.x;
    float s = clamp(q.x - nozzle.x, 0.0, span);
    float y = nozzle.y + climb * s + sag * s * s;
    float slope = climb + 2.0 * sag * s;
    float waveX = waveAt + 0.45 * s / span;
    float shake = (waveform(waveX - 0.012) + waveform(waveX - 0.004) + waveform(waveX + 0.004) + waveform(waveX + 0.012)) * 0.25;
    y += 0.035 * shake * smoothstep(0.0, 0.4, s) * (0.3 + power);
    float across = (q.y - y) / sqrt(1.0 + slope * slope);
    float thick = 0.026 + 0.02 * power + 0.02 * (0.5 + 0.5 * sin(s * 13.0 - BeatGlide * PI * 2.0));
    float beyond = max(max(nozzle.x - q.x, q.x - meet.x), 0.0);
    return float3(across, thick, length(float2(beyond, across)));
}

float3 Render(float2 uv, float2 p)
{
    // Laid out for a wide screen and squeezed sideways to fit a narrow one.
    float2 q = float2(p.x * 1.6 / max(Aspect, 0.3), p.y);
    float e = pixelSize() * 1.5;

    float3 orange = float3(1.8, 0.4, 0.04);
    float3 blue = float3(0.04, 0.3, 1.7);
    float3 shine = float3(1.3, 1.3, 1.3);

    // The studio: a white wall and a floor a shade darker.
    float3 colour = float3(1.2, 1.17, 1.1) * (1.02 - 0.1 * uv.y);
    colour = lerp(colour, float3(1.0, 0.96, 0.9), smoothstep(-0.03, 0.03, q.y - 0.76));

    float warm = saturate(0.14 + 0.45 * pjLoud(0.08) + 0.45 * pjLoud(0.14) + 0.25 * DropPulse);
    float cool = saturate(0.14 + 0.45 * pjLoud(0.5) + 0.45 * pjLoud(0.72) + 0.25 * DropPulse);
    float both = 0.5 * (warm + cool);
    float2 meet = float2(clamp(1.4 * (warm - cool), -0.9, 0.9), 0.36 - 1.0 * both);
    float2 nozzle = float2(-1.42, 0.5);

    // The harder a jet is driven the flatter and faster it flies.
    float sagL = 0.5 / ((0.45 + 1.4 * warm) * (0.45 + 1.4 * warm));
    float sagR = 0.5 / ((0.45 + 1.4 * cool) * (0.45 + 1.4 * cool));
    float spanL = meet.x - nozzle.x;
    float spanR = -meet.x - nozzle.x;
    float climbL = (meet.y - nozzle.y - sagL * spanL * spanL) / spanL;
    float climbR = (meet.y - nozzle.y - sagR * spanR * spanR) / spanR;
    float2 mirrored = float2(-q.x, q.y);
    float2 meetR = float2(-meet.x, meet.y);

    // Soft shadows of the jets on the wall behind.
    float2 shade = float2(0.05, 0.07);
    float3 shadowL = pjJet(q - shade, nozzle, meet, sagL, climbL, warm, 0.0);
    float3 shadowR = pjJet(mirrored + float2(shade.x, -shade.y), nozzle, meetR, sagR, climbR, cool, 0.5);
    float shadow = max(smoothstep(0.04, 0.0, shadowL.z - shadowL.y), smoothstep(0.04, 0.0, shadowR.z - shadowR.y));
    colour *= 1.0 - 0.13 * shadow * smoothstep(0.82, 0.74, q.y);

    // The pool on the floor, marbled in both colours, with a ripple leaving the place where the braid lands.
    float2 inPool = (q - float2(0.0, 0.89)) / float2(0.95, 0.1);
    float pool = length(inPool) - 1.0;
    float marble = sin(q.x * 7.0 + 2.2 * sin(q.y * 26.0 + q.x * 3.0 + Time * 0.8) + Time * 0.5);
    float3 poolColour = lerp(orange, blue, smoothstep(-0.25, 0.25, marble));
    float sinceBeat = frac(BeatGlide);
    float ripple = abs(length((q - float2(meet.x, 0.89)) / float2(1.0, 0.1)) - sinceBeat * 0.6);
    poolColour = lerp(poolColour, shine, smoothstep(0.02, 0.0, ripple) * (1.0 - sinceBeat) * 0.7);
    float poolEdge = e / 0.1;
    colour = lerp(colour, poolColour, smoothstep(poolEdge, -poolEdge, pool));

    // The braid: both paints falling together from the collision, twisting round each other.
    float fall = q.y - meet.y;
    float twist = fall * 13.0 - Time * 6.0;
    float swing = 0.05 * sin(twist) * smoothstep(0.0, 0.2, fall);
    float falling = step(0.0, fall) * step(q.y, 0.9);
    float strand = 0.022 + 0.014 * both;
    float warmStrand = smoothstep(e, -e, abs(q.x - meet.x - swing) - strand) * falling;
    float coolStrand = smoothstep(e, -e, abs(q.x - meet.x + swing) - strand) * falling;
    bool warmInFront = cos(twist) > 0.0;
    colour = lerp(colour, warmInFront ? blue : orange, warmInFront ? coolStrand : warmStrand);
    colour = lerp(colour, warmInFront ? orange : blue, warmInFront ? warmStrand : coolStrand);

    // The jets themselves, each with a streak of shine along its top.
    float3 jetR = pjJet(mirrored, nozzle, meetR, sagR, climbR, cool, 0.5);
    colour = lerp(colour, blue, smoothstep(e, -e, jetR.z - jetR.y));
    colour = lerp(colour, shine, smoothstep(e, -e, abs(jetR.x + jetR.y * 0.45) - jetR.y * 0.16) * step(jetR.z, jetR.y) * 0.85);
    float3 jetL = pjJet(q, nozzle, meet, sagL, climbL, warm, 0.0);
    colour = lerp(colour, orange, smoothstep(e, -e, jetL.z - jetL.y));
    colour = lerp(colour, shine, smoothstep(e, -e, abs(jetL.x + jetL.y * 0.45) - jetL.y * 0.16) * step(jetL.z, jetL.y) * 0.85);

    // Where they collide: a splash, orange on its side and blue on the other.
    float burst = 0.08 + 0.09 * both + 0.015 * sin(BeatGlide * PI * 2.0);
    float2 fromMeet = q - meet;
    float crown = length(fromMeet) - burst * (1.0 + 0.22 * sin(atan2(fromMeet.y, fromMeet.x + 1e-5) * 7.0 + Time * 5.0));
    float side = fromMeet.x + 0.02 * sin(q.y * 40.0 + Time * 8.0);
    colour = lerp(colour, lerp(orange, blue, smoothstep(-e, e, side)), smoothstep(e, -e, crown));

    // Drops thrown out of the splash, falling back under their own weight.
    [unroll]
    for (int k = 0; k < 16; k++)
    {
        float f = (float)k;
        float seed = hash(float2(f, 4.2));
        float life = frac(BeatGlide * (0.4 + 0.3 * seed) + hash(float2(f, 9.9)));
        float aim = (hash(float2(f, 1.7)) - 0.5) * 2.7;
        float speed = (0.5 + 0.9 * seed) * (0.35 + 0.9 * both);
        float air = life * 1.1;
        float2 at = meet + float2(sin(aim), -cos(aim)) * speed * air + float2(0.0, 0.95 * air * air);
        float size = 0.05 * (1.0 - life) * (0.45 + seed) * smoothstep(0.0, 0.06, life);
        float2 off = q - at;
        float drop = smoothstep(e, -e, length(off) - size);
        colour = lerp(colour, fmod(f, 2.0) < 0.5 ? orange : blue, drop);
        colour = lerp(colour, shine, smoothstep(e, -e, length(off + size * 0.35) - size * 0.28) * drop * 0.85);
    }

    // The two nozzles, each pointing the way its jet leaves.
    float2 aimL = normalize(float2(1.0, climbL));
    float2 aimR = normalize(float2(1.0, climbR));
    float2 fromL = q - nozzle;
    float2 fromR = mirrored - nozzle;
    float alongL = clamp(dot(fromL, aimL), -0.5, 0.02);
    float alongR = clamp(dot(fromR, aimR), -0.5, 0.02);
    float barrel = min(length(fromL - aimL * alongL), length(fromR - aimR * alongR)) - 0.065;
    colour = lerp(colour, float3(0.16, 0.18, 0.22), smoothstep(e, -e, barrel));
    colour = lerp(colour, float3(0.4, 0.43, 0.5), smoothstep(e, -e, abs(barrel + 0.02) - 0.006));

    return colour * (0.96 + 0.04 * Level);
}
