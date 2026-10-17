// Name: Incense Smoke
// A single thread of smoke rises from a stick of incense, smooth and straight at first, then breaking into curling ribbons lit from behind by coloured light; the mids stir the air and every beat sends a soft breath through it.
// Family: Particles
// Tags: Calm, Dark
// Particles: 400000

// y is up. The glowing tip of the incense is at the origin.
static const float3 Tip = float3(0.0, 0.0, 0.0);
static const float Zoom = 1.7;

float3 cameraEye()
{
    float orbit = sin(Time * 0.03) * 0.35;
    return float3(sin(orbit) * 3.4, 0.75 + sin(Time * 0.05) * 0.1, -cos(orbit) * 3.4);
}

float3 cameraTarget()
{
    return float3(0.0, 1.05, 0.0);
}

// The two lights: a warm amber from the front left and a cool teal rim from behind on the right.
// They drift slowly round the colour wheel together.
float3 warmLight()
{
    return hsv2rgb(0.07 + sin(Time * 0.013) * 0.04, 0.75, 1.0);
}

float3 coolLight()
{
    return hsv2rgb(0.52 + sin(Time * 0.011) * 0.08, 0.7, 1.0);
}

void Spawn(inout Particle particle, uint id)
{
    particle.position = Tip + (float3(random(id, 1.0), random(id, 2.0), random(id, 3.0)) - 0.5) * 0.006;
    particle.velocity = float3(0.0, 0.3, 0.0);
    particle.life = 7.0 + random(id, 4.0) * 3.0;
    particle.data = float4(particle.life, 0.0, 0.0, 0.0);
    if (ParticleReset > 0.5)
    {
        // On the first frame, start everyone at the tip but at different points in their lives, so the
        // plume grows as it would from a freshly lit stick, with no gap.
        particle.life *= random(id, 5.0);
        particle.data.x = particle.life;
    }
}

void Simulate(inout Particle particle, uint id)
{
    float dt = min(DeltaTime, 0.05);
    float3 position = particle.position;
    float height = position.y - Tip.y;

    // Warm smoke rises steadily, drawn into a single smooth thread. Higher up it breaks into eddies;
    // where that happens drifts up and down, and the mids bring it lower and stir it harder.
    float breakup = 0.4 + 0.2 * sin(Time * 0.21) - Mid * 0.25;
    float turbulent = smoothstep(breakup, breakup + 0.7, height);
    float3 rise = float3(0.0, 0.32 - turbulent * 0.12, 0.0);
    // Even the smooth part sways like a hair in a draught.
    float3 sway = float3(sin(height * 3.0 - Time * 0.9) * 0.06 + sin(Time * 0.31) * 0.03, 0.0, cos(height * 2.3 - Time * 0.7) * 0.04) * saturate(height * 2.0);
    float3 eddies = curlNoise(position * float3(3.4, 2.4, 3.4) + float3(0.0, -Time * 0.18, Time * 0.07)) * (0.22 + Mid * 0.3);
    float3 swirls = curlNoise(position * 0.9 + float3(Time * 0.04, -Time * 0.06, 0.0)) * 0.16;
    // A beat is a soft breath of air from the side.
    float3 breath = float3(0.12, 0.0, 0.05) * Beat * Beat * saturate(height);
    // Each wisp also slowly spreads away from its neighbours, so the broken smoke opens into sheets.
    float3 spread = (float3(fixedRandom(id, 6.0), fixedRandom(id, 7.0), fixedRandom(id, 8.0)) - 0.5) * 0.12;
    float3 wanted = rise + sway + (eddies + swirls + spread) * turbulent + breath;
    particle.velocity = lerp(particle.velocity, wanted, 1.0 - exp(-dt * 3.0));
    particle.position += particle.velocity * dt;
}

Sprite Show(Particle particle, uint id)
{
    Sprite sprite = (Sprite)0;
    float3 eye = cameraEye();
    float3 target = cameraTarget();
    float3 seen = project(particle.position, eye, target, Zoom, 0.0);
    if (seen.z < 0.2)
    {
        return sprite;
    }
    float age = particle.data.x - particle.life;
    float height = particle.position.y - Tip.y;
    // Smoke thins out as it spreads: fades over its life, and is barely there at the very tip.
    // The thread low down packs every wisp into one line, so each one there is drawn fainter.
    float fade = saturate(particle.life / 2.0) * smoothstep(0.0, 0.25, height) * exp(-age * 0.12) * lerp(0.1, 1.0, smoothstep(0.1, 1.2, height));

    // Lit from two sides: which light a wisp catches depends on where it is across the plume,
    // and smoke glows brightest when a light is behind it.
    float3 toCamera = normalize(eye - particle.position);
    float across = particle.position.x * 2.0 + particle.position.z * 0.8 + sin(height * 1.7 + Time * 0.1) * 0.4;
    float coolSide = smoothstep(-0.5, 0.5, across + (height - 1.1) * 0.6);
    float3 coolDirection = normalize(float3(1.0, 0.4, 1.0));
    float3 warmDirection = normalize(float3(-1.0, 0.3, -0.6));
    float coolScatter = 0.4 + 1.6 * pow(saturate(-dot(toCamera, coolDirection) * 0.5 + 0.5), 3.0);
    float warmScatter = 0.6 + 0.6 * pow(saturate(-dot(toCamera, warmDirection) * 0.5 + 0.5), 2.0);
    float3 colour = coolLight() * coolSide * coolScatter + warmLight() * (1.0 - coolSide) * warmScatter * 1.6;
    // Fine grey haze in between, so the colours meet softly.
    colour += 0.18;

    float3 before = project(particle.position - particle.velocity * 0.05, eye, target, Zoom, 0.0);
    sprite.position = float2(seen.x, -seen.y);
    sprite.streak = float2(seen.x - before.x, before.y - seen.y);
    sprite.size = 0.9 + height * 0.5;
    sprite.colour = colour * fade * 0.045 * (0.8 + Level * 0.5);
    focus(sprite, seen.z, length(eye - target), 0.03);
    return sprite;
}

