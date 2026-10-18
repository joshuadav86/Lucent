// Name: Iron Filings
// Iron filings scattered on a dark plate snap into line around a handful of slowly wandering bar magnets, gathering into fine chains along glowing field lines; each magnet pulls with its own part of the spectrum and every beat jolts the filings loose before they settle again.
// Family: Particles
// Tags: Geometric, Dark
// Added: 2026-10-17 17:15
// Particles: 262144

// The field of a flat bar magnet is easiest to follow through its "flux function": a number at
// each point whose contour lines are exactly the field lines, and whose slope gives the field.
// The filings turn to lie along the field, and drift sideways onto the nearest of a fine set of
// those contours, which is how real filings clump into chains.
static const int Magnets = 5;
static const float MagnetLength = 0.12;  // half length
static const float MagnetWidth = 0.042;   // half width

void magnet(int i, out float2 position, out float2 moment, out float3 colour)
{
    float k = i;
    float t = Time * (0.045 + k * 0.007) + k * 1.9;
    position = float2(sin(t * 1.3 + k * 2.4) * Aspect * 0.62, sin(t * 0.9 + k * 4.1) * 0.6);
    float angle = Time * (0.08 + k * 0.02) * ((i & 1) == 0 ? 1.0 : -1.0) + k * 1.7;
    // Each magnet pulls with its own band of the spectrum.
    float strength = 0.25 + spectrum(0.04 + k * 0.17) * 0.9 + Bass * 0.15;
    moment = float2(cos(angle), sin(angle)) * strength;
    colour = hsv2rgb(0.52 + k * 0.11, 0.7, 1.0);
}

// The flux function at q, and its slope.
float fluxAt(float2 q, out float2 slope)
{
    float flux = 0.0;
    slope = 0.0;
    [unroll]
    for (int i = 0; i < Magnets; i++)
    {
        float2 position;
        float2 moment;
        float3 colour;
        magnet(i, position, moment, colour);
        float2 d = q - position;
        float r2 = dot(d, d) + 0.004;
        float c = moment.x * d.y - moment.y * d.x;
        flux += c / r2;
        slope += (float2(-moment.y, moment.x) * r2 - c * 2.0 * d) / (r2 * r2);
    }
    return flux * 0.05;
}

float2 fieldFromSlope(float2 slope)
{
    return float2(slope.y, -slope.x) * 0.05;
}

bool insideMagnet(float2 q, float margin)
{
    bool inside = false;
    [unroll]
    for (int i = 0; i < Magnets; i++)
    {
        float2 position;
        float2 moment;
        float3 colour;
        magnet(i, position, moment, colour);
        float2 axis = normalize(moment);
        float2 d = q - position;
        float2 local = float2(dot(d, axis), dot(d, float2(-axis.y, axis.x)));
        inside = inside || (abs(local.x) < MagnetLength + margin && abs(local.y) < MagnetWidth + margin);
    }
    return inside;
}

void Spawn(inout Particle particle, uint id)
{
    particle.position = float3((random(id, 1.0) * 2.0 - 1.0) * (Aspect + 0.05), (random(id, 2.0) * 2.0 - 1.0) * 1.05, 0.0);
    particle.velocity = 0.0;
    particle.life = lerp(15.0, 50.0, random(id, 3.0));
    // data: x = the filing's angle, y = the last beat it felt.
    particle.data = float4(random(id, 4.0) * PI, BeatCount, 0.0, 0.0);
}

void Simulate(inout Particle particle, uint id)
{
    float dt = min(DeltaTime, 0.05);
    float2 q = particle.position.xy;
    float2 slope;
    float flux = fluxAt(q, slope);
    float2 field = fieldFromSlope(slope);
    float strength = length(field);

    // Turn to lie along the field: quickly where it's strong, hardly at all where it's weak.
    float target = atan2(field.y, field.x);
    float turn = wrap(target - particle.data.x + PI * 0.5, PI) - PI * 0.5;
    particle.data.x += turn * (1.0 - exp(-dt * 30.0 * saturate(strength * 3.0)));

    // Drift sideways onto the nearest chain.
    float spacing = 40.0;
    float off = frac(flux * spacing + 0.5) - 0.5;
    float steep = max(length(slope) * 0.05 * spacing, 1e-3);
    float2 across = slope / max(length(slope), 1e-6);
    float shift = clamp(off / steep, -0.02, 0.02) * (1.0 - exp(-dt * 2.5 * saturate(strength * 2.0)));
    q -= across * shift;

    // Every beat knocks the filings askew and shakes them a little.
    if (BeatCount != particle.data.y)
    {
        particle.data.y = BeatCount;
        float kick = 0.3 + Bass * 0.9;
        particle.data.x += (random(id, 5.0) - 0.5) * kick * 1.6;
        q += (float2(random(id, 6.0), random(id, 7.0)) - 0.5) * 0.012 * kick;
    }

    particle.velocity = float3((q - particle.position.xy) / max(dt, 1e-4), 0.0);
    particle.position.xy = q;
    particle.data.z = strength;

    // Filings that end up under a magnet or off the plate start again somewhere else.
    if (insideMagnet(q, 0.004) || abs(q.x) > Aspect + 0.1 || abs(q.y) > 1.1)
    {
        particle.life = 0.0;
    }
}

