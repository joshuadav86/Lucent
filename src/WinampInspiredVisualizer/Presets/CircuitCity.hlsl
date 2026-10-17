// Name: Circuit City
// Gliding high above a city laid out like a glowing circuit board, light trails racing down its streets and rings of light sweeping out on every beat.
// Family: Cyberpunk
// Tags: Neon, Geometric, Dark, 3D

static const float MajorEvery = 5.0;    // a wide avenue every this many blocks
static const float AvenueHalf = 0.32;   // half the width of an avenue
static const float StreetHalf = 0.07;   // half the width of an ordinary street

float circuitHash(float2 p)
{
    float3 q = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
    q += dot(q, q.yzx + 33.33);
    return frac((q.x + q.y) * q.z);
}

// How far a block's building is set back from its streets (x, z) and its height (w; 0 for an open plaza).
float4 circuitBlock(float2 cell)
{
    float seed = circuitHash(cell);
    float2 m = wrap(cell, MajorEvery);
    float2 avenue = (step(m, 0.5) + step(MajorEvery - 1.5, m)) * (AvenueHalf - StreetHalf);
    float tall = frac(seed * 7.3);
    float height = (0.15 + tall * tall * tall * 1.6) * step(0.22, frac(seed * 3.1));
    float2 inset = StreetHalf + 0.02 + frac(seed * float2(5.7, 9.3)) * 0.1;
    // Blocks next to an avenue are pushed back from it.
    return float4(inset + avenue, seed, height);
}

float circuitBox(float3 p, float3 size)
{
    float3 q = abs(p) - size;
    return length(max(q, 0.0)) + min(max(q.x, max(q.y, q.z)), 0.0);
}

// Distance to the city: one building per block, sitting on a flat ground at y = 0.
float circuitDE(float3 p, out float isBuilding)
{
    float2 cell = floor(p.xz);
    float2 within = p.xz - cell - 0.5;
    float4 block = circuitBlock(cell);
    float building = 1e3;
    if (block.w > 0.0)
    {
        building = circuitBox(float3(within.x, p.y - block.w * 0.5, within.y), float3(0.5 - block.x, block.w * 0.5, 0.5 - block.y));
    }
    // Don't step past the block's edge: the next building may be just beyond it.
    float edge = 0.5 - max(abs(within.x), abs(within.y));
    float d = min(building, max(edge, 0.0) + StreetHalf + 0.01);
    isBuilding = building < p.y ? 1.0 : 0.0;
    return min(d, p.y);
}

