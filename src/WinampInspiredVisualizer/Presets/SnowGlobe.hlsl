// Name: Snow Globe
// A glass snow globe holds a tiny winter village with glowing windows; the beat shakes it so the snow whirls up around the houses and slowly settles again, all seen through the curving glass.
// Family: Ray traced
// Tags: Calm, 3D
// Particles: 120000

// The globe: a water-filled glass ball on a wooden base. Everything inside is in the globe's own
// coordinates, centred on the middle of the ball; y is up.
static const float3 GlobeCentre = float3(0.0, 1.0, 0.0);
static const float GlobeRadius = 1.0;
static const float InnerRadius = 0.93;   // how far the snow can reach inside the glass
static const float GroundHeight = -0.42; // the snowy ground inside, below the centre
static const float Water = 1.33;
static const float Zoom = 3.0;

float3 cameraEye()
{
    float orbit = sin(Time * 0.05) * 0.6;
    return float3(sin(orbit) * 4.3, 1.75 + sin(Time * 0.07) * 0.2, -cos(orbit) * 4.3);
}

float3 cameraTarget()
{
    return float3(0.0, 0.82, 0.0);
}

// Every sixteenth beat gives the globe a proper shake; the beats in between just nudge the water.
float shakeStrength()
{
    return wrap(BeatCount, 16.0) < 0.5 ? 1.0 : 0.12 + Bass * 0.25;
}

// ---- Snow ----

void Spawn(inout Particle particle, uint id)
{
    // Most of the snow starts lying on the ground; some is still drifting down.
    float angle = random(id, 1.0) * 2.0 * PI;
    float radius = sqrt(random(id, 2.0)) * 0.85;
    float lying = random(id, 3.0) < 0.6 ? 1.0 : 0.0;
    float height = lying > 0.5 ? GroundHeight : GroundHeight + random(id, 4.0) * 1.2;
    particle.position = float3(cos(angle) * radius, height, sin(angle) * radius);
    float reach = sqrt(max(InnerRadius * InnerRadius - height * height, 0.0));
    particle.position.xz *= reach / max(0.85, 1e-3);
    particle.velocity = 0.0;
    particle.life = 1e6;
    particle.data = float4(0.0, 0.0, 0.0, BeatCount);
}

void Simulate(inout Particle particle, uint id)
{
    float dt = min(DeltaTime, 0.05);
    float3 position = particle.position;

    // A shake: the water spins round and lifts, flinging the snow up off the ground.
    if (Beat > 0.85 && particle.data.w != BeatCount)
    {
        particle.data.w = BeatCount;
        float strength = shakeStrength() * (0.6 + 0.8 * fixedRandom(id, 5.0));
        float3 spin = float3(-position.z, 0.0, position.x) * 2.2;
        float3 lift = float3(0.0, 1.4 + (GroundHeight + 0.1 - position.y) * 1.5, 0.0);
        particle.velocity += (spin + lift * saturate(1.2 - length(position.xz)) + randomDirection(id, 6.0) * 0.9) * strength;
    }

    // Swirling water slows the snow; flakes drift down slowly, each at its own pace, tumbling in the eddies.
    float3 eddies = curlNoise(position * 2.2 + float3(0.0, Time * 0.1, 0.0)) * (0.04 + length(particle.velocity) * 0.5);
    float3 settle = float3(0.0, -(0.05 + fixedRandom(id, 7.0) * 0.06), 0.0);
    particle.velocity = lerp(particle.velocity, settle, 1.0 - exp(-dt * 1.1)) + eddies * dt;
    position += particle.velocity * dt;

    // Keep it inside the glass, bouncing gently off the wall.
    float reach = length(position);
    if (reach > InnerRadius)
    {
        float3 outward = position / reach;
        position = outward * InnerRadius;
        particle.velocity -= outward * max(dot(particle.velocity, outward), 0.0) * 1.5;
    }
    // Flakes that reach the ground lie there until the next shake.
    if (position.y < GroundHeight)
    {
        position.y = GroundHeight;
        particle.velocity = 0.0;
    }
    particle.position = position;
}