Sprite Show(Particle particle, uint id)
{
    float angle = particle.data.x;
    float2 along = float2(cos(angle), sin(angle));
    float filingLength = 0.012 + fixedRandom(id, 8.0) * 0.012;

    // Steel catching a slowly circling light: filings lying across the light glint.
    float lightAngle = Time * 0.21;
    float glint = pow(abs(sin(angle - lightAngle)), 24.0);
    float strength = saturate(particle.data.z * 2.0);
    float3 steel = float3(0.55, 0.58, 0.64);
    float3 colour = steel * (0.16 + strength * 1.0) + float3(1.0, 0.95, 0.85) * glint * (2.0 + Treble * 3.0) * (0.4 + strength);

    // Fade in so respawned filings don't pop.
    colour *= saturate(particle.age * 1.5);

    Sprite sprite = (Sprite)0;
    sprite.position = particle.position.xy;
    sprite.size = 0.8;
    sprite.streak = along * filingLength;
    sprite.colour = colour;
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    // A dark plate of blued steel, softly lit from above.
    float grain = noise(float2(p.x * 3.0, p.y * 260.0)) * 0.5 + noise(p * 40.0) * 0.5;
    float3 plate = float3(0.012, 0.014, 0.02) * (0.7 + grain * 0.6) * (1.2 - dot(p, p) * 0.15);

    // Glowing field lines: contours of the flux function, each tinted by whichever magnets
    // pull hardest there.
    float2 slope;
    float flux = fluxAt(p, slope);
    float3 tint = 0.0;
    float weight = 0.0;
    [unroll]
    for (int i = 0; i < Magnets; i++)
    {
        float2 position;
        float2 moment;
        float3 colour;
        magnet(i, position, moment, colour);
        float2 d = p - position;
        float w = dot(moment, moment) / pow(dot(d, d) + 0.01, 2.0);
        tint += colour * w;
        weight += w;
    }
    tint /= max(weight, 1e-6);

    float spacing = 9.0;
    float steep = max(length(slope) * 0.05 * spacing, 1e-5);
    float off = abs(frac(flux * spacing + 0.5) - 0.5);
    float pixelsAway = off / steep / pixelSize();
    // Where lines crowd together near the poles they fade, rather than turning to noise.
    float crowding = saturate(1.6 - steep * pixelSize() * 18.0);
    float lines = (exp(-pixelsAway * pixelsAway * 0.25) * 0.6 + exp(-pixelsAway * 0.12) * 0.12) * crowding;
    float pulse = 0.55 + Level * 0.6 + Beat * 0.5;
    float3 colour = plate + tint * lines * pulse * 0.55;

    colour += particles(uv) + previous(uv) * 0.15;

    // The magnets themselves: polished bars with a red north end and a slate south end,
    // casting a soft shadow and glowing at the poles with their strength.
    [unroll]
    for (int j = 0; j < Magnets; j++)
    {
        float2 position;
        float2 moment;
        float3 magnetColour;
        magnet(j, position, moment, magnetColour);
        float2 axis = normalize(moment);
        float2 d = p - position;
        float2 local = float2(dot(d, axis), dot(d, float2(-axis.y, axis.x)));
        float2 box = abs(local) - float2(MagnetLength, MagnetWidth) + 0.012;
        float edge = length(max(box, 0.0)) + min(max(box.x, box.y), 0.0) - 0.012;

        float2 shadowBox = abs(local - float2(0.01, -0.012)) - float2(MagnetLength, MagnetWidth) + 0.02;
        float shadowEdge = length(max(shadowBox, 0.0)) + min(max(shadowBox.x, shadowBox.y), 0.0) - 0.02;
        colour *= lerp(0.35, 1.0, smoothstep(-0.01, 0.03, shadowEdge));

        float strength = length(moment);
        float3 poles = magnetColour * strength * (exp(-length(local - float2(MagnetLength, 0.0)) * 22.0) + exp(-length(local + float2(MagnetLength, 0.0)) * 22.0));
        colour += poles * (0.25 + Beat * 0.4);

        // Rounded top: brighter along the middle, a sharp highlight off to one side.
        float across = local.y / MagnetWidth;
        float3 paint = local.x > 0.0 ? float3(0.5, 0.05, 0.04) : float3(0.12, 0.14, 0.18);
        float sheen = 0.35 + 0.65 * saturate(1.0 - across * across);
        float highlight = exp(-pow((across - 0.35) * 5.0, 2.0)) * 0.6;
        float band = smoothstep(0.004, 0.0, abs(local.x)) * 0.5;
        float3 body = paint * sheen + float3(0.9, 0.92, 1.0) * highlight * 0.35 + band;
        float coverage = smoothstep(pixelSize(), -pixelSize(), edge);
        colour = lerp(colour, body, coverage);
    }
    return colour;
}
