// Name: Phoenix Rising
// A firebird made of streaming flame rises out of the dark, beating its great wings down on every beat and shedding glowing embers, its long tail streamers rippling with the mids and its fire flaring with the bass.
// Family: Particles
// Tags: Fire, Intense, Colourful
// Added: 2026-10-17 17:15
// Particles: 262144

// The bird is drawn by flames rather than built from them: every flame particle is lit at a
// random spot on the bird's wings, body or tail as it is at that moment, and lives for less than a
// second, streaming back and up off the bird before it burns out. Because new flames are always
// being lit on the moving shape, the fire takes the shape of the bird and follows its wing beats.
// World y is up; the bird faces the camera with its wings spread to the sides.

// How the particles are shared out.
static const float WingShare = 0.42;
static const float BodyShare = 0.12;
static const float TailShare = 0.2;     // the rest are loose embers
static const float WingSpan = 1.3;
static const int Streamers = 5;

float sinceBeat()
{
    return -log(max(Beat, 1e-4)) / 5.0;
}

// The wing angle, a second or so after a beat: a quick, strong downstroke, then a slow recovery.
float flapAngle(float age)
{
    float k = age / 0.17;
    float stroke = k * k * exp(2.0 * (1.0 - k));
    return 0.58 - 1.15 * stroke * (0.75 + Bass * 0.35) + 0.1 * sin(Time * 1.7);
}

// The whole bird lifts a little on each downstroke.
float3 birdCentre(float age)
{
    float k = age / 0.17;
    float lift = k * k * exp(2.0 * (1.0 - k));
    return float3(sin(Time * 0.23) * 0.12, 0.05 + lift * 0.06 + sin(Time * 0.31) * 0.05, 0.0);
}

// A point on a wing. s runs from the shoulder (0) to the tip (1); c from the leading edge (0)
// back to the ends of the feathers (1). side is -1 or 1.
float3 wingPoint(float s, float c, float side, float angle)
{
    // The leading edge arches up and the tip curls upward.
    float2 leading = float2(s * WingSpan, 0.34 * sin(PI * s * 0.75) + 0.2 * s * s * s);
    // The trailing edge breaks into long flame feathers, longest towards the tip.
    float feather = pow(abs(cos(s * PI * 6.5)), 0.6);
    float chord = (0.5 * (1.0 - s) + 0.12) * (1.0 + 0.9 * s * feather);
    float2 back = normalize(float2(0.15 + s * 0.55, -1.0));
    float2 local = leading + back * chord * c;

    // The wing swings at the shoulder and bends more at the wrist.
    float bend = angle * (0.75 + 0.55 * smoothstep(0.35, 1.0, s));
    local = rotate(local, bend);
    // Wingtips sweep towards the camera on the downstroke, cupping the air.
    float depth = -0.45 * s * s * saturate(0.6 - angle) - 0.08 * c;
    return float3(0.1 * side + local.x * side, 0.18 + local.y, depth);
}

// A point on a tail streamer, a from the root (0) to the tip (1).
float3 tailPoint(float k, float a)
{
    float spread = (k / (Streamers - 1.0) - 0.5) * 1.7;
    float sway = sin(a * 5.0 - Time * 2.6 + k * 1.3) * (0.08 + Mid * 0.2) * a;
    float3 position = float3(sin(spread) * a * (0.6 + a * 0.5) + sway, -0.38 - a * (1.3 + 0.25 * cos(k * 2.1)), 0.15 * a);
    position.z += cos(a * 4.0 - Time * 2.0 + k) * 0.12 * a;
    return position;
}

// A point on the body, neck or head, with a little crest of flame on top.
float3 bodyPoint(float u, float v, float w)
{
    float around = v * 2.0 * PI;
    if (u < 0.65)
    {
        // The chest: a long egg shape.
        float t = u / 0.65;
        float height = lerp(-0.42, 0.3, t);
        float width = 0.14 * sin(PI * saturate(t * 0.95 + 0.05));
        return float3(cos(around) * width, height, sin(around) * width * 0.8 - 0.04);
    }
    if (u < 0.88)
    {
        // The neck and head, reaching up and a little forward.
        float t = (u - 0.65) / 0.23;
        float3 neck = float3(0.0, 0.3 + t * 0.32, -0.04 - t * 0.08);
        float width = lerp(0.07, 0.085, sin(t * PI));
        return neck + float3(cos(around), 0.0, sin(around)) * width;
    }
    // The crest: a few flames streaming back from the crown.
    float t = (u - 0.88) / 0.12;
    float plume = floor(w * 3.0) - 1.0;
    return float3(plume * 0.05 * t, 0.66 + t * 0.22, -0.1 + t * 0.12);
}

