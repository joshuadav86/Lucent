// Name: Fibre Optic Braid
// Flying down the hollow heart of a cable braided from ropes of ropes of glowing optical fibres, with spectrum light racing along every strand.
// Family: Fly-through
// Tags: Colourful, Neon, 3D

static const float BraidTwoPi = 6.2831853;

float2 braidPath(float z)
{
    return float2(sin(z * 0.13) * 0.25, cos(z * 0.1) * 0.2);
}

// The ropes' twist along the cable, tightening and loosening as we go.
float braidTwist(float z)
{
    return z * 0.22 + 1.5 * sin(z * 0.05) + Time * 0.05;
}

// How far the inner ropes stand from the axis: the cable swells and narrows.
float braidSpread(float z)
{
    return 1.7 + 0.25 * sin(z * 0.07 + 1.0);
}

// atan2 from a short polynomial (accurate to about 0.001 radians), cheaper than the built-in.
float braidAngle(float2 q)
{
    float2 a = abs(q);
    float ratio = min(a.x, a.y) / max(max(a.x, a.y), 1e-8);
    float s = ratio * ratio;
    float r = ((-0.0464964749 * s + 0.15931422) * s - 0.327622764) * s * ratio + ratio;
    r = a.y > a.x ? 1.5707963 - r : r;
    r = q.x < 0.0 ? PI - r : r;
    return q.y < 0.0 ? -r : r;
}

// One level of the braid: turns q by the level's twist, folds it into one of count strands
// and moves to that strand's centre. strand returns which strand it was.
float2 braidLevel(float2 q, float twist, float count, float radius, out float strand)
{
    float sector = BraidTwoPi / count;
    strand = floor((braidAngle(q) + twist) / sector + 0.5);
    float turn = twist - strand * sector;
    strand = strand - count * floor(strand / count);
    float s = 0.0;
    float c = 0.0;
    sincos(turn, s, c);
    return float2(c * q.x - s * q.y - radius, s * q.x + c * q.y);
}

// Distance to the braid: an inner ring of 7 ropes around the axis and an outer ring of 11 larger
// ropes twisting the other way; each rope is 6 strands, each strand 6 fibres, every level
// twisting against the one above. ids returns the rope (outer ropes are numbered 7 and up),
// strand and fibre numbers.
float braidDE(float3 pos, float widen, out float3 ids)
{
    float2 q = pos.xy - braidPath(pos.z);
    bool outer = dot(q, q) > 3.0 * 3.0;
    float size = outer ? 1.7 : 1.0;
    float spin = outer ? -0.6 : 1.0;
    ids = 0.0;
    float rope = 0.0;
    float strand = 0.0;
    float fibre = 0.0;
    q = braidLevel(q, braidTwist(pos.z) * spin, outer ? 11.0 : 7.0, outer ? 4.5 : braidSpread(pos.z), rope);
    rope += outer ? 7.0 : 0.0;
    q /= size;
    float envelope = length(q) - 0.48;
    if (envelope > 0.1)
    {
        ids = float3(rope, 0.0, 0.0);
        return envelope * 0.85 * size;
    }
    q = braidLevel(q, (-pos.z * 1.1 - Time * 0.2) * spin, 6.0, 0.3, strand);
    envelope = length(q) - 0.14;
    if (envelope > 0.04)
    {
        ids = float3(rope, strand, 0.0);
        return envelope * 0.85 * size;
    }
    ids = float3(rope, strand, -1.0);
    // Normal quality draws each strand as a smooth tube with its fibres painted on (see Render);
    // higher settings model the six fibres themselves.
    if (Quality < 1.5)
    {
        return (length(q) - 0.115 - widen / size) * 0.85 * size;
    }
    q = braidLevel(q, (pos.z * 3.6 + Time * 0.6) * spin, 6.0, 0.09, fibre);
    ids = float3(rope, strand, fibre);
    return (length(q) - 0.03 - widen / size) * 0.8 * size;
}

