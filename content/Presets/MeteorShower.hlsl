// Name: Meteor Shower
// Meteors pour out of a glowing radiant above a mirror lake, their trains twisting into braids of light, while the star trails wheeling overhead light up ring by ring with the spectrum; strong beats send out rings of meteors in perfect symmetry, each one bursting into a turning shape of light.
// Family: Particles
// Tags: Space, Geometric, Dark, Colourful
// Added: 2026-10-17 17:12
// Particles: 262144

// Every meteor is a line of particles: each one waits at its place along the meteor's path, lights
// up as the burning head passes, then glows on as part of the train left behind. The train is a
// braid of strands twisting round the path, which loosens and blooms as it ages and drifts in the
// wind. The particles of one meteor all make the same choices on the same frame, so they stay
// together as one meteor without talking to each other.
static const uint Trail = 1024;         // particles per meteor
static const uint VolleySlots = 8;      // a volley is a ring of up to 8 matching meteors
static const uint Volleys = 6;          // the first Volleys * VolleySlots meteors are kept for volleys
static const uint VolleyMeteors = Volleys * VolleySlots;
static const uint BurstPoints = 256;    // the last particles of a volley meteor make the shape it bursts into

uint meteorOf(uint id)
{
    return id / Trail;
}

// A random number shared by every particle of one meteor on this frame.
float meteorChance(uint meteor, float salt)
{
    return pcgHash(meteor * 7919u ^ pcgHash((uint)FrameCount + asuint(salt))) / 4294967295.0;
}

float meteorRandom(float seed, float salt)
{
    return pcgHash(asuint(seed) ^ pcgHash(asuint(salt) + 0x632BE5ABu)) / 4294967295.0;
}

// ---- The sky's layout ----

// Where the lake meets the mountains, in p units (y down).
static const float Horizon = 0.42;

// The point in the sky the meteors seem to fly away from, drifting slowly.
float2 radiant()
{
    return float2(sin(Time * 0.017) * 0.45, -0.42 + sin(Time * 0.023) * 0.07);
}

// The sky wheels round a pole just above the top of the picture.
static const float2 Pole = float2(0.12, -1.35);

// How many strands a meteor's train braids together.
uint strandsOf(bool volley)
{
    return volley ? 3u : 2u;
}

// How many sides a volley's ring has: 5, 6 or 8.
float foldOf(float seed)
{
    float pick = meteorRandom(seed, 11.0);
    return pick < 0.34 ? 5.0 : pick < 0.67 ? 6.0 : 8.0;
}

// A meteor's path: where it starts (xy) and its direction (zw).
float4 meteorPath(float seed, bool volley, uint slot)
{
    float angle;
    float from;
    if (volley)
    {
        // Every meteor in a volley flies the same path, turned round the radiant by its slot.
        angle = meteorRandom(seed, 1.0) * 2.0 * PI + slot * 2.0 * PI / foldOf(seed);
        from = lerp(0.08, 0.16, meteorRandom(seed, 2.0));
    }
    else
    {
        // Lone meteors mostly fall: straight up is rare.
        angle = PI * 0.5 + (meteorRandom(seed, 1.0) * 2.0 - 1.0) * PI * lerp(0.35, 1.0, meteorRandom(seed, 8.0));
        from = lerp(0.1, 0.9, meteorRandom(seed, 2.0));
    }
    float2 direction = float2(cos(angle), sin(angle));
    return float4(radiant() + direction * from, direction);
}

// Its length (x, in p units), speed (y, p units a second) and how long its train lingers (z, seconds).
float3 meteorShape(float seed, bool volley)
{
    if (volley)
    {
        return float3(lerp(0.3, 0.5, meteorRandom(seed, 3.0)), lerp(1.1, 1.6, meteorRandom(seed, 4.0)), lerp(2.0, 3.5, meteorRandom(seed, 5.0)));
    }
    float bright = meteorRandom(seed, 5.0);
    return float3(lerp(0.15, 0.6, meteorRandom(seed, 3.0)), lerp(1.4, 3.0, meteorRandom(seed, 4.0)), lerp(0.4, 3.0, bright * bright * bright));
}

