// Name: Frequency Terrain
// Gliding over liquid hills sculpted from the last few seconds of the spectrum, with rainbow contours flowing up their slopes.

// The spectrum's history lives in a 48x30 pixel block in the top-left corner of the feedback
// buffer (bands across, one row per moment), stored far too dim to see.
static const float TerrainBands = 48.0;
static const float TerrainRows = 30.0;
static const float TerrainRate = 6.25;     // history rows per second
static const float TerrainStore = 0.004;
static const float TerrainMarker = 0.0023;
static const float TerrainSpacing = 0.272;  // world distance between history rows
static const float TerrainWidth = 4.5;      // world width from the bass ridge to the treble edge

// The live spectrum, smoothed across neighbouring bands so the hills roll instead of bristle.
float terrainLive(float band)
{
    float x = band * 0.86 + 0.04;
    const float d = 1.0 / 110.0;
    float s = spectrum(x) * 0.2
            + (spectrum(x - d) + spectrum(x + d)) * 0.17
            + (spectrum(x - 2.0 * d) + spectrum(x + 2.0 * d)) * 0.12
            + (spectrum(x - 3.0 * d) + spectrum(x + 3.0 * d)) * 0.07
            + (spectrum(x - 4.0 * d) + spectrum(x + 4.0 * d)) * 0.04;
    return saturate((s - 0.1) / 0.9);
}

// Stored history at world position (x, z): r = level, b = beat strength when it was written.
float2 terrainSample(float2 xz, float scroll)
{
    // Mirrored and repeated sideways: bass ridges down the middle, treble valleys between them.
    float band = abs(frac(xz.x / (2.0 * TerrainWidth) + 0.5) * 2.0 - 1.0);
    float cellX = clamp(band * TerrainBands - 0.5, 0.0, TerrainBands - 1.0);
    float row = clamp(xz.y / TerrainSpacing - scroll, 0.0, TerrainRows - 1.0);
    float3 stored = previous(float2(cellX + 0.5, row + 0.5) / Resolution);
    bool valid = abs(stored.g - TerrainMarker) < TerrainMarker * 0.03;
    return valid ? stored.rb / TerrainStore : float2(0.0, 0.0);
}

float terrainHeight(float2 xz, float scroll)
{
    float level = terrainSample(xz, scroll).x;
    float shaped = smoothstep(0.12, 1.0, level);
    // Beyond the remembered history the land settles into a calm plain.
    float remembered = smoothstep(TerrainRows * TerrainSpacing, (TerrainRows - 14.0) * TerrainSpacing, xz.y);
    return shaped * shaped * 1.15 * remembered;
}

// Flowing psychedelic colour: amber, magenta, violet, electric blue and olive gold, drifting over time.
float3 terrainPalette(float t)
{
    float3 phase = float3(0.0, 0.2, 0.55) + float3(0.0, 0.06, 0.03) * sin(Time * float3(0.05, 0.031, 0.043));
    return saturate(float3(0.6, 0.35, 0.55) + float3(0.4, 0.35, 0.45) * cos(2.0 * PI * (t + phase)));
}

