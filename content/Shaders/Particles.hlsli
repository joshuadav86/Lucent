// Particle visuals. Besides Render, a particle visual defines three functions:
//
//     void Spawn(inout Particle particle, uint id)     gives a new particle its start
//     void Simulate(inout Particle particle, uint id)  moves it on by one frame (DeltaTime seconds)
//     Sprite Show(Particle particle, uint id)          says where and how it appears on screen
//
// Hundreds of thousands of particles run every frame on the graphics card, each drawn as a soft
// glowing dot and all added together into a layer that Render reads with particles(uv), usually
// with some of the last frame for trails:
//
//     float3 Render(float2 uv, float2 p) { return particles(uv) + previous(uv) * 0.8; }
//
// A "// Particles: 300000" line near the top sets how many run on Normal detail (262144 if it's
// left out). Low runs half as many, High twice and Ultra four times, each drawn dimmer or brighter
// to match, so the picture keeps its brightness and just gets finer. id runs from 0 to
// ParticleCount - 1, and is the same particle every frame.
// Screen positions are in p units, like Render's p: y grows downward.
// Common.hlsli is placed in front of this file before compiling.

struct Particle
{
    float3 position;  // where it is: in p units for flat visuals, or anywhere in a 3D world
    float life;       // seconds left; the engine counts it down and calls Spawn when it runs out
    float3 velocity;  // free to use, usually units per second
    float age;        // seconds since it was spawned (the engine counts it up)
    float4 data;      // free to use: anything else a particle needs to remember
    float4 push;      // the engine's: how far the mouse has pushed its dot on screen (xy) and how fast (zw)
};

// Start each one from (Sprite)0 so any field left alone is zero.
struct Sprite
{
    float2 position;  // centre on screen, in p units (like Render's p)
    float size;       // radius in pixels of a 1080-line screen (up to 32); 0 hides the particle
    float3 colour;    // linear colour; above 1 glows
    float2 streak;    // optional: stretches the dot into a streak this long, in p units, centred on
                      // position (e.g. velocity * 0.03 for motion blur); same total light either way
};

Texture2D ParticleLayer : register(t3);

// The size of one Sprite.size unit in p units: a pixel of a 1080-line screen. Sprites scale with
// the window, so a visual looks the same at every resolution.
float spritePixel()
{
    return 2.0 / 1080.0;
}

// This frame's particles, as drawn. For Render.
float3 particles(float2 uv)
{
    return ParticleLayer.SampleLevel(LinearClamp, uv, 0).rgb;
}

// ---- Random numbers ----

uint pcgHash(uint v)
{
    uint state = v * 747796405u + 2891336453u;
    uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
    return (word >> 22u) ^ word;
}

// A random number from 0 to 1 for this particle, new every frame. Different salts give
// unrelated numbers, so use one per random choice.
float random(uint id, float salt)
{
    return pcgHash(id ^ pcgHash(asuint(salt) ^ pcgHash((uint)FrameCount))) / 4294967295.0;
}

// A random number from 0 to 1 that stays the same for this particle: its personality.
float fixedRandom(uint id, float salt)
{
    return pcgHash(id ^ pcgHash(asuint(salt) ^ 0x9E3779B9u)) / 4294967295.0;
}

// A random direction (length 1), and a random point inside a sphere of radius 1.
float3 randomDirection(uint id, float salt)
{
    float z = random(id, salt) * 2.0 - 1.0;
    float angle = random(id, salt + 17.0) * 2.0 * PI;
    float r = sqrt(max(1.0 - z * z, 0.0));
    return float3(r * cos(angle), r * sin(angle), z);
}

float3 randomInSphere(uint id, float salt)
{
    return randomDirection(id, salt) * pow(random(id, salt + 31.0), 1.0 / 3.0);
}

// ---- 3D ----

