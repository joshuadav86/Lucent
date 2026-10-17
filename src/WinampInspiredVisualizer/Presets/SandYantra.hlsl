// Name: Sand Yantra
// A quarter of a million grains of coloured sand spiral out from a single point to lay down the Sri Yantra, its nine interlocking triangles ringed by lotus petals and a gated square, shiver with the music, then are swept back into the centre and poured out again.
// Family: Particles
// Tags: Geometric, Colourful, Calm
// Particles: 262144

// One full build, hold and sweep of the mandala takes this long, in seconds.
static const float Cycle = 44.0;
// How big the mandala is on screen: its square's half width, in p units.
static const float Scale = 0.9;

// The Sri Yantra's nine triangles in a circle of radius 1: the y of the flat side, the y of the
// point, and the flat side's half width. Four point up and five point down; together they make
// the forty-three small triangles of the classic figure.
static const float3 Triangles[9] =
{
    float3(0.2679, -1.0000, 0.9634),
    float3(-0.2425, 1.0000, 0.9702),
    float3(-0.7004, 0.2679, 0.5128),
    float3(-0.4792, 0.7190, 0.7174),
    float3(0.7190, -0.1066, 0.5951),
    float3(0.4688, -0.7004, 0.6902),
    float3(-0.1066, 0.4688, 0.3505),
    float3(0.1569, -0.4792, 0.3365),
    float3(0.0520, -0.2425, 0.2539),
};

// Where the triangles sit in the mandala, which has a square of half width 1.
static const float YantraRadius = 0.5;

// The colours of a sand mandala: lapis, cinnabar, saffron, malachite, white and gold.
static const float3 Lapis = float3(0.08, 0.22, 1.0);
static const float3 Cinnabar = float3(1.0, 0.12, 0.06);
static const float3 Saffron = float3(1.0, 0.55, 0.05);
static const float3 Malachite = float3(0.05, 0.75, 0.35);
static const float3 Rose = float3(1.0, 0.25, 0.45);
static const float3 Chalk = float3(1.0, 0.92, 0.8);
static const float3 Gold = float3(1.0, 0.75, 0.3);
static const float3 Midnight = float3(0.05, 0.06, 0.3);

// How far q is from the outline of one triangle, and whether it's inside.
float triangleOutline(float2 q, float3 t, out bool inside)
{
    float flat = t.x;
    float tip = t.y;
    float halfWidth = t.z;
    float height = flat - tip;
    // Along the triangle from its point (0) to its flat side (1).
    float along = (q.y - tip) / height;
    inside = along > 0.0 && along < 1.0 && abs(q.x) < halfWidth * along;

    float2 side = normalize(float2(abs(height), -halfWidth * sign(height)));
    float toSide = abs(dot(float2(abs(q.x), q.y - tip), side));
    float toFlat = abs(q.y - flat) + max(abs(q.x) - halfWidth, 0.0);
    float sideReach = along > -0.01 && along < 1.01 ? 0.0 : 1.0;
    return min(toSide + sideReach, toFlat);
}

// A ring of lotus petals between two radii: 1 inside a petal, with outline the distance to its edge.
float lotus(float2 q, float count, float inner, float outer, float turn, out float outline)
{
    float radius = length(q);
    float u = (radius - inner) / (outer - inner);
    float wedge = 2.0 * PI / count;
    float angle = wrap(atan2(q.y, q.x) + turn, wedge) - wedge * 0.5;
    // Round at the base, pointed at the tip.
    float halfWidth = wedge * 0.5 * pow(saturate(1.0 - u), 0.7) * sqrt(saturate(u * 4.0 + 0.3));
    outline = min(abs(abs(angle) - halfWidth) * radius, abs(radius - inner));
    return u > 0.0 && u < 1.0 && abs(angle) < halfWidth ? 1.0 : 0.0;
}

// The colour of the sand at a point of the mandala (square of half width 1), and whether there's
// any sand there at all (alpha 0 outside the outer wall).
float4 sandColour(float2 q)
{
    float radius = length(q);
    float nearestLine = 1e3;

    // The square outer wall, three bands deep, with a T-shaped gate in the middle of each side.
    float2 a = abs(q);
    float wall = max(a.x, a.y);
    float along = min(a.x, a.y);
    if (along < 0.16)
    {
        wall -= 0.07;
    }
    if (wall > 1.0)
    {
        return 0.0;
    }
    if (wall > 0.86)
    {
        float band = floor((wall - 0.86) / 0.0467);
        float3 colour = band < 0.5 ? Malachite : band < 1.5 ? Cinnabar : Saffron;
        float edge = abs(frac((wall - 0.86) / 0.0467 + 0.5) - 0.5) * 0.0467;
        return float4(edge < 0.0045 ? Gold : colour, 1.0);
    }

    // Between the wall and the rings: deep blue ground with a scattering of gold.
    if (radius > 0.82)
    {
        return float4(Midnight, 1.0);
    }

    // Three thin rings.
    if (radius > 0.765)
    {
        float ring = abs(frac((radius - 0.765) / 0.0183) - 0.5);
        return float4(ring > 0.3 ? Gold : Midnight * 2.0, 1.0);
    }

    // Sixteen outer petals, then eight inner ones, each pink with a pale heart.
    float outline;
    if (radius > 0.625)
    {
        float petal = lotus(q, 16.0, 0.625, 0.765, PI / 16.0, outline);
        if (outline < 0.0065)
        {
            return float4(Gold, 1.0);
        }
        return float4(petal > 0.5 ? lerp(Rose, Chalk, saturate(1.0 - (radius - 0.625) * 9.0) * 0.5) : Malachite * 0.6, 1.0);
    }
    if (radius > YantraRadius)
    {
        float petal = lotus(q, 8.0, YantraRadius, 0.625, PI / 8.0, outline);
        if (outline < 0.0065 || abs(radius - YantraRadius) < 0.005)
        {
            return float4(Gold, 1.0);
        }
        return float4(petal > 0.5 ? lerp(Saffron, Chalk, 0.25) : Lapis * 0.7, 1.0);
    }

    // The yantra itself: the deeper inside the nest of triangles, the warmer the colour.
    float2 t = q / YantraRadius;
    int count = 0;
    [unroll]
    for (int i = 0; i < 9; i++)
    {
        bool inside;
        nearestLine = min(nearestLine, triangleOutline(t, Triangles[i], inside));
        count += inside ? 1 : 0;
    }
    if (nearestLine * YantraRadius < 0.0065)
    {
        return float4(Gold * 1.2, 1.0);
    }
    if (length(t - float2(0.0, -0.06)) < 0.05)
    {
        return float4(Chalk * 1.6, 1.0);  // the bindu, the point everything grows from
    }
    float3 colour = count == 0 ? Lapis : count == 1 ? Cinnabar : count == 2 ? Lapis * 1.2 :
                    count == 3 ? Saffron : count == 4 ? Cinnabar : count == 5 ? Malachite : Chalk;
    return float4(colour, 1.0);
}

