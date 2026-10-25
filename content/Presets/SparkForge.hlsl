// Name: Spark Forge
// In a dark forge a white-hot steel bar lies on a gleaming anvil; on every beat the hammer comes down and throws a shower of sparks that skitter and bounce across the polished floor, mirrored in it as they fade, while the metal's glow flares with the bass.
// Family: Ray traced
// Tags: Fire, Intense, Dark, 3D
// Added: 2026-10-17 17:15
// Particles: 262144

// The anvil, hammer, bar and floor are ray traced; the sparks are particles laid over the top.
// Particles come in twins: the even one of each pair is a spark, the odd one draws that spark's
// reflection in the floor. Both twins draw the same random numbers, so they move identically.
// World y is up; the floor is y = 0.
static const float AnvilTop = 0.85;
static const float BarRadius = 0.045;
static const float3 BarStart = float3(-0.42, AnvilTop + BarRadius, 0.02);
static const float3 BarEnd = float3(0.62, AnvilTop + BarRadius, -0.04);
static const float3 StrikePoint = float3(0.12, AnvilTop + BarRadius * 2.0, 0.0);
static const float Zoom = 1.7;

float sinceBeat()
{
    return -log(max(Beat, 1e-4)) / 5.0;
}

// How hot the bar is, 0..1: it flares on each strike and with the bass.
float barHeat()
{
    return saturate(0.62 + Bass * 0.25 + Beat * 0.2);
}

// Glowing metal: deep red through orange and yellow to white.
float3 heatColour(float t)
{
    float3 colour = lerp(float3(0.0, 0.0, 0.0), float3(0.7, 0.05, 0.01), smoothstep(0.0, 0.25, t));
    colour = lerp(colour, float3(1.6, 0.35, 0.04), smoothstep(0.2, 0.5, t));
    colour = lerp(colour, float3(2.6, 1.3, 0.3), smoothstep(0.45, 0.75, t));
    colour = lerp(colour, float3(3.4, 2.8, 1.8), smoothstep(0.75, 1.0, t));
    return colour;
}

float3 cameraEye()
{
    float swing = sin(Time * 0.045) * 0.55 - 0.2;
    return float3(sin(swing) * 2.1 + 0.2, 1.15 + sin(Time * 0.06) * 0.12, -cos(swing) * 2.1);
}

float3 cameraTarget()
{
    return float3(0.2, 0.68, 0.0);
}

// ---- Sparks ----

void Spawn(inout Particle particle, uint id)
{
    // Idle until a hammer blow throws it.
    particle.position = float3(0.0, -10.0, 0.0);
    particle.velocity = 0.0;
    particle.life = 1e6;
    particle.data = float4(BeatCount, 0.0, 0.0, 0.0);
}

