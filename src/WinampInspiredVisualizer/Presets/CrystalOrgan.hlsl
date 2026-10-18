// Name: Crystal Organ
// A great pipe organ of glass and chrome stands in a dark hall, each glass pipe filling with coloured light as its own part of the music swells, mirrored in a polished black floor and caught again in every chrome pipe beside it.
// Family: Ray traced
// Tags: Geometric, Colourful, 3D
// Added: 2026-10-17 17:12

// Fifteen pipes stand in a gentle arc on a chrome plinth, tallest in the middle. The glass ones are
// solid crystal rods with a column of glowing gas inside that rises and falls with the pipe's band
// of the spectrum (the bass in the middle, the treble at the ends); the chrome ones between them
// are mirrors. Rays bounce between the pipes and the polished floor, and refract through the glass.
static const int Pipes = 15;
static const float PipeGap = 0.34;
static const float PlinthTop = 0.32;

// Materials.
static const int Hall = 0;
static const int Floor = 1;
static const int Chrome = 2;
static const int Glass = 3;
static const int Plinth = 4;

struct Hit
{
    float distance;
    float3 normal;
    int material;
    int pipe;
};

float pipeX(int i)
{
    return (i - (Pipes - 1) * 0.5) * PipeGap;
}

float pipeZ(int i)
{
    float x = pipeX(i);
    return x * x * 0.07;
}

float pipeRadius(int i)
{
    return 0.115 - abs(i - (Pipes - 1) * 0.5) * 0.0045;
}

float pipeTop(int i)
{
    float fromMiddle = abs(i - (Pipes - 1) * 0.5);
    return PlinthTop + 2.9 - fromMiddle * 0.25 + (fromMiddle > 0.5 ? 0.08 * sin(fromMiddle * 2.0) : 0.0);
}

bool isGlass(int i)
{
    return ((i + (Pipes - 1) / 2) & 1) == 0;
}

// Which part of the spectrum a pipe sings: bass in the middle, treble at the two ends.
float pipeBand(int i)
{
    return abs(i - (Pipes - 1) * 0.5) / ((Pipes - 1) * 0.5);
}

float pipeLevel(int i)
{
    float level = spectrum(0.02 + pipeBand(i) * 0.7);
    return saturate(level * 1.15);
}

float3 pipeColour(int i)
{
    float hue = 0.98 - pipeBand(i) * 0.72 + sin(Time * 0.03) * 0.04;
    return hsv2rgb(frac(hue), 0.8, 1.0);
}

// How high the glowing gas fills a glass pipe.
float fillHeight(int i)
{
    return lerp(PlinthTop + 0.15, pipeTop(i) - 0.05, 0.12 + pipeLevel(i) * 0.88);
}

// Ray against an upright capped cylinder. Returns the distance and the outward normal.
float cylinderHit(float3 origin, float3 direction, float2 centre, float radius, float bottom, float top, out float3 normal)
{
    normal = float3(0.0, 1.0, 0.0);
    float2 o = origin.xz - centre;
    float2 d = direction.xz;
    float a = dot(d, d);
    float b = dot(o, d);
    float c = dot(o, o) - radius * radius;
    float h = b * b - a * c;
    if (h < 0.0 || a < 1e-8)
    {
        return -1.0;
    }
    float t = (-b - sqrt(h)) / a;
    float y = origin.y + direction.y * t;
    if (t > 1e-3 && y > bottom && y < top)
    {
        normal = normalize(float3(o.x + d.x * t, 0.0, o.y + d.y * t));
        return t;
    }
    // The flat top.
    if (direction.y < 0.0)
    {
        float tc = (top - origin.y) / direction.y;
        float2 q = o + d * tc;
        if (tc > 1e-3 && dot(q, q) < radius * radius)
        {
            normal = float3(0.0, 1.0, 0.0);
            return tc;
        }
    }
    return -1.0;
}

