// Name: Glass Knot
// A thick knot of flawless glass turns slowly in a dark studio, bending and mirroring the coloured light strips around it into rainbow-fringed streaks, and pooling bright caustics on the floor below; every strip of light glows with its own part of the music.
// Family: Ray traced
// Tags: Dark, Colourful, 3D

// The knot is a trefoil: it winds twice round its centre while looping three times through the
// middle, a tube of glass following that path. Light strips stand in a ring around it.
static const float KnotRadius = 1.0;    // the ring the knot winds round
static const float KnotLoop = 0.46;     // how far it swings out from that ring
static const float FloorHeight = -1.35;
static const int Strips = 8;
static const float StripDistance = 6.0;

float tubeRadius()
{
    return 0.26 * (1.0 + Bass * 0.05);
}

// The knot's turning, worked out once per pixel.
static float3x3 KnotFrame;

float3x3 turning(float a, float b)
{
    float sa, ca, sb, cb;
    sincos(a, sa, ca);
    sincos(b, sb, cb);
    float3x3 spin = float3x3(ca, 0.0, sa, 0.0, 1.0, 0.0, -sa, 0.0, ca);
    float3x3 tip = float3x3(1.0, 0.0, 0.0, 0.0, cb, -sb, 0.0, sb, cb);
    return mul(spin, tip);
}

// Distance to the glass. Each point round the knot's centre lies over two passes of the tube
// (it winds round twice), so we find both and keep the nearer.
float knot(float3 position)
{
    float3 p = mul(KnotFrame, position);
    float r = length(p.xz);
    float around = atan2(p.z, p.x);
    float2 section = float2(r - KnotRadius, p.y);
    float nearest = 1e5;
    [unroll]
    for (int k = 0; k < 2; k++)
    {
        float t = (around + 2.0 * PI * k) * 0.5;
        float s, c;
        sincos(3.0 * t, s, c);
        nearest = min(nearest, length(section - float2(c, s) * KnotLoop));
    }
    return (nearest - tubeRadius()) * 0.8;
}

float3 knotNormal(float3 position)
{
    const float2 e = float2(0.002, -0.002);
    return normalize(e.xyy * knot(position + e.xyy) + e.yyx * knot(position + e.yyx)
                   + e.yxy * knot(position + e.yxy) + e.xxx * knot(position + e.xxx));
}

// The colour of each light strip, round the rainbow, slowly turning.
float3 stripColour(int i)
{
    return hsv2rgb(i / (float)Strips + Time * 0.01, 0.75, 1.0);
}

float stripLevel(int i)
{
    return 0.25 + spectrum(0.04 + i / (float)Strips * 0.8) * 2.2 + Beat * 0.2;
}

// What a ray sees of the studio: a round room of black walls with tall light strips standing
// round it, and a soft overhead panel in the ceiling.
static const float CeilingHeight = 3.6;

float3 studio(float3 origin, float3 direction)
{
    float3 colour = float3(0.006, 0.006, 0.009);
    if (direction.y > 0.0)
    {
        float up = (CeilingHeight - origin.y) / direction.y;
        float2 onCeiling = origin.xz + direction.xz * up;
        float2 panel = abs(rotate(onCeiling, Time * 0.03));
        float overhead = smoothstep(1.3, 1.15, max(panel.x, panel.y * 1.8));
        colour += float3(0.9, 0.92, 1.0) * overhead * (0.7 + Level * 0.6) * 1.5;
        colour += float3(0.03, 0.03, 0.04) * exp(-length(onCeiling) * 0.4);
        if (length(onCeiling) < StripDistance)
        {
            return colour;
        }
    }
    // Where the ray meets the wall of the room.
    float a = dot(direction.xz, direction.xz);
    float b = dot(origin.xz, direction.xz);
    float c = dot(origin.xz, origin.xz) - StripDistance * StripDistance;
    float t = (-b + sqrt(max(b * b - a * c, 0.0))) / max(a, 1e-5);
    float3 wall = origin + direction * t;
    float around = atan2(wall.z, wall.x) + Time * 0.05;
    float slot = around / (2.0 * PI) * Strips;
    int index = (int)wrap(floor(slot + 0.5), (float)Strips);
    float across = abs(frac(slot + 0.5) - 0.5) * 2.0 * PI * StripDistance / Strips;
    float tall = smoothstep(0.2, 0.0, abs(wall.y - 1.1) - 2.3);
    float strip = smoothstep(0.3, 0.2, across) * tall;
    colour += float3(0.012, 0.012, 0.016) * smoothstep(-1.0, 3.0, wall.y);
    colour += stripColour(index) * stripLevel(index) * (strip * 2.5 + exp(-across * 3.0) * 0.08 * tall);
    return colour;
}

// Light focused through the knot onto the floor: bright, shimmering, coloured threads in the
// shape of the knot's shadow.
float3 caustics(float3 position)
{
    // The knot's shadow shape: the nearest of three slices through it.
    float d = min(knot(float3(position.x, -0.45, position.z)), min(knot(float3(position.x, 0.0, position.z)), knot(float3(position.x, 0.45, position.z)))) / 0.8 + tubeRadius();
    float edge = exp(-abs(d - tubeRadius() * 0.55) * 18.0);
    float centre = exp(-d * d * 30.0);
    float shimmer = 0.6 + 0.4 * noise(position.xz * 9.0 + Time * 0.6);
    float3 tint = hsv2rgb(atan2(position.z, position.x) / (2.0 * PI) + Time * 0.03, 0.55, 1.0);
    float inside = smoothstep(tubeRadius() * 1.2, 0.0, d);
    return tint * (edge * 0.9 + centre * 1.4) * shimmer * (0.6 + Level * 0.8) * inside;
}

