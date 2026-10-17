// Name: Barnsley Fern
// A little glade of ferns grows out of the dark, each frond unfurling from a curled tip and then swaying in a breeze that stirs with the music, every leaflet a perfect small copy of the whole.
// Family: Particles
// Tags: Calm
// Particles: 393216

// Barnsley's fern is drawn by the chaos game: four simple stretch-and-turn rules, one picked at
// random each step. One rule draws the stem, one builds each frond from a smaller copy of itself,
// and two throw out the first leaflets left and right. Repeat forever and the whole fern appears.
static const int Ferns = 6;
static const float Cycle = 48.0;   // seconds in a fern's life, from unfurling to fading

// Where each fern grows, how big it is, which way it leans, and how far back it stands (0 = front).
void fernPlacement(int index, out float2 root, out float size, out float lean, out float depth, out float mirror)
{
    float xs[6] = { -0.95, 0.55, -0.15, 1.25, -1.55, 0.2 };
    float sizes[6] = { 0.17, 0.165, 0.13, 0.12, 0.115, 0.1 };
    float leans[6] = { 0.32, -0.28, 0.06, -0.45, 0.5, -0.12 };
    float depths[6] = { 0.0, 0.05, 0.45, 0.6, 0.7, 0.9 };
    root = float2(xs[index] * min(Aspect / 1.78, 1.2), 1.04 - depths[index] * 0.12);
    size = sizes[index];
    lean = leans[index];
    depth = depths[index];
    mirror = (index & 1) == 0 ? 1.0 : -1.0;
}

// How far through its life each fern is: 0 just sprouted, 1 about to begin again.
float lifeOf(int index)
{
    return frac(Time / Cycle + index * 0.173 + 0.35);
}

// How far it has unfurled (0..1), and how visible it is.
float unfurledOf(float life)
{
    return smoothstep(0.0, 0.3, life);
}

float presenceOf(float life)
{
    return smoothstep(0.0, 0.04, life) * smoothstep(1.0, 0.9, life);
}

int fernOf(uint id)
{
    // Front ferns get more points, so they stay just as finely drawn.
    float u = fixedRandom(id, 1.0);
    return min((int)(u * u * 0.9 * Ferns + u * 0.1 * Ferns), Ferns - 1);
}

void Spawn(inout Particle particle, uint id)
{
    particle.position = float3(random(id, 2.0) * 2.0 - 1.0, random(id, 3.0) * 9.0, 0.0);
    particle.velocity = 0.0;
    particle.data = float4(0.0, 0.0, 0.0, 0.0);
    particle.life = 1e6;
}

void Simulate(inout Particle particle, uint id)
{
    // One step of the chaos game, in the fern's own coordinates (about 5 wide and 10 tall).
    float2 f = particle.position.xy;
    float roll = random(id, 4.0);
    float rule = 0.0;
    if (roll < 0.01)
    {
        f = float2(0.0, 0.16 * f.y);                                               // the stem
        rule = 0.0;
    }
    else if (roll < 0.86)
    {
        f = float2(0.85 * f.x + 0.04 * f.y, -0.04 * f.x + 0.85 * f.y + 1.6);       // the fern, a little smaller, a little higher
        rule = 1.0;
    }
    else if (roll < 0.93)
    {
        f = float2(0.2 * f.x - 0.26 * f.y, 0.23 * f.x + 0.22 * f.y + 1.6);        // the bottom-left leaflet
        rule = 2.0;
    }
    else
    {
        f = float2(-0.15 * f.x + 0.28 * f.y, 0.26 * f.x + 0.24 * f.y + 0.44);     // the bottom-right leaflet
        rule = 3.0;
    }
    particle.position.xy = f;

    // Remember how deep in the leaflets a point is: leaflet rules push it towards the tips, the
    // main rule eases it back, so the outer leaflets of each frond come out lighter.
    particle.data.x = rule >= 2.0 ? lerp(particle.data.x, 1.0, 0.6) : particle.data.x * 0.8;
    particle.data.y = rule;
}

