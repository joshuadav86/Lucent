// Name: Sakura Storm
// A gust of glowing neon cherry blossom petals swirls through a moonlit garden, each petal tumbling and flashing white as it turns, the nearest ones melting into soft bokeh; the wind rises and falls with the music and every beat shakes loose a new flurry. Each petal glows with its own band of the spectrum, a great neon blossom turns round the moon, and the hills are traced in light.
// Family: Particles
// Tags: Neon, Colourful, 3D
// Added: 2026-10-17 17:02
// Particles: 110000

// Most petals drift in a box of air that travels with the camera, wrapping round as they leave
// it. The rest wait on the branches and are shaken loose in flurries on the beats.
static const float FlurryShare = 0.35;
static const float3 BoxSize = float3(24.0, 10.0, 22.0);
static const float3 Tree = float3(-5.5, 3.2, 7.0);

float shareOf(uint id)
{
    return (id + 0.5) / ParticleCount;
}

float3 cameraEye()
{
    return float3(sin(Time * 0.03) * 1.5, 1.4 + sin(Time * 0.045) * 0.3, Time * 0.25);
}

float3 cameraTarget()
{
    float3 eye = cameraEye();
    return eye + float3(sin(Time * 0.05) * 0.3, 0.25 + sin(Time * 0.037) * 0.1, 1.0);
}

float3 inBox(float3 position)
{
    float3 middle = cameraEye() + float3(0.0, 0.8, BoxSize.z * 0.5);
    float3 relative = position - middle;
    return middle + relative - BoxSize * floor(relative / BoxSize + 0.5);
}

// The wind: a steady breeze from the left that strengthens with the mids, swirling as it goes.
float3 wind(float3 position)
{
    float strength = 0.6 + Mid * 1.6 + Level * 0.4;
    float3 breeze = float3(1.0, -0.12, 0.25) * strength;
    float3 swirl = curlNoise(position * 0.28 + float3(-Time * 0.12 * strength, 0.0, Time * 0.03));
    return breeze + swirl * (0.45 + Mid * 0.7);
}

void Spawn(inout Particle particle, uint id)
{
    float share = shareOf(id);
    // data: x tumble angle, y tumble speed, z beat it last saw, w 1 when blowing free.
    particle.data = float4(random(id, 1.0) * 6.28, 2.0 + random(id, 2.0) * 5.0, BeatCount, 1.0);
    particle.velocity = 0.0;
    if (share < FlurryShare)
    {
        particle.data.w = 0.0;
        particle.position = cameraEye() + Tree + randomInSphere(id, 3.0) * float3(1.6, 0.8, 1.6);
        particle.life = 1e6;
        return;
    }
    float3 corner = cameraEye() + float3(-BoxSize.x * 0.5, 0.8 - BoxSize.y * 0.5, 0.0);
    particle.position = corner + float3(random(id, 4.0), random(id, 5.0), random(id, 6.0)) * BoxSize;
    particle.life = 1e6;
}

void Simulate(inout Particle particle, uint id)
{
    float share = shareOf(id);
    float dt = min(DeltaTime, 0.05);

    if (particle.data.w < 0.5)
    {
        // Waiting on the branches, which move along with us. A beat shakes some of us loose.
        particle.position = cameraEye() + Tree + randomInSphere(id, 7.0) * float3(1.6, 0.8, 1.6);
        if (particle.data.z != BeatCount)
        {
            particle.data.z = BeatCount;
            if (random(id, 8.0) < 0.03 + Bass * 0.05 + Beat * 0.02)
            {
                particle.data.w = 1.0;
                particle.velocity = float3(1.5 + random(id, 9.0) * 1.5, -0.3, random(id, 10.0) - 0.3) * (1.0 + Bass);
                particle.life = 14.0;
                particle.age = 0.0;
            }
        }
        return;
    }

    // Petals are light: they follow the wind closely, flutter, and fall slowly.
    float3 air = wind(particle.position);
    float flutter = sin(particle.data.x * 2.0) * 0.25;
    float3 wanted = air + float3(0.0, -0.22 + flutter, 0.0);
    particle.velocity = lerp(particle.velocity, wanted, saturate(dt * 2.5));
    particle.position += particle.velocity * dt;
    // A petal tumbles faster when the wind is gusty.
    particle.data.x += particle.data.y * dt * (0.6 + Mid * 0.8);
    if (share >= FlurryShare)
    {
        particle.position = inBox(particle.position);
    }
    else if (particle.life < 0.5 || particle.position.y < -1.0)
    {
        // A flurry petal that has blown away goes back to the tree.
        particle.data.w = 0.0;
        particle.life = 1e6;
    }
}

