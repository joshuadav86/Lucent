// Name: Reef Aquarium
// A glowing reef tank: a school of neon tetras darts on the beat, clownfish play in anemones that wave with the spectrum, tangs glide past swaying kelp and bubbles stream up through rippling light.
// Family: Particles
// Tags: Calm, Colourful, Water
// Particles: 131072

// How the particles are shared out between the parts of the tank. Each range is [start, end).
static const float TetraEnd = 0.24;      // a school of small neon tetras
static const float ClownEnd = 0.38;      // clownfish
static const float BlueTangEnd = 0.52;   // blue tangs
static const float YellowTangEnd = 0.62; // yellow tangs
static const float KelpEnd = 0.83;       // kelp swaying from the sand
static const float AnemoneEnd = 0.95;    // two anemones
static const float BubbleEnd = 0.955;     // a bubbler's stream
// The rest: specks drifting in the water.

static const int Tetras = 36;
static const int Clowns = 4;
static const int BlueTangs = 3;
static const int YellowTangs = 3;
static const int KelpStrands = 18;
static const int AnemoneTentacles = 56;
static const uint BubbleRing = 16;
// Where the sand starts (screen y grows downward).
static const float Sand = 0.78;

float shareOf(uint id)
{
    return (id + 0.5) / ParticleCount;
}

// Which item (fish, strand, ...) of a range a particle belongs to, and where it falls within it (0..1).
void itemOf(float share, float start, float end, int count, out int index, out float within)
{
    float local = saturate((share - start) / (end - start)) * count;
    index = min((int)local, count - 1);
    within = saturate(local - index);
}

// Two evenly spread numbers for this particle, so shapes fill smoothly rather than in clumps.
float2 evenPair(uint id)
{
    return float2((id * 3242174889u) / 4294967296.0, (id * 2447445414u) / 4294967296.0);
}

float3 anemoneBase(int index)
{
    return index == 0 ? float3(-0.55 * Aspect, Sand + 0.03, 0.0) : float3(0.42 * Aspect, Sand + 0.06, 0.0);
}

// ---- Fish ----

// A fish's place, its velocity, its length, its depth (0 near, 1 far back) and how tall it is for its length.
void fishPath(int species, int index, float clock, out float2 position, out float2 velocity, out float bodyLength, out float depth, out float tall)
{
    float salt = index * 1.731 + species * 4.17;
    float phase = frac(salt * 3.13) * 2.0 * PI;
    if (species == 0)
    {
        // The school swims together around the tank; each tetra holds its own place in it.
        float t = clock * 0.16;
        float2 centre = float2(sin(t) * Aspect * 0.55, sin(t * 1.7 + 1.0) * 0.18 - 0.2);
        float2 heading = float2(cos(t) * Aspect * 0.55, cos(t * 1.7 + 1.0) * 0.18 * 1.7) * 0.16;
        float2 place = float2(frac(salt * 7.7) - 0.5, frac(salt * 5.3) - 0.5) * float2(0.55, 0.3);
        place += float2(sin(clock * 0.9 + salt * 3.0), cos(clock * 0.7 + salt * 2.0)) * 0.025;
        position = centre + place;
        velocity = heading;
        bodyLength = 0.075;
        depth = frac(salt * 11.3) * 0.6;
        tall = 0.3;
        return;
    }
    if (species == 1)
    {
        // Clownfish never stray far from their anemone.
        float3 home = anemoneBase(index & 1);
        float t = clock * (0.35 + frac(salt * 9.7) * 0.15);
        float2 reach = float2(0.22, 0.07);
        position = home.xy + float2(sin(t + phase) * reach.x, -0.26 + sin(t * 1.9 + phase) * reach.y);
        velocity = float2(cos(t + phase) * reach.x, cos(t * 1.9 + phase) * reach.y * 1.9) * (0.35 + frac(salt * 9.7) * 0.15);
        bodyLength = 0.15;
        depth = 0.1 + frac(salt * 2.9) * 0.3;
        tall = 0.42;
        return;
    }

    // Tangs glide the length of the tank and back, at their own depths.
    float t = clock * (0.07 + frac(salt * 6.1) * 0.04);
    float rangeX = Aspect * (0.85 + frac(salt * 2.3) * 0.25);
    float level = -0.55 + frac(salt * 4.9) * 0.7;
    position = float2(sin(t + phase) * rangeX, level + sin(t * 2.3 + phase) * 0.12);
    velocity = float2(cos(t + phase) * rangeX, cos(t * 2.3 + phase) * 0.12 * 2.3) * (0.07 + frac(salt * 6.1) * 0.04);
    bodyLength = species == 2 ? 0.27 : 0.23;
    depth = frac(salt * 8.3) * 0.8;
    tall = species == 2 ? 0.5 : 0.75;
}

