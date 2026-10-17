// Name: Volcanic Eruption
// A volcano roars in the night, hurling fountains of glowing lava bombs that arc and crash down its slopes while a towering cloud of lit ash billows overhead and lightning crackles inside it; every beat sets off a fresh surge.
// Family: Particles
// Tags: Fire, Intense, Dark
// Particles: 262144

// The world has y pointing up. The volcano is a cone round the y axis with its crater on top.
static const float ConeApex = 1.42;    // where the cone's sides would meet if it had no crater
static const float ConeSlope = 0.7; // height lost per unit outwards
static const float RimHeight = 1.0;    // the crater rim
static const float Gravity = 4.2;

// How the particles are shared out: lava bombs, then ash, then lightning.
static const float BombShare = 0.32;
static const float AshShare = 0.64;

float shareOf(uint id)
{
    return (id + 0.5) / ParticleCount;
}

// The height of the mountain's surface at distance r from its middle.
float groundHeight(float r)
{
    return max(ConeApex - ConeSlope * max(r, 0.0), 0.0);
}

float rimRadius()
{
    return (ConeApex - RimHeight) / ConeSlope;
}

// Where a falling bomb comes to rest: the slopes, or the lava lake inside the crater.
float landingHeight(float r)
{
    return r < rimRadius() ? RimHeight - 0.08 : groundHeight(r);
}

// ---- Lightning ----

// Bolts strike inside the plume in short slots; louder treble makes them more likely.
float boltSlot()
{
    return floor(Time * 2.3);
}

float boltFlash()
{
    float slot = boltSlot();
    float age = Time - slot / 2.3;
    float chance = hash(float2(slot, 3.1));
    float strikes = chance < 0.18 + Treble * 0.35 + Beat * 0.15 ? 1.0 : 0.0;
    // A real flash stutters: two or three quick pulses, then gone.
    float stutter = 0.55 + 0.45 * sin(age * 70.0 + chance * 20.0);
    return strikes * exp(-age * 7.0) * stutter;
}

// A corner of the current bolt's jagged path, i from 0 (top) to 9.
float3 boltCorner(float i)
{
    float slot = boltSlot();
    float3 start = float3((hash(float2(slot, 1.0)) - 0.5) * 2.4, 4.6 + hash(float2(slot, 2.0)) * 0.6, (hash(float2(slot, 5.0)) - 0.5) * 1.2);
    float3 finish = float3(start.x + (hash(float2(slot, 6.0)) - 0.5) * 2.0, 2.4 + hash(float2(slot, 7.0)) * 0.5, start.z + (hash(float2(slot, 8.0)) - 0.5));
    float3 jag = float3(hash(float2(slot, i + 11.0)), hash(float2(slot, i + 31.0)), hash(float2(slot, i + 51.0))) - 0.5;
    float edge = (i < 0.5 || i > 8.5) ? 0.0 : 1.0;
    return lerp(start, finish, i / 9.0) + jag * float3(0.45, 0.15, 0.3) * edge;
}

float3 boltMiddle()
{
    return (boltCorner(0.0) + boltCorner(9.0)) * 0.5;
}

// ---- Lava bombs ----

// A bomb waits unseen in the crater until it's thrown. Every beat throws a share of the
// waiting ones (more with the bass), and a gentle trickle goes up between beats.
void launchBomb(inout Particle particle, uint id)
{
    float angle = random(id, 21.0) * 2.0 * PI;
    float spread = pow(random(id, 22.0), 0.7);
    float power = 0.75 + Bass * 0.35 + Beat * 0.25;
    float2 outwards = float2(cos(angle), sin(angle));
    particle.position = float3(outwards.x * spread * 0.25, RimHeight - 0.05, outwards.y * spread * 0.25);
    particle.velocity = float3(outwards.x * (0.2 + spread * 1.4), (2.6 + random(id, 23.0) * 2.4) * power, outwards.y * (0.2 + spread * 1.4));
    particle.data = float4(BeatCount, 1.0, 1.0, 0.0);
    particle.age = 0.0;
    particle.life = 12.0;
}