// A grain's home in the mandala, found once from its fixed personality. A few tries make sure it
// lands somewhere with sand.
float2 homeOf(uint id, out float3 colour)
{
    colour = 0.0;
    float2 q = 0.0;
    [unroll]
    for (int attempt = 0; attempt < 4; attempt++)
    {
        q = float2(fixedRandom(id, 1.0 + attempt), fixedRandom(id, 11.0 + attempt)) * 2.14 - 1.07;
        float4 sand = sandColour(q);
        if (sand.a > 0.5)
        {
            colour = sand.rgb;
            return q;
        }
    }
    return q;
}

// How far through its journey a grain is: 0 at the centre, 1 resting in place. The mandala grows
// outward from the bindu, then is swept back in from the outside.
float progressOf(uint id, float radius, float t)
{
    float reach = radius / 1.42;
    float personal = fixedRandom(id, 21.0);
    float arrive = 1.0 + reach * 13.0 + personal * 1.2;
    float build = smoothstep(arrive, arrive + 2.8, t);
    float leave = Cycle - 11.0 + (1.0 - reach) * 6.5 + personal * 1.0;
    float sweep = smoothstep(leave, leave + 3.0, t);
    return build * (1.0 - sweep);
}

float cycleTime()
{
    return wrap(Time + 26.0, Cycle);
}

// Each grain finds its home and colour once, when it's spawned, and remembers them:
// data holds its colour and home x, velocity.z its home y.
void Spawn(inout Particle particle, uint id)
{
    float3 colour;
    float2 home = homeOf(id, colour);
    particle.position = 0.0;
    particle.velocity = float3(0.0, 0.0, home.y);
    particle.life = 1e6;
    particle.data = float4(colour, home.x);
}

void Simulate(inout Particle particle, uint id)
{
    float2 home = float2(particle.data.w, particle.velocity.z);
    float radius = length(home);
    float angle = atan2(home.y, home.x);
    float s = progressOf(id, radius, cycleTime());

    // On the way in and out each grain rides a spiral, a turn and a half from the centre.
    float2 place = float2(cos(angle), sin(angle)) * radius * s;
    place = rotate(place, (1.0 - s) * 3.0 * PI);

    // Resting sand trembles like grains on a drum skin, each part to its own slice of the spectrum.
    float level = spectrum(saturate(radius / 1.42) * 0.8 + 0.02);
    float2 shiver = float2(random(id, 31.0), random(id, 32.0)) - 0.5;
    place += shiver * (0.002 + level * level * 0.012 + Beat * 0.004) * s;

    // A gentle breath with the bass.
    place *= 1.0 + Bass * 0.025;

    float2 onScreen = place * Scale;
    particle.velocity.xy = (onScreen - particle.position.xy) / max(DeltaTime, 1e-4);
    particle.position = float3(onScreen, s);
}

Sprite Show(Particle particle, uint id)
{
    float3 colour = particle.data.rgb;
    float2 home = float2(particle.data.w, particle.velocity.z);
    float radius = length(home);
    float s = particle.position.z;

    // A ring of light runs outward through the sand on every beat.
    float ring = (1.0 - Beat) * 1.6;
    float wave = exp(-abs(radius - ring) * 30.0) * Beat;

    // Gold catches the treble and glints.
    float glint = dot(colour, float3(0.2, 0.7, 0.1)) > 0.7 ? step(0.97, random(id, 41.0)) * Treble * 4.0 : 0.0;

    // Grains in flight are a faint, paler spray (thousands pass over the same spot at once), and
    // they fade out near the centre so the pour point doesn't burn white.
    float flight = 1.0 - smoothstep(0.85, 1.0, s);
    float3 shown = lerp(colour, float3(1.0, 0.85, 0.65), flight * 0.2);
    float level = spectrum(saturate(radius / 1.42) * 0.8 + 0.02);
    float brightness = 0.5 * (0.75 + level * 0.5) * (1.0 - flight * 0.75) * smoothstep(0.0, 0.25, s) * (1.0 + wave * 1.5 + glint);

    Sprite sprite = (Sprite)0;
    sprite.position = particle.position.xy;
    sprite.size = 1.0;
    sprite.colour = shown * brightness;
    sprite.streak = particle.velocity.xy * 0.02 * flight;
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    // Trails only while grains fly; the resting mandala stays crisp because each frame redraws it.
    return particles(uv) + previous(uv) * 0.35;
}
