// Name: Dance Floor
// A light-up disco floor stretching away into the haze of a dark club: rounded glass tiles glowing deep blue from
// below, with the ceiling lights glinting in them. Each column of tiles lights up in colour away from you as its
// part of the music gets louder, bass down the middle and treble at the sides. Every beat sends a ring of light
// out across the floor and slides the colours one step along.
// Family: Music-shaped
// Tags: 3D, Colourful, Retro
// Added: 2026-11-16 00:06

// How loud the music is at x (0 deep bass, 1 high treble), stretched so that ordinary music swings it most of
// the way between 0 and 1 instead of sitting near the top.
float dfLoud(float x)
{
    return saturate((spectrum(x) - 0.56 + 0.5 * x) / 0.4);
}

float3 Render(float2 uv, float2 p)
{
    float sway = 0.55 * sin(Time * 0.21);
    float3 eye = float3(sway, 1.05, -2.3);
    float3 ray = cameraRay(eye, float3(-sway * 0.3, 0.0, 1.8), float2(p.x, -p.y), 1.5, 0.03 * sin(Time * 0.17));

    // The far wall: dark, with coloured haze hanging low over the floor and a row of small lights.
    float3 hazeTint = lerp(float3(0.55, 0.06, 0.42), float3(0.06, 0.14, 0.6), 0.5 + 0.5 * sin(Time * 0.13 + ray.x * 2.0));
    float3 colour = float3(0.012, 0.006, 0.03) + hazeTint * exp(-max(ray.y, 0.0) * 9.0) * 0.55;
    float lampRow = ray.x / max(ray.z, 0.05) * 9.0;
    float2 toLamp = float2((frac(lampRow) - 0.5) / 9.0, ray.y - 0.035);
    colour += float3(1.4, 1.0, 0.6) * smoothstep(0.0035, 0.002, length(toLamp)) * step(0.0, ray.y);

    if (ray.y < -0.001)
    {
        float t = -eye.y / ray.y;
        float3 at = eye + ray * t;
        float2 g = at.xz / 0.5;
        float2 id = floor(g);
        float2 inTile = g - id - 0.5;
        float2 fromEdge = abs(inTile) - 0.36;
        float d = length(max(fromEdge, 0.0)) + min(max(fromEdge.x, fromEdge.y), 0.0) - 0.09;     // below zero inside the tile
        float soft = pixelSize() * t * 1.3 / max(-ray.y, 0.12);
        float inside = smoothstep(soft, -soft, d);

        // This tile's column listens to one part of the music; the louder it is, the further away the column lights.
        float column = abs(id.x + 0.5);
        float heard = dfLoud(0.07 + 0.05 * column);
        float row = id.y + 4.0;
        float bar = saturate((0.8 + heard * heard * 11.0 - row) * 0.55) * (1.0 - 0.035 * clamp(row, 0.0, 14.0));

        // A ring of light spreading from the middle of the floor on every beat.
        float sinceBeat = frac(BeatGlide);
        float fromMiddle = length(id + 0.5 - float2(0.0, 1.0));
        float gap = fromMiddle - sinceBeat * 13.0;
        float ring = exp(-gap * gap / 2.2) * (1.0 - sinceBeat) * smoothstep(0.0, 0.45, sinceBeat);

        float hue = frac((id.x + id.y) * 0.11 + BeatGlide * 0.125);
        float3 glass = hsv2rgb(hue, 0.95, 1.0);
        glass *= glass;                                                             // deeper, purer colours
        // Tiles the music hasn't reached idle in deep blue, so the floor never goes dark between beats.
        float3 idle = float3(0.1, 0.04, 0.85) * (0.88 + 0.2 * sin(Time * 0.7 + hash(id + 31.0) * 2.0 * PI));
        float lightUp = 1.1 * bar + 0.5 * ring + 0.4 * DropPulse;
        float3 lit = lerp(idle, glass * 1.1, bar) + glass * (0.5 * ring + 0.4 * DropPulse);
        float3 tile = lit * (0.62 + 0.38 * smoothstep(0.0, -0.3, d));
        tile += lit * 0.3 * smoothstep(-0.07, -0.02, d);                             // light caught in the tile's rim
        tile += float3(0.3, 0.3, 0.3) * smoothstep(1.1, 1.8, lightUp);            // the brightest tiles run towards white
        float3 floorColour = lerp(float3(0.008, 0.006, 0.016), tile, inside);

        // Ceiling lights mirrored in the glass.
        float2 above = at.xz + ray.xz * (3.2 / max(-ray.y, 0.02));
        float2 spotCell = above / 2.6 + float2(0.5, Time * 0.02);
        float spot = smoothstep(0.17, 0.06, length(frac(spotCell) - 0.5));
        float glance = pow(1.0 - saturate(-ray.y), 3.0);
        floorColour += float3(0.9, 0.8, 1.0) * spot * (0.1 + 0.9 * glance) * 0.22 * (0.4 + 0.6 * inside);
        floorColour += hazeTint * glance * 0.06 * inside;

        colour = lerp(floorColour, colour, max(1.0 - exp(-t * 0.07), smoothstep(-0.07, -0.015, ray.y)));
    }

    // Four beams of coloured light from the ceiling, swinging in time with the beat.
    [unroll]
    for (int k = 0; k < 4; k++)
    {
        float f = (float)k;
        float2 lampAt = float2((f - 1.5) * 0.75 * Aspect / 1.6, -1.15);
        float2 toHere = p - lampAt;
        float aim = (1.5 - f) * 0.22 + 0.5 * sin(BeatGlide * PI * 0.25 + f * 1.9);
        float2 across = float2(cos(aim), -sin(aim));
        float side = dot(toHere, across);
        float down = dot(toHere, float2(sin(aim), cos(aim)));
        float beam = smoothstep(0.035 + 0.09 * down, 0.0, abs(side)) * smoothstep(0.0, 0.2, down) * exp(-down * 0.9);
        colour += hsv2rgb(frac(0.83 + f * 0.19 + BeatGlide * 0.03), 0.85, 1.0) * beam * (0.14 + 0.24 * dfLoud(0.08 + 0.2 * f));
    }

    return colour * (0.94 + 0.07 * Level);
}
