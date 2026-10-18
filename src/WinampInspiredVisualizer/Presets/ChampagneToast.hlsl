// Name: Champagne Toast
// Two crystal flutes of golden champagne meet in a toast on every beat, threads of tiny bubbles racing up through the wine and sparkling with the treble, against the soft glow of party lights.
// Family: Ray traced
// Tags: Calm, Colourful, 3D
// Added: 2026-10-17 17:02
// Particles: 131072

// The flutes are ray traced in Render; the bubbles are particles that live inside the wine and
// are added on top where the wine is. Each flute has its own frame: y runs up its stem, with the
// bottom of the bowl at 0.

static const float BowlTop = 1.05;
static const float OuterCurve = 0.0549;   // the bowl's outside: x^2 + z^2 = curve * y
static const float InnerCurve = 0.0511;   // its inside, starting a little higher
static const float InnerBottom = 0.07;
static const float StemRadius = 0.022;
static const float FootHeight = -0.78;
static const float FootRadius = 0.27;
static const float FillLevel = 0.74;
static const float Tilt = 0.17;           // how far each flute leans in towards the other

// ---- Where the flutes are ----

// Seconds since the last beat, from how far Beat has faded.
float sinceBeat()
{
    return -log(max(Beat, 1e-4)) / 5.0;
}

// The flutes touch rims on the beat and ease apart again.
float gapNow()
{
    return 0.07 * smoothstep(0.0, 0.45, sinceBeat());
}

// Flute g's position and turn. Its frame turns local directions into the world's.
void fluteFrame(int g, out float3 centre, out float3x3 frame)
{
    float side = g == 0 ? -1.0 : 1.0;
    float sway = sin(Time * 0.31) * 0.03;
    float lean = Tilt * side + sway * 0.5;
    float reach = 0.418 + gapNow();
    centre = float3(side * reach + sway, -0.3 + sin(Time * 0.23 + g) * 0.02, 0.0);
    float s;
    float c;
    sincos(lean, s, c);
    // A lean round z, then a little turn round y so the two glasses aren't quite in a line.
    float3x3 leanZ = float3x3(c, -s, 0.0, s, c, 0.0, 0.0, 0.0, 1.0);
    float turn = side * 0.12;
    float3x3 turnY = float3x3(cos(turn), 0.0, sin(turn), 0.0, 1.0, 0.0, -sin(turn), 0.0, cos(turn));
    frame = mul(turnY, leanZ);
}

float3 toWorld(int g, float3 local)
{
    float3 centre;
    float3x3 frame;
    fluteFrame(g, centre, frame);
    return centre + mul(frame, local);
}

// The wine's surface stays level in the world however the glass leans, and sloshes a little after
// each clink. Returns its upward normal in the glass's own frame.
float3 surfaceNormal(float3x3 frame)
{
    float slosh = sin(sinceBeat() * 14.0) * exp(-sinceBeat() * 3.0) * 0.06;
    float3 up = normalize(float3(slosh, 1.0, slosh * 0.4));
    return mul(up, frame);
}

// ---- Bubbles ----

void Spawn(inout Particle particle, uint id)
{
    // Most bubbles come up in threads from a few specks on the glass (as real ones do); some rise
    // on their own from anywhere in the wine.
    float stream = floor(fixedRandom(id, 1.0) * 7.0);
    bool threaded = stream < 5.5;
    float angle = threaded ? fixedRandom(id, 2.0 + stream) * 6.28 + stream * 1.3 : random(id, 3.0) * 6.28;
    float y = threaded ? InnerBottom + 0.02 + frac(stream * 0.618 + fixedRandom(id, 9.0) * 0.01) * 0.3 : lerp(InnerBottom + 0.02, FillLevel - 0.1, random(id, 8.0));
    float limit = sqrt(InnerCurve * (y - InnerBottom));
    float r = threaded ? (0.55 + 0.3 * frac(stream * 0.37)) * limit : sqrt(random(id, 4.0)) * limit;
    particle.position = float3(cos(angle) * r, y, sin(angle) * r);
    particle.velocity = 0.0;
    particle.data = float4(threaded ? 1.0 : 0.0, fixedRandom(id, 5.0), 0.0, 0.0);
    particle.life = 30.0;
    if (ParticleReset > 0.5)
    {
        // Start partway up so the wine is already fizzing.
        particle.position.y = lerp(particle.position.y, FillLevel - 0.02, random(id, 6.0));
    }
}

