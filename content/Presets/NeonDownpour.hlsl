// Name: Neon Downpour
// Gliding low down a rain-soaked megacity avenue at night, towers of glowing windows and neon signs pulsing to the music and mirrored in the wet street.
// Family: Cyberpunk
// Tags: Neon, Dark, Water, 3D
// Added: 2026-10-17 01:16

static const float StreetHalf = 3.6;    // from the middle of the avenue to the building fronts
static const float BlockLength = 6.0;   // one building per block along each side

float downpourBox(float3 p, float3 size)
{
    float3 q = abs(p) - size;
    return length(max(q, 0.0)) + min(max(q.x, max(q.y, q.z)), 0.0);
}

// The building in one block: x = setback from the street, y = height, z = half its length,
// w = 1 if it has a neon sign hanging over the street.
float4 downpourBuilding(float seed)
{
    float tall = frac(seed * 13.1);
    return float4(frac(seed * 7.3) * 0.7, 7.0 + 34.0 * tall * tall, BlockLength * 0.5 - 0.3 - frac(seed * 3.7) * 0.5, step(0.25, frac(seed * 6.6)));
}

// The block's sign: x = how far it sticks out, y = bottom, z = height, w = position along the block.
float4 downpourSign(float seed, float4 building)
{
    float reach = 0.6 + frac(seed * 4.4) * 0.5;
    float bottom = 2.2 + frac(seed * 5.9) * 3.5;
    float height = 1.6 + frac(seed * 2.3) * 4.0;
    float along = (frac(seed * 9.1) - 0.5) * (building.z * 2.0 - 0.8);
    return float4(reach, bottom, height, along);
}

// Distance to the city. info: x = block seed, y = which side (-1 or 1), z = 0 street, 1 building, 2 sign.
// signDistance is the distance to the nearest sign, for the glow it casts into the rain.
float downpourCity(float3 p, out float3 info, out float signDistance)
{
    float side = p.x >= 0.0 ? 1.0 : -1.0;
    float x = abs(p.x) - StreetHalf;
    float cell = floor(p.z / BlockLength);
    float along = p.z - (cell + 0.5) * BlockLength;
    float seed = hash(float2(cell * 0.137, side * 0.71 + 0.3));
    float4 building = downpourBuilding(seed);
    float4 signShape = downpourSign(seed, building);

    float body = downpourBox(float3(x - building.x - 12.0, p.y - building.y * 0.5, along), float3(12.0, building.y * 0.5, building.z));
    float board = downpourBox(float3(x - building.x + signShape.x * 0.5 + 0.08, p.y - signShape.y - signShape.z * 0.5, along - signShape.w),
                              float3(signShape.x * 0.5, signShape.z * 0.5, 0.05));
    board = building.w > 0.5 ? board : 1e3;
    signDistance = board;

    // Never step past the block's end: the next building or sign may be just beyond it.
    float edge = max(BlockLength * 0.5 - abs(along), 0.0) + 0.2;
    float solid = min(body, board);
    info = float3(seed, side, body < board ? 1.0 : 2.0);
    float d = min(solid, edge);
    if (p.y < d)
    {
        info.z = 0.0;
        d = p.y;
    }
    return d;
}

// The neon colour of a block: magenta, cyan, violet or amber.
float3 downpourNeon(float seed)
{
    float pick = frac(seed * 17.3);
    float3 colour = pick < 0.35 ? float3(1.0, 0.1, 0.55) : pick < 0.65 ? float3(0.1, 0.85, 1.0) : pick < 0.85 ? float3(0.55, 0.2, 1.0) : float3(1.0, 0.55, 0.1);
    return colour;
}

