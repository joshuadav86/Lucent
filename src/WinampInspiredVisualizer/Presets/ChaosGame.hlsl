// Name: Chaos Game
// Hundreds of thousands of points hop halfway to random corners of a slowly turning polygon, and out of pure chance a perfect fractal appears: triangles within triangles, pentagons within pentagons, a carpet of squares. Every few bars the rules change and the picture snaps into a new pattern, breathing with each beat.
// Family: Particles
// Tags: Geometric
// Particles: 393216

// The chaos game: start anywhere, pick a corner at random, jump part of the way towards it, repeat.
// The points can never settle, yet together they always draw the same fractal. Which fractal
// depends on the shape, how far each jump goes, and which corners are allowed after the last one.
static const float Shapes = 8.0;
static const float Radius = 0.9;

// One set of rules: how many corners, how far to jump towards the chosen one (0..1), which corner
// (counted on from the last one) is forbidden (-1 = none), and whether the edge midpoints count
// as corners too.
void rulesFor(float index, out float corners, out float jump, out float forbidden, out float midpoints)
{
    float k = wrap(index, Shapes);
    corners = 3.0; jump = 0.5; forbidden = -1.0; midpoints = 0.0;
    if (k < 0.5) { corners = 3.0; jump = 0.5; forbidden = -1.0; }                  // Sierpinski triangle
    else if (k < 1.5) { corners = 5.0; jump = 0.618; forbidden = -1.0; }           // Sierpinski pentagon
    else if (k < 2.5) { corners = 4.0; jump = 0.5; forbidden = 0.0; }              // never the same corner twice
    else if (k < 3.5) { corners = 4.0; jump = 0.6667; midpoints = 1.0; }           // Sierpinski carpet
    else if (k < 4.5) { corners = 6.0; jump = 0.6667; forbidden = -1.0; }          // hexagonal snowflake
    else if (k < 5.5) { corners = 4.0; jump = 0.5; forbidden = 1.0; }              // never the next corner round
    else if (k < 6.5) { corners = 5.0; jump = 0.5; forbidden = 0.0; }              // pentagonal lace
    else { corners = 3.0; jump = 0.5; forbidden = -1.0; midpoints = 1.0; }         // triangle with midpoints
}

// The rule set in play: a new one every four bars (or every half minute in a quiet stretch).
float rulesIndex()
{
    return floor(max(BeatCount / 16.0, Time / 30.0));
}

float turnAngle()
{
    return Time * 0.04 + sin(Time * 0.13) * 0.15;
}

// Corner (or midpoint) i of the current shape, in p units. Point up, turning slowly.
float2 cornerAt(float i, float corners, float midpoints)
{
    float spin = turnAngle();
    if (midpoints > 0.5)
    {
        float c = floor(i * 0.5);
        float2 a = rotate(float2(0.0, -Radius), spin + c * 2.0 * PI / corners);
        float2 b = rotate(float2(0.0, -Radius), spin + (c + 1.0) * 2.0 * PI / corners);
        return frac(i * 0.5) > 0.25 ? (a + b) * 0.5 : a;
    }
    return rotate(float2(0.0, -Radius), spin + i * 2.0 * PI / corners);
}

// Each corner has its own colour, from a calm triad that drifts slowly round the colour wheel.
float3 cornerColour(float i, float count)
{
    float hue = Time * 0.01 + i / count * 0.62;
    return hsv2rgb(hue, 0.72, 1.0);
}

void Spawn(inout Particle particle, uint id)
{
    particle.position = float3((random(id, 1.0) - 0.5) * 2.0, (random(id, 2.0) - 0.5) * 2.0, 0.0);
    particle.velocity = 1.0;
    particle.data = float4(floor(random(id, 3.0) * 3.0), 0.0, 0.0, 0.0);
    particle.life = 1e6;
}