void Simulate(inout Particle particle, uint id)
{
    float dt = min(DeltaTime, 0.05);
    // Bubbles speed up as they grow on the way up; a clink sends a rush of them.
    float rise = (0.12 + particle.age * 0.06) * (0.7 + particle.data.y * 0.6) * (1.0 + Beat * 0.8 + Level * 0.3);
    particle.position.y += rise * dt;
    // A slight wobble, and the wine stirring after a clink.
    float wobble = sin(particle.age * 20.0 + particle.data.y * 30.0) * 0.004;
    float stir = Beat * 0.05;
    particle.position.xz = rotate(particle.position.xz, stir * dt * 6.0) + float2(wobble, -wobble) * dt * 20.0;
    // Keep inside the bowl.
    float limit = sqrt(max(InnerCurve * (particle.position.y - InnerBottom), 0.0)) * 0.9;
    float r = length(particle.position.xz);
    if (r > limit)
    {
        particle.position.xz *= limit / max(r, 1e-4);
    }
    particle.velocity = float3(0.0, rise, 0.0);
    if (particle.position.y > FillLevel - 0.01)
    {
        particle.life = 0.0;
    }
}

float3 cameraEye()
{
    return float3(sin(Time * 0.05) * 0.5, 0.35 + sin(Time * 0.07) * 0.1, 2.5 + sin(Time * 0.04) * 0.15);
}

float3 cameraTarget()
{
    return float3(0.0, 0.05, 0.0);
}

static const float Zoom = 1.9;

Sprite Show(Particle particle, uint id)
{
    int g = (int)(id & 1u);
    float3 world = toWorld(g, particle.position);
    float3 eye = cameraEye();
    float3 seen = project(world, eye, cameraTarget(), Zoom, 0.0);
    float3 seenBefore = project(toWorld(g, particle.position - particle.velocity * 0.03), eye, cameraTarget(), Zoom, 0.0);

    // Tiny bright beads, bigger near the top; now and then one catches the light and flashes.
    float grown = saturate((particle.position.y - InnerBottom) / (FillLevel - InnerBottom));
    float sparkle = step(0.985, random(id, 7.0)) * (0.5 + Treble * 4.0);
    float3 colour = float3(1.0, 0.92, 0.7) * (0.025 + grown * 0.03 + sparkle * 0.4) * (0.75 + particle.data.x * 0.5);
    colour *= saturate(particle.age * 4.0);

    Sprite sprite = (Sprite)0;
    sprite.position = float2(seen.x, -seen.y);
    sprite.streak = float2(seen.x - seenBefore.x, seenBefore.y - seen.y);
    sprite.size = seen.z > 0.1 ? 0.6 + grown * 0.7 : 0.0;
    sprite.colour = colour;
    focus(sprite, seen.z, length(eye - cameraTarget()), 0.01);
    return sprite;
}

// ---- The party lights behind ----

// Out-of-focus party lights: soft discs of gold, amber and rose with brighter rims, the way a
// camera lens draws lights it isn't focused on. They breathe gently with the music.
float3 partyLights(float3 direction)
{
    float3 colour = lerp(float3(0.03, 0.015, 0.012), float3(0.008, 0.006, 0.012), saturate(direction.y * 1.5 + 0.5));
    float2 view = float2(atan2(direction.x, -direction.z), direction.y);
    [unroll]
    for (int layer = 0; layer < 3; layer++)
    {
        float scale = 5.0 + layer * 3.5;
        float2 q = view * scale + float2(layer * 3.7, layer * 1.3);
        float2 cell = floor(q);
        float2 inCell = frac(q) - 0.5;
        float2 jitter = (float2(hash(cell + layer), hash(cell + 7.0 + layer)) - 0.5) * 0.3;
        float d = length(inCell - jitter);
        float size = 0.16 + hash(cell + 3.0) * 0.16;
        float disc = smoothstep(size, size - 0.025, d);
        float rim = smoothstep(size - 0.06, size, d) * disc;
        float present = step(0.62, hash(cell + 11.0 + layer));
        float pick = hash(cell + 19.0);
        float3 tint = pick < 0.5 ? float3(1.0, 0.65, 0.25) : (pick < 0.8 ? float3(1.0, 0.4, 0.3) : float3(1.0, 0.45, 0.6));
        float breathe = 0.6 + 0.4 * sin(Time * (0.5 + pick) + pick * 20.0) + spectrum(pick * 0.6 + 0.05) * 0.6;
        colour += tint * (disc * 0.45 + rim * 0.3) * present * breathe * (0.3 / (1.0 + layer * 0.8));
    }
    // Soft warm light from the sides of the room, which the glass picks up along its edges.
    float sideways = abs(direction.x) / max(length(direction.xz), 1e-3);
    colour += float3(0.5, 0.4, 0.32) * smoothstep(0.75, 1.0, sideways) * smoothstep(0.9, 0.2, abs(direction.y)) * (0.5 + Level * 0.4);
    // A warm key light above to the left, which gives the glass its brightest highlights.
    colour += float3(1.0, 0.9, 0.75) * smoothstep(0.94, 0.985, dot(direction, normalize(float3(-0.6, 0.6, 0.5)))) * 6.0;
    colour += float3(1.0, 0.8, 0.6) * smoothstep(0.97, 0.99, dot(direction, normalize(float3(0.7, 0.3, 0.6)))) * 2.5;
    return colour;
}

