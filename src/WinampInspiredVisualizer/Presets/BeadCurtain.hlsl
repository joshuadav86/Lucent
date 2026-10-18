// Name: Bead Curtain
// Strands of coloured glass beads sway in a gentle breeze in front of a wall of glowing neon; every bead bends the lights into a tiny jewel, the strands ripple with the bass and the neon changes colour with the music.
// Family: Ray traced
// Tags: Neon, Calm, 3D
// Added: 2026-10-17 17:12

// The curtain hangs in a plane, one strand every Spacing along x. A ray only ever needs to test
// the few beads near where it crosses that plane, so thousands of glass spheres cost no more than
// nine. Each bead is solid glass: rays refract in, pick up its tint, refract out and carry on to
// the next bead or the neon wall behind.
static const float Spacing = 0.2;
static const float BeadRadius = 0.058;
static const float BeadPitch = 0.165;
static const float Top = 1.9;
static const float WallZ = 1.3;
static const float FloorY = -1.45;

// How far a strand is pushed sideways and back at a given height: a slow breeze that swings the
// strands like pendulums, and ripples that run down them with the bass.
float2 swayAt(float strand, float y)
{
    float drop = Top - y;
    float breeze = 0.035 * sin(Time * 0.55 + strand * 0.21) + 0.02 * sin(Time * 1.3 + strand * 0.63) + 0.012 * sin(Time * 2.1 - strand * 0.4);
    float ripple = (0.012 + Bass * 0.03) * sin(drop * 5.5 - Time * 6.0 + strand * 0.35) * smoothstep(0.0, 1.0, drop);
    float x = breeze * drop * 0.6 + ripple;
    float z = 0.05 * sin(Time * 0.4 + strand * 0.27) * drop * 0.4 + ripple * 0.6;
    return float2(x, z);
}

// Strands end at different heights, giving the curtain a ragged hem.
float strandEnd(float strand)
{
    return -1.05 - hash(float2(strand, 1.3)) * 0.35;
}

float3 beadCentre(float strand, float k)
{
    float y = Top - (k + 0.5) * BeadPitch;
    float2 sway = swayAt(strand, y);
    return float3(strand * Spacing + sway.x, y, sway.y);
}

bool beadExists(float strand, float k)
{
    return k >= 0.0 && Top - (k + 0.5) * BeadPitch > strandEnd(strand);
}

// Bead glass comes in a handful of tints: clear, amber, rose, aqua and violet.
float3 beadTint(float strand, float k)
{
    float pick = hash(float2(strand * 1.7 + 3.0, k * 0.91 + 7.0));
    return pick < 0.3 ? float3(0.95, 0.97, 1.0) : (pick < 0.5 ? float3(1.0, 0.62, 0.22) : (pick < 0.7 ? float3(1.0, 0.4, 0.55) : (pick < 0.87 ? float3(0.3, 0.9, 0.95) : float3(0.62, 0.42, 1.0))));
}

float sphereEntry(float3 origin, float3 direction, float3 centre, float radius)
{
    float3 oc = origin - centre;
    float b = dot(oc, direction);
    float c = dot(oc, oc) - radius * radius;
    float h = b * b - c;
    if (h < 0.0)
    {
        return -1.0;
    }
    float t = -b - sqrt(h);
    return t > 1e-4 ? t : -1.0;
}

// The nearest bead along a ray, testing the nine around where it crosses the curtain.
// Returns the distance (or -1) and the bead's strand and number.
float findBead(float3 origin, float3 direction, out float strandHit, out float beadHit)
{
    strandHit = 0.0;
    beadHit = 0.0;
    if (abs(direction.z) < 1e-3)
    {
        return -1.0;
    }
    float t = -origin.z / direction.z;
    if (t < -0.2)
    {
        return -1.0;
    }
    float3 crossing = origin + direction * max(t, 0.0);
    // Allow for the sway: guess the strand, see how far it's swung, and look again.
    float guess = round(crossing.x / Spacing);
    float2 sway = swayAt(guess, crossing.y);
    crossing = origin + direction * max((sway.y - origin.z) / direction.z, 0.0);
    float strand0 = round((crossing.x - sway.x) / Spacing);
    float k0 = round((Top - crossing.y) / BeadPitch - 0.5);

    float best = 1e5;
    [unroll]
    for (int i = -1; i <= 1; i++)
    {
        [unroll]
        for (int j = -1; j <= 1; j++)
        {
            float strand = strand0 + i;
            float k = k0 + j;
            if (beadExists(strand, k))
            {
                float hit = sphereEntry(origin, direction, beadCentre(strand, k), BeadRadius);
                if (hit > 0.0 && hit < best)
                {
                    best = hit;
                    strandHit = strand;
                    beadHit = k;
                }
            }
        }
    }
    return best < 1e4 ? best : -1.0;
}