static const float3 MoonDirection = float3(0.45, 0.42, 0.79);

Sprite Show(Particle particle, uint id)
{
    float3 eye = cameraEye();
    float3 target = cameraTarget();
    Sprite sprite = (Sprite)0;
    if (particle.data.w < 0.5)
    {
        return sprite;
    }
    float3 seen = project(particle.position, eye, target, 1.6, sin(Time * 0.04) * 0.04);
    float3 seenBefore = project(particle.position - particle.velocity * 0.015, eye, target, 1.6, sin(Time * 0.04) * 0.04);

    // As a petal tumbles it shows us its face (bright) and then its edge (almost nothing), and
    // when its face swings towards the moon it flashes silver.
    float tumble = particle.data.x;
    float face = abs(cos(tumble));
    float glint = pow(saturate(cos(tumble + fixedRandom(id, 11.0) * 6.28)), 24.0);
    // Neon petals, mostly sakura pink and magenta with some violet and a few teal, each glowing with
    // its own band of the spectrum.
    float shade = fixedRandom(id, 12.0);
    float hue = shade < 0.75 ? lerp(0.9, 0.97, shade / 0.75) : (shade < 0.93 ? 0.78 : 0.5);
    float3 petal = hsv2rgb(hue, 0.72, 1.0);
    float level = spectrum(0.03 + shade * 0.65);
    float3 colour = petal * (0.1 + 0.9 * face) * 0.3 * (0.5 + level * 1.4) + float3(0.9, 0.9, 1.0) * glint * 1.1 * (0.6 + Treble);
    // Petals just shaken loose on the beat burn bright for a moment.
    colour *= 1.0 + exp(-particle.age * 1.5) * step(shareOf(id), FlurryShare) * 1.5;
    // Fade into the night mist with distance, and in at the edges of the box.
    float mist = exp(-seen.z * 0.09) * smoothstep(0.2, 0.8, seen.z);
    colour *= mist * saturate(particle.age * 2.0) * (0.85 + Level * 0.4);

    sprite.position = float2(seen.x, -seen.y);
    sprite.streak = float2(seen.x - seenBefore.x, seenBefore.y - seen.y);
    sprite.size = seen.z > 0.15 ? (2.4 + fixedRandom(id, 13.0) * 1.6) * (0.55 + 0.45 * face) : 0.0;
    sprite.colour = colour;
    // Petals near the camera swell into soft bokeh discs.
    focus(sprite, seen.z, 4.5, 0.09);
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    float3 eye = cameraEye();
    float3 target = cameraTarget();
    float3 direction = cameraRay(eye, target, float2(p.x, -p.y), 1.6, sin(Time * 0.04) * 0.04);
    float3 moonward = normalize(MoonDirection);

    // A deep blue night sky, paler near the horizon, with a few faint stars.
    float up = direction.y;
    float3 colour = lerp(float3(0.03, 0.035, 0.07), float3(0.008, 0.01, 0.03), saturate(up * 2.0));
    float2 cell = float2(atan2(direction.x, direction.z) * 150.0, up * 150.0);
    colour += step(0.993, hash(floor(cell))) * smoothstep(0.35, 0.0, length(frac(cell) - 0.5)) * 0.25 * smoothstep(0.1, 0.4, up);

    // The full moon, with a soft halo and faint markings, behind thin drifting cloud.
    float toMoon = acos(clamp(dot(direction, moonward), -1.0, 1.0));
    float2 onMoon = float2(dot(direction, normalize(cross(moonward, float3(0, 1, 0)))), direction.y - moonward.y) / 0.07;
    float disc = smoothstep(0.072, 0.066, toMoon);
    float marks = 0.8 + 0.2 * fbm(onMoon * 2.0 + 4.0);
    colour += float3(1.0, 0.95, 0.88) * disc * marks * 1.2;
    // A great neon blossom turns slowly round the moon: five petals traced in light, the outline
    // flaring with the bass and a ring of light round the moon on every beat.
    float2 around = onMoon * 0.07;
    float spin = atan2(around.y, around.x) + Time * 0.04;
    float bloom = 0.2 * (0.55 + 0.45 * abs(cos(spin * 2.5))) * (1.0 + Bass * 0.08);
    float fromBloom = abs(length(around) - bloom);
    float petals = exp(-pow(fromBloom / 0.0018, 2.0)) + exp(-fromBloom / 0.012) * 0.15;
    colour += hsv2rgb(0.92, 0.7, 1.0) * petals * (0.35 + Bass * 0.6) * smoothstep(0.05, 0.12, toMoon);
    float sinceBeat = -log(max(Beat, 1e-4)) / 5.0;
    colour += hsv2rgb(0.5, 0.6, 1.0) * exp(-pow((toMoon - 0.08 - sinceBeat * 0.2) / 0.002, 2.0)) * Beat * 0.6;
    colour += float3(0.5, 0.55, 0.75) * (exp(-toMoon * 9.0) * 0.22 + exp(-toMoon * 2.5) * 0.06) * (1.0 + Bass * 0.3);
    float cloud = smoothstep(0.45, 0.8, fbm(float2(atan2(direction.x, direction.z) * 3.0 + Time * 0.01, up * 8.0)));
    colour = lerp(colour, float3(0.06, 0.065, 0.1) + float3(0.2, 0.2, 0.25) * exp(-toMoon * 4.0), cloud * smoothstep(0.05, 0.25, up) * 0.6);

    // Rolling hills and the dark shapes of trees on the horizon, layered into the mist.
    float along = atan2(direction.x, direction.z) + eye.z * 0.004;
    float far = 0.0 + fbm(float2(along * 3.0, 1.0)) * 0.1;
    float near = -0.06 + fbm(float2(along * 6.0 + 3.0, 2.0)) * 0.1 + step(0.6, noise(float2(along * 40.0, 0.0))) * 0.02;
    float3 mistColour = float3(0.05, 0.05, 0.09) + float3(0.08, 0.06, 0.1) * exp(-toMoon * 3.0);
    colour = lerp(colour, mistColour, smoothstep(far + 0.004, far - 0.004, up) * 0.85);
    colour = lerp(colour, float3(0.012, 0.012, 0.025), smoothstep(near + 0.004, near - 0.004, up));
    // The hills' skylines traced in fine neon, violet far off and pink close by.
    colour += hsv2rgb(0.78, 0.7, 1.0) * exp(-pow((up - far) / 0.0015, 2.0)) * 0.25 * (0.5 + spectrum(0.1));
    colour += hsv2rgb(0.93, 0.7, 1.0) * exp(-pow((up - near) / 0.0015, 2.0)) * 0.35 * (0.5 + spectrum(0.3));
    // Paper lanterns glowing far off along the garden path, swaying with the bass.
    float lanternRow = step(0.75, hash(float2(floor(along * 30.0), 3.0)));
    float lantern = lanternRow * exp(-length(float2((frac(along * 30.0) - 0.5) * 2.0, (up - near + 0.012) * 90.0)) * 4.0);
    colour += float3(1.0, 0.55, 0.3) * lantern * (0.5 + Bass * 0.5);

    return colour + particles(uv) + previous(uv) * 0.45;
}