// ---- Tracing the glasses ----

static const int OuterGlass = 0;
static const int InnerGlass = 1;
static const int WineTop = 2;
static const int Stem = 3;
static const int Foot = 4;

// The nearest root of x^2 + z^2 = curve * (y - bottom) beyond near, between bottom and BowlTop.
float bowlHit(float3 o, float3 d, float curve, float bottom, float near)
{
    float a = d.x * d.x + d.z * d.z;
    float b = 2.0 * (o.x * d.x + o.z * d.z) - curve * d.y;
    float c = o.x * o.x + o.z * o.z - curve * (o.y - bottom);
    float t0;
    float t1;
    if (abs(a) < 1e-7)
    {
        t0 = abs(b) > 1e-7 ? -c / b : -1.0;
        t1 = -1.0;
    }
    else
    {
        float h = b * b - 4.0 * a * c;
        if (h < 0.0)
        {
            return 1e5;
        }
        h = sqrt(h);
        t0 = (-b - h) / (2.0 * a);
        t1 = (-b + h) / (2.0 * a);
    }
    float best = 1e5;
    float y0 = o.y + d.y * t0;
    if (t0 > near && y0 > bottom && y0 < BowlTop)
    {
        best = t0;
    }
    float y1 = o.y + d.y * t1;
    if (t1 > near && t1 < best && y1 > bottom && y1 < BowlTop)
    {
        best = t1;
    }
    return best;
}

// Is a world point inside the wine of either glass?
bool inWine(float3 position)
{
    [unroll]
    for (int g = 0; g < 2; g++)
    {
        float3 centre;
        float3x3 frame;
        fluteFrame(g, centre, frame);
        float3 q = mul(position - centre, frame);
        float3 up = surfaceNormal(frame);
        bool below = dot(q - float3(0.0, FillLevel, 0.0), up) < 0.0;
        if (below && q.y > InnerBottom && q.x * q.x + q.z * q.z < InnerCurve * (q.y - InnerBottom))
        {
            return true;
        }
    }
    return false;
}

// The next surface a ray meets on either glass: its distance, world normal and kind.
float nextSurface(float3 origin, float3 direction, out float3 normal, out int kind)
{
    float best = 1e5;
    normal = float3(0.0, 1.0, 0.0);
    kind = -1;
    const float near = 1e-4;
    [unroll]
    for (int g = 0; g < 2; g++)
    {
        float3 centre;
        float3x3 frame;
        fluteFrame(g, centre, frame);
        float3 o = mul(origin - centre, frame);
        float3 d = mul(direction, frame);

        float t = bowlHit(o, d, OuterCurve, 0.0, near);
        if (t < best)
        {
            float3 q = o + d * t;
            best = t;
            normal = mul(frame, normalize(float3(2.0 * q.x, -OuterCurve, 2.0 * q.z)));
            kind = OuterGlass;
        }
        t = bowlHit(o, d, InnerCurve, InnerBottom, near);
        if (t < best)
        {
            float3 q = o + d * t;
            best = t;
            normal = mul(frame, normalize(float3(2.0 * q.x, -InnerCurve, 2.0 * q.z)));
            kind = InnerGlass;
        }
        // The wine's surface, inside the bowl.
        float3 up = surfaceNormal(frame);
        float along = dot(d, up);
        if (abs(along) > 1e-5)
        {
            t = dot(float3(0.0, FillLevel, 0.0) - o, up) / along;
            float3 q = o + d * t;
            if (t > near && t < best && q.y > InnerBottom && q.x * q.x + q.z * q.z < InnerCurve * (q.y - InnerBottom))
            {
                best = t;
                normal = mul(frame, up);
                kind = WineTop;
            }
        }
        // The stem: a slim glass rod.
        float a = d.x * d.x + d.z * d.z;
        if (a > 1e-7)
        {
            float b = o.x * d.x + o.z * d.z;
            float c = o.x * o.x + o.z * o.z - StemRadius * StemRadius;
            float h = b * b - a * c;
            if (h > 0.0)
            {
                t = (-b - sqrt(h)) / a;
                float y = o.y + d.y * t;
                if (t > near && t < best && y > FootHeight && y < 0.02)
                {
                    float3 q = o + d * t;
                    best = t;
                    normal = mul(frame, normalize(float3(q.x, 0.0, q.z)));
                    kind = Stem;
                }
            }
        }
        // The foot: a thin disc.
        if (abs(d.y) > 1e-5)
        {
            t = (FootHeight - o.y) / d.y;
            float3 q = o + d * t;
            if (t > near && t < best && q.x * q.x + q.z * q.z < FootRadius * FootRadius)
            {
                best = t;
                normal = mul(frame, float3(0.0, d.y < 0.0 ? 1.0 : -1.0, 0.0));
                kind = Foot;
            }
        }
    }
    return best;
}

