// Name: Lava Lamp
// Glowing blobs of wax swell, stretch and drift up and down through warm liquid, melting into each other as the bass heats them, while bubbles fizz up with the treble.
// Family: Particles
// Tags: Calm, Colourful, Retro
// Particles: 131072

// The wax is a cloud of particles drawn into a hidden density layer (the red channel); Render
// smooths that density and turns it into solid-looking blobs, so neighbours melt together like wax.
static const int Blobs = 9;
// The last few particles are bubbles, drawn into the green channel instead.
static const float BubbleShare = 0.0015;

float shareOf(uint id)
{
    return (id + 0.5) / ParticleCount;
}

// Where a floating blob is now, and how wide and tall it is.
void blobShape(int index, out float2 centre, out float2 radius)
{
    float salt = index * 1.618 + 0.3;
    float period = 34.0 + frac(salt * 7.31) * 22.0;
    float phase = Time * 2.0 * PI / period + frac(salt * 3.17) * 2.0 * PI;

    // Blobs linger by the heater and at the cool top, and move fastest in between.
    float c = cos(phase);
    float moving = abs(sin(phase));
    centre.x = ((index + 0.5) / Blobs * 2.0 - 1.0) * Aspect * 0.78 + sin(Time * 0.06 + salt * 5.0) * 0.2;
    centre.y = sign(c) * pow(abs(c), 0.65) * 0.74 + 0.1;

    float size = 0.1 + frac(salt * 11.7) * 0.07;
    // Hot wax at the bottom is fatter; the bass swells every blob.
    size *= (0.85 + 0.25 * saturate(centre.y + 0.4)) * (1.0 + Bass * 0.16 + Beat * 0.05);
    // Moving blobs stretch out into teardrops.
    radius = size * float2(1.0 / (1.0 + moving * 0.22), 1.0 + moving * 0.5);
}

// The kth of n points in a soft round cloud: dense in the middle, thinning out towards the edge.
// The points spiral out like the seeds of a sunflower rather than falling at random, so the wax
// looks smooth, not grainy.
float2 cloudOffset(uint k, uint n, uint id)
{
    float spread = min((k + fixedRandom(id, 1.0)) / max((float)n, 1.0), 1.0) * 0.997;
    float angle = (k * 1640531527u) / 4294967296.0 * 2.0 * PI;
    return float2(cos(angle), sin(angle)) * sqrt(-2.0 * log(1.0 - spread));
}

void Spawn(inout Particle particle, uint id)
{
    particle.life = 1e6;
    particle.velocity = 0.0;
    particle.data = 0.0;
    particle.position = 0.0;
    if (shareOf(id) >= 1.0 - BubbleShare)
    {
        // A bubble starts by the heater and wobbles upward until it fades.
        particle.position = float3((random(id, 3.0) * 2.0 - 1.0) * Aspect, 0.98, 0.0);
        particle.velocity = float3(0.0, -(0.08 + random(id, 4.0) * 0.12), 0.0);
        particle.life = 1.4 / -particle.velocity.y;
        if (ParticleReset > 0.5)
        {
            particle.life *= random(id, 5.0);
            particle.position.y -= random(id, 6.0) * 1.2;
        }
    }
}

void Simulate(inout Particle particle, uint id)
{
    if (shareOf(id) >= 1.0 - BubbleShare)
    {
        // Bubbles rise faster in loud passages, swaying from side to side.
        float dt = min(DeltaTime, 0.05);
        particle.position.y += particle.velocity.y * dt * (0.7 + Level * 0.8 + Beat * 0.6);
        particle.position.x += sin(particle.age * 3.0 + fixedRandom(id, 7.0) * 6.0) * 0.04 * dt;
    }
}

Sprite Show(Particle particle, uint id)
{
    float share = shareOf(id);
    Sprite sprite = (Sprite)0;
    if (share >= 1.0 - BubbleShare)
    {
        float fade = saturate(particle.age * 2.0) * saturate(particle.position.y + 0.2);
        sprite.position = particle.position.xy;
        sprite.size = 2.0 + fixedRandom(id, 8.0) * 2.5;
        sprite.colour = float3(0.0, (0.15 + Treble * 0.6) * fade, 0.0);
        return sprite;
    }

    // Which blob this particle belongs to, and which of its points it is.
    float blobShare = (1.0 - BubbleShare) / Blobs;
    int index = min((int)(share / blobShare), Blobs - 1);
    uint first = (uint)ceil(index * blobShare * ParticleCount - 0.5);
    uint count = (uint)(blobShare * ParticleCount);
    float2 offset = cloudOffset(id - min(first, id), count, id);

    float2 centre;
    float2 radius;
    blobShape(index, centre, radius);

    // Wobble the outline, more with the mids, so the blobs look soft and alive. The swirl is
    // gentle enough that the cloud never folds over itself (which would show as hard creases).
    float3 swirl = float3(offset * 0.4, Time * 0.2 + index * 2.0);
    offset += float2(noise3(swirl), noise3(swirl + float3(5.2, 1.3, 7.7))) * (0.35 + Mid * 0.15);

    sprite.position = centre + offset * radius;
    sprite.size = 7.0;
    // The same peak density for every blob, however big it is.
    sprite.colour = float3(radius.x * radius.y / blobShare * 0.36, 0.0, 0.0);
    return sprite;
}