void Simulate(inout Particle particle, uint id)
{
    float corners, jump, forbidden, midpoints;
    rulesFor(rulesIndex(), corners, jump, forbidden, midpoints);
    float count = midpoints > 0.5 ? corners * 2.0 : corners;

    // Choose the next corner from the allowed ones, counted on from the last.
    float last = wrap(particle.data.x, count);
    float pick;
    if (forbidden < 0.0)
    {
        pick = floor(random(id, 4.0) * count * 0.99999);
    }
    else
    {
        float j = floor(random(id, 4.0) * (count - 1.0) * 0.99999);
        float offset = j >= forbidden ? j + 1.0 : j;
        pick = wrap(last + offset, count);
    }

    // Each beat stretches the jump a touch, so the fractal breathes in and out.
    float stretch = jump + Beat * 0.025 + Bass * 0.012;
    float2 target = cornerAt(pick, corners, midpoints);
    particle.position.xy = lerp(particle.position.xy, target, stretch);
    particle.data.x = pick;
    particle.data.y = pick / count;

    // Colour is inherited: half from the corner just visited, half from the ones before, so each
    // little copy of the shape takes the colour of the path that leads into it.
    particle.velocity = lerp(particle.velocity, cornerColour(pick, count), 0.5);
}

Sprite Show(Particle particle, uint id)
{
    Sprite sprite = (Sprite)0;
    sprite.position = particle.position.xy;
    sprite.size = 0.75;

    // Points glow with the band of the spectrum that matches their corner, and a few sparkle
    // with the treble.
    float level = spectrum(0.05 + particle.data.y * 0.6);
    float sparkle = 1.0 + step(0.997, fixedRandom(id, 5.0)) * Treble * 6.0;
    sprite.colour = particle.velocity * 0.15 * (0.75 + level * 0.6 + Beat * 0.25) * sparkle;

    // The first few hops are still on their way in from a random start: keep them hidden.
    sprite.colour *= saturate(particle.age * 4.0 - 0.3);
    return sprite;
}

// Distance to the segment from a to b.
float segmentDistance(float2 p, float2 a, float2 b)
{
    float2 pa = p - a;
    float2 ba = b - a;
    float h = saturate(dot(pa, ba) / max(dot(ba, ba), 1e-6));
    return length(pa - ba * h);
}

float3 Render(float2 uv, float2 p)
{
    float corners, jump, forbidden, midpoints;
    rulesFor(rulesIndex(), corners, jump, forbidden, midpoints);
    float count = midpoints > 0.5 ? corners * 2.0 : corners;
    float px = pixelSize();

    // A deep, quiet ground.
    float3 colour = lerp(float3(0.012, 0.012, 0.022), float3(0.0, 0.0, 0.004), saturate(length(p) * 0.7));

    // The outline of the shape, hair-thin, and a soft halo round each corner pulsing on the beat.
    float outline = 1e3;
    float3 halo = 0.0;
    [loop]
    for (int i = 0; i < 12; i++)
    {
        if (i >= (int)corners)
        {
            break;
        }
        float2 a = cornerAt(i * (midpoints > 0.5 ? 2.0 : 1.0), corners, midpoints);
        float2 b = cornerAt(wrap((i + 1) * (midpoints > 0.5 ? 2.0 : 1.0), count), corners, midpoints);
        outline = min(outline, segmentDistance(p, a, b));
    }
    [loop]
    for (int j = 0; j < 12; j++)
    {
        if (j >= (int)count)
        {
            break;
        }
        float d = length(p - cornerAt(j, corners, midpoints));
        float ring = abs(d - 0.022 - Beat * 0.01);
        halo += cornerColour(j, count) * (smoothstep(px * 1.5, 0.0, ring) * 0.35 + glow(d, 40.0) * 0.06) * (0.6 + Beat * 0.6);
    }
    colour += float3(0.5, 0.55, 0.7) * smoothstep(px * 1.2, 0.0, outline) * 0.08;
    colour += halo;

    // A whisper of trail keeps the dots crisp but steadies them between frames.
    return colour + particles(uv) + previous(uv) * 0.35;
}