// From inside a cylinder, how far until the ray leaves it through the side.
float cylinderExit(float3 origin, float3 direction, float2 centre, float radius)
{
    float2 o = origin.xz - centre;
    float2 d = direction.xz;
    float a = max(dot(d, d), 1e-8);
    float b = dot(o, d);
    float c = dot(o, o) - radius * radius;
    float h = max(b * b - a * c, 0.0);
    return max((-b + sqrt(h)) / a, 0.0);
}

float boxHit(float3 origin, float3 direction, float3 centre, float3 size, out float3 normal)
{
    float3 m = 1.0 / direction;
    float3 n = m * (origin - centre);
    float3 k = abs(m) * size;
    float3 t1 = -n - k;
    float3 t2 = -n + k;
    float entry = max(max(t1.x, t1.y), t1.z);
    float leave = min(min(t2.x, t2.y), t2.z);
    normal = -sign(direction) * step(t1.yzx, t1.xyz) * step(t1.zxy, t1.xyz);
    return (entry > leave || entry < 1e-3) ? -1.0 : entry;
}

Hit traceScene(float3 origin, float3 direction)
{
    Hit hit;
    hit.distance = 1e5;
    hit.normal = float3(0.0, 1.0, 0.0);
    hit.material = Hall;
    hit.pipe = 0;

    if (direction.y < 0.0)
    {
        hit.distance = -origin.y / direction.y;
        hit.material = Floor;
    }

    float3 normal;
    float t = boxHit(origin, direction, float3(0.0, PlinthTop * 0.5, 0.2), float3(PipeGap * Pipes * 0.5 + 0.1, PlinthTop * 0.5, 0.45), normal);
    if (t > 0.0 && t < hit.distance)
    {
        hit.distance = t;
        hit.normal = normal;
        hit.material = Plinth;
    }

    [loop]
    for (int i = 0; i < Pipes; i++)
    {
        t = cylinderHit(origin, direction, float2(pipeX(i), pipeZ(i)), pipeRadius(i), PlinthTop - 0.01, pipeTop(i), normal);
        if (t > 0.0 && t < hit.distance)
        {
            hit.distance = t;
            hit.normal = normal;
            hit.material = isGlass(i) ? Glass : Chrome;
            hit.pipe = i;
        }
    }
    return hit;
}

// The dark hall: tall arched windows of cold moonlight far behind the organ, faint warm candle
// light behind the viewer, and a few lamps high above for the chrome to catch.
float3 hall(float3 direction)
{
    float3 colour = float3(0.006, 0.006, 0.01) + float3(0.01, 0.008, 0.014) * saturate(direction.y + 0.3);
    float around = atan2(direction.x, direction.z);
    float across = frac(around * 2.2 + 0.5) - 0.5;
    float arches = smoothstep(0.06, 0.05, abs(across));
    float archTop = 0.32 + 0.06 * sqrt(saturate(1.0 - across * across / 0.0036));
    float window = arches * smoothstep(0.04, 0.05, direction.y) * smoothstep(archTop, archTop - 0.005, direction.y) * step(0.0, direction.z);
    // Leaded panes.
    float panes = max(smoothstep(0.9, 0.97, cos(across * 160.0)), smoothstep(0.9, 0.97, cos(direction.y * 200.0)));
    colour += float3(0.2, 0.28, 0.5) * window * (0.25 - panes * 0.15);
    colour += float3(1.0, 0.6, 0.3) * smoothstep(0.2, 0.9, -direction.z) * 0.02 * (1.0 - abs(direction.y));
    float lamps = smoothstep(0.985, 0.995, cos((around - 0.4) * 3.0)) * smoothstep(0.55, 0.65, direction.y) * smoothstep(0.8, 0.7, direction.y);
    colour += float3(1.0, 0.85, 0.65) * lamps * 1.5;
    // A big soft light overhead and two tall light strips behind the viewer, for the chrome.
    colour += float3(0.9, 0.9, 1.0) * smoothstep(0.82, 0.9, direction.y) * 0.5;
    float strips = smoothstep(0.03, 0.01, abs(abs(around) - 2.3)) * smoothstep(-0.1, 0.0, direction.y) * smoothstep(0.7, 0.6, direction.y);
    float panels = smoothstep(0.5, 0.2, abs(abs(around) - 2.6)) * smoothstep(-0.3, 0.1, direction.y) * smoothstep(0.8, 0.5, direction.y);
    colour += float3(1.0, 0.9, 0.8) * strips * 0.8 + float3(0.35, 0.3, 0.4) * panels * 0.25;
    return colour;
}

