// Name: Neon Alley
// Wandering down a narrow back alley in the rain, past buzzing neon signs, paper lanterns that glow with the bass and steam that billows on the beat.
// Family: Cyberpunk
// Tags: Neon, Dark, Water, Calm, 3D

static const float AlleyHalf = 1.7;      // from the middle of the alley to each wall
static const float AlleyBlock = 3.4;     // signs, pipes and doors repeat along each wall every this far
static const float CableSpacing = 3.0;   // a string of lanterns crosses the alley every this far
static const float CableHeight = 4.4;

float alleyBox(float3 p, float3 size)
{
    float3 q = abs(p) - size;
    return length(max(q, 0.0)) + min(max(q.x, max(q.y, q.z)), 0.0);
}

// The sign on one stretch of wall: x = how far it sticks out, y = bottom, z = height, w = position along the stretch.
float4 alleySign(float seed)
{
    return float4(0.55 + frac(seed * 4.4) * 0.35, 2.3 + frac(seed * 5.9) * 1.2, 1.2 + frac(seed * 2.3) * 1.8, (frac(seed * 9.1) - 0.5) * 1.9);
}

// Which neon colour a stretch of wall uses: stepping by the golden ratio keeps neighbours different.
float alleyColourPick(float block, float side)
{
    return frac(block * 0.618034 + side * 0.27 + 0.3);
}

// Neon colours: hot pink, cyan, teal-green, violet and a warm amber.
float3 alleyNeon(float pick)
{
    pick = frac(pick);
    return pick < 0.3 ? float3(1.0, 0.1, 0.5) : pick < 0.6 ? float3(0.1, 0.85, 1.0) : pick < 0.72 ? float3(0.1, 1.0, 0.6) : pick < 0.88 ? float3(0.6, 0.25, 1.0) : float3(1.0, 0.6, 0.15);
}

// Height of a lantern string's cable at x across the alley, sagging in the middle.
float alleyCableY(float x)
{
    return CableHeight - 0.35 * (1.0 - x * x / (AlleyHalf * AlleyHalf));
}

// Distance to the alley. info: x = seed, y = side (-1 or 1), z = what was hit:
// 0 ground, 1 wall, 2 pipe, 3 air-conditioner box, 4 sign, 5 cable, 6 lantern.
float alleyDE(float3 p, out float3 info)
{
    float side = p.x >= 0.0 ? 1.0 : -1.0;
    float x = AlleyHalf - abs(p.x);   // distance in front of the wall
    float block = floor(p.z / AlleyBlock);
    float along = p.z - (block + 0.5) * AlleyBlock;
    float seed = hash(float2(block * 0.173, side * 0.37 + 0.5));

    // Walls, with a recessed doorway in some stretches.
    float wall = x;
    float doorway = alleyBox(float3(x + 0.4, p.y - 1.1, along - (frac(seed * 3.3) - 0.5) * 1.6), float3(0.4, 1.1, 0.55));
    wall = frac(seed * 7.7) > 0.45 ? max(wall, -doorway) : wall;
    float d = wall;
    info = float3(seed, side, 1.0);

    // A drainpipe running up the wall.
    float pipe = length(float2(x - 0.09, along - 1.45)) - 0.06;
    if (pipe < d) { d = pipe; info.z = 2.0; }

    // Air-conditioner boxes.
    float unit = alleyBox(float3(x - 0.28, p.y - 3.1 - frac(seed * 2.1) * 1.5, along + 1.0), float3(0.28, 0.3, 0.4));
    unit = frac(seed * 5.3) > 0.35 ? unit : 1e3;
    if (unit < d) { d = unit; info.z = 3.0; }

    // A blade sign sticking out over the alley.
    float4 sign4 = alleySign(seed);
    float board = alleyBox(float3(x - sign4.x * 0.5 - 0.05, p.y - sign4.y - sign4.z * 0.5, along - sign4.w), float3(sign4.x * 0.5, sign4.z * 0.5, 0.05));
    if (board < d) { d = board; info.z = 4.0; }

    // Strings of lanterns across the alley, swaying a little.
    float row = floor(p.z / CableSpacing);
    float rowZ = p.z - (row + 0.5) * CableSpacing;
    float sway = sin(Time * 0.9 + row * 1.7) * 0.05;
    float cable = length(float2(p.y - alleyCableY(p.x), rowZ - sway * 0.3)) - 0.012;
    if (cable < d) { d = cable; info = float3(row, side, 5.0); }
    float slot = clamp(floor(p.x / 0.85 + 0.5), -1.0, 1.0);
    float3 lanternCentre = float3(slot * 0.85 + sway, alleyCableY(slot * 0.85) - 0.38, (row + 0.5) * CableSpacing + sway * 0.4);
    float3 toLantern = p - lanternCentre;
    float lantern = length(toLantern * float3(1.0, 0.8, 1.0)) - 0.2;
    if (lantern < d) { d = lantern; info = float3(row + slot * 0.31, slot, 6.0); }

    // Don't step past the end of a stretch: the next one's sign or box may be just beyond it.
    float edge = max(AlleyBlock * 0.5 - abs(along), 0.0) + 0.15;
    d = x > 0.95 ? min(d, edge) : d;

    if (p.y < d)
    {
        d = p.y;
        info = float3(seed, side, 0.0);
    }
    return d;
}

