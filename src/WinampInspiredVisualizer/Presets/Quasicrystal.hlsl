// Name: Quasicrystal
// Light waves crossing from many directions at once build a pattern that almost repeats but never does,
// drawn as fine contour lines like a map of shimmering hills; every few bars the number of waves changes and new symmetry blooms.
// Family: Geometric
// Tags: Colourful, Geometric

// Number of crossing waves in each section; the pattern shows twice that many-fold symmetry.
static const float QC_WAVES[6] = { 7.0, 5.0, 9.0, 7.0, 11.0, 6.0 };

// Sum of n plane waves at evenly spread angles; returns the height (about -1..1) and its slope.
float qcField(float2 x, float n, float phase, out float2 slope)
{
    float total = 0.0;
    slope = float2(0.0, 0.0);
    [loop]
    for (int j = 0; j < 12; j++)
    {
        if (j >= (int)n)
            break;
        float angle = PI * j / n;
        float2 dir = float2(cos(angle), sin(angle));
        float arg = dot(x, dir) + phase;
        total += cos(arg);
        slope -= sin(arg) * dir;
    }
    // Scaled so the typical height is similar whatever the number of waves.
    float norm = 1.0 / (sqrt(n) * 1.9);
    slope *= norm;
    return total * norm;
}

float3 Render(float2 uv, float2 p)
{
    float px = pixelSize();
    float zoom = 26.0 + 5.0 * sin(Time * 0.04) - Bass * 1.5;
    float2 x = rotate(p, Time * 0.015) * zoom;
    float pxX = px * zoom;

    // Which wave count we are on, and the glide (over one beat) into the next every eight beats.
    float beatPhase = BeatCount - saturate(Beat);
    float section = floor(beatPhase / 8.0);
    float within = beatPhase - section * 8.0;
    float glide = smoothstep(7.0, 8.0, within);
    float nA = QC_WAVES[(int)fmod(section, 6.0)];
    float nB = QC_WAVES[(int)fmod(section + 1.0, 6.0)];

    // The waves drift steadily; the bass pushes them along a little faster.
    float phase = Time * 0.55 + Bass * 0.6;
    float2 slopeA = float2(0.0, 0.0);
    float2 slopeB = float2(0.0, 0.0);
    float vA = qcField(x, nA, phase, slopeA);
    float vB = glide > 0.0 ? qcField(x, nB, phase, slopeB) : vA;
    float v = lerp(vA, vB, glide);
    float2 slope = lerp(slopeA, glide > 0.0 ? slopeB : slopeA, glide);

    // Fine contour lines: several per unit of height, kept a constant pixel width using the slope.
    const float bands = 4.0;
    float c = (v + 1.0) * bands;
    float steepness = max(length(slope) * bands, 1e-4);
    float lineDist = abs(frac(c + 0.5) - 0.5) / steepness;
    float spacingPx = 1.0 / (steepness * pxX);
    float clarity = smoothstep(2.5, 7.0, spacingPx);
    float core = saturate(1.0 - lineDist / (pxX * 0.9));
    float halo = exp(-lineDist / (pxX * 3.0)) * 0.25;

    // Colour by height: deep indigo valleys through teal to warm gold peaks, each band lit by its own slice of the spectrum.
    float height = saturate(v * 0.5 + 0.5);
    float level = floor(c) / (bands * 2.0);
    float shift = 0.5 + 0.5 * sin(Time * 0.02);
    float3 valley = lerp(float3(0.25, 0.2, 1.0), float3(0.6, 0.15, 0.9), shift);
    float3 slopeColour = lerp(float3(0.1, 0.8, 0.9), float3(0.2, 0.9, 0.6), shift);
    float3 peak = lerp(float3(1.0, 0.75, 0.35), float3(1.0, 0.45, 0.4), shift);
    float3 tint = height < 0.5 ? lerp(valley, slopeColour, height * 2.0) : lerp(slopeColour, peak, height * 2.0 - 1.0);
    float bandLight = spectrum(0.03 + level * 0.85);
    float3 colour = tint * (0.015 + height * height * 0.08) * (0.7 + bandLight);
    colour += tint * (core * clarity * (0.45 + bandLight * 0.9 + Beat * 0.2) + halo * clarity * (0.5 + Mid));
    // Treble makes the highest peaks sparkle.
    colour += float3(1.0, 0.95, 0.85) * smoothstep(0.82, 0.98, height) * Treble * 0.35;
    return colour;
}
