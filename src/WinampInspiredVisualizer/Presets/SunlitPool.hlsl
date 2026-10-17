// Name: Sunlit Pool
// A sunny swimming pool from above: every beat drops a ripple, and the waves bend sunlight into dancing caustics on the tiles, ray traced through the water with floating balls bobbing to the bass.
// Family: Ray traced
// Tags: 3D, Water, Calm, Colourful

static const float2 PoolSize = float2(3.2, 6.5);   // half width and half length
static const float PoolDepth = 1.5;
static const float WaterLevel = -0.18;
static const int BallCount = 3;
static const float BallRadius = 0.32;
// The angle one pixel covers, set in Render.
static float PixelAngle;

static const float3 SunDirection = float3(0.42, 0.78, 0.46);   // towards the sun (normalised in use)

// Materials.
static const int Sky = 0;
static const int Deck = 1;
static const int Water = 2;
static const int Tiles = 3;
static const int Ball = 4;

float sinceBeat()
{
    return -log(max(Beat, 1e-4)) / 5.0;
}

float3 sun()
{
    return normalize(SunDirection);
}

// Ripples: each of the last three beats dropped one somewhere in the pool. Older ones are timed
// from an average gap between beats, which is close enough once they've spread and faded.
float ripples(float2 q)
{
    float h = 0.0;
    float age = sinceBeat();
    [unroll]
    for (int k = 0; k < 3; k++)
    {
        float beat = BeatCount - k;
        float2 centre = (float2(hash(float2(beat, 1.7)), hash(float2(beat, 5.3))) * 2.0 - 1.0) * PoolSize * 0.75;
        float t = age + k * 0.55;
        float front = length(q - centre) - 0.3 - t * 1.9;
        h += 0.05 * exp(-t * 1.3) * sin(front * 13.0) * exp(-front * front * 5.0) * step(0.5, BeatCount - k);
    }
    return h;
}

// Height of the water surface above its resting level.
float waterHeight(float2 q)
{
    float amplitude = 0.012 + Bass * 0.022;
    float h = sin(dot(q, float2(0.8, 0.6)) * 3.1 + Time * 1.3) * amplitude;
    h += sin(dot(q, float2(-0.5, 0.86)) * 4.7 + Time * 1.7) * amplitude * 0.7;
    h += sin(dot(q, float2(0.95, -0.31)) * 7.3 + Time * 2.3) * amplitude * 0.45;
    h += sin(dot(q, float2(-0.2, -0.98)) * 11.0 + Time * 2.9) * amplitude * 0.3;
    h += (noise(q * 3.5 + float2(Time * 0.6, -Time * 0.4)) - 0.5) * amplitude * 0.9;
    return h + ripples(q);
}

float3 waterNormal(float2 q)
{
    const float e = 0.01;
    float h = waterHeight(q);
    float dx = waterHeight(q + float2(e, 0.0)) - h;
    float dz = waterHeight(q + float2(0.0, e)) - h;
    return normalize(float3(-dx, e, -dz));
}

float4 ballAt(int i)
{
    float k = i;
    float2 drift = float2(sin(Time * 0.07 + k * 2.1) * 2.2, sin(Time * 0.05 + k * 1.3 + 1.0) * 3.5 + 1.0);
    float bob = sin(Time * 1.4 + k * 2.0) * 0.03 + Bass * 0.06 + Beat * 0.03;
    return float4(drift.x, WaterLevel + BallRadius * 0.35 + bob, drift.y, BallRadius);
}

static const float3 BallColours[BallCount] = { float3(0.95, 0.95, 1.0), float3(1.0, 0.25, 0.4), float3(1.0, 0.75, 0.1) };

float3 ballColour(int i)
{
    return BallColours[i];
}

float sphereHit(float3 origin, float3 direction, float4 sphere)
{
    float3 oc = origin - sphere.xyz;
    float b = dot(oc, direction);
    float c = dot(oc, oc) - sphere.w * sphere.w;
    float h = b * b - c;
    if (h < 0.0)
    {
        return -1.0;
    }
    float t = -b - sqrt(h);
    return t > 1e-3 ? t : -1.0;
}

float sphereShadow(float3 origin, float3 direction, float4 sphere, float softness)
{
    float3 oc = origin - sphere.xyz;
    float b = dot(oc, direction);
    float c = dot(oc, oc) - sphere.w * sphere.w;
    float h = b * b - c;
    float d = sqrt(max(0.0, sphere.w * sphere.w - h)) - sphere.w;
    float t = -b - sqrt(max(h, 0.0));
    return (t < 0.0) ? 1.0 : smoothstep(0.0, 1.0, 2.5 * softness * d / t);
}