float3 Render(float2 uv, float2 p)
{
    float z = Time * 0.9 + (BeatCount - Beat) * 0.25;
    float3 eye = float3(braidPath(z), z);
    float3 dir = cameraRay(eye, float3(braidPath(z + 3.0), z + 3.0), p, 0.8 - Beat * 0.07, sin(Time * 0.06) * 2.0);

    const float farPlane = 14.0;
    int maxSteps = qualitySteps(64);
    float pixelAngle = 0.7 / (Resolution.y * 0.8);
    float travelled = 0.0;
    float3 ids = 0.0;
    float haze = 0.0;
    float hazeBand = 0.0;
    bool landed = false;
    [loop]
    for (int i = 0; i < maxSteps; i++)
    {
        float d = braidDE(eye + dir * travelled, travelled * pixelAngle, ids);
        haze += exp(-d * 25.0) * exp(-travelled * 0.12) * (ids.x < 6.5 ? 1.0 : 0.5);
        hazeBand += exp(-d * 25.0) * exp(-travelled * 0.12) * frac((ids.x + 0.5) / 7.0);
        if (d < 0.001 * travelled)
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

    float flow = Time * 0.04;
    float3 colour = 0.0;
    if (landed)
    {
        float3 hit = eye + dir * travelled;
        float3 i2 = 0.0;
        float widen = travelled * pixelAngle;
        float e1 = 0.0004 + 0.0008 * travelled;
        float2 e = float2(e1, -e1);
        float3 normal = normalize(e.xyy * braidDE(hit + e.xyy, widen, i2) + e.yyx * braidDE(hit + e.yyx, widen, i2)
                                + e.yxy * braidDE(hit + e.yxy, widen, i2) + e.xxx * braidDE(hit + e.xxx, widen, i2));
        // On a smooth strand, paint its six twisting fibres: the angle around the strand picks
        // the fibre, with a dark groove between neighbours.
        float groove = 1.0;
        if (ids.z < 0.0)
        {
            float2 local = hit.xy - braidPath(hit.z);
            bool outerRope = ids.x > 6.5;
            float size = outerRope ? 1.7 : 1.0;
            float spin = outerRope ? -0.6 : 1.0;
            float r0 = 0.0;
            float s0 = 0.0;
            local = braidLevel(local, braidTwist(hit.z) * spin, outerRope ? 11.0 : 7.0, outerRope ? 4.5 : braidSpread(hit.z), r0) / size;
            local = braidLevel(local, (-hit.z * 1.1 - Time * 0.2) * spin, 6.0, 0.3, s0);
            float around = (braidAngle(local) + (hit.z * 3.6 + Time * 0.6) * spin) * 6.0 / BraidTwoPi;
            ids.z = frac(floor(around) / 6.0) * 6.0;
            float edgeDist = abs(frac(around) - 0.5) * 2.0;
            groove = 0.25 + 0.75 * smoothstep(1.0, 0.6, edgeDist);
        }
        float facing = saturate(dot(normal, -dir));

        // Each rope carries one band of the spectrum; its strands and fibres vary the colour a little.
        float band = frac((ids.x + 0.5) / 7.0);
        float energy = spectrum(band);
        float hue = band + ids.y * 0.03 + ids.z * 0.015 + flow;
        float3 tint = hsv2rgb(hue, 0.8, 1.0);

        // Glassy fibres: a faint body, a sharp highlight and bright pulses of light racing along
        // each one at its own pace, towards us in the inner ropes and away in the outer ones.
        float seed = frac(sin(dot(ids, float3(12.9898, 78.233, 37.719))) * 43758.5453);
        float direction = ids.x < 6.5 ? 1.0 : -1.0;
        float pulse = pow(0.5 + 0.5 * sin(hit.z * 0.9 + Time * (5.0 + seed * 5.0) * direction + seed * 40.0), 6.0);
        float shock = glow(hit.z - z - (1.0 - Beat) * 14.0, 1.5) * Beat;
        float3 toLamp = normalize(float3(0.4, -0.6, -0.5));
        float specular = pow(saturate(dot(reflect(dir, normal), toLamp)), 30.0);
        float core = facing * facing;
        colour = tint * (0.05 + facing * 0.1 + energy * 0.25) * groove + specular * 0.35
               + lerp(tint, 1.0, core * 0.4) * (pulse * (0.6 + energy * 3.0) + shock * 1.5) * (0.3 + core) * groove
               + tint * pow(1.0 - facing, 4.0) * (0.15 + energy * 0.4);
        colour *= exp(-travelled * 0.1);
    }
    // A haze of light between the ropes.
    colour += hsv2rgb(hazeBand / max(haze, 1e-4) + flow, 0.75, 1.0) * haze * 0.006 * (0.5 + Bass);
    return colour + previous(uv) * 0.1;
}