float3 alleyNormal(float3 p)
{
    float3 ignore;
    float2 e = float2(0.002, -0.002);
    return normalize(e.xyy * alleyDE(p + e.xyy, ignore) + e.yyx * alleyDE(p + e.yyx, ignore)
                   + e.yxy * alleyDE(p + e.yxy, ignore) + e.xxx * alleyDE(p + e.xxx, ignore));
}

// How strongly a band of the music drives a sign or lantern, 0..1.
float alleyEnergy(float seed)
{
    return spectrum(0.04 + frac(seed * 5.1) * 0.55);
}

// Light falling on a point from the nearest signs and lanterns.
float3 alleyLight(float3 p, float3 normal)
{
    float3 light = 0.0;
    float block = floor(p.z / AlleyBlock);
    [unroll]
    for (int k = -1; k <= 1; k++)
    {
        [unroll]
        for (int s = 0; s < 2; s++)
        {
            float side = s == 0 ? -1.0 : 1.0;
            float seed = hash(float2((block + k) * 0.173, side * 0.37 + 0.5));
            float4 sign4 = alleySign(seed);
            float3 centre = float3(side * (AlleyHalf - sign4.x * 0.5), sign4.y + sign4.z * 0.5, (block + k + 0.5) * AlleyBlock + sign4.w);
            float3 toLight = centre - p;
            float distance2 = dot(toLight, toLight);
            float facing = saturate(dot(normal, toLight * rsqrt(distance2)) * 0.7 + 0.3);
            light += alleyNeon(alleyColourPick(block + k, side)) * facing / (0.2 + distance2 * 0.7) * (0.7 + alleyEnergy(seed) * 1.8);
        }
    }
    // The nearest string of lanterns.
    float row = floor(p.z / CableSpacing + 0.5) - 0.5;
    float3 lanternRow = float3(0.0, CableHeight - 0.7, (row + 0.5) * CableSpacing);
    float3 toRow = lanternRow - p;
    toRow.x *= 0.4;
    float rowDistance2 = dot(toRow, toRow);
    light += float3(1.0, 0.2, 0.06) * (0.15 + Bass * 0.45) / (1.0 + rowDistance2 * 0.9) * saturate(dot(normal, normalize(toRow)) * 0.6 + 0.4);
    return light;
}

