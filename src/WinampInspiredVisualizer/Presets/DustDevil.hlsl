// Name: Dust Devil
// A towering whirlwind of glowing sand twists its way across the desert at dusk, lit gold by the setting sun; the bass winds it tighter and faster, and every beat flings showers of grit out of the funnel.
// Family: Particles
// Tags: Intense, 3D
// Added: 2026-10-17 17:12
// Particles: 262144

// World y is up; the desert floor is y = 0.
static const float FunnelHeight = 9.0;
static const float3 SunDirection = float3(-0.78, 0.09, 0.62);

// How the particles are shared out: the funnel itself, then the skirt of dust rolling round its foot.
static const float FunnelShare = 0.78;

// The whirlwind wanders slowly over the sand.
float3 baseAt(float t)
{
    return float3(sin(t * 0.043) * 4.5 + sin(t * 0.11) * 0.8, 0.0, cos(t * 0.031) * 3.0);
}

// The centre of the funnel at a height: it leans and snakes, the top lagging behind the foot.
float3 axisAt(float height)
{
    float3 foot = baseAt(Time - height * 0.35);
    float bend = height / FunnelHeight;
    return foot + float3(sin(height * 0.45 + Time * 0.6), 0.0, cos(height * 0.38 + Time * 0.47)) * 0.45 * bend * bend * 3.0
         + float3(0.0, height, 0.0);
}

// How wide the funnel is at a height: a narrow waist near the ground flaring into a broad top.
// The bass draws it in tight.
float radiusAt(float height)
{
    float tighten = 1.0 - Bass * 0.3;
    return (0.35 + 0.05 * height + 0.022 * height * height) * tighten;
}

float shareOf(uint id)
{
    return (id + 0.5) / ParticleCount;
}

void Spawn(inout Particle particle, uint id)
{
    float share = shareOf(id);
    float angle = random(id, 1.0) * 2.0 * PI;
    // On the first frame spread them all the way up; afterwards they're born at the foot.
    float height = ParticleReset > 0.5 ? pow(random(id, 2.0), 0.8) * FunnelHeight : random(id, 2.0) * 0.6;
    // How far out from the wall: most ride the wall of the funnel, some fill its core.
    float spread = lerp(0.3, 1.1, pow(random(id, 3.0), 0.35));
    if (share >= FunnelShare)
    {
        height = random(id, 2.0) * 0.4;
        spread = lerp(1.2, 4.0, random(id, 3.0));
    }
    particle.data = float4(angle, height, spread, 0.0);
    particle.position = axisAt(height) + float3(cos(angle), 0.0, sin(angle)) * radiusAt(height) * spread;
    particle.velocity = 0.0;
    particle.life = lerp(25.0, 40.0, random(id, 4.0));
}

void Simulate(inout Particle particle, uint id)
{
    float dt = min(DeltaTime, 0.05);
    float share = shareOf(id);

    if (particle.data.w > 0.5)
    {
        // Flung out of the funnel: flying free, slowed by the air and pulled back down.
        particle.velocity *= exp(-dt * 0.6);
        particle.velocity.y -= 4.0 * dt;
        particle.position += particle.velocity * dt;
        if (particle.position.y < 0.0)
        {
            particle.life = 0.0;
        }
        return;
    }

    float height = particle.data.y;
    float spread = particle.data.z;
    float radius = radiusAt(height) * spread;
    // Spinning fastest in the narrow throat, quicker with the bass; climbing as it turns.
    float spin = (2.2 + Bass * 4.0 + Beat * 1.5) / max(radius, 0.25) * (share >= FunnelShare ? 0.35 : 1.0);
    float climb = share >= FunnelShare ? 0.05 : (0.55 + Bass * 0.9) * lerp(1.0, 0.5, spread - 0.35);
    particle.data.x += spin * dt;
    particle.data.y += climb * dt;
    // The wall breathes in and out in gusts.
    float gust = noise3(float3(particle.data.x * 0.5, height * 0.4 - Time * 0.8, spread * 3.0)) * 0.18;
    float3 moved = axisAt(particle.data.y) + float3(cos(particle.data.x), 0.0, sin(particle.data.x)) * radius * (1.0 + gust);
    moved += curlNoise(moved * 0.6 + Time * 0.15) * 0.06;
    particle.velocity = (moved - particle.position) / max(dt, 1e-4);
    particle.position = moved;

    // At the top the sand spills out and falls away; on strong beats grit is hurled clear of the funnel.
    bool spill = particle.data.y > FunnelHeight * (0.85 + 0.15 * fixedRandom(id, 5.0));
    bool flung = Beat > 0.85 && random(id, 6.0) < 0.006 + Bass * 0.015;
    if (spill || flung)
    {
        float3 outward = normalize(float3(cos(particle.data.x), 0.0, sin(particle.data.x)));
        float3 along = float3(-outward.z, 0.0, outward.x);
        particle.data.w = 1.0;
        particle.velocity = along * min(spin * radius, 9.0) * 0.6 + outward * lerp(1.5, 6.0, random(id, 7.0)) * (spill ? 0.2 : 1.0)
                          + float3(0.0, lerp(0.5, 3.0, random(id, 8.0)), 0.0);
        particle.life = min(particle.life, 6.0);
    }
}