// The light running along the streets: glowing traces with packets of light racing along them.
float3 circuitStreets(float2 ground, float pixel, float2 waveCentre)
{
    float2 cell = floor(ground);
    float2 within = ground - cell - 0.5;
    // Distance to the nearest street centre line, along x and along z.
    float2 toStreet = 0.5 - abs(within);
    float2 avenueLine = wrap(ground, MajorEvery);
    avenueLine = min(avenueLine, MajorEvery - avenueLine);   // distance to the avenue centre lines
    float3 colour = 0.0;

    // Ordinary streets: one thin trace each, with an occasional packet.
    float streetLine = min(toStreet.x, toStreet.y);
    float trace = saturate(1.0 - (streetLine - 0.006) / max(pixel, 1e-4)) * saturate(0.012 / pixel + 0.2);
    float alongX = toStreet.y < toStreet.x ? 1.0 : 0.0;     // this street runs along x
    float streetId = alongX > 0.5 ? floor(ground.y + 0.5) : floor(ground.x + 0.5) + 100.0;
    float streetSeed = circuitHash(float2(streetId, 4.0));
    float position = alongX > 0.5 ? ground.x : ground.y;
    float direction = streetSeed > 0.5 ? 1.0 : -1.0;
    float packetPhase = frac(position * 0.12 * direction - Time * (0.25 + streetSeed * 0.3) + streetSeed * 7.0);
    float packet = pow(packetPhase, 30.0) + pow(packetPhase, 4.0) * 0.25;
    float3 streetColour = streetSeed > 0.75 ? float3(1.0, 0.2, 0.7) : float3(0.1, 0.75, 1.0);
    colour += streetColour * trace * (0.08 + packet * (0.8 + Treble * 1.5));

    // Avenues: four lanes of light trails each, bright and fast.
    [unroll]
    for (int axis = 0; axis < 2; axis++)
    {
        float across = axis == 0 ? avenueLine.y : avenueLine.x;     // axis 0 runs along x
        if (across < AvenueHalf)
        {
            float laneIndex = floor(across / (AvenueHalf * 0.5));
            float laneCentre = (laneIndex + 0.5) * AvenueHalf * 0.5;
            float laneLine = abs(across - laneCentre);
            float sideOfAvenue = axis == 0 ? sign(wrap(ground.y, MajorEvery) - MajorEvery * 0.5) : sign(wrap(ground.x, MajorEvery) - MajorEvery * 0.5);
            float avenueId = axis == 0 ? floor(ground.y / MajorEvery + 0.5) : floor(ground.x / MajorEvery + 0.5) + 50.0;
            float laneSeed = circuitHash(float2(avenueId * 3.0 + laneIndex, sideOfAvenue + 2.0));
            float flow = (sideOfAvenue > 0.0 ? 1.0 : -1.0);
            float along = axis == 0 ? ground.x : ground.y;
            float speed = 0.8 + laneSeed * 0.8 + Bass * 0.6;
            float phase = frac(along * 0.18 * flow - Time * speed * 0.18 + laneSeed * 13.0);
            float trail = pow(phase, 12.0) * 1.5 + pow(phase, 3.0) * 0.35;
            float width = saturate(1.0 - (laneLine - 0.012) / max(pixel, 1e-4)) * saturate(0.024 / pixel + 0.2);
            float3 laneColour = laneIndex < 0.5 ? float3(1.0, 0.85, 0.6) : (flow > 0.0 ? float3(0.1, 0.8, 1.0) : float3(1.0, 0.15, 0.5));
            float energy = spectrum(0.03 + laneSeed * 0.5);
            float halo = glow(laneLine, 1.0 / max(0.03, pixel * 2.0)) * 0.35;
            colour += laneColour * (width + halo) * (0.1 + trail * (1.2 + energy * 2.0));
            // Faint edges of the avenue.
            float kerb = saturate(1.0 - abs(across - AvenueHalf + 0.01) / max(pixel, 1e-4)) * 0.1;
            colour += float3(0.3, 0.4, 1.0) * kerb;
        }
    }

    // Vias: a dot of light where streets cross.
    float2 toCorner = abs(frac(ground) - 0.5) - 0.5;
    float via = glow(length(toCorner), 1.0 / max(0.012, pixel)) * 0.25;
    colour += float3(0.4, 0.8, 1.0) * via;

    // After each beat a ring of light sweeps outwards across the streets.
    float ring = glow(length(ground - waveCentre) - (1.0 - Beat) * 14.0, 1.5) * Beat;
    colour += float3(0.3, 0.7, 1.0) * ring * (trace * 2.5 + 0.6 * saturate(1.0 - streetLine / 0.2) + 0.08);
    return colour;
}

