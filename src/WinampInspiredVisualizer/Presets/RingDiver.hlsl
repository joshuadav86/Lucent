// Name: Ring Diver
// Skim just above the rings of a giant striped planet as countless icy grains and boulders rush past, the nearest ones blurred and streaking by; the ring ripples with the bass, the rush quickens with the music and ice glints with the treble.
// Family: Particles
// Tags: Space, Calm, 3D
// Particles: 262144

// The ring is a thin sheet on world y = 0, with y up. We fly along +z through a box of ring particles
// that travels with us: they stream past towards -z and wrap round to the far end of the box once
// they're behind us. Beyond the box the ring carries on as a smooth sheet, drawn in Render.
static const float3 BoxSize = float3(20.0, 1.0, 36.0);
static const float RingThickness = 0.16;
static const float3 PlanetCentre = float3(-170.0, 0.0, 115.0);
static const float PlanetRadius = 100.0;
static const float RingInner = 128.0;
static const float RingOuter = 238.0;
static const float Zoom = 1.55;

float3 sunDirection()
{
    return normalize(float3(0.55, 0.4, 0.75));
}

// The camera sways gently from side to side and up and down as it skims the ring.
float3 cameraEye()
{
    return float3(sin(Time * 0.043) * 1.6, 0.36 + 0.2 * sin(Time * 0.061), 0.0);
}

float3 cameraTarget(float3 eye)
{
    return eye + float3(-0.42 + 0.12 * sin(Time * 0.027), 0.2 + 0.06 * sin(Time * 0.033), 1.0);
}

float cameraRoll()
{
    return sin(Time * 0.035) * 0.07 - 0.04;
}

// How thick the ring is at a distance r from the planet's centre: many fine bands, a dark gap
// like Saturn's Cassini division, and soft inner and outer edges. 0 to about 1.
float ringDensity(float r)
{
    float x = r * 0.35;
    float bands = noise(float2(x, 1.7)) * 0.5 + noise(float2(x * 3.1, 4.2)) * 0.3 + noise(float2(x * 11.0, 7.9)) * 0.2;
    float d = 0.25 + bands * 0.95;
    d *= 0.7 + 0.3 * sin(r * 0.21);
    d *= lerp(0.08, 1.0, smoothstep(2.5, 0.0, abs(r - 186.0) - 3.5));
    d *= smoothstep(RingInner, RingInner + 12.0, r) * smoothstep(RingOuter, RingOuter - 8.0, r);
    return saturate(d);
}

float ringRadiusAt(float3 position)
{
    return length(position.xz - PlanetCentre.xz);
}

// The ring bends in slow waves that swell with the bass, and every beat sends a ripple out from ahead of us.
float ringWave(float x, float z)
{
    float along = z + Time * 2.6;
    float swell = (0.04 + Bass * 0.16) * sin(x * 0.42 + along * 0.16 - Time * 0.7);
    swell += 0.03 * sin(x * 1.1 - along * 0.31 + Time * 0.5) * (0.5 + Mid);
    float age = -log(max(Beat, 1e-4)) / 5.0;
    float spread = length(float2(x - 1.5, z - 14.0));
    float ripple = 0.14 * Beat * sin(spread * 1.3 - age * 9.0) * exp(-abs(spread - age * 9.0) * 0.35);
    return swell + ripple;
}

// Where a y-up world point lands on screen (project() works with y growing downward).
float3 seenFrom(float3 position, float3 eye, float3 target)
{
    float3 flip = float3(1.0, -1.0, 1.0);
    return project(position * flip, eye * flip, target * flip, Zoom, cameraRoll());
}

void Spawn(inout Particle particle, uint id)
{
    float3 eye = cameraEye();
    // Heights cluster near the middle of the sheet, a few stray further out.
    float g = (random(id, 1.0) + random(id, 2.0) + random(id, 3.0) - 1.5) * 0.9;
    particle.data = float4(g * RingThickness, 0.0, 0.0, 0.0);
    particle.position = float3(eye.x + (random(id, 4.0) - 0.5) * BoxSize.x, particle.data.x, random(id, 5.0) * BoxSize.z);
    particle.position.y += ringWave(particle.position.x, particle.position.z);
    particle.velocity = 0.0;
    particle.life = 1e6;
}

void Simulate(inout Particle particle, uint id)
{
    float3 eye = cameraEye();
    float dt = min(DeltaTime, 0.05);
    float speed = 2.4 + Level * 1.2 + Bass * 1.4 + Beat * 0.8;
    float3 moved = particle.position;
    moved.z -= speed * dt;
    // A very slow sideways shear, as inner orbits slightly outrun outer ones.
    moved.x += (moved.x - eye.x) * 0.004 * dt;

    if (moved.z < -1.5)
    {
        moved.z += BoxSize.z;
        moved.x = eye.x + (random(id, 6.0) - 0.5) * BoxSize.x;
    }
    float side = moved.x - eye.x;
    moved.x = eye.x + side - BoxSize.x * floor(side / BoxSize.x + 0.5);
    moved.y = particle.data.x + ringWave(moved.x, moved.z);

    particle.velocity = (moved - particle.position) / max(dt, 1e-4);
    // Wrapping round would make a huge jump; keep the streak to the real flight.
    particle.velocity = float3(0.0, clamp(particle.velocity.y, -2.0, 2.0), -speed);
    particle.position = moved;
}

