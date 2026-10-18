// Name: Cloud Sea
// Glide low over an endless sea of clouds at golden hour, the low sun glowing through their billows and lining every edge with silver; the sunlight swells gently with the bass and each beat sends a slow ripple across the cloud tops.
// Family: Ray traced
// Tags: Calm, 3D
// Added: 2026-10-17 17:12

// The clouds are a layer of fog whose top is a rolling landscape of billows. Each pixel's ray
// marches down through the layer, adding up how much light each bit of cloud scatters towards
// the eye: sunlight that has made it through the cloud above it (a few short steps towards the
// sun tell how shadowed it is), glowing strongest when looking almost into the sun, plus the
// soft light of the sky.
static const float Floor = 0.0;
static const float Ceiling = 1.9;
static const float3 SunTowards = float3(0.35, 0.13, 1.0);

float sinceBeat()
{
    return -log(max(Beat, 1e-4)) / 5.0;
}

float3 sunDirection()
{
    return normalize(SunTowards + float3(sin(Time * 0.01) * 0.15, 0.0, 0.0));
}

float3 cameraEye()
{
    return float3(sin(Time * 0.023) * 3.0, 2.15 + sin(Time * 0.037) * 0.12, Time * 0.55);
}

// Rings spreading across the cloud tops from where the last few beats landed, ahead of us.
float beatRipples(float2 q)
{
    float h = 0.0;
    float age = sinceBeat();
    float3 eye = cameraEye();
    [unroll]
    for (int k = 0; k < 2; k++)
    {
        float beat = BeatCount - k;
        float2 centre = eye.xz + float2((hash(float2(beat, 2.3)) - 0.5) * 6.0, 6.0 + hash(float2(beat, 8.1)) * 5.0);
        float t = age + k * 0.5;
        float front = length(q - centre) - t * 2.2;
        h += 0.09 * exp(-t * 0.7) * sin(front * 4.0) * exp(-front * front * 0.6) * step(0.5, BeatCount - k + 1.0);
    }
    return h;
}

// The height of the cloud tops: big slow swells with billows on top.
float cloudTop(float2 q)
{
    float2 drift = float2(Time * 0.03, Time * 0.08);
    float shape = noise(q * 0.18 + drift * 0.3) * 0.75 + noise(q * 0.45 - drift) * 0.35 + noise(q * 1.1 + drift) * 0.16 + noise(q * 2.6 - drift * 1.5) * 0.08 + noise(q * 6.0 + drift) * 0.035;
    return 0.55 + shape * 0.95 + beatRipples(q);
}

// Smooth 3D value noise, 0..1, for the wisps.
float cloudNoise(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float2 lift = i.z * float2(37.0, 17.0);
    float a = lerp(lerp(hash(i.xy + lift), hash(i.xy + lift + float2(1.0, 0.0)), f.x),
                   lerp(hash(i.xy + lift + float2(0.0, 1.0)), hash(i.xy + lift + float2(1.0, 1.0)), f.x), f.y);
    lift += float2(37.0, 17.0);
    float b = lerp(lerp(hash(i.xy + lift), hash(i.xy + lift + float2(1.0, 0.0)), f.x),
                   lerp(hash(i.xy + lift + float2(0.0, 1.0)), hash(i.xy + lift + float2(1.0, 1.0)), f.x), f.y);
    return lerp(a, b, f.z);
}

// How dense the cloud is at a point: solid below the tops, with soft wisps and holes near them.
float density(float3 p, float top)
{
    float depth = top - p.y;
    if (depth < -0.12)
    {
        return 0.0;
    }
    float wisps = (cloudNoise(p * 2.2 + float3(0.0, Time * 0.05, Time * 0.08)) - 0.5) * 0.4 + (cloudNoise(p * 6.5 - Time * 0.1) - 0.5) * 0.18;
    return saturate((depth + wisps) * 3.0);
}

// The sky: warm gold and rose along the horizon, cooling to violet-blue overhead, with the sun.
float3 sky(float3 direction)
{
    float3 sun = sunDirection();
    float y = max(direction.y, 0.0);
    float toSun = saturate(dot(direction, sun));
    float3 colour = lerp(float3(0.95, 0.45, 0.22), float3(0.16, 0.16, 0.38), pow(y, 0.4));
    colour = lerp(colour, float3(0.1, 0.13, 0.32), smoothstep(0.08, 0.4, y));
    colour = lerp(colour, float3(0.04, 0.05, 0.15), smoothstep(0.4, 0.9, y));
    colour += float3(1.0, 0.6, 0.3) * pow(toSun, 8.0) * 0.6 + float3(1.0, 0.75, 0.45) * pow(toSun, 60.0) * 1.2;
    colour += float3(1.0, 0.9, 0.7) * smoothstep(0.9993, 0.9996, toSun) * 5.0;
    return colour * 0.6;
}