// What a lone meteor burns with: green magnesium, gold sodium, violet calcium, or white iron.
float3 meteorColour(float seed)
{
    float pick = meteorRandom(seed, 6.0);
    if (pick < 0.35)
    {
        return float3(0.45, 1.0, 0.55);
    }
    if (pick < 0.6)
    {
        return float3(1.0, 0.7, 0.3);
    }
    if (pick < 0.8)
    {
        return float3(0.75, 0.5, 1.0);
    }
    return float3(0.8, 0.9, 1.0);
}

// A volley's strands are three neighbouring colours from one hue.
float3 strandColour(float seed, uint strand)
{
    float hue = meteorRandom(seed, 6.0) + strand * 0.11;
    return hsv2rgb(hue, 0.7, 1.0);
}

// How long after launch a meteor's head reaches the end of its path.
float arrival(float3 shape)
{
    return shape.x / shape.y;
}

// How long a meteor stays up, train and burst included.
float flightTime(float3 shape, bool volley)
{
    return arrival(shape) + max(shape.z * 2.5, volley ? 6.0 : 0.0);
}

void Spawn(inout Particle particle, uint id)
{
    uint meteor = meteorOf(id);
    // A meteor that isn't flying is parked: data.z is 0. On the very first frame a few lone ones
    // are already part-way through, so the sky doesn't start empty.
    particle.life = 1e6;
    particle.velocity = 0.0;
    particle.position = 0.0;
    particle.data = float4(-100.0, 0.0, 0.0, 0.0);
    if (meteor >= VolleyMeteors && meteorChance(meteor, 1.0) < 0.2)
    {
        particle.data = float4(Time - meteorChance(meteor, 2.0) * 3.0, meteor + 0.5, 1.0, 0.0);
    }
}

void Simulate(inout Particle particle, uint id)
{
    uint meteor = meteorOf(id);
    bool volley = meteor < VolleyMeteors;
    if (particle.data.z > 0.5)
    {
        // Flying: park it again once its train (and burst) have faded.
        if (Time - particle.data.x > flightTime(meteorShape(particle.data.y, volley), volley))
        {
            particle.data.z = 0.0;
        }
        return;
    }

    // Parked: wait for a turn.
    if (volley)
    {
        // Every fourth strong beat sends out the next volley, and a drop sends out half the volleys
        // at once (those that are free). The whole ring shares one seed, so its meteors match.
        uint group = meteor / VolleySlots;
        uint slot = meteor % VolleySlots;
        bool onBeat = Beat > 0.6 && Bass > 0.35 && ((uint)BeatCount % 4u) == 0u && group == ((uint)BeatCount / 4u) % Volleys;
        bool onDrop = DropPulse > 0.95 && group % 2u == 0u;
        if (onBeat || onDrop)
        {
            float seed = (float)(group * 7919u + (uint)BeatCount * 31u + (onDrop ? (uint)FrameCount % 997u : 0u)) + 0.5;
            if (slot < (uint)foldOf(seed))
            {
                particle.data = float4(Time, seed, 1.0, 0.0);
            }
        }
        return;
    }

    // Lone meteors: louder music launches them more often. The rate is shared out over however
    // many meteors this detail setting has, so High doesn't double it.
    float lone = max(ParticleCount / Trail - VolleyMeteors, 1.0);
    float perSecond = (0.015 + Level * 0.06 + Treble * 0.035) * 208.0 / lone;
    if (meteorChance(meteor, 3.0) < perSecond * min(DeltaTime, 0.1))
    {
        particle.data = float4(Time, (float)(meteor * 977u + (uint)FrameCount % 4096u) + 0.5, 1.0, 0.0);
    }
}

