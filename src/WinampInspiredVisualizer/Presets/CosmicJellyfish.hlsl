// Name: Cosmic Jellyfish
// Glass-clear jellyfish with galaxies swirling inside their bells drift up through deep space, pulsing on every beat and trailing tentacles that ripple with the music.
// Family: Cosmic
// Tags: Calm, Space

static const int JellyCount = 5;

// A little spiral galaxy on the plane inside the bell (g in bell radii).
float3 jellyGalaxy(float2 g, float spin, float hue, float pixel, float seed)
{
    float radius = length(g);
    float angle = atan2(g.y, g.x) - spin;
    float arms = pow(0.5 + 0.5 * cos(2.0 * angle - log(radius + 0.03) * 4.5), 3.0);
    float grain = noise(g * 7.0 + seed);
    float glowing = (arms * (0.4 + 0.9 * grain) * 0.5 + exp(-radius * 10.0) * 1.2) * exp(-radius * 2.6);
    float3 colour = hsv2rgb(hue + 0.25 - radius * 0.3, 0.75, 1.0) * glowing;

    // Stars inside the galaxy, pin-sharp.
    float2 grid = g * 14.0;
    float2 cell = floor(grid);
    float2 spot = float2(hash(cell + seed), hash(cell + seed + 3.7)) * 0.7 + 0.15;
    float gap = length(frac(grid) - spot) / (14.0 * pixel);
    float star = step(0.8, hash(cell + seed + 9.1)) * exp(-gap * gap * 1.2) * exp(-radius * 1.5);
    colour += float3(0.9, 0.95, 1.0) * star * (0.8 + Treble * 1.5);
    return colour;
}

// One side of the bell (front or back) where the ray crosses it.
float3 jellyShell(float3 hit, float3 ray, float3 shape, float hue, float spin, float pixel, float weight)
{
    float3 normal = normalize(hit / (shape * shape));
    float facing = abs(dot(normal, ray));

    // Light gathers where we look through the jelly edge-on: a soft body and a crisp rim whose
    // colour slides like a soap film.
    float rimWidth = sqrt(3.0 * pixel);
    float rim = exp(-(facing / rimWidth) * (facing / rimWidth));
    float3 film = hsv2rgb(hue - 0.22 * (1.0 - facing), 0.6, 1.0);
    float3 colour = film * (0.04 + 0.45 * pow(1.0 - facing, 3.0)) + lerp(film, float3(1.0, 1.0, 1.0), 0.5) * rim * 1.4;

    // Sixteen fine radial canals running from the top of the bell to its rim.
    float around = atan2(hit.z, hit.x) + spin;
    float spread = length(hit.xz);
    float canal = abs(frac(around * 16.0 / (2.0 * PI) + 0.5) - 0.5) * (2.0 * PI / 16.0) * spread;
    float canalPixels = canal * max(facing, 0.3) / pixel;
    // After each beat a wave of light runs down the canals from the top of the bell to its rim.
    float wave = shape.y * (1.0 - (1.0 - Beat) * 1.4);
    float surge = 1.0 + Beat * 6.0 * exp(-abs(hit.y - wave) * 14.0);
    colour += film * exp(-canalPixels * canalPixels * 0.8) * 0.4 * smoothstep(0.05, 0.3, spread) * surge;
    return colour * weight;
}

