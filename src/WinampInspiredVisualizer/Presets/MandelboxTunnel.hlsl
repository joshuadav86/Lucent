// Name: Mandelbox Tunnel
// Cruising down a round corridor carved through a morphing Mandelbox fractal of iridescent rings, domes and glowing seams.
// Family: Fly-through
// Tags: Intense, 3D

static const float NaveRadius = 0.8;
static const float WorldScale = 0.25;
static const float FoldPeriod = 4.0;

float boxTunnelBox(float3 p, float3 b)
{
    float3 q = abs(p) - b;
    return length(max(q, 0.0)) + min(max(q.x, max(q.y, q.z)), 0.0);
}

float2 boxTunnelPath(float z)
{
    return float2(sin(z * 0.19) * 1.1, cos(z * 0.13) * 0.7);
}

// The Mandelbox folds, mirrored around the corridor and repeating along it.
// trap returns which fold level came closest to the centre, so every plate gets its own tint.
// shape: x = fold scale, y = 1 / minimum radius squared, zw = offset tweaks.
float boxTunnelFractal(float3 w, float4 shape, out float trap)
{
    float3 p = w * WorldScale;
    p.xy = abs(p.xy);
    p.z = abs(wrap(p.z, FoldPeriod) - FoldPeriod * 0.5);
    float3 offset = float3(1.1 + shape.z, 1.1, 1.1 + shape.w);

    float dr = 1.0;
    float closest = 10.0;
    trap = 0.0;
    [unroll]
    for (int i = 0; i < 4; i++)
    {
        p = clamp(p, -1.0, 1.0) * 2.0 - p;            // box fold
        float r2 = dot(p, p);
        if (r2 < closest)
        {
            closest = r2;
            trap = i;
        }
        float k = clamp(1.0 / max(r2, 1e-6), 1.0, shape.y);   // sphere fold
        p *= k;
        dr *= k;
        p = p * shape.x - offset;
        dr *= shape.x;
    }
    // Higher quality settings add finer folds.
    if (Quality > 1.5)
    {
        int extra = qualitySteps(4) - 4;
        [loop]
        for (int j = 0; j < extra; j++)
        {
            p = clamp(p, -1.0, 1.0) * 2.0 - p;
            float k = clamp(1.0 / max(dot(p, p), 1e-6), 1.0, shape.y);
            p = p * (k * shape.x) - offset;
            dr *= k * shape.x;
        }
    }
    return boxTunnelBox(p, float3(1.0, 1.0, 1.0)) / (dr * WorldScale);
}

// The corridor's radius breathes along its length, so narrow gates recede into the distance.
float boxTunnelRadius(float z)
{
    return NaveRadius + sin(z * 0.9) * 0.2;
}

float boxTunnelDE(float3 pos, float4 shape, out float trap)
{
    float3 w = pos;
    w.xy -= boxTunnelPath(pos.z);
    float nave = (boxTunnelRadius(pos.z) - length(w.xy)) * 0.9;
    trap = 1.0;
    // Inside the open corridor the wall distance is already a safe step: skip the fractal.
    if (nave > 0.06)
    {
        return nave;
    }
    return max(boxTunnelFractal(w, shape, trap), nave);
}