void Spawn(inout Particle particle, uint id)
{
    float share = shareOf(id);
    particle.velocity = 0.0;
    particle.data = 0.0;
    if (share < BombShare)
    {
        // Waiting in the crater (data.y = 0 means not yet thrown).
        particle.position = float3(0.0, RimHeight - 0.2, 0.0);
        particle.data = float4(BeatCount, 0.0, 0.0, 0.0);
        particle.life = 1e6;
        return;
    }
    if (share < BombShare + AshShare)
    {
        // Ash leaves the crater in a hot column that slows, spreads and drifts with the wind.
        float angle = random(id, 1.0) * 2.0 * PI;
        float r = sqrt(random(id, 2.0)) * 0.3;
        particle.position = float3(cos(angle) * r, RimHeight + random(id, 3.0) * 0.1, sin(angle) * r);
        particle.velocity = float3(cos(angle) * r, 1.4 + random(id, 4.0) * 1.0 + Bass * 0.6, sin(angle) * r);
        particle.life = lerp(7.0, 14.0, random(id, 5.0));
        if (ParticleReset > 0.5)
        {
            // On the first frame, scatter the ash as if it had been rising for a while.
            float rise = random(id, 6.0);
            particle.position += float3(rise * 1.2 * (random(id, 7.0) - 0.3), rise * 4.0, rise * (random(id, 8.0) - 0.5)) ;
            particle.life *= 1.0 - rise * 0.8;
            particle.age = rise * 8.0;
        }
        return;
    }
    // Lightning particles never move on their own; Show places them along the current bolt.
    particle.position = 0.0;
    particle.life = 1e6;
}

void Simulate(inout Particle particle, uint id)
{
    float share = shareOf(id);
    float dt = min(DeltaTime, 0.05);

    if (share < BombShare)
    {
        if (particle.data.y < 0.5)
        {
            // Waiting. A new beat throws some of us; a trickle goes up anyway.
            bool thrown = random(id, 24.0) < dt * 0.06;
            if (particle.data.x != BeatCount)
            {
                particle.data.x = BeatCount;
                thrown = thrown || random(id, 25.0) < 0.013 + Bass * 0.025;
            }
            if (thrown)
            {
                launchBomb(particle, id);
            }
            return;
        }

        if (particle.data.y < 1.5)
        {
            // In flight: a little air drag, gravity, and a slow tumble of its glow.
            particle.velocity.y -= Gravity * dt;
            particle.velocity *= 1.0 - 0.12 * dt;
            particle.position += particle.velocity * dt;
            float r = length(particle.position.xz);
            if (particle.position.y < landingHeight(r) && particle.velocity.y < 0.0)
            {
                // Landed: roll a little way down the slope, cooling as it goes.
                particle.position.y = landingHeight(r) + 0.01;
                float2 down = r > 1e-3 ? particle.position.xz / r : float2(1.0, 0.0);
                particle.velocity = float3(down.x, -ConeSlope, down.y) * (0.25 + random(id, 26.0) * 0.4);
                particle.data.y = 2.0;
                particle.life = 1.5 + random(id, 27.0) * 2.5;
            }
        }
        else
        {
            particle.velocity *= 1.0 - 1.4 * dt;
            particle.position += particle.velocity * dt;
            particle.position.y = landingHeight(length(particle.position.xz)) + 0.01;
        }
        // Lava cools from white-hot to dull red over a few seconds.
        particle.data.z *= exp(-dt * (particle.data.y > 1.5 ? 0.9 : 0.35));
        if (particle.life < 0.05)
        {
            // Back to waiting in the crater.
            particle.position = float3(0.0, RimHeight - 0.2, 0.0);
            particle.velocity = 0.0;
            particle.data = float4(BeatCount, 0.0, 0.0, 0.0);
            particle.life = 1e6;
        }
        return;
    }

    if (share < BombShare + AshShare)
    {
        // Ash: buoyant at first, then it slows, spreads into a mushrooming cloud and drifts downwind.
        float height = particle.position.y - RimHeight;
        float3 swirl = curlNoise(particle.position * 0.7 + float3(0.0, -Time * 0.15, Time * 0.05));
        float lift = 1.3 * exp(-height * 0.5) + 0.15 + Bass * 0.4;
        float3 wind = float3(0.1 + height * 0.04, 0.0, 0.03);
        float3 spread = float3(particle.position.x, 0.0, particle.position.z) * smoothstep(1.0, 3.5, height) * 0.5;
        float3 wanted = float3(0.0, lift, 0.0) + wind + spread + swirl * (0.35 + Mid * 0.35) * (0.4 + saturate(height * 0.4));
        particle.velocity = lerp(particle.velocity, wanted, saturate(dt * 1.2));
        particle.position += particle.velocity * dt;
    }
}

