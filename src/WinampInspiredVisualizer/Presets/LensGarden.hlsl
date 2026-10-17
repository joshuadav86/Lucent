// Name: Lens Garden
// Glass lenses, crystal balls and prisms drift above a glowing neon grid, magnifying it, flipping it upside down and splitting its lines into rainbows at their edges; they tip and sway to the beat while the grid pulses with the spectrum.
// Family: Ray traced
// Tags: Neon, Geometric, 3D

static const int ObjectCount = 5;
// Kinds of glass.
static const int Lens = 0;    // a magnifying lens: the overlap of two large spheres
static const int Ball = 1;    // a solid glass ball, which turns the world upside down
static const int Prism = 2;   // a triangular prism

static const float LensCurve = 1.6;     // radius of each face's sphere
static const float LensHalfThickness = 0.2;
static const float BallRadius = 0.5;
static const float PrismRadius = 0.62;
static const float PrismHalfLength = 0.6;

// The angle one pixel covers, set in Render.
static float PixelAngle;

int kindOf(int i)
{
    return i == 2 ? Ball : (i == 1 || i == 4 ? Prism : Lens);
}

float3x3 rotationFrom(float3 angles)
{
    float3 s = sin(angles);
    float3 c = cos(angles);
    float3x3 x = float3x3(1.0, 0.0, 0.0, 0.0, c.x, -s.x, 0.0, s.x, c.x);
    float3x3 y = float3x3(c.y, 0.0, s.y, 0.0, 1.0, 0.0, -s.y, 0.0, c.y);
    float3x3 z = float3x3(c.z, -s.z, 0.0, s.z, c.z, 0.0, 0.0, 0.0, 1.0);
    return mul(z, mul(y, x));
}

// Where each piece of glass floats and how it's turned. They drift on slow loops and tip over a
// little on every beat, each in its own direction.
static float3 Centres[ObjectCount];
static float3x3 Frames[ObjectCount];

void placeObjects()
{
    [unroll]
    for (int i = 0; i < ObjectCount; i++)
    {
        float k = i * 1.2566 + 0.4;
        float t = Time * (0.06 + i * 0.008);
        float radius = i == 2 ? 0.0 : 1.75 + 0.25 * sin(t * 1.3 + i);
        Centres[i] = float3(cos(k + t) * radius, 1.15 + 0.3 * sin(t * 2.0 + i * 2.0) + (i == 2 ? 0.2 : 0.0), sin(k + t) * radius);
        float tip = Beat * 0.35 * (i % 2 == 0 ? 1.0 : -1.0) + Bass * 0.15;
        float3 angles = float3(sin(Time * 0.21 + i) * 0.5 + tip + (kindOf(i) == Lens ? 0.45 : PI * 0.5), Time * 0.13 * (i % 2 == 0 ? 1.0 : -1.0) + i, cos(Time * 0.17 + i * 2.0) * 0.4 + tip * 0.6);
        Frames[i] = rotationFrom(angles);
    }
}

// The interval a ray spends inside one piece of glass ([enter, leave]) and the outward normals
// where it enters and leaves. Every piece is convex, so a ray is inside it at most once.
bool glassInterval(int i, float3 origin, float3 direction, out float enter, out float leave, out float3 enterNormal, out float3 leaveNormal)
{
    enter = -1e5;
    leave = 1e5;
    enterNormal = float3(0.0, 1.0, 0.0);
    leaveNormal = float3(0.0, 1.0, 0.0);
    float3 o = mul(Frames[i], origin - Centres[i]);
    float3 d = mul(Frames[i], direction);
    int kind = kindOf(i);

    if (kind == Prism)
    {
        [unroll]
        for (int f = 0; f < 5; f++)
        {
            float angle = (30.0 + 120.0 * f) * PI / 180.0;
            float3 normal = f < 3 ? float3(cos(angle), sin(angle), 0.0) : float3(0.0, 0.0, f == 3 ? 1.0 : -1.0);
            float offset = f < 3 ? PrismRadius * 0.5 : PrismHalfLength;
            float along = dot(normal, d);
            float outside = dot(normal, o) - offset;
            if (abs(along) < 1e-6)
            {
                if (outside > 0.0)
                {
                    return false;
                }
                continue;
            }
            float t = -outside / along;
            if (along < 0.0)
            {
                if (t > enter)
                {
                    enter = t;
                    enterNormal = normal;
                }
            }
            else if (t < leave)
            {
                leave = t;
                leaveNormal = normal;
            }
        }
    }
    else
    {
        // A ball is one sphere; a lens is where two big spheres overlap.
        int parts = kind == Ball ? 1 : 2;
        [unroll]
        for (int s = 0; s < 2; s++)
        {
            if (s >= parts)
            {
                continue;
            }
            float radius = kind == Ball ? BallRadius : LensCurve;
            float3 centre = kind == Ball ? 0.0 : float3(0.0, 0.0, (s == 0 ? 1.0 : -1.0) * (LensCurve - LensHalfThickness));
            float3 oc = o - centre;
            float b = dot(oc, d);
            float c = dot(oc, oc) - radius * radius;
            float h = b * b - c;
            if (h < 0.0)
            {
                return false;
            }
            h = sqrt(h);
            if (-b - h > enter)
            {
                enter = -b - h;
                enterNormal = (oc + d * enter) / radius;
            }
            if (-b + h < leave)
            {
                leave = -b + h;
                leaveNormal = (oc + d * leave) / radius;
            }
        }
    }
    // Back into the world's directions (the frame is a pure rotation, so its transpose undoes it).
    enterNormal = mul(enterNormal, Frames[i]);
    leaveNormal = mul(leaveNormal, Frames[i]);
    return enter < leave && leave > 1e-4;
}

