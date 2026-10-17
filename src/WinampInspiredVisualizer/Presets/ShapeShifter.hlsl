// Name: Shape Shifter
// A swarm of glowing nanobots pours itself from one form into the next every two bars, sphere, torus, cube, star, double helix, heart and knot, boiling into a storm while it changes and then settling into a shimmering skin that ripples with the spectrum.
// Family: Particles
// Tags: Geometric, Neon, 3D
// Particles: 262144

static const int ShapeCount = 7;

// Which shape the swarm is making: a new one every two bars.
int shapeNow()
{
    return (int)wrap(floor(BeatCount / 8.0), (float)ShapeCount);
}

// A point on shape s for a particle, picked from its two fixed random numbers a and b (plus c
// for shapes made of parts). Every shape is about two units across.
float3 shapePoint(int s, float a, float b, float c)
{
    float angle = a * 2.0 * PI;
    if (s == 0)
    {
        // Sphere.
        float z = b * 2.0 - 1.0;
        float r = sqrt(1.0 - z * z);
        return float3(r * cos(angle), z, r * sin(angle)) * 0.95;
    }
    if (s == 1)
    {
        // Torus.
        float tube = b * 2.0 * PI;
        float ring = 0.78 + 0.32 * cos(tube);
        return float3(ring * cos(angle), 0.32 * sin(tube), ring * sin(angle));
    }
    if (s == 2)
    {
        // Cube: six faces, with a few more particles on the edges so they read crisply.
        int face = (int)(c * 6.0);
        float2 uv = float2(a, b) * 2.0 - 1.0;
        uv = sign(uv) * pow(abs(uv), 0.6);
        float3 q = face == 0 ? float3(1.0, uv) : (face == 1 ? float3(-1.0, uv) : (face == 2 ? float3(uv.x, 1.0, uv.y) : (face == 3 ? float3(uv.x, -1.0, uv.y) : (face == 4 ? float3(uv, 1.0) : float3(uv, -1.0)))));
        return q * 0.72;
    }
    if (s == 3)
    {
        // A spiky star: a sphere pushed out into fourteen points.
        float z = b * 2.0 - 1.0;
        float r = sqrt(1.0 - z * z);
        float3 d = float3(r * cos(angle), z, r * sin(angle));
        float3 m = abs(d);
        float axes = pow(max(m.x, max(m.y, m.z)), 16.0);
        float corners = pow(saturate(dot(m, 0.57735)), 40.0);
        return d * (0.42 + 0.75 * max(axes, corners * 0.85));
    }
    if (s == 4)
    {
        // Double helix: two strands and the rungs between them.
        float y = a * 2.4 - 1.2;
        float turn = y * 2.6 * PI;
        float3 strandA = float3(cos(turn), y / 0.48, sin(turn)) * 0.48;
        float3 strandB = float3(cos(turn + PI), y / 0.48, sin(turn + PI)) * 0.48;
        if (c < 0.36)
        {
            return strandA + float3(cos(b * 6.28), 0.0, sin(b * 6.28)) * 0.05;
        }
        if (c < 0.72)
        {
            return strandB + float3(cos(b * 6.28), 0.0, sin(b * 6.28)) * 0.05;
        }
        float rung = floor(a * 28.0) / 28.0 * 2.4 - 1.2;
        float rungTurn = rung * 2.6 * PI;
        float3 endA = float3(cos(rungTurn), rung / 0.48, sin(rungTurn)) * 0.48;
        float3 endB = float3(cos(rungTurn + PI), rung / 0.48, sin(rungTurn + PI)) * 0.48;
        return lerp(endA, endB, b);
    }
    if (s == 5)
    {
        // Heart: the classic outline, filled and puffed out like a cushion.
        float t = angle;
        float2 edge = float2(16.0 * pow(sin(t), 3.0), 13.0 * cos(t) - 5.0 * cos(2.0 * t) - 2.0 * cos(3.0 * t) - cos(4.0 * t)) / 17.0;
        float inside = sqrt(b);
        float2 spot = lerp(float2(0.0, 0.1), edge, inside);
        float puff = sqrt(saturate(1.0 - inside * inside)) * 0.42 * (c < 0.5 ? 1.0 : -1.0);
        return float3(spot.x, spot.y, puff) * 1.05;
    }
    // Trefoil knot, as a tube.
    float t = angle;
    float3 centre = float3(sin(t) + 2.0 * sin(2.0 * t), cos(t) - 2.0 * cos(2.0 * t), -sin(3.0 * t)) * 0.32;
    float3 ahead = float3(cos(t) + 4.0 * cos(2.0 * t), -sin(t) + 4.0 * sin(2.0 * t), -3.0 * cos(3.0 * t));
    float3 side = normalize(cross(ahead, float3(0.0, 0.0, 1.0)) + 1e-4);
    float3 other = normalize(cross(ahead, side));
    float tube = b * 2.0 * PI;
    return centre + (side * cos(tube) + other * sin(tube)) * 0.12;
}

