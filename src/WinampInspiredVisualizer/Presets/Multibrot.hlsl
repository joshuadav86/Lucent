// Name: Multibrot
// The Mandelbrot set's whole family at once: its power slowly climbs from two to seven and back, so the set grows extra lobes and blooms into ever more petals. It turns a step with every beat, edged in fine glowing filaments.
// Family: Fractal
// Tags: Colourful, Dark
// Added: 2026-10-17 21:50

float3 multibrotGradient(float t)
{
    // Ink blue -> teal -> pale gold -> rose, cycling.
    float3 c = 0.5 + 0.5 * cos(2.0 * PI * (t + float3(0.55, 0.42, 0.25)));
    return c * c;
}

float3 Render(float2 uv, float2 p)
{
    // The power rests a while on each whole number and eases between them.
    float phase = Time / 9.0;
    float stage = floor(phase);
    float ease = smoothstep(0.55, 1.0, phase - stage);
    float stepIndex = wrap(stage, 10.0);
    float fromPower = stepIndex < 5.0 ? 2.0 + stepIndex : 12.0 - stepIndex;      // 2,3,4,5,6,7,6,5,4,3
    float nextIndex = wrap(stage + 1.0, 10.0);
    float toPower = nextIndex < 5.0 ? 2.0 + nextIndex : 12.0 - nextIndex;
    float power = lerp(fromPower, toPower, ease);

    // Turn a little with each beat (the beat count rises smoothly as the beat fades), and breathe in and out.
    float turn = (BeatCount - Beat) * 0.035 + Time * 0.01;
    float scale = 1.22 - 0.1 * power / 7.0 + 0.08 * sin(Time * 0.07);
    float2 c = rotate(float2(p.x, -p.y), turn) * scale;
    // Low powers sit off-centre; nudge so the body stays in view.
    c.x -= 0.4 * saturate(3.0 - power);
    float pixel = pixelSize() * scale;

    // The first step from z = 0 lands on c with dz = 1, so start there: atan2(0, 0) isn't a number on
    // Direct3D, and it turned the whole picture black.
    float2 z = c;
    float2 dz = float2(1.0, 0.0);
    float n = 1.0;
    float trap = abs(dot(c, c) - 0.25);
    bool escaped = false;
    float bailout = 256.0;
    int maxIterations = qualitySteps(140);
    [loop]
    for (int i = 1; i < maxIterations; i++)
    {
        // z^d in polar form, and its derivative d z^(d-1) dz + 1.
        float r2 = max(dot(z, z), 1.0e-30);
        float logR = 0.5 * log(r2);
        float angle = r2 > 1.0e-30 ? atan2(z.y, z.x) : 0.0;
        float rd1 = exp(logR * (power - 1.0));
        float s1, c1;
        sincos(angle * (power - 1.0), s1, c1);
        float2 zd1 = rd1 * float2(c1, s1);   // z^(d-1)
        dz = power * float2(zd1.x * dz.x - zd1.y * dz.y, zd1.x * dz.y + zd1.y * dz.x) + float2(1.0, 0.0);
        z = float2(zd1.x * z.x - zd1.y * z.y, zd1.x * z.y + zd1.y * z.x) + c;
        float m = dot(z, z);
        trap = min(trap, abs(m - 0.25));
        if (m > bailout * bailout)
        {
            escaped = true;
            break;
        }
        n += 1.0;
    }

    float hue = Time * 0.012 + power * 0.05;
    float3 colour = 0.0;
    float dzLength = length(dz);
    if (escaped)
    {
        float logZ = 0.5 * log(dot(z, z));
        float smoothN = n + 1.0 - log(logZ / log(bailout)) / log(power);
        float de = length(z) * logZ / max(dzLength, 1.0e-20);
        float dpx = de / pixel;

        // Relief from the potential's slope, lit by a light circling the set.
        float2 slope = float2(z.x * dz.x + z.y * dz.y, z.y * dz.x - z.x * dz.y) / max(dzLength * dzLength, 1.0e-30);
        slope = rotate(slope, -turn);
        float3 normal = normalize(float3(slope / max(length(slope), 1.0e-20), 1.1));
        float3 lightDir = normalize(float3(cos(Time * 0.17), sin(Time * 0.17), 0.9));
        float relief = saturate(dot(normal, lightDir));

        float3 field = multibrotGradient(hue + smoothN * 0.035);
        float nearSet = exp(-dpx * (0.05 - Bass * 0.02));
        float filament = exp(-dpx * 0.5);
        float core = saturate(1.3 - dpx * 0.7);
        float band = 0.5 + 0.5 * cos(smoothN * 0.8 - Time * 0.6);   // soft equipotential ripples flowing inward

        float approach = saturate(smoothN / 15.0);   // 0 far out, rising towards the set
        colour = field * (0.025 + 0.22 * approach * approach * (0.6 + 0.4 * band)) * (0.4 + relief)
               + field * nearSet * (0.08 + 0.35 * relief * relief) * (0.7 + Mid * 0.6)
               + lerp(field, float3(1.0, 0.95, 0.85), 0.4) * filament * (0.35 + Treble * 0.3)
               + multibrotGradient(hue + 0.5) * core * (0.6 + Bass * 0.5 + Beat * 0.2);
    }
    else
    {
        // Inside: dark lacquer with faint rings from how close the orbit swung to a circle.
        float ring = exp(-trap * 12.0);
        colour = multibrotGradient(hue + 0.3) * (0.01 + 0.08 * ring * (0.6 + Level));
        if (dzLength * pixel > 2.0)
        {
            colour = multibrotGradient(hue + 0.5) * (0.6 + Bass * 0.5);   // on the boundary itself
        }
    }
    return lerp(colour, previous(uv), 0.25);
}
