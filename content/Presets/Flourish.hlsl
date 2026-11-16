// Name: Flourish
// Fine strands of ink sweep across warm paper like the hairs of a dry brush: a hundred and twenty thousand of
// them stream from five brushes at the left edge, and a slow current carries them over the page in long, crossing
// curves. The brushes write the music as they go. Each carries one part of it, bass at the top down to treble at
// the foot, and flicks up and down once a beat, wider and with darker, heavier ink the louder its part sounds, so
// every strand bears a run of loops and swells that the current carries off across the page. On every beat a pulse
// of darker ink runs along all the strands from the brushes across the page, one brush in turn presses down for a
// darker line, the strands run faster, and the current turns a little further.
// The ink soon fades back into the paper, and every eight beats each brush takes up a new ink, indigo, violet,
// wine, olive, or teal, so the colours mix wherever the strands cross.
// Family: Particles
// Tags: Colourful, Minimal, Calm
// Added: 2026-11-16 18:45
// Particles: 120000

static const float FlTips = 5.0;
static const float FlHairs = 22.0;     // hairs on each brush
static const float FlInkStrength = 0.16;
static const float FlFade = 1.4;       // how fast the ink fades back into the paper, per second

float flPart(float x)
{
    return spectrum(x) * (1.0 + 0.9 * x);
}

// How loud one part of the music is, judged against the music as a whole, so a quiet song shows as plainly as a
// loud one.
float flLoud(float x)
{
    float sound = 0.0;
    [unroll]
    for (int part = 0; part < 6; part++)
        sound += flPart(0.08 + 0.12 * part) / 6.0;
    float heard = (flPart(x - 0.03) + flPart(x) + flPart(x + 0.03)) / 3.0;
    return smoothstep(0.0, 1.0, saturate((heard - 0.55 * sound) / (0.8 * sound + 0.12)));
}

// Which part of the music brush tip k carries: bass at the top, treble at the foot.
float flBand(float k)
{
    return 0.06 + 0.16 * k;
}

// Where brush tip k sits: just off the left edge, riding up and down once a bar, each tip a little behind the one
// above it, and drifting slowly besides.
float2 flTip(float k)
{
    float sway = 0.13 * sin(6.2832 * BarPhase - 0.9 * k) + 0.07 * sin(Time * 0.19 + 2.3 * k);
    return float2(-Aspect - 0.04, -0.68 + 0.34 * k + sway);
}

// The brush's flick: up and down once a beat, wider the louder its part of the music, with a slow sway beneath so
// it never stops writing between beats.
float flFlick(float k, float loud)
{
    float reach = 0.025 + 0.085 * loud * (0.7 + 0.3 * BeatSwell) + 0.05 * DropPulse;
    return reach * sin(6.2832 * BeatPhase - 0.9 * k) + 0.025 * sin(Time * 2.3 + 1.7 * k);
}

// How much of each colour an ink takes out of the paper's light: indigo, violet, wine, olive, and teal.
float3 flInk(float which)
{
    float k = wrap(which, 5.0);
    if (k < 0.5) return float3(1.9, 1.55, 0.45);
    if (k < 1.5) return float3(0.95, 1.95, 0.45);
    if (k < 2.5) return float3(0.55, 2.1, 1.35);
    if (k < 3.5) return float3(0.75, 0.7, 1.95);
    return float3(2.1, 0.65, 0.75);
}

void Spawn(inout Particle particle, uint id)
{
    // Each particle belongs to one hair of one brush and leaves the brush where the hair is just now, so together
    // the particles of a hair trace the brush's path as the current carries them off: one fine line that records
    // the music.
    float hair = floor(fixedRandom(id, 1.0) * FlTips * FlHairs);
    float k = floor(hair / FlHairs);
    float within = hair - k * FlHairs;
    float loud = flLoud(flBand(k));
    float across = ((within + 0.5) / FlHairs - 0.5) * (0.2 + 0.06 * loud + 0.06 * BeatSwell);
    particle.position = float3(flTip(k) + float2(0.0, across + flFlick(k, loud)), 0.0);
    particle.velocity = 0.0;
    particle.life = 4.0 + 4.0 * random(id, 4.0);
    // How much ink it carries, set as it leaves: more as its part of the music grows loud, a little more on the
    // beat, and much more from the brush that presses down this beat.
    float pressed = abs(k - wrap(BeatCount, FlTips)) < 0.5 ? 1.2 * BeatSwell : 0.0;
    float ink = 0.25 + 1.5 * loud + 0.4 * BeatSwell * BeatSwell + pressed;
    // The ink it carries for life; each brush moves to the next every eight beats.
    particle.data = float4(k, wrap(floor(BeatCount / 8.0), 1024.0) + 2.0 * k, fixedRandom(hair, 9.0), ink);
}

void Simulate(inout Particle particle, uint id)
{
    float dt = min(DeltaTime, 0.05);
    float2 at = particle.position.xy;
    // The current: broad swirls that turn a little with every beat, and finer ones that comb the strands apart.
    float turn = Time * 0.025 + wrap(BeatGlide, 4096.0) * 0.035;
    float2 broad = curlNoise2(at * 1.05 + float2(0.0, 3.1), turn);
    float2 fine = curlNoise2(at * 3.6 + float2(7.7, 1.9), turn * 1.6);
    float2 flow = float2(0.8, 0.0) + broad * 0.7 + fine * 0.16;
    float pace = 0.55 * (1.0 + 0.4 * BeatSwell + 0.5 * DropPulse) * (0.85 + 0.3 * fixedRandom(id, 6.0));
    particle.velocity.xy = flow * pace;
    particle.position.xy = at + particle.velocity.xy * dt;
}

Sprite Show(Particle particle, uint id)
{
    Sprite sprite = (Sprite)0;
    sprite.position = particle.position.xy;
    float ink = particle.data.w;
    sprite.size = 0.7 + 0.35 * fixedRandom(id, 5.0) + 0.25 * ink;
    // A streak back along the way it came, so each hair draws an unbroken line however fast the screen runs.
    sprite.streak = particle.velocity.xy * min(DeltaTime, 0.05);
    float fade = saturate(particle.age * 2.0) * saturate(particle.life * 0.8);
    float dark = 0.15 + 2.2 * particle.data.z * particle.data.z * particle.data.z;
    // On every beat a pulse of darker ink runs along all the strands, out from the brushes across the page.
    float since = SongMapped > 0.25 ? BeatPhase : saturate(-log(max(Beat, 0.004)) / 2.75);
    float across = (particle.position.x + Aspect) / (2.0 * Aspect);
    float pulse = exp(-pow((across - since * 1.2 + 0.05) * 6.0, 2.0)) * smoothstep(0.0, 0.06, since);
    sprite.colour = flInk(particle.data.y) * FlInkStrength * dark * ink * (1.0 + (0.8 + 1.2 * BeatSwell) * pulse) * fade * min(DeltaTime, 0.05) * 60.0;
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    // Warm paper with a soft fall-off to the corners and a faint tooth.
    float3 paper = float3(1.04, 0.99, 0.88) * (1.0 - 0.05 * dot(p, p));
    paper *= 0.985 + 0.03 * hash(floor(p / (2.0 * pixelSize())));
    float3 last = FrameCount < 3 ? paper : previous(uv);
    // The ink fades back into the paper within a second or so, and the new hairs soak in on top.
    float keep = exp(-min(DeltaTime, 0.05) * FlFade);
    return lerp(paper, last, keep) * exp(-particles(uv));
}