// The glow of a surface the ray reached: windows, neon trim and signs. Buildings only light themselves.
float3 downpourShade(float3 hit, float3 info, float travelled)
{
    float pixel = travelled * pixelSize();
    float seed = info.x;
    float side = info.y;
    float x = abs(hit.x) - StreetHalf;
    float cell = floor(hit.z / BlockLength);
    float along = hit.z - (cell + 0.5) * BlockLength;
    float4 building = downpourBuilding(seed);
    float3 neonColour = downpourNeon(seed);
    float energy = spectrum(0.05 + frac(seed * 5.1) * 0.6);

    if (info.z > 1.5)
    {
        // A sign: rows of glowing glyphs inside a neon tube frame, flickering with its band of the music.
        float4 signShape = downpourSign(seed, building);
        float2 face = float2((building.x - x) / signShape.x, (hit.y - signShape.y) / signShape.z);   // 0..1 across and up
        float2 fromEdge = min(face, 1.0 - face) * float2(signShape.x, signShape.z);
        float frame = saturate(1.0 - (min(fromEdge.x, fromEdge.y) - 0.05) / max(pixel * 1.5, 0.02));
        float columns = 2.0;
        float rows = floor(signShape.z / signShape.x * columns * 0.8);
        float2 glyphGrid = float2((face.x - 0.12) / 0.76 * columns, (face.y - 0.06) / 0.88 * rows);
        float2 glyph = floor(glyphGrid);
        float inside = step(0.0, glyphGrid.x) * step(glyphGrid.x, columns) * step(0.0, glyphGrid.y) * step(glyphGrid.y, rows);
        // Each glyph is a 4 x 5 block of strokes, chosen at random.
        float2 stroke = floor(frac(glyphGrid) * float2(4.0, 5.0));
        float2 inStroke = abs(frac(frac(glyphGrid) * float2(4.0, 5.0)) - 0.5);
        float on = step(0.45, hash(stroke + glyph * 7.0 + seed * 31.0)) * step(max(inStroke.x, inStroke.y), 0.42);
        float fine = saturate(signShape.x / columns / 4.0 / max(pixel, 1e-4) - 1.0);
        float letters = lerp(0.45, on, fine) * inside;
        float flicker = step(0.08, hash(float2(seed, floor(Time * 12.0)))) * 0.3 + 0.7;
        float3 tubeColour = lerp(neonColour, 1.0, 0.35);
        float3 letterColour = downpourNeon(seed + 0.37);
        return neonColour * 0.06 + tubeColour * frame * (1.2 + energy * 1.6) + letterColour * letters * (0.7 + energy * 2.2) * flicker;
    }

    if (info.z > 0.5)
    {
        // A building: the face nearest the hit decides how windows are laid out.
        float front = abs(x - building.x);
        float end = building.z - abs(along);
        float2 facade = front < end ? float2(along, hit.y) : float2(x, hit.y);
        if (min(front, end) > building.y - hit.y)
        {
            return 0.0;   // roof
        }
        float2 windowGrid = facade / float2(0.26, 0.4);
        float2 pane = floor(windowGrid);
        float2 inPane = abs(frac(windowGrid) - 0.5);
        float lit = step(0.5 + frac(seed * 2.9) * 0.35, hash(pane + seed * 53.0));
        float glass = step(inPane.x, 0.32) * step(inPane.y, 0.28);
        float fine = saturate(0.26 / max(pixel, 1e-4) / 3.0 - 1.0);
        float windows = lerp(0.12, glass * lit, fine);
        float tint = hash(pane * 1.7 + seed);
        float3 windowColour = tint < 0.6 ? float3(1.0, 0.62, 0.32) : tint < 0.8 ? float3(0.45, 0.75, 1.0) : neonColour;
        // Some floors flash with the music.
        float pulse = step(0.85, hash(float2(pane.y, seed))) * energy * 2.0;
        // Neon trim along a few floors, and up the building's corner.
        float trimFloor = abs(frac(hit.y / 7.4 + seed) - 0.5) * 7.4;
        float trim = saturate(1.0 - (trimFloor - 0.04) / max(pixel * 1.5, 0.01)) * step(0.4, frac(seed * 8.3));
        float corner = saturate(1.0 - (max(front, end) - 0.03) / max(pixel * 1.5, 0.01)) * step(0.5, frac(seed * 3.3));
        return float3(0.012, 0.012, 0.025) + windowColour * windows * (0.25 + pulse) * 0.8
             + neonColour * (trim + corner) * (0.8 + energy * 1.5);
    }
    return 0.0;
}

// The sky between the towers: low smog lit pink and violet from below.
float3 downpourSky(float3 dir)
{
    float smog = fbm(dir.xz / max(dir.y, 0.05) * 0.8 + float2(0.0, Time * 0.05));
    float low = exp(-max(dir.y, 0.0) * 4.0);
    return lerp(float3(0.03, 0.01, 0.07), float3(0.35, 0.06, 0.3), low) * (0.6 + smog * 0.8);
}