float sunStrength()
{
    return 3.0 * (1.0 + Bass * 0.25 + Beat * 0.05);
}

// Henyey-Greenstein: how much light scatters from the sun's direction towards the eye. A mix of a
// strong forward lobe (the silver linings when looking towards the sun) and a soft backward one.
float phase(float cosine)
{
    float g = 0.65;
    float forward = (1.0 - g * g) / pow(1.0 + g * g - 2.0 * g * cosine, 1.5);
    float g2 = -0.2;
    float backward = (1.0 - g2 * g2) / pow(1.0 + g2 * g2 - 2.0 * g2 * cosine, 1.5);
    return (forward * 0.6 + backward * 0.4) / (4.0 * PI);
}

float3 render(float3 eye, float3 direction, float jitter)
{
    float3 background = sky(direction);
    float3 sun = sunDirection();
    if (direction.y > -0.005)
    {
        // Looking above the cloud tops: just the sky.
        return background;
    }

    // Start where the ray dips into the top of the cloud layer, end at its bottom or the far haze.
    float start = max((Ceiling - eye.y) / direction.y, 0.0);
    float end = min((Floor - eye.y) / direction.y, 40.0);
    float3 light = 0.0;
    float transmittance = 1.0;
    float cosine = dot(direction, sun);
    float scatter = phase(cosine);
    float3 sunColour = float3(1.0, 0.62, 0.33) * sunStrength();
    float3 skyColour = float3(0.13, 0.13, 0.24);

    int steps = qualitySteps(56);
    float t = start;
    float stepSize = 0.04 + start * 0.004;
    t += stepSize * jitter;
    [loop]
    for (int i = 0; i < steps; i++)
    {
        if (t > end || transmittance < 0.02)
        {
            break;
        }
        float3 p = eye + direction * t;
        float top = cloudTop(p.xz);
        float d = density(p, top);
        if (d > 0.001)
        {
            // How much cloud lies between here and the sun: two short steps towards it.
            float shade = 0.0;
            [unroll]
            for (int j = 1; j <= 2; j++)
            {
                float3 q = p + sun * (0.12 * j * j);
                // (The billows' shape alone is enough for shadows; the fine wisps are skipped here.)
                shade += saturate((cloudTop(q.xz) - q.y) * 3.0 + 0.1) * 0.12 * j * j;
            }
            float towardsSun = exp(-shade * 8.0);
            // Thin, sunlit edges glow more than thick cloud (the "powder" look of real clouds).
            float powder = 1.0 - exp(-d * 4.0);
            float3 lit = sunColour * towardsSun * scatter * (0.4 + powder * 0.6) * 4.0;
            // Skylight from above, dimmer deeper down; and warm light bounced up from below.
            float height = saturate((p.y - top + 0.6) / 0.6);
            lit += skyColour * (0.1 + 0.3 * height) + float3(0.3, 0.12, 0.08) * (1.0 - height) * 0.08;
            float sigma = d * 9.0;
            float absorbed = 1.0 - exp(-sigma * stepSize);
            light += transmittance * absorbed * lit;
            transmittance *= 1.0 - absorbed;
        }
        // Take smaller steps in cloud, larger ones in clear air, and grow them with distance.
        t += stepSize * (d > 0.001 ? 1.0 : 1.8);
        stepSize *= 1.035;
    }

    // Distant cloud melts into the golden haze of the horizon.
    float haze = 1.0 - exp(-max(start - 2.0, 0.0) * 0.06);
    float3 colour = light + background * transmittance * 0.5;
    return lerp(colour, background * 0.95, haze);
}

float3 Render(float2 uv, float2 p)
{
    float3 eye = cameraEye();
    float3 target = eye + float3(sin(Time * 0.02) * 0.5 + 0.25, -0.32 + sin(Time * 0.029) * 0.05, 1.0);
    float roll = sin(Time * 0.023 + 1.0) * 0.06;
    float3 direction = cameraRay(eye, target, float2(p.x, -p.y), 1.35, roll);
    // A fresh offset for each pixel and frame hides the steps of the march; blending in a little
    // of the last frame smooths the grain away.
    float jitter = hash(uv * Resolution + frac(Time * 3.17) * 61.0);
    float3 colour = render(eye, direction, jitter);
    return lerp(colour, previous(uv), 0.35);
}
