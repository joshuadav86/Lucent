// Name: Kali Flame
// Kali's fold-and-invert fractal as an endless lace of fine filaments that writhes while tongues of iridescent fire lick up through it.
// Family: Fractal
// Tags: Intense, Colourful, Fire
// Added: 2026-10-16 23:54

// Fire colour by heat: ember red -> orange -> gold.
float3 kaliFire(float heat)
{
    float t = saturate(heat);
    return float3(1.5 * t, 0.85 * t * t, 0.25 * t * t * t);
}

// Rotates a colour's hue around the grey axis, keeping its brightness.
float3 kaliHueTurn(float3 colour, float angle)
{
    const float3 axis = float3(0.57735, 0.57735, 0.57735);
    float s, c;
    sincos(angle, s, c);
    float3 turned = colour * c + cross(axis, colour) * s + axis * dot(axis, colour) * (1.0 - c);
    return max(turned, 0.0);
}

float3 Render(float2 uv, float2 p)
{
    float scale = 1.05 * exp2(0.45 * sin(Time * 0.061));
    float2 centre = 0.15 * float2(sin(Time * 0.037), cos(Time * 0.029));
    float2 q = rotate(p, 0.25 * sin(Time * 0.023)) * scale + centre;
    float pixel = 2.0 * scale / min(Resolution.y, 540.0);   // how fine the lace may get: 1/540ths of the screen at high resolutions
    float linePixel = 2.0 * scale * pixelSize();            // line widths: two screen pixels, so they stay crisp at 4K

    // The slice and the fold offset drift, so the lace writhes like a flame.
    float3 position = float3(q, 0.26 + 0.12 * sin(Time * 0.049));
    float3 offset = float3(0.86 + 0.07 * sin(Time * 0.083), 0.52 + 0.06 * cos(Time * 0.071), 0.58 + 0.04 * sin(Time * 0.059))
                  + float3(0.025, 0.015, 0.0) * Mid;

    float stretch = 1.0;
    float lace = 0.0;
    float fine = 0.0;
    int levels = max(qualitySteps(11), 9);
    [loop]
    for (int i = 0; i < levels; i++)
    {
        float r2 = max(dot(position, position), 1.0e-6);
        position = abs(position) / r2 - offset;
        stretch /= r2;

        // Distance in pixels to where this level crosses its fold plane, and how dense its lines are.
        float spacing = 1.0 / (stretch * pixel);
        float d = abs(position.y) / (stretch * linePixel);
        float detail = saturate((spacing - 4.0) / 14.0);
        float filament = saturate(1.3 - d * 0.8) * detail;
        lace += filament * (i < 2 ? 0.3 : 1.0);
        fine += filament * saturate((float)i / 6.0 - 0.3);
    }

    // Tongues of fire licking upward through the lace, fuller on loud passages and kicked by the bass.
    float2 flow = float2(p.x * 1.6, p.y * 0.55 + Time * 0.35);
    float2 warp = float2(fbm(flow * 0.8 + 3.1), fbm(flow * 0.8 + 7.7)) - 0.5;
    float tongues = fbm(flow * 1.5 + warp * 1.8);
    float heat = saturate((tongues - 0.48) * 6.0 + 0.1 + 0.2 * Level + 0.25 * Bass - p.y * 0.15 + 0.1 * Beat);

    // Unlit lace glows a faint cold violet; where the fire passes it burns, white-hot at the finest threads.
    float3 cold = float3(0.22, 0.08, 0.55) * (1.0 - heat);
    float3 fire = kaliFire(0.3 + 0.8 * heat) * lace * heat * 0.75
                + kaliFire(heat) * heat * heat * 0.03;

    // The fire is iridescent: its hue shifts across the flames and drifts on, a step further on every beat.
    float hueAngle = 2.0 * PI * (0.45 * tongues + 0.08 * p.x + Time * 0.012 + (BeatCount - Beat) * 0.02);
    float3 colour = cold * lace * 0.3
                  + kaliHueTurn(fire, hueAngle)
                  + float3(1.0, 0.92, 0.8) * fine * heat * heat * heat * heat * (0.25 + Treble * 0.6);
    return colour + previous(uv) * 0.12;
}