// ---- Camera ----

float3 cameraEye()
{
    float angle = Time * 0.035 + 0.3;
    return float3(sin(angle) * 4.6, 0.55 + sin(Time * 0.05) * 0.2, -cos(angle) * 4.6);
}

float3 cameraTarget()
{
    return float3(0.0, 1.75, 0.0);
}

static const float Zoom = 1.45;

float3 lavaColour(float heat)
{
    // From dull red through orange to yellow-white, like cooling rock.
    float3 colour = float3(1.0, 0.18, 0.02) * smoothstep(0.0, 0.35, heat);
    colour += float3(0.0, 0.32, 0.06) * smoothstep(0.25, 0.7, heat);
    colour += float3(0.2, 0.3, 0.35) * smoothstep(0.65, 1.0, heat);
    return colour * (0.25 + heat * heat * 3.0);
}

Sprite Show(Particle particle, uint id)
{
    float share = shareOf(id);
    float3 eye = cameraEye();
    float3 target = cameraTarget();
    Sprite sprite = (Sprite)0;

    float3 position = particle.position;
    float3 colour = 0.0;
    float size = 1.0;
    float3 trail = 0.0;

    if (share < BombShare)
    {
        if (particle.data.y < 0.5)
        {
            return sprite;
        }
        float heat = particle.data.z;
        float big = fixedRandom(id, 30.0);
        colour = lavaColour(heat) * (0.25 + big * 0.45) * saturate(particle.life * 2.0);
        size = 1.4 + big * big * 3.2;
        trail = particle.velocity * 0.05;
    }
    else if (share < BombShare + AshShare)
    {
        // Ash glows orange where the crater lights it from below and turns to grey smoke higher up.
        float height = position.y - RimHeight;
        float fromVent = length(position - float3(0.0, RimHeight, 0.0));
        float3 underlit = float3(1.0, 0.3, 0.06) * exp(-fromVent * 0.9) * (0.6 + Bass * 1.2 + Beat * 0.6);
        float3 smoke = float3(0.12, 0.07, 0.08) * (0.3 + 0.7 * saturate(height * 0.3));
        // Lightning lights the cloud around it in cold blue-white.
        float3 toBolt = position - boltMiddle();
        float3 flashlit = float3(0.6, 0.7, 1.0) * boltFlash() * exp(-dot(toBolt, toBolt) * 0.6) * 2.5;
        float fade = saturate(particle.age * 0.8) * saturate(particle.life * 0.4);
        colour = (underlit + smoke + flashlit) * fade * 0.009;
        size = 5.0 + saturate(height * 0.3) * 16.0 * (0.6 + fixedRandom(id, 31.0) * 0.8);
        trail = particle.velocity * 0.03;
    }
    else
    {
        // Lightning: spread these out along the bolt's jagged path, with a fork halfway down.
        float flash = boltFlash();
        if (flash < 0.02)
        {
            return sprite;
        }
        float along = fixedRandom(id, 32.0);
        float fork = fixedRandom(id, 33.0);
        float3 a;
        float3 b;
        float t;
        if (fork < 0.7)
        {
            float k = along * 9.0;
            float i = min(floor(k), 8.0);
            a = boltCorner(i);
            b = boltCorner(i + 1.0);
            t = k - i;
        }
        else
        {
            // A fainter branch splitting off the fourth corner.
            float3 root = boltCorner(4.0);
            float slot = boltSlot();
            float3 bend = float3(hash(float2(slot, 71.0)) - 0.2, -0.5, hash(float2(slot, 72.0)) - 0.5);
            a = along < 0.5 ? root : root + bend * 0.5;
            b = along < 0.5 ? root + bend * 0.5 : root + bend + float3(0.3, -0.3, 0.1);
            t = frac(along * 2.0);
            flash *= 0.5;
        }
        position = lerp(a, b, t);
        colour = float3(0.7, 0.8, 1.0) * flash * 1.6;
        size = 1.0;
    }

    float3 seen = project(position, eye, target, Zoom, 0.0);
    float3 seenBefore = project(position - trail, eye, target, Zoom, 0.0);
    sprite.position = float2(seen.x, -seen.y);
    sprite.streak = float2(seen.x - seenBefore.x, seenBefore.y - seen.y);
    sprite.size = seen.z > 0.3 ? size : 0.0;
    sprite.colour = colour;
    focus(sprite, seen.z, length(eye - target), 0.04);
    return sprite;
}