// The neon tubes on the wall: a big ring, a wave, and a stack of bars, each glowing with its own
// part of the spectrum, their colours slowly turning.
float3 tubeColour(int index)
{
    float level = spectrum(index == 0 ? 0.05 : (index == 1 ? 0.3 : 0.65));
    float hue = (index == 0 ? 0.92 : (index == 1 ? 0.5 : 0.08)) + sin(Time * 0.05 + index * 2.0) * 0.06 + level * 0.08;
    return hsv2rgb(frac(hue), 0.85, 1.0) * (1.2 + level * 4.0);
}

float tubeDistance(int index, float2 q)
{
    if (index == 0)
    {
        return abs(length(q - float2(-0.7, 0.25)) - 0.72);
    }
    if (index == 1)
    {
        float wave = -0.62 + sin(q.x * 3.2 + Time * 0.3) * 0.12;
        float strips = abs(abs(q.x) - 2.3) + max(abs(q.y - 0.2) - 1.1, 0.0);
        return min(abs(q.y - wave) * 0.8 + max(abs(q.x) - 2.3, 0.0), strips);
    }
    // A stack of chevrons.
    float2 r = q - float2(1.1, 0.25);
    float chevron = r.y + abs(r.x) * 0.6;
    float bars = abs(frac(chevron * 2.5) - 0.5) / 2.5;
    return max(bars, max(abs(r.x) - 0.5, abs(chevron) - 0.62));
}

float3 wallColour(float2 q)
{
    float3 light = 0.0;
    float3 colour = 0.0;
    [unroll]
    for (int i = 0; i < 3; i++)
    {
        float d = tubeDistance(i, q);
        float3 tube = tubeColour(i);
        // The glass tube itself, white-hot at its core, and its spill of light on the wall.
        colour += tube * smoothstep(0.022, 0.008, d) * 1.5 + lerp(tube, 1.0, 0.5) * smoothstep(0.009, 0.0, d) * 2.0;
        light += tube * (exp(-d * 1.8) * 0.2 + exp(-d * 10.0) * 0.35);
    }
    // Rough brick under the light.
    float course = floor(q.y * 7.0);
    float2 brick = float2(q.x * 3.5 + course * 0.5, q.y * 7.0);
    float mortar = max(smoothstep(0.42, 0.47, abs(frac(brick.x) - 0.5)), smoothstep(0.38, 0.45, abs(frac(brick.y) - 0.5)));
    float3 albedo = float3(0.42, 0.36, 0.34) * (0.7 + 0.5 * noise(q * 18.0)) * (0.8 + 0.4 * hash(floor(brick))) * (1.0 - mortar * 0.6);
    return colour + albedo * (light + 0.01);
}

