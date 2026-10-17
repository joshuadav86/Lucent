// Name: Murmuration
// A vast flock of starlings pours across a glowing dusk sky above a dark line of trees, folding, splitting and flowing back together like smoke, with waves of birds rippling through it on every beat.
// Family: Particles
// Tags: Calm, 3D
// Particles: 300000

// The flock lives well out in front of the camera, above the trees. y is up in this world.
static const float3 FlockHome = float3(0.0, 21.0, 52.0);
static const float3 Eye = float3(0.0, 1.6, 0.0);
static const float Zoom = 1.45;

float sinceBeat()
{
    return -log(max(Beat, 1e-4)) / 5.0;
}

// The camera turns slowly to follow the flock about, never quite catching it.
float3 cameraTarget()
{
    return float3(sin(Time * 0.031) * 8.0, 17.0 + sin(Time * 0.023) * 1.5, 40.0);
}

// Where the main body of the flock is heading: a slow wandering loop across the sky.
float3 flockCentre(float t)
{
    return FlockHome + float3(sin(t * 0.043) * 16.0 + sin(t * 0.11) * 4.0,
                              sin(t * 0.067) * 3.0 + cos(t * 0.13) * 1.2,
                              cos(t * 0.037) * 10.0);
}

// How far the breakaway groups have drifted from the main body: they part, wander and come back.
float separation(float t, float salt)
{
    float s = sin(t * 0.071 + salt) * 0.6 + sin(t * 0.029 + salt * 2.3) * 0.4;
    return pow(saturate(s), 1.5) * 22.0;
}

// The point each bird is trying to reach. Every bird has a fixed spot in a soft blob, and the blob
// stretches, turns and folds over on itself over time, so the flock keeps changing shape.
float3 birdTarget(uint id, float t, out float waveAlong)
{
    // A spot in a ball, a little denser in the middle.
    float z = fixedRandom(id, 1.0) * 2.0 - 1.0;
    float angle = fixedRandom(id, 2.0) * 2.0 * PI;
    float r = sqrt(max(1.0 - z * z, 0.0));
    float3 spot = float3(r * cos(angle), r * sin(angle), z) * pow(fixedRandom(id, 3.0), 0.45);

    // Which group this bird belongs to: most stay in the main body, two smaller groups break away.
    float group = fixedRandom(id, 4.0);
    float3 centre = flockCentre(t);
    float scale = 1.0;
    if (group > 0.86)
    {
        centre += float3(sin(t * 0.05) * 1.0, 0.35, cos(t * 0.05) * 0.4) * separation(t, 1.7);
        scale = 0.55;
    }
    else if (group > 0.74)
    {
        centre += float3(-cos(t * 0.04) * 1.0, -0.25, 0.6) * separation(t, 4.1);
        scale = 0.5;
    }

    // Stretch into a long, thin, curving ribbon that breathes between a sheet and a ball.
    float breathe = sin(t * 0.09) * 0.5 + 0.5;
    float3 stretch = float3(lerp(19.0, 30.0, breathe), lerp(9.0, 3.4, breathe), lerp(10.0, 6.0, breathe)) * scale;
    float3 shaped = spot * stretch;
    // Fold the sheet over like a wave breaking, and twist it along its length.
    shaped.y += sin(shaped.x * 0.28 + t * 0.35) * 2.6 * scale + sin(shaped.z * 0.4 - t * 0.2) * 1.2 * scale;
    shaped.yz = rotate(shaped.yz, shaped.x * 0.08 * sin(t * 0.06));
    shaped.xz = rotate(shaped.xz, sin(t * 0.027) * 1.2);
    shaped.xy = rotate(shaped.xy, sin(t * 0.041) * 0.35);

    // On each beat a ripple of tightly packed birds runs along the flock from one end to the other.
    waveAlong = spot.x * 0.5 + 0.5;
    float front = sinceBeat() * 1.6 - waveAlong;
    float ripple = exp(-front * front * 40.0) * step(1e-3, Beat) * (0.5 + Bass);
    shaped += normalize(float3(1.0, 0.25, 0.3)) * ripple * 2.2 * scale;
    shaped *= 1.0 - ripple * 0.15;

    return centre + shaped;
}

void Spawn(inout Particle particle, uint id)
{
    float along;
    particle.position = birdTarget(id, Time, along) + (float3(random(id, 5.0), random(id, 6.0), random(id, 7.0)) - 0.5) * 0.5;
    particle.velocity = 0.0;
    particle.life = 1e6;
    particle.data = 0.0;
}

void Simulate(inout Particle particle, uint id)
{
    float dt = min(DeltaTime, 0.05);
    float along;
    float3 target = birdTarget(id, Time, along);
    float3 toTarget = target - particle.position;

    // Each bird flies towards its spot while a slow swirling current folds the flock like liquid.
    // Birds far from their spot fly harder, so the flock pours rather than drifts. The music speeds it all up.
    float hurry = 0.55 + Bass * 0.35 + Level * 0.2;
    float3 swirl = curlNoise(particle.position * 0.06 + float3(0.0, Time * 0.07, Time * 0.05)) * (5.0 + Mid * 3.0);
    float3 wanted = toTarget * hurry + swirl;
    float response = 1.0 - exp(-dt * (1.6 + fixedRandom(id, 8.0) * 0.8));
    particle.velocity = lerp(particle.velocity, wanted, response);
    particle.position += particle.velocity * dt;

    // Anything that has wandered too far just rejoins the flock.
    if (dot(toTarget, toTarget) > 1600.0)
    {
        particle.life = 0.0;
    }
}

