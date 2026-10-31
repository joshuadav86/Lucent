// Name: Polytope Bloom
// The 600-cell, a four-dimensional crystal of 120 corners and 720 edges, turns through the fourth dimension and casts its shadow into ours as nested shells of glowing neon arcs. Every beat gives the turn a fresh push and sends light running out from each corner, the shells breathe with the bass, the colours ride the spectrum, and corners sparkle with the treble.
// Family: Particles
// Tags: Geometric, Neon, 3D
// Added: 2026-10-31 02:30
// Particles: 262144

// The 600-cell's corners sit on the unit sphere in 4D (x, y, z, w). Read as quaternions (w the real
// part) they form a group, so the 12 neighbours of any corner v are v times the 12 neighbours of
// (0, 0, 0, 1), listed in PB_NEXT. Each particle runs along one edge, on the great circle between its
// two corners. The turned 4D crystal is seen in our 3D space by stereographic projection from
// (0, 0, 0, 1): every great circle lands as a circle or a line, so the straight edges become arcs, and
// the cells nearest that pole swell huge and sweep out past the camera while the far ones shrink to
// a tight knot in the middle.
static const float4 PB_CORNERS[120] = {
    float4(0.000000, 0.000000, 0.000000, 1.000000),
    float4(-0.500000, 0.000000, -0.309017, 0.809017),
    float4(-0.500000, 0.000000, 0.309017, 0.809017),
    float4(-0.309017, -0.500000, 0.000000, 0.809017),
    float4(-0.309017, 0.500000, 0.000000, 0.809017),
    float4(0.000000, -0.309017, -0.500000, 0.809017),
    float4(0.000000, -0.309017, 0.500000, 0.809017),
    float4(0.000000, 0.309017, -0.500000, 0.809017),
    float4(0.000000, 0.309017, 0.500000, 0.809017),
    float4(0.309017, -0.500000, 0.000000, 0.809017),
    float4(0.309017, 0.500000, 0.000000, 0.809017),
    float4(0.500000, 0.000000, -0.309017, 0.809017),
    float4(0.500000, 0.000000, 0.309017, 0.809017),
    float4(-0.809017, -0.309017, 0.000000, 0.500000),
    float4(-0.809017, 0.309017, 0.000000, 0.500000),
    float4(-0.500000, -0.500000, -0.500000, 0.500000),
    float4(-0.500000, -0.500000, 0.500000, 0.500000),
    float4(-0.500000, 0.500000, -0.500000, 0.500000),
    float4(-0.500000, 0.500000, 0.500000, 0.500000),
    float4(-0.309017, 0.000000, -0.809017, 0.500000),
    float4(-0.309017, 0.000000, 0.809017, 0.500000),
    float4(0.000000, -0.809017, -0.309017, 0.500000),
    float4(0.000000, -0.809017, 0.309017, 0.500000),
    float4(0.000000, 0.809017, -0.309017, 0.500000),
    float4(0.000000, 0.809017, 0.309017, 0.500000),
    float4(0.309017, 0.000000, -0.809017, 0.500000),
    float4(0.309017, 0.000000, 0.809017, 0.500000),
    float4(0.500000, -0.500000, -0.500000, 0.500000),
    float4(0.500000, -0.500000, 0.500000, 0.500000),
    float4(0.500000, 0.500000, -0.500000, 0.500000),
    float4(0.500000, 0.500000, 0.500000, 0.500000),
    float4(0.809017, -0.309017, 0.000000, 0.500000),
    float4(0.809017, 0.309017, 0.000000, 0.500000),
    float4(-0.809017, 0.000000, -0.500000, 0.309017),
    float4(-0.809017, 0.000000, 0.500000, 0.309017),
    float4(-0.500000, -0.809017, 0.000000, 0.309017),
    float4(-0.500000, 0.809017, 0.000000, 0.309017),
    float4(0.000000, -0.500000, -0.809017, 0.309017),
    float4(0.000000, -0.500000, 0.809017, 0.309017),
    float4(0.000000, 0.500000, -0.809017, 0.309017),
    float4(0.000000, 0.500000, 0.809017, 0.309017),
    float4(0.500000, -0.809017, 0.000000, 0.309017),
    float4(0.500000, 0.809017, 0.000000, 0.309017),
    float4(0.809017, 0.000000, -0.500000, 0.309017),
    float4(0.809017, 0.000000, 0.500000, 0.309017),
    float4(-1.000000, 0.000000, 0.000000, 0.000000),
    float4(-0.809017, -0.500000, -0.309017, 0.000000),
    float4(-0.809017, -0.500000, 0.309017, 0.000000),
    float4(-0.809017, 0.500000, -0.309017, 0.000000),
    float4(-0.809017, 0.500000, 0.309017, 0.000000),
    float4(-0.500000, -0.309017, -0.809017, 0.000000),
    float4(-0.500000, -0.309017, 0.809017, 0.000000),
    float4(-0.500000, 0.309017, -0.809017, 0.000000),
    float4(-0.500000, 0.309017, 0.809017, 0.000000),
    float4(-0.309017, -0.809017, -0.500000, 0.000000),
    float4(-0.309017, -0.809017, 0.500000, 0.000000),
    float4(-0.309017, 0.809017, -0.500000, 0.000000),
    float4(-0.309017, 0.809017, 0.500000, 0.000000),
    float4(0.000000, -1.000000, 0.000000, 0.000000),
    float4(0.000000, 0.000000, -1.000000, 0.000000),
    float4(0.000000, 0.000000, 1.000000, 0.000000),
    float4(0.000000, 1.000000, 0.000000, 0.000000),
    float4(0.309017, -0.809017, -0.500000, 0.000000),
    float4(0.309017, -0.809017, 0.500000, 0.000000),
    float4(0.309017, 0.809017, -0.500000, 0.000000),
    float4(0.309017, 0.809017, 0.500000, 0.000000),
    float4(0.500000, -0.309017, -0.809017, 0.000000),
    float4(0.500000, -0.309017, 0.809017, 0.000000),
    float4(0.500000, 0.309017, -0.809017, 0.000000),
    float4(0.500000, 0.309017, 0.809017, 0.000000),
    float4(0.809017, -0.500000, -0.309017, 0.000000),
    float4(0.809017, -0.500000, 0.309017, 0.000000),
    float4(0.809017, 0.500000, -0.309017, 0.000000),
    float4(0.809017, 0.500000, 0.309017, 0.000000),
    float4(1.000000, 0.000000, 0.000000, 0.000000),
    float4(-0.809017, 0.000000, -0.500000, -0.309017),
    float4(-0.809017, 0.000000, 0.500000, -0.309017),
    float4(-0.500000, -0.809017, 0.000000, -0.309017),
    float4(-0.500000, 0.809017, 0.000000, -0.309017),
    float4(0.000000, -0.500000, -0.809017, -0.309017),
    float4(0.000000, -0.500000, 0.809017, -0.309017),
    float4(0.000000, 0.500000, -0.809017, -0.309017),
    float4(0.000000, 0.500000, 0.809017, -0.309017),
    float4(0.500000, -0.809017, 0.000000, -0.309017),
    float4(0.500000, 0.809017, 0.000000, -0.309017),
    float4(0.809017, 0.000000, -0.500000, -0.309017),
    float4(0.809017, 0.000000, 0.500000, -0.309017),
    float4(-0.809017, -0.309017, 0.000000, -0.500000),
    float4(-0.809017, 0.309017, 0.000000, -0.500000),
    float4(-0.500000, -0.500000, -0.500000, -0.500000),
    float4(-0.500000, -0.500000, 0.500000, -0.500000),
    float4(-0.500000, 0.500000, -0.500000, -0.500000),
    float4(-0.500000, 0.500000, 0.500000, -0.500000),
    float4(-0.309017, 0.000000, -0.809017, -0.500000),
    float4(-0.309017, 0.000000, 0.809017, -0.500000),
    float4(0.000000, -0.809017, -0.309017, -0.500000),
    float4(0.000000, -0.809017, 0.309017, -0.500000),
    float4(0.000000, 0.809017, -0.309017, -0.500000),
    float4(0.000000, 0.809017, 0.309017, -0.500000),
    float4(0.309017, 0.000000, -0.809017, -0.500000),
    float4(0.309017, 0.000000, 0.809017, -0.500000),
    float4(0.500000, -0.500000, -0.500000, -0.500000),
    float4(0.500000, -0.500000, 0.500000, -0.500000),
    float4(0.500000, 0.500000, -0.500000, -0.500000),
    float4(0.500000, 0.500000, 0.500000, -0.500000),
    float4(0.809017, -0.309017, 0.000000, -0.500000),
    float4(0.809017, 0.309017, 0.000000, -0.500000),
    float4(-0.500000, 0.000000, -0.309017, -0.809017),
    float4(-0.500000, 0.000000, 0.309017, -0.809017),
    float4(-0.309017, -0.500000, 0.000000, -0.809017),
    float4(-0.309017, 0.500000, 0.000000, -0.809017),
    float4(0.000000, -0.309017, -0.500000, -0.809017),
    float4(0.000000, -0.309017, 0.500000, -0.809017),
    float4(0.000000, 0.309017, -0.500000, -0.809017),
    float4(0.000000, 0.309017, 0.500000, -0.809017),
    float4(0.309017, -0.500000, 0.000000, -0.809017),
    float4(0.309017, 0.500000, 0.000000, -0.809017),
    float4(0.500000, 0.000000, -0.309017, -0.809017),
    float4(0.500000, 0.000000, 0.309017, -0.809017),
    float4(0.000000, 0.000000, 0.000000, -1.000000)
};

