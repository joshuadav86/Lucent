// Name: Mercury Lake
// A still lake of liquid mercury under a deep twilight sky where neon auroras ripple; the silver surface mirrors it all, and every beat drops a ring of slow, heavy ripples that bend the reflections.
// Family: Ray traced
// Tags: Water, Dark, 3D

// The lake is a mirror: liquid metal reflects most light at every angle and nearly all of it at
// grazing angles. All the interest comes from what it reflects (the sky, the aurora, the hills
// and the moon) and from the ripples, which tilt the mirror and so warp the reflections.
static float2 PixelPosition;   // this pixel, in pixels, for the aurora's sampling offsets
static const float3 MoonDirection = float3(-0.32, 0.2, 0.92);   // towards the moon (normalised in use)

float sinceBeat()
{
    return -log(max(Beat, 1e-4)) / 5.0;
}

float3 cameraEye()
{
    return float3(sin(Time * 0.021) * 3.0, 0.55 + sin(Time * 0.033) * 0.12, Time * 0.12);
}

float3 cameraTarget(float3 eye)
{
    return eye + float3(sin(Time * 0.027) * 0.6, 0.12 + sin(Time * 0.019) * 0.04, 1.0);
}

// Each of the last three beats dropped a ring of ripples somewhere on the lake ahead. Older ones
// are timed from an average gap between beats, which is close enough once they've spread and faded.
float beatRipples(float2 q)
{
    float h = 0.0;
    float age = sinceBeat();
    float3 eye = cameraEye();
    [unroll]
    for (int k = 0; k < 3; k++)
    {
        float beat = BeatCount - k;
        float2 centre = eye.xz + float2((hash(float2(beat, 1.7)) - 0.5) * 3.0, 2.0 + hash(float2(beat, 5.3)) * 3.0);
        float t = age + k * 0.5;
        float front = length(q - centre) - t * 1.1;
        h += 0.022 * exp(-t * 0.9) * sin(front * 16.0) * exp(-front * front * 6.0) * step(0.5, BeatCount - k + 1.0) * (0.6 + Bass * 0.6);
    }
    return h;
}

// Mercury is heavy, so its waves are low, long and slow.
float lakeHeight(float2 q)
{
    float swell = 0.004 + Bass * 0.006;
    float h = sin(dot(q, float2(0.6, 0.8)) * 2.1 + Time * 0.7) * swell;
    h += sin(dot(q, float2(-0.9, 0.45)) * 3.3 + Time * 0.9) * swell * 0.6;
    h += sin(dot(q, float2(0.3, -0.95)) * 5.7 + Time * 1.3) * swell * 0.35;
    h += (noise(q * 2.5 + Time * 0.15) - 0.5) * swell * 0.8;
    return h + beatRipples(q);
}

float3 lakeNormal(float2 q, float distance)
{
    const float e = 0.01;
    float h = lakeHeight(q);
    float dx = lakeHeight(q + float2(e, 0.0)) - h;
    float dz = lakeHeight(q + float2(0.0, e)) - h;
    // Far ripples are too fine to see; let the surface settle into a clean mirror with distance.
    float detail = exp(-distance * 0.12);
    return normalize(float3(-dx * detail, e, -dz * detail));
}

// The aurora: glowing curtains hung high in the sky. The same pattern of wavy ribbons is drawn
// on a stack of thin layers one above another, which stretches each ribbon into a tall, streaky
// sheet of light, brightest at its hem.
float curtain(float2 q, float sharpness)
{
    float total = 0.0;
    [unroll]
    for (int j = 0; j < 3; j++)
    {
        float middle = 3.0 + j * 2.2 + sin(q.x * 0.35 + Time * 0.06 + j * 2.0) * 1.2 + (noise(float2(q.x * 0.8 + j * 7.0, Time * 0.1)) - 0.5) * 1.4;
        float ribbon = exp(-abs(q.y - middle) * (14.0 - j * 3.0)) + exp(-abs(q.y - middle) * 3.0) * 0.15;
        // Fine vertical rays, smoothed towards their average where they'd be finer than a pixel.
        float rays = lerp(0.45, pow(noise(float2(q.x * 7.0 + j * 3.0, Time * 0.25)), 2.0) * 1.6 + 0.1, sharpness);
        total += ribbon * rays * (1.0 - j * 0.25);
    }
    return total;
}