Sprite Show(Particle particle, uint id)
{
    float3 eye = cameraEye();
    float3 target = cameraTarget(eye);
    float3 seen = seenFrom(particle.position, eye, target);
    float3 seenBefore = seenFrom(particle.position - particle.velocity * 0.022, eye, target);

    Sprite sprite = (Sprite)0;
    if (seen.z < 0.12)
    {
        return sprite;
    }

    // Most of the ring is fine ice dust; now and then a pebble, and rarely a proper boulder.
    float bigness = fixedRandom(id, 7.0);
    float radius = lerp(0.0025, 0.07, pow(bigness, 14.0));
    float pixels = radius * Zoom / seen.z / spritePixel();

    // Lit by the sun: bright ice with tan and rusty tints, a touch brighter seen against the light.
    float tone = fixedRandom(id, 8.0);
    float3 albedo = lerp(float3(1.0, 0.94, 0.86), float3(0.86, 0.72, 0.56), tone * tone);
    albedo = lerp(albedo, float3(0.8, 0.88, 1.0), step(0.85, tone) * 0.6);
    float r = ringRadiusAt(particle.position + float3(0.0, 0.0, Time * 2.6));
    float band = 0.35 + ringDensity(r) * 0.9;
    float3 view = normalize(particle.position - eye);
    float phase = 0.55 + 0.45 * pow(saturate(dot(view, sunDirection()) * 0.5 + 0.5), 3.0);
    // Grains low in the sheet sit a little in the shade of those above them.
    float shade = lerp(0.55, 1.0, saturate((particle.data.x / RingThickness) * 0.5 + 0.7));
    float3 colour = albedo * band * phase * shade * 0.32 * (1.0 + Level * 0.25);

    // Tumbling chunks of ice flash as their faces catch the sun, more so with the treble.
    float flash = pow(saturate(sin(Time * (3.0 + fixedRandom(id, 9.0) * 6.0) + fixedRandom(id, 10.0) * 40.0)), 16.0);
    colour *= 1.0 + step(0.97, fixedRandom(id, 11.0)) * flash * (1.0 + Treble * 6.0);

    // Fade in at the far end of the box (where the sheet in Render takes over) and very close up.
    float fade = smoothstep(BoxSize.z - 1.5, BoxSize.z - 9.0, seen.z) * smoothstep(0.12, 0.45, seen.z);

    sprite.position = seen.xy;
    sprite.streak = seen.xy - seenBefore.xy;
    sprite.size = clamp(pixels, 0.5, 30.0);
    sprite.colour = colour * fade;
    focus(sprite, seen.z, 3.0, 0.007);
    return sprite;
}

// ---- The planet, the far ring and the stars ----

float sphereHit(float3 origin, float3 direction, float3 centre, float radius)
{
    float3 oc = origin - centre;
    float b = dot(oc, direction);
    float h = b * b - dot(oc, oc) + radius * radius;
    if (h < 0.0)
    {
        return -1.0;
    }
    return -b - sqrt(h);
}

float3 starfield(float3 direction)
{
    float2 cell = float2(atan2(direction.z, direction.x) * 120.0, direction.y * 120.0);
    float2 id = floor(cell);
    float2 inCell = frac(cell) - 0.5;
    float pick = hash(id);
    float2 spot = (float2(hash(id + 3.1), hash(id + 7.7)) - 0.5) * 0.7;
    float star = step(0.965, pick) * exp(-dot(inCell - spot, inCell - spot) * 90.0);
    float3 tint = lerp(float3(0.7, 0.8, 1.0), float3(1.0, 0.85, 0.7), hash(id + 1.3));
    float milky = noise(cell * 0.02 + 4.0) * noise(cell * 0.05);
    return tint * star * (0.3 + pick * 0.9) + float3(0.012, 0.012, 0.022) * milky;
}

// Bands of cream, gold and tan swirling round the planet, paler towards the poles.
float3 planetColour(float3 normal)
{
    float latitude = normal.y;
    float longitude = atan2(normal.z, normal.x) + Time * 0.004;
    float2 swirl = float2(longitude * 3.0, latitude * 9.0);
    float turbulence = fbm(swirl + float2(Time * 0.01, 0.0)) - 0.5;
    float bandCoord = latitude * 14.0 + turbulence * 0.9;
    float b = noise(float2(bandCoord, 0.5)) * 0.6 + noise(float2(bandCoord * 2.7, 3.3)) * 0.4;
    float3 colour = lerp(float3(0.78, 0.6, 0.38), float3(0.98, 0.88, 0.68), b);
    colour = lerp(colour, float3(0.62, 0.5, 0.36), smoothstep(0.55, 0.8, noise(float2(bandCoord * 5.0, 9.0))) * 0.4);
    colour = lerp(colour, float3(0.55, 0.62, 0.7), smoothstep(0.65, 0.95, abs(latitude)));
    return colour;
}