// A point of a volley meteor's burst, before it's turned and placed: a sphere of points, a torus
// knot, or a loxodrome (the spiral a ship sails holding one compass bearing), by the volley's seed.
float3 burstShape(uint index, float seed)
{
    float t = (index + 0.5) / BurstPoints;
    float pick = meteorRandom(seed, 12.0);
    if (pick < 0.34)
    {
        // Points laid out on the golden angle, like a dandelion clock.
        float z = 1.0 - 2.0 * t;
        float r = sqrt(max(1.0 - z * z, 0.0));
        float angle = index * 2.39996323;
        return float3(cos(angle) * r, sin(angle) * r, z);
    }
    if (pick < 0.67)
    {
        // A (2, 3), (2, 5) or (3, 4) torus knot.
        float which = meteorRandom(seed, 13.0);
        float p = which < 0.67 ? 2.0 : 3.0;
        float q = which < 0.34 ? 3.0 : which < 0.67 ? 5.0 : 4.0;
        float angle = t * 2.0 * PI;
        float ring = 0.65 + 0.35 * cos(q * angle);
        return float3(ring * cos(p * angle), ring * sin(p * angle), 0.35 * sin(q * angle));
    }
    // Pole to pole and back, winding round as it goes.
    float turns = meteorRandom(seed, 13.0) < 0.5 ? 4.0 : 6.0;
    float latitude = (abs(t * 2.0 - 1.0) * 2.0 - 1.0) * 1.45;
    float longitude = t * turns * 2.0 * PI;
    float r = cos(latitude);
    return float3(r * cos(longitude), r * sin(longitude), sin(latitude));
}

Sprite Show(Particle particle, uint id)
{
    Sprite sprite = (Sprite)0;
    if (particle.data.z < 0.5)
    {
        return sprite;
    }
    uint meteor = meteorOf(id);
    bool volley = meteor < VolleyMeteors;
    uint slot = meteor % VolleySlots;
    float seed = particle.data.y;
    float4 path = meteorPath(seed, volley, slot);
    float3 shape = meteorShape(seed, volley);
    float flown = Time - particle.data.x;
    uint local = id % Trail;
    float wake = 0.75 + Level * 0.5;

    if (volley && local >= Trail - BurstPoints)
    {
        // The burst: at the end of its path the meteor breaks into a small turning shape of
        // light. Each meteor of a ring turns its shape by its slot, so the ring stays symmetric.
        float since = flown - arrival(shape);
        if (since < 0.0)
        {
            return sprite;
        }
        uint index = local - (Trail - BurstPoints);
        float3 q = burstShape(index, seed);
        float spin = since * (0.6 + Mid * 0.6);
        // It turns mostly face on, rocking gently in depth, so knots and spirals stay readable.
        q.xz = rotate(q.xz, 0.45 * sin(spin * 0.6 + meteorRandom(seed, 14.0) * 6.0));
        q.yz = rotate(q.yz, 0.55 + 0.3 * sin(spin * 0.45));
        q.xy = rotate(q.xy, spin);
        float radius = lerp(0.11, 0.16, meteorRandom(seed, 15.0)) * (1.0 - exp(-since * 2.5)) * (1.0 + Beat * 0.1);
        float angle = atan2(path.w, path.z);
        float2 centre = path.xy + path.zw * shape.x + float2(0.004, 0.03 * since) * since;
        sprite.position = centre + rotate(q.xy, angle) * radius;
        float fade = exp(-since * 0.55) * saturate(since * 4.0);
        float3 tint = strandColour(seed, index % 3u);
        float depth = 0.55 + 0.45 * q.z;
        float sparkle = 1.0 + step(0.96, fixedRandom(id, 3.0)) * Treble * 6.0;
        sprite.colour = lerp(tint, float3(1.0, 1.0, 1.0), exp(-since * 3.0) * 0.6) * fade * depth * sparkle * 0.35 * wake;
        sprite.size = 1.6 + depth * 1.2;
        return sprite;
    }

    // The train.
    uint strands = strandsOf(volley);
    uint perStrand = (volley ? Trail - BurstPoints : Trail) / strands;
    uint strand = local % strands;
    float along = ((local / strands) + 0.5) / perStrand;
    // How long ago the burning head passed this particle's place on the path.
    float since = flown - along * arrival(shape);
    if (since < 0.0)
    {
        return sprite;
    }

    // The meteor flares up, burns brightest part-way along, and fizzles out.
    float burn = pow(sin(along * PI), 0.6) * (0.75 + 0.5 * meteorRandom(seed, 7.0));
    float3 tint = volley ? strandColour(seed, strand) : (strand == 0u ? meteorColour(seed) : float3(0.35, 1.0, 0.6));
    float head = exp(-since * 30.0);
    float glowing = exp(-since * 4.0);
    float train = exp(-since / shape.z);

    // The strands twist round the path. As the train ages the braid loosens and blooms outward,
    // and the strand behind the path shows dimmer than the one in front.
    float twists = lerp(2.5, 4.5, meteorRandom(seed, 9.0)) * shape.x / 0.4;
    float phase = along * twists * 2.0 * PI + strand * 2.0 * PI / strands + since * (0.8 + Mid * 1.2);
    float open = (0.004 + 0.045 * (1.0 - exp(-since * 0.6))) * pow(sin(along * PI), 0.5) * (volley ? 1.0 : 0.6);
    float front = 0.55 + 0.45 * cos(phase);

    float3 colour = lerp(tint, float3(1.0, 1.0, 1.0), head * 0.7) * head * (volley ? 0.35 : 0.45)
                  + tint * glowing * 0.35
                  + tint * train * (volley ? 0.24 : 0.12) * saturate(shape.z / 2.0);
    colour *= burn * front * wake;
    colour *= 1.0 + step(0.985, fixedRandom(id, 5.0)) * Treble * 4.0 * train;

    float2 place = path.xy + path.zw * along * shape.x;
    float2 side = float2(-path.w, path.z);
    place += side * sin(phase) * open;
    // The train is blown slowly sideways and stirred by winds high in the atmosphere.
    float2 wind = curlNoise2(place * 2.5 + seed * 0.01, since * 0.15) * 0.01 + float2(0.005, 0.002);
    place += wind * since;

    sprite.position = place;
    sprite.size = (volley ? 1.2 : 1.0) * (0.8 + front * 0.4) + head * 1.8 + since * 0.12;
    // Light from the fastest part streaks along the path.
    sprite.streak = path.zw * shape.y * 0.02 * head;
    sprite.colour = colour;
    return sprite;
}