float3 cameraEye()
{
    float angle = Time * 0.045 + 0.6;
    float3 centre = baseAt(Time);
    return float3(centre.x * 0.6 + sin(angle) * 11.0, 1.3 + 0.5 * sin(Time * 0.07), centre.z * 0.6 + cos(angle) * 11.0);
}

float3 cameraTarget()
{
    return baseAt(Time) * 0.8 + float3(0.0, 4.4, 0.0);
}

static const float Zoom = 1.25;

float3 toScreen(float3 position)
{
    float3 seen = project(position, cameraEye(), cameraTarget(), Zoom, 0.0);
    return float3(seen.x, -seen.y, seen.z);
}

Sprite Show(Particle particle, uint id)
{
    Sprite sprite = (Sprite)0;
    float3 seen = toScreen(particle.position);
    if (seen.z < 0.5)
    {
        return sprite;
    }
    float share = shareOf(id);

    // Sand lit by the low sun: bright gold on the side facing it, dusky rose in the funnel's shadow.
    float3 fromAxis = particle.position - axisAt(clamp(particle.position.y, 0.0, FunnelHeight));
    float3 outward = normalize(float3(fromAxis.x, 0.0, fromAxis.z) + 1e-4);
    float sunlit = saturate(dot(outward, normalize(float3(SunDirection.x, 0.0, SunDirection.z))) * 0.6 + 0.45);
    float3 colour = lerp(float3(0.35, 0.12, 0.16), float3(1.0, 0.55, 0.2), sunlit);
    // The grains catch the light at their own band of the music, higher up the funnel for higher notes.
    float band = spectrum(saturate(particle.position.y / FunnelHeight) * 0.6 + 0.04);
    float brightness = 0.09 * (0.6 + band * 1.2) * (0.8 + Level * 0.4);
    // Twisted ropes of denser sand wind up the wall.
    float rope = 0.5 + 0.5 * sin(particle.data.x * 3.0 - particle.data.y * 1.3 + Time * 0.5);
    brightness *= 0.45 + 0.9 * rope * rope;
    // Fine dust fades out as it spills from the top or settles.
    float fade = saturate(particle.age * 1.5) * saturate(particle.life * 0.6);
    // Where the funnel is narrow the sand is packed tight, so each grain is drawn fainter.
    brightness *= clamp(length(fromAxis.xz) * 0.7, 0.3, 1.4);
    if (particle.data.w > 0.5)
    {
        // Flung grit glows hotter, like sparks.
        colour = lerp(colour, float3(1.0, 0.7, 0.35), 0.5);
        brightness *= 1.6;
    }
    if (share >= FunnelShare)
    {
        brightness *= 0.6;
    }
    // A few glinting flecks of mica sparkle with the treble.
    brightness *= 1.0 + step(0.995, fixedRandom(id, 9.0)) * (4.0 + Treble * 12.0);

    sprite.position = seen.xy;
    sprite.size = lerp(0.9, 1.6, fixedRandom(id, 10.0));
    sprite.colour = colour * brightness * fade;
    float3 before = toScreen(particle.position - particle.velocity * 0.025);
    sprite.streak = seen.xy - before.xy;
    focus(sprite, seen.z, length(cameraTarget() - cameraEye()), 0.05);
    return sprite;
}

// ---- The desert at dusk ----

