// Name: Koch Snowflake
// An endless fall into a Koch snowflake built from smaller snowflakes, each built from smaller ones again, glittering like frost.

static const float KochRoot3 = 1.7320508;

// Signed distance to the Koch snowflake on a triangle of side 2 centred on the origin (negative inside).
float kochFlake(float2 p, int detail)
{
    float sector = round(atan2(p.x, -p.y) / (2.0 * PI / 3.0)) * (2.0 * PI / 3.0);
    float2 q = rotate(p, -sector);
    float2 k = float2(q.x, -q.y - 1.0 / KochRoot3);   // this side runs from x = -1 to 1, bumps towards +y
    const float2 bisector = float2(0.8660254, -0.5);
    float scale = 1.0;
    [loop]
    for (int i = 0; i < detail; i++)
    {
        k.x = abs(k.x);
        k -= 2.0 * min(dot(k - float2(1.0 / 3.0, 0.0), bisector), 0.0) * bisector;
        k = (k - float2(2.0 / 3.0, 0.0)) * 3.0;
        scale *= 3.0;
    }
    float dist = length(float2(k.x - clamp(k.x, -1.0, 1.0), k.y)) / scale;
    return k.y > 0.0 ? dist : -dist;
}

float3 kochIce(float t)
{
    // Frost hues only: cyan -> ice blue -> violet -> magenta and back.
    return hsv2rgb(0.62 + 0.13 * sin(2.0 * PI * t), 0.55, 1.0);
}

float3 Render(float2 uv, float2 p)
{
    // The snowflake splits into a central snowflake (1/sqrt3 the size, turned 30 degrees) and six of 1/3
    // the size around it, so zooming by sqrt3 while turning 30 degrees returns to the same picture: an endless dive.
    const float period = 7.0;
    float u = frac(Time / period);
    float dives = floor(Time / period);
    const float baseScale = 1.25;
    float2 local = rotate(p * baseScale, (PI / 6.0) * u) * exp2(-u * 0.7924813);   // sqrt3^-u
    float toScreen = exp2(u * 0.7924813) / baseScale;                               // screen units per local unit

    // Start three generations up so the outer snowflakes fill the screen.
    [unroll]
    for (int a = 0; a < 3; a++)
    {
        local = rotate(local / KochRoot3, PI / 6.0);
        toScreen *= KochRoot3;
    }

    float pixel = 2.0 / min(Resolution.y, 540.0);    // how much detail to draw: 1/540ths of the screen at high resolutions
    float linePixel = 2.0 * pixelSize();             // line widths: two screen pixels, so they stay crisp at 4K
    float hue = Time * 0.01 + (BeatCount - Beat) * 0.025;
    float3 colour = 0.0;
    float generation = -3.0 - u;               // continuous generation index relative to the screen
    float pulse = (1.0 - Beat) * 7.0 - 3.0;    // a beat ripples inward through the generations
    float sideWeight = 1.0;                    // snowflakes off the central chain are drawn fainter
    bool onChain = true;
    float lineGlow = 0.0;

    int levels = max(qualitySteps(9), 8);
    [loop]
    for (int level = 0; level < levels; level++)
    {
        float sizePx = toScreen / pixel;      // half the snowflake's side, in pixels
        int detail = (int)clamp(log2(max(sizePx, 1.0)) * 0.63 - 1.0, 0.0, 6.0);
        float d = kochFlake(local, detail);
        float dpx = abs(d) * toScreen / linePixel;
        float glowPx = abs(d) * toScreen / pixel;

        float visible = saturate((sizePx - 14.0) / 30.0);
        float prominence = exp(-abs(log2(toScreen) + 0.3) * 0.6);   // the chain snowflake nearest screen size is brightest
        float ripple = saturate(1.0 - abs(generation - pulse) * 0.7);
        float3 tint = kochIce(hue + generation * 0.09);

        float lineCore = saturate(1.3 - dpx * 0.8);
        float halo = exp(-glowPx * (0.25 - Bass * 0.08));
        float strength = visible * sideWeight * (0.3 + 0.9 * prominence + ripple * 0.7);
        colour += lerp(tint, float3(1.0, 1.0, 1.0), 0.35 * prominence) * lineCore * strength
                + tint * halo * (0.1 + 0.1 * Bass) * prominence * visible * sideWeight;
        lineGlow = max(lineGlow, lineCore * visible);
        if (d > 0.0)
        {
            break;                              // outside this snowflake: no smaller ones here
        }

        // Step into whichever of the seven smaller snowflakes holds this point.
        float2 central = rotate(local, -PI / 6.0) * KochRoot3;
        if (kochFlake(central, min(detail, 3)) < 0.0)
        {
            local = central;
            toScreen /= KochRoot3;
            generation += 1.0;
        }
        else
        {
            if (onChain)
            {
                // The ring between this chain snowflake and the next one in: rings of frosted ice that
                // flow outward as the dive goes on, alternating in shade, frosted brightest at their rims.
                float rim = exp(-abs(d) * toScreen * 7.0);
                float shade = 0.5 + 0.5 * cos(PI * (generation + 3.0 + u + dives));   // alternates ring by ring
                float3 ice = kochIce(hue + generation * 0.09 + 0.15);
                colour += ice * prominence * (0.025 + 0.05 * shade + 0.18 * rim) * (0.7 + Mid * 0.6 + ripple * 0.8);
                onChain = false;
            }
            float angle = atan2(local.y, local.x);
            float slot = round((angle + PI / 6.0) / (PI / 3.0));
            float centreAngle = slot * (PI / 3.0) - PI / 6.0;
            float2 centre = float2(cos(centreAngle), sin(centreAngle)) * (4.0 / (3.0 * KochRoot3));
            local = (local - centre) * 3.0;
            toScreen /= 3.0;
            generation += 2.0;
            sideWeight *= 0.35;
        }
    }

    // Frost glitter: tiny sparks on the lines, flashing with the treble.
    float spark = step(0.9, hash(floor(p * 0.34 / pixelSize()) + floor(Time * 9.0)));
    colour += float3(0.8, 0.95, 1.0) * spark * lineGlow * Treble * 1.5;
    return colour + previous(uv) * 0.1;
}
