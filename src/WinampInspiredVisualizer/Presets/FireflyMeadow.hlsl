// Name: Firefly Meadow
// Thousands of fireflies drift over a dark summer meadow under the moon, each blinking on its own until a beat sends a wave of light rippling through them all, the nearest ones swimming in soft focus between the grass.
// Family: Particles
// Tags: Calm, Dark, 3D
// Particles: 131072

// The fireflies live in a big box of air over the meadow (world y is up), wrapping round its sides.
static const float3 FieldMin = float3(-22.0, 0.08, 1.5);
static const float3 FieldSize = float3(44.0, 2.6, 26.0);
static const float3 MoonDirection = float3(-0.42, 0.36, 0.83);

float3 cameraEye()
{
    return float3(sin(Time * 0.023) * 4.0, 1.75 + 0.2 * sin(Time * 0.041), -1.0 + sin(Time * 0.017) * 1.2);
}

float3 cameraTarget()
{
    float3 eye = cameraEye();
    return eye + float3(sin(Time * 0.019 + 1.0) * 1.6, -0.35 + 0.15 * sin(Time * 0.029), 10.0);
}

static const float Zoom = 1.7;

// Seconds since the last beat (Beat fades as e^-5t).
float sinceBeat()
{
    return -log(max(Beat, 1e-4)) / 5.0;
}

// Each beat a flash of light spreads out across the meadow from a new spot.
float3 waveOrigin()
{
    uint seed = (uint)BeatCount;
    float2 pick = float2(pcgHash(seed * 2u + 1u), pcgHash(seed * 2u + 2u)) / 4294967295.0;
    return float3((pick.x - 0.5) * 18.0, 1.0, 6.0 + pick.y * 16.0);
}

void Spawn(inout Particle particle, uint id)
{
    float height = pow(random(id, 3.0), 1.6);
    particle.position = FieldMin + float3(random(id, 1.0), height, pow(random(id, 2.0), 1.7)) * FieldSize;
    particle.velocity = 0.0;
    particle.life = lerp(30.0, 90.0, random(id, 4.0));
    // Its own blink: when in the cycle it flashes, and how often.
    particle.data = float4(fixedRandom(id, 5.0), lerp(0.18, 0.45, fixedRandom(id, 6.0)), 0.0, 0.0);
}

void Simulate(inout Particle particle, uint id)
{
    float dt = min(DeltaTime, 0.05);
    float3 position = particle.position;
    // A lazy wandering flight on the night air, a little livelier with the music.
    float3 drift = curlNoise(position * 0.22 + float3(0.0, Time * 0.04, fixedRandom(id, 7.0) * 3.0));
    float3 wish = drift * (0.22 + Mid * 0.25) + float3(0.12, 0.0, 0.0);
    // Stay above the grass and below the tree tops.
    wish.y += saturate(0.3 - position.y) * 1.5 - saturate(position.y - 2.4) * 1.0;
    particle.velocity = lerp(particle.velocity, wish, saturate(dt * 1.5));
    position += particle.velocity * dt;
    // Wrap round the sides of the box so the meadow never empties.
    float3 local = position - FieldMin;
    local.xz = local.xz - FieldSize.xz * floor(local.xz / FieldSize.xz);
    position = FieldMin + local;
    position.y = clamp(position.y, 0.03, 3.2);
    particle.position = position;
}

// Where a world point lands on screen in p units (y down), and its depth.
float3 toScreen(float3 position)
{
    float3 seen = project(position, cameraEye(), cameraTarget(), Zoom, 0.0);
    return float3(seen.x, -seen.y, seen.z);
}