// The studio as seen along a ray that has left the glass: the floor, or the walls and lights.
float3 surroundings(float3 origin, float3 direction)
{
    float t = (FloorHeight - origin.y) / min(direction.y, -1e-5);
    float3 position = origin + direction * t;
    if (direction.y < 0.0 && length(position.xz) < StripDistance)
    {
        float fade = exp(-length(position.xz) * 0.25);
        float facing = saturate(-direction.y);
        float fresnel = 0.04 + 0.96 * pow(1.0 - facing, 5.0);
        // A polished dark floor: the caustics, plus a soft mirror of the studio.
        float3 colour = caustics(position) * fade + studio(position, reflect(direction, float3(0.0, 1.0, 0.0))) * fresnel * 0.6;
        // A soft dark shadow round the knot's foot.
        colour *= 0.6 + 0.4 * smoothstep(0.0, 1.6, length(position.xz));
        return colour + float3(0.004, 0.004, 0.005) * fade;
    }
    return studio(origin, direction);
}

// March a ray from outside towards the knot. Returns the distance, or -1 for a miss.
float marchOutside(float3 origin, float3 direction, float limit, int steps)
{
    float t = 0.0;
    [loop]
    for (int i = 0; i < steps; i++)
    {
        float d = knot(origin + direction * t);
        if (d < 0.001)
        {
            return t;
        }
        t += d;
        if (t > limit)
        {
            break;
        }
    }
    return -1.0;
}

// March through the glass from just inside the surface to where the ray leaves it.
float marchInside(float3 origin, float3 direction, int steps)
{
    float t = 0.01;
    [loop]
    for (int i = 0; i < steps; i++)
    {
        float d = -knot(origin + direction * t);
        if (d < 0.001)
        {
            break;
        }
        t += max(d, 0.004);
    }
    return t;
}

static const float GlassIndex = 1.5;
static const float3 Dispersion = float3(-0.035, 0.0, 0.04);

float3 traceGlass(float3 eye, float3 direction)
{
    int steps = qualitySteps(80);
    float hit = marchOutside(eye, direction, 12.0, steps);
    if (hit < 0.0)
    {
        return surroundings(eye, direction);
    }

    float3 colour = 0.0;
    float3 throughput = 1.0;
    float3 origin = eye + direction * hit;
    float3 ray = direction;
    // Through the glass, out, and if the ray meets the knot again, through once more.
    [loop]
    for (int crossing = 0; crossing < 2; crossing++)
    {
        float3 normal = knotNormal(origin);
        float facing = saturate(dot(-ray, normal));
        float fresnel = 0.04 + 0.96 * pow(1.0 - facing, 5.0);
        colour += throughput * fresnel * surroundings(origin, reflect(ray, normal));
        throughput *= 1.0 - fresnel;

        float3 inside = refract(ray, normal, 1.0 / GlassIndex);
        float travelled = 0.0;
        float3 exitPoint = origin;
        float3 exitNormal = normal;
        float3 outside = 0.0;
        bool escaped = false;
        // Inside, the ray may bounce off the inner walls (total internal reflection) before it gets out.
        [loop]
        for (int bounce = 0; bounce < 3; bounce++)
        {
            float t = marchInside(origin, inside, steps / 2);
            travelled += t;
            exitPoint = origin + inside * t;
            exitNormal = knotNormal(exitPoint);
            outside = refract(inside, -exitNormal, GlassIndex);
            if (dot(outside, outside) > 0.5)
            {
                escaped = true;
                break;
            }
            inside = reflect(inside, -exitNormal);
            origin = exitPoint - exitNormal * 0.004;
        }
        // The glass is faintly blue-green, deeper where the light has travelled further.
        throughput *= exp(-travelled * float3(0.22, 0.08, 0.1));
        if (!escaped)
        {
            break;
        }
        origin = exitPoint + exitNormal * 0.004;
        float again = crossing == 0 ? marchOutside(origin, outside, 4.0, steps / 2) : -1.0;
        if (again > 0.0)
        {
            ray = outside;
            origin = origin + outside * again;
            continue;
        }
        // Each colour bends by its own amount on the way out, splitting edges into rainbows.
        float3 seen = 0.0;
        seen.r = surroundings(exitPoint, refract(inside, -exitNormal, GlassIndex + Dispersion.r)).r;
        seen.g = surroundings(exitPoint, outside).g;
        seen.b = surroundings(exitPoint, refract(inside, -exitNormal, GlassIndex + Dispersion.b)).b;
        colour += throughput * seen;
        // Light caught and carried along inside the glass gives it a faint inner glow.
        colour += throughput * stripColour((int)wrap(floor(travelled * 3.0 + Time * 0.2), (float)Strips)) * 0.03 * (0.5 + Level);
        break;
    }
    return colour;
}

float3 Render(float2 uv, float2 p)
{
    KnotFrame = turning(Time * 0.23 + Mid * 0.2, 0.6 + 0.35 * sin(Time * 0.11));
    float angle = Time * 0.06;
    float3 eye = float3(sin(angle) * 4.4, 0.55 + 0.45 * sin(Time * 0.08), cos(angle) * 4.4);
    float3 target = float3(0.0, 0.05, 0.0);
    return traceGlass(eye, cameraRay(eye, target, float2(p.x, -p.y), 1.75, 0.0));
}