Sprite Show(Particle particle, uint id)
{
    Sprite sprite = (Sprite)0;
    float3 seen = project(particle.position, Eye, cameraTarget(), Zoom, 0.0);
    if (seen.z < 1.0)
    {
        return sprite;
    }
    // Each bird is a little dark speck. The layer counts how many birds cover each pixel, which
    // Render turns into how much of the sky they hide; a little motion blur shows their speed.
    float3 seenAhead = project(particle.position + particle.velocity * 0.04, Eye, cameraTarget(), Zoom, 0.0);
    sprite.position = float2(seen.x, -seen.y);
    sprite.streak = float2(seenAhead.x - seen.x, seen.y - seenAhead.y);
    sprite.size = clamp(55.0 / seen.z, 0.8, 2.2);
    sprite.colour = float3(1.0, 1.0, 1.0) * 0.2 * (0.85 + fixedRandom(id, 9.0) * 0.3);
    return sprite;
}

// The dusk sky: deep blue overhead, a lilac band, and a hot orange glow where the sun has just set.
float3 sky(float3 direction)
{
    float height = direction.y;
    float sunAzimuth = 0.35 + sin(Time * 0.01) * 0.05;
    float3 toSun = normalize(float3(sin(sunAzimuth), -0.03, cos(sunAzimuth)));
    float sunward = saturate(dot(direction, toSun));

    float3 zenith = float3(0.035, 0.06, 0.16);
    float3 middle = float3(0.28, 0.2, 0.36);
    float3 horizon = float3(1.25, 0.52, 0.22);
    float3 colour = lerp(middle, zenith, smoothstep(0.08, 0.55, height));
    colour = lerp(horizon, colour, smoothstep(-0.02, 0.2 - sunward * 0.08, height));
    // The afterglow round where the sun went down, swelling a little with the music.
    colour += float3(1.2, 0.48, 0.15) * pow(sunward, 18.0) * (1.0 + Level * 0.4) * smoothstep(0.35, -0.02, height);
    colour += float3(0.6, 0.25, 0.15) * pow(sunward, 4.0) * 0.4 * smoothstep(0.5, 0.0, height);

    // Long thin clouds catching pink light underneath.
    float2 cloudPlane = direction.xz / max(direction.y + 0.06, 0.02);
    float streaks = fbm(cloudPlane * float2(0.6, 2.2) + float2(Time * 0.01, 0.0));
    float cloud = smoothstep(0.5, 0.75, streaks) * smoothstep(0.02, 0.12, height) * smoothstep(0.5, 0.15, height);
    float3 cloudColour = lerp(float3(0.9, 0.42, 0.42), float3(0.3, 0.22, 0.38), smoothstep(0.05, 0.35, height));
    colour = lerp(colour, cloudColour * (0.7 + sunward * 0.8), cloud * 0.75);
    return colour;
}

// A row of rounded tree crowns of random heights along the horizon, density trees per radian.
float treeCrowns(float azimuth, float density, float salt)
{
    float x = azimuth * density;
    float cell = floor(x);
    float across = frac(x) * 2.0 - 1.0;
    float height = 0.4 + 0.6 * hash(float2(cell, salt));
    // Now and then a tall, narrow poplar.
    height *= hash(float2(cell, salt + 7.0)) > 0.9 ? 1.8 : 1.0;
    return height * sqrt(max(1.0 - across * across, 0.0)) * (0.8 + 0.2 * noise(float2(x * 6.0, salt)));
}

float3 Render(float2 uv, float2 p)
{
    float3 direction = cameraRay(Eye, cameraTarget(), float2(p.x, -p.y), Zoom, 0.0);
    float3 colour = sky(direction);

    // The flock hides the sky in proportion to how many birds cover each pixel; thick parts go near black,
    // thin edges stay a smoky grey. A trace of the sky's warmth stays in the thinnest wisps.
    float birds = particles(uv).g;
    float cover = 1.0 - exp(-birds * 2.6);
    colour = lerp(colour, float3(0.012, 0.01, 0.018) + colour * 0.08, cover);

    // A dark treeline along the bottom: rounded crowns with the odd taller tree.
    float azimuth = atan2(direction.x, direction.z);
    float treeline = -0.035 + max(treeCrowns(azimuth, 11.0, 0.0) * 0.075, treeCrowns(azimuth, 26.0, 3.0) * 0.045 + 0.01)
                   + (noise(float2(azimuth * 300.0, 1.3)) - 0.5) * 0.006;
    float edge = pixelSize() / Zoom;
    float trees = smoothstep(treeline + edge, treeline - edge, direction.y);
    // Mist sits low between the trees, glowing faintly with the sunset.
    float mist = exp(-max(direction.y - treeline + 0.015, 0.0) * 40.0) * 0.12;
    colour = lerp(colour, float3(0.008, 0.008, 0.014) + float3(0.35, 0.16, 0.1) * mist * 0.2, trees);
    return colour;
}
