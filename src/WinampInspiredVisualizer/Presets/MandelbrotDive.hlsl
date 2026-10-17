// Name: Mandelbrot Dive
// An endless dive into the Mandelbrot set: seahorse spirals, star-shaped dendrites and hidden tiny copies of itself, traced in razor-thin light.

static const float DiveLength = 26.0;     // seconds per dive
static const float DiveDoublings = 9.8;   // how many times the view halves during one dive (stays within float precision)

float2 diveTarget(float index)
{
    float k = wrap(index, 4.0);
    if (k < 0.5) return float2(-0.743643887, 0.131825904);           // seahorse valley, ending in a deep spiral
    if (k < 1.5) return float2(-0.101096364, 0.956286511);           // star-shaped dendrites
    if (k < 2.5) return float2(-0.235125, 0.827215);                  // triple spirals and a tiny Mandelbrot
    return float2(-0.77568377, 0.13646737);                           // double spirals
}

float3 diveGradient(float t)
{
    // Indigo -> magenta -> amber -> teal, cycling, kept saturated.
    float3 c = 0.5 + 0.5 * cos(2.0 * PI * (t + float3(0.62, 0.80, 0.02)));
    float grey = dot(c, float3(0.3, 0.5, 0.2));
    return max(lerp(grey.xxx, c, 1.35), 0.0);
}

float3 Render(float2 uv, float2 p)
{
    float dive = Time / DiveLength;
    float index = floor(dive);
    float u = dive - index;

    float height = 0.4 * exp2(-u * DiveDoublings);
    float spin = Time * 0.045;
    float2 c = diveTarget(index) + rotate(float2(p.x, -p.y), spin) * (height * 0.5);
    float pixel = height / Resolution.y;

    float2 z = 0.0;
    float2 dz = 0.0;
    float n = 0.0;
    bool escaped = false;
    int maxIterations = qualitySteps(450);
    [loop]
    for (int i = 0; i < maxIterations; i++)
    {
        dz = 2.0 * float2(z.x * dz.x - z.y * dz.y, z.x * dz.y + z.y * dz.x) + float2(1.0, 0.0);
        z = float2(z.x * z.x - z.y * z.y, 2.0 * z.x * z.y) + c;
        if (dot(z, z) > 1.0e6)
        {
            escaped = true;
            break;
        }
        n += 1.0;
    }

    float colourPhase = (BeatCount - Beat) * 0.035 + Time * 0.01;
    float3 colour = 0.0;
    float dzLength = length(dz);
    if (escaped)
    {
        float logR = 0.5 * log(dot(z, z));
        float smoothN = n + 1.0 - log2(logR);
        float de = length(z) * logR / max(dzLength, 1.0e-20);   // distance to the set (plane units)
        float dpx = de / pixel;                                  // ... in pixels
        float rel = de / height;                                 // ... as a fraction of the screen

        // Relief lighting: the potential's gradient (z / dz) acts as a surface normal.
        float2 slope = float2(z.x * dz.x + z.y * dz.y, z.y * dz.x - z.x * dz.y) / max(dzLength * dzLength, 1.0e-30);
        slope = rotate(slope, -spin);
        float3 normal = normalize(float3(slope / max(length(slope), 1.0e-20), 0.9));
        float3 lightDir = normalize(float3(cos(Time * 0.21), sin(Time * 0.21), 0.8));
        float relief = saturate(dot(normal, lightDir));
        float relief4 = relief * relief * relief * relief;
        float sheen = relief4 * relief4 * relief4;

        float3 tint = diveGradient(smoothN * 0.02 + colourPhase);
        // Filaments share one slowly drifting theme colour per dive, so dense regions read as detail, not confetti.
        float themeHue = index * 0.27 + colourPhase * 0.5 + u * 0.3 + 0.1;
        float3 filament = lerp(diveGradient(themeHue), diveGradient(themeHue + 0.4), relief * relief);
        filament = lerp(filament, tint, 0.2);
        float nearSet = exp(-rel * (55.0 - Bass * 20.0 - Mid * 10.0));
        float core = saturate(1.4 - dpx * 0.8);
        float halo = exp(-dpx * 0.45);

        // Equipotential lines flowing slowly into the set: fine engraving in the dark field.
        float perPixel = 1.0 / (dpx * 0.6931);                 // change of smoothN across one pixel
        float bandPos = frac(smoothN * 0.5 - Time * 0.15);
        float lineDist = min(bandPos, 1.0 - bandPos) * 2.0 / perPixel;
        float contour = saturate(1.0 - lineDist) * saturate(2.0 - perPixel * 3.0);

        colour = tint * (0.018 + contour * 0.22)
               + filament * (nearSet * (0.04 + 0.32 * relief * relief) + halo * 0.25)
               + (filament * 0.5 + 0.5) * sheen * nearSet * (0.4 + Treble * 0.9)
               + filament * core * (0.85 + Bass * 0.6 + Beat * 0.3);
    }
    else if (dzLength * pixel > 2.0)
    {
        // Not escaped yet but chaotic across this pixel: it lies on the boundary itself.
        colour = diveGradient(index * 0.27 + colourPhase * 0.5 + u * 0.3 + 0.1) * (1.0 + Bass * 0.6 + Beat * 0.3);
    }

    // Reproject the last frame (the zoom and spin are known): gentle temporal smoothing of the fine detail.
    // When a new dive starts, the old picture keeps zooming in this way while it dissolves.
    float frameZoom = exp2(-DiveDoublings * DeltaTime / DiveLength);
    float3 history = previous(toUv(rotate(p, -0.045 * DeltaTime) * frameZoom));
    float sinceStart = u * DiveLength;
    float keep = sinceStart < 1.6 ? exp(-DeltaTime * (0.5 + 7.0 * sinceStart)) : 0.3;
    return lerp(colour, history, keep);
}