float3 aurora(float3 direction)
{
    if (direction.y < 0.03)
    {
        return 0.0;
    }
    float3 colour = 0.0;
    int layers = qualitySteps(28);
    float3 eye = cameraEye();
    float2 sway = float2(0.0, -eye.z * 0.03);
    // Start each pixel's stack of layers at a slightly different height so the layers blend
    // into smooth streaks instead of showing as bands.
    // Interleaved gradient noise: an even, fine-grained offset that the frame blend averages away.
    float jitter = frac(52.9829189 * frac(dot(PixelPosition, float2(0.06711056, 0.00583715))) + frac(FrameCount * 0.618034));
    [loop]
    for (int i = 0; i < layers; i++)
    {
        float f = (i + jitter) / layers;
        float height = 1.0 + f * 1.4;
        // Each layer sees the same ribbons from a little higher up.
        float2 q = direction.xz * (height / direction.y) + sway;
        // How much sky one pixel covers at this layer: it grows fast towards the horizon.
        float footprint = 0.004 * height / (direction.y * direction.y);
        float density = curtain(q, saturate(0.12 / footprint));
        // Green at the hem, shading up through cyan to violet and magenta at the top, nudged by the music.
        float hue = 0.36 + f * 0.48 + spectrum(f * 0.6) * 0.08 + sin(Time * 0.04) * 0.04;
        colour += hsv2rgb(frac(hue), 0.9, 1.0) * density * exp(-f * 2.6);
    }
    float fade = smoothstep(0.03, 0.2, direction.y);
    return colour / layers * fade * (3.0 + Mid * 5.0);
}

// Low hills round the lake, as dark silhouettes against the glowing horizon.
float hillHeight(float3 direction)
{
    float around = atan2(direction.x, direction.z);
    return 0.005 + 0.035 * fbm(float2(around * 3.0, 1.0)) + 0.012 * sin(around * 2.0 + 1.0);
}

float3 sky(float3 direction, float moonDisc)
{
    float y = direction.y;
    float3 moon = normalize(MoonDirection);
    // Twilight: deep indigo overhead, a band of dusky rose and amber along the horizon.
    float3 colour = lerp(float3(0.05, 0.04, 0.1), float3(0.004, 0.008, 0.03), smoothstep(0.0, 0.6, y));
    float glowSide = pow(saturate(dot(normalize(float3(direction.x, 0.0, direction.z) + 1e-5), normalize(float3(0.6, 0.0, 0.8))) * 0.5 + 0.5), 3.0);
    colour += float3(0.3, 0.1, 0.08) * exp(-max(y, 0.0) * 16.0) * (0.15 + glowSide * 0.8);
    // Stars twinkling with the treble.
    float3 cell = floor(direction * 280.0);
    float star = hash(cell.xy + cell.z * float2(17.31, 41.7));
    colour += step(0.998, star) * smoothstep(0.05, 0.3, y) * (0.3 + Treble * 0.6 * frac(star * 97.0 + Time)) * float3(0.8, 0.85, 1.0);
    colour += aurora(direction);
    // The moon, with a soft halo.
    float toMoon = dot(direction, moon);
    colour += float3(1.0, 0.95, 0.85) * smoothstep(0.99975, 0.99985, toMoon) * 1.3 * moonDisc;
    colour += float3(0.5, 0.45, 0.6) * pow(saturate(toMoon), 800.0) * 0.15;
    // The hills cut off everything below their ridge.
    float hills = hillHeight(direction);
    if (y < hills)
    {
        colour = float3(0.006, 0.005, 0.01) + float3(0.08, 0.03, 0.03) * exp(-(hills - y) * 60.0) * (0.3 + glowSide);
    }
    return colour;
}