// ---- The mountain, ground and sky ----

// Ray against the cone (below the rim). Returns the distance, or -1.
float coneHit(float3 origin, float3 direction)
{
    float q = 1.0 / (ConeSlope * ConeSlope);
    float h = ConeApex - origin.y;
    float a = dot(direction.xz, direction.xz) - q * direction.y * direction.y;
    float b = dot(origin.xz, direction.xz) + q * direction.y * h;
    float c = dot(origin.xz, origin.xz) - q * h * h;
    float d = b * b - a * c;
    if (d < 0.0 || abs(a) < 1e-6)
    {
        return -1.0;
    }
    d = sqrt(d);
    float t0 = (-b - d) / a;
    float t1 = (-b + d) / a;
    float near = min(t0, t1);
    float far = max(t0, t1);
    float y0 = origin.y + direction.y * near;
    if (near > 0.0 && y0 > 0.0 && y0 < RimHeight)
    {
        return near;
    }
    float y1 = origin.y + direction.y * far;
    if (far > 0.0 && y1 > 0.0 && y1 < RimHeight)
    {
        return far;
    }
    return -1.0;
}

// How much lava runs through this spot on the mountain's side.
float lavaRivers(float3 position)
{
    float r = length(position.xz);
    float angle = atan2(position.z, position.x);
    float wander = noise(float2(r * 3.0, angle * 2.0)) * 0.9 + sin(r * 6.0 + angle * 3.0) * 0.12;
    float lane = angle / (2.0 * PI) * 9.0 + wander;
    float which = floor(lane);
    float live = step(0.45, hash(float2(which, 4.0)));
    // Rivers narrow and dim towards the foot of the mountain.
    float width = 0.13 * (1.0 - smoothstep(0.7, 2.3, r));
    float river = smoothstep(width, width * 0.3, abs(frac(lane) - 0.5)) * live;
    float flow = 0.6 + 0.4 * noise(float2(r * 9.0 - Time * 1.1, which * 7.0));
    return river * flow * smoothstep(2.4, 0.9, r);
}

float3 craterLight()
{
    return float3(1.0, 0.4, 0.1) * (1.5 + Bass * 2.5 + Beat * 1.5);
}

float3 sky(float3 direction)
{
    float up = saturate(direction.y);
    float3 colour = lerp(float3(0.05, 0.03, 0.05), float3(0.004, 0.006, 0.02), pow(up, 0.5));
    // Stars, twinkling faintly.
    float2 cell = float2(atan2(direction.z, direction.x) * 120.0, direction.y * 120.0);
    float2 id = floor(cell);
    float star = step(0.985, hash(id)) * smoothstep(0.35, 0.0, length(frac(cell) - 0.5));
    colour += star * (0.4 + 0.4 * sin(Time * 3.0 + hash(id + 3.0) * 30.0)) * smoothstep(0.02, 0.2, direction.y);
    // A red glow on the underside of the sky, reflected off the ash.
    colour += float3(0.25, 0.06, 0.02) * exp(-abs(direction.y - 0.12) * 6.0) * (0.3 + Bass * 0.4);
    return colour;
}