// A fish's half-height at u along its body (u: -1.45 tail tip, -1 tail root, 1 nose), for a fish of height 1.
float fishHalfHeight(float u, float tall)
{
    if (u < -0.9)
    {
        // The tail fin flares out from a narrow root.
        return 0.12 + (-0.9 - u) * 1.3 * (0.35 + tall * 0.6);
    }
    float body = sqrt(saturate(1.0 - u * u)) * (1.0 - 0.18 * u);
    return max(body, 0.12) * tall;
}

// The colours of each kind of fish, from where a point is on its body (v: -1 belly to 1 back).
float3 fishColour(int species, float u, float v, float tall)
{
    // Every fish has a dark eye with a glint.
    float2 eye = float2((u - 0.62) * 1.0, v - 0.18);
    float eyeDistance = length(eye * float2(1.0, tall * 1.6));
    if (eyeDistance < 0.08)
    {
        return eyeDistance < 0.03 ? float3(0.9, 0.9, 0.9) : float3(0.01, 0.01, 0.02);
    }

    if (species == 0)
    {
        // Neon tetra: an electric blue stripe, a red rear belly, silvery elsewhere.
        if (v > 0.0 && v < 0.42 && u > -0.85)
        {
            return float3(0.1, 0.75, 1.3);
        }
        if (v < 0.0 && u < 0.15 && u > -0.95)
        {
            return float3(1.2, 0.08, 0.1);
        }
        return float3(0.35, 0.4, 0.45) * (u < -0.9 ? 0.4 : 1.0);
    }
    if (species == 1)
    {
        // Clownfish: orange with three white bands edged in black.
        float band = min(min(abs(u - 0.45), abs(u + 0.15)), abs(u + 0.85));
        if (band < 0.09)
        {
            return float3(1.1, 1.1, 1.05);
        }
        if (band < 0.13 || abs(v) > 0.92)
        {
            return float3(0.02, 0.01, 0.0);
        }
        return float3(1.3, 0.42, 0.04);
    }
    if (species == 2)
    {
        // Blue tang: royal blue, a black swoosh, and a yellow tail.
        if (u < -0.9)
        {
            return float3(1.3, 1.0, 0.1);
        }
        float swoosh = abs(v - 0.15 - 0.35 * sin(u * 2.2 + 0.4));
        if (swoosh < 0.18 && u > -0.75 && u < 0.6)
        {
            return float3(0.02, 0.02, 0.08);
        }
        return float3(0.08, 0.3, 1.4);
    }
    // Yellow tang: bright yellow all over, with a pale spine by the tail.
    if (u < -0.82 && u > -0.95 && abs(v) < 0.18)
    {
        return float3(1.2, 1.2, 1.1);
    }
    return float3(1.4, 1.1, 0.08);
}

