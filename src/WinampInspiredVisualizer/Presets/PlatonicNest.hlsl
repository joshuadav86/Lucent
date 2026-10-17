// Name: Platonic Nest
// Kepler's dream of the heavens in streams of light: the five perfect solids nest one inside the next, each turning its own way and humming with its own part of the spectrum, then melting into glowing spheres and folding back into crystal.
// Family: Particles
// Tags: Geometric, 3D, Space, Colourful
// Particles: 262144

// The five solids from the outside in, in the order Kepler nested them: cube, tetrahedron,
// dodecahedron, icosahedron and octahedron. Every edge, from one corner to the other, with each
// solid's corners on a sphere of radius 1.
static const uint EdgeCount = 90;
static const float3 Edges[180] =
{
    // the cube
    float3(-0.5774, -0.5774, -0.5774), float3(-0.5774, -0.5774, 0.5774),
    float3(-0.5774, -0.5774, -0.5774), float3(-0.5774, 0.5774, -0.5774),
    float3(-0.5774, -0.5774, -0.5774), float3(0.5774, -0.5774, -0.5774),
    float3(-0.5774, -0.5774, 0.5774), float3(-0.5774, 0.5774, 0.5774),
    float3(-0.5774, -0.5774, 0.5774), float3(0.5774, -0.5774, 0.5774),
    float3(-0.5774, 0.5774, -0.5774), float3(-0.5774, 0.5774, 0.5774),
    float3(-0.5774, 0.5774, -0.5774), float3(0.5774, 0.5774, -0.5774),
    float3(-0.5774, 0.5774, 0.5774), float3(0.5774, 0.5774, 0.5774),
    float3(0.5774, -0.5774, -0.5774), float3(0.5774, -0.5774, 0.5774),
    float3(0.5774, -0.5774, -0.5774), float3(0.5774, 0.5774, -0.5774),
    float3(0.5774, -0.5774, 0.5774), float3(0.5774, 0.5774, 0.5774),
    float3(0.5774, 0.5774, -0.5774), float3(0.5774, 0.5774, 0.5774),
    // the tetrahedron
    float3(0.5774, 0.5774, 0.5774), float3(0.5774, -0.5774, -0.5774),
    float3(0.5774, 0.5774, 0.5774), float3(-0.5774, 0.5774, -0.5774),
    float3(0.5774, 0.5774, 0.5774), float3(-0.5774, -0.5774, 0.5774),
    float3(0.5774, -0.5774, -0.5774), float3(-0.5774, 0.5774, -0.5774),
    float3(0.5774, -0.5774, -0.5774), float3(-0.5774, -0.5774, 0.5774),
    float3(-0.5774, 0.5774, -0.5774), float3(-0.5774, -0.5774, 0.5774),
    // the dodecahedron
    float3(-0.5774, -0.5774, -0.5774), float3(0.0000, -0.3568, -0.9342),
    float3(-0.5774, -0.5774, -0.5774), float3(-0.3568, -0.9342, 0.0000),
    float3(-0.5774, -0.5774, -0.5774), float3(-0.9342, 0.0000, -0.3568),
    float3(-0.5774, -0.5774, 0.5774), float3(-0.3568, -0.9342, 0.0000),
    float3(-0.5774, -0.5774, 0.5774), float3(0.0000, -0.3568, 0.9342),
    float3(-0.5774, -0.5774, 0.5774), float3(-0.9342, 0.0000, 0.3568),
    float3(-0.5774, 0.5774, -0.5774), float3(-0.9342, 0.0000, -0.3568),
    float3(-0.5774, 0.5774, -0.5774), float3(-0.3568, 0.9342, 0.0000),
    float3(-0.5774, 0.5774, -0.5774), float3(0.0000, 0.3568, -0.9342),
    float3(-0.5774, 0.5774, 0.5774), float3(-0.3568, 0.9342, 0.0000),
    float3(-0.5774, 0.5774, 0.5774), float3(-0.9342, 0.0000, 0.3568),
    float3(-0.5774, 0.5774, 0.5774), float3(0.0000, 0.3568, 0.9342),
    float3(0.5774, -0.5774, -0.5774), float3(0.0000, -0.3568, -0.9342),
    float3(0.5774, -0.5774, -0.5774), float3(0.9342, 0.0000, -0.3568),
    float3(0.5774, -0.5774, -0.5774), float3(0.3568, -0.9342, 0.0000),
    float3(0.5774, -0.5774, 0.5774), float3(0.0000, -0.3568, 0.9342),
    float3(0.5774, -0.5774, 0.5774), float3(0.3568, -0.9342, 0.0000),
    float3(0.5774, -0.5774, 0.5774), float3(0.9342, 0.0000, 0.3568),
    float3(0.5774, 0.5774, -0.5774), float3(0.9342, 0.0000, -0.3568),
    float3(0.5774, 0.5774, -0.5774), float3(0.0000, 0.3568, -0.9342),
    float3(0.5774, 0.5774, -0.5774), float3(0.3568, 0.9342, 0.0000),
    float3(0.5774, 0.5774, 0.5774), float3(0.0000, 0.3568, 0.9342),
    float3(0.5774, 0.5774, 0.5774), float3(0.3568, 0.9342, 0.0000),
    float3(0.5774, 0.5774, 0.5774), float3(0.9342, 0.0000, 0.3568),
    float3(0.0000, -0.3568, -0.9342), float3(0.0000, 0.3568, -0.9342),
    float3(-0.3568, -0.9342, 0.0000), float3(0.3568, -0.9342, 0.0000),
    float3(-0.9342, 0.0000, -0.3568), float3(-0.9342, 0.0000, 0.3568),
    float3(0.0000, -0.3568, 0.9342), float3(0.0000, 0.3568, 0.9342),
    float3(-0.3568, 0.9342, 0.0000), float3(0.3568, 0.9342, 0.0000),
    float3(0.9342, 0.0000, -0.3568), float3(0.9342, 0.0000, 0.3568),
    // the icosahedron
    float3(0.0000, -0.5257, -0.8507), float3(-0.5257, -0.8507, 0.0000),
    float3(0.0000, -0.5257, -0.8507), float3(-0.8507, 0.0000, -0.5257),
    float3(0.0000, -0.5257, -0.8507), float3(0.8507, 0.0000, -0.5257),
    float3(0.0000, -0.5257, -0.8507), float3(0.0000, 0.5257, -0.8507),
    float3(0.0000, -0.5257, -0.8507), float3(0.5257, -0.8507, 0.0000),
    float3(-0.5257, -0.8507, 0.0000), float3(-0.8507, 0.0000, -0.5257),
    float3(-0.5257, -0.8507, 0.0000), float3(0.0000, -0.5257, 0.8507),
    float3(-0.5257, -0.8507, 0.0000), float3(0.5257, -0.8507, 0.0000),
    float3(-0.5257, -0.8507, 0.0000), float3(-0.8507, 0.0000, 0.5257),
    float3(-0.8507, 0.0000, -0.5257), float3(-0.5257, 0.8507, 0.0000),
    float3(-0.8507, 0.0000, -0.5257), float3(0.0000, 0.5257, -0.8507),
    float3(-0.8507, 0.0000, -0.5257), float3(-0.8507, 0.0000, 0.5257),
    float3(0.0000, -0.5257, 0.8507), float3(0.5257, -0.8507, 0.0000),
    float3(0.0000, -0.5257, 0.8507), float3(-0.8507, 0.0000, 0.5257),
    float3(0.0000, -0.5257, 0.8507), float3(0.0000, 0.5257, 0.8507),
    float3(0.0000, -0.5257, 0.8507), float3(0.8507, 0.0000, 0.5257),
    float3(-0.5257, 0.8507, 0.0000), float3(0.0000, 0.5257, -0.8507),
    float3(-0.5257, 0.8507, 0.0000), float3(-0.8507, 0.0000, 0.5257),
    float3(-0.5257, 0.8507, 0.0000), float3(0.0000, 0.5257, 0.8507),
    float3(-0.5257, 0.8507, 0.0000), float3(0.5257, 0.8507, 0.0000),
    float3(0.8507, 0.0000, -0.5257), float3(0.0000, 0.5257, -0.8507),
    float3(0.8507, 0.0000, -0.5257), float3(0.5257, -0.8507, 0.0000),
    float3(0.8507, 0.0000, -0.5257), float3(0.5257, 0.8507, 0.0000),
    float3(0.8507, 0.0000, -0.5257), float3(0.8507, 0.0000, 0.5257),
    float3(0.0000, 0.5257, -0.8507), float3(0.5257, 0.8507, 0.0000),
    float3(0.5257, -0.8507, 0.0000), float3(0.8507, 0.0000, 0.5257),
    float3(-0.8507, 0.0000, 0.5257), float3(0.0000, 0.5257, 0.8507),
    float3(0.0000, 0.5257, 0.8507), float3(0.5257, 0.8507, 0.0000),
    float3(0.0000, 0.5257, 0.8507), float3(0.8507, 0.0000, 0.5257),
    float3(0.5257, 0.8507, 0.0000), float3(0.8507, 0.0000, 0.5257),
    // the octahedron
    float3(1.0000, 0.0000, 0.0000), float3(0.0000, 1.0000, 0.0000),
    float3(1.0000, 0.0000, 0.0000), float3(0.0000, -1.0000, 0.0000),
    float3(1.0000, 0.0000, 0.0000), float3(0.0000, 0.0000, 1.0000),
    float3(1.0000, 0.0000, 0.0000), float3(0.0000, 0.0000, -1.0000),
    float3(-1.0000, 0.0000, 0.0000), float3(0.0000, 1.0000, 0.0000),
    float3(-1.0000, 0.0000, 0.0000), float3(0.0000, -1.0000, 0.0000),
    float3(-1.0000, 0.0000, 0.0000), float3(0.0000, 0.0000, 1.0000),
    float3(-1.0000, 0.0000, 0.0000), float3(0.0000, 0.0000, -1.0000),
    float3(0.0000, 1.0000, 0.0000), float3(0.0000, 0.0000, 1.0000),
    float3(0.0000, 1.0000, 0.0000), float3(0.0000, 0.0000, -1.0000),
    float3(0.0000, -1.0000, 0.0000), float3(0.0000, 0.0000, 1.0000),
    float3(0.0000, -1.0000, 0.0000), float3(0.0000, 0.0000, -1.0000)
};