// The light the glowing pipes cast on a point (the floor, the plinth), falling off with distance.
float3 pipeLight(float3 position, float3 normal)
{
    float3 light = 0.0;
    [loop]
    for (int i = 0; i < Pipes; i++)
    {
        if (isGlass(i))
        {
            float top = fillHeight(i);
            float3 nearest = float3(pipeX(i), clamp(position.y, PlinthTop, top), pipeZ(i));
            float3 toPipe = nearest - position;
            float d2 = dot(toPipe, toPipe);
            float facing = saturate(dot(normal, toPipe * rsqrt(max(d2, 1e-4))) * 0.8 + 0.2);
            light += pipeColour(i) * (0.4 + pipeLevel(i) * 2.0) * (top - PlinthTop) * facing * 0.05 / (d2 + 0.05);
        }
    }
    return light;
}

// How brightly a pipe's gas glows.
float3 gasColour(int i)
{
    return pipeColour(i) * (1.5 + pipeLevel(i) * 6.0 + Beat * 1.5 * pipeLevel(i));
}

// The glow picked up by a ray passing through a glass pipe, from where it enters to where it leaves.
float3 throughGas(float3 entry, float3 direction, float across, int i)
{
    float2 centre = float2(pipeX(i), pipeZ(i));
    float along = max(-dot(entry.xz - centre, direction.xz) / max(dot(direction.xz, direction.xz), 1e-6), 0.0);
    float3 closest = entry + direction * min(along, across);
    float off = length(closest.xz - centre) / pipeRadius(i);
    float top = fillHeight(i);
    float filled = smoothstep(top + 0.04, top - 0.04, closest.y) * smoothstep(PlinthTop, PlinthTop + 0.05, closest.y);
    // The meniscus of light at the top of the column glows brightest.
    float fromTop = (closest.y - top) / 0.03;
    float meniscus = exp(-fromTop * fromTop) * 0.8;
    float core = exp(-off * off * 6.0);
    // A faint haze of light fills the empty glass above the column, so the whole pipe reads.
    float haze = 0.06 + 0.04 * pipeLevel(i);
    return gasColour(i) * (core * (filled + haze) + meniscus * core) * across / pipeRadius(i) * 0.6;
}