float3 Render(float2 uv, float2 p)
{
    float3 eye = cameraEye();
    float3 target = cameraTarget();
    float3 direction = cameraRay(eye, target, float2(p.x, -p.y), Zoom, 0.0);

    float3 colour = sky(direction);
    float t = coneHit(eye, direction);
    float3 vent = float3(0.0, RimHeight + 0.15, 0.0);
    if (t > 0.0)
    {
        float3 position = eye + direction * t;
        float r = length(position.xz);
        float angle = atan2(position.z, position.x);
        float3 normal = normalize(float3(position.x, (ConeApex - position.y) / (ConeSlope * ConeSlope), position.z));
        // Gullies and ridges running down the slopes.
        float3 around = normalize(float3(-position.z, 0.0, position.x));
        float ridges = noise(float2(angle * 14.0, r * 2.0)) - 0.5 + (fbm(float2(angle * 30.0, r * 8.0)) - 0.5) * 0.6;
        normal = normalize(normal + around * ridges * 0.8);

        // Lit by the glowing crater above, by the faint moon, and by the lava rivers themselves.
        float3 toVent = vent - position;
        float ventDistance = length(toVent);
        float3 lit = craterLight() * saturate(dot(normal, toVent / ventDistance)) * 0.35 / (1.0 + ventDistance * ventDistance);
        lit += float3(0.05, 0.06, 0.1) * saturate(dot(normal, normalize(float3(-0.4, 0.6, 0.5)))) * 0.8;
        // A dull red fill from the glowing cloud overhead.
        lit += float3(0.5, 0.16, 0.06) * (0.4 + 0.6 * normal.y) * (0.5 + Bass * 0.5);
        float3 rock = float3(0.1, 0.085, 0.08) * (0.6 + fbm(float2(angle * 20.0, r * 6.0)) * 0.8);
        colour = rock * lit;

        float river = lavaRivers(position);
        float crust = noise(float2(angle * 60.0, r * 25.0 - Time * 0.8));
        colour += lavaColour(0.45 + crust * 0.3) * river * (0.7 + Bass * 1.0);
        // The rim itself glows from the lava lake inside it.
        colour += float3(1.0, 0.3, 0.05) * smoothstep(RimHeight - 0.12, RimHeight, position.y) * (0.6 + Bass);
    }
    else if (direction.y < 0.0)
    {
        // The plain around the mountain, dark, with a red sheen from the eruption.
        float g = -eye.y / direction.y;
        float3 position = eye + direction * g;
        float r = length(position.xz);
        float3 ground = float3(0.015, 0.012, 0.012) * (0.6 + fbm(position.xz * 1.5) * 0.8);
        ground += float3(0.2, 0.05, 0.01) * exp(-r * 0.5) * (0.3 + Bass * 0.4);
        // Rivers that reached the plain, fading out as they cool.
        ground += lavaColour(0.4) * lavaRivers(float3(position.x * 0.9, 0.0, position.z * 0.9)) * 0.2;
        colour = lerp(ground, colour, saturate(g * 0.02));
    }

    // The glow over the crater and the haze of the column, seen through the air.
    float3 toVent = vent - eye;
    float closest = length(toVent - direction * max(dot(toVent, direction), 0.0));
    colour += craterLight() * (exp(-closest * 3.0) * 0.25 + exp(-closest * 0.8) * 0.03);
    float3 toBolt = boltMiddle() - eye;
    float nearBolt = length(toBolt - direction * max(dot(toBolt, direction), 0.0));
    colour += float3(0.35, 0.4, 0.6) * boltFlash() * exp(-nearBolt * 0.9) * 0.4;

    // Short trails keep the bombs' arcs smooth without smearing the ash.
    return colour + particles(uv) + previous(uv) * 0.35;
}
