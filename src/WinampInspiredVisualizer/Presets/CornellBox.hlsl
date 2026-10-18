// Name: Cornell Box
// The classic red and green room, path traced with real bouncing light: soft shadows, walls tinting each other with colour, and a glass and a mirror ball that hop to the beat while the ceiling light breathes with the music.
// Family: Ray traced
// Tags: Calm, Geometric, 3D
// Added: 2026-10-17 17:12

// Every frame each pixel follows a light path or two at random through the room, and the result
// is blended with the last frame's picture, so the grain of the random paths settles into a
// smooth image within a fraction of a second.
// The room runs from -1 to 1 on every axis, open at the front (+z), where the camera looks in.

static const float SphereRadius = 0.33;
static const float LightSize = 0.26;    // half the width of the square ceiling light
static const float LightHeight = 0.995;

// Materials.
static const int Nothing = 0;
static const int Wall = 1;
static const int Mirror = 2;
static const int GlassBall = 3;
static const int Lamp = 4;

// ---- Random numbers, one stream per pixel per frame ----

static uint Seed;

uint scramble(uint v)
{
    uint state = v * 747796405u + 2891336453u;
    uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
    return (word >> 22u) ^ word;
}

float nextRandom()
{
    Seed = Seed * 747796405u + 2891336453u;
    uint word = ((Seed >> ((Seed >> 28u) + 4u)) ^ Seed) * 277803737u;
    return ((word >> 22u) ^ word) / 4294967295.0;
}

// ---- The scene ----

static float3 MirrorCentre;
static float3 GlassCentre;

// A hop on each beat: up as the beat fades in, down again before the next.
float hop(float level)
{
    return 4.0 * Beat * (1.0 - Beat) * level;
}

void placeBalls()
{
    bool even = frac(BeatCount * 0.5) < 0.25;
    float drift = Time * 0.15;
    MirrorCentre = float3(-0.42 + sin(drift) * 0.05, -1.0 + SphereRadius + (even ? hop(0.35 + Bass * 0.35) : 0.0), -0.3 + cos(drift) * 0.05);
    GlassCentre = float3(0.42 + cos(drift * 1.3) * 0.05, -1.0 + SphereRadius + (even ? 0.0 : hop(0.35 + Bass * 0.35)), 0.25);
}

float lampPower()
{
    return 20.0 * (0.7 + Level * 0.45 + Bass * 0.3);
}

float3 lampColour()
{
    return float3(1.0, 0.86, 0.68) * lampPower();
}

struct Hit
{
    float distance;
    float3 normal;
    int material;
    float3 albedo;
};

float sphereHit(float3 origin, float3 direction, float3 centre)
{
    float3 oc = origin - centre;
    float b = dot(oc, direction);
    float h = b * b - dot(oc, oc) + SphereRadius * SphereRadius;
    if (h < 0.0)
    {
        return -1.0;
    }
    h = sqrt(h);
    float t = -b - h;
    if (t < 1e-4)
    {
        t = -b + h;
    }
    return t > 1e-4 ? t : -1.0;
}

// One wall: the plane where axis (0 x, 1 y, 2 z) equals side, limited to the room.
void wallHit(float3 origin, float3 direction, int axis, float side, float3 normal, float3 albedo, inout Hit hit)
{
    float o = axis == 0 ? origin.x : (axis == 1 ? origin.y : origin.z);
    float d = axis == 0 ? direction.x : (axis == 1 ? direction.y : direction.z);
    if (abs(d) < 1e-6)
    {
        return;
    }
    float t = (side - o) / d;
    if (t < 1e-4 || t > hit.distance)
    {
        return;
    }
    float3 position = origin + direction * t;
    if (max(abs(position.x), max(abs(position.y), abs(position.z))) > 1.0001)
    {
        return;
    }
    hit.distance = t;
    hit.normal = normal;
    hit.material = Wall;
    hit.albedo = albedo;
}