float3 Render(float2 uv, float2 p)
{
    float scroll = frac(Time * TerrainRate);

    // Memory pixels: shift the history back by one row whenever a new row is due.
    float2 pixel = floor(uv * Resolution);
    if (pixel.x < TerrainBands && pixel.y < TerrainRows)
    {
        float shift = floor(Time * TerrainRate) - floor((Time - DeltaTime) * TerrainRate);
        float live = terrainLive((pixel.x + 0.5) / TerrainBands);
        float sourceRow = pixel.y - shift;
        float value = live;
        float beat = Beat;
        if (sourceRow >= 0.0)
        {
            float3 stored = previous(float2(pixel.x + 0.5, sourceRow + 0.5) / Resolution);
            bool valid = abs(stored.g - TerrainMarker) < TerrainMarker * 0.03;
            value = valid ? stored.r / TerrainStore : live;
            beat = valid ? stored.b / TerrainStore : 0.0;
            if (pixel.y < 0.5)
            {
                value = max(value, live);    // the newest row keeps the loudest moment until it moves on
                beat = max(beat, Beat);
            }
        }
        return float3(value * TerrainStore, TerrainMarker, beat * TerrainStore);
    }

    // Camera: above the newest ridge, looking out across the history, drifting and banking.
    float sway = sin(Time * 0.11) * 1.8;
    float3 eye = float3(sway, 1.75 + 0.3 * sin(Time * 0.17), -1.0);
    float3 target = float3(sway * 0.5 + sin(Time * 0.07) * 1.0, 0.1, 5.0);
    float3 direction = cameraRay(eye, target, float2(p.x, -p.y), 1.3, sin(Time * 0.09) * 0.12);

    float px = 2.0 / Resolution.y / 1.3;
    const float farPlane = 28.0;
    const float ceiling = 1.2;
    float hit = 0.0;
    float travelled = direction.y < 0.0 ? max((eye.y - ceiling) / -direction.y, 0.0) : farPlane + 1.0;
    float exitDistance = direction.y < 0.0 ? eye.y / -direction.y : farPlane;
    float stepTaken = 0.0;
    float gap = 1.0;
    int steps = qualitySteps(64);
    [loop]
    for (int i = 0; i < steps; i++)
    {
        if (travelled > min(exitDistance, farPlane))
        {
            break;
        }
        float3 position = eye + direction * travelled;
        gap = position.y - terrainHeight(position.xz, scroll);
        if (gap < 0.0015 * travelled)
        {
            hit = 1.0;
            break;
        }
        stepTaken = max(gap * 0.6, 0.01 + travelled * 0.016);
        travelled += stepTaken;
    }
    // A ray that ran out of steps just above the surface still counts as touching it.
    if (hit < 0.5 && gap < 0.05 * travelled && travelled < min(exitDistance, farPlane))
    {
        hit = 0.5;
    }

    // Refine the crossing for crisp silhouettes.
    if (hit > 0.75)
    {
        float lo = travelled - stepTaken;
        float hi = travelled;
        [unroll]
        for (int k = 0; k < 5; k++)
        {
            float mid = (lo + hi) * 0.5;
            float3 at = eye + direction * mid;
            if (at.y - terrainHeight(at.xz, scroll) < 0.0)
            {
                hi = mid;
            }
            else
            {
                lo = mid;
            }
        }
        travelled = hi;
    }

    // Sky: black overhead, a soft glow along the horizon in the landscape's flowing colours.
    float flow = Time * 0.06 + Bass * 0.1;
    float3 haze = lerp(float3(0.05, 0.03, 0.22), terrainPalette(flow + 0.55) * 0.2, 0.35) * (0.6 + Level * 0.6);
    float3 colour = haze * exp(-max(direction.y, 0.0) * 22.0);

    if (hit > 0.0)
    {
        float3 position = eye + direction * travelled;
        float2 here = terrainSample(position.xz, scroll);
        float h = terrainHeight(position.xz, scroll);
        const float e = 0.12;   // wide differences smooth over the facets between history rows
        float hx = terrainHeight(position.xz + float2(e, 0.0), scroll);
        float hz = terrainHeight(position.xz + float2(0.0, e), scroll);
        float3 normal = normalize(float3(h - hx, e, h - hz));
        float slope = length(float2(h - hx, h - hz)) / e;

        float3 light = normalize(float3(-0.35, 0.55, 0.75));
        float diffuse = saturate(dot(normal, light));
        float3 halfway = normalize(light - direction);
        float specular = pow(saturate(dot(normal, halfway)), 40.0);
        float rim = pow(1.0 - saturate(dot(normal, -direction)), 4.0);
        float heightMix = saturate(h / 1.1);

        // Colour bands that climb the slopes over time, with fine bright contour lines between them.
        float footprint = px * travelled;
        float band = h * 1.1 - flow * 2.0 + position.z * 0.01;
        float3 hue = terrainPalette(band);
        float contourCoord = h / 0.09;
        float contourWidth = slope / 0.09 * footprint * 1.2 + 0.02;
        float contour = saturate(1.0 - abs(frac(contourCoord + 0.5) - 0.5) / contourWidth);
        contour *= smoothstep(0.1, 0.25, h) * saturate(0.6 / contourWidth);   // fade where lines would alias

        float3 surface = hue * (0.05 + 0.3 * heightMix) * (0.35 + diffuse * 0.9);
        colour = surface
               + lerp(hue, 1.0, 0.25) * contour * (0.25 + heightMix * 1.4)
               + lerp(hue, 1.0, 0.6) * specular * (0.3 + heightMix) * 1.2
               + terrainPalette(band + 0.3) * rim * 0.35 * (0.3 + heightMix);

        // The rows written on a beat glow as they roll away, so the rhythm stays visible.
        float beatRow = smoothstep(0.55, 0.95, here.y);
        colour += lerp(hue, 1.0, 0.5) * beatRow * 0.12 * (0.3 + heightMix);

        // The newest ridge, at your feet, is the live spectrum: it burns brighter and cools as it rolls away.
        float row = position.z / TerrainSpacing - scroll;
        colour *= 1.0 + exp(-max(row, 0.0) * 0.5) * 0.8;

        colour = lerp(haze, colour, exp(-max(travelled - 2.0, 0.0) * 0.14));
    }

    // Shade the sky softly towards the memory block in the corner, so it never shows as a dark square.
    float clearOfMemory = max(pixel.x - TerrainBands, pixel.y - TerrainRows);
    colour *= smoothstep(0.0, Resolution.y * 0.12, clearOfMemory);
    return colour;
}