float3 trace(float3 origin, float3 direction)
{
    float3 colour = 0.0;
    float3 throughput = 1.0;
    int bounces = min(qualitySteps(4), 6);
    [loop]
    for (int depth = 0; depth < bounces; depth++)
    {
        Hit hit = traceScene(origin, direction);
        float3 position = origin + direction * hit.distance;
        float3 normal = hit.normal;
        float facing = saturate(dot(normal, -direction));

        if (hit.material == Hall)
        {
            // Seen directly, the hall's lights stay dim so they don't upstage the organ.
            colour += throughput * hall(direction) * (depth == 0 ? 0.35 : 1.0);
            break;
        }
        if (hit.material == Floor)
        {
            // Polished black stone: dim where lit by the pipes, a near-perfect mirror at a glance.
            float veins = pow(abs(noise(position.xz * 1.5) - 0.5) * 2.0, 8.0) * 0.08;
            float3 albedo = float3(0.02, 0.02, 0.025) + veins;
            float fresnel = 0.05 + 0.95 * pow(1.0 - facing, 5.0);
            colour += throughput * albedo * pipeLight(position, normal);
            float fade = exp(-max(length(position.xz) - 5.0, 0.0) * 0.3);
            throughput *= (0.25 + fresnel * 0.7) * fade;
            direction = reflect(direction, normal);
            origin = position + normal * 1e-3;
        }
        else if (hit.material == Chrome || hit.material == Plinth)
        {
            // Polished chrome, slightly warm; the chrome pipes carry a thin band of their own colour
            // at the mouth.
            float3 tint = float3(0.9, 0.88, 0.85);
            if (hit.material == Chrome)
            {
                float mouth = smoothstep(0.03, 0.01, abs(position.y - (PlinthTop + 0.45)));
                colour += throughput * pipeColour(hit.pipe) * mouth * (0.5 + pipeLevel(hit.pipe) * 4.0);
                // Fine vertical brushing breaks up the reflections a little.
                float angle = atan2(normal.z, normal.x);
                tint *= 0.92 + 0.08 * noise(float2(angle * 40.0, position.y * 2.0));
                // The glass pipes either side wash the chrome with their colours.
                colour += throughput * 0.5 * pipeLight(position, normal);
            }
            else
            {
                colour += throughput * float3(0.08, 0.08, 0.09) * pipeLight(position, normal);
            }
            throughput *= lerp(tint, 1.0, pow(1.0 - facing, 5.0));
            direction = reflect(direction, normal);
            origin = position + normal * 1e-3;
        }
        else
        {
            // A crystal rod: a glint off the surface, then in through the glass, through the glowing
            // gas, and out the far side.
            int i = hit.pipe;
            float fresnel = 0.04 + 0.96 * pow(1.0 - facing, 5.0);
            colour += throughput * fresnel * hall(reflect(direction, normal));
            float3 inside = refract(direction, normal, 1.0 / 1.5);
            float2 centre = float2(pipeX(i), pipeZ(i));
            float across = cylinderExit(position, inside, centre, pipeRadius(i));
            colour += throughput * (1.0 - fresnel) * throughGas(position, inside, across, i);
            float3 exitPoint = position + inside * across;
            float3 exitNormal = normalize(float3(exitPoint.x - centre.x, 0.0, exitPoint.z - centre.y));
            float3 outside = refract(inside, -exitNormal, 1.5);
            if (dot(outside, outside) < 0.5)
            {
                outside = reflect(inside, -exitNormal);
            }
            throughput *= (1.0 - fresnel) * float3(0.92, 0.96, 0.98);
            direction = outside;
            origin = exitPoint + outside * 2e-3;
        }
        if (max(throughput.x, max(throughput.y, throughput.z)) < 0.02)
        {
            break;
        }
    }
    return colour;
}

static const float2 SampleOffsets[2] = { float2(0.25, -0.25), float2(-0.25, 0.25) };

float3 Render(float2 uv, float2 p)
{
    float angle = sin(Time * 0.045) * 0.55;
    float3 eye = float3(sin(angle) * 4.0, 1.0 + sin(Time * 0.031) * 0.3, -cos(angle) * 4.0);
    float3 target = float3(0.0, 1.45, 0.0);
    const float zoom = 1.55;

    int samples = Quality > 1.5 ? 2 : 1;
    float3 colour = 0.0;
    [loop]
    for (int s = 0; s < samples; s++)
    {
        float2 jitter = samples > 1 ? SampleOffsets[s] * pixelSize() : 0.0;
        colour += trace(eye, cameraRay(eye, target, float2(p.x, -p.y) + jitter, zoom, 0.0));
    }
    colour /= samples;

    // The coloured haze hanging round the lit pipes.
    float3 direction = cameraRay(eye, target, float2(p.x, -p.y), zoom, 0.0);
    [loop]
    for (int i = 0; i < Pipes; i++)
    {
        if (isGlass(i))
        {
            float3 toPipe = float3(pipeX(i), fillHeight(i) * 0.6, pipeZ(i)) - eye;
            float3 off = toPipe - direction * dot(toPipe, direction);
            float d = length(off.xz) + max(abs(off.y) - fillHeight(i) * 0.4, 0.0);
            colour += pipeColour(i) * exp(-d * 5.0) * (0.01 + pipeLevel(i) * 0.04);
        }
    }
    return colour;
}