// ---- The floor and the sky ----

float gridLine(float x, float width)
{
    float d = abs(frac(x + 0.5) - 0.5);
    return saturate(1.0 - d / width);
}

// The floor: dark tiles inlaid with faint quarter-circle patterns, and neon grid lines that glow
// with the spectrum. A ring of light runs out from the middle on every beat. footprint is how wide
// a pixel is here, so thin lines blur out instead of shimmering.
float3 floorColour(float3 position, float footprint)
{
    float2 q = position.xz * 2.0;
    float r = length(q);
    float width = max(0.02, footprint * 2.4);
    float fade = 0.02 / width;

    float2 cell = floor(q);
    float2 inCell = frac(q) - 0.5;
    // Truchet arcs: each tile has two quarter-circles, flipped at random.
    float flip = step(0.5, hash(cell + 3.0));
    float2 local = flip > 0.5 ? float2(-inCell.x, inCell.y) : inCell;
    float arcA = abs(length(local - 0.5) - 0.5);
    float arcB = abs(length(local + 0.5) - 0.5);
    float arcs = saturate(1.0 - min(arcA, arcB) / max(0.03, footprint * 1.2)) * min(1.0, 0.03 / max(0.03, footprint * 1.2));
    float checker = frac((cell.x + cell.y) * 0.5) * 2.0;

    float3 colour = float3(0.015, 0.012, 0.03) + checker * float3(0.02, 0.012, 0.035);
    colour += lerp(float3(0.35, 0.12, 0.6), float3(0.1, 0.4, 0.6), flip) * arcs * 1.3;

    float lines = max(gridLine(q.x, width), gridLine(q.y, width)) * fade;
    float level = spectrum(saturate(r / 14.0) * 0.7 + 0.03);
    float3 neonA = float3(1.0, 0.15, 0.7);
    float3 neonB = float3(0.1, 0.8, 1.0);
    float3 lineColour = lerp(neonA, neonB, 0.5 + 0.5 * sin(r * 0.4 - Time * 0.3));
    float ring = exp(-abs(r - (1.0 - Beat) * 14.0) * 1.5) * Beat;
    colour += lineColour * lines * (0.5 + level * 2.0 + ring * 3.0);
    // Major lines every four tiles, brighter.
    float major = max(gridLine(q.x * 0.25, width * 0.25), gridLine(q.y * 0.25, width * 0.25)) * fade;
    colour += lineColour * major * 1.2;
    return colour * exp(-r * 0.03);
}

float3 skyColour(float3 direction)
{
    float up = direction.y;
    float3 colour = lerp(float3(0.09, 0.02, 0.12), float3(0.005, 0.005, 0.02), saturate(up * 3.0));
    colour += float3(0.6, 0.15, 0.5) * exp(-abs(up) * 30.0) * 0.25;
    // Two long soft lights hang high overhead, so the glass always has something bright to mirror.
    float barA = exp(-abs(dot(direction, normalize(float3(0.4, 0.0, -0.9)))) * 30.0) * smoothstep(0.45, 0.65, up);
    float barB = exp(-abs(dot(direction, normalize(float3(0.9, 0.0, 0.45)))) * 30.0) * smoothstep(0.55, 0.75, up);
    colour += float3(1.0, 0.4, 0.85) * barA * (1.2 + Mid * 1.5) + float3(0.35, 0.8, 1.0) * barB * (1.2 + Treble * 1.5);
    float2 cell = float2(atan2(direction.z, direction.x) * 120.0, up * 120.0);
    colour += step(0.992, hash(floor(cell))) * smoothstep(0.3, 0.0, length(frac(cell) - 0.5)) * 0.4 * smoothstep(0.05, 0.3, up);
    return colour;
}

