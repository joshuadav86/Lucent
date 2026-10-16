// Name: Silk Currents
// A quarter of a million grains of light ride swirling currents, painting long silky strands that curl around drifting whirlpools and surge with the beat.
// Particles: 262144

// Three whirlpools wander the screen; the bass spins them harder.
float2 whirlpool(int index)
{
    float t = Time * (0.05 + index * 0.013) + index * 2.1;
    return float2(sin(t * 1.3 + index) * Aspect * 0.6, cos(t * 0.9 + index * 1.7) * 0.6);
}

float2 current(float2 position)
{
    float2 flow = curlNoise2(position * 1.1, Time * 0.07) * 0.22;
    [unroll]
    for (int index = 0; index < 3; index++)
    {
        float2 offset = position - whirlpool(index);
        float falloff = exp(-dot(offset, offset) * 3.0);
        float spin = (index == 1 ? -1.0 : 1.0) * (0.5 + Bass * 0.9);
        flow += float2(-offset.y, offset.x) * falloff * spin;
    }
    return flow * (0.7 + Level * 0.6 + Beat * 0.6);
}

void Spawn(inout Particle particle, uint id)
{
    particle.position = float3((random(id, 1.0) * 2.0 - 1.0) * Aspect * 1.05, (random(id, 2.0) * 2.0 - 1.0) * 1.05, 0.0);
    particle.velocity = 0.0;
    particle.life = lerp(2.5, 7.0, random(id, 3.0));
    // Each grain keeps the colour of where it was born, so the currents mix the colours like paint.
    particle.data = float4(atan2(particle.position.y, particle.position.x) / (2.0 * PI) + Time * 0.02, particle.life, 0.0, 0.0);
}

void Simulate(inout Particle particle, uint id)
{
    float dt = min(DeltaTime, 0.05);
    float2 position = particle.position.xy;
    // Midpoint steps keep the strands smooth around the tight whirlpools.
    float2 halfway = position + current(position) * dt * 0.5;
    float2 velocity = current(halfway);
    particle.position.xy = position + velocity * dt;
    particle.velocity = float3(velocity, 0.0);
}

Sprite Show(Particle particle, uint id)
{
    float speed = length(particle.velocity.xy);
    float fadeIn = saturate(particle.age * 2.0);
    float fadeOut = saturate(particle.life * 1.5);
    float3 colour = hsv2rgb(particle.data.x + fixedRandom(id, 4.0) * 0.08, 0.85, 1.0);
    colour = lerp(colour, 1.0, saturate(speed * 1.2 - 0.7) * 0.35);

    // Each grain is drawn as a streak over the ground it covered this frame, so strands stay unbroken.
    float2 moved = particle.velocity.xy * DeltaTime;
    Sprite sprite = (Sprite)0;
    sprite.position = particle.position.xy - moved * 0.5;
    sprite.streak = moved;
    sprite.size = 0.8;
    sprite.colour = colour * fadeIn * fadeOut * (0.02 + speed * speed * 0.35) * (1.0 + Treble * 0.6);
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    // Long trails, so each grain paints a strand behind it; the same length at any frame rate.
    float frames = min(DeltaTime * 60.0, 3.0);
    return particles(uv) * frames + previous(uv) * exp(-DeltaTime * 2.8);
}