float4 farRing(float3 origin, float3 direction, out float distance)
{
    distance = -1.0;
    if (abs(direction.y) < 1e-5)
    {
        return 0.0;
    }
    float t = -origin.y / direction.y;
    if (t <= 0.0)
    {
        return 0.0;
    }
    float3 position = origin + direction * t;
    // The sheet scrolls past as we fly, matching the particles.
    float r = ringRadiusAt(position + float3(0.0, 0.0, Time * 2.6));
    float density = ringDensity(r);
    // In the planet's shadow?
    float3 sun = sunDirection();
    float shadowT = sphereHit(position, sun, PlanetCentre, PlanetRadius);
    float lit = shadowT > 0.0 ? 0.08 : 1.0;
    float3 tint = lerp(float3(0.92, 0.84, 0.72), float3(0.78, 0.8, 0.84), noise(float2(r * 0.08, 2.0)));
    float3 colour = tint * (0.07 + 0.55 * lit) * (0.5 + density);
    // Near the camera the particles take over from the smooth sheet.
    // Close up only a dim haze of fine dust remains between them.
    float closeUp = lerp(0.6, 1.0, smoothstep(BoxSize.z * 0.55, BoxSize.z * 1.05, t));
    colour *= lerp(0.35, 1.0, smoothstep(BoxSize.z * 0.4, BoxSize.z * 1.05, t));
    distance = t;
    return float4(colour, saturate(density * 1.25) * closeUp);
}

float3 Render(float2 uv, float2 p)
{
    float3 eye = cameraEye();
    float3 target = cameraTarget(eye);
    float3 direction = cameraRay(eye, target, float2(p.x, -p.y), Zoom, -cameraRoll());
    float3 sun = sunDirection();

    float3 colour = starfield(direction);
    // The distant sun: a small fierce disc with a soft halo.
    float toSun = saturate(dot(direction, sun));
    colour += float3(1.0, 0.92, 0.8) * (smoothstep(0.99985, 0.99995, toSun) * 30.0 + pow(toSun, 600.0) * 2.0 + pow(toSun, 20.0) * 0.08);

    float planetT = sphereHit(eye, direction, PlanetCentre, PlanetRadius);
    if (planetT > 0.0)
    {
        float3 position = eye + direction * planetT;
        float3 normal = (position - PlanetCentre) / PlanetRadius;
        float light = dot(normal, sun);
        float day = smoothstep(-0.08, 0.3, light) * (0.35 + 0.65 * saturate(light));
        // The rings throw striped shadows across the planet.
        float ringShade = 1.0;
        if (sun.y * normal.y < 0.0)
        {
            float toRing = -position.y / sun.y;
            float r = ringRadiusAt(position + sun * toRing);
            ringShade = 1.0 - ringDensity(r) * 0.85;
        }
        float facing = saturate(dot(normal, -direction));
        float3 surface = planetColour(normal) * day * ringShade * 0.85 * pow(facing, 0.3);
        // Light bounced up from the bright rings faintly lights the night side.
        surface += planetColour(normal) * float3(0.05, 0.045, 0.04) * saturate(-normal.y * 0.5 + 0.5) * (1.0 - day);
        // A thin hazy rim on the sunlit edge.
        surface += float3(0.55, 0.65, 0.9) * pow(1.0 - facing, 4.0) * saturate(light + 0.2) * 0.6;
        colour = surface;
    }
    else
    {
        // Faint haze just outside the planet's edge.
        float3 oc = eye - PlanetCentre;
        float b = dot(oc, direction);
        float closest = sqrt(max(dot(oc, oc) - b * b, 0.0));
        float edge = exp(-max(closest - PlanetRadius, 0.0) * 0.35);
        float3 edgePoint = normalize(eye + direction * -b - PlanetCentre);
        colour += float3(0.45, 0.6, 1.0) * edge * saturate(dot(edgePoint, sun) + 0.3) * 0.35;
    }

    float ringDistance;
    float4 ring = farRing(eye, direction, ringDistance);
    if (ringDistance > 0.0 && (planetT < 0.0 || ringDistance < planetT))
    {
        colour = lerp(colour, ring.rgb * (1.0 + Bass * 0.15), ring.a);
    }

    // A whisper of the last frame keeps fast streaks smooth without smearing the scene.
    return colour + particles(uv) + previous(uv) * 0.25;
}