// Which solid an edge belongs to, 0 (the outer cube) to 4 (the inner octahedron).
int solidOf(uint edge)
{
    return edge < 12u ? 0 : edge < 18u ? 1 : edge < 48u ? 2 : edge < 78u ? 3 : 4;
}

// Each solid's size. Kepler fitted each one exactly inside the next; these are spaced more
// evenly so the inner ones aren't lost in the middle.
float radiusOf(int solid)
{
    return solid == 0 ? 1.0 : solid == 1 ? 0.84 : solid == 2 ? 0.64 : solid == 3 ? 0.48 : 0.32;
}

// How far a solid has melted into its sphere: 0 crystal, 1 a glowing shell. Each takes its turn,
// so there's always something folding or unfolding.
float meltOf(int solid)
{
    float wave = sin(Time * 0.13 - solid * 1.2566) * 0.5 + 0.5;
    return smoothstep(0.6, 0.95, wave);
}

// Each solid turns about its own tilted axis at its own pace, alternating direction.
float3 turn(float3 v, int solid)
{
    float direction = (solid & 1) == 0 ? 1.0 : -1.0;
    v.xz = rotate(v.xz, Time * (0.11 + solid * 0.035) * direction + solid * 0.7);
    v.yz = rotate(v.yz, 0.5 + solid * 0.45 + sin(Time * 0.05 + solid) * 0.3);
    v.xy = rotate(v.xy, solid * 0.9);
    return v;
}