// Distance between a ray and a line segment, and how far along the ray that closest point is.
float2 raySegment(float3 origin, float3 direction, float3 a, float3 b)
{
    float3 ba = b - a;
    float3 oa = origin - a;
    float d = dot(direction, ba);
    float denominator = dot(ba, ba) - d * d;
    float s = saturate((dot(oa, ba) - dot(oa, direction) * d) / max(denominator, 1e-6));
    float3 onSegment = a + ba * s;
    float t = max(dot(onSegment - origin, direction), 0.0);
    return float2(length(origin + direction * t - onSegment), t);
}

float3 Render(float2 uv, float2 p)
{
    float3 eye = cameraEye();
    float3 target = cameraTarget();
    float3 direction = cameraRay(eye, target, float2(p.x, -p.y), Zoom, 0.0);

    // A dark room: the faintest glow of each light on the far wall.
    float3 colour = float3(0.004, 0.004, 0.006);
    colour += coolLight() * 0.05 * exp(-dot(p - float2(0.7, -0.3), p - float2(0.7, -0.3)) * 1.2);
    colour += warmLight() * 0.03 * exp(-dot(p - float2(-0.9, 0.4), p - float2(-0.9, 0.4)) * 1.5);

    // The table top: a dark lacquered surface that catches a soft reflection of the lights.
    float tableHeight = -0.75;
    float tableDistance = 1e4;
    if (direction.y < 0.0)
    {
        tableDistance = (tableHeight - eye.y) / direction.y;
        float3 table = eye + direction * tableDistance;
        float sheen = pow(saturate(1.0 + direction.y * 1.4), 4.0);
        // The far edge of the table is lost in the dark, so it melts into the wall with no hard line.
        float near = exp(-length(table.xz) * 0.45);
        colour = lerp(colour * 0.6, float3(0.006, 0.005, 0.005) + (coolLight() * 0.03 + warmLight() * 0.02) * sheen * near, smoothstep(6.0, 3.0, tableDistance));
        // The incense's own warm glow on the table beneath it.
        colour += float3(1.0, 0.35, 0.1) * 0.03 * exp(-length(table.xz) * 3.0) * (0.8 + Bass * 0.6);
    }

    // The incense stick, leaning a little, from its holder up to the tip.
    float3 bottom = float3(-0.12, tableHeight + 0.05, 0.05);
    float pixelAngle = pixelSize() / Zoom;
    float2 stick = raySegment(eye, direction, bottom, Tip - float3(0.0, 0.01, 0.0));
    float width = max(0.006, pixelAngle * stick.y * 0.6);
    if (stick.x < width && stick.y < tableDistance)
    {
        float along = saturate((dot(eye + direction * stick.y - bottom, normalize(Tip - bottom))) / length(Tip - bottom));
        // Bare bamboo near the holder, dark incense paste above; the paste turns to ash near the tip.
        float3 shade = along < 0.25 ? float3(0.12, 0.08, 0.04) : float3(0.03, 0.018, 0.012);
        shade = lerp(shade, float3(0.18, 0.17, 0.16), smoothstep(0.92, 0.98, along));
        colour = lerp(colour, shade * (0.3 + warmLight() * 0.5), saturate(0.006 / width) * smoothstep(width, width * 0.4, stick.x));
    }

    // The glowing tip, breathing with the bass.
    float2 tipOnScreen = raySegment(eye, direction, Tip, Tip);
    float ember = 0.8 + Bass * 0.9 + sin(Time * 2.3) * 0.1;
    colour += float3(1.0, 0.32, 0.06) * 1.2 * ember * exp(-tipOnScreen.x * tipOnScreen.x / (0.00008 + 0.00004 * Bass));
    colour += float3(1.0, 0.3, 0.05) * 0.05 * ember * exp(-tipOnScreen.x * 25.0);

    // The smoke, softened by a little of the last frame.
    return colour + particles(uv) + previous(uv) * 0.45;
}