// Slopes of the small, quick ripples that make the moon glitter: a few short waves running
// different ways, plus a little noise to break up their pattern.
float2 glitterSlope(float2 q)
{
    float2 slope = 0.0;
    float2 dirs[4] = { float2(0.8, 0.6), float2(-0.5, 0.87), float2(0.96, -0.28), float2(-0.2, -0.98) };
    float speeds[4] = { 1.7, 2.1, 2.6, 1.3 };
    [unroll]
    for (int k = 0; k < 4; k++)
    {
        float frequency = 17.0 + k * 9.0;
        slope += dirs[k] * cos(dot(q, dirs[k]) * frequency + Time * speeds[k] + k * 1.7) * (0.045 - k * 0.006);
    }
    slope += (float2(noise(q * 23.0 + Time * 0.9), noise(q * 23.0 + 7.3 - Time * 0.8)) - 0.5) * 0.14;
    return slope * (0.8 + Bass * 0.5);
}

float3 trace(float3 eye, float3 direction)
{
    if (direction.y >= -0.002)
    {
        return sky(direction, 1.0);
    }
    float t = -eye.y / direction.y;
    float3 position = eye + direction * t;
    float3 normal = lakeNormal(position.xz, t);
    float3 reflected = reflect(direction, normal);
    reflected.y = abs(reflected.y);
    // Liquid metal: a bright silver mirror, nearly perfect at grazing angles.
    float cosine = saturate(dot(normal, -direction));
    float3 metal = float3(0.78, 0.8, 0.84);
    float3 reflectance = metal + (1.0 - metal) * pow(1.0 - cosine, 5.0);
    // The moon's own disc is left out of the mirror image: on rippled metal it breaks up into
    // a path of glints instead, drawn below.
    float3 colour = sky(reflected, 0.0) * reflectance;

    // Glitter path: tiny fast ripples tilt each patch of the surface a little differently, and
    // every patch tilted just right flashes with the moon. Far away the ripples are finer than a
    // pixel, so there the glints merge into a soft, wider band carrying the same light.
    float3 moon = normalize(MoonDirection);
    float detail = exp(-t * 0.07);
    float2 fine = glitterSlope(position.xz);
    float3 tilted = normalize(normal + float3(fine.x, 0.0, fine.y) * detail);
    float3 bounced = reflect(direction, tilted);
    float spread = 0.018 + (1.0 - detail) * 0.05;
    float glint = exp(-2.0 * (1.0 - dot(bounced, moon)) / (spread * spread)) * (0.03 / spread);
    colour += float3(1.0, 0.95, 0.88) * glint * reflectance * (3.0 + Treble * 2.5);
    // A faint grey sheen so the surface reads as a surface even where the sky is dark.
    colour += float3(0.01, 0.01, 0.014) * (1.0 - cosine);
    // Mist hanging over the far water.
    float mist = 1.0 - exp(-t * 0.025);
    return lerp(colour, float3(0.05, 0.03, 0.06), mist * 0.6);
}

float3 Render(float2 uv, float2 p)
{
    PixelPosition = floor(uv * Resolution);
    float3 eye = cameraEye();
    float3 target = cameraTarget(eye);
    float3 direction = cameraRay(eye, target, float2(p.x, -p.y), 1.5, sin(Time * 0.02) * 0.02);
    float3 colour = trace(eye, direction);
    // A whisper of the last frame smooths the fine shimmer on the ripples and the glitter.
    // The aurora's layered streaks get a longer blend, which hides their sampling grain.
    return lerp(colour, previous(uv), direction.y > 0.0 ? 0.55 : 0.35);
}
