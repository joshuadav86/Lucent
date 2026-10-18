// Name: Sunset Highway
// Racing down a neon grid highway towards a striped retro sunset, the road surging forward on every beat and the mountains dancing to the spectrum.
// Family: Cyberpunk
// Tags: Neon, Retro, Colourful, 3D
// Added: 2026-10-17 01:16

static const float Horizon = -0.04;      // screen height of the horizon (up is positive)
static const float CameraHeight = 1.5;
static const float Lens = 1.1;

// Height of the far mountain range at screen x, shaped by the spectrum: bass in the middle, treble at the sides.
float highwayMountains(float x)
{
    float ridge = fbm(float2(x * 1.8 + 3.0, 1.7)) * 0.5 + (1.0 - abs(sin(x * 2.3 + 1.0))) * 0.08;
    float band = saturate(abs(x) / 1.9);
    float music = spectrum(0.03 + band * 0.6);
    // A dip in the middle frames the sun.
    float valley = smoothstep(0.2, 1.1, abs(x));
    return ridge * (0.08 + valley * 0.85) + music * 0.18 * valley;
}

// Anti-aliased grid line: 1 on the line, fading over about a pixel. footprint is how far the
// coordinate moves across one pixel; lines thinner than that dim instead of shimmering.
float highwayLine(float coordinate, float width, float footprint)
{
    float w = max(footprint, 1e-5);
    float d = abs(frac(coordinate + 0.5) - 0.5);
    return saturate((width + w - d) / (w * 1.5)) * saturate(width / w);
}