Sprite showFish(int species, int index, float within, uint id, float clock)
{
    float2 position;
    float2 velocity;
    float bodyLength;
    float depth;
    float tall;
    fishPath(species, index, clock, position, velocity, bodyLength, depth, tall);

    // A point on the body: along it (u) and across it (v).
    float2 pick = evenPair(id);
    float u = lerp(-1.45, 1.0, pick.x);
    float v = pick.y * 2.0 - 1.0;
    float halfHeight = fishHalfHeight(u, tall);
    float3 colour = fishColour(species, u, v, tall);

    // Swimming: a wave runs down the body, strongest at the tail and quicker for small fish.
    float beatSpeed = species == 0 ? 14.0 : 7.0;
    float wag = sin(u * 2.4 - Time * beatSpeed - index) * 0.09 * saturate((1.0 - u) * 0.5);

    // Facing the way it swims; while turning around it is seen nose-on, so it looks narrow.
    float speed = length(velocity);
    float facing = velocity.x / max(sqrt(velocity.x * velocity.x + speed * speed * 0.04), 1e-4);
    float tilt = clamp(velocity.y / max(abs(velocity.x) + 0.05, 1e-4), -0.6, 0.6);
    float scale = bodyLength * 0.5 * (1.0 - depth * 0.35);
    float2 offset = float2(u * facing, v * halfHeight + wag) * scale;
    offset = rotate(offset, tilt * sign(facing + 1e-4) * 0.6);

    // Further fish are smaller, dimmer and bluer through the water.
    colour = lerp(colour, float3(0.05, 0.25, 0.4), depth * 0.45) * (1.0 - depth * 0.3);
    // Fill the shape evenly: thin parts get fewer points, so they get brighter ones.
    float sharePerFish = species == 0 ? TetraEnd / Tetras : species == 1 ? (ClownEnd - TetraEnd) / Clowns
                       : species == 2 ? (BlueTangEnd - ClownEnd) / BlueTangs : (YellowTangEnd - BlueTangEnd) / YellowTangs;
    float weight = halfHeight * scale * scale / sharePerFish * 1.1;
    // Lit from above: a sheen along the back, shadow under the belly.
    colour *= 0.6 + 0.5 * saturate(v * 0.5 + 0.6) + 0.35 * exp(-pow((v - 0.45) * 4.0, 2.0));

    Sprite sprite = (Sprite)0;
    sprite.position = position + offset;
    // Bigger dots on bigger fish, so their bodies look smooth (the same light either way).
    sprite.size = species == 0 ? 1.6 : 2.6;
    sprite.colour = colour * weight * (1.0 + Beat * 0.15) * pow(1.6 / sprite.size, 2.0);
    return sprite;
}

// ---- The rest of the tank ----

Sprite showKelp(int index, float within, uint id)
{
    float salt = index * 2.31 + 0.7;
    float depth = frac(salt * 3.9);
    float baseX = (frac(salt * 7.13) * 2.0 - 1.0) * Aspect * 1.05;
    float height = (0.55 + frac(salt * 5.7) * 0.75) * (1.0 - depth * 0.3);
    float s = within;

    // Each strand sways in the current, more towards its tip; the mids stir it harder.
    float sway = sin(Time * 0.6 + salt * 3.0 + s * 2.2) * (0.07 + Mid * 0.05) * s + sin(Time * 1.7 + salt + s * 6.0) * 0.012 * s;
    float2 spine = float2(baseX + sway, Sand + 0.04 - s * height);
    // Ruffled blades on either side of the stalk.
    float blade = (0.012 + 0.02 * saturate(sin(s * 23.0 + salt) * 0.6 + 0.6)) * (1.0 - s * 0.4);
    float across = fixedRandom(id, 1.0) * 2.0 - 1.0;
    float2 position = spine + float2(across * blade, 0.0);

    float3 colour = lerp(float3(0.08, 0.35, 0.05), float3(0.45, 0.6, 0.1), s);
    colour = lerp(colour, float3(0.03, 0.15, 0.2), depth * 0.6);
    float strands = (KelpEnd - YellowTangEnd) / KelpStrands;
    Sprite sprite = (Sprite)0;
    sprite.position = position;
    sprite.size = 3.0;
    sprite.colour = colour * height * blade / strands * 0.05;
    return sprite;
}