float3 Render(float2 uv, float2 p)
{
    float2 q = float2(p.x, -p.y);
    float z = Time * 3.2 + (BeatCount - Beat) * 0.9;
    float3 eye = float3(sin(Time * 0.23) * 0.9, 2.3 + sin(Time * 0.31) * 0.5, z);
    float3 forward = normalize(float3(sin(Time * 0.17) * 0.12, 0.06 + sin(Time * 0.13) * 0.05, 1.0));
    float3 right = normalize(cross(float3(0.0, 1.0, 0.0), forward));
    float3 up = cross(forward, right);
    float roll = sin(Time * 0.19) * 0.06;
    q = rotate(q, roll);
    float3 dir = normalize(forward * (1.1 - Beat * 0.05) + q.x * right + q.y * up);

    const float farPlane = 70.0;
    int maxSteps = qualitySteps(90);
    float travelled = 0.0;
    float3 info = 0.0;
    float signDistance = 1e3;
    float3 haze = 0.0;
    bool landed = false;
    [loop]
    for (int i = 0; i < maxSteps; i++)
    {
        float3 position = eye + dir * travelled;
        float d = downpourCity(position, info, signDistance);
        // Light from the signs scatters in the rain around them.
        haze += downpourNeon(info.x) * exp(-signDistance * 2.5) * min(d, 1.0) * 0.06;
        if (d < 0.0015 * travelled)
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

    float3 fogColour = float3(0.06, 0.03, 0.11) * (0.8 + Bass * 0.5);
    float3 colour = downpourSky(dir);
    if (landed)
    {
        float3 hit = eye + dir * travelled;
        if (info.z > 0.5)
        {
            colour = downpourShade(hit, info, travelled);
        }
        else
        {
            // The wet street: ripples from the rain break up a mirror image of the city.
            float2 rippleUv = hit.xz * 3.0;
            float2 ripple = float2(noise(rippleUv + Time * 1.5), noise(rippleUv.yx * 1.3 - Time * 1.7)) - 0.5;
            float puddle = smoothstep(0.35, 0.65, fbm(hit.xz * 0.35));
            float3 mirrored = normalize(float3(dir.x + ripple.x * (0.06 - puddle * 0.05), -dir.y, dir.z + ripple.y * (0.06 - puddle * 0.05)));
            float reflected = 0.0;
            float3 reflectInfo = 0.0;
            float ignore = 0.0;
            bool reflectHit = false;
            float3 start = hit + float3(0.0, 0.01, 0.0);
            int reflectSteps = qualitySteps(48);
            [loop]
            for (int j = 0; j < reflectSteps; j++)
            {
                float d = downpourCity(start + mirrored * reflected, reflectInfo, ignore);
                if (d < 0.003 * (reflected + travelled))
                {
                    reflectHit = reflectInfo.z > 0.5;
                    break;
                }
                reflected += d;
                if (reflected > farPlane * 0.6)
                {
                    break;
                }
            }
            float3 mirror = reflectHit ? downpourShade(start + mirrored * reflected, reflectInfo, travelled + reflected) : downpourSky(mirrored) * 0.55;
            mirror = lerp(fogColour, mirror, exp(-reflected * 0.035));
            float fresnel = lerp(0.45, 0.95, pow(1.0 - saturate(-dir.y), 4.0));
            float3 tarmac = float3(0.01, 0.01, 0.015) * (1.0 + noise(hit.xz * 8.0));
            // Lane markings, worn and dim.
            float lane = step(abs(abs(hit.x) - 1.2), 0.04) * step(0.5, frac(hit.z / 3.0));
            colour = tarmac + mirror * fresnel * lerp(0.55, 1.0, puddle) + float3(0.5, 0.5, 0.45) * lane * 0.15;
        }
        colour = lerp(fogColour, colour, exp(-travelled * 0.03));
    }
    colour += haze * (0.8 + Mid * 0.8);

    // Rain: three layers of streaks falling past the camera, lit by the city.
    float3 rain = 0.0;
    [unroll]
    for (int layer = 0; layer < 3; layer++)
    {
        float scale = 26.0 + layer * 22.0;
        float2 r = float2(q.x + q.y * 0.12, q.y) * scale;
        float column = floor(r.x);
        float offset = hash(float2(column, layer * 3.1));
        float fall = r.y * 0.07 + Time * (2.2 + layer * 0.6) * (1.0 + offset * 0.3) + offset * 13.0;
        float streak = smoothstep(0.25, 0.0, frac(fall)) * smoothstep(0.0, 0.03, frac(fall));
        float across = abs(frac(r.x) - 0.25 - offset * 0.5);
        float thin = saturate(1.0 - across * (6.0 + layer * 2.0));
        rain += thin * streak * step(0.55, hash(float2(column, floor(fall) + layer))) * (0.55 - layer * 0.13);
    }
    colour += rain * float3(0.7, 0.75, 1.0) * (0.5 + Treble * 1.2) * (0.6 + length(haze) * 3.0);

    // A brief flash of lightning-white haze on strong beats.
    colour += fogColour * smoothstep(0.7, 1.0, Beat) * 1.5;
    return colour;
}