// The pool of wax lying over the heater, heaving slowly: its density at p.
float poolDensity(float2 p)
{
    float surface = 0.9 - 0.05 * sin(p.x * 2.3 + Time * 0.2) - 0.04 * sin(p.x * 5.1 - Time * 0.13) - Bass * 0.04;
    return 1.3 * exp(-pow(max(surface - p.y, 0.0) / 0.1, 2.0) * 0.5);
}

// The colours of the wax and the liquid drift slowly between three classic lamps.
void lampColours(out float3 waxDeep, out float3 waxBright, out float3 liquidTop, out float3 liquidBottom)
{
    float scheme = Time / 70.0;
    float which = wrap(floor(scheme), 3.0);
    float blend = smoothstep(0.8, 1.0, frac(scheme));
    float3 deep[3] = { float3(0.75, 0.06, 0.02), float3(0.75, 0.03, 0.35), float3(0.1, 0.45, 0.05) };
    float3 bright[3] = { float3(1.5, 0.6, 0.1), float3(1.5, 0.5, 0.9), float3(0.9, 1.4, 0.2) };
    float3 top[3] = { float3(0.05, 0.01, 0.08), float3(0.0, 0.04, 0.08), float3(0.03, 0.0, 0.08) };
    float3 bottom[3] = { float3(0.5, 0.08, 0.3), float3(0.05, 0.35, 0.5), float3(0.35, 0.05, 0.55) };
    int a = (int)which;
    int b = (int)wrap(which + 1.0, 3.0);
    waxDeep = lerp(deep[a], deep[b], blend);
    waxBright = lerp(bright[a], bright[b], blend);
    liquidTop = lerp(top[a], top[b], blend);
    liquidBottom = lerp(bottom[a], bottom[b], blend);
}

float3 Render(float2 uv, float2 p)
{
    // Blur the wax density over two rings of taps, so the blobs are smooth; the taps' imbalance
    // gives the slope of the surface, for lighting.
    float2 toUvScale = spritePixel() / (2.0 * float2(Aspect, 1.0));
    float density = particles(uv).r * 2.0;
    float2 slope = 0.0;
    float total = 2.0;
    [unroll]
    for (int i = 0; i < 12; i++)
    {
        float angle = i * (2.0 * PI / 12.0) + (i & 1) * 0.3;
        float reach = (i & 1) == 0 ? 7.0 : 14.0;
        float2 direction = float2(cos(angle), sin(angle));
        float tap = particles(uv + direction * reach * toUvScale).r;
        float weight = (i & 1) == 0 ? 1.0 : 0.6;
        density += tap * weight;
        slope += direction * tap * weight;
        total += weight;
    }
    density = density / total + poolDensity(p);
    slope = slope / total + float2(0.0, -0.4) * smoothstep(0.65, 1.0, p.y) * saturate(poolDensity(p));

    float3 waxDeep;
    float3 waxBright;
    float3 liquidTop;
    float3 liquidBottom;
    lampColours(waxDeep, waxBright, liquidTop, liquidBottom);

    // The liquid glows from the heater below, which flares on the beat.
    float height = saturate(p.y * 0.5 + 0.5);
    float heater = exp(-(1.0 - height) * 5.0) * (0.5 + Beat * 0.6 + Bass * 0.3);
    float3 liquid = lerp(liquidTop, liquidBottom, height * height) + liquidBottom * heater;

    // Thick wax glows brightly; thin edges are deep and rich. Lower wax is lit more by the heater.
    float wax = smoothstep(0.47, 0.56, density);
    float thickness = saturate((density - 0.5) * 0.9);
    float3 body = lerp(waxDeep, waxBright, thickness * thickness) * (0.6 + 0.7 * height + heater * 0.4);
    // Edges facing down catch the heater's light; a soft sheen from above lights the tops.
    float2 outward = -slope / max(length(slope), 1e-4);
    float edge = saturate(length(slope) * 4.0) * (1.0 - thickness);
    body += waxBright * edge * saturate(outward.y) * (0.3 + heater) * 0.6;
    body += float3(1.0, 0.95, 0.9) * edge * saturate(-outward.y - 0.2) * 0.2;

    // The wax also glows a little into the liquid around it.
    float3 colour = lerp(liquid + waxDeep * saturate(density) * 0.3, body, wax);

    // Bubbles, and the reflections of the glass running up each side.
    colour += float3(1.0, 0.95, 0.85) * particles(uv).g;
    float glass = exp(-pow((p.x / Aspect + 0.72) * 9.0, 2.0)) + 0.6 * exp(-pow((p.x / Aspect - 0.8) * 14.0, 2.0));
    colour += float3(1.0, 0.9, 0.95) * glass * 0.05 * (0.7 + height);
    return colour;
}