// The breeze: slow gusts that build with the mids and ripple along the glade.
float breezeAt(float x, float depth)
{
    float gust = sin(Time * 0.45 + x * 0.8) * 0.6 + sin(Time * 0.27 - x * 0.5 + 1.3) * 0.4;
    return gust * (0.05 + Mid * 0.12 + Level * 0.03) * (1.0 - depth * 0.4);
}

Sprite Show(Particle particle, uint id)
{
    int index = fernOf(id);
    float2 root;
    float size;
    float lean;
    float depth;
    float mirror;
    fernPlacement(index, root, size, lean, depth, mirror);
    float life = lifeOf(index);
    float unfurled = unfurledOf(life);
    float presence = presenceOf(life);

    float2 f = particle.position.xy;
    float height = saturate(f.y / 10.0);

    // A young frond is still curled at the tip like a fiddlehead; the curl opens as it grows,
    // and the whole fern bends further the higher you go, swaying in the breeze.
    float curl = (1.0 - unfurled) * 6.5 * height * height * height;
    float bend = lean * height + breezeAt(root.x, depth) * height * height * 1.6 + curl * mirror;
    float2 local = float2(f.x * mirror, -f.y) * size * lerp(0.35, 1.0, unfurled);
    // Leaflets flutter a little on their own as the treble picks up.
    local.x += sin(Time * 3.1 + f.y * 4.0 + index) * 0.004 * height * (0.3 + Treble * 2.0);

    Sprite sprite = (Sprite)0;
    sprite.position = root + rotate(local, bend * mirror);
    sprite.size = 0.8 + depth * 0.9;

    // Deep green at the stem to fresh yellow-green at the tips, and the unfurling crown a
    // pale gold. Ferns further back are hazier and bluer.
    float tip = particle.data.x;
    float3 colour = lerp(float3(0.05, 0.32, 0.12), float3(0.45, 0.9, 0.25), saturate(height * 0.8 + tip * 0.45));
    colour = lerp(colour, float3(1.0, 0.82, 0.4), (1.0 - unfurled) * height * 0.8);
    colour = lerp(colour, float3(0.25, 0.5, 0.55), depth * 0.45);

    // Each beat sends a soft wave of light up through the fronds.
    float rise = exp(-abs(height - (1.0 - Beat)) * 9.0) * Beat * 0.6;
    float level = spectrum(0.05 + height * 0.5);
    colour *= 0.09 * (0.75 + level * 0.6 + rise) * (1.0 - depth * 0.45) * presence;

    // Dewdrops: a few points catch the light with the treble.
    colour += step(0.9985, fixedRandom(id, 5.0)) * float3(0.8, 1.0, 0.9) * Treble * 0.35 * presence;

    sprite.colour = colour * saturate(particle.age * 3.0 - 0.2);
    focus(sprite, 1.0 + depth * 1.5, 1.0, 0.012);
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    // A dim glade: dark teal above fading to a deep green floor, with a soft shaft of light.
    float3 ground = lerp(float3(0.006, 0.02, 0.022), float3(0.004, 0.012, 0.006), saturate(p.y * 0.5 + 0.5));
    float shaft = glow(abs(p.x - 0.35 + p.y * 0.4 + sin(Time * 0.05) * 0.2), 3.0) * saturate(0.6 - p.y * 0.4);
    ground += float3(0.03, 0.04, 0.025) * shaft * (0.6 + Level * 0.4);

    // Out-of-focus specks of pollen drifting through the shaft.
    float2 cell = p * 5.0 + float2(Time * 0.05, -Time * 0.08);
    float2 id = floor(cell);
    float2 centre = float2(hash(id), hash(id + 11.3)) * 0.6 + 0.2;
    float speck = smoothstep(0.12, 0.08, length(frac(cell) - centre)) * step(0.85, hash(id + 4.7));
    ground += float3(0.06, 0.07, 0.04) * speck * shaft * (0.5 + Treble);

    return ground + particles(uv) + previous(uv) * 0.55;
}