// Where something inside the globe appears: the water and glass act as a lens, magnifying the
// inside a little about the middle, so the snow is placed to match the refracted village.
Sprite Show(Particle particle, uint id)
{
    Sprite sprite = (Sprite)0;
    float3 eye = cameraEye();
    float3 target = cameraTarget();
    float3 centre = project(GlobeCentre, eye, target, Zoom, 0.0);
    float3 seen = project(GlobeCentre + particle.position, eye, target, Zoom, 0.0);
    if (seen.z < 0.3)
    {
        return sprite;
    }
    float2 onScreen = centre.xy + (seen.xy - centre.xy) * 1.22;
    sprite.position = float2(onScreen.x, -onScreen.y);
    // Flakes lying on the ground are packed close, so they're drawn fainter.
    bool lying = particle.position.y <= GroundHeight + 1e-3;
    float moving = saturate(length(particle.velocity) * 4.0);
    // Most of it is fine glittery snow; one in seven is a big soft flake.
    bool big = fixedRandom(id, 10.0) > 0.86;
    sprite.size = big ? 2.2 + fixedRandom(id, 8.0) * 1.2 : 0.9 + fixedRandom(id, 8.0) * 0.5;
    float3 tint = lerp(float3(0.75, 0.82, 1.0), float3(1.0, 0.85, 0.65), fixedRandom(id, 9.0) * 0.5);
    sprite.colour = tint * (lying ? 0.02 : (big ? 0.3 : 0.045) + moving * 0.05) * (0.8 + Treble * 0.4);
    // Nearer flakes are a touch out of focus.
    focus(sprite, seen.z, length(eye - GlobeCentre), 0.05);
    return sprite;
}

// ---- The village inside ----

float boxDistance(float3 p, float3 size)
{
    float3 q = abs(p) - size;
    return length(max(q, 0.0)) + min(max(q.x, max(q.y, q.z)), 0.0);
}

// A little house: a box with a pitched roof, turned to face a way.
float houseDistance(float3 p, float3 at, float turn, float3 size)
{
    float3 q = p - at;
    q.xz = rotate(q.xz, turn);
    float walls = boxDistance(q - float3(0.0, size.y, 0.0), size);
    // The roof: a slab cut by two sloping planes.
    float3 r = q - float3(0.0, size.y * 2.0, 0.0);
    float slope = max(abs(r.z) * 0.8 + r.y * 0.6 - size.z * 0.8, -r.y);
    float roof = max(slope, abs(r.x) - size.x - 0.012);
    return min(walls, roof);
}

float treeDistance(float3 p, float3 at, float height)
{
    float3 q = p - at;
    // Three stacked cones of snowy fir.
    float d = 1e3;
    [unroll]
    for (int i = 0; i < 3; i++)
    {
        float base = height * (0.15 + i * 0.25);
        float tier = height * (0.45 - i * 0.08);
        float y = q.y - base;
        float r = length(q.xz);
        float width = tier * 0.55 * saturate(1.0 - y / tier);
        float cone = max(r - width, max(-y, y - tier)) * 0.8;
        d = min(d, cone);
    }
    float trunk = max(length(q.xz) - 0.012, abs(q.y - height * 0.08) - height * 0.08);
    return min(d, trunk);
}

static const float3 HousePlaces[3] = { float3(-0.32, 0.0, 0.08), float3(0.22, 0.0, -0.18), float3(0.06, 0.0, 0.34) };
static const float HouseTurns[3] = { 0.4, -0.7, 1.9 };
static const float3 HouseSizes[3] = { float3(0.13, 0.08, 0.1), float3(0.11, 0.07, 0.09), float3(0.09, 0.06, 0.08) };

float groundHeight(float2 xz)
{
    return GroundHeight + 0.03 * noise(xz * 6.0) + 0.04 * exp(-dot(xz, xz) * 3.0);
}

// Distance to the village, and which part was nearest: 0 snow, 1 house, 2 tree.
float village(float3 p, out int part)
{
    float ground = (p.y - groundHeight(p.xz)) * 0.8;
    part = 0;
    float d = ground;
    [unroll]
    for (int i = 0; i < 3; i++)
    {
        float3 at = HousePlaces[i];
        at.y = groundHeight(at.xz) - 0.01;
        float house = houseDistance(p, at, HouseTurns[i], HouseSizes[i]);
        if (house < d)
        {
            d = house;
            part = 1;
        }
    }
    float3 treeA = float3(0.42, 0.0, 0.2);
    float3 treeB = float3(-0.12, 0.0, -0.4);
    float3 treeC = float3(-0.45, 0.0, -0.18);
    treeA.y = groundHeight(treeA.xz);
    treeB.y = groundHeight(treeB.xz);
    treeC.y = groundHeight(treeC.xz);
    float trees = min(treeDistance(p, treeA, 0.42), min(treeDistance(p, treeB, 0.55), treeDistance(p, treeC, 0.32)));
    if (trees < d)
    {
        d = trees;
        part = 2;
    }
    return d;
}

float3 villageNormal(float3 p)
{
    int part;
    const float2 k = float2(1.0, -1.0) * 0.0015;
    return normalize(k.xyy * village(p + k.xyy, part) + k.yyx * village(p + k.yyx, part)
                   + k.yxy * village(p + k.yxy, part) + k.xxx * village(p + k.xxx, part));
}

