// Name: Caustic Rings
// Each beat drops a ring of ripples into clear water; the light they bend draws shimmering caustic rings on the floor below, shaped by that beat's spectrum.

// Memory: the first 109 pixels of the top row of the feedback buffer, stored far too dim to see.
// Pixel 0 holds the beat detector; then 12 ring slots of 9 pixels each: a header (birth time,
// strength, hue) and 8 pixels holding a 24-band snapshot of the spectrum (three bands per pixel).
// Birth times are kept modulo SonarPeriod, so ages come from Time itself and never drift.
static const float SonarStore = 0.004;
static const float SonarSlots = 12.0;
static const float SonarSlotWidth = 9.0;
static const float SonarBands = 24.0;
static const float SonarMaxAge = 4.5;
static const float SonarPeriod = 16.0;

float3 sonarEncode(float3 v)
{
    return (0.3 + 0.6 * saturate(v)) * SonarStore;
}

bool sonarValid(float3 stored)
{
    float3 t = stored / SonarStore;
    return all(t > 0.27) && all(t < 0.93);
}

float3 sonarDecode(float3 stored)
{
    return (stored / SonarStore - 0.3) / 0.6;
}

float3 sonarFetch(float index)
{
    return previous(float2((index + 0.5) / Resolution.x, 0.5 / Resolution.y));
}

// The live spectrum for a snapshot band, smoothed.
float sonarLive(float band)
{
    float x = (band + 0.5) / SonarBands * 0.88 + 0.03;
    float s = spectrum(x) * 0.5 + (spectrum(x - 0.015) + spectrum(x + 0.015)) * 0.25;
    return saturate((s - 0.1) / 0.9);
}

// A ring's stored level for mirrored angle position u (0..1).
float sonarLevel(float slot, float u)
{
    float fb = clamp(u * (SonarBands - 1.0), 0.0, SonarBands - 1.001);
    float b0 = floor(fb);
    float b1 = b0 + 1.0;
    float base = 1.0 + slot * SonarSlotWidth + 1.0;
    float3 s0 = sonarFetch(base + floor(b0 / 3.0));
    float3 s1 = sonarFetch(base + floor(b1 / 3.0));
    float c0 = b0 - floor(b0 / 3.0) * 3.0;
    float c1 = b1 - floor(b1 / 3.0) * 3.0;
    float v0 = dot(sonarDecode(s0), float3(step(c0, 0.5), step(0.5, c0) * step(c0, 1.5), step(1.5, c0)));
    float v1 = dot(sonarDecode(s1), float3(step(c1, 0.5), step(0.5, c1) * step(c1, 1.5), step(1.5, c1)));
    bool ok = sonarValid(s0) && sonarValid(s1);
    return ok ? lerp(v0, v1, smoothstep(0.0, 1.0, fb - b0)) : 0.0;
}

float3 sonarHue(float seed)
{
    float3 c = 0.5 + 0.5 * cos(2.0 * PI * (seed + float3(0.0, 0.33, 0.67)));
    return c * c * 1.2 + 0.05;
}

// A travelling wave packet: its height profile at s (distance past the front) and two derivatives.
float3 causticPacket(float s, float width, float wave)
{
    float k = 1.0 / (width * width);
    float e = exp(-s * s * k);
    float e1 = -2.0 * s * k * e;
    float e2 = (4.0 * s * s * k * k - 2.0 * k) * e;
    float c = cos(wave * s);
    float c1 = -wave * sin(wave * s);
    float c2 = -wave * wave * c;
    return float3(e * c, e1 * c + e * c1, e2 * c + 2.0 * e1 * c1 + e * c2);
}

// Adds the curvature (Hessian xx, yy, xy) of a radial wave f(r) with slope f1 and curvature f2.
float3 causticRadial(float2 n, float r, float f1, float f2)
{
    float bend = f1 / r;
    return float3(f2 * n.x * n.x + bend * (1.0 - n.x * n.x),
                  f2 * n.y * n.y + bend * (1.0 - n.y * n.y),
                  (f2 - bend) * n.x * n.y);
}

// Age in seconds of a ring born at the stored phase.
float sonarAge(float birth)
{
    return frac(Time / SonarPeriod - birth) * SonarPeriod;
}

// Where the drops fall: a point that wanders slowly, so successive rings are nested off-centre.
float2 sonarDrop(float t)
{
    float a = t * 0.7;
    return float2(cos(a), sin(a)) * (0.35 + 0.15 * sin(t * 0.21));
}

