// Name: Knot Stream
// Rivers of light pour endlessly around a glowing knot that reties itself into new shapes, swelling wherever its slice of the spectrum is loud.
// Family: Particles
// Tags: Colourful, Dark, Neon
// Particles: 262144

// Every so often the knot reties itself: the particles flow smoothly from one knot to the next.
static const float KnotTime = 18.0;

float2 knotWinding(int index)
{
    int which = (int)wrap((float)index, 5.0);
    return which == 0 ? float2(2, 3) : which == 1 ? float2(3, 5) : which == 2 ? float2(2, 5) : which == 3 ? float2(3, 4) : float2(3, 7);
}

float3 torusKnot(float2 winding, float s)
{
    float angle = s * 2.0 * PI;
    float ring = 0.75 + 0.3 * cos(winding.y * angle);
    return float3(ring * cos(winding.x * angle), 0.3 * sin(winding.y * angle), ring * sin(winding.x * angle));
}

float3 knot(float s)
{
    float phase = Time / KnotTime;
    float which = floor(phase);
    float blend = smoothstep(0.82, 1.0, phase - which);
    return lerp(torusKnot(knotWinding((int)which), s), torusKnot(knotWinding((int)which + 1), s), blend);
}

// Where along the knot the particle is: it keeps flowing, surging a little on each beat.
float alongKnot(uint id)
{
    float flow = Time * 0.022 + (BeatCount - Beat) * 0.004;
    return frac(fixedRandom(id, 1.0) + flow * (0.8 + 0.4 * fixedRandom(id, 2.0)));
}

float3 placeOf(uint id)
{
    float s = alongKnot(id);
    // Six strands twist around the tube like the threads of a rope.
    float strand = floor(fixedRandom(id, 3.0) * 6.0);
    float around = strand * (PI / 3.0) + s * 90.0 + Time * 0.7 + (fixedRandom(id, 5.0) - 0.5) * 0.35;

    float3 centre = knot(s);
    float3 ahead = knot(s + 0.002);
    float3 tangent = normalize(ahead - centre);
    float3 side = normalize(cross(tangent, float3(0.0, 1.0, 0.0)));
    float3 up = cross(side, tangent);

    // The tube swells where its slice of the spectrum is loud.
    float swell = 0.03 + spectrum(abs(frac(s * 2.0) * 2.0 - 1.0) * 0.8 + 0.02) * 0.09 + Beat * 0.015;
    float radius = swell * (0.8 + 0.2 * fixedRandom(id, 4.0));
    return centre + (side * cos(around) + up * sin(around)) * radius;
}

void Spawn(inout Particle particle, uint id)
{
    particle.position = placeOf(id);
    particle.velocity = 0.0;
    particle.life = 1e6;
    particle.data = 0.0;
}

void Simulate(inout Particle particle, uint id)
{
    float3 place = placeOf(id);
    particle.velocity = (place - particle.position) / max(DeltaTime, 1e-4);
    particle.position = place;
    particle.data.x = alongKnot(id);
}

float3 cameraEye()
{
    float angle = Time * 0.06;
    return float3(sin(angle) * 2.2, -1.0 + sin(Time * 0.05) * 0.6, cos(angle) * 2.2);
}

Sprite Show(Particle particle, uint id)
{
    float3 eye = cameraEye();
    float zoom = 2.1 + Bass * 0.06;
    float roll = sin(Time * 0.04) * 0.25;
    float3 seen = project(particle.position, eye, 0.0, zoom, roll);
    // A streak back to where it was a moment ago (wrapping round the knot jumps, so skip those).
    float3 before = project(particle.position - particle.velocity * min(DeltaTime, 0.05) * 1.5, eye, 0.0, zoom, roll);
    float2 streak = seen.xy - before.xy;
    if (dot(streak, streak) > 0.01)
    {
        streak = 0.0;
    }

    // Rainbow bands run along the knot, and each part glows with its slice of the spectrum.
    float s = particle.data.x;
    float level = spectrum(abs(frac(s * 2.0) * 2.0 - 1.0) * 0.8 + 0.02);
    float3 colour = hsv2rgb(s * 3.0 - Time * 0.05, 0.8, 1.0);

    Sprite sprite = (Sprite)0;
    sprite.position = seen.xy - streak * 0.5;
    sprite.streak = streak;
    sprite.size = seen.z > 0.1 ? 0.8 : 0.0;
    sprite.colour = colour * (0.12 + level * 0.3) * (1.0 + Beat * 0.4);
    focus(sprite, seen.z, length(eye), 0.025);
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    return particles(uv) + previous(uv) * 0.6;
}