void Simulate(inout Particle particle, uint id)
{
    uint spark = id >> 1;
    float dt = min(DeltaTime, 0.04);
    bool active = particle.data.y > 0.5;

    // A new beat: a share of the idle sparks fly off the strike, more with a heavier bass.
    if (BeatCount != particle.data.x)
    {
        particle.data.x = BeatCount;
        float share = 0.012 + Bass * 0.016;
        if (!active && random(spark, 1.0) < share)
        {
            float3 direction = randomDirection(spark, 2.0);
            direction.y = abs(direction.y) * 0.7 + 0.15;
            direction = normalize(direction);
            float speed = 1.2 + 4.2 * pow(random(spark, 3.0), 2.0);
            particle.position = StrikePoint + (float3(random(spark, 4.0), 0.0, random(spark, 5.0)) - 0.5) * 0.06;
            particle.velocity = direction * speed;
            particle.life = lerp(0.7, 2.4, random(spark, 6.0));
            particle.data.y = 1.0;
            particle.data.z = lerp(0.75, 1.0, random(spark, 7.0));
            particle.age = 0.0;
            return;
        }
    }
    // Between blows a few flakes of glowing scale lift off the bar.
    if (!active && random(spark, 8.0) < 0.00006 * (1.0 + Bass * 2.0))
    {
        float along = random(spark, 9.0);
        particle.position = lerp(BarStart, BarEnd, along) + float3(0.0, BarRadius, 0.0);
        particle.velocity = float3((random(spark, 10.0) - 0.5) * 0.4, 0.4 + random(spark, 11.0) * 0.5, (random(spark, 12.0) - 0.5) * 0.4);
        particle.life = lerp(0.6, 1.4, random(spark, 13.0));
        particle.data.y = 1.0;
        particle.data.z = 0.55;
        particle.age = 0.0;
        return;
    }
    if (!active)
    {
        particle.life = 1e6;
        return;
    }

    // Flight: gravity, a little air drag, and cooling.
    particle.velocity.y -= 6.5 * dt;
    particle.velocity *= exp(-dt * 0.5);
    float3 before = particle.position;
    particle.position += particle.velocity * dt;
    particle.data.z *= exp(-dt * 1.3);

    // Bounce off the anvil's face and skitter across the floor.
    bool overAnvil = abs(particle.position.x) < 0.5 && abs(particle.position.z) < 0.2;
    if (overAnvil && before.y >= AnvilTop && particle.position.y < AnvilTop)
    {
        particle.position.y = AnvilTop + (AnvilTop - particle.position.y) * 0.3;
        particle.velocity.y = abs(particle.velocity.y) * 0.35;
        particle.velocity.xz *= 0.7;
    }
    if (particle.position.y < 0.0)
    {
        particle.position.y = -particle.position.y * 0.3;
        particle.velocity.y = abs(particle.velocity.y) * 0.4;
        particle.velocity.xz *= 0.65;
        // A spark sometimes bursts in two as it hits; here it just flares a little.
        particle.data.z = min(particle.data.z * 1.08, 1.0);
    }
    // Burnt out: back to idle.
    if (particle.data.z < 0.12 || particle.life < 0.05)
    {
        particle.data.y = 0.0;
        particle.position = float3(0.0, -10.0, 0.0);
        particle.velocity = 0.0;
        particle.life = 1e6;
    }
}

// Ray against the box that roughly holds the anvil, for hiding sparks behind it.
bool behindAnvil(float3 from, float3 to)
{
    float3 direction = to - from;
    float3 boxes[2] = { float3(0.05, 0.75, 0.0), float3(0.0, 0.32, 0.0) };
    float3 sizes[2] = { float3(0.5, 0.1, 0.2), float3(0.3, 0.32, 0.18) };
    bool hidden = false;
    [unroll]
    for (int i = 0; i < 2; i++)
    {
        float3 inverse = 1.0 / direction;
        float3 t1 = (boxes[i] - sizes[i] - from) * inverse;
        float3 t2 = (boxes[i] + sizes[i] - from) * inverse;
        float3 entries = min(t1, t2);
        float3 exits = max(t1, t2);
        float enter = max(max(entries.x, entries.y), entries.z);
        float leave = min(min(exits.x, exits.y), exits.z);
        hidden = hidden || (enter < leave && enter > 0.0 && enter < 0.99);
    }
    return hidden;
}

Sprite Show(Particle particle, uint id)
{
    Sprite sprite = (Sprite)0;
    if (particle.data.y < 0.5)
    {
        return sprite;
    }
    bool mirrored = (id & 1u) == 1u;
    float3 eye = cameraEye();
    float3 target = cameraTarget();
    float3 position = particle.position;
    float3 velocity = particle.velocity;
    if (mirrored)
    {
        position.y = -position.y;
        velocity.y = -velocity.y;
    }
    // A spark is hidden by the anvil; its reflection is hidden where the anvil blocks the floor.
    float3 seenPoint = position;
    if (mirrored)
    {
        float3 toMirror = position - eye;
        seenPoint = eye + toMirror * (eye.y / max(eye.y - position.y, 1e-3));
    }
    if (behindAnvil(eye, seenPoint))
    {
        return sprite;
    }

    float3 flip = float3(1.0, -1.0, 1.0);
    float3 seen = project(position * flip, eye * flip, target * flip, Zoom, 0.0);
    float3 seenBefore = project((position - velocity * 0.028) * flip, eye * flip, target * flip, Zoom, 0.0);
    if (seen.z < 0.2)
    {
        return sprite;
    }

    float heat = particle.data.z;
    float3 colour = heatColour(heat) * (0.8 + heat * 0.9);
    // Sparks crackle as they fly.
    colour *= 0.75 + 0.5 * step(0.5, frac(particle.age * 23.0 + fixedRandom(id >> 1, 14.0)));
    if (mirrored)
    {
        // The floor reflects only part of the light, blurred a touch.
        colour *= 0.3;
    }

    sprite.position = seen.xy;
    sprite.streak = seen.xy - seenBefore.xy;
    sprite.size = mirrored ? 1.6 : 1.05;
    sprite.colour = colour;
    focus(sprite, seen.z, length(eye - target), 0.02);
    return sprite;
}

