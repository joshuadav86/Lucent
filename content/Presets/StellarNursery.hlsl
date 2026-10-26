// Name: Stellar Nursery
// Drift around a glowing cloud of gas where new stars are born: filaments of red, gold and teal billow in the stellar wind, and every beat sends a shockwave rippling out from the young stars at its heart.
// Family: Particles
// Tags: Space, Colourful, Calm, 3D
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
float gasDensity(float3 p)
{
    float r = length(p);
    float shell = smoothstep(0.3, 0.8, r) * (1.0 - smoothstep(1.1, 2.0, r));
    float3 q = p * 1.8 + float3(4.1, 1.7, 8.3);
    float turbulence = noise3(q) * 0.55 + noise3(q * 2.1 + 3.0) * 0.3 + noise3(q * 4.3 + 7.0) * 0.15;
    float ridges = 1.0 - abs(turbulence);
    return shell * saturate(ridges * ridges * ridges * 2.8 - 1.3);
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
        // Try a few random spots and keep the densest, so the gas gathers into filaments; Show
        // also dims the gas by its density, so stragglers in empty space hardly show.
        float3 position = randomInSphere(id, 1.0) * 2.1;
        float best = gasDensity(position);
        [unroll]
        for (int attempt = 0; attempt < 6; attempt++)
        {
            float3 candidate = randomInSphere(id, 10.0 + attempt * 3.0) * 2.1;
            float density = gasDensity(candidate);
            if (density > best)
            {
                best = density;
                position = candidate;
            }
        }
        particle.position = position;
        particle.data.x = gasDensity(position);
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
        // The gas billows slowly on a turbulent flow, pushed outward by the stellar wind.
        float dt = min(DeltaTime, 0.05);
        float3 position = particle.position;
        float3 flow = curlNoise(position * 0.9 + float3(0.0, Time * 0.02, 0.0)) * (0.02 + Mid * 0.025);
        flow += normalize(position + 1e-4) * 0.006;
        particle.velocity = flow;
        particle.position += flow * dt;
    }
}

// The colours of the gas: teal close to the hot stars, red further out, gold in the densest knots.
float3 gasColour(float3 position, float density, uint id)
{
    float r = length(position);
    float3 teal = float3(0.0, 0.7, 1.0);
    float3 red = float3(1.0, 0.06, 0.2);
    float3 gold = float3(1.0, 0.55, 0.12);
    float drift = noise3(position * 0.8 + Time * 0.01);
    float3 colour = lerp(teal, red, smoothstep(0.75, 1.25, r + drift * 0.35));
    colour = lerp(colour, gold, smoothstep(0.5, 0.9, density) * 0.6);
    // A few wisps of violet.
    colour = lerp(colour, float3(0.6, 0.3, 1.0), step(0.93, fixedRandom(id, 7.0)) * 0.6);
    return colour;
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
        float density = particle.data.x;
        float r = length(particle.position);
        // Dark lanes of dust run through the glowing gas, hiding what lies inside them.
        float dust = 1.0 - 0.9 * smoothstep(0.05, 0.35, noise3(particle.position * 2.6 + float3(2.0, 9.0, 4.0)));
        // Every beat sends a shockwave out through the gas, lighting it as it passes.
        float wave = 0.3 + sinceBeat() * 1.6;
        float shock = exp(-pow((r - wave) / 0.12, 2.0)) * Beat * 2.5;
        float fade = saturate(particle.age * 0.4) * saturate(particle.life * 0.4);
        float brightness = density * 0.019 * dust * (1.0 + Bass * 0.5 + shock) * fade;
        sprite.size = 5.0;
        sprite.colour = gasColour(particle.position, density, id) * brightness;
        // Each frame every wisp lands somewhere slightly different, and Render keeps a few frames
        // blended, so the gas looks smooth and soft rather than grainy.
        float3 jitter = (float3(random(id, 13.0), random(id, 14.0), random(id, 15.0)) - 0.5) * 0.05;
        float3 jittered = project(particle.position + jitter, eye, 0.0, zoom, sin(Time * 0.03) * 0.2);
        sprite.position = jittered.xy;
        focus(sprite, seen.z, focusDepth, 0.06);
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
    return particles(uv) + previous(uv) * 0.72;
}