// The colour of whatever the ray reached (without reflections).
float3 alleyShade(float3 hit, float3 dir, float3 info, float travelled)
{
    float pixel = travelled * pixelSize();
    float3 normal = alleyNormal(hit);
    float3 light = alleyLight(hit, normal);
    float seed = info.x;

    if (info.z > 5.5)
    {
        // A paper lantern: glowing red-orange, brighter on the bass, with darker ribs.
        float ribs = 0.75 + 0.25 * cos(hit.y * 60.0);
        float rim = pow(1.0 - saturate(dot(normal, -dir)), 2.0);
        return float3(1.0, 0.16, 0.04) * (0.45 + Bass * 0.9 + Beat * 0.3) * ribs * (1.0 - rim * 0.5) + float3(1.0, 0.45, 0.15) * (1.0 - rim) * 0.12;
    }
    if (info.z > 4.5)
    {
        return light * 0.05;   // cable
    }
    if (info.z > 3.5)
    {
        // A sign: glyphs inside a buzzing neon tube frame.
        float4 sign4 = alleySign(seed);
        float block = floor(hit.z / AlleyBlock);
        float x = AlleyHalf - abs(hit.x);
        float2 face = float2((x - 0.05) / sign4.x, (hit.y - sign4.y) / sign4.z);
        float2 fromEdge = min(face, 1.0 - face) * float2(sign4.x, sign4.z);
        float frame = saturate(1.0 - (min(fromEdge.x, fromEdge.y) - 0.035) / max(pixel * 1.5, 0.008));
        float rows = max(floor(sign4.z / sign4.x * 1.2), 1.0);
        float2 glyphGrid = float2((face.x - 0.15) / 0.7, (face.y - 0.08) / 0.84 * rows);
        float2 glyph = floor(glyphGrid);
        float inside = step(0.0, glyphGrid.x) * step(glyphGrid.x, 1.0) * step(0.0, glyphGrid.y) * step(glyphGrid.y, rows);
        float2 cell = frac(glyphGrid) * float2(5.0, 5.0);
        float2 inStroke = abs(frac(cell) - 0.5);
        float on = step(0.5, hash(floor(cell) + glyph * 7.0 + seed * 31.0)) * step(max(inStroke.x, inStroke.y), 0.4);
        float fine = saturate(sign4.x * 0.7 / 5.0 / max(pixel, 1e-4) - 1.0);
        float letters = lerp(0.45, on, fine) * inside;
        float energy = alleyEnergy(seed);
        // One sign in four has a faulty tube that stutters with the treble.
        float faulty = step(0.75, frac(seed * 3.9));
        float buzz = lerp(1.0, step(0.35, hash(float2(seed, floor(Time * 20.0)))) * (0.4 + Treble), faulty);
        float pick = alleyColourPick(block, info.y);
        float3 neonColour = alleyNeon(pick);
        return neonColour * 0.08 + lerp(neonColour, 1.0, 0.3) * frame * (1.0 + energy * 1.5) * buzz
             + alleyNeon(pick + 0.41) * letters * (0.6 + energy * 2.0);
    }

    // Grimy surfaces: bricks on the walls, a sheen of rain on everything.
    float3 albedo = float3(0.2, 0.19, 0.2);
    if (info.z > 0.5 && info.z < 1.5)
    {
        float2 wallUv = float2(hit.z, hit.y);
        float2 brick = wallUv / float2(0.3, 0.11);
        brick.x += floor(brick.y) * 0.5;
        float2 mortar = abs(frac(brick) - 0.5);
        float lines = saturate(max(smoothstep(0.44, 0.48, mortar.x), smoothstep(0.4, 0.47, mortar.y)) * saturate(0.03 / max(pixel, 1e-4) - 0.5));
        albedo = float3(0.18, 0.17, 0.19) * (0.6 + 0.8 * noise(wallUv * 3.0)) * (1.0 - lines * 0.7);
    }
    else if (info.z > 1.5)
    {
        albedo = float3(0.3, 0.3, 0.33);
    }
    float sheen = pow(1.0 - saturate(dot(normal, -dir)), 3.0) * 0.3;
    return albedo * (light * 1.6 + float3(0.02, 0.035, 0.05)) + light * sheen * 0.5;
}

float3 alleySky(float3 dir)
{
    float glowFar = exp(-abs(dir.x) * 3.0) * exp(-max(dir.y, 0.0) * 1.5);
    return lerp(float3(0.02, 0.01, 0.04), float3(0.25, 0.08, 0.3), glowFar) * (0.8 + Level * 0.4);
}

// March a ray through the alley; returns the distance travelled (beyond far when it escaped).
float alleyMarch(float3 origin, float3 dir, int steps, float far, out float3 info)
{
    float travelled = 0.0;
    info = 0.0;
    [loop]
    for (int i = 0; i < steps; i++)
    {
        float d = alleyDE(origin + dir * travelled, info);
        if (d < 0.001 * (1.0 + travelled))
        {
            return travelled;
        }
        travelled += d * 0.9;
        if (travelled > far)
        {
            break;
        }
    }
    return far + 1.0;
}