// ---- The forge ----

float boxDistance(float3 q, float3 centre, float3 size, float round)
{
    float3 d = abs(q - centre) - size + round;
    return length(max(d, 0.0)) + min(max(d.x, max(d.y, d.z)), 0.0) - round;
}

float capsuleDistance(float3 q, float3 a, float3 b, float radius)
{
    float3 pa = q - a;
    float3 ba = b - a;
    float h = saturate(dot(pa, ba) / dot(ba, ba));
    return length(pa - ba * h) - radius;
}

float hammerLift()
{
    // Down on the beat, then raised for the next blow.
    return 0.65 * smoothstep(0.03, 0.38, sinceBeat());
}

// The anvil: a broad foot, a narrow waist, a flat face with a tapering horn, and the hammer.
float anvilDistance(float3 q, out int part)
{
    float foot = boxDistance(q, float3(0.0, 0.11, 0.0), float3(0.42, 0.11, 0.27), 0.02);
    float waistWidth = 0.2 + 0.08 * smoothstep(0.55, 0.25, q.y) + 0.06 * smoothstep(0.55, 0.7, q.y);
    float waist = boxDistance(q, float3(0.0, 0.42, 0.0), float3(waistWidth, 0.21, 0.14), 0.02);
    float face = boxDistance(q, float3(0.0, 0.76, 0.0), float3(0.5, 0.09, 0.19), 0.015);
    float t = saturate((q.x - 0.45) / 0.6);
    float hornRadius = lerp(0.13, 0.015, t);
    float horn = length(float2(q.y - (0.77 - t * t * 0.06), q.z) ) - hornRadius;
    horn = max(horn, max(0.45 - q.x, q.x - 1.05));
    float anvil = min(min(foot, waist), min(face, horn));

    float lift = hammerLift();
    float3 head = StrikePoint + float3(0.0, 0.07 + lift, 0.0);
    float hammer = boxDistance(q, head, float3(0.075, 0.07, 0.13), 0.012);
    float handle = capsuleDistance(q, head, head + float3(0.15, 1.3, 1.1), 0.028);
    float tool = min(hammer, handle);

    part = tool < anvil ? 1 : 0;
    return min(anvil, tool);
}

float3 anvilNormal(float3 q)
{
    const float e = 0.0015;
    int part;
    return normalize(float3(
        anvilDistance(q + float3(e, 0, 0), part) - anvilDistance(q - float3(e, 0, 0), part),
        anvilDistance(q + float3(0, e, 0), part) - anvilDistance(q - float3(0, e, 0), part),
        anvilDistance(q + float3(0, 0, e), part) - anvilDistance(q - float3(0, 0, e), part)));
}

// The hot bar: where a ray hits it (or -1), as a rod with rounded ends.
float barHit(float3 origin, float3 direction, out float3 normal, out float along)
{
    float3 ba = BarEnd - BarStart;
    float3 oa = origin - BarStart;
    float baba = dot(ba, ba);
    float bard = dot(ba, direction);
    float baoa = dot(ba, oa);
    float a = baba - bard * bard;
    float b = baba * dot(oa, direction) - baoa * bard;
    float c = baba * dot(oa, oa) - baoa * baoa - BarRadius * BarRadius * baba;
    float h = b * b - a * c;
    normal = float3(0.0, 1.0, 0.0);
    along = 0.0;
    if (h < 0.0)
    {
        return -1.0;
    }
    float t = (-b - sqrt(h)) / a;
    float y = baoa + t * bard;
    if (y <= 0.0 || y >= baba || t < 1e-3)
    {
        return -1.0;
    }
    float3 q = origin + direction * t;
    along = y / baba;
    normal = normalize(q - (BarStart + ba * along));
    return t;
}

float3 barGlow(float along)
{
    // Hottest where the hammer falls, cooler towards the ends.
    float x = lerp(BarStart.x, BarEnd.x, along);
    float heat = barHeat() * (0.55 + 0.45 * exp(-pow((x - StrikePoint.x) * 3.0, 2.0)));
    return heatColour(heat) * 1.4;
}

