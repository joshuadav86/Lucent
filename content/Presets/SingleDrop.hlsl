// Name: Single Drop
// A dark, perfectly still pool under a low moon. On each downbeat a single drop falls into it, a tiny bead leaps back up,
// and clean rings spread out across the water, catching the moonlight as they pass.
// Family: Minimal
// Tags: Minimal, Calm, Water, Dark
// Added: 2026-10-17 21:41

// Memory: four pixels at the top-left of the feedback buffer, stored far too dim to see.
// Pixel 0: the beat detector. Pixel 1: when the last three drops fell (as a fraction of DropPeriod).
// Pixel 2: how hard each one fell (0 once it has faded). Pixel 3: where each one fell.
static const float DropStore = 0.004;
static const float DropPeriod = 32.0;
static const float DropLife = 9.0;

float3 sdEncode(float3 v)
{
    return (0.3 + 0.6 * saturate(v)) * DropStore;
}

bool sdValid(float3 stored)
{
    float3 t = stored / DropStore;
    return all(t > 0.27) && all(t < 0.93);
}

float3 sdDecode(float3 stored)
{
    return (stored / DropStore - 0.3) / 0.6;
}

float3 sdFetch(float index)
{
    return previous(float2((index + 0.5) / Resolution.x, 0.5 / Resolution.y));
}

float3 sdLoad(float index, float3 fallback)
{
    float3 stored = sdFetch(index);
    return sdValid(stored) ? sdDecode(stored) : fallback;
}

float sdAge(float birth)
{
    return frac(Time / DropPeriod - birth) * DropPeriod;
}

// Where a drop with this seed lands on the water (x, z).
float2 sdSpot(float seed)
{
    float a = seed * 6.2831853;
    return float2(-0.22 + cos(a) * 0.16, 2.5 + sin(a) * 0.3);
}

// Height of the ripples left by one drop, at distance r from where it fell.
float sdRipple(float r, float age, float strength)
{
    float front = age * 0.45;
    float s = r - front;
    // A short train of rings behind the front, fading as they spread out.
    float envelope = exp(-s * s / (0.02 + age * 0.03)) * step(-1.2, s);
    float wave = cos(s * 34.0 / (1.0 + age * 0.35));
    float fade = strength * exp(-age * 0.35) * smoothstep(0.0, 0.08, age) / sqrt(0.15 + r * 3.0);
    // A second, slower ring from the bead falling back a moment later.
    float s2 = r - max(age - 0.32, 0.0) * 0.4;
    float second = 0.35 * exp(-s2 * s2 / (0.01 + age * 0.02)) * cos(s2 * 40.0) * step(0.32, age);
    return (envelope * wave + second) * fade * 0.02;
}

// The night sky: deep blue fading to a pale band at the horizon, with a soft moon and its halo.
float3 sdSky(float3 dir, float3 moonDir)
{
    float h = saturate(dir.y);
    float3 sky = lerp(float3(0.10, 0.12, 0.18), float3(0.008, 0.012, 0.03), pow(h, 0.4));
    float c = dot(dir, moonDir);
    float disc = smoothstep(0.9988, 0.9994, c);
    // The moon itself sits just above the frame; it is bright enough that even the dark water's faint reflection of it shines.
    sky += float3(1.0, 0.95, 0.86) * disc * (16.0 + Level * 4.0);
    sky += float3(0.55, 0.6, 0.75) * 0.16 * exp((c - 1.0) * 30.0);
    sky += float3(0.4, 0.45, 0.6) * 0.06 * exp((c - 1.0) * 5.0);
    return sky;
}