// How brightly the cottage windows glow: warm, a little uneven, swelling with the music.
float windowGlow()
{
    return 1.4 + Level * 0.8 + sin(Time * 1.3) * 0.08;
}

float3 shadeVillage(float3 p, float3 direction, int part)
{
    float3 normal = villageNormal(p);
    // Cool moonlight from above, and warm light spilling from the houses onto the snow.
    float3 light = float3(0.35, 0.42, 0.65) * (0.35 + 0.65 * saturate(dot(normal, normalize(float3(0.3, 1.0, -0.4)))));
    [unroll]
    for (int i = 0; i < 3; i++)
    {
        float3 at = HousePlaces[i] + float3(0.0, groundHeight(HousePlaces[i].xz) + 0.07, 0.0);
        float3 toHouse = at - p;
        float d2 = dot(toHouse, toHouse);
        light += float3(1.0, 0.6, 0.25) * windowGlow() * 0.012 / (d2 + 0.004) * saturate(dot(normal, toHouse * rsqrt(d2)) * 0.5 + 0.5);
    }
    float3 albedo = float3(0.85, 0.88, 0.95);
    float3 glow = 0.0;
    if (part == 1)
    {
        // Snow on the roof, painted timber walls with glowing windows.
        bool roof = normal.y > 0.35;
        int nearest = 0;
        float best = 1e3;
        [unroll]
        for (int i = 0; i < 3; i++)
        {
            float d = length(p.xz - HousePlaces[i].xz);
            if (d < best)
            {
                best = d;
                nearest = i;
            }
        }
        float3 q = p - float3(HousePlaces[nearest].x, groundHeight(HousePlaces[nearest].xz) - 0.01, HousePlaces[nearest].z);
        q.xz = rotate(q.xz, HouseTurns[nearest]);
        float3 wallColours[3] = { float3(0.5, 0.12, 0.08), float3(0.25, 0.3, 0.45), float3(0.55, 0.45, 0.3) };
        albedo = roof ? float3(0.9, 0.92, 1.0) : wallColours[nearest];
        float along = abs(normal.x) > abs(normal.z) ? q.z : q.x;
        float2 pane = float2(frac(along * 18.0 + 0.5), q.y * 18.0);
        float window = step(0.3, pane.x) * step(pane.x, 0.7) * step(0.6, pane.y) * step(pane.y, 1.6) * (roof ? 0.0 : 1.0);
        glow = float3(1.0, 0.62, 0.25) * window * windowGlow() * 1.6;
    }
    else if (part == 2)
    {
        // Dark green fir dusted with snow on its upper sides.
        albedo = lerp(float3(0.03, 0.12, 0.07), float3(0.85, 0.9, 0.95), smoothstep(0.3, 0.8, normal.y));
    }
    return albedo * light + glow;
}

// ---- The room around it ----

// A dark, cosy room: the warm glow of a fire off to one side and out-of-focus fairy lights behind.
float3 room(float3 direction)
{
    float3 colour = lerp(float3(0.025, 0.016, 0.014), float3(0.07, 0.035, 0.02), saturate(0.5 - direction.x * 0.5));
    colour += float3(0.35, 0.14, 0.04) * exp(-length(direction - normalize(float3(-1.0, -0.1, 0.4))) * 2.5) * (0.8 + Bass * 0.3);
    // Bokeh: soft discs of fairy lights strung across the far wall.
    float2 wall = direction.xy / max(direction.z, 0.2) * 6.0;
    float2 cell = floor(wall);
    float2 inCell = frac(wall) - 0.5;
    float2 jitter = float2(hash(cell), hash(cell + 7.1)) - 0.5;
    float disc = smoothstep(0.32, 0.27, length(inCell - jitter * 0.35));
    float sag = abs(wall.y - 0.75 - 0.4 * cos(wall.x * 0.45));
    float garland = step(0.0, direction.z) * smoothstep(1.5, 0.3, sag);
    float3 bulb = lerp(float3(1.0, 0.7, 0.35), float3(1.0, 0.45, 0.25), hash(cell + 3.0));
    float twinkle = 0.6 + 0.4 * sin(Time * (1.0 + hash(cell + 9.0) * 2.0) + hash(cell) * 6.0) + Beat * 0.3;
    colour += bulb * disc * garland * twinkle * 0.35 * step(0.4, hash(cell + 1.3));
    // A tall window behind the camera, seen only in reflections on the glass.
    float2 pane = abs(float2(direction.x + 0.35, direction.y - 0.35)) - float2(0.22, 0.32);
    colour += float3(0.6, 0.7, 1.0) * 1.2 * step(direction.z, -0.4) * smoothstep(0.02, -0.02, max(pane.x, pane.y));
    return colour;
}