// The nearest floating ball along a ray: distance (x) and which one (y), or x < 0.
float2 ballHit(float3 origin, float3 direction, float4 balls[BallCount])
{
    float2 best = float2(-1.0, 0.0);
    [unroll]
    for (int i = 0; i < BallCount; i++)
    {
        float t = sphereHit(origin, direction, balls[i]);
        if (t > 0.0 && (best.x < 0.0 || t < best.x))
        {
            best = float2(t, i);
        }
    }
    return best;
}

float3 skyColour(float3 direction)
{
    float up = saturate(direction.y);
    float3 colour = lerp(float3(0.75, 0.86, 0.95), float3(0.22, 0.45, 0.85), pow(up, 0.6));
    float toSun = saturate(dot(direction, sun()));
    colour += float3(1.0, 0.9, 0.7) * (pow(toSun, 600.0) * 30.0 + pow(toSun, 12.0) * 0.4);
    // Soft clouds drifting by.
    float2 cloudUv = direction.xz / max(direction.y, 0.05) * 0.6 + Time * 0.02;
    colour = lerp(colour, float3(1.0, 1.0, 1.0), smoothstep(0.55, 0.85, fbm(cloudUv)) * 0.6 * up);
    if (direction.y < 0.0)
    {
        // Below the horizon (seen in reflections from low down): the sunny deck and garden.
        colour = lerp(float3(0.6, 0.62, 0.6), float3(0.3, 0.62, 0.68), saturate(-direction.y * 2.0));
    }
    return colour;
}

// Dappled shade from leaves overhead, swaying in the breeze: 0 in shade, 1 in full sun.
float leafShade(float2 q)
{
    float2 sway = float2(sin(Time * 0.5), cos(Time * 0.37)) * 0.15 * (1.0 + Mid);
    float leaves = fbm(q * 0.55 + sway + float2(3.0, 1.0));
    // Mostly sunny over the water, shadier towards one corner of the garden.
    float cover = 0.42 + 0.12 * saturate((q.x + q.y) * 0.08);
    return lerp(0.35, 1.0, smoothstep(cover + 0.06, cover - 0.06, leaves));
}

// Pale stone slabs round the pool, then lawn.
// footprint is roughly how much ground one pixel covers, so thin joints can fade out in the distance.
float3 deckColour(float3 position, float footprint)
{
    float2 slab = position.xz * 0.9;
    float2 joint = abs(frac(slab) - 0.5);
    // Joints fade out in the distance instead of shimmering.
    float blur = footprint * 0.9 * 2.0;
    float grout = smoothstep(0.48 - blur, 0.49, max(joint.x, joint.y)) * saturate(1.0 - blur * 4.0);
    float3 stone = float3(0.86, 0.82, 0.74) * (0.85 + 0.15 * hash(floor(slab))) * (0.92 + 0.08 * noise(position.xz * 4.0));
    // A rounded edge of darker coping stone right round the water.
    float2 edge = abs(position.xz) - PoolSize;
    float rim = smoothstep(0.32, 0.28, max(edge.x, edge.y));
    stone = lerp(stone, float3(0.62, 0.6, 0.56), rim);
    stone *= 1.0 - grout * 0.35;
    float lawn = smoothstep(3.6, 3.7, max(edge.x, edge.y));
    float3 grass = float3(0.1, 0.17, 0.08) * (0.75 + 0.35 * fbm(position.xz * 3.0)) + float3(0.02, 0.03, 0.0) * noise(position.xz * 25.0);
    return lerp(stone, grass, lawn);
}

// Small blue and white tiles, with dark lane lines down the floor.
float3 tileColour(float3 position, float3 normal, float footprint)
{
    float2 surface = abs(normal.y) > 0.5 ? position.xz : (abs(normal.x) > 0.5 ? position.zy : position.xy);
    float2 tile = surface * 5.0;
    float2 joint = abs(frac(tile) - 0.5);
    float blur = footprint * 5.0;
    float grout = smoothstep(0.44 - blur, 0.48, max(joint.x, joint.y)) * saturate(1.0 - blur * 2.0);
    float3 colour = lerp(float3(0.55, 0.82, 0.88), float3(0.75, 0.92, 0.95), hash(floor(tile)) * 0.6);
    if (abs(normal.y) > 0.5)
    {
        float lane = abs(frac(position.x / 1.3 + 0.5) - 0.5) * 1.3;
        colour = lerp(colour, float3(0.05, 0.15, 0.4), smoothstep(0.11, 0.09, lane) * smoothstep(PoolSize.y - 0.4, PoolSize.y - 0.5, abs(position.z)));
    }
    // A band of darker tiles along the top of the walls.
    colour = lerp(colour, float3(0.1, 0.3, 0.55), smoothstep(WaterLevel - 0.15, WaterLevel - 0.1, position.y) * step(abs(normal.y), 0.5));
    return colour * (1.0 - grout * 0.3);
}

