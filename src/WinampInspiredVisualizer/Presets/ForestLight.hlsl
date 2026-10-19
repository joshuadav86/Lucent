// Name: Forest Light
// Wander slowly along a trail through a misty forest at sunrise as shafts of golden light fall through gaps in the canopy onto ferns and moss, glinting on drifting dust and spores; the light breathes with the music and the leaves stir with the mids.
// Family: Ray traced
// Tags: Calm, 3D
// Added: 2026-10-17 17:15

// The trunks and the ground are found by ray marching. The light shafts come from marching
// through the mist as well: at each step we ask whether the sun can see that spot through the
// canopy (a sheet of leaves high overhead with holes in it) and past the trunks, and add the
// light scattered towards us. The mist is drawn with a little noise that changes every frame,
// and blended with the last frame so it settles into smooth shafts.
static const float CellSize = 2.6;
static const float CanopyHeight = 7.5;
static const float FarDistance = 38.0;

float3 sunDirection()
{
    return normalize(float3(0.18, 0.4, 1.0));
}

float3 sunColour()
{
    float breathe = 1.0 + Bass * 0.45 + Level * 0.3 + Beat * 0.12;
    return float3(1.0, 0.72, 0.4) * 2.0 * breathe;
}

float hash3(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float mistNoise(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(lerp(hash3(i), hash3(i + float3(1, 0, 0)), f.x), lerp(hash3(i + float3(0, 1, 0)), hash3(i + float3(1, 1, 0)), f.x), f.y),
                lerp(lerp(hash3(i + float3(0, 0, 1)), hash3(i + float3(1, 0, 1)), f.x), lerp(hash3(i + float3(0, 1, 1)), hash3(i + float3(1, 1, 1)), f.x), f.y), f.z);
}

// ---- Trees ----

// The trunk in a cell: where it stands (xz), its radius (z) and a lean (w). The middle column
// of cells is left clear for the trail.
float4 trunkIn(float2 cell)
{
    if (abs(cell.x) < 0.5)
    {
        return float4(0.0, 0.0, 0.0, 0.0);
    }
    float a = hash(cell * 1.37 + 0.5);
    float b = hash(cell * 2.11 + 3.1);
    float2 centre = (cell + 0.5) * CellSize + (float2(a, b) - 0.5) * CellSize * 0.5;
    float radius = 0.16 + hash(cell + 7.7) * 0.26;
    return float4(centre, radius, (hash(cell + 9.1) - 0.5) * 0.06);
}

float trunkDistance(float3 q, float4 trunk)
{
    if (trunk.z <= 0.0)
    {
        return 1e3;
    }
    float2 offset = q.xz - trunk.xy - float2(trunk.w * q.y, 0.0);
    // Roots flare out at the foot; bark is ridged up and down.
    float flare = 1.0 + 0.7 * exp(-max(q.y, 0.0) * 2.5);
    float rounded = length(offset) - trunk.z * flare;
    // The ridges are never more than 0.025 deep, so away from the bark that bound is as good as
    // the exact distance for marching, and saves the angle and the noise.
    if (rounded > 0.2)
    {
        return rounded - 0.025;
    }
    float angle = atan2(offset.y, offset.x);
    float bark = (noise(float2(angle * 3.0, q.y * 1.5)) - 0.5) * 0.05;
    return rounded - bark;
}

float treesDistance(float3 q)
{
    // The four cells nearest the point, so no trunk is missed near a cell edge.
    float2 base = floor(q.xz / CellSize - 0.5);
    float d = 1e3;
    [unroll]
    for (int i = 0; i < 4; i++)
    {
        float2 cell = base + float2(i & 1, i >> 1);
        d = min(d, trunkDistance(q, trunkIn(cell)));
    }
    return d;
}

// ---- Ground ----

float fernAmount(float2 xz)
{
    float clumps = noise(xz * 0.55 + 3.0) * 0.65 + noise(xz * 1.4) * 0.35;
    // Fewer ferns on the trail itself.
    float trail = smoothstep(0.5, 1.4, abs(xz.x - sin(xz.y * 0.15) * 0.4));
    return smoothstep(0.45, 0.75, clumps) * lerp(0.15, 1.0, trail);
}

float groundHeight(float2 xz)
{
    float h = noise(xz * 0.18) * 0.6 + noise(xz * 0.7) * 0.15;
    // Fern clumps: mounds of fronds, ruffled in a fan pattern.
    float fern = fernAmount(xz);
    float fronds = 0.6 + 0.4 * sin(atan2(frac(xz.y * 0.9) - 0.5, frac(xz.x * 0.9) - 0.5) * 7.0);
    return h + fern * (0.35 + 0.15 * fronds);
}

// The ground is never higher than this: the rolling noise reaches 0.75, the ferns 0.5 more.
static const float HighestGround = 1.25;