// ---- The sky, the mountains and the lake ----

// Star trails: every star draws an arc round the pole as the sky wheels. Each ring of the sky
// is a band of the spectrum, bass nearest the pole and treble down by the mountains, and lights up
// in its own colour when that band is loud.
float3 starTrails(float2 p)
{
    const float spacing = 0.014;
    float2 d = p - Pole;
    float r = length(d);
    float ring = floor(r / spacing);
    float across = (r - (ring + 0.5) * spacing) / pixelSize();
    float onRing = exp(-across * across * 0.5);

    // Turn with the sky, so each star keeps its place in its ring.
    float angle = atan2(d.y, d.x) - Time * 0.012;
    float cellWidth = 0.32 / max(r, 0.2);
    float cell = floor(angle / cellWidth);
    float h = hash(float2(ring, cell));
    float arc = lerp(0.25, 0.85, hash(float2(cell, ring) + 4.7)) * cellWidth;
    float headAngle = (cell + 1.0) * cellWidth - hash(float2(ring, cell) + 9.1) * (cellWidth - arc);
    float t = (angle - (headAngle - arc)) / arc;
    float onArc = step(0.0, t) * step(t, 1.0);
    float tail = t * t * onArc;
    float star = exp(-pow((angle - headAngle) * r / pixelSize(), 2.0) * 0.15) * exp(-across * across * 0.3);
    float shown = step(0.68, h);
    float magnitude = pow((h - 0.68) / 0.32, 2.0) * shown;

    float band = saturate((r - 0.45) / 2.0);
    float loud = spectrum(band);
    float3 temperature = lerp(float3(0.7, 0.8, 1.0), float3(1.0, 0.85, 0.65), hash(float2(ring, cell) + 1.7));
    float3 bandColour = hsv2rgb(0.78 - band * 0.68, 0.75, 1.0);
    float3 light = temperature * (0.05 + magnitude * 0.35) + bandColour * loud * loud * 0.8;
    return light * (tail * onRing + star * 0.8) * shown;
}

// The meteors' radiant: a faint turning star of light that swells on the beat.
float3 radiantGlow(float2 p)
{
    float2 d = p - radiant();
    float r = length(d);
    float3 colour = float3(0.5, 0.6, 1.0) * exp(-r * 18.0) * (0.06 + Beat * 0.08);
    float2 k = kaleidoscope(rotate(d, Time * 0.05), 6.0);
    float star = abs(polygon(rotate(d, -Time * 0.08), 6.0, 0.045));
    star = min(star, abs(polygon(rotate(d, Time * 0.06), 3.0, 0.03)));
    star = min(star, abs(k.y) + max(r - 0.075, 0.0) * 4.0);
    colour += float3(0.55, 0.65, 1.0) * glow(star / pixelSize(), 0.7) * exp(-r * 12.0) * (0.12 + Beat * Bass * 0.25);
    return colour;
}