float3 Render(float2 uv, float2 p)
{
    // ---- Memory pixels ----
    float2 pixel = floor(uv * Resolution);
    if (pixel.y < 1.0 && pixel.x < 1.0 + SonarSlots * SonarSlotWidth)
    {
        float3 control = sonarFetch(0.0);
        bool known = sonarValid(control);
        float3 state = known ? sonarDecode(control) : float3(1.0, 0.0, 0.0);
        float lastBeat = state.x;
        float counter = state.z;
        float3 newest = sonarFetch(1.0);
        float3 newestInfo = sonarValid(newest) ? sonarDecode(newest) : float3(0.0, 0.0, 0.0);
        float sinceRing = frac(newestInfo.y * 2.0) > 0.0 ? sonarAge(newestInfo.x) : 10.0;
        // A ring on every beat, and a quieter one now and then when the music has no beat.
        bool onBeat = Beat > lastBeat + 0.25 && Beat > 0.5;
        bool idle = sinceRing > 2.2;
        bool emit = known && (onBeat || idle);
        float nextCounter = emit ? frac(counter + 0.13) : counter;
        float side = state.y > 0.5 ? 1.0 : 0.0;
        float nextSide = emit ? 1.0 - side : side;

        if (pixel.x < 0.5)
        {
            return sonarEncode(float3(Beat, nextSide, nextCounter));
        }

        float slot = floor((pixel.x - 1.0) / SonarSlotWidth);
        float within = pixel.x - 1.0 - slot * SonarSlotWidth;
        if (emit && slot < 0.5)
        {
            if (within < 0.5)
            {
                // Drops fall alternately on two sides; the side rides along with the strength.
                float strength = onBeat ? 0.4 + 0.58 * saturate(Bass * 1.2) : 0.75;
                return sonarEncode(float3(frac(Time / SonarPeriod), (nextSide + strength) * 0.5, nextCounter));
            }
            float first = (within - 1.0) * 3.0;
            return sonarEncode(float3(sonarLive(first), sonarLive(first + 1.0), sonarLive(first + 2.0)));
        }
        // Otherwise keep the slot (or take the one before it, when a new ring pushes them along).
        float source = emit ? pixel.x - SonarSlotWidth : pixel.x;
        float3 stored = sonarFetch(source);
        if (!sonarValid(stored))
        {
            return sonarEncode(float3(0.0, 0.0, 0.0));
        }
        float3 value = sonarDecode(stored);
        if (within < 0.5 && sonarAge(value.x) > SonarMaxAge)
        {
            value.y = 0.0;      // this ring has died away
        }
        return sonarEncode(value);
    }

    // ---- Looking straight down through the water at the floor ----
    float2 q = rotate(p, Time * 0.02) * 1.25;

    float3 hess = 0.0;
    float3 tint = 0.0;
    float tintWeight = 1e-4;
    float3 splash = 0.0;

    const float speed = 0.7;
    [loop]
    for (int k = 0; k < 12; k++)
    {
        float slot = k;
        float3 header = sonarFetch(1.0 + slot * SonarSlotWidth);
        if (!sonarValid(header))
        {
            continue;
        }
        float3 info = sonarDecode(header);
        float age = sonarAge(info.x);
        float sideFlag = info.y >= 0.5 ? 1.0 : 0.0;
        float strength = info.y * 2.0 - sideFlag;
        float2 centre = sonarDrop(Time - age) * (1.0 - 2.0 * sideFlag);
        float2 offset = q - centre;
        float r = max(length(offset), 0.03);
        float s = r - age * speed;
        if (strength > 0.01 && age < SonarMaxAge && s < 0.6 && s > -1.0)
        {
            float u = abs(atan2(offset.x, offset.y)) / PI;
            float level = sonarLevel(slot, u);
            float fade = exp(-age * 0.25) * smoothstep(0.0, 0.15, age) * smoothstep(4.2, 2.6, age) / sqrt(0.3 + r);
            float a = strength * fade * (0.35 + level * level) * 0.012;
            // The ring bulges outward where its beat was loud, so it carries the spectrum's shape.
            s -= level * level * 0.06;
            float3 g = causticPacket(s, 0.25 + age * 0.04, 2.0 * PI / 0.3);
            hess += causticRadial(offset / r, r, a * g.y, a * g.z);
            float weight = abs(a * g.z);
            tint += sonarHue(info.z) * weight;
            tintWeight += weight;
        }
        // A soft splash of light where a drop has just landed.
        splash += sonarHue(info.z) * strength * exp(-age * 5.0) * exp(-r * r * 25.0);
    }

    // Light through the surface: where it focuses (the Jacobian of the refraction collapses) it burns
    // bright. Each colour bends a little differently, so the lines split into rainbows.
    float3 depth = float3(0.8, 1.0, 1.22);
    float3 jxx = 1.0 + depth * hess.x;
    float3 jyy = 1.0 + depth * hess.y;
    float3 jxy = depth * hess.z;
    float3 det = jxx * jyy - jxy * jxy;
    // Box-filter the focus over the pixel so the sharpest lines do not break into dots.
    float3 focus = 1.0 / (abs(det) + fwidth(det) * 0.35 + 0.02);

    // The three fringes take neighbouring colours of a palette that drifts with the music.
    float3 light = pow(max(focus - 1.0, 0.0), 2.5) * 0.02;
    light = light / (1.0 + light * 0.5);      // tame the hottest focus points so the glow stays clean
    float drift = Time * 0.025;
    float3 ringTint = tint / tintWeight;
    float3 colour = (sonarHue(drift) * light.r + sonarHue(drift + 0.14) * light.g + sonarHue(drift + 0.28) * light.b)
                  * lerp(1.0, ringTint, 0.6) * 1.5;
    colour += splash * 0.5;
    colour += sonarHue(Time * 0.02 + length(q) * 0.15) * 0.008;
    return colour;
}