// The shape, slowly turning.
float3 placed(int s, uint id)
{
    float3 q = shapePoint(s, fixedRandom(id, 1.0), fixedRandom(id, 2.0), fixedRandom(id, 3.0));
    q.xz = rotate(q.xz, Time * 0.25);
    q.xy = rotate(q.xy, sin(Time * 0.13) * 0.35);
    return q;
}

void Spawn(inout Particle particle, uint id)
{
    particle.position = randomInSphere(id, 4.0) * 1.5;
    particle.velocity = 0.0;
    particle.life = 1e6;
    particle.data = 0.0;
}

void Simulate(inout Particle particle, uint id)
{
    float dt = min(DeltaTime, 0.05);
    // Each particle sets off a moment after its neighbours, so the change sweeps through.
    float lag = fixedRandom(id, 5.0) * 0.8;
    int s = (int)wrap(floor((BeatCount - lag) / 8.0), (float)ShapeCount);
    float3 home = placed(s, id);
    float3 toHome = home - particle.position;
    float away = length(toHome);

    // A spring pulls each bot to its place; far from home it boils along a swirling flow, so a
    // change looks like a storm rather than points sliding in straight lines.
    float storm = smoothstep(0.08, 0.7, away);
    storm *= storm;
    float3 swirl = curlNoise(particle.position * 1.3 + float3(0.0, Time * 0.4, 0.0));
    float3 wanted = toHome * (5.5 + Bass * 2.0) + swirl * storm * (0.9 + Mid * 0.8);
    particle.velocity = lerp(particle.velocity, wanted, saturate(dt * 5.0));
    // A beat gives the skin a little outward kick.
    particle.velocity += normalize(particle.position + 1e-4) * Beat * dt * 4.0 * (1.0 - storm);
    particle.position += particle.velocity * dt;
    particle.data.x = storm;
}

float3 cameraEye()
{
    float angle = Time * 0.05;
    return float3(sin(angle) * 3.6, 0.7 + sin(Time * 0.07) * 0.6, cos(angle) * 3.6);
}

Sprite Show(Particle particle, uint id)
{
    float3 eye = cameraEye();
    float roll = sin(Time * 0.05) * 0.08;
    float3 seen = project(particle.position, eye, 0.0, 2.3, roll);
    float3 seenBefore = project(particle.position - particle.velocity * 0.04, eye, 0.0, 2.3, roll);

    // Bands of colour sweep up the shape; each band also rides one part of the spectrum.
    float height = particle.position.y * 0.5 + 0.5;
    float band = frac(height * 0.8 + fixedRandom(id, 3.0) * 0.1);
    float level = spectrum(band * 0.8 + 0.02);
    float hue = height * 0.6 + Time * 0.04;
    float3 colour = neon(hue);
    // Ripples run over the skin with the beat.
    float ripple = pow(saturate(sin(length(particle.position) * 9.0 - BeatCount * 2.0 - (1.0 - Beat) * 6.0) * 0.5 + 0.5), 6.0) * Beat;
    float storm = particle.data.x;
    // Mid-change the swarm runs hot and white; settled, it glows its colours.
    colour = lerp(colour, float3(1.0, 0.95, 1.0), storm * 0.35 + ripple * 0.3);
    colour *= 0.075 * (0.5 + level * 1.6 + ripple * 1.2) * (1.0 + storm * 0.4);
    colour *= 1.0 + step(0.995, fixedRandom(id, 6.0)) * Treble * 10.0;

    Sprite sprite = (Sprite)0;
    sprite.position = float2(seen.x, -seen.y);
    sprite.streak = float2(seen.x - seenBefore.x, seenBefore.y - seen.y);
    sprite.size = seen.z > 0.1 ? 0.9 : 0.0;
    sprite.colour = colour;
    focus(sprite, seen.z, length(eye), 0.05);
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    // A dark stage with a faint glow beneath the swarm, like light reflected off a floor.
    float3 stage = float3(0.006, 0.004, 0.014) + float3(0.04, 0.02, 0.06) * exp(-length(p - float2(0.0, 0.75)) * 2.5) * (0.6 + Bass);
    return stage + particles(uv) + previous(uv) * 0.6;
}