// Sunlight focused by the waves onto a point under water. Light that went through the surface near
// the point above it lands spread out or squeezed together; the squeezing is the bright lines.
// Each colour bends by a slightly different amount, so the lines have rainbow edges.
float3 caustics(float3 position)
{
    float depth = WaterLevel - position.y;
    float3 down = refract(-sun(), float3(0.0, 1.0, 0.0), 1.0 / 1.33);
    float2 above = position.xz - down.xz * depth / -down.y;
    const float e = 0.03;
    float3 n0 = waterNormal(above);
    float3 n1 = waterNormal(above + float2(e, 0.0));
    float3 n2 = waterNormal(above + float2(0.0, e));
    float3 light = 0.0;
    [unroll]
    for (int channel = 0; channel < 3; channel++)
    {
        float eta = 1.0 / (1.315 + channel * 0.02);
        float3 r0 = refract(-sun(), n0, eta);
        float3 r1 = refract(-sun(), n1, eta);
        float3 r2 = refract(-sun(), n2, eta);
        float2 l0 = r0.xz * depth / -r0.y;
        float2 l1 = float2(e, 0.0) + r1.xz * depth / -r1.y;
        float2 l2 = float2(0.0, e) + r2.xz * depth / -r2.y;
        float2 a = l1 - l0;
        float2 b = l2 - l0;
        float area = abs(a.x * b.y - a.y * b.x);
        light[channel] = min(e * e / max(area, 1e-6), 8.0);
    }
    return light;
}

// Inside the pool below the water: the nearest wall or floor.
float poolInside(float3 origin, float3 direction, out float3 normal)
{
    float3 t = (sign(direction) * float3(PoolSize.x, 0.0, PoolSize.y) - origin * float3(1.0, 0.0, 1.0)) / direction;
    t.y = direction.y < 0.0 ? (-PoolDepth - origin.y) / direction.y : 1e5;
    float nearest = min(t.x, min(t.y, t.z));
    normal = nearest == t.x ? float3(-sign(direction.x), 0.0, 0.0)
           : (nearest == t.y ? float3(0.0, 1.0, 0.0) : float3(0.0, 0.0, -sign(direction.z)));
    return nearest;
}

// Light on a floating ball: sun, sky and a mirror-like shine.
float3 shadeBall(int i, float3 position, float3 direction, float4 balls[BallCount], bool underwater)
{
    float3 normal = normalize(position - balls[i].xyz);
    float3 mirrored = reflect(direction, normal);
    float facing = saturate(dot(normal, -direction));
    float3 colour;
    if (i == 0)
    {
        // A chrome ball: a perfect mirror of the sky and deck.
        colour = skyColour(mirrored) * lerp(0.8, 1.0, pow(1.0 - facing, 5.0));
    }
    else
    {
        float diffuse = saturate(dot(normal, sun()));
        float fresnel = 0.05 + 0.95 * pow(1.0 - facing, 5.0);
        // Stripes like a beach ball.
        float stripe = step(0.5, frac(atan2(normal.z, normal.x) / PI * 1.5 + Time * 0.05 * (i == 1 ? 1.0 : -1.0)));
        float3 albedo = lerp(ballColour(i), float3(0.95, 0.95, 0.92), stripe);
        colour = albedo * (diffuse * 1.1 + 0.25) * (1.0 - fresnel) + skyColour(mirrored) * fresnel;
        colour += pow(saturate(dot(mirrored, sun())), 200.0) * 4.0;
    }
    if (underwater)
    {
        colour *= float3(0.45, 0.8, 0.85);
    }
    return colour;
}