// The dark back wall of the forge: soot-black brick lit orange by the fire's open mouth.
float3 forgeBackdrop(float3 origin, float3 direction)
{
    float3 colour = float3(0.004, 0.003, 0.003);
    if (direction.z > 0.01)
    {
        float t = (3.5 - origin.z) / direction.z;
        float3 q = origin + direction * t;
        float2 brick = float2(q.x * 3.2 + floor(q.y * 6.0) * 0.5, q.y * 6.0);
        float2 inBrick = abs(frac(brick) - 0.5);
        float mortar = smoothstep(0.42, 0.47, max(inBrick.x * 1.0, inBrick.y));
        float3 wall = lerp(float3(0.05, 0.025, 0.018), float3(0.012, 0.008, 0.006), mortar) * (0.6 + 0.4 * hash(floor(brick)));
        // The forge's arched mouth: glowing coals low down, flames licking up inside.
        float2 mouth = q.xy - float2(-1.3, 0.9);
        float2 arch = float2(mouth.x, max(mouth.y, 0.0));
        float mouthEdge = mouth.y < 0.0 ? max(abs(mouth.x) - 0.55, -mouth.y - 0.35) : length(arch) - 0.55;
        float flicker = 0.85 + 0.15 * noise(float2(Time * 6.0, 0.0)) + Bass * 0.3;
        float light = exp(-length(mouth) * 0.9) * flicker;
        colour = wall * (0.25 + light * 3.0);
        float coals = smoothstep(0.0, -0.3, mouth.y);
        float flames = noise(float2(mouth.x * 6.0, mouth.y * 3.0 - Time * 2.5)) * smoothstep(0.55, -0.2, mouth.y);
        float3 fire = lerp(float3(0.5, 0.06, 0.01), float3(3.0, 1.1, 0.2), saturate(coals * 0.8 + flames * 1.2));
        fire = lerp(fire, float3(4.0, 2.8, 1.2), smoothstep(0.7, 1.0, flames + coals * 0.4) * 0.8);
        float inside = 1.0 - smoothstep(-0.02, 0.02, mouthEdge);
        // Thick brick rim round the opening, lit from inside.
        float rim = (1.0 - smoothstep(0.0, 0.1, abs(mouthEdge - 0.05))) * (1.0 - inside);
        colour = lerp(colour, fire * flicker, inside) + float3(0.6, 0.2, 0.05) * rim * flicker * 0.4;
        colour *= smoothstep(4.5, 1.2, abs(q.x - 0.0) * 0.7 + max(q.y - 2.0, 0.0));
    }
    return colour;
}

// Light reaching a point: the bar's glow, the forge mouth, a cold blue window, and the flash of each strike.
float3 lighting(float3 position, float3 normal, float3 view, float shininess, out float3 specular)
{
    float3 colour = 0.0;
    specular = 0.0;
    float3 lights[4] = { (BarStart + BarEnd) * 0.5 + float3(0.0, 0.05, 0.0), float3(-1.3, 0.9, 3.3), float3(2.0, 2.6, -1.4), StrikePoint + float3(0.0, 0.12, -0.05) };
    float3 tints[4] = { heatColour(barHeat()) * 0.7, float3(1.0, 0.45, 0.12) * 3.5 * (0.9 + Bass * 0.3), float3(0.35, 0.5, 0.9) * 2.0, float3(1.0, 0.7, 0.4) * Beat * Beat * 2.5 };
    [unroll]
    for (int i = 0; i < 4; i++)
    {
        float3 toLight = lights[i] - position;
        float d2 = dot(toLight, toLight);
        float3 l = toLight * rsqrt(d2);
        float falloff = 1.0 / (0.15 + d2);
        colour += tints[i] * saturate(dot(normal, l)) * falloff;
        float3 halfway = normalize(l - view);
        specular += tints[i] * pow(saturate(dot(normal, halfway)), shininess) * falloff * (shininess + 8.0) / 8.0;
    }
    return colour;
}