float partOf(uint id)
{
    return fixedRandom(id, 1.0);
}

void Spawn(inout Particle particle, uint id)
{
    float part = partOf(id);
    float age = sinceBeat();
    float angle = flapAngle(age);
    float anglePrevious = flapAngle(age - 0.02);
    float3 centre = birdCentre(age);
    float3 position = 0.0;
    float3 velocity = 0.0;
    float heat = 1.0;

    if (part < WingShare)
    {
        float side = fixedRandom(id, 2.0) < 0.5 ? -1.0 : 1.0;
        // Half the flames run along the leading edge, the rest along the shafts of the long
        // feathers, so the wing reads as feathers of fire rather than a solid sheet.
        float s = sqrt(random(id, 3.0));
        float c = random(id, 4.0) * 0.12;
        if (random(id, 16.0) < 0.55)
        {
            s = saturate((floor(random(id, 3.0) * 11.0) + 0.5) / 11.0 + (random(id, 17.0) - 0.5) * 0.012);
            s = sqrt(s);
            c = random(id, 4.0);
        }
        position = wingPoint(s, c, side, angle);
        float3 before = wingPoint(s, c, side, anglePrevious);
        velocity = (position - before) / 0.02 * 0.35;
        // Flames stream off the trailing edge, back along the feathers.
        velocity += float3(side * 0.15 * s, -0.25 - c * 0.3, 0.1);
        heat = lerp(1.0, 0.55, c);
        particle.life = lerp(0.3, 0.75, random(id, 5.0));
    }
    else if (part < WingShare + BodyShare)
    {
        position = bodyPoint(random(id, 3.0), random(id, 4.0), random(id, 6.0));
        velocity = float3(0.0, -0.3, 0.05);
        heat = 0.92;
        particle.life = lerp(0.25, 0.6, random(id, 5.0));
    }
    else if (part < WingShare + BodyShare + TailShare)
    {
        float k = floor(fixedRandom(id, 2.0) * Streamers);
        float a = random(id, 3.0);
        position = tailPoint(k, a);
        float3 ahead = tailPoint(k, saturate(a + 0.05));
        velocity = normalize(ahead - position) * (0.4 + Mid * 0.4);
        heat = lerp(0.8, 0.55, a) + 0.25 * smoothstep(0.85, 1.0, a);
        particle.life = lerp(0.35, 0.8, random(id, 5.0));
    }
    else
    {
        // Embers break off anywhere along the wings and tail and fall away behind the rising bird.
        float side = random(id, 2.0) < 0.5 ? -1.0 : 1.0;
        position = random(id, 7.0) < 0.6 ? wingPoint(random(id, 3.0), random(id, 4.0), side, angle) : tailPoint(floor(random(id, 3.0) * Streamers), random(id, 4.0));
        velocity = float3((random(id, 8.0) - 0.5) * 0.8, -0.4 - random(id, 9.0) * 0.6, (random(id, 10.0) - 0.5) * 0.6);
        heat = 0.8;
        particle.life = lerp(1.2, 3.2, random(id, 5.0));
    }

    position += (float3(random(id, 11.0), random(id, 12.0), random(id, 13.0)) - 0.5) * 0.025;
    particle.position = centre + position;
    particle.velocity = velocity;
    // data: x = starting heat, y = starting life, z = a flicker seed.
    particle.data = float4(heat, max(particle.life, 0.05), random(id, 14.0), 0.0);
    if (ParticleReset > 0.5)
    {
        // Stagger the first flames so they don't all burn out together.
        particle.life *= random(id, 15.0);
    }
}