Hit trace(float3 origin, float3 direction)
{
    Hit hit;
    hit.distance = 1e4;
    hit.normal = 0.0;
    hit.material = Nothing;
    hit.albedo = 0.0;
    const float3 white = float3(0.73, 0.71, 0.68);
    // Red on the left and green on the right, as the camera sees it (looking towards -z).
    wallHit(origin, direction, 0, 1.0, float3(-1.0, 0.0, 0.0), float3(0.63, 0.06, 0.05), hit);
    wallHit(origin, direction, 0, -1.0, float3(1.0, 0.0, 0.0), float3(0.12, 0.45, 0.09), hit);
    wallHit(origin, direction, 1, -1.0, float3(0.0, 1.0, 0.0), white, hit);
    wallHit(origin, direction, 1, 1.0, float3(0.0, -1.0, 0.0), white, hit);
    wallHit(origin, direction, 2, -1.0, float3(0.0, 0.0, 1.0), white, hit);

    // The light: a bright square just below the ceiling.
    if (direction.y > 0.0)
    {
        float t = (LightHeight - origin.y) / direction.y;
        float3 position = origin + direction * t;
        if (t > 1e-4 && t < hit.distance && max(abs(position.x), abs(position.z)) < LightSize)
        {
            hit.distance = t;
            hit.normal = float3(0.0, -1.0, 0.0);
            hit.material = Lamp;
        }
    }

    float t = sphereHit(origin, direction, MirrorCentre);
    if (t > 0.0 && t < hit.distance)
    {
        hit.distance = t;
        hit.normal = normalize(origin + direction * t - MirrorCentre);
        hit.material = Mirror;
        hit.albedo = float3(0.95, 0.95, 0.97);
    }
    t = sphereHit(origin, direction, GlassCentre);
    if (t > 0.0 && t < hit.distance)
    {
        hit.distance = t;
        hit.normal = normalize(origin + direction * t - GlassCentre);
        hit.material = GlassBall;
        hit.albedo = 1.0;
    }
    return hit;
}

// Is the way between two points clear? Only the balls can block it inside the room.
bool clearPath(float3 from, float3 to)
{
    float3 direction = to - from;
    float reach = length(direction);
    direction /= reach;
    float a = sphereHit(from, direction, MirrorCentre);
    float b = sphereHit(from, direction, GlassCentre);
    return !((a > 0.0 && a < reach) || (b > 0.0 && b < reach));
}

// A random direction round a normal, more of them near the normal (as diffuse light scatters).
float3 cosineDirection(float3 normal)
{
    float u = nextRandom();
    float v = nextRandom() * 2.0 * PI;
    float r = sqrt(u);
    float3 tangent = normalize(abs(normal.y) < 0.9 ? cross(normal, float3(0.0, 1.0, 0.0)) : cross(normal, float3(1.0, 0.0, 0.0)));
    float3 bitangent = cross(normal, tangent);
    return normalize(tangent * r * cos(v) + bitangent * r * sin(v) + normal * sqrt(max(1.0 - u, 0.0)));
}