// What a ray sees once it has left the glass for good: the floor or the sky.
float3 world(float3 origin, float3 direction, float travelled)
{
    if (direction.y < -1e-4)
    {
        float t = -origin.y / direction.y;
        float3 position = origin + direction * t;
        float footprint = PixelAngle * (travelled + t) / max(-direction.y, 0.15);
        float3 colour = floorColour(position, footprint);
        // Each piece of glass gathers a soft pool of focused light onto the floor beneath it.
        [unroll]
        for (int i = 0; i < ObjectCount; i++)
        {
            float2 off = position.xz - Centres[i].xz;
            colour += float3(0.35, 0.3, 0.5) * exp(-dot(off, off) * 14.0) * (0.12 + Bass * 0.2);
        }
        float haze = exp(-(travelled + t) * 0.02);
        return lerp(skyColour(float3(direction.x, -0.001, direction.z)), colour, haze);
    }
    return skyColour(direction);
}

// One ray through the garden at one refractive index. hitGlass reports whether it met any glass.
float3 trace(float3 origin, float3 direction, float ior, out bool hitGlass)
{
    hitGlass = false;
    float3 colour = 0.0;
    float3 throughput = 1.0;
    float travelled = 0.0;
    int inside = -1;
    [loop]
    for (int bounce = 0; bounce < 8; bounce++)
    {
        if (inside >= 0)
        {
            float enter;
            float leave;
            float3 enterNormal;
            float3 leaveNormal;
            glassInterval(inside, origin, direction, enter, leave, enterNormal, leaveNormal);
            leave = max(leave, 1e-4);
            float3 position = origin + direction * leave;
            travelled += leave;
            // A faint tint the further light travels through the glass.
            throughput *= exp(-leave * float3(0.08, 0.04, 0.02));
            float3 bent = refract(direction, -leaveNormal, ior);
            if (dot(bent, bent) < 0.5)
            {
                // Total internal reflection: bounce back inside.
                direction = reflect(direction, -leaveNormal);
                origin = position - leaveNormal * 1e-3;
            }
            else
            {
                direction = bent;
                origin = position + leaveNormal * 1e-3;
                inside = -1;
            }
            continue;
        }

        // Find the nearest piece of glass ahead.
        float nearest = 1e5;
        float3 normal = 0.0;
        int which = -1;
        [unroll]
        for (int i = 0; i < ObjectCount; i++)
        {
            float enter;
            float leave;
            float3 enterNormal;
            float3 leaveNormal;
            if (glassInterval(i, origin, direction, enter, leave, enterNormal, leaveNormal) && enter > 1e-4 && enter < nearest)
            {
                nearest = enter;
                normal = enterNormal;
                which = i;
            }
        }
        if (which < 0)
        {
            colour += throughput * world(origin, direction, travelled);
            return colour;
        }
        hitGlass = true;
        float3 position = origin + direction * nearest;
        travelled += nearest;
        float cosine = saturate(dot(normal, -direction));
        float fresnel = 0.04 + 0.96 * pow(1.0 - cosine, 5.0);
        // The mirror-like sheen on the glass shows the floor or sky it faces.
        colour += throughput * fresnel * world(position, reflect(direction, normal), travelled);
        // A thin bright rim where the glass meets the light at a glancing angle.
        colour += throughput * float3(0.6, 0.7, 1.0) * pow(1.0 - cosine, 6.0) * 0.8 * (0.5 + Treble);
        throughput *= 1.0 - fresnel;
        direction = refract(direction, normal, 1.0 / ior);
        origin = position - normal * 1e-3;
        inside = which;
    }
    return colour + throughput * skyColour(direction);
}

float3 Render(float2 uv, float2 p)
{
    placeObjects();
    float orbit = Time * 0.05;
    float3 eye = float3(sin(orbit) * 4.8, 2.6 + sin(Time * 0.07) * 0.4, cos(orbit) * 4.8);
    float3 target = float3(0.0, 0.7, 0.0);
    const float zoom = 1.75;
    float3 direction = cameraRay(eye, target, float2(p.x, -p.y), zoom, 0.0);
    PixelAngle = pixelSize() / zoom;

    // Trace once for green; if the ray went through glass, trace red and blue again with slightly
    // different strengths of bending, which splits white lines into rainbows.
    bool glass;
    float3 colour = trace(eye, direction, 1.52, glass);
    if (glass)
    {
        bool unused;
        float red = trace(eye, direction, 1.49, unused).r;
        float blue = trace(eye, direction, 1.56, unused).b;
        colour = float3(red, colour.g, blue);
    }
    return colour;
}