// Everything that isn't a bead: the neon wall, the glossy floor that reflects it, and the dim room.
float3 background(float3 origin, float3 direction)
{
    float tWall = direction.z > 0.0 ? (WallZ - origin.z) / direction.z : 1e5;
    float tFloor = direction.y < 0.0 ? (FloorY - origin.y) / direction.y : 1e5;
    if (tWall < tFloor && tWall < 1e4)
    {
        float3 hit = origin + direction * tWall;
        return wallColour(hit.xy);
    }
    if (tFloor < 1e4)
    {
        // Polished dark wood: a blurred, faded mirror image of the wall.
        float3 hit = origin + direction * tFloor;
        float3 mirrored = float3(direction.x, -direction.y, direction.z);
        float fresnel = 0.04 + 0.96 * pow(1.0 - abs(direction.y), 5.0);
        float tBack = (WallZ - hit.z) / max(mirrored.z, 1e-3);
        float3 seen = hit + mirrored * tBack;
        float3 reflected = mirrored.z > 0.0 ? wallColour(seen.xy) : 0.0;
        float3 wood = float3(0.05, 0.03, 0.02) * (0.7 + 0.6 * noise(float2(hit.x * 2.0, hit.z * 30.0)));
        return wood * 0.05 + reflected * (0.15 + fresnel * 0.6);
    }
    // Behind the camera: a dim room, with a faint warm doorway for the beads to catch.
    float doorway = smoothstep(0.35, 0.3, abs(direction.x + 0.3)) * smoothstep(-0.2, 0.0, direction.y) * smoothstep(0.6, 0.5, direction.y);
    return float3(0.02, 0.016, 0.025) * (1.0 + direction.y) + float3(1.0, 0.75, 0.5) * doorway * 0.35;
}

float fresnel(float cosine)
{
    return 0.04 + 0.96 * pow(1.0 - saturate(cosine), 5.0);
}

float3 trace(float3 origin, float3 direction)
{
    float3 colour = 0.0;
    float3 throughput = 1.0;
    [loop]
    for (int bounce = 0; bounce < 3; bounce++)
    {
        float strand;
        float k;
        float t = findBead(origin, direction, strand, k);
        if (t < 0.0)
        {
            // The thread the beads hang on, glinting between them.
            if (bounce == 0)
            {
                float tCurtain = -origin.z / direction.z;
                float3 crossing = origin + direction * tCurtain;
                float nearest = round(crossing.x / Spacing);
                float2 sway = swayAt(nearest, crossing.y);
                float offset = abs(crossing.x - nearest * Spacing - sway.x);
                float thread = smoothstep(0.005, 0.002, offset) * step(strandEnd(nearest), crossing.y);
                float3 behind = background(origin, direction);
                return colour + throughput * lerp(behind, float3(0.05, 0.045, 0.04) + behind * 0.3, thread);
            }
            colour += throughput * background(origin, direction);
            return colour;
        }

        float3 centre = beadCentre(strand, k);
        float3 position = origin + direction * t;
        float3 normal = normalize(position - centre);
        float cosine = dot(normal, -direction);
        float reflectance = fresnel(cosine);
        // The mirror glint off the outside of the bead.
        colour += throughput * reflectance * background(position, reflect(direction, normal));

        // Through the glass: in, across (taking on its tint), and out the far side.
        float3 inside = refract(direction, normal, 1.0 / 1.52);
        float across = -2.0 * dot(position - centre, inside);
        float3 exitPoint = position + inside * max(across, 0.0);
        float3 exitNormal = normalize(exitPoint - centre);
        float3 outside = refract(inside, -exitNormal, 1.52);
        if (dot(outside, outside) < 0.5)
        {
            outside = reflect(inside, -exitNormal);
        }
        float3 tint = beadTint(strand, k);
        throughput *= (1.0 - reflectance) * pow(tint, max(across, 0.0) * 18.0);
        origin = exitPoint + outside * 2e-3;
        direction = outside;
    }
    colour += throughput * background(origin, direction);
    return colour;
}

// Where the two rays per pixel go on High and Ultra, in pixels.
static const float2 SampleOffsets[2] = { float2(0.25, -0.25), float2(-0.25, 0.25) };

float3 Render(float2 uv, float2 p)
{
    float3 eye = float3(0.9 + sin(Time * 0.045) * 0.9, 0.1 + sin(Time * 0.031) * 0.2, -2.1 + sin(Time * 0.02) * 0.2);
    float3 target = float3(sin(Time * 0.03) * 0.3 - 0.1, -0.05, 0.5);
    const float zoom = 1.45;

    int samples = Quality > 1.5 ? 2 : 1;
    float3 colour = 0.0;
    [loop]
    for (int s = 0; s < samples; s++)
    {
        float2 jitter = samples > 1 ? SampleOffsets[s] * pixelSize() : 0.0;
        colour += trace(eye, cameraRay(eye, target, float2(p.x, -p.y) + jitter, zoom, 0.0));
    }
    return colour / samples;
}