// A range of mountains with straight-edged, crystal-sharp peaks: the height of its ridge
// (p units, y down) at x.
float faceted(float x, float layer)
{
    float height = 0.0;
    float amplitude = 1.0;
    float spacing = layer < 0.5 ? 0.24 : 0.36;
    [unroll]
    for (int octave = 0; octave < 3; octave++)
    {
        float cell = floor(x / spacing);
        float f = x / spacing - cell;
        float a = hash(float2(cell, layer * 7.3 + octave * 19.0));
        float b = hash(float2(cell + 1.0, layer * 7.3 + octave * 19.0));
        height += lerp(a, b, f) * amplitude;
        amplitude *= 0.35;
        spacing *= 0.4;
    }
    float top = layer < 0.5 ? Horizon - 0.06 : Horizon - 0.015;
    return top - height * (layer < 0.5 ? 0.2 : 0.11);
}

// Everything above the water at p: sky, star trails, meteors and mountains.
float3 above(float2 p)
{
    float altitude = saturate((Horizon - p.y) / 1.5);
    float3 colour = lerp(float3(0.03, 0.035, 0.07), float3(0.004, 0.006, 0.018), pow(altitude, 0.6));
    // A faint green airglow along the horizon.
    colour += float3(0.015, 0.045, 0.035) * exp(-max(Horizon - p.y, 0.0) * 5.0);
    colour += starTrails(p);
    colour += radiantGlow(p);
    colour += particles(toUv(p));

    // Two ranges of mountains, the far one paler with distance. Their ridges carry a thin line of
    // light, brightest where the music is loudest: bass in the middle, treble at the sides.
    float ridgeBand = spectrum(saturate(abs(p.x) / max(Aspect, 1.0)));
    [unroll]
    for (int layer = 0; layer < 2; layer++)
    {
        float depth = layer;
        float x = p.x + sin(Time * 0.021) * 0.06 * (1.0 + depth);
        float top = faceted(x, depth);
        float cover = smoothstep(-pixelSize(), pixelSize(), p.y - top);
        float below = max(p.y - top, 0.0);
        float3 rock = lerp(float3(0.02, 0.025, 0.05), float3(0.008, 0.01, 0.02), depth);
        rock *= 1.0 + exp(-below * 10.0) * 0.8;
        float3 rimColour = hsv2rgb(0.62 - ridgeBand * 0.25 + depth * 0.08, 0.6, 1.0);
        rock += rimColour * exp(-below / (pixelSize() * 1.5)) * (0.06 + ridgeBand * ridgeBand * 0.5) * (1.0 - depth * 0.3);
        // Mist where the far range meets the water.
        rock += float3(0.02, 0.03, 0.05) * exp(-(Horizon - p.y) * 30.0) * (1.0 - depth * 0.5);
        colour = lerp(colour, rock, cover);
    }
    return colour;
}

float3 Render(float2 uv, float2 p)
{
    if (p.y < Horizon)
    {
        return above(p);
    }

    // The lake mirrors everything above it, broken up by ripples that the bass stirs.
    float depth = p.y - Horizon;
    float ripple = noise(float2(p.x * 2.0, depth * 60.0 / (depth + 0.05) - Time * 0.8)) - 0.5;
    float swell = noise(float2(p.x * 6.0 + Time * 0.3, depth * 25.0)) - 0.5;
    float2 mirrored = float2(p.x + ripple * depth * (0.05 + Bass * 0.06), Horizon - depth + swell * depth * 0.03);
    float3 colour = above(mirrored) * lerp(0.6, 0.35, saturate(depth * 2.0));
    colour += float3(0.004, 0.007, 0.014);
    // A dark line where the shore meets the water.
    colour *= 0.4 + 0.6 * smoothstep(0.0, pixelSize() * 3.0, depth);
    return colour;
}
