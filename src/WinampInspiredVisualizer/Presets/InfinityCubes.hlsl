// Name: Infinity Cubes
// Falling forever into nested, counter-rotating cube frames whose edges stream with light like an infinity mirror.
// Family: Fly-through
// Tags: Neon, Geometric, 3D
// Added: 2026-10-16 23:54

static const float BeamWidth = 0.03;      // edge thickness relative to the frame's size
static const float NodeRadius = 0.075;    // corner nodes, relative to the frame's size

// Distance to the edges of a cube frame of half-size 1 (after Inigo Quilez's box frame),
// plus glowing nodes on its corners. part returns 1 on a node, 0 on a beam.
float cubesFrame(float3 p, out float part)
{
    float3 a = abs(p);
    float node = length(a - 1.0) - NodeRadius;
    p = a - 1.0;
    float3 q = abs(p + BeamWidth) - BeamWidth;
    float beam = min(min(length(max(float3(p.x, q.y, q.z), 0.0)) + min(max(p.x, max(q.y, q.z)), 0.0),
                         length(max(float3(q.x, p.y, q.z), 0.0)) + min(max(q.x, max(p.y, q.z)), 0.0)),
                         length(max(float3(q.x, q.y, p.z), 0.0)) + min(max(q.x, max(q.y, p.z)), 0.0));
    part = node < beam ? 1.0 : 0.0;
    return min(beam, node);
}

static const float SpinStep = 0.55;       // extra twist from one level to the next

// Rotates xy by the angle whose cosine and sine are cs.
float2 cubesSpin(float2 v, float2 cs)
{
    return float2(cs.x * v.x - cs.y * v.y, cs.y * v.x + cs.x * v.y);
}

// zoomWhole/zoomFrac: levels already passed and growth within the current one.
// motion: x = spin angle, y/z = cos/sin of the tilt. Neighbouring levels spin in opposite directions,
// so each level's rotation follows from its neighbour's by a fixed step: stepCs holds the cosine and
// sine of that step for a forward-spinning level (xy) and a backward-spinning one (zw).
// level returns the absolute index of the nearest frame, local the point in that frame's own space.
float cubesDE(float3 q, float zoomWhole, float zoomFrac, float3 motion, float4 stepCs, out float level, out float3 local, out float part)
{
    float r = max(max(abs(q.x), abs(q.y)), abs(q.z));
    float rel = floor(log2(max(r, 1e-4)) - zoomFrac) - 1.0;
    float absolute = rel - zoomWhole;
    float direction = frac(absolute * 0.5) < 0.25 ? 1.0 : -1.0;
    float spin = absolute * SpinStep + motion.x * direction;
    float2 cs = float2(cos(spin), sin(spin));
    float best = 1e5;
    level = 0.0;
    local = q;
    part = 0.0;
    [unroll]
    for (int j = 0; j < 3; j++)
    {
        float size = exp2(rel + j + zoomFrac);
        float3 lp = q;
        lp.xy = cubesSpin(lp.xy, cs);
        lp.yz = cubesSpin(lp.yz, float2(motion.y, motion.z * direction));
        lp /= size;
        float pt = 0.0;
        float d = cubesFrame(lp, pt) * size;
        if (d < best)
        {
            best = d;
            level = absolute + j;
            local = lp;
            part = pt;
        }
        // Step to the next level: one more twist, and the spin and tilt reverse.
        cs = cubesSpin(cs, direction > 0.0 ? stepCs.xy : stepCs.zw);
        direction = -direction;
    }
    return best;
}