void Simulate(inout Particle particle, uint id)
{
    float dt = min(DeltaTime, 0.05);
    bool ember = partOf(id) >= WingShare + BodyShare + TailShare;
    // Flames curl and lick as they go, and the whole flow sinks away behind the rising bird.
    float3 swirl = curlNoise(particle.position * 2.2 + float3(0.0, Time * 0.6, 0.0));
    particle.velocity += swirl * dt * (ember ? 0.8 : 2.2) * (0.7 + Bass * 0.6);
    particle.velocity.y -= dt * (ember ? 0.25 : 0.9);
    particle.velocity *= exp(-dt * (ember ? 0.4 : 1.6));
    particle.position += particle.velocity * dt;
}

// Fire colours from white-hot through gold and orange to crimson and a dying violet.
float3 fireColour(float t)
{
    float3 colour = lerp(float3(0.35, 0.03, 0.18), float3(1.0, 0.1, 0.05), smoothstep(0.0, 0.3, t));
    colour = lerp(colour, float3(1.0, 0.45, 0.08), smoothstep(0.25, 0.55, t));
    colour = lerp(colour, float3(1.0, 0.8, 0.35), smoothstep(0.5, 0.8, t));
    colour = lerp(colour, float3(1.0, 0.97, 0.85), smoothstep(0.82, 1.0, t));
    return colour;
}

static const float Zoom = 1.5;

float3 cameraEye()
{
    return float3(sin(Time * 0.07) * 0.6, -0.55 + sin(Time * 0.05) * 0.2, -4.3);
}

Sprite Show(Particle particle, uint id)
{
    float3 eye = cameraEye();
    float3 target = float3(0.0, -0.2, 0.0);
    float roll = sin(Time * 0.11) * 0.05;
    float3 flip = float3(1.0, -1.0, 1.0);
    float3 seen = project(particle.position * flip, eye * flip, target * flip, Zoom, roll);
    float3 seenBefore = project((particle.position - particle.velocity * 0.035) * flip, eye * flip, target * flip, Zoom, roll);

    Sprite sprite = (Sprite)0;
    if (seen.z < 0.2)
    {
        return sprite;
    }

    bool ember = partOf(id) >= WingShare + BodyShare + TailShare;
    float burnt = saturate(particle.age / particle.data.y);
    float heat = particle.data.x * (1.0 - burnt) * (0.85 + Bass * 0.25);
    float3 colour = fireColour(saturate(heat));

    float brightness;
    if (ember)
    {
        // Embers flicker as they tumble and cool.
        float flicker = 0.5 + 0.5 * sin(particle.age * (14.0 + particle.data.z * 20.0) + particle.data.z * 30.0);
        brightness = 0.6 * (0.4 + flicker) * (1.0 - burnt) * (1.0 - burnt);
        colour = fireColour(0.55 * (1.0 - burnt) + 0.2);
        sprite.size = 1.2;
    }
    else
    {
        // Young flames are small and fierce; older ones swell and dim like smoke.
        brightness = 0.07 * (0.6 + heat * 1.2) * smoothstep(0.0, 0.08, particle.age) * (1.0 - burnt * burnt);
        sprite.size = lerp(1.1, 3.2, burnt);
    }
    // The beat and the bass make the whole bird flare.
    brightness *= 0.75 + Beat * 0.5 + Level * 0.4;

    sprite.position = seen.xy;
    sprite.streak = seen.xy - seenBefore.xy;
    sprite.colour = colour * brightness;
    focus(sprite, seen.z, 4.3, 0.02);
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    // Night sky shading to deep violet low down, with the bird's own warm glow behind it.
    float3 sky = lerp(float3(0.004, 0.002, 0.012), float3(0.03, 0.006, 0.03), saturate(p.y * 0.5 + 0.5));
    float halo = exp(-dot(p - float2(0.0, -0.1), p - float2(0.0, -0.1)) * 1.6);
    sky += float3(0.35, 0.09, 0.02) * halo * (0.12 + Bass * 0.12 + Beat * 0.06);

    // The trail of fire left below the rising bird drifts down and wavers in the heat.
    float2 drift = float2(sin(uv.y * 30.0 + Time * 3.0) * 0.0008, -0.0025);
    return sky + particles(uv) + previous(uv + drift) * 0.62;
}