Sprite showAnemone(float share, uint id)
{
    float perAnemone = (AnemoneEnd - KelpEnd) * 0.5;
    int anemone = share < KelpEnd + perAnemone ? 0 : 1;
    int tentacle;
    float s;
    itemOf(share, KelpEnd + anemone * perAnemone, KelpEnd + (anemone + 1) * perAnemone, AnemoneTentacles, tentacle, s);
    float3 base = anemoneBase(anemone);

    // Each tentacle is one slice of the spectrum, so the anemone ripples with the music.
    float fan = (tentacle + 0.5) / AnemoneTentacles;
    float level = spectrum(abs(fan - 0.5) * 1.6 + 0.04);
    float reach = 0.11 + level * 0.18 + frac(fan * 37.0) * 0.04;
    float angle = (fan - 0.5) * 2.6 + sin(Time * 0.9 + fan * 9.0) * 0.12;
    float curl = sin(Time * 1.3 + fan * 13.0 + anemone) * 0.8;
    float2 direction = rotate(float2(0.0, -1.0), angle + curl * s * 0.6);
    float2 root = base.xy + float2((fan - 0.5) * 0.2, 0.0);
    float2 position = root + direction * s * reach + (fixedRandom(id, 2.0) - 0.5) * 0.006;

    float3 colour = anemone == 0 ? float3(0.9, 0.25, 0.7) : float3(0.3, 0.8, 0.5);
    colour = lerp(colour * 0.5, float3(1.0, 0.85, 1.0), smoothstep(0.75, 1.0, s));
    Sprite sprite = (Sprite)0;
    sprite.position = position;
    sprite.size = 1.4 + smoothstep(0.85, 1.0, s) * 1.2;
    sprite.colour = colour * reach * 0.5 * (1.0 + Beat * 0.4);
    return sprite;
}

Sprite showBubble(float share, uint id)
{
    // Each bubble is a ring of dots.
    uint first = (uint)ceil(AnemoneEnd * ParticleCount - 0.5);
    uint local = id - min(first, id);
    int bubble = (int)(local / BubbleRing);
    float salt = bubble * 0.618;
    uint spot = local % BubbleRing;
    int bubbles = max((int)((BubbleEnd - AnemoneEnd) * ParticleCount / BubbleRing), 1);

    // Bubbles stream up from a stone on the sand, wobbling and growing as they rise.
    float journey = frac(Time * (0.12 + frac(salt * 5.1) * 0.06) + frac(bubble * 0.618));
    float2 stone = float2(0.78 * Aspect, Sand + 0.05);
    float2 centre = stone + float2(sin(journey * 18.0 + salt * 6.0) * 0.02 + (frac(salt) - 0.5) * 0.05, -journey * (Sand + 1.1));
    float radius = (0.012 + frac(salt * 3.3) * 0.02) * (1.0 + journey * 0.6) * (1.0 + Bass * 0.25);
    float angle = spot * (2.0 * PI / BubbleRing);
    // A ring with a bright glint at its top left.
    float glint = spot == 5u ? 4.0 : 1.0;
    Sprite sprite = (Sprite)0;
    sprite.position = centre + float2(cos(angle), sin(angle)) * radius;
    sprite.size = 1.3;
    sprite.colour = float3(0.7, 0.9, 1.0) * 0.3 * glint * saturate(journey * 10.0);
    return sprite;
}

void Spawn(inout Particle particle, uint id)
{
    particle.life = 1e6;
    particle.velocity = 0.0;
    particle.data = float4(Time, 0.0, 0.0, 0.0);
    particle.position = float3((random(id, 3.0) * 2.0 - 1.0) * Aspect, random(id, 4.0) * 1.8 - 1.0, 0.0);
}