float3 Render(float2 uv, float2 p)
{
    // ---- Memory pixels ----
    float2 pixel = floor(uv * Resolution);
    if (pixel.y < 1.0 && pixel.x < 4.0)
    {
        float3 control = sdFetch(0.0);
        bool known = sdValid(control);
        float3 state = known ? sdDecode(control) : float3(1.0, 0.0, 0.0);
        float3 births = sdLoad(1.0, float3(0.0, 0.0, 0.0));
        float3 strengths = sdLoad(2.0, float3(0.0, 0.0, 0.0));
        float3 seeds = sdLoad(3.0, float3(0.0, 0.3, 0.6));

        // A drop on every downbeat (every fourth beat), and a gentle one now and then if the music has no beat.
        bool onBeat = Beat > state.x + 0.25 && Beat > 0.5;
        bool downbeat = onBeat && fmod(BeatCount, 4.0) < 0.5;
        bool idle = strengths.x < 0.01 || sdAge(births.x) > 6.0;
        bool emit = known && (downbeat || idle);
        float counter = emit ? frac(state.y + 0.381966) : state.y;

        if (emit)
        {
            births = float3(frac(Time / DropPeriod), births.xy);
            strengths = float3(downbeat ? 0.55 + 0.45 * saturate(Bass * 1.3) : 0.5, strengths.xy);
            seeds = float3(counter, seeds.xy);
        }
        // Drops that have faded away are switched off, so their old birth time can never seem new again.
        strengths.x = sdAge(births.x) > DropLife ? 0.0 : strengths.x;
        strengths.y = sdAge(births.y) > DropLife ? 0.0 : strengths.y;
        strengths.z = sdAge(births.z) > DropLife ? 0.0 : strengths.z;

        if (pixel.x < 0.5)
        {
            return sdEncode(float3(Beat, counter, 0.0));
        }
        if (pixel.x < 1.5)
        {
            return sdEncode(births);
        }
        if (pixel.x < 2.5)
        {
            return sdEncode(strengths);
        }
        return sdEncode(seeds);
    }

    // ---- The scene ----
    float3 births = sdLoad(1.0, float3(0.0, 0.0, 0.0));
    float3 strengths = sdLoad(2.0, float3(0.0, 0.0, 0.0));
    float3 seeds = sdLoad(3.0, float3(0.0, 0.3, 0.6));

    float3 eye = float3(sin(Time * 0.03) * 0.2, 0.85 + sin(Time * 0.021) * 0.05, 0.0);
    float3 target = float3(-0.1 + sin(Time * 0.017) * 0.08, 0.0, 3.0);
    float3 dir = cameraRay(eye, target, float2(p.x, -p.y), 1.9, sin(Time * 0.02) * 0.01);
    float3 moonDir = normalize(float3(-0.08 + sin(Time * 0.004) * 0.03, 0.36, 1.0));

    float3 colour = sdSky(dir, moonDir);

    if (dir.y < -1e-4)
    {
        float t = -eye.y / dir.y;
        float3 hit = eye + dir * t;

        // Gradient of the water's surface: the drops' rings plus the faintest breath of wind.
        float2 slope = float2(0.0, 0.0);
        [unroll]
        for (int i = 0; i < 3; i++)
        {
            float age = sdAge(births[i]);
            float strength = strengths[i];
            if (strength > 0.01 && age < DropLife)
            {
                float2 offset = hit.xz - sdSpot(seeds[i]);
                float r = max(length(offset), 1e-3);
                float e = 0.002;
                float dh = (sdRipple(r + e, age, strength) - sdRipple(r - e, age, strength)) / (2.0 * e);
                slope += dh * offset / r;
            }
        }
        float2 w = hit.xz * float2(6.0, 14.0) + float2(Time * 0.15, -Time * 0.1);
        float breeze = 0.007 + Treble * 0.006;
        slope += breeze * float2(noise(w) - noise(w + float2(0.3, 0.0)), noise(w) - noise(w + float2(0.0, 0.3))) * 3.3;
        // Far ripples blur into the surface rather than shimmering.
        slope *= 1.0 / (1.0 + t * t * 0.01);

        float3 normal = normalize(float3(-slope.x, 1.0, -slope.y));
        float3 reflected = reflect(dir, normal);
        reflected.y = abs(reflected.y);
        float cosine = saturate(-dot(dir, normal));
        float fresnel = 0.02 + 0.98 * pow(1.0 - cosine, 5.0);
        float3 water = float3(0.004, 0.007, 0.012);
        colour = lerp(water, sdSky(reflected, moonDir), fresnel);
        // Keep the brightest glints modest so they glow rather than sparkle as hard points.
        colour = colour / (1.0 + max(max(colour.r, colour.g), colour.b) * 0.35);
        // Mist on the far water so the horizon melts softly into the sky.
        colour = lerp(colour, sdSky(normalize(float3(dir.x, 0.0, dir.z)), moonDir), 1.0 - exp(-t * 0.035));
    }

    // ---- The little bead that leaps back out of the water after each drop ----
    [unroll]
    for (int j = 0; j < 3; j++)
    {
        float age = sdAge(births[j]);
        if (strengths[j] > 0.01 && age < 0.34)
        {
            float2 spot = sdSpot(seeds[j]);
            float height = 1.0 * age - 0.5 * 6.0 * age * age;
            float radius = 0.012 * strengths[j];
            float3 centre = float3(spot.x, max(height, 0.0) + radius, spot.y);
            float3 oc = eye - centre;
            float b = dot(oc, dir);
            float c = dot(oc, oc) - radius * radius;
            float h = b * b - c;
            if (h > 0.0)
            {
                float3 n = normalize(oc + dir * (-b - sqrt(h)));
                float3 glint = sdSky(reflect(dir, n), moonDir);
                float edge = saturate(h / (radius * radius) * 4.0);
                colour = lerp(colour, glint * (0.04 + 0.5 * pow(1.0 - saturate(dot(n, -dir)), 5.0)) + float3(0.02, 0.025, 0.035), edge);
            }
        }
    }
    return colour;
}