// Light from one jellyfish. q: the pixel relative to the jelly in bell radii (screen axes, y
// down); across/axis/toward: the jelly's frame (axis runs from its mouth to the top of the
// bell); squeeze: how far the bell is contracted; pixel: a pixel's size in bell radii.
float3 jellyLight(float2 q, float3 across, float3 axis, float3 toward, float squeeze, float spin,
                  float hue, float pixel, float seed)
{
    float3 colour = 0.0;
    float3 tint = hsv2rgb(hue, 0.6, 1.0);
    float3 deepTint = hsv2rgb(hue + 0.1, 0.8, 1.0);

    // The ray through this pixel, in the jelly's own frame.
    float3 origin = float3(q, -4.0);
    float3 ro = float3(dot(origin, across), dot(origin, axis), dot(origin, toward));
    float3 ray = float3(across.z, axis.z, toward.z);

    // The bell: a little more than half an ellipsoid, narrower and taller as it contracts.
    float bellWidth = 1.0 - 0.17 * squeeze;
    float bellHeight = 0.7 + 0.15 * squeeze;
    float3 shape = float3(bellWidth, bellHeight, bellWidth);
    float rimY = -0.1 * bellHeight;
    float3 os = ro / shape;
    float3 ds = ray / shape;
    float a = dot(ds, ds);
    float b = dot(os, ds);
    float c = dot(os, os) - 1.0;
    float disc = b * b - a * c;
    if (disc > 0.0)
    {
        float root = sqrt(disc);
        float3 front = ro + ray * ((-b - root) / a);
        float3 back = ro + ray * ((-b + root) / a);
        float scallopFront = 0.025 * cos(atan2(front.z, front.x) * 16.0);
        float scallopBack = 0.025 * cos(atan2(back.z, back.x) * 16.0);
        if (front.y > rimY + scallopFront)
        {
            colour += jellyShell(front, ray, shape, hue, spin, pixel, 1.0);
        }
        if (back.y > rimY + scallopBack)
        {
            colour += jellyShell(back, ray, shape, hue, spin, pixel, 0.5);
        }
    }

    // Inside the bell, seen through it: four horseshoe gonads around a swirling galaxy.
    float innerY = 0.3 * bellHeight;
    if (abs(ray.y) > 0.03)
    {
        float3 inner = ro + ray * ((innerY - ro.y) / ray.y);
        float spread = dot(inner.xz, inner.xz) / (bellWidth * bellWidth) + (innerY / bellHeight) * (innerY / bellHeight);
        if (spread < 1.0)
        {
            float2 g = inner.xz / bellWidth;
            colour += jellyGalaxy(g, spin * 3.0 + Time * 0.2, hue, pixel, seed) * (0.6 + Bass * 0.9);
            float around = atan2(g.y, g.x) + spin;
            float lobe = floor(around / (PI * 0.5) + 0.5) * (PI * 0.5);
            float2 lobeCentre = float2(cos(lobe - spin), sin(lobe - spin)) * 0.3;
            float2 fromLobe = g - lobeCentre;
            float horseshoe = abs(length(fromLobe) - 0.13) / (pixel / max(abs(ray.y), 0.2));
            float open = smoothstep(-0.2, 0.5, -dot(normalize(fromLobe), normalize(lobeCentre)));
            colour += deepTint * exp(-horseshoe * horseshoe * 0.25) * open * 0.9;
        }
    }

    // The rim: a crisp scalloped ring (an ellipse on screen) with eight glinting sense organs.
    float rimRadius = bellWidth * sqrt(1.0 - 0.01);
    float2 rimCentre = axis.xy * rimY;
    float squash = max(length(toward.xy), pixel);
    float2 e = float2(dot(q - rimCentre, across.xy), dot(q - rimCentre, toward.xy) / squash);
    float2 axes = float2(rimRadius, rimRadius * squash);
    float rimAngle = atan2(e.y * axes.x, e.x * axes.y);
    [unroll]
    for (int n = 0; n < 3; n++)
    {
        // Newton steps towards the nearest point of the ellipse.
        float s, co;
        sincos(rimAngle, s, co);
        float2 onRim = axes * float2(co, s);
        float2 tangent = axes * float2(-s, co);
        float slope = dot(onRim - e, tangent);
        float curve = dot(tangent, tangent) - dot(onRim - e, onRim);
        rimAngle -= slope / max(curve, 0.0001);
    }
    float2 nearestRim = axes * float2(cos(rimAngle), sin(rimAngle)) * (1.0 + 0.025 * cos(rimAngle * 16.0));
    float rimGap = length(e - nearestRim) / pixel;
    float organ = abs(frac(rimAngle * 8.0 / (2.0 * PI)) - 0.5) * 2.0;
    colour += tint * exp(-rimGap * rimGap * 0.4) * (0.6 + 2.5 * pow(organ, 30.0) * (0.5 + Treble * 2.0));

    // A faint aura of light around the bell.
    float2 bellCentre = axis.xy * bellHeight * 0.35;
    colour += deepTint * exp(-max(length(q - bellCentre) - 0.85, 0.0) * 4.0) * 0.05;

    // Tentacles hang from the rim and trail behind, rippling as the jelly swims; a bead of light
    // runs down each one after a beat.
    float2 down = normalize(-axis.xy);
    float2 sideways = float2(-down.y, down.x);
    float foreshorten = max(length(axis.xy), 0.3);
    float bead = (1.0 - Beat) * 4.5;
    [unroll]
    for (int i = 0; i < 8; i++)
    {
        float k = i;
        float angle = (k + 0.5) * (2.0 * PI / 8.0) - spin;
        float2 anchor = rimCentre + rimRadius * (cos(angle) * across.xy + sin(angle) * toward.xy);
        float2 rel = q - anchor;
        float along = dot(rel, down) / foreshorten;
        if (along > 0.0 && along < 6.0)
        {
            float sway = (sin(along * 2.1 - Time * 1.9 + k * 1.7 + seed) * 0.12
                        + sin(along * 4.9 - Time * 3.1 + k * 2.3) * 0.03) * along;
            float gap = abs(dot(rel, sideways) - sway) / pixel;
            float fade = exp(-along * (0.35 + 0.1 * hash(float2(seed, k))));
            float flare = Beat * exp(-abs(along - bead) * 4.0);
            colour += tint * (exp(-gap * gap * 0.5) + exp(-gap * 0.3) * 0.05) * fade * (0.5 + flare * 3.0);
        }
    }

    // Four frilled oral arms twist below the bell.
    [unroll]
    for (int arm = 0; arm < 4; arm++)
    {
        float k = arm;
        float angle = (k + 0.25) * (PI * 0.5) - spin;
        float2 anchor = rimCentre + 0.1 * (cos(angle) * across.xy + sin(angle) * toward.xy);
        float2 rel = q - anchor;
        float along = dot(rel, down) / foreshorten;
        if (along > 0.0 && along < 3.2)
        {
            float sway = sin(along * 1.4 - Time * 1.1 + k * 2.1 + seed) * 0.1 * along;
            float twist = 0.5 + 0.5 * sin(along * 2.4 - Time * 1.3 + k * 1.3);
            float ruffle = 0.018 * sin(along * 40.0 - Time * 5.0 + k) * (0.4 + 0.6 * twist);
            float halfWidth = ((0.045 + 0.05 * twist) * (1.0 - along / 3.4) + ruffle) ;
            float offset = abs(dot(rel, sideways) - sway);
            float edge = abs(offset - halfWidth) / pixel;
            float fade = exp(-along * 0.7);
            float3 pale = lerp(deepTint, float3(1.0, 0.95, 0.9), 0.35);
            colour += pale * (exp(-edge * edge * 0.5) * 0.5 + step(offset, halfWidth) * 0.12) * fade;
        }
    }
    return colour;
}

