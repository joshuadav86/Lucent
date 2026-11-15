// Name: Fable Bismuth
// Bismuth crystals seen from above: square staircases spiralling down into themselves, crowded together at every size,
// their metal tarnished into bands of gold, magenta, blue and green. The stairs creep round as the crystals grow, each
// crystal shines with its own band of the spectrum, and a step of light walks down every staircase on the beat.
// Family: Geometric
// Tags: Geometric, Colourful, 3D
// Added: 2026-11-15 17:45

// A bismuth crystal grows faster along its edges than across its faces, so each face is left behind as a
// staircase winding down into a pit: a "hopper". Here the picture is cut into squares, some of them cut again
// into four, and each square holds one hopper. Its stairs are a square spiral: the step number is how far out
// a point is (as a square measures it) less how far round, so going once round climbs exactly one step.
static const float Biggest = 0.92;             // the side of the largest crystals
static const float StepsPerUnit = 15.0;        // stairs per unit of distance

float3 fbOxide(float t)
{
    // The colours bismuth's oxide skin goes as it thickens: gold, rose, violet, blue, green.
    float3 hue = 0.5 + 0.5 * cos(2.0 * PI * (t + float3(0.02, 0.36, 0.62)));
    return lerp(dot(hue, 0.3333), hue, 0.9) * float3(1.0, 0.93, 0.98);
}

float3 Render(float2 uv, float2 p)
{
    float2 q = rotate(p, 0.26 + sin(Time * 0.021) * 0.08) * (1.0 + 0.06 * sin(Time * 0.05));
    q += float2(Time * 0.008, Time * 0.005) + 5.0;

    // Cut into squares, and some of those into four, and some of those again.
    float side = Biggest;
    float2 cell = floor(q / side);
    for (int depth = 0; depth < 2; depth++)
    {
        if (hash(cell * 0.37 + side * 11.3) > 0.52)
        {
            break;
        }

        side *= 0.5;
        cell = floor(q / side);
    }

    float own = hash(cell * 0.173 + side * 5.1);
    float2 local = (q - (cell + 0.5) * side) / (side * 0.5);       // -1..1 across the crystal
    float2 size = abs(local);
    float far = max(size.x, size.y);                              // 0 at the pit, 1 at the rim
    float around = (dot(local, local) > 1e-9 ? atan2(local.y, local.x) : 0.0) / (2.0 * PI);

    // The staircase. It creeps round as the crystal grows, with the music's help.
    float stairs = floor(side * 0.5 * StepsPerUnit + 0.5);
    float handed = own < 0.5 ? -1.0 : 1.0;
    float climb = far * stairs - around * handed + own * 7.0 + (Time * 0.06 + BeatGlide * 0.05) * (0.6 + own);
    float stair = floor(climb);
    float across = climb - stair;                                 // 0..1 across one tread, from its inner edge

    // Which way this side of the crystal faces: its risers face the pit.
    float2 facing = size.x > size.y ? float2(local.x < 0.0 ? 1.0 : -1.0, 0.0) : float2(0.0, local.y < 0.0 ? 1.0 : -1.0);
    float2 lightFrom = normalize(float2(-0.6, -0.8));
    float lit = dot(facing, -lightFrom);                          // -1..1: is this riser turned to the light

    // Each tread has its own thickness of oxide, so its own colour; the colours drift slowly.
    // (Counting the part of a turn as well keeps the colour running smoothly round the spiral.)
    float tint = (stair + around * handed) * 0.05 + own * 3.0 + Time * 0.012 + far * 0.08;
    float3 oxide = fbOxide(tint);
    // Brushed metal: fine streaks running along the tread.
    float2 grainAt = size.x > size.y ? float2(local.x * 90.0, local.y * 3.0) : float2(local.y * 90.0, local.x * 3.0);
    float grain = noise(grainAt * side + own * 50.0);
    float band = spectrum(0.02 + own * 0.72);
    float run = far - BeatPhase * 1.15;
    float walk = exp(-run * run * 40.0) * (SongMapped > 0.0 ? 0.35 + Beat * 0.65 : Beat);

    // Treads are darker the deeper into the pit they are; risers are a bright or a dark line.
    float tread = (0.2 + 0.5 * far) * (0.75 + 0.5 * grain);
    float pixel = pixelSize() / (side * 0.5) * stairs;            // one pixel, measured in treads
    float riser = smoothstep(0.16 + pixel, 0.16, across);
    float shadow = smoothstep(0.5, 0.16, across) * 0.35;         // the riser's shadow across the tread below
    // The lamp's reflection is a bright bar that lands on a different tread of every crystal.
    float2 lamp = float2(sin(Time * 0.09) * Aspect * 0.6, cos(Time * 0.071) * 0.5);
    float bar = dot(p - lamp, lightFrom) + (far - 0.5) * side * 0.6;
    float sheen = exp(-bar * bar * 14.0);
    float3 colour = oxide * tread * (0.5 + band * 0.9 + walk * 0.8 + DropPulse * 0.3);
    colour += lerp(oxide, float3(1.0, 0.97, 0.9), 0.5) * sheen * (0.12 + 0.3 * grain) * (0.4 + 0.6 * far);
    colour *= 1.0 - shadow * saturate(-lit + 0.4);
    float3 edge = lerp(oxide, float3(1.0, 0.97, 0.9), 0.55) * (0.5 + band * 0.8 + walk * 1.0);
    colour = lerp(colour, lit > 0.0 ? edge * (0.55 + 0.6 * lit) : oxide * 0.05, riser);

    // The crystal's rim stands above its neighbours, and the gaps between crystals are dark.
    float rim = smoothstep(0.93, 0.955, far);
    colour = lerp(colour, lerp(oxide, float3(1.0, 0.97, 0.9), 0.4) * (0.5 + band * 0.7), rim * smoothstep(1.0, 0.975, far) * 0.8);
    colour *= smoothstep(1.0, 0.985, far);

    // A broad soft light drifting over the whole cluster.
    colour *= 0.7 + 0.5 * exp(-dot(p - lamp, p - lamp) * 1.2);
    return colour * (0.9 + Intensity * 0.3) * (1.0 - dot(uv - 0.5, uv - 0.5) * 0.8);
}