float3 Render(float2 uv, float2 p)
{
    float3 eye = cameraEye();
    float3 target = cameraTarget();
    float3 origin = eye;
    float3 direction = cameraRay(eye, target, float2(p.x, -p.y), Zoom, 0.0);

    float3 colour = 0.0;
    float3 throughput = 1.0;
    [loop]
    for (int bounce = 0; bounce < 3; bounce++)
    {
        // The floor, the bar, then march the anvil and hammer inside their bounding box.
        float best = 1e4;
        int material = 0;
        float3 normal = float3(0.0, 1.0, 0.0);
        if (direction.y < 0.0)
        {
            float t = -origin.y / direction.y;
            // The floor runs back to the forge's wall.
            float wall = direction.z > 0.0 ? (3.5 - origin.z) / direction.z : 1e4;
            if (t > 1e-3 && t < wall)
            {
                best = t;
                material = 1;
            }
        }
        float3 barNormal;
        float along;
        float tBar = barHit(origin, direction, barNormal, along);
        if (tBar > 0.0 && tBar < best)
        {
            best = tBar;
            material = 2;
            normal = barNormal;
        }
        float3 boxMin = float3(-0.55, 0.0, -0.32);
        float3 boxMax = float3(1.1, 3.0, 1.6);
        float3 inverse = 1.0 / direction;
        float3 t1 = (boxMin - origin) * inverse;
        float3 t2 = (boxMax - origin) * inverse;
        float enter = max(max(min(t1.x, t2.x), min(t1.y, t2.y)), min(t1.z, t2.z));
        float leave = min(min(max(t1.x, t2.x), max(t1.y, t2.y)), max(t1.z, t2.z));
        int part = 0;
        if (enter < leave && leave > 0.0)
        {
            float t = max(enter, 1e-3);
            int steps = qualitySteps(80);
            [loop]
            for (int i = 0; i < steps; i++)
            {
                float d = anvilDistance(origin + direction * t, part);
                if (d < 6e-4 * surfacePrecision())
                {
                    break;
                }
                t += d;
                if (t > min(leave, best))
                {
                    break;
                }
            }
            if (t < min(leave, best))
            {
                best = t;
                material = 3;
                normal = anvilNormal(origin + direction * t);
            }
        }

        float3 position = origin + direction * best;
        if (material == 0)
        {
            colour += throughput * forgeBackdrop(origin, direction);
            break;
        }
        if (material == 2)
        {
            // The glowing bar, a little brighter where we see its top, with scale darkening the ends.
            colour += throughput * barGlow(along) * (0.75 + 0.25 * normal.y);
            break;
        }
        float facing = saturate(dot(normal, -direction));
        float3 specular;
        if (material == 3)
        {
            // Polished steel anvil and hammer: mostly mirror, with a dark sheen of their own.
            float3 diffuse = lighting(position, normal, direction, 120.0, specular);
            float fresnel = 0.6 + 0.4 * pow(1.0 - facing, 5.0);
            colour += throughput * (diffuse * 0.18 + specular * 0.8);
            throughput *= float3(0.62, 0.63, 0.66) * fresnel;
        }
        else
        {
            // Smooth dark iron plates on the floor, worn brighter in patches, with faint seams.
            float2 plate = abs(frac(position.xz * 0.8) - 0.5);
            float seam = smoothstep(0.485, 0.495, max(plate.x, plate.y));
            float wear = noise(position.xz * 3.0) * 0.5 + noise(position.xz * 11.0) * 0.5;
            float3 diffuse = lighting(position, normal, direction, 40.0, specular);
            float3 albedo = float3(0.03, 0.028, 0.026) * (0.6 + wear * 0.8);
            colour += throughput * (albedo * diffuse + specular * 0.05) * (1.0 - seam * 0.6);
            float fresnel = 0.05 + 0.95 * pow(1.0 - facing, 5.0);
            throughput *= (0.25 + 0.5 * fresnel) * (1.0 - seam) * (0.6 + wear * 0.4);
            // Far off, the floor fades into the dark.
            throughput *= smoothstep(9.0, 3.0, length(position.xz));
        }
        direction = reflect(direction, normal);
        origin = position + normal * 2e-3;
        if (max(throughput.x, max(throughput.y, throughput.z)) < 0.02)
        {
            break;
        }
    }

    // Sparks on top, with short glowing trails where they've just been.
    float3 sparks = particles(uv);
    float3 trail = max(previous(uv) - colour, 0.0) * 0.4;
    return colour + sparks + trail;
}