float sphereEntry(float3 origin, float3 direction, float3 centre, float radius, out float leave)
{
    float3 oc = origin - centre;
    float b = dot(oc, direction);
    float h = b * b - dot(oc, oc) + radius * radius;
    leave = -1.0;
    if (h < 0.0)
    {
        return -1.0;
    }
    h = sqrt(h);
    leave = -b + h;
    return -b - h;
}

float3 Render(float2 uv, float2 p)
{
    float3 eye = cameraEye();
    float3 target = cameraTarget();
    float3 direction = cameraRay(eye, target, float2(p.x, -p.y), Zoom, 0.0);
    float3 colour = room(direction);

    // The table: dark polished wood, lit by the fire and the globe's own glow.
    const float tableHeight = -0.32;
    float tableDistance = 1e4;
    if (direction.y < 0.0)
    {
        tableDistance = (tableHeight - eye.y) / direction.y;
        float3 table = eye + direction * tableDistance;
        float grain = 0.75 + 0.25 * noise(float2(table.x * 1.5, table.z * 25.0));
        float glow = exp(-dot(table.xz, table.xz) * 0.8);
        float firelight = saturate(0.5 - table.x * 0.25) * exp(-length(table.xz) * 0.15);
        colour = float3(0.06, 0.03, 0.018) * grain * (0.25 + glow * windowGlow() * 0.5 + firelight * 1.2)
               + room(reflect(direction, float3(0.0, 1.0, 0.0))) * (0.06 + 0.3 * pow(1.0 + direction.y, 6.0));
        colour = lerp(room(direction) * 0.6, colour, exp(-tableDistance * 0.05));
    }

    // The base: a turned wooden plinth.
    float plinthTop = 0.12;
    {
        float3 o = eye;
        float a = dot(direction.xz, direction.xz);
        float b = dot(o.xz, direction.xz);
        float c = dot(o.xz, o.xz) - 0.86 * 0.86;
        float h = b * b - a * c;
        if (h > 0.0)
        {
            float t = (-b - sqrt(h)) / a;
            float y = o.y + direction.y * t;
            if (y > tableHeight && y < plinthTop && t < tableDistance)
            {
                tableDistance = t;
                float3 at = o + direction * t;
                float3 normal = normalize(float3(at.x, 0.0, at.z));
                float rings = 0.8 + 0.2 * sin(y * 120.0 + noise(at.xz * 6.0) * 4.0);
                float lit = 0.25 + 0.75 * saturate(dot(normal, normalize(float3(-1.0, 0.2, -0.6))));
                colour = float3(0.16, 0.07, 0.03) * rings * lit * 0.6 + room(reflect(direction, normal)) * 0.15;
                // A brass band round the top.
                colour = lerp(colour, float3(0.6, 0.42, 0.15) * (0.2 + lit) + room(reflect(direction, normal)) * 0.6, smoothstep(0.05, 0.06, y) * step(y, 0.1));
            }
        }
    }

    float leave;
    float t = sphereEntry(eye, direction, GlobeCentre, GlobeRadius, leave);
    if (t > 0.0 && t < tableDistance)
    {
        float3 surface = eye + direction * t;
        float3 normal = (surface - GlobeCentre) / GlobeRadius;
        float fresnel = 0.04 + 0.96 * pow(1.0 - saturate(dot(normal, -direction)), 5.0);

        // Into the water: march the village along the bent ray.
        float3 inside = refract(direction, normal, 1.0 / Water);
        float3 start = surface - GlobeCentre;
        float chord = -2.0 * dot(start, inside);
        float3 seen = 0.0;
        float travelled = 0.0;
        bool hit = false;
        int part = 0;
        int steps = qualitySteps(72);
        [loop]
        for (int s = 0; s < steps; s++)
        {
            float d = village(start + inside * travelled, part);
            if (d < 0.0008 * surfacePrecision())
            {
                hit = true;
                break;
            }
            travelled += d;
            if (travelled > chord)
            {
                break;
            }
        }
        if (hit)
        {
            seen = shadeVillage(start + inside * travelled, inside, part);
        }
        else
        {
            // Out through the back of the glass to the room behind.
            float3 back = start + inside * chord;
            float3 escaped = refract(inside, -normalize(back), Water);
            seen = room(dot(escaped, escaped) > 0.0 ? escaped : reflect(inside, -normalize(back))) * 0.9;
        }
        // The water tints faintly blue with depth, and the snow drifts in front of it all.
        seen *= exp(-travelled * float3(0.12, 0.06, 0.03));
        seen += particles(uv);
        colour = seen * (1.0 - fresnel) + room(reflect(direction, normal)) * fresnel;
        // A glint of the window on the glass, and a thin bright rim where the glass is seen edge-on.
        colour += float3(0.5, 0.55, 0.7) * pow(1.0 - saturate(dot(normal, -direction)), 8.0) * 0.6;
    }
    return colour;
}
