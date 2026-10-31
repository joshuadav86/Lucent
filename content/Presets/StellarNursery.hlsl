// Name: Stellar Nursery
// Drift around a cloud of gas where new stars are born, mapped in glowing neon contour lines: nested shells round the young stars at its heart, each traced where it cuts the cloud's ridges and filaments, with light flowing along every line. Each shell flares with its own band of the spectrum, the shells breathe with the bass, and every beat sends a shockwave of light rippling out through them.
// Family: Particles
// Tags: Space, Neon, Geometric, 3D
// Added: 2026-10-17 01:51
// Particles: 262144

// How the particles are shared out: gas, then the young stars at the heart, then distant stars.
static const float GasShare = 0.93;
static const float ClusterShare = 0.012;

float shareOf(uint id)
{
    return (id + 0.5) / ParticleCount;
}

// The gas lies in a ragged, hollow shell blown out by the young stars, in twisting filaments.
float gasRidges(float3 p)
{
    float3 q = p * 1.8 + float3(4.1, 1.7, 8.3);
    float turbulence = noise3(q) * 0.55 + noise3(q * 2.1 + 3.0) * 0.3 + noise3(q * 4.3 + 7.0) * 0.15;
    float ridges = 1.0 - abs(turbulence);
    return saturate(ridges * ridges * ridges * 2.8 - 1.3);
}

// The contour map: Shells nested spheres round the cluster, and on each one the lines where the gas
// reaches each of Levels densities, like the contour lines of hills drawn on a globe.
static const float Shells = 7.0;
static const float ShellInner = 0.55;
static const float ShellStep = 0.16;
static const float Levels[3] = { 0.08, 0.3, 0.6 };

float shellRadius(float shell)
{
    return ShellInner + shell * ShellStep;
}

// The contour lines follow the gas's ridges and filaments themselves; each shell cuts through them.
float gasDensity(float3 p)
{
    return gasRidges(p);
}

// The gas's slope at p, from the density a little way either side.
float3 gasSlope(float3 p)
{
    float e = 0.02;
    float d = gasDensity(p);
    return float3(gasDensity(p + float3(e, 0.0, 0.0)) - d, gasDensity(p + float3(0.0, e, 0.0)) - d, gasDensity(p + float3(0.0, 0.0, e)) - d) / e;
}

// How long ago the last beat was (Beat fades from 1 as exp(-5 t)), for the shockwave.
float sinceBeat()
{
    return -log(max(Beat, 1e-3)) / 5.0;
}

float3 cameraEye()
{
    float angle = Time * 0.035;
    float distance = 3.0 + sin(Time * 0.021) * 0.5;
    return float3(sin(angle) * distance, -0.5 + sin(Time * 0.027) * 0.9, cos(angle) * distance);
}

void Spawn(inout Particle particle, uint id)
{
    float share = shareOf(id);
    particle.velocity = 0.0;
    particle.data = 0.0;
    particle.life = 1e6;
    if (share < GasShare)
    {
        // Pick a shell and a contour level, then try spots on that shell and keep the one closest to
        // the contour; Simulate pulls it the rest of the way on.
        float shell = min(floor(fixedRandom(id, 1.0) * Shells), Shells - 1.0);
        float level = Levels[min((uint)(fixedRandom(id, 2.0) * 3.0), 2u)];
        float radius = shellRadius(shell);
        float3 position = randomDirection(id, 10.0) * radius;
        float best = abs(gasDensity(position) - level);
        [unroll]
        for (int attempt = 0; attempt < 10; attempt++)
        {
            float3 candidate = randomDirection(id, 13.0 + attempt * 3.0) * radius;
            float off = abs(gasDensity(candidate) - level);
            if (off < best)
            {
                best = off;
                position = candidate;
            }
        }
        particle.position = position;
        particle.data = float4(level, shell, fixedRandom(id, 3.0) < 0.5 ? -1.0 : 1.0, 0.6 + fixedRandom(id, 4.0) * 0.8);
        particle.life = lerp(18.0, 40.0, random(id, 2.0));
        if (ParticleReset > 0.5)
        {
            particle.life *= random(id, 3.0);
        }
    }
    else if (share < GasShare + ClusterShare)
    {
        // The young cluster: tightly packed at the centre.
        particle.position = randomInSphere(id, 4.0) * 0.32 * pow(fixedRandom(id, 5.0), 1.5);
    }
    else
    {
        // Far-off stars all around.
        particle.position = randomDirection(id, 6.0) * 12.0;
    }
}