static const float4 PB_NEXT[12] = {
    float4(-0.500000, 0.000000, -0.309017, 0.809017),
    float4(-0.500000, 0.000000, 0.309017, 0.809017),
    float4(-0.309017, -0.500000, 0.000000, 0.809017),
    float4(-0.309017, 0.500000, 0.000000, 0.809017),
    float4(0.000000, -0.309017, -0.500000, 0.809017),
    float4(0.000000, -0.309017, 0.500000, 0.809017),
    float4(0.000000, 0.309017, -0.500000, 0.809017),
    float4(0.000000, 0.309017, 0.500000, 0.809017),
    float4(0.309017, -0.500000, 0.000000, 0.809017),
    float4(0.309017, 0.500000, 0.000000, 0.809017),
    float4(0.500000, 0.000000, -0.309017, 0.809017),
    float4(0.500000, 0.000000, 0.309017, 0.809017)
};

static const float EdgeAngle = PI / 5.0;  // 36 degrees between neighbouring corners
static const float CornerShare = 0.05;    // of the particles, how many gather on the corners

float4 pbMultiply(float4 a, float4 b)
{
    return float4(a.w * b.xyz + b.w * a.xyz + cross(a.xyz, b.xyz), a.w * b.w - dot(a.xyz, b.xyz));
}