// Where a point in a 3D world appears to a camera at eye looking towards target (the same camera
// as cameraRay). xy is the position on screen in p units, z the distance in front of the camera;
// z at or below 0 means it's behind the camera.
float3 project(float3 position, float3 eye, float3 target, float zoom, float roll)
{
    float3 forward = normalize(target - eye);
    float3 side = normalize(float3(forward.z, 0.0, -forward.x));
    float3 up = cross(forward, side);
    float3 v = position - eye;
    float depth = dot(v, forward);
    float2 onScreen = float2(dot(v, side), dot(v, up)) * zoom / max(depth, 1e-4);
    return float3(rotate(onScreen, -roll), depth);
}

// Blurs a 3D particle's dot the way a camera lens blurs things nearer or further than where it's
// focused, so a cloud of particles gets depth. depth is project()'s z; blur sets how strongly
// (try 0.02). The dot dims as it grows, so it keeps the same total light.
void focus(inout Sprite sprite, float depth, float focusDepth, float blur)
{
    float spread = min(blur * abs(1.0 / max(depth, 1e-3) - 1.0 / focusDepth), 0.03) / spritePixel();
    float grown = sqrt(sprite.size * sprite.size + spread * spread);
    sprite.colour *= sprite.size * sprite.size / max(grown * grown, 1e-6);
    sprite.size = grown;
}

// ---- Flow ----

float hash3d(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

// Smooth 3D noise, -1..1.
float noise3(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    float3 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
    float a = lerp(lerp(hash3d(i), hash3d(i + float3(1, 0, 0)), u.x),
                   lerp(hash3d(i + float3(0, 1, 0)), hash3d(i + float3(1, 1, 0)), u.x), u.y);
    float b = lerp(lerp(hash3d(i + float3(0, 0, 1)), hash3d(i + float3(1, 0, 1)), u.x),
                   lerp(hash3d(i + float3(0, 1, 1)), hash3d(i + float3(1, 1, 1)), u.x), u.y);
    return lerp(a, b, u.z) * 2.0 - 1.0;
}

float3 noiseField(float3 p)
{
    return float3(noise3(p), noise3(p + float3(31.4, 17.3, 5.9)), noise3(p + float3(-11.7, 43.1, 23.3)));
}

// A swirling 3D flow with no sources or sinks: particles that follow it curl and fold like smoke
// without bunching up. Scale p to change the size of the swirls; roughly -3..3 per axis.
float3 curlNoise(float3 p)
{
    const float e = 0.08;
    float3 x0 = noiseField(p - float3(e, 0, 0));
    float3 x1 = noiseField(p + float3(e, 0, 0));
    float3 y0 = noiseField(p - float3(0, e, 0));
    float3 y1 = noiseField(p + float3(0, e, 0));
    float3 z0 = noiseField(p - float3(0, 0, e));
    float3 z1 = noiseField(p + float3(0, 0, e));
    return float3((y1.z - y0.z) - (z1.y - z0.y),
                  (z1.x - z0.x) - (x1.z - x0.z),
                  (x1.y - x0.y) - (y1.x - y0.x)) / (2.0 * e);
}

// The same for flat visuals: a 2D swirling flow. t moves it along smoothly over time.
float2 curlNoise2(float2 p, float t)
{
    const float e = 0.08;
    float dx = noise3(float3(p + float2(e, 0), t)) - noise3(float3(p - float2(e, 0), t));
    float dy = noise3(float3(p + float2(0, e), t)) - noise3(float3(p - float2(0, e), t));
    return float2(dy, -dx) / (2.0 * e);
}

// ---- Drawing (used by the engine) ----

struct SpriteVertex
{
    float4 position : SV_Position;
    float2 corner : TEXCOORD0;  // across the dot's square, which is 4 radii wide: -1..1, plus the
                                // streak's half length along x
    float3 colour : TEXCOORD1;
    float reach : TEXCOORD2;    // the streak's half length, in the same units as corner
};

// Each particle is a soft round glow (or a streak of one), added onto everything drawn before it.
float4 PSSprite(SpriteVertex input) : SV_Target
{
    float2 offset = float2(max(abs(input.corner.x) - input.reach, 0.0), input.corner.y);
    float fade = exp(-dot(offset, offset) * 8.0);
    return float4(input.colour * fade, 1.0);
}
