// Name: Fractal Flame
// Silky veils of fire fold and curl round a slowly turning heart of light, like flame caught in glass. Their shapes melt from one form to the next as the music plays, the bass swelling the inner petals and every beat flaring them brighter.
// Family: Particles
// Tags: Colourful, Fire
// Particles: 458752

// A fractal flame is the chaos game with bent rules. Each rule stretches and turns a point, then
// pushes it through a curved "variation" (a sine wave, a swirl, an inside-out bubble). Points hop
// from rule to rule at random, and where they land most often the flame burns brightest. Each rule
// also carries a colour, and a point's colour is a running blend of the rules it has passed
// through, which is where the soft gradients come from. The trail buffer keeps a running count of
// hits in every pixel, stored on a log scale so faint wisps and the blazing core both show.
static const float Symmetry = 5.0;

// The slowly turning timeline the flame morphs along; louder music moves it on a little faster.
float morphTime()
{
    return Time * 0.045 + Level * 0.15;
}

// An affine rule: x' = (a x + b y + c, d x + e y + f), built from a scale, a turn, a shear and a shift.
float2 affine(float2 v, float scale, float turn, float shear, float2 shift)
{
    float2 r = rotate(v, turn);
    r.x += r.y * shear;
    return r * scale + shift;
}

float2 sinusoidal(float2 v)
{
    return sin(v);
}

float2 spherical(float2 v)
{
    return v / max(dot(v, v), 0.02);
}

float2 swirl(float2 v)
{
    float r2 = dot(v, v);
    float s, c;
    sincos(r2, s, c);
    return float2(v.x * s - v.y * c, v.x * c + v.y * s);
}

// The Julia variation: a square root that splits every shape into two half-turned copies.
float2 julia(float2 v, float branch)
{
    float r = sqrt(length(v));
    float angle = atan2(v.y, v.x) * 0.5 + branch * PI;
    return float2(cos(angle), sin(angle)) * r;
}

float2 horseshoe(float2 v)
{
    float r = max(length(v), 1e-3);
    return float2((v.x - v.y) * (v.x + v.y), 2.0 * v.x * v.y) / r;
}

// Applies rule i to v, and gives the colour (0..1 along the palette) that rule carries.
float2 applyRule(int i, float2 v, float branch, out float tone)
{
    float t = morphTime();
    if (i == 0)
    {
        // The inner petals: an inside-out bubble that the bass swells.
        float2 a = affine(v, 0.62 + 0.08 * sin(t * 1.3), 0.35 + 0.5 * sin(t * 0.7), 0.15 * sin(t * 0.9), float2(0.38 + 0.12 * sin(t * 1.1), 0.05));
        float bubble = 0.45 + 0.3 * sin(t * 0.8) + Bass * 0.25;
        tone = 0.08;
        return lerp(a, spherical(a), saturate(bubble)) ;
    }
    if (i == 1)
    {
        // The long veils: a square root that splits and folds, softened by a sine wave.
        float2 a = affine(v, 0.85 + 0.1 * sin(t * 0.9 + 1.0), -0.6 + 0.4 * sin(t * 0.6 + 2.0), 0.2 * sin(t * 0.5), float2(-0.3, 0.32 + 0.15 * sin(t * 0.75)));
        tone = 0.45;
        return lerp(julia(a, branch), sinusoidal(a * 1.3), 0.25 + 0.2 * sin(t * 0.55));
    }
    // The curls: a swirl that the mids spin up.
    float2 a = affine(v, 0.55 + 0.1 * sin(t * 1.4 + 3.0), 1.9 + 0.6 * sin(t * 0.45), -0.25 * sin(t * 0.8 + 1.0), float2(0.1 * sin(t * 0.6), -0.45));
    tone = 0.85;
    float2 curled = swirl(a * (1.0 + Mid * 0.4));
    return lerp(lerp(a, curled, 0.6 + 0.3 * sin(t * 0.65 + 1.0)), horseshoe(a), 0.2 + 0.2 * sin(t * 0.35));
}