// The turn. Multiplying on the left and on the right by two unit quaternions turns 4D space in two
// planes at once. It drifts slowly, and each beat adds a push that eases in over the beat.
float4 pbTurnLeft()
{
    float beats = BeatCount + 1.0 - Beat;
    float a = Time * 0.045 + beats * 0.035;
    return float4(normalize(float3(0.3, 1.0, 0.2)) * sin(a), cos(a));
}

float4 pbTurnRight()
{
    float a = Time * 0.028 + sin(Time * 0.05) * 0.4;
    return float4(normalize(float3(1.0, 0.2, -0.4)) * sin(a), cos(a));
}

float4 pbTurned(float4 v)
{
    return pbMultiply(pbMultiply(pbTurnLeft(), v), pbTurnRight());
}

// Down from 4D: stereographic projection from (0, 0, 0, 1), breathing with the bass.
float3 pbShadow(float4 v)
{
    float3 p = v.xyz / max(1.0 - v.w, 0.02);
    return p * (1.0 + Bass * 0.07 + DropPulse * 0.1);
}

// The point a fraction along the edge from corner a to corner b, on the great circle between them.
float4 pbAlong(float4 a, float4 b, float along)
{
    return (a * sin((1.0 - along) * EdgeAngle) + b * sin(along * EdgeAngle)) / sin(EdgeAngle);
}

void pbEdge(Particle particle, out float4 a, out float4 b)
{
    a = PB_CORNERS[(uint)particle.data.x];
    b = pbMultiply(a, PB_NEXT[(uint)particle.data.y]);
}

float3 pbPosition(Particle particle)
{
    float4 a, b;
    pbEdge(particle, a, b);
    return pbShadow(pbTurned(pbAlong(a, b, particle.data.z)));
}