float sceneDistance(float3 q, out int material)
{
    float trees = treesDistance(q);
    // Well above the highest ground, how far above it is a safe step and saves working out the
    // ground's shape; close to it, the exact height.
    float ground = q.y > HighestGround + 0.1 ? (q.y - HighestGround) * 0.6 : (q.y - groundHeight(q.xz)) * 0.6;
    material = trees < ground ? 1 : 0;
    return min(trees, ground);
}

float3 sceneNormal(float3 q)
{
    const float e = 0.01;
    int m;
    return normalize(float3(
        sceneDistance(q + float3(e, 0, 0), m) - sceneDistance(q - float3(e, 0, 0), m),
        sceneDistance(q + float3(0, e, 0), m) - sceneDistance(q - float3(0, e, 0), m),
        sceneDistance(q + float3(0, 0, e), m) - sceneDistance(q - float3(0, 0, e), m)));
}

// ---- Light through the leaves ----

// How open the canopy is at a point overhead: 0 under leaves, 1 in a gap. The leaves sway with the mids.
float canopyGap(float2 xz)
{
    float2 sway = float2(sin(Time * 0.4), cos(Time * 0.33)) * (0.15 + Mid * 0.35);
    float n = noise(xz * 0.22 + sway * 0.3) * 0.6 + noise(xz * 0.6 - sway * 0.5) * 0.28 + noise(xz * 1.7 + sway) * 0.12;
    return smoothstep(0.56, 0.68, n);
}

// How much sunlight reaches a point: through the canopy, then past the trunks.
float sunVisibility(float3 q)
{
    float3 sun = sunDirection();
    float up = max(CanopyHeight - q.y, 0.0) / sun.y;
    float light = canopyGap(q.xz + sun.xz * up);
    float2 flat = normalize(sun.xz);
    [unroll]
    for (int k = 0; k < 3; k++)
    {
        float2 ahead = q.xz + flat * (k * CellSize * 0.9);
        float4 trunk = trunkIn(floor(ahead / CellSize));
        if (trunk.z > 0.0)
        {
            float2 toTrunk = trunk.xy - q.xz;
            float along = dot(toTrunk, flat);
            float off = length(toTrunk - flat * along);
            float height = q.y + along * sun.y / length(sun.xz);
            light *= along > 0.0 && height < CanopyHeight ? smoothstep(trunk.z * 0.8, trunk.z * 1.25 + 0.05, off) : 1.0;
        }
    }
    return light;
}

float mistDensity(float3 q)
{
    float drift = Time * 0.15;
    float n = mistNoise(q * 0.35 + float3(drift, 0.0, drift * 0.4)) * 0.65 + mistNoise(q * 0.9 - float3(0.0, drift, 0.0)) * 0.35;
    return 0.022 * exp(-max(q.y, 0.0) * 0.25) * (0.35 + n * 1.1);
}

// Forward-scattering haze: the shafts are brightest looking towards the sun.
float phase(float cosine)
{
    const float g = 0.55;
    float denominator = 1.0 + g * g - 2.0 * g * cosine;
    return (1.0 - g * g) / (4.0 * PI * denominator * sqrt(denominator)) * 4.0 * PI;
}

float3 cameraEye()
{
    float walk = Time * 0.32;
    return float3(sin(walk * 0.16) * 0.45, 1.55 + sin(walk * 1.9) * 0.025, walk);
}