Sprite Show(Particle particle, uint id)
{
    Sprite sprite = (Sprite)0;
    float3 seen = toScreen(particle.position);
    if (seen.z < 1.2)
    {
        return sprite;
    }

    // Its own slow blink: a quick swell of light, then a long dark pause.
    float cycle = frac(Time * particle.data.y + particle.data.x);
    float flash = exp(-pow((cycle - 0.1) / 0.05, 2.0));
    // The beat's wave: a ring of flashes spreading outward over the grass, fading as it goes.
    float age = sinceBeat();
    float reach = age * 7.0;
    float apart = length(particle.position.xz - waveOrigin().xz);
    float wave = exp(-pow((apart - reach) / 0.9, 2.0)) * exp(-age * 0.9) * (0.5 + Bass * 0.9);
    // The fireflies each listen to their own part of the spectrum.
    float band = spectrum(0.05 + fixedRandom(id, 8.0) * 0.7);
    float light = 0.012 + flash * (0.45 + band * 0.9) + wave * 1.0;

    // Most glow yellow-green; a few are warmer amber.
    float3 colour = lerp(float3(0.62, 1.0, 0.18), float3(1.0, 0.72, 0.2), step(0.85, fixedRandom(id, 9.0)));
    colour = lerp(colour, float3(0.9, 1.0, 0.6), saturate(wave * 0.6));
    // Further ones fade into the night mist.
    colour *= light * exp(-seen.z * 0.06) * 2.2 * saturate(particle.age * 0.5);

    sprite.position = seen.xy;
    sprite.size = 1.5 + flash * 0.8;
    sprite.colour = colour;
    float3 before = toScreen(particle.position - particle.velocity * 0.08);
    sprite.streak = seen.xy - before.xy;
    focus(sprite, seen.z, 7.0, 0.07);
    return sprite;
}

// ---- The scene behind and around the fireflies ----

float starField(float3 direction)
{
    float2 q = float2(atan2(direction.x, direction.z), direction.y) * 180.0;
    float2 cell = floor(q);
    float2 f = frac(q) - 0.5;
    float h = hash(cell);
    float2 offset = float2(hash(cell + 7.1), hash(cell + 3.3)) - 0.5;
    float star = step(0.93, h) * exp(-dot(f - offset * 0.6, f - offset * 0.6) * 40.0);
    float twinkle = 0.6 + 0.4 * sin(Time * (2.0 + h * 5.0) + h * 40.0) * (0.4 + Treble);
    return star * twinkle * (h - 0.9) * 12.0;
}

float3 sky(float3 direction)
{
    float up = saturate(direction.y);
    float3 colour = lerp(float3(0.03, 0.045, 0.085), float3(0.004, 0.006, 0.018), pow(up, 0.5));
    float facing = saturate(dot(direction, normalize(MoonDirection)));
    // The moon: a pale disc with faint grey seas, a halo in the haze.
    float disc = smoothstep(0.99955, 0.99975, facing);
    float3 local = direction - normalize(MoonDirection);
    float seas = fbm(local.xy * 400.0 + 3.0);
    colour += float3(1.0, 0.97, 0.88) * disc * (2.2 - seas * 1.1);
    colour += float3(0.35, 0.42, 0.6) * (pow(facing, 300.0) * 0.4 + pow(facing, 20.0) * 0.05) * (0.9 + Level * 0.3);
    // Thin clouds catching the moonlight, slowly passing.
    float2 cloudPlane = direction.xz / max(direction.y, 0.05) * 0.6 + float2(Time * 0.01, 0.0);
    float cloud = smoothstep(0.45, 0.85, fbm(cloudPlane));
    colour = lerp(colour, float3(0.06, 0.07, 0.1) + float3(0.25, 0.27, 0.32) * pow(facing, 8.0), cloud * 0.55 * smoothstep(0.0, 0.15, direction.y));
    colour += starField(direction) * (1.0 - cloud) * smoothstep(0.02, 0.2, direction.y) * (1.0 - disc);
    return colour;
}

// The far tree line: a ragged dark edge just above the horizon.
float treeLine(float3 direction)
{
    float around = atan2(direction.x, direction.z);
    float height = 0.025 + 0.035 * fbm(float2(around * 9.0, 1.0)) + 0.03 * pow(noise(float2(around * 40.0, 3.0)), 3.0);
    return smoothstep(height + 0.002, height - 0.002, direction.y);
}

float3 meadow(float3 eye, float3 direction)
{
    float t = -eye.y / min(direction.y, -1e-4);
    float3 position = eye + direction * t;
    float texture_ = fbm(position.xz * 0.8);
    float3 ground = float3(0.01, 0.02, 0.016) * (0.5 + texture_) + float3(0.012, 0.016, 0.026) * 0.5;
    // The fireflies' combined glow lying on the grass, and the mist towards the horizon.
    ground += float3(0.02, 0.035, 0.008) * (0.4 + Level * 0.6) * exp(-t * 0.08);
    float mist = 1.0 - exp(-t * 0.06);
    return lerp(ground, float3(0.035, 0.045, 0.07), mist);
}