// Fire with a cool edge: deep crimson, molten orange, gold, then into rose and violet for the
// faint outer wisps, kept rich rather than garish.
float3 flameColour(float t)
{
    float3 crimson = float3(0.75, 0.06, 0.08);
    float3 orange = float3(1.0, 0.38, 0.05);
    float3 gold = float3(1.0, 0.78, 0.32);
    float3 rose = float3(0.95, 0.22, 0.45);
    float3 violet = float3(0.35, 0.18, 0.95);
    t = frac(t);
    if (t < 0.25) return lerp(crimson, orange, t / 0.25);
    if (t < 0.5) return lerp(orange, gold, (t - 0.25) / 0.25);
    if (t < 0.75) return lerp(gold, rose, (t - 0.5) / 0.25);
    return lerp(rose, violet, (t - 0.75) / 0.25);
}

void Spawn(inout Particle particle, uint id)
{
    particle.position = float3(random(id, 1.0) * 2.0 - 1.0, random(id, 2.0) * 2.0 - 1.0, 0.0);
    particle.velocity = 0.0;
    particle.data = float4(random(id, 3.0), 0.0, 0.0, 0.0);
    // Points start somewhere random; give them a moment to fall onto the flame before they show,
    // and let them restart every so often so none get stuck.
    particle.life = 2.0 + random(id, 4.0) * 6.0;
}

void Simulate(inout Particle particle, uint id)
{
    float2 v = particle.position.xy;
    float tone = particle.data.x;
    [unroll]
    for (int k = 0; k < 2; k++)
    {
        float roll = random(id, 5.0 + k * 7.0);
        int rule = roll < 0.36 ? 0 : (roll < 0.72 ? 1 : 2);
        float ruleTone;
        v = applyRule(rule, v, floor(random(id, 9.0 + k * 7.0) * 2.0), ruleTone);
        tone = (tone + ruleTone) * 0.5;

        // Now and then, turn the point by a fifth of a circle: the flame gains five-fold symmetry.
        if (random(id, 6.0 + k * 7.0) < 0.5)
        {
            float turns = floor(random(id, 8.0 + k * 7.0) * (Symmetry - 1.0)) + 1.0;
            v = rotate(v, turns * 2.0 * PI / Symmetry);
        }
    }

    // A point flung too far restarts on its next frame.
    if (!(dot(v, v) < 400.0))
    {
        particle.life = -1.0;
        v = 0.0;
    }
    particle.position.xy = v;
    particle.data.x = tone;
}

Sprite Show(Particle particle, uint id)
{
    // Turn the whole flame slowly, breathing a little with the bass.
    float2 v = particle.position.xy;
    float2 seen = rotate(v, Time * 0.03) * (0.6 + Bass * 0.03);

    float tone = particle.data.x;
    float level = spectrum(0.04 + tone * 0.5);
    Sprite sprite = (Sprite)0;
    sprite.position = seen;
    sprite.size = 1.1;
    sprite.colour = flameColour(tone + sin(Time * 0.02) * 0.1) * 0.03 * (0.7 + level * 0.8 + Beat * 0.4);
    sprite.colour *= step(0.25, particle.age);
    return sprite;
}

// The trail buffer holds brightness on a log scale; these convert to and from the raw count of hits.
static const float Squeeze = 6.0;
static const float Scale = 0.2;

// The log of the count is raised to a small power for contrast: faint wisps stay delicate and
// the dense folds stand out.
static const float Contrast = 1.5;

float3 toHits(float3 shown)
{
    return (exp(pow(max(shown, 0.0) / Scale, 1.0 / Contrast)) - 1.0) / Squeeze;
}

float3 toShown(float3 hits)
{
    return pow(log(1.0 + max(hits, 0.0) * Squeeze), Contrast) * Scale;
}

float3 Render(float2 uv, float2 p)
{
    // Old hits fade quickly enough that the flame can change shape without leaving ghosts.
    float3 hits = toHits(previous(uv)) * 0.9 + particles(uv);
    float3 colour = toShown(hits);

    // A faint ember glow behind it all so the darkest corners are never quite black.
    colour += float3(0.012, 0.004, 0.006) * glow(length(p), 1.2) * (1.0 + Bass);
    return colour;
}
