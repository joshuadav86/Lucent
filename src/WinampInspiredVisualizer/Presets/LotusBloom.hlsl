// Name: Lotus Bloom
// A lotus that never stops opening: rings of veined, glowing petals are born at the golden seed pod,
// unfold outward layer after layer, flare open on the beat and stretch with the spectrum.
// Family: Kaleidoscope
// Tags: Calm, Colourful

float lbStroke(float d, float halfWidth, float px)
{
    float w = max(halfWidth, px * 0.5);
    return saturate((w - abs(d)) / px + 0.5) * (halfWidth / w);
}

float3 Render(float2 uv, float2 p)
{
    float px = pixelSize();
    float hw = max(px * 0.5, 0.0012);
    float r = length(p);
    float theta = atan2(p.y, p.x);

    int layers = qualitySteps(10);
    float clock = Time * 0.013 * layers;    // rings move outward at the same speed at any quality
    float phase = frac(clock);
    float3 colour = float3(0.0, 0.0, 0.0);
    float transmit = 1.0;
    float shadow = 1.0;

    [loop]
    for (int k = 0; k < layers; k++)
    {
        float age = (k + phase) / (float)layers;     // 0 newest (centre) .. 1 oldest (rim)
        float generation = floor(clock) - k;
        float petals = 6.0 + 2.0 * wrap(generation, 4.0);
        float wedge = 2.0 * PI / petals;
        float spin = (wrap(generation, 2.0) < 0.5 ? 1.0 : -1.0) * Time * 0.03 + generation * 1.94;

        // Petals tilt from upright to flat as they age; the beat flares them open.
        float opening = lerp(1.25, 0.15, smoothstep(0.0, 0.9, age)) - Beat * 0.12;
        float baseRadius = 0.03 + age * 0.55;
        float petalLength = (0.12 + age * 0.75) * cos(max(opening, 0.0));

        // Which petal: fold the angle, and stretch each petal with its own part of the spectrum.
        float a = theta - spin;
        float index = floor(a / wedge + 0.5);
        float local = a - index * wedge;
        float band = spectrum(frac(abs(index) * 0.13 + generation * 0.07));
        float lengthNow = petalLength * (0.9 + band * 0.25);
        float2 q = float2(r * cos(local) - baseRadius, r * sin(local));

        // Pointed lens: two arcs meeting at base and tip.
        float span = lengthNow * 0.5;
        float bulge = min(span * 0.62, sin(wedge * 0.5) * (baseRadius + span) * 1.15);
        float rho = (span * span + bulge * bulge) / (2.0 * bulge);
        float2 c = float2(span, 0.0);
        float lens = max(length(q - c - float2(0.0, bulge - rho)), length(q - c + float2(0.0, bulge - rho))) - rho;

        float alpha = saturate(0.5 - lens / px) * smoothstep(0.0, 0.06, age) * (1.0 - smoothstep(0.82, 1.0, age));
        float s = saturate(q.x / max(lengthNow, 1e-3));      // 0 base .. 1 tip
        float halfWidth = max(bulge * sqrt(saturate(1.0 - (2.0 * s - 1.0) * (2.0 * s - 1.0))), 1e-4);
        float across = q.y / halfWidth;

        // Colour: each generation of petals takes the next hue; deep at the base, luminous toward the tip.
        float hue = 0.735 + 0.235 * sin(generation * 0.75 + Time * 0.015);   // teal, blue, violet, magenta, rose
        float3 deep = hsv2rgb(hue, 0.9, 1.0);
        float3 pale = hsv2rgb(hue + 0.06, 0.45, 1.0);
        float3 body = lerp(deep * 0.05, pale * 0.32, pow(s, 1.5)) * (0.6 + band * 0.8);

        // Veins converge on the tip; a bright rim outlines the petal; a dewdrop glints near the tip.
        float veinCoord = across * 3.0;
        float vein = lbStroke((veinCoord - floor(veinCoord + 0.5)) * halfWidth / 3.0, hw * 0.7, px) * (0.25 + 0.5 * s);
        float rim = lbStroke(lens, hw, px) * (0.6 + s * 0.8);
        float2 dew = q - float2(lengthNow * 0.82, 0.0);
        float glint = exp(-dot(dew, dew) / (0.00004 + lengthNow * lengthNow * 0.0004)) * (0.3 + Treble * 2.0);
        float3 petal = body + pale * (vein * 0.35 + rim * 0.9) + float3(1.0, 0.95, 0.9) * glint;

        colour += transmit * shadow * petal * alpha;
        // Petals above cast a soft shadow on the ones beneath.
        shadow *= 1.0 - 0.7 * exp(-max(lens, 0.0) / 0.02) * smoothstep(0.0, 0.06, age);
        transmit *= 1.0 - alpha;
    }

    // The seed pod: golden seeds in a sunflower spiral, glowing with the bass.
    float podRadius = 0.075 + Bass * 0.01;
    float3 pod = float3(0.0, 0.0, 0.0);
    if (r < podRadius + 0.01)
    {
        float nearest = 10.0;
        [loop]
        for (int i = 1; i < 34; i++)
        {
            float rr = podRadius * 0.92 * sqrt(i / 34.0);
            float aa = i * 2.3999632 + Time * 0.1;
            nearest = min(nearest, length(p - rr * float2(cos(aa), sin(aa))));
        }
        float seed = 1.0 - smoothstep(0.0045, 0.0045 + px, nearest);
        float disc = saturate((podRadius - r) / px + 0.5);
        pod = (float3(1.0, 0.75, 0.25) * (0.15 + Bass * 0.3) + float3(1.0, 0.9, 0.6) * seed * (0.5 + Beat * 0.8)) * disc;
        pod += float3(1.0, 0.8, 0.4) * lbStroke(r - podRadius, hw, px) * 0.8;
    }
    colour = colour + transmit * pod;

    // Dark water beneath, with rings spreading on each beat.
    float ripple = lbStroke(r - (1.0 - Beat) * 1.2 - 0.1, hw, px) * Beat * 0.3;
    colour += transmit * (float3(0.005, 0.01, 0.03) + float3(0.4, 0.6, 1.0) * ripple);

    return colour + previous(uv) * 0.08;
}