float3 Render(float2 uv, float2 p)
{
    // Endless inward fall: the frames grow continuously; each beat adds a quick surge.
    float zoom = Time * 0.22 + (BeatCount - Beat) * 0.08;
    float zoomWhole = floor(zoom);
    float zoomFrac = zoom - zoomWhole;

    // Every level spins (neighbours in opposite directions) and tips slightly.
    float tilt = sin(Time * 0.07) * 0.22;
    float3 motion = float3(Time * 0.14, cos(tilt), sin(tilt));
    float4 stepCs = float4(cos(SpinStep - 2.0 * motion.x), sin(SpinStep - 2.0 * motion.x),
                           cos(SpinStep + 2.0 * motion.x), sin(SpinStep + 2.0 * motion.x));

    // The camera hangs off the axis, slowly circling, always looking at the vanishing centre.
    float orbit = Time * 0.05;
    float3 eye = float3(sin(orbit) * 0.32, cos(orbit) * 0.32, -1.0) * 1.6;
    float3 dir = cameraRay(eye, float3(0.0, 0.0, 0.0), p, 1.1 - Beat * 0.08, Time * 0.03);

    const float farPlane = 12.0;
    int maxSteps = qualitySteps(90);
    float travelled = 0.0;
    float level = 0.0;
    float3 local = 0.0;
    float part = 0.0;
    float glowSum = 0.0;
    float glowLevel = 0.0;
    bool landed = false;
    [loop]
    for (int i = 0; i < maxSteps; i++)
    {
        float3 pos = eye + dir * travelled;
        float d = cubesDE(pos, zoomWhole, zoomFrac, motion, stepCs, level, local, part);
        float g = exp(-d * 60.0 / (0.3 + length(pos)));
        glowSum += g;
        glowLevel += g * level;
        if (d < 0.0005 * travelled)
        {
            landed = true;
            break;
        }
        travelled += d * 0.9;
        if (travelled > farPlane)
        {
            break;
        }
    }

    float flow = Time * 0.1;
    float3 colour = 0.0;
    if (landed)
    {
        float3 hit = eye + dir * travelled;
        float s2 = 0.0;
        float3 l2 = 0.0;
        float p2 = 0.0;
        float e1 = 0.0002 + 0.001 * travelled;
        float2 e = float2(e1, -e1);
        float3 normal = normalize(e.xyy * cubesDE(hit + e.xyy, zoomWhole, zoomFrac, motion, stepCs, s2, l2, p2) + e.yyx * cubesDE(hit + e.yyx, zoomWhole, zoomFrac, motion, stepCs, s2, l2, p2)
                                + e.yxy * cubesDE(hit + e.yxy, zoomWhole, zoomFrac, motion, stepCs, s2, l2, p2) + e.xxx * cubesDE(hit + e.xxx, zoomWhole, zoomFrac, motion, stepCs, s2, l2, p2));

        float hue = level * 0.13 - flow;
        float energy = spectrum(frac(level * 0.137));

        // Dark polished metal with a sharp highlight and coloured rims.
        float facing = saturate(dot(normal, -dir));
        float fresnel = pow(1.0 - facing, 4.0);
        float3 light = normalize(float3(0.3, -0.6, -0.7));
        float specular = pow(saturate(dot(reflect(dir, normal), light)), 30.0);
        float3 tint = neon(hue);

        // Light streams along every edge in dashes; the corner nodes pulse with the music.
        float3 a = abs(local);
        float along = a.x < a.y ? (a.x < a.z ? local.x : local.z) : (a.y < a.z ? local.y : local.z);
        float dashes = glow(abs(frac(along * 2.0 - Time * 0.8 + level * 0.3) - 0.5), 9.0);
        float3 led = palette(hue + along * 0.15) * dashes * (0.4 + energy * 2.0);
        float3 node = neon(hue + 0.5) * (0.5 + energy * 1.2 + Beat * 0.8);

        colour = tint * (0.03 + facing * 0.1) + specular * (0.4 + Treble) + tint * fresnel * (0.5 + energy)
               + lerp(led, node, part);
        colour *= exp(-max(travelled - 2.0, 0.0) * 0.12);
    }

    // The beams glow like neon, each level in its own colour and spectrum band.
    float avgLevel = glowLevel / max(glowSum, 1e-4);
    float3 haloColour = lerp(neon(avgLevel * 0.13 - flow), palette(avgLevel * 0.13 - flow), 0.3);
    colour += haloColour * glowSum * 0.01 * (0.5 + spectrum(frac(avgLevel * 0.137)) * 1.5) * (0.6 + Bass * 0.8);

    // The infinitely small heart of the zoom shines through.
    float centre = length(cross(dir, -eye));
    colour += hsv2rgb(flow * 0.3, 0.4, 1.0) * 0.012 / (centre * centre + 0.004) * (0.6 + Bass * 0.6);
    // An infinity-mirror echo: the last frame, pulled slightly towards the centre, trails outward.
    return colour + previous(toUv(p * 0.985)) * (0.3 + Bass * 0.08);
}