// A building: a dark chip with a neon rim, glowing panels on top and floors of windows down its sides.
float3 circuitBuilding(float3 hit, float pixel, float2 waveCentre)
{
    float2 cell = floor(hit.xz);
    float2 within = hit.xz - cell - 0.5;
    float4 block = circuitBlock(cell);
    float2 halfSize = 0.5 - block.xy;
    float2 fromEdge = halfSize - abs(within);
    bool roof = hit.y > block.w - 0.002 - pixel * 0.5;
    float seed = block.z;
    float energy = spectrum(0.03 + frac(seed * 5.1) * 0.6);
    float hue = frac(seed * 17.3);
    float3 neonColour = hue < 0.55 ? float3(0.1, 0.8, 1.0) : hue < 0.8 ? float3(1.0, 0.2, 0.65) : float3(0.6, 0.35, 1.0);
    float ring = glow(length(hit.xz - waveCentre) - (1.0 - Beat) * 14.0, 2.0) * Beat;

    if (roof)
    {
        float rim = saturate(1.0 - (min(fromEdge.x, fromEdge.y) - 0.012) / max(pixel * 1.2, 1e-4));
        // A grid of little lights on the roof, lit up by the music.
        float2 ledGrid = (within + halfSize) / 0.07;
        float2 led = floor(ledGrid);
        float2 inLed = abs(frac(ledGrid) - 0.5);
        float ledsOn = step(1.0 - energy * 0.9, circuitHash(led + seed * 31.0)) * step(0.6, frac(seed * 4.3));
        float fine = saturate(0.07 / max(pixel, 1e-4) / 3.0 - 0.5);
        float leds = lerp(0.25 * ledsOn, ledsOn * step(max(inLed.x, inLed.y), 0.28), fine) * step(0.05, min(fromEdge.x, fromEdge.y));
        // Some roofs carry a glowing core that flares on the beat.
        float core = glow(max(abs(within.x), abs(within.y)) - min(halfSize.x, halfSize.y) * 0.3, 1.0 / max(pixel * 2.0, 0.006))
                   * step(0.75, frac(seed * 2.3)) * (0.4 + Beat * 1.5);
        float rimStrength = frac(seed * 6.1) < 0.45 ? 0.15 : 0.5 + energy * 1.2;
        return float3(0.015, 0.02, 0.035) + neonColour * rim * (rimStrength + ring * 2.0) + neonColour * leds * 1.2 + lerp(neonColour, 1.0, 0.5) * core;
    }

    // Sides: dark glass with thin bands of light on each floor.
    bool facingX = fromEdge.x < fromEdge.y;
    float floors = abs(frac(hit.y / 0.08) - 0.5);
    float band = saturate(1.0 - (floors - 0.1) / max(pixel / 0.08, 1e-3)) * saturate(0.03 / pixel);
    float lit = step(0.45, circuitHash(float2(floor(hit.y / 0.08), seed * 9.0)));
    float corner = saturate(1.0 - (max(fromEdge.x, fromEdge.y) - 0.008) / max(pixel, 1e-4));
    float shade = facingX ? 0.6 : 1.0;
    return float3(0.01, 0.012, 0.025) * shade + neonColour * band * lit * 0.15 * shade + neonColour * corner * 0.3;
}

float3 Render(float2 uv, float2 p)
{
    float2 q = float2(p.x, -p.y);
    float travel = Time * 0.6 + (BeatCount - Beat) * 0.25;
    float heading = Time * 0.03;
    float2 forward2 = float2(sin(heading), cos(heading));
    float2 position = float2(0.37, 0.61) + forward2 * travel * 2.0;
    float3 eye = float3(position.x, 6.0 + sin(Time * 0.13) * 0.8, position.y);
    float3 target = float3(position.x + forward2.x * 3.2, 0.0, position.y + forward2.y * 3.2);
    float3 forward = normalize(target - eye);
    float3 right = normalize(cross(float3(0.0, 1.0, 0.0), forward));
    float3 up = cross(forward, right);
    q = rotate(q, sin(Time * 0.09) * 0.08);
    float3 dir = normalize(forward * (1.1 - Beat * 0.04) + q.x * right + q.y * up);
    float2 waveCentre = target.xz;

    // March down to the city.
    float travelled = 0.0;
    float isBuilding = 0.0;
    bool landed = false;
    // Start just above the tallest roofs.
    float start = dir.y < 0.0 ? max((eye.y - 1.9) / -dir.y, 0.0) : 0.0;
    travelled = start;
    int maxSteps = qualitySteps(80);
    const float farPlane = 45.0;
    [loop]
    for (int i = 0; i < maxSteps; i++)
    {
        float d = circuitDE(eye + dir * travelled, isBuilding);
        if (d < 0.0008 * travelled)
        {
            landed = true;
            break;
        }
        travelled += d;
        if (travelled > farPlane)
        {
            break;
        }
    }

    float3 haze = float3(0.03, 0.02, 0.08) * (0.7 + Bass * 0.5);
    float3 colour = haze;
    if (landed)
    {
        float3 hit = eye + dir * travelled;
        float pixel = travelled * pixelSize() / 1.1;
        if (isBuilding > 0.5)
        {
            colour = circuitBuilding(hit, pixel, waveCentre);
        }
        else
        {
            // The ground between the streets: dark board with a faint grid etched in.
            float3 board = float3(0.008, 0.012, 0.025);
            colour = board + circuitStreets(hit.xz, pixel / max(-dir.y, 0.3), waveCentre);
        }
        colour = lerp(haze, colour, exp(-max(travelled - 6.0, 0.0) * 0.13));
    }
    return colour;
}