float3 highwayScene(float2 q, float2 uv)
{
    float pixel = pixelSize();
    float travel = Time * 3.5 + (BeatCount - Beat) * 1.4;
    float3 colour = 0.0;

    // Sky: deep indigo overhead melting into hot magenta at the horizon.
    float above = max(q.y - Horizon, 0.0);
    float3 sky = lerp(float3(0.85, 0.12, 0.45), float3(0.22, 0.04, 0.35), saturate(above * 3.2));
    sky = lerp(sky, float3(0.015, 0.0, 0.06), saturate(above * 1.4 - 0.25));
    colour = sky * (0.55 + Level * 0.2);

    // Stars twinkle with the treble.
    float2 starCell = floor(q * 60.0);
    float starSeed = hash(starCell);
    float2 starPos = (starCell + 0.5 + (float2(hash(starCell + 3.1), hash(starCell + 7.7)) - 0.5) * 0.6) / 60.0;
    float star = step(0.93, starSeed) * glow(length(q - starPos), 900.0) * saturate(above * 3.0 - 0.6);
    colour += star * (0.4 + Treble * 1.4 * (0.5 + 0.5 * sin(Time * 5.0 + starSeed * 50.0)));

    // The sun: a gradient disc with bands cut out of its lower half that slide downwards.
    float2 sunCentre = float2(0.0, Horizon + 0.3);
    float sunRadius = 0.42 * (1.0 + Bass * 0.06 + Beat * 0.02);
    float2 fromSun = q - sunCentre;
    float sunDistance = length(fromSun);
    float sunY = fromSun.y / sunRadius;   // -1 bottom .. 1 top
    float stripeCoordinate = (sunY + Time * 0.08) * 9.0;
    float stripeGap = saturate(-sunY * 0.55 + 0.05);
    float stripes = smoothstep(stripeGap - 0.04, stripeGap + 0.04, abs(frac(stripeCoordinate) - 0.5) * 2.0);
    stripes = sunY > 0.1 ? 1.0 : stripes;
    float edgeWidth = pixel * 1.5;
    float disc = saturate((sunRadius - sunDistance) / edgeWidth) * stripes;
    float3 sunColour = lerp(float3(1.0, 0.06, 0.45), float3(1.0, 0.7, 0.12), saturate(sunY * 0.6 + 0.5));
    colour = lerp(colour, sunColour * (0.85 + Beat * 0.35), disc);
    // Its glow spills across the sky.
    colour += float3(1.0, 0.25, 0.5) * glow(sunDistance - sunRadius, 5.0) * (0.25 + Bass * 0.35) * step(Horizon, q.y);

    // Mountains in front of the sun, with neon rims and contour lines.
    float mountainTop = Horizon + highwayMountains(q.x);
    if (q.y > Horizon && q.y < mountainTop)
    {
        float depth = (mountainTop - q.y) / max(mountainTop - Horizon, 1e-3);
        float3 rock = lerp(float3(0.12, 0.01, 0.2), float3(0.015, 0.0, 0.04), depth);
        float contours = highwayLine((q.y - Horizon) * 45.0 + noise(float2(q.x * 6.0, 0.0)) * 0.6, 0.07, 45.0 * pixel) * (1.0 - depth * 0.8) * 0.7;
        float3 mountainColour = rock + float3(0.4, 0.1, 0.9) * contours;
        colour = mountainColour;
    }
    float rimWidth = pixel * 1.5;
    float rim = glow(q.y - mountainTop, 1.0 / rimWidth) * step(Horizon, q.y);
    colour += float3(1.0, 0.3, 0.85) * rim * (0.7 + Mid * 0.8);

    // The ground: an endless neon grid with the highway running down the middle.
    if (q.y < Horizon)
    {
        float below = Horizon - q.y;
        float groundDistance = CameraHeight * Lens / below;
        float2 ground = float2(q.x * groundDistance / Lens, groundDistance + travel);
        float fade = exp(-groundDistance * 0.028);
        // How far the ground moves across one pixel, for the grid lines' anti-aliasing.
        float depthStep = groundDistance / below * pixel;
        float sideStep = (groundDistance + abs(q.x) * groundDistance / below) / Lens * pixel;

        float3 floorColour = float3(0.01, 0.0, 0.025);
        float roadHalf = 1.2;
        float road = 1.0 - smoothstep(roadHalf - 0.02, roadHalf + 0.02, abs(ground.x));

        // Grid lines across and along, the along-lines brightening with their band of the spectrum.
        float across = highwayLine(ground.y / 2.0, 0.025, depthStep / 2.0);
        float along = highwayLine(ground.x / 2.0, 0.025, sideStep / 2.0);
        float columnBand = saturate(abs(ground.x) / 40.0);
        float columnEnergy = spectrum(0.04 + columnBand * 0.7);
        // A wave of light rolls towards the camera after each beat.
        float wave = glow(groundDistance - 45.0 * Beat, 0.25) * Beat;
        float3 gridColour = lerp(float3(1.0, 0.1, 0.75), float3(0.2, 0.5, 1.0), saturate(groundDistance / 60.0));
        float gridLight = across * (0.55 + wave * 2.0) + along * (0.45 + columnEnergy * 1.4);
        colour = floorColour + gridColour * gridLight * (1.0 - road);

        // The road: dark wet tarmac with cyan edges, dashes down the middle and the sun mirrored in it.
        float3 tarmac = float3(0.02, 0.01, 0.04);
        float edges = highwayLine((abs(ground.x) - roadHalf + 0.12) * 0.5, 0.03, sideStep * 0.5) * step(abs(ground.x), roadHalf);
        float dashes = highwayLine(ground.x * 0.5, 0.025, sideStep * 0.5) * step(0.45, frac(ground.y / 6.0));
        float3 roadColour = tarmac + float3(0.1, 0.9, 1.0) * edges * (0.9 + Treble * 0.8)
                          + float3(1.0, 0.85, 0.5) * dashes * (0.6 + Mid * 0.5);
        float shimmer = 0.6 + 0.4 * noise(float2(ground.x * 3.0, ground.y * 0.2));
        float mirror = glow(q.x, 9.0 + below * 30.0) * exp(-below * 4.0) * shimmer;
        roadColour += float3(1.0, 0.3, 0.55) * mirror * (0.5 + Bass * 0.6);
        colour = lerp(colour, roadColour, road);

        // Haze where the ground meets the sky.
        float haze = exp(-below * 18.0);
        colour = lerp(float3(0.75, 0.12, 0.5) * 0.45, colour, fade * (1.0 - haze * 0.85));
        colour += float3(1.0, 0.2, 0.6) * haze * 0.15 * (0.6 + Bass * 0.5);
    }

    return colour;
}

float3 Render(float2 uv, float2 p)
{
    float2 q = float2(p.x, -p.y);
    float3 colour = highwayScene(q, uv);

    // On strong beats the picture splits slightly into red and blue, like an old video signal.
    float split = smoothstep(0.55, 1.0, Beat) * 0.006;
    [branch]
    if (split > 0.0005)
    {
        colour.r = highwayScene(q + float2(split, 0.0), uv).r;
        colour.b = highwayScene(q - float2(split, 0.0), uv).b;
    }

    // Faint scanlines, a little stronger on the beat.
    float scanline = 0.5 + 0.5 * sin(uv.y * Resolution.y / max(RenderScale, 1.0) * (2.0 * PI / 3.0));
    colour *= 1.0 - (0.06 + Beat * 0.08) * scanline;
    return colour;
}