// One layer of grass blades in front of the camera, drawn as silhouettes on the screen.
// Returns how much of the pixel the blades cover (x) and how much of their edge faces the moon (y).
float2 grassLayer(float2 p, float layer, float shift, float bottom, float tallest, float width)
{
    float2 q = float2(p.x + shift, p.y);
    float cellWidth = width * 3.2;
    float cell = floor(q.x / cellWidth);
    float cover = 0.0;
    float rim = 0.0;
    float edge = pixelSize() * 1.2;
    [unroll]
    for (int c = -2; c <= 2; c++)
    {
        [unroll]
        for (int k = 0; k < 2; k++)
        {
            float index = cell + c;
            float2 seed = float2(index * 2.0 + k, layer * 17.0);
            float h = tallest * lerp(0.35, 1.0, hash(seed));
            float baseX = (index + hash(seed + 5.3)) * cellWidth;
            float along = (bottom - q.y) / h;
            if (along > 0.0 && along < 1.0)
            {
                // Each blade arcs over, swaying in the breeze and with the bass.
                float lean = (hash(seed + 9.1) - 0.5) * 0.8 * h;
                float sway = (sin(Time * 0.9 + baseX * 3.0 + layer) * 0.04 + Bass * 0.03 * sin(Time * 2.3 + baseX * 7.0)) * h;
                float centre = baseX + (lean + sway) * along * along;
                float halfWidth = width * pow(1.0 - along, 0.8);
                float d = abs(q.x - centre) - halfWidth;
                float blade = smoothstep(edge, -edge, d);
                cover = max(cover, blade);
                // The edge on the moon's side picks up a cool rim of light.
                float side = (q.x < centre + lean * 0.1) ? 1.0 : 0.0;
                rim = max(rim, blade * side * smoothstep(-edge * 3.0, 0.0, d) * along);
            }
        }
    }
    return float2(cover, rim);
}

float3 Render(float2 uv, float2 p)
{
    float3 eye = cameraEye();
    float3 direction = cameraRay(eye, cameraTarget(), float2(p.x, -p.y), Zoom, 0.0);

    float3 colour = direction.y > 0.0 ? sky(direction) : meadow(eye, direction);
    float trees = treeLine(direction) * step(0.0, direction.y);
    colour = lerp(colour, float3(0.006, 0.008, 0.014) + float3(0.012, 0.014, 0.02) * (1.0 - exp(-direction.y * 30.0)), trees);
    // A low band of mist lying over the meadow at the foot of the trees.
    colour += float3(0.03, 0.04, 0.06) * exp(-abs(direction.y + 0.01) * 40.0) * 0.6;

    colour += particles(uv);

    // Grass in the foreground, in three layers sliding past at different speeds as the camera drifts.
    float3 grassColour = float3(0.002, 0.003, 0.004);
    float3 rimColour = float3(0.1, 0.13, 0.2);
    // The fireflies just beside a blade light it softly green.
    float2 px = float2(pixelSize() * 6.0 / Aspect, pixelSize() * 6.0) * 0.5;
    float3 nearby = particles(uv + float2(px.x, 0.0)) + particles(uv - float2(px.x, 0.0)) + particles(uv + float2(0.0, px.y)) + particles(uv - float2(0.0, px.y));
    [unroll]
    for (int layer = 0; layer < 3; layer++)
    {
        float depth = 2.0 - layer;
        float2 blades = grassLayer(p, depth, eye.x * (1.4 - depth * 0.35) + depth * 13.0, 1.05 + depth * 0.03, lerp(0.48, 0.22, depth * 0.5), lerp(0.011, 0.006, depth * 0.5));
        float3 layerColour = grassColour * (1.0 + depth) + rimColour * blades.y * (1.0 - depth * 0.25) + nearby * 0.15;
        colour = lerp(colour, layerColour, blades.x);
    }
    return colour;
}