// Where a particle is. flow says how far the light has streamed along the edges so far: particles
// run from one corner to the other and start again, faster when the music is loud.
float3 placeOf(uint id, float flow, out int solid, out float along)
{
    uint edge = id % EdgeCount;
    solid = solidOf(edge);
    along = frac(fixedRandom(id, 2.0) + flow);
    float3 start = Edges[edge * 2u];
    float3 end = Edges[edge * 2u + 1u];
    float3 spot = lerp(start, end, along);

    // A slight thickness, so each edge is a beam of light rather than a hairline.
    spot += (float3(fixedRandom(id, 3.0), fixedRandom(id, 4.0), fixedRandom(id, 5.0)) - 0.5) * 0.012;

    // Melting: the edges bow out onto the sphere around the solid as great circles, then spread
    // into a shell of dust.
    float melt = meltOf(solid);
    float3 scatter = normalize(float3(fixedRandom(id, 6.0), fixedRandom(id, 7.0), fixedRandom(id, 8.0)) - 0.5);
    float3 onSphere = normalize(lerp(spot, scatter, melt * melt * 0.75));
    spot = lerp(spot, onSphere, melt);

    // Each solid breathes with its own band; the bass also pushes them all outward on a beat.
    float level = spectrum(0.04 + solid * 0.17);
    float size = radiusOf(solid) * (1.0 + level * 0.12 + Beat * 0.04 * (5 - solid) / 5.0);
    return turn(spot, solid) * size;
}