float3 sky(float3 direction)
{
    float up = saturate(direction.y);
    float3 sun = normalize(SunDirection);
    float facing = saturate(dot(direction, sun));
    float3 colour = lerp(float3(0.9, 0.38, 0.14), float3(0.25, 0.12, 0.25), pow(up, 0.45));
    colour = lerp(colour, float3(0.03, 0.035, 0.1), smoothstep(0.15, 0.8, up));
    colour *= 0.32;
    // The sun sitting just above the horizon, and its glow spreading along it.
    colour += float3(1.0, 0.6, 0.3) * (pow(facing, 12.0) * 0.6 + pow(facing, 80.0) * 1.2) * (0.9 + Bass * 0.3);
    colour += float3(1.0, 0.85, 0.6) * smoothstep(0.9993, 0.9996, facing) * 6.0;
    // Long streaks of cloud lit pink from below.
    float2 cloudPlane = direction.xz / max(direction.y + 0.03, 0.03) * float2(0.25, 1.2) + float2(Time * 0.006, 0.0);
    float cloud = smoothstep(0.5, 0.8, fbm(cloudPlane)) * smoothstep(0.02, 0.12, direction.y) * smoothstep(0.6, 0.2, direction.y);
    colour = lerp(colour, float3(0.9, 0.4, 0.35) * (0.25 + pow(facing, 3.0) * 0.8), cloud * 0.7);
    return colour;
}

// Flat-topped mesas along the horizon.
float mesas(float3 direction)
{
    float around = atan2(direction.x, direction.z);
    float rock = fbm(float2(around * 3.0, 7.0));
    float height = 0.008 + smoothstep(0.48, 0.56, rock) * 0.045 + noise(float2(around * 60.0, 1.0)) * 0.002;
    return smoothstep(height + 0.0015, height - 0.0015, direction.y);
}

float duneHeight(float2 q)
{
    return fbm(q * 0.08) * 1.2 + sin(q.x * 2.2 + q.y * 1.4 + noise(q * 0.5) * 3.0) * 0.03;
}

float3 desert(float3 eye, float3 direction)
{
    float t = -eye.y / min(direction.y, -1e-4);
    float3 position = eye + direction * t;
    // Ripples in the sand, shaded by the grazing sunlight.
    float2 e = float2(0.05, 0.0);
    float h = duneHeight(position.xz);
    float3 normal = normalize(float3(h - duneHeight(position.xz + e.xy), e.x, h - duneHeight(position.xz + e.yx)));
    float3 sun = normalize(SunDirection);
    float lit = saturate(dot(normal, sun) * 3.0 + 0.2);
    float3 colour = float3(0.32, 0.15, 0.08) * (0.25 + lit * 0.9) * 0.25;
    // The glow of the whirlwind on the sand round its foot, and its long shadow away from the sun.
    float3 foot = baseAt(Time);
    float2 offset = position.xz - foot.xz;
    colour += float3(0.5, 0.25, 0.1) * exp(-dot(offset, offset) * 0.08) * (0.15 + Bass * 0.25);
    float2 away = -normalize(SunDirection.xz);
    float along = dot(offset, away);
    float across = abs(offset.x * away.y - offset.y * away.x);
    colour *= 1.0 - 0.55 * smoothstep(0.0, 1.0, along) * exp(-along * 0.04) * smoothstep(0.9 + along * 0.08, 0.2, across);
    // Haze thickening towards the horizon.
    float haze = 1.0 - exp(-t * 0.025);
    return lerp(colour, sky(float3(direction.x, 0.01, direction.z)) * 0.8, haze);
}

float3 Render(float2 uv, float2 p)
{
    float3 eye = cameraEye();
    float3 direction = cameraRay(eye, cameraTarget(), float2(p.x, -p.y), Zoom, 0.0);
    float3 colour = direction.y > 0.0 ? sky(direction) : desert(eye, direction);
    float rock = mesas(direction) * step(0.0, direction.y);
    colour = lerp(colour, float3(0.09, 0.04, 0.06) + sky(float3(direction.x, 0.0, direction.z)) * 0.15, rock);

    // A faint haze of airborne dust round the whole funnel.
    float3 toFunnel = baseAt(Time) + float3(0.0, 3.5, 0.0) - eye;
    float along = dot(toFunnel, direction);
    float off = length(toFunnel - direction * along);
    colour += float3(0.5, 0.25, 0.12) * exp(-off * off * 0.12) * 0.08 * (0.6 + Level);

    return colour + particles(uv);
}