void Simulate(inout Particle particle, uint id)
{
    if (shareOf(id) < GasShare)
    {
        // Light flows along the contour line: across the slope, round the shell, then settles back
        // onto the line and the shell.
        float dt = min(DeltaTime, 0.05);
        float3 position = particle.position;
        float radius = shellRadius(particle.data.y);
        float3 up = normalize(position + 1e-4);
        float3 slope = gasSlope(position);
        float3 across = slope - up * dot(slope, up);
        float steep = dot(across, across);
        float3 along = steep > 1e-6 ? normalize(cross(up, across)) : 0.0;
        float speed = (0.04 + Bass * 0.14 + Beat * 0.05 + DropPulse * 0.1) * particle.data.z * particle.data.w;
        position += along * speed * dt;
        if (steep > 1e-6)
        {
            position -= across * (gasDensity(position) - particle.data.x) / steep * 0.5;
        }
        position = normalize(position + 1e-4) * radius;
        particle.velocity = (position - particle.position) / max(dt, 1e-4);
        particle.position = position;
    }
}

// Neon by shell, teal next to the hot stars through blue, violet and magenta to red-gold at the
// edge; the denser contours on each shell run a little warmer.
float3 shellColour(float shell, float level)
{
    float hue = 0.47 + shell / (Shells - 1.0) * 0.6 + level * 0.12 + sin(Time * 0.02) * 0.03;
    return hsv2rgb(frac(hue), 0.85, 1.0);
}

Sprite Show(Particle particle, uint id)
{
    float share = shareOf(id);
    float3 eye = cameraEye();
    float zoom = 1.9 + Bass * 0.04;
    float3 seen = project(particle.position, eye, 0.0, zoom, sin(Time * 0.03) * 0.2);
    float focusDepth = length(eye);
    Sprite sprite = (Sprite)0;
    sprite.position = seen.xy;
    if (seen.z < 0.1)
    {
        return sprite;
    }

    if (share < GasShare)
    {
        float shell = particle.data.y;
        float r = length(particle.position);
        // The shells breathe with the bass, the outer ones most.
        float breathe = 1.0 + Bass * 0.05 * (1.0 + shell * 0.3) + DropPulse * 0.08;
        seen = project(particle.position * breathe, eye, 0.0, zoom, sin(Time * 0.03) * 0.2);
        float3 seenBefore = project((particle.position - particle.velocity * 0.03) * breathe, eye, 0.0, zoom, sin(Time * 0.03) * 0.2);
        // Each shell plays its own band of the spectrum, bass inside and treble at the edge.
        float level = spectrum(0.03 + shell / (Shells - 1.0) * 0.6);
        // Every beat sends a shockwave out through the shells, lighting each as it passes.
        float wave = 0.3 + sinceBeat() * 1.6;
        float shock = exp(-pow((r - wave) / 0.1, 2.0)) * Beat * 3.0;
        // Lines that drift off their contour in the thinnest gas fade rather than wander.
        float onLine = exp(-pow((gasDensity(particle.position) - particle.data.x) / 0.06, 2.0));
        float fade = saturate(particle.age * 0.4) * saturate(particle.life * 0.4);
        float brightness = 0.22 * (0.35 + level * 1.5 + shock) * onLine * fade * (0.8 + Intensity * 0.4);
        sprite.position = seen.xy;
        float2 streak = seen.xy - seenBefore.xy;
        sprite.streak = streak * min(1.0, 0.012 / max(length(streak), 1e-5));
        sprite.size = 0.9;
        sprite.colour = shellColour(shell, particle.data.x) * brightness * (1.0 + step(0.997, fixedRandom(id, 7.0)) * Treble * 4.0);
        focus(sprite, seen.z, focusDepth, 0.05);
        return sprite;
    }

    if (share < GasShare + ClusterShare)
    {
        // Young stars: blue-white, a few of them blazing, all twinkling with the treble.
        float personality = fixedRandom(id, 8.0);
        float bright = (pow(personality, 12.0) * 8.0 + 0.12) * 0.3;
        float twinkle = 1.0 + (random(id, 9.0) - 0.5) * Treble * 1.2;
        sprite.size = 1.3;
        sprite.colour = lerp(float3(0.7, 0.85, 1.0), float3(1.0, 0.95, 0.85), fixedRandom(id, 10.0)) * bright * twinkle * (1.0 + Beat * 0.5);
        // The brightest few get long six-pointed spikes, like a big telescope's photos; each of
        // these is drawn three times, as streaks at different angles.
        if (personality > 0.995)
        {
            float angle = (id % 3u) * (PI / 3.0) + PI / 6.0;
            sprite.streak = float2(cos(angle), sin(angle)) * 0.09 * (1.0 + Bass * 0.4);
            sprite.colour *= 1.2;
        }
        return sprite;
    }

    sprite.size = 0.9;
    sprite.colour = hsv2rgb(0.55 + fixedRandom(id, 11.0) * 0.5, 0.25, 1.0) * pow(fixedRandom(id, 12.0), 6.0) * 0.9;
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    // A faint glow round the cluster keeps the heart of the nursery warm between the lines.
    float glow = exp(-dot(p, p) * 9.0) * (0.02 + Bass * 0.02);
    return particles(uv) + previous(uv) * 0.5 + float3(0.4, 0.5, 1.0) * glow;
}
