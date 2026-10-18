// Name: Quantum Foam
// Drifting through a vast foam of iridescent soap-film bubbles that swell, glow and pop in and out of existence.
// Family: Ray traced
// Tags: Calm, 3D
// Added: 2026-10-16 23:54

static const float3 FoamHeading = float3(0.31, 0.17, 0.935);   // flight line, tilted across the lattice

float foamHash(float3 p)
{
    p = frac(p * 0.3183099 + float3(0.71, 0.113, 0.419));
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

// The bubble living in a lattice cell: xyz = centre, w = radius (0 when the cell is empty);
// seed returns the cell's random number. Bubbles slowly grow, live and pop; cells near the
// flight line stay empty so we glide through.
float4 foamBubble(float3 cell, float3 lineOrigin, out float seed)
{
    seed = foamHash(cell);
    float3 more = frac(seed * float3(13.71, 57.23, 91.37));
    float life = frac(Time * (0.03 + seed * 0.04) + more.x);
    float alive = smoothstep(0.0, 0.15, life) * smoothstep(1.0, 0.9, life);
    float radius = (0.18 + 0.27 * more.y) * alive * (1.0 + Beat * 0.12);
    float3 centre = cell + 0.5;
    float3 fromLine = centre - lineOrigin;
    float offLine = length(fromLine - FoamHeading * dot(fromLine, FoamHeading));
    radius *= step(0.85, offLine) * step(0.45, seed);
    radius = min(radius, 0.46);
    float3 jitter = frac(seed * float3(31.17, 73.91, 17.53)) - 0.5;
    centre += jitter * (1.0 - 2.0 * radius) * 0.9;
    return float4(centre, radius);
}

// What a film reflects: a soft rainbow sky with a couple of bright windows of light.
float3 foamSky(float3 r)
{
    float3 sky = palette(r.y * 0.35 + r.x * 0.15 + Time * 0.02) * (0.15 + 0.1 * r.z);
    float3 sun = normalize(float3(-0.5, -0.6, 0.6));
    float3 lamp = normalize(float3(0.7, 0.2, 0.7));
    sky += pow(saturate(dot(r, sun)), 60.0) * 3.0 + pow(saturate(dot(r, lamp)), 30.0) * float3(1.0, 0.6, 0.9) * 1.5;
    return sky;
}

// Thin-film interference: colour shifts with film thickness and the viewing angle.
float3 foamFilm(float thickness, float cosine)
{
    float phase = thickness * (0.6 + cosine * 1.4);
    return 0.5 + 0.5 * cos(2.0 * PI * (phase * float3(1.0, 1.22, 1.45) + float3(0.0, 0.1, 0.2)));
}

float3 Render(float2 uv, float2 p)
{
    float travel = Time * 0.55 + (BeatCount - Beat) * 0.1;
    float3 lineOrigin = float3(0.37, 0.61, 0.0);
    float3 sway = float3(sin(Time * 0.31), cos(Time * 0.23), 0.0) * 0.12;
    float3 eye = lineOrigin + FoamHeading * travel + sway;
    float3 dir = cameraRay(eye, eye + FoamHeading * 2.0 + float3(sin(Time * 0.13), cos(Time * 0.17), 0.0) * 0.3,
                           p, 1.0 - Beat * 0.08, Time * 0.04);

    // Walk the lattice cell by cell along the ray (exact grid traversal).
    float3 cell = floor(eye);
    float3 stepSign = float3(dir.x >= 0.0 ? 1.0 : -1.0, dir.y >= 0.0 ? 1.0 : -1.0, dir.z >= 0.0 ? 1.0 : -1.0);
    float3 safeDir = stepSign * max(abs(dir), 1e-5);
    float3 tDelta = abs(1.0 / safeDir);
    float3 tMax = (cell + max(stepSign, 0.0) - eye) / safeDir;

    float3 colour = 0.0;
    float transmit = 1.0;
    float hueShift = Time * 0.03;
    int cellCount = qualitySteps(30);
    [loop]
    for (int i = 0; i < cellCount; i++)
    {
        float seed = 0.0;
        float4 bubble = foamBubble(cell, lineOrigin, seed);
        if (bubble.w > 0.02)
        {
            float3 oc = eye - bubble.xyz;
            float b = dot(oc, dir);
            float c = dot(oc, oc) - bubble.w * bubble.w;
            float h = b * b - c;
            float band = frac(seed * 7.3);
            float energy = spectrum(band);

            // A glowing core in some bubbles, brighter with the bass and its spectrum band.
            float coreMiss = length(oc - dir * b);
            float hasCore = step(0.72, seed);
            float coreDepth = -b;
            float fade = exp(-max(coreDepth, 0.0) * 0.11);
            float inside = smoothstep(bubble.w, 0.0, coreMiss);
            colour += transmit * hasCore * step(0.0, coreDepth) * neon(band + hueShift) * inside * inside
                    * (0.003 + energy * 0.007) * (0.6 + Bass) / (coreMiss * coreMiss + 0.004) * fade;

            if (h > 0.0)
            {
                float root = sqrt(h);
                [unroll]
                for (int side = 0; side < 2; side++)
                {
                    float t = side == 0 ? -b - root : -b + root;
                    if (t > 0.0)
                    {
                        float3 hit = eye + dir * t;
                        float3 normal = (hit - bubble.xyz) / bubble.w;
                        float cosine = abs(dot(normal, dir));
                        float fresnel = 0.04 + 0.96 * pow(1.0 - cosine, 5.0);
                        // Films are thin on top and thick below, with bands that swirl over time.
                        float thickness = 1.2 + normal.y * 0.8 + sin(normal.x * 4.0 + normal.z * 3.0 + Time * 0.6 + seed * 20.0) * 0.35 + hueShift * 4.0;
                        float3 film = foamFilm(thickness, cosine);
                        float3 reflected = foamSky(reflect(dir, normal));
                        float distanceFade = exp(-t * 0.13);
                        colour += transmit * film * reflected * (fresnel * 2.2 + 0.03) * distanceFade
                                + transmit * film * hasCore * (0.05 + energy * 0.15) * distanceFade;
                        transmit *= 1.0 - fresnel * 0.6;
                    }
                }
            }
        }

        // Step into the neighbouring cell the ray reaches first.
        float3 nextAxis = float3(tMax.x <= tMax.y && tMax.x <= tMax.z ? 1.0 : 0.0,
                                 tMax.y < tMax.x && tMax.y <= tMax.z ? 1.0 : 0.0,
                                 tMax.z < tMax.x && tMax.z < tMax.y ? 1.0 : 0.0);
        cell += nextAxis * stepSign;
        tMax += nextAxis * tDelta;
        if (transmit < 0.05 || min(tMax.x, min(tMax.y, tMax.z)) > 16.0)
        {
            break;
        }
    }

    // Deep space behind the foam, with faint sparks that twinkle with the treble.
    float3 space = hsv2rgb(0.7 + hueShift, 0.7, 0.03) * (0.6 + 0.4 * fbm(p * 1.5 + Time * 0.02));
    float2 sky = dir.xy / max(dir.z, 0.2) * 40.0;
    float2 starCell = floor(sky);
    float starMiss = length(frac(sky) - 0.5 - (float2(hash(starCell + 1.7), hash(starCell + 4.1)) - 0.5) * 0.6);
    float star = step(0.97, hash(starCell)) * glow(starMiss, 25.0) * (0.3 + Treble * 1.2)
               * (0.6 + 0.4 * sin(Time * 4.0 + hash(starCell + 3.0) * 40.0));
    colour += transmit * (space + star * 0.8);
    return colour + previous(uv) * 0.1;
}