float3 Render(float2 uv, float2 p)
{
    float2 q = float2(p.x, -p.y);
    float z = Time * 0.75 + (BeatCount - Beat) * 0.12;
    float3 eye = float3(sin(Time * 0.15) * 0.35, 1.65 + sin(Time * 1.6) * 0.02, z);
    float3 forward = normalize(float3(sin(Time * 0.11) * 0.15, 0.16 + sin(Time * 0.07) * 0.06, 1.0));
    float3 right = normalize(cross(float3(0.0, 1.0, 0.0), forward));
    float3 up = cross(forward, right);
    float3 dir = normalize(forward * 0.8 + q.x * right + q.y * up);

    const float farPlane = 40.0;
    float3 info;
    float travelled = alleyMarch(eye, dir, qualitySteps(100), farPlane, info);
    float3 fogColour = float3(0.07, 0.03, 0.09);
    float3 colour = alleySky(dir);
    float hitDistance = min(travelled, farPlane);
    if (travelled < farPlane)
    {
        float3 hit = eye + dir * travelled;
        if (info.z > 0.5)
        {
            colour = alleyShade(hit, dir, info, travelled);
        }
        else
        {
            // Wet cobbles: puddles mirror the signs and lanterns, rippled by the rain.
            float puddle = smoothstep(0.42, 0.6, fbm(hit.xz * 0.7 + 4.0));
            float2 ripple = (float2(noise(hit.xz * 6.0 + Time * 2.0), noise(hit.zx * 6.0 - Time * 2.3)) - 0.5) * lerp(0.07, 0.015, puddle);
            float3 mirrored = normalize(float3(dir.x + ripple.x, -dir.y, dir.z + ripple.y));
            float3 start = hit + float3(0.0, 0.005, 0.0);
            float3 reflectInfo;
            float reflected = alleyMarch(start, mirrored, qualitySteps(50), farPlane * 0.6, reflectInfo);
            float3 mirror = reflected < farPlane * 0.6 && reflectInfo.z > 0.5
                ? alleyShade(start + mirrored * reflected, mirrored, reflectInfo, travelled + reflected)
                : alleySky(mirrored);
            mirror = lerp(fogColour, mirror, exp(-reflected * 0.06));
            float fresnel = lerp(0.3, 0.9, pow(1.0 - saturate(-dir.y), 5.0));
            float2 cobble = abs(frac(hit.xz * float2(3.0, 2.2)) - 0.5);
            float3 stone = float3(0.12, 0.11, 0.12) * (0.6 + 0.8 * hash(floor(hit.xz * float2(3.0, 2.2))))
                         * (1.0 - smoothstep(0.38, 0.48, max(cobble.x, cobble.y)) * 0.6);
            float3 light = alleyLight(hit, float3(0.0, 1.0, 0.0));
            colour = stone * (light * 1.6 + float3(0.02, 0.035, 0.05)) + mirror * fresnel * lerp(0.2, 1.0, puddle);
        }
        colour = lerp(fogColour, colour, exp(-travelled * 0.05));
    }

    // Steam: sheets of mist at fixed spots along the alley, rising from the ground and lit by the neon.
    float sheetSpacing = 1.9;
    float firstSheet = floor(eye.z / sheetSpacing) + 1.0;
    float3 steam = 0.0;
    float clear = 1.0;
    [unroll]
    for (int k = 0; k < 6; k++)
    {
        float sheetZ = (firstSheet + k) * sheetSpacing;
        float along = (sheetZ - eye.z) / dir.z;
        if (along < hitDistance && along > 0.0)
        {
            float3 at = eye + dir * along;
            float sheetSeed = hash(float2(firstSheet + k, 3.7));
            // Vents near the walls breathe out plumes; low mist hangs everywhere.
            float ventX = (sheetSeed > 0.5 ? 1.0 : -1.0) * (AlleyHalf - 0.3 - frac(sheetSeed * 7.0) * 0.5);
            float plumeWidth = 0.25 + at.y * 0.3;
            float plume = exp(-pow((at.x - ventX - sin(at.y * 1.3 + Time) * 0.2) / plumeWidth, 2.0)) * exp(-at.y * 0.35) * step(0.35, frac(sheetSeed * 3.1));
            float mist = exp(-at.y * 1.4) * 0.6;
            float cloud = fbm(float2(at.x * 1.3 + sheetSeed * 10.0, at.y * 0.9 - Time * 0.45) + float2(Time * 0.05, 0.0));
            float density = saturate((plume * (1.0 + Beat * 1.2 + Bass * 0.8) + mist) * smoothstep(0.25, 0.7, cloud) * 1.1);
            // Thin out against the walls and right in front of the camera, so no sheet shows an edge.
            density *= smoothstep(0.0, 0.5, AlleyHalf - abs(at.x)) * smoothstep(0.4, 1.6, along) * smoothstep(0.0, 0.6, hitDistance - along);
            float3 litBy = alleyLight(at, float3(0.0, 0.0, -1.0)) * 0.8 + 0.04;
            steam += clear * density * litBy;
            clear *= 1.0 - density;
        }
    }
    colour = colour * clear + steam;

    // Rain, lit pink and blue by the signs.
    float3 rain = 0.0;
    [unroll]
    for (int layer = 0; layer < 2; layer++)
    {
        float scale = 30.0 + layer * 26.0;
        float2 r = float2(q.x + q.y * 0.05, q.y) * scale;
        float column = floor(r.x);
        float offset = hash(float2(column, layer * 5.3));
        float fall = r.y * 0.06 + Time * (1.8 + layer * 0.5) * (1.0 + offset * 0.3) + offset * 11.0;
        float streak = smoothstep(0.25, 0.0, frac(fall)) * smoothstep(0.0, 0.03, frac(fall));
        float thin = saturate(1.0 - abs(frac(r.x) - 0.25 - offset * 0.5) * (7.0 + layer * 3.0));
        rain += thin * streak * step(0.6, hash(float2(column, floor(fall) + layer))) * (0.35 - layer * 0.12);
    }
    colour += rain * float3(0.75, 0.7, 1.0) * (0.4 + Treble * 0.8);
    return colour;
}