// What a ray sees under the water: tiles lit by caustics, or the underwater part of a ball, tinted by the water.
float3 underwater(float3 origin, float3 direction, float4 balls[BallCount], float distanceSoFar)
{
    float3 normal;
    float t = poolInside(origin, direction, normal);
    float2 ball = ballHit(origin, direction, balls);
    if (ball.x > 0.0 && ball.x < t)
    {
        float3 colour = shadeBall((int)ball.y, origin + direction * ball.x, direction, balls, true);
        return colour * exp(-ball.x * float3(0.45, 0.12, 0.08));
    }
    float3 position = origin + direction * t;
    float3 albedo = tileColour(position, normal, (distanceSoFar + t) * PixelAngle / max(abs(dot(direction, normal)), 0.15));
    float3 down = refract(-sun(), float3(0.0, 1.0, 0.0), 1.0 / 1.33);
    float shadow = 1.0;
    [unroll]
    for (int i = 0; i < BallCount; i++)
    {
        shadow *= sphereShadow(position, -down, balls[i], 6.0);
    }
    float sunLight = saturate(dot(normal, -down)) * 0.7 + 0.15;
    float shade = leafShade(position.xz - down.xz * (WaterLevel - position.y) / -down.y);
    float3 light = caustics(position) * sunLight * shadow * shade * (0.85 + Level * 0.5) + float3(0.18, 0.3, 0.35);
    // The further light travels through the water, the more red it loses.
    float travelled = t + (WaterLevel - position.y);
    return albedo * light * exp(-travelled * float3(0.4, 0.1, 0.07));
}

float3 Render(float2 uv, float2 p)
{
    float4 balls[BallCount];
    [unroll]
    for (int b = 0; b < BallCount; b++)
    {
        balls[b] = ballAt(b);
    }

    // Drifting slowly round above one end of the pool, looking along the water.
    float orbit = sin(Time * 0.045) * 0.7;
    float3 eye = float3(sin(orbit) * 5.0, 3.8 + 0.4 * sin(Time * 0.1), cos(orbit) * 7.0);
    float3 target = float3(sin(orbit) * 0.5, -1.0, cos(orbit) * 0.5);
    float3 direction = cameraRay(eye, target, float2(p.x, -p.y), 1.35, 0.0);
    PixelAngle = pixelSize() / 1.35;

    float3 colour = skyColour(direction);
    float2 ball = ballHit(eye, direction, balls);
    float deckT = direction.y < 0.0 ? -eye.y / direction.y : 1e5;
    float3 deckPoint = eye + direction * deckT;
    bool overPool = all(abs(deckPoint.xz) < PoolSize);

    if (ball.x > 0.0 && ball.x < deckT + (overPool ? 1.0 : 0.0) && (eye + direction * ball.x).y > WaterLevel + waterHeight((eye + direction * ball.x).xz))
    {
        float3 position = eye + direction * ball.x;
        colour = shadeBall((int)ball.y, position, direction, balls, false);
    }
    else if (deckT < 1e4 && !overPool)
    {
        float shadow = 1.0;
        float shade = leafShade(deckPoint.xz);
        colour = deckColour(deckPoint, deckT * PixelAngle / max(-direction.y, 0.1)) * (saturate(dot(float3(0.0, 1.0, 0.0), sun())) * 1.05 * shade + 0.25) * shadow;
    }
    else if (overPool)
    {
        // Through the opening: a strip of wall above the water, or the water itself.
        float waterT = (WaterLevel - eye.y) / direction.y;
        float3 waterPoint = eye + direction * waterT;
        if (any(abs(waterPoint.xz) > PoolSize))
        {
            float3 normal;
            float t = poolInside(deckPoint, direction, normal);
            float3 wallPoint = deckPoint + direction * t;
            colour = tileColour(wallPoint, normal, (deckT + t) * PixelAngle / max(abs(dot(direction, normal)), 0.15)) * 0.7;
        }
        else
        {
            float3 normal = waterNormal(waterPoint.xz);
            float facing = saturate(dot(normal, -direction));
            float fresnel = 0.02 + 0.98 * pow(1.0 - facing, 5.0);
            float3 mirrored = reflect(direction, normal);
            float3 reflection = skyColour(mirrored);
            float2 mirroredBall = ballHit(waterPoint, mirrored, balls);
            if (mirroredBall.x > 0.0)
            {
                reflection = shadeBall((int)mirroredBall.y, waterPoint + mirrored * mirroredBall.x, mirrored, balls, false);
            }
            float3 refracted = refract(direction, normal, 1.0 / 1.33);
            float3 below = underwater(waterPoint, refracted, balls, waterT);
            colour = lerp(below, reflection, fresnel);
            // Glints of sun on the wave crests, sparkling more with the treble.
            colour += float3(1.0, 0.95, 0.8) * pow(saturate(dot(mirrored, sun())), 900.0) * (6.0 + Treble * 20.0);
        }
    }
    return colour * 0.85;
}