void Spawn(inout Particle particle, uint id)
{
    float corner = min(floor(fixedRandom(id, 1.0) * 120.0), 119.0);
    float next = min(floor(fixedRandom(id, 2.0) * 12.0), 11.0);
    // Corner particles sit within a whisker of their corner; the rest anywhere along the edge.
    bool onCorner = fixedRandom(id, 3.0) < CornerShare;
    float along = onCorner ? fixedRandom(id, 4.0) * 0.025 : fixedRandom(id, 4.0);
    particle.data = float4(corner, next, along, onCorner ? -1.0 : 0.7 + fixedRandom(id, 5.0) * 0.6);
    particle.velocity = 0.0;
    particle.position = pbPosition(particle);
    particle.life = 1e6;
}

void Simulate(inout Particle particle, uint id)
{
    float dt = min(DeltaTime, 0.05);
    if (particle.data.w > 0.0)
    {
        float speed = (0.05 + Bass * 0.25 + Beat * 0.12 + DropPulse * 0.2) * particle.data.w;
        particle.data.z = frac(particle.data.z + speed * dt);
    }
    float3 before = particle.position;
    particle.position = pbPosition(particle);
    float3 moved = particle.position - before;
    // No streak across the jump back to the start of an edge.
    particle.velocity = length(moved) > 0.2 ? 0.0 : moved / max(DeltaTime, 1e-4);
}

float3 cameraEye()
{
    float angle = Time * 0.03 + 0.4;
    return float3(sin(angle) * 4.6, -1.3 + sin(Time * 0.023) * 0.6, cos(angle) * 4.6);
}

Sprite Show(Particle particle, uint id)
{
    Sprite sprite = (Sprite)0;
    float3 eye = cameraEye();
    float3 target = float3(0.0, 0.0, 0.0);
    float zoom = 2.8 * (1.0 + DropPulse * 0.04);
    float roll = sin(Time * 0.04) * 0.06;
    float3 seen = project(particle.position, eye, target, zoom, roll);
    float3 seenBefore = project(particle.position - particle.velocity * 0.03, eye, target, zoom, roll);

    float4 a, b;
    pbEdge(particle, a, b);
    // Colour by how far the edge sits from the crystal's own pole before it turns, so each shell of
    // the 600-cell keeps one colour as it moves, mirrored so the two halves match.
    float shell = abs(a.w + b.w) * 0.5;
    float level = spectrum(0.03 + (1.0 - shell) * 0.55);
    float hue = frac(0.5 + shell * 0.62 + sin(Time * 0.017) * 0.05);
    bool corner = particle.data.w < 0.0;
    float3 colour = hsv2rgb(hue, corner ? 0.35 : 0.9, 1.0);

    // A pulse of light runs out from both ends of every edge to meet in the middle over each beat.
    // With a beat to follow it rides through the whole beat; without one it shows just as it lands.
    float fromEnd = 0.5 - abs(particle.data.z - 0.5);
    float pulseAt = (fromEnd - BeatPhase * 0.5) * 14.0;
    float pulse = exp(-pulseAt * pulseAt) * (SongMapped > 0.0 ? 0.35 + Beat * 0.65 : Beat);
    float glow = corner ? 0.05 + Treble * 0.12 + Beat * 0.04 : 0.12 * (0.4 + level * 1.5) + pulse * 0.2;
    colour *= glow * (0.8 + Intensity * 0.4);
    colour *= 1.0 + step(0.996, fixedRandom(id, 6.0)) * Treble * 4.0;
    colour *= saturate(particle.age * 2.0);

    // The arcs that swing out towards the camera grow sparse and huge: let them fade away gently.
    float reach = length(particle.position);
    colour *= saturate((5.0 - reach) / 2.5) * (seen.z > 0.25 ? 1.0 : 0.0);

    sprite.position = seen.xy;
    float2 streak = seen.xy - seenBefore.xy;
    sprite.streak = streak * min(1.0, 0.012 / max(length(streak), 1e-5));
    sprite.size = seen.z > 0.25 ? (corner ? 1.3 : 0.9) : 0.0;
    sprite.colour = colour;
    // Focus on the knot in the middle so the near shells melt into soft light.
    focus(sprite, seen.z, length(eye - target), 0.06);
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    float haze = exp(-dot(p, p) * 3.0) * (0.008 + Bass * 0.012);
    return particles(uv) + previous(uv) * 0.5 + float3(0.35, 0.25, 0.9) * haze;
}
