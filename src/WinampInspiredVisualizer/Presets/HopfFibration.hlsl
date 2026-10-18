// Name: Hopf Fibration
// Rings of light that all pass through one another without ever touching, nested into glowing tori like the layers of an onion, with particles racing round every ring; the bass speeds the flow, each layer swells with its own part of the spectrum and beats send pulses chasing round the circles.
// Family: Particles
// Tags: Geometric, 3D
// Added: 2026-10-17 22:17
// Particles: 262144

// The Hopf fibration fills space with circles. Every point on an ordinary sphere (the "base")
// has its own circle, and any two of those circles are linked like the rings of a chain.
// Circles whose base points share a latitude lie together on one torus, so taking a few
// latitudes gives a set of nested tori woven entirely from interlocking rings.
//
// Each particle belongs to one circle and travels round it. The circle lives in four
// dimensions; a slow rotation there, followed by a stereographic projection into our space,
// makes the tori gently roll and turn inside out without the circles ever coming apart.
static const float Layers = 5.0;
static const float CirclesPerLayer = 40.0;

// Which layer (latitude) and which circle on it a particle rides.
float layerOf(uint id)
{
    return floor(fixedRandom(id, 1.0) * Layers);
}

float circleOf(uint id)
{
    return floor(fixedRandom(id, 2.0) * CirclesPerLayer);
}

// The latitude of a layer, as the angle that sets how the circle shares itself between the two
// complex planes. Each layer breathes with its own slice of the spectrum.
float layerAngle(float layer)
{
    float base = lerp(0.28, 1.3, layer / (Layers - 1.0));
    return base + spectrum(0.04 + layer * 0.17) * 0.07 + sin(Time * 0.11 + layer) * 0.03;
}

// A point on the circle above base point (latitude eta, longitude phi), travelled along by t,
// as a point of the 3-sphere in four dimensions.
float4 hopfPoint(float eta, float phi, float t)
{
    float a = t;
    float b = t + phi;
    return float4(cos(a) * sin(eta), sin(a) * sin(eta), cos(b) * cos(eta), sin(b) * cos(eta));
}

// The slow four-dimensional turn, then the projection from the north pole of the 3-sphere.
float3 projectToSpace(float4 q)
{
    float2 xw = rotate(q.xw, Time * 0.045);
    q.x = xw.x;
    q.w = xw.y;
    float2 zw = rotate(q.zw, sin(Time * 0.031) * 0.6);
    q.z = zw.x;
    q.w = zw.y;
    float2 yz = rotate(q.yz, Time * 0.02);
    q.y = yz.x;
    q.z = yz.y;
    float scale = 1.0 / max(1.0 - q.w, 0.05);
    return q.xyz * scale;
}

// How much the projection magnifies the circle a particle is on: big circles spread their
// particles thinner, so they're drawn brighter to match.
float circleScale(uint id)
{
    float layer = layerOf(id);
    float eta = layerAngle(layer);
    // Average of 1/(1-w) round the circle, which is 1/sqrt(1-r^2) for a circle at w-radius r.
    return 1.0 / sqrt(max(1.0 - cos(eta) * cos(eta) * 0.85, 0.05));
}

float3 circlePosition(uint id, float t)
{
    float layer = layerOf(id);
    float circle = circleOf(id);
    // Neighbouring layers are twisted a little against each other so the rings don't line up.
    float phi = (circle + layer * 0.5) / CirclesPerLayer * 2.0 * PI;
    return projectToSpace(hopfPoint(layerAngle(layer), phi, t));
}

void Spawn(inout Particle particle, uint id)
{
    // Spread evenly round its circle, plus a hair of jitter so the rings look like threads.
    float t = fixedRandom(id, 3.0) * 2.0 * PI;
    particle.data = float4(t, 0.0, 0.0, 0.0);
    particle.position = circlePosition(id, t);
    particle.velocity = 0.0;
    particle.life = 1e6;
}

void Simulate(inout Particle particle, uint id)
{
    float dt = min(DeltaTime, 0.05);
    float layer = layerOf(id);
    // Inner layers run faster than outer ones, so the weave keeps shifting.
    float speed = (0.18 + Bass * 0.55 + Beat * 0.25) * lerp(1.3, 0.75, layer / (Layers - 1.0));
    particle.data.x = wrap(particle.data.x + speed * dt, 2.0 * PI);

    float3 moved = circlePosition(id, particle.data.x);
    particle.velocity = (moved - particle.position) / max(DeltaTime, 1e-4);
    particle.position = moved;
}

float3 cameraEye()
{
    float angle = Time * 0.05;
    return float3(sin(angle) * 5.2, -2.2 + sin(Time * 0.043) * 1.2, cos(angle) * 5.2);
}

Sprite Show(Particle particle, uint id)
{
    Sprite sprite = (Sprite)0;
    float3 eye = cameraEye();
    float zoom = 2.3 + Bass * 0.05;
    float roll = sin(Time * 0.027) * 0.12;
    float3 seen = project(particle.position, eye, 0.0, zoom, roll);
    float3 seenBefore = project(particle.position - particle.velocity * 0.025, eye, 0.0, zoom, roll);

    float layer = layerOf(id);
    float circle = circleOf(id);
    float t = particle.data.x;

    // Colour by the circle's base point: longitude picks the hue, latitude its paleness.
    float hue = circle / CirclesPerLayer + Time * 0.01;
    float level = spectrum(0.04 + layer * 0.17);
    float3 colour = hsv2rgb(hue, lerp(0.8, 0.55, layer / (Layers - 1.0)), 1.0);

    // A beat sends a bright bead running round every ring.
    float bead = pow(saturate(cos(t - BeatCount * 1.7 + circle * 0.4) * 0.5 + 0.5), 24.0) * Beat;
    float brightness = (0.05 * (0.55 + level * 1.4) + bead * 0.2) * circleScale(id);
    colour *= brightness;
    colour *= 1.0 + step(0.995, fixedRandom(id, 6.0)) * Treble * 5.0;

    // The projection stretches circles that pass near the pole into huge arcs; fade those out
    // so the far reaches stay quiet.
    float spread = length(particle.position);
    colour *= saturate((4.5 - spread) / 1.5);
    // And the reverse near the middle, where many small rings crowd through the same space.
    colour *= 0.35 + 0.65 * smoothstep(0.25, 1.1, spread);

    sprite.position = seen.xy;
    sprite.streak = seen.xy - seenBefore.xy;
    sprite.size = (seen.z > 0.2 && spread < 6.0) ? 1.0 : 0.0;
    sprite.colour = colour;
    focus(sprite, seen.z, length(eye), 0.06);
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    // A faint violet haze behind the knot so the dark isn't flat.
    float haze = exp(-dot(p, p) * 1.2) * (0.02 + Bass * 0.03);
    return particles(uv) + previous(uv) * 0.6 + float3(0.5, 0.4, 0.9) * haze;
}