void Simulate(inout Particle particle, uint id)
{
    // Every particle keeps the same swimming clock, which runs faster when the music is loud and
    // leaps on each beat, so the fish all dart together.
    float dt = min(DeltaTime, 0.05);
    particle.data.x += dt * (0.7 + Level * 0.5 + Beat * 1.2);

    if (shareOf(id) >= BubbleEnd)
    {
        // Specks drift on the slow swirl of the water.
        float2 flow = curlNoise2(particle.position.xy * 1.5, Time * 0.05) * 0.02 + float2(0.0, -0.004);
        particle.position.xy += flow * dt;
        particle.position.x = wrap(particle.position.x + Aspect, 2.0 * Aspect) - Aspect;
        particle.position.y = wrap(particle.position.y + 1.0, 1.8) - 1.0;
    }
}

Sprite Show(Particle particle, uint id)
{
    float share = shareOf(id);
    float clock = particle.data.x;
    int index;
    float within;
    if (share < TetraEnd)
    {
        itemOf(share, 0.0, TetraEnd, Tetras, index, within);
        return showFish(0, index, within, id, clock);
    }
    if (share < ClownEnd)
    {
        itemOf(share, TetraEnd, ClownEnd, Clowns, index, within);
        return showFish(1, index, within, id, clock);
    }
    if (share < BlueTangEnd)
    {
        itemOf(share, ClownEnd, BlueTangEnd, BlueTangs, index, within);
        return showFish(2, index, within, id, clock);
    }
    if (share < YellowTangEnd)
    {
        itemOf(share, BlueTangEnd, YellowTangEnd, YellowTangs, index, within);
        return showFish(3, index, within, id, clock);
    }
    if (share < KelpEnd)
    {
        itemOf(share, YellowTangEnd, KelpEnd, KelpStrands, index, within);
        return showKelp(index, within, id);
    }
    if (share < AnemoneEnd)
    {
        return showAnemone(share, id);
    }
    if (share < BubbleEnd)
    {
        return showBubble(share, id);
    }

    Sprite sprite = (Sprite)0;
    sprite.position = particle.position.xy;
    sprite.size = 0.8 + fixedRandom(id, 5.0);
    sprite.colour = float3(0.6, 0.8, 0.9) * 0.08 * (1.0 + Treble * 1.5);
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    // The water: lit from above, deepening towards the sand.
    float height = saturate(p.y * 0.5 + 0.5);
    float3 water = lerp(float3(0.03, 0.2, 0.28), float3(0.0, 0.04, 0.1), height);

    // Shafts of light from the surface, slowly shifting.
    float slant = p.x + p.y * 0.35;
    float rays = noise(float2(slant * 3.0, Time * 0.15)) * noise(float2(slant * 7.0 + 3.0, Time * 0.1));
    water += float3(0.15, 0.3, 0.3) * rays * rays * saturate(1.0 - height * 1.3) * (0.6 + Treble * 0.6);

    // The sand, with dancing caustic light.
    float sandLine = Sand + 0.03 * sin(p.x * 2.1) + 0.015 * sin(p.x * 5.3);
    if (p.y > sandLine)
    {
        float2 q = p * float2(5.0, 9.0);
        float caustic = abs(noise(q + float2(Time * 0.3, 0.0)) - noise(q * 1.3 - float2(0.0, Time * 0.25)));
        caustic = pow(saturate(1.0 - caustic * 3.0), 6.0);
        float3 sand = float3(0.32, 0.27, 0.18) * (0.55 + 0.2 * noise(p * 60.0)) * (1.2 - height * 0.5);
        water = lerp(water, sand + float3(0.4, 0.45, 0.35) * caustic * (0.2 + Treble * 0.3), smoothstep(0.0, 0.02, p.y - sandLine));
    }

    // Fish, plants and bubbles cover the water behind them.
    float3 layer = particles(uv);
    float cover = saturate(max(layer.r, max(layer.g, layer.b)) * 2.5);
    return water * (1.0 - cover) + layer;
}