// Follow one light path from the camera.
float3 tracePath(float3 origin, float3 direction)
{
    float3 colour = 0.0;
    float3 throughput = 1.0;
    bool sharpLast = true;   // the last bounce was a mirror or glass (or the camera), so a direct view of the lamp counts
    bool diffuseSeen = false;
    [loop]
    for (int bounce = 0; bounce < 6; bounce++)
    {
        Hit hit = trace(origin, direction);
        if (hit.material == Nothing)
        {
            break;
        }
        float3 position = origin + direction * hit.distance;
        if (hit.material == Lamp)
        {
            if (sharpLast)
            {
                // Light focused by the balls onto the walls (caustics) arrives along rare paths; capping
                // each one keeps them a soft glow instead of a scatter of bright specks.
                float3 light = throughput * lampColour();
                float strongest = max(light.r, max(light.g, light.b));
                colour += bounce > 0 && diffuseSeen ? light * min(1.0, 0.6 / max(strongest, 1e-4)) : light;
            }
            break;
        }
        if (hit.material == Wall)
        {
            // Light straight from the lamp: pick a point on it and check nothing's in the way.
            float3 onLamp = float3((nextRandom() * 2.0 - 1.0) * LightSize, LightHeight, (nextRandom() * 2.0 - 1.0) * LightSize);
            float3 toLamp = onLamp - position;
            float distanceSquared = dot(toLamp, toLamp);
            toLamp *= rsqrt(distanceSquared);
            float cosSurface = dot(hit.normal, toLamp);
            float cosLamp = toLamp.y;
            if (cosSurface > 0.0 && cosLamp > 0.0 && clearPath(position + hit.normal * 1e-3, onLamp))
            {
                float area = 4.0 * LightSize * LightSize;
                colour += throughput * hit.albedo / PI * lampColour() * cosSurface * cosLamp * area / distanceSquared;
            }
            throughput *= hit.albedo;
            direction = cosineDirection(hit.normal);
            origin = position + hit.normal * 1e-3;
            sharpLast = false;
            diffuseSeen = true;
            if (bounce >= 3)
            {
                break;
            }
        }
        else if (hit.material == Mirror)
        {
            throughput *= hit.albedo;
            direction = reflect(direction, hit.normal);
            origin = position + hit.normal * 1e-3;
            sharpLast = true;
        }
        else
        {
            // Glass: reflect or pass through, at random in proportion to how much light does each.
            bool entering = dot(direction, hit.normal) < 0.0;
            float3 normal = entering ? hit.normal : -hit.normal;
            float eta = entering ? 1.0 / 1.5 : 1.5;
            float cosine = saturate(dot(-direction, normal));
            float fresnel = 0.04 + 0.96 * pow(1.0 - cosine, 5.0);
            float3 bent = refract(direction, normal, eta);
            if (dot(bent, bent) < 0.5 || nextRandom() < fresnel)
            {
                direction = reflect(direction, normal);
                origin = position + normal * 1e-3;
            }
            else
            {
                direction = bent;
                origin = position - normal * 1e-3;
                // A faint warm tint in the glass.
                throughput *= float3(0.98, 0.97, 0.94);
            }
            sharpLast = true;
        }
    }
    // Clamp the rare very bright path so it can't leave a speck.
    float brightest = max(colour.r, max(colour.g, colour.b));
    return colour * min(1.0, 5.0 / max(brightest, 1e-4));
}

// 1 where a ray passes the column a ball hops up and down in.
float nearHop(float3 eye, float3 direction, float3 centre)
{
    float3 bottom = float3(centre.x, -1.0 + SphereRadius, centre.z);
    float3 column = float3(0.0, 1.0, 0.0);
    // Closest approach between the ray and the column's centre line.
    float3 w = eye - bottom;
    float b = dot(direction, column);
    float d = dot(direction, w);
    float e = dot(column, w);
    float along = clamp((e - b * d) / max(1.0 - b * b, 1e-4), 0.0, 0.75);
    float t = max(dot(bottom + column * along - eye, direction), 0.0);
    float gap = length(eye + direction * t - bottom - column * along);
    return smoothstep(SphereRadius * 1.5, SphereRadius * 1.1, gap);
}

float3 Render(float2 uv, float2 p)
{
    placeBalls();
    uint2 pixel = (uint2)(uv * Resolution);
    Seed = scramble(pixel.x * 1973u + pixel.y * 9277u + (uint)FrameCount * 26699u);

    float3 eye = float3(sin(Time * 0.05) * 0.25, cos(Time * 0.037) * 0.12, 3.6);
    float3 target = float3(sin(Time * 0.05) * 0.08, 0.0, 0.0);
    int samples = Quality < 0.5 ? 1 : (Quality > 1.5 ? 4 : 2);
    float3 colour = 0.0;
    [loop]
    for (int s = 0; s < samples; s++)
    {
        // Each path goes through a random spot in the pixel, smoothing the edges.
        float2 jitter = (float2(nextRandom(), nextRandom()) - 0.5) * pixelSize();
        colour += tracePath(eye, cameraRay(eye, target, float2(p.x, -p.y) + jitter, 2.25, 0.0));
    }
    colour /= samples;

    // Blend with the last frame to settle the noise: about the last tenth of a second of frames
    // averaged together, short enough that the hopping balls only blur a little, like a camera's.
    // Around the path of a hopping ball the new frame counts for more, so the ball doesn't leave a ghost.
    float3 direction = cameraRay(eye, target, float2(p.x, -p.y), 2.25, 0.0);
    float nearBall = max(nearHop(eye, direction, MirrorCentre), nearHop(eye, direction, GlassCentre));
    float blend = FrameCount < 2.0 ? 1.0 : saturate(DeltaTime * 6.0 + 0.02 + nearBall * 0.45 * saturate(Beat * 8.0));
    return lerp(previous(uv), colour, blend);
}