float3 Render(float2 uv, float2 p)
{
    // Steady cruise plus a smooth surge after every beat.
    float z = Time * 0.55 + (BeatCount - Beat) * 0.12;
    float3 eye = float3(boxTunnelPath(z), z);
    float3 ahead = float3(boxTunnelPath(z + 1.6), z + 1.6);
    float roll = sin(Time * 0.11) * 0.6 + (boxTunnelPath(z + 1.0).x - eye.x) * 0.8;
    float3 dir = cameraRay(eye, ahead, p, 0.95 - Beat * 0.08, roll);

    // Slowly morph the fold scale and strength so the architecture keeps reshaping itself.
    float4 shape = float4(2.2 + sin(Time * 0.071) * 0.12, 1.0 / (0.32 + sin(Time * 0.053 + 1.0) * 0.08),
                          sin(Time * 0.043) * 0.08, cos(Time * 0.037) * 0.08);

    const float farPlane = 14.0;
    int maxSteps = qualitySteps(84);
    float hitScale = 0.0028 - Quality * 0.0005;   // finer surfaces at higher quality
    float travelled = 0.0;
    float trap = 0.0;
    bool landed = false;
    [loop]
    for (int i = 0; i < maxSteps; i++)
    {
        float d = boxTunnelDE(eye + dir * travelled, shape, trap);
        if (d < hitScale * travelled)
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
    travelled = landed ? travelled : farPlane;

    float flow = Time * 0.05;
    float3 lampPos = float3(boxTunnelPath(z + 3.5), z + 3.5);
    float3 lampColour = hsv2rgb(flow * 0.7 + 0.1, 0.18, 1.0) * (1.3 + Bass * 1.0);
    float3 colour = 0.0;
    if (landed)
    {
        float3 hit = eye + dir * travelled;
        float tr = 0.0;
        float e1 = 0.0005 + hitScale * travelled;
        float2 e = float2(e1, -e1);
        float3 normal = normalize(e.xyy * boxTunnelDE(hit + e.xyy, shape, tr) + e.yyx * boxTunnelDE(hit + e.yyx, shape, tr)
                                + e.yxy * boxTunnelDE(hit + e.yxy, shape, tr) + e.xxx * boxTunnelDE(hit + e.xxx, shape, tr));
        // Where the corridor slices through solid fractal, the cut face becomes a glowing membrane.
        float3 local = hit;
        local.xy -= boxTunnelPath(hit.z);
        float cutFace = step(boxTunnelFractal(local, shape, trap), (boxTunnelRadius(hit.z) - length(local.xy)) * 0.9 + 0.003);

        // Ambient occlusion from two probes along the normal.
        float occlusion = saturate(1.0 - ((0.05 - boxTunnelDE(hit + normal * 0.05, shape, tr))
                                        + (0.12 - boxTunnelDE(hit + normal * 0.12, shape, tr)) * 0.5) * 5.0);

        float3 toLamp = lampPos - hit;
        float lampDist = length(toLamp);
        toLamp /= lampDist;
        float lampFall = 1.0 / (1.0 + lampDist * lampDist * 0.45);
        float diffuse = saturate(dot(normal, toLamp));
        float facing = saturate(dot(normal, -dir));
        float specular = pow(saturate(dot(normal, normalize(toLamp - dir))), 50.0);
        float fresnel = pow(1.0 - facing, 3.0);

        // Colour flows down the tunnel in spiralling bands; each plate is tinted by its fold level.
        float2 around = hit.xy - boxTunnelPath(hit.z);
        float spiral = atan2(around.y, around.x + 1e-5) / (2.0 * PI);
        float hue = hit.z * 0.045 - flow + spiral * 0.5 + trap * 0.13;
        float3 tint = lerp(neon(hue), palette(hue), 0.3);
        float3 plate = tint * (0.02 + diffuse * lampFall * lampColour * 1.4 + facing * 0.03) * occlusion;

        // Rings of coloured light sweep along the structure towards the camera, one per spectrum band.
        float ringPhase = hit.z * 0.3 - Time * 0.45;
        float ringBand = frac(floor(ringPhase) * 0.137);
        float rings = glow(abs(frac(ringPhase) - 0.5), 30.0) * (0.15 + spectrum(ringBand) * 1.5) * (0.6 + Bass * 0.8);

        // Light pours out of the deep crevices in the colour of its part of the spectrum.
        float energy = spectrum(frac(hue * 0.7));
        float hidden = 1.0 - occlusion;
        float3 crevice = neon(hue + 0.35) * hidden * hidden * hidden * (0.2 + energy * 1.6) * (0.6 + Bass);

        // The membranes are dark glass etched with a fine lattice of light that streams along the tunnel.
        float membraneLines = glow(abs(frac(hit.z * 4.0 + spiral * 12.0 - Time * 0.6) - 0.5), 14.0)
                            + glow(abs(frac(hit.z * 4.0 - spiral * 12.0) - 0.5), 14.0);
        float3 membrane = plate * 0.12 + neon(hue + 0.15) * (0.015 + membraneLines * (0.2 + Mid * 0.6))
                        + neon(hue + 0.5) * fresnel * 0.3;
        plate = lerp(plate, membrane, cutFace);

        colour = plate
               + specular * lampFall * lampColour * (0.7 + Treble * 2.0) * occlusion
               + neon(hue + 0.5) * fresnel * 0.15 * occlusion
               + palette(ringBand + flow) * rings * (0.2 + fresnel * 0.8) * (0.35 + occlusion * 0.65)
               + crevice;
        colour *= exp(-travelled * 0.16);
    }

    // Deep violet haze, and the lamp itself glowing in the air when nothing hides it.
    // Far away everything dissolves into a glowing violet haze: light at the end of the tunnel.
    float3 haze = hsv2rgb(0.72 + flow * 0.3, 0.65, 0.06 + Bass * 0.04);
    float distant = landed ? smoothstep(4.0, farPlane, travelled) : 1.0;
    colour = lerp(colour, haze * 2.0, distant) + haze * (1.0 - exp(-travelled * 0.16)) * 0.5;
    float3 toOrb = lampPos - eye;
    float orbDepth = dot(toOrb, dir);
    float orbMiss = length(toOrb - dir * orbDepth);
    float orb = (orbDepth > 0.0 && orbDepth < travelled) ? 0.004 / (orbMiss * orbMiss + 0.002) : 0.0;
    colour += lampColour * orb * 0.25;
    return colour + previous(uv) * 0.12;
}