float3 trace(float3 origin, float3 direction, out float wineDepth)
{
    wineDepth = 0.0;
    float3 colour = 0.0;
    float3 throughput = 1.0;
    bool inside = inWine(origin);
    [loop]
    for (int i = 0; i < 14; i++)
    {
        float3 normal;
        int kind;
        float t = nextSurface(origin, direction, normal, kind);
        if (kind < 0)
        {
            break;
        }
        float3 position = origin + direction * t;
        if (inside)
        {
            // Light travelling through the wine turns golden, and the wine glows faintly with
            // the light scattered inside it.
            throughput *= exp(-t * float3(0.25, 0.9, 3.2));
            colour += throughput * float3(1.0, 0.7, 0.25) * t * (0.25 + Bass * 0.15);
            wineDepth += t;
        }
        bool after = inWine(position + direction * 2e-4);
        float3 facingNormal = dot(normal, direction) < 0.0 ? normal : -normal;
        float cosine = saturate(dot(facingNormal, -direction));

        if (after != inside)
        {
            // Crossing between wine and air (through the thin glass wall, or at the surface): bend.
            float eta = inside ? 1.33 : 1.0 / 1.33;
            float fresnel = 0.03 + 0.97 * pow(1.0 - cosine, 5.0);
            float3 bent = refract(direction, facingNormal, eta);
            if (dot(bent, bent) < 0.5)
            {
                direction = reflect(direction, facingNormal);
                origin = position + facingNormal * 2e-4;
                continue;
            }
            if (!inside)
            {
                colour += throughput * fresnel * partyLights(reflect(direction, facingNormal));
            }
            throughput *= 1.0 - fresnel;
            direction = bent;
            origin = position - facingNormal * 2e-4;
            inside = after;
        }
        else
        {
            // Thin crystal: a bright reflection, and the light passes straight through, very
            // slightly tinted. The stem and foot are solid, so they bend a little more.
            float fresnel = 0.04 + 0.96 * pow(1.0 - cosine, 5.0);
            colour += throughput * fresnel * partyLights(reflect(direction, facingNormal)) * 1.3;
            throughput *= (1.0 - fresnel) * float3(0.97, 0.98, 0.99);
            if (kind == Stem)
            {
                direction = normalize(direction - facingNormal * 0.25);
            }
            origin = position + direction * 2e-4;
        }
        if (max(throughput.x, max(throughput.y, throughput.z)) < 0.01)
        {
            break;
        }
    }
    return colour + throughput * partyLights(direction);
}

float3 Render(float2 uv, float2 p)
{
    float3 eye = cameraEye();
    float3 direction = cameraRay(eye, cameraTarget(), float2(p.x, -p.y), Zoom, 0.0);
    float wineDepth;
    float3 colour = trace(eye, direction, wineDepth);

    // The clink: a brief star of light where the rims touch.
    float3 touch = (toWorld(0, float3(0.24, BowlTop, 0.0)) + toWorld(1, float3(-0.24, BowlTop, 0.0))) * 0.5;
    float3 toTouch = touch - eye;
    float3 across = toTouch - direction * dot(toTouch, direction);
    float3 side = normalize(cross(direction, float3(0.0, 1.0, 0.0)));
    float2 offset = float2(dot(across, side), dot(across, cross(side, direction)));
    float star = exp(-length(offset) * 60.0) + exp(-abs(offset.x) * 300.0 - abs(offset.y) * 25.0) * 0.6 + exp(-abs(offset.y) * 300.0 - abs(offset.x) * 25.0) * 0.6;
    colour += float3(1.0, 0.95, 0.85) * star * pow(Beat, 3.0) * 1.5;

    // Bubbles show only where we're looking through wine.
    float wine = saturate(wineDepth * 20.0);
    return colour + particles(uv) * wine * float3(1.0, 0.9, 0.7);
}