float3 Render(float2 uv, float2 p)
{
    float3 eye = cameraEye();
    float3 target = eye + float3(sin(Time * 0.031) * 0.35, 0.16 + sin(Time * 0.023) * 0.08, 1.0);
    float3 direction = cameraRay(eye, target, float2(p.x, -p.y), 1.45, sin(Time * 0.02) * 0.02);
    float3 sun = sunDirection();
    float3 light = sunColour();
    float towardSun = dot(direction, sun);

    // March to the first surface.
    float t = 0.05;
    int material = -1;
    int steps = qualitySteps(96);
    [loop]
    for (int i = 0; i < steps; i++)
    {
        float3 q = eye + direction * t;
        int m;
        float d = sceneDistance(q, m);
        if (d < 0.002 * t * surfacePrecision())
        {
            material = m;
            break;
        }
        t += d;
        if (t > FarDistance || q.y > CanopyHeight)
        {
            break;
        }
    }
    t = min(t, FarDistance);
    // What's behind the mist: the canopy overhead, or the surface we hit.
    float3 ambient = float3(0.14, 0.2, 0.15) * (0.8 + Level * 0.3);

    // Looking up into the leaves: dark foliage, glowing green where the sun shines through, with
    // bright sky in the gaps. Far off, the leaves melt into a dim green-grey.
    float up = direction.y > 0.02 ? (CanopyHeight - eye.y) / direction.y : 1e3;
    float2 overhead = eye.xz + direction.xz * up;
    float leafTexture = noise(overhead * 3.0) * 0.5 + noise(overhead * 9.0) * 0.5;
    float gap = up < 120.0 ? canopyGap(overhead) * smoothstep(0.35, 0.75, leafTexture) * smoothstep(120.0, 40.0, up) : 0.0;
    float3 leaves = float3(0.015, 0.03, 0.012) * (0.5 + leafTexture) + float3(0.25, 0.4, 0.06) * pow(saturate(towardSun), 6.0) * 0.35 * (0.3 + leafTexture);
    float3 sky = float3(1.0, 0.82, 0.6) * 0.8 + light * pow(saturate(towardSun), 80.0) * 4.0;
    float3 canopy = lerp(leaves, sky, gap);
    canopy = lerp(canopy, float3(0.05, 0.065, 0.05), exp(-max(direction.y, 0.0) * 5.0) * 0.8);

    float3 colour = canopy;
    if (material >= 0)
    {
        float3 q = eye + direction * t;
        float3 normal = sceneNormal(q);
        float sunlit = sunVisibility(q + normal * 0.02);
        float diffuse = saturate(dot(normal, sun));
        float3 albedo;
        float3 glowThrough = 0.0;
        if (material == 1)
        {
            // Bark, greener with moss on the shady side and near the ground.
            float moss = saturate(0.5 - normal.z * 0.6 + exp(-q.y * 1.2) * 0.6) * noise(q.xy * 6.0 + q.z);
            albedo = lerp(float3(0.12, 0.09, 0.07), float3(0.12, 0.2, 0.05), moss);
            albedo *= 0.75 + 0.5 * noise(float2(atan2(normal.z, normal.x) * 8.0, q.y * 6.0));
        }
        else
        {
            // Moss and leaf litter, with ferns (finely divided fronds) that glow when the sun
            // shines through them.
            float fern = fernAmount(q.xz);
            float litter = noise(q.xz * 4.0);
            float3 floorColour = lerp(float3(0.1, 0.14, 0.04), float3(0.18, 0.12, 0.05), litter * smoothstep(1.2, 0.4, abs(q.x - sin(q.z * 0.15) * 0.4)));
            float leaflets = 0.55 + 0.45 * sin(q.x * 40.0 + sin(q.z * 9.0) * 3.0) * sin(q.z * 37.0 + sin(q.x * 7.0) * 3.0);
            albedo = lerp(floorColour, float3(0.09, 0.24, 0.04) * leaflets, fern);
            glowThrough = float3(0.35, 0.6, 0.08) * fern * leaflets * pow(saturate(towardSun), 2.0) * sunlit;
        }
        float3 surface = albedo * (light * diffuse * sunlit + ambient * (0.5 + 0.5 * normal.y)) + glowThrough * light * 0.3;
        // Trunks disappear up into the leaves.
        colour = lerp(surface, canopy, smoothstep(CanopyHeight - 3.0, CanopyHeight, q.y));
    }

    // March back through the mist, adding the light it scatters our way.
    int mistSteps = qualitySteps(48);
    float jitter = frac(hash(uv * Resolution + frac(FrameCount * 0.618) * 97.0) + FrameCount * 0.618);
    float stepLength = t / mistSteps;
    float3 scattered = 0.0;
    float transmittance = 1.0;
    float shafts = phase(towardSun);
    [loop]
    for (int s = 0; s < mistSteps; s++)
    {
        float3 q = eye + direction * ((s + jitter) * stepLength);
        float density = mistDensity(q);
        float3 inLight = light * sunVisibility(q) * shafts + ambient * 0.18;
        scattered += transmittance * density * inLight * stepLength;
        transmittance *= exp(-density * stepLength);
    }
    colour = colour * transmittance + scattered;

    // Dust and spores drifting in the air, glinting where the beams catch them.
    float3 forward = normalize(target - eye);
    [unroll]
    for (int layer = 0; layer < 6; layer++)
    {
        float depth = 1.2 + layer * 1.3;
        float along = depth / max(dot(direction, forward), 0.2);
        if (along < t)
        {
            float3 q = eye + direction * along;
            float3 drifted = q + float3(sin(Time * 0.1 + layer) * 0.3, -Time * 0.04, 0.0);
            float3 cell = floor(drifted * 3.0);
            float3 inCell = frac(drifted * 3.0) - 0.5;
            float pick = hash3(cell + layer * 13.0);
            float3 spot = float3(hash3(cell + 1.7), hash3(cell + 4.3), hash3(cell + 8.9)) - 0.5;
            float distanceToMote = length((inCell - spot * 0.8) - direction * dot(inCell - spot * 0.8, direction));
            float size = 0.025 + 0.02 / depth;
            float twinkle = 0.5 + 0.5 * sin(Time * (2.0 + pick * 5.0) + pick * 40.0);
            float mote = step(0.8, pick) * exp(-distanceToMote * distanceToMote / (size * size)) * twinkle;
            colour += light * mote * sunVisibility(q) * (0.25 + Treble * 0.9) * transmittance;
        }
    }

    // Settle the mist's noise over a few frames.
    return lerp(colour, previous(uv), 0.55);
}