float3 Render(float2 uv, float2 p)
{
    float pixel = pixelSize();

    // Deep space: faint nebula clouds and drifting motes at three depths.
    float2 drift = float2(Time * 0.01, Time * 0.025);
    float cloud = fbm(p * 0.9 + drift);
    float3 colour = lerp(float3(0.0, 0.008, 0.025), float3(0.06, 0.015, 0.11), smoothstep(0.3, 0.8, cloud));
    colour += float3(0.0, 0.06, 0.09) * smoothstep(0.45, 0.85, fbm(p * 1.7 - drift * 1.3 + 3.0)) * (0.7 + Bass * 0.6);
    [unroll]
    for (int layer = 0; layer < 3; layer++)
    {
        float k = layer;
        float scale = 14.0 + k * 10.0;
        float2 grid = (p + float2(0.0, Time * (0.02 + 0.015 * k))) * scale + k * 7.3;
        float2 cell = floor(grid);
        float2 spot = float2(hash(cell + 1.7), hash(cell + 4.2)) * 0.8 + 0.1;
        float gap = length(frac(grid) - spot) / (scale * pixel);
        float bright = step(0.75, hash(cell)) * (0.15 + 0.3 * hash(cell + 9.1)) / (1.0 + k);
        colour += float3(0.6, 0.75, 1.0) * bright * exp(-gap * gap * 1.5) * (0.7 + Treble * 0.8);
    }

    // The jellyfish, each rising through the frame and coming back somewhere new.
    [loop]
    for (int j = 0; j < JellyCount; j++)
    {
        float k = j;
        float depth = 0.3 + 0.7 * frac(k * 0.618 + 0.11);        // 1 = nearest
        float size = 0.09 + 0.36 * depth * depth;
        // Each beat is a stroke of the bell that lifts it a little faster.
        float climb = Time * (0.022 + 0.03 * depth) + (BeatCount + 1.0 - Beat) * 0.004 + k / JellyCount;
        float cycle = floor(climb);
        float span = 2.0 + size * 7.0;
        float2 centre = float2((hash(float2(cycle, k + 0.5)) - 0.5) * 1.6 * Aspect + sin(Time * 0.17 + k * 2.0) * 0.1,
                               1.0 + size * 0.9 - frac(climb) * span);

        // Orientation: rolling gently and leaning so we see into the bell, then over its top.
        float roll = 0.3 * sin(Time * 0.21 + k * 1.9);
        float lean = -0.3 + 0.4 * sin(Time * 0.13 + k * 2.7);
        float sr, cr, sl, cl;
        sincos(roll, sr, cr);
        sincos(lean, sl, cl);
        float3 across = float3(cr, sr, 0.0);
        float3 axis = float3(cl * sr, -cl * cr, -sl);
        float3 toward = float3(-sl * sr, sl * cr, -cl);

        float2 q = (p - centre) / size;
        float2 down = normalize(-axis.xy);
        float along = dot(q, down);
        if (along > -1.3 && along < 6.5 && abs(dot(q, float2(-down.y, down.x))) < 1.4 + along * 0.35)
        {
            float level = spectrum(0.06 + k * 0.12);
            float squeeze = Beat * (0.6 + 0.4 * level) + 0.25 * (0.5 + 0.5 * sin(Time * 1.6 + k * 2.0));
            float hue = 0.5 + 0.45 * frac(k * 0.618 + Time * 0.005);
            float blur = 1.0 + (1.0 - depth) * 1.2;
            float3 light = jellyLight(q, across, axis, toward, squeeze, Time * 0.08 + k, hue, pixel * blur / size, k * 13.0);
            colour += light * (0.3 + 0.7 * depth) * (0.75 + level * 0.8);
        }
    }
    return colour;
}