void Spawn(inout Particle particle, uint id)
{
    int solid;
    float along;
    particle.position = placeOf(id, 0.0, solid, along);
    particle.velocity = 0.0;
    particle.life = 1e6;
    particle.data = 0.0;
}

void Simulate(inout Particle particle, uint id)
{
    int solid;
    float along;
    particle.data.y = frac(particle.data.y + DeltaTime * (0.025 + Level * 0.06 + Beat * 0.04));
    float3 target = placeOf(id, particle.data.y, solid, along);
    // Ease towards the target so beats and melting move smoothly.
    float3 eased = lerp(particle.position, target, 1.0 - exp(-DeltaTime * 12.0));
    particle.velocity = (eased - particle.position) / max(DeltaTime, 1e-4);
    particle.position = eased;
    particle.data.x = along;
}

float3 cameraEye()
{
    float angle = Time * 0.045;
    return float3(sin(angle) * 3.3, -0.6 + sin(Time * 0.031) * 0.8, cos(angle) * 3.3);
}

Sprite Show(Particle particle, uint id)
{
    float3 eye = cameraEye();
    float3 seen = project(particle.position, eye, 0.0, 2.7 + Bass * 0.08, sin(Time * 0.04) * 0.2);

    uint edge = id % EdgeCount;
    int solid = solidOf(edge);
    float along = particle.data.x;
    float level = spectrum(0.04 + solid * 0.17);

    // Shorter edges have the same number of particles packed closer, so they're dimmed to match.
    float edgeLength = length(Edges[edge * 2u] - Edges[edge * 2u + 1u]) * radiusOf(solid);
    float density = edgeLength / 0.75;

    // Bright corners, and pulses of light running along every edge.
    float corner = exp(-min(along, 1.0 - along) * 30.0) * 2.5;
    float pulse = pow(saturate(cos((along * 3.0 - Time * 0.9 - solid) * 2.0 * PI) * 0.5 + 0.5), 16.0) * (0.6 + Mid * 2.0);

    float hue = (solid == 0 ? 0.09 : solid == 1 ? 0.98 : solid == 2 ? 0.42 : solid == 3 ? 0.6 : 0.78) + Time * 0.008;
    float melt = meltOf(solid);
    float3 colour = hsv2rgb(hue, 0.8 - pulse * 0.2, 1.0);
    float brightness = 0.08 * density * (0.45 + level * 1.4) * (1.0 + corner * (1.0 - melt) + pulse) * (1.0 + Beat * 0.5);

    // A few catch the treble and sparkle.
    brightness *= 1.0 + step(0.995, fixedRandom(id, 9.0)) * Treble * 10.0;

    Sprite sprite = (Sprite)0;
    sprite.position = seen.xy;
    sprite.size = seen.z > 0.1 ? 0.9 : 0.0;
    sprite.colour = colour * brightness;
    focus(sprite, seen.z, length(eye), 0.06);
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    float heart = exp(-dot(p, p) * 7.0) * (0.02 + Bass * 0.05);
    return particles(uv) + previous(uv) * 0.6 + float3(0.6, 0.4, 1.0) * heart;
}
