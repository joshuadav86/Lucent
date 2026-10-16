// Name: Synthwave Horizon
// A neon grid racing toward a striped sun, behind mountains shaped by the spectrum.

float3 Render(float2 uv, float2 p)
{
    const float horizon = 0.1;   // p.y of the horizon line (positive is down)

    // Sky: purple near the horizon, dark above.
    float skyHeight = saturate((horizon - p.y) * 0.8);
    float3 colour = lerp(float3(0.35, 0.05, 0.35), float3(0.02, 0.0, 0.08), skyHeight) * 0.6;

    // The sun, with scrolling stripes cut through its lower half.
    float2 sunCentre = float2(0.0, horizon - 0.35);
    float sunRadius = 0.32 + Bass * 0.05;
    float sunDistance = length(p - sunCentre);
    float stripes = saturate(step(0.5, frac((p.y - sunCentre.y) * 18.0 - Time * 0.5)) + step(p.y, sunCentre.y));
    float aboveHorizon = step(p.y, horizon);
    float sun = smoothstep(sunRadius, sunRadius - 0.01, sunDistance) * stripes * aboveHorizon;
    float3 sunColour = lerp(float3(1.0, 0.85, 0.2), float3(1.0, 0.2, 0.55),
        saturate((p.y - sunCentre.y + sunRadius) / (2.0 * sunRadius)));
    colour = lerp(colour, sunColour * (1.0 + Beat * 0.5), sun);
    colour += sunColour * glow(sunDistance - sunRadius, 12.0) * 0.25 * aboveHorizon;

    // Mountains whose height follows the spectrum, mirrored so the bass is in the middle.
    float across = abs(p.x) / Aspect;
    float ridge = horizon - (spectrum(across) * 0.35 + fbm(float2(p.x * 3.0, 1.0)) * 0.08);
    float mountain = step(ridge, p.y) * aboveHorizon;
    colour = lerp(colour, float3(0.02, 0.0, 0.05), mountain);
    colour += float3(0.2, 0.9, 1.0) * glow(p.y - ridge, 200.0) * aboveHorizon * (0.4 + spectrum(across));

    // The ground grid, in perspective, scrolling faster with the bass.
    float below = p.y - horizon;
    float distanceAway = 0.25 / max(below, 0.002);
    float2 ground = float2(p.x * distanceAway, distanceAway + Time * (1.0 + Bass * 3.0));
    float2 fromLine = abs(frac(ground) - 0.5);           // 0.5 means exactly on a grid line
    float2 lineWidth = fwidth(ground) * 1.5;
    float2 onLine = smoothstep(0.5 - lineWidth, 0.5, fromLine);
    float grid = max(onLine.x, onLine.y) * step(0.0, below) * saturate(below * 6.0);
    colour += float3(1.0, 0.2, 0.8) * grid * (0.6 + Level);

    // A touch of the last frame for glow and motion blur.
    return colour * 0.7 + previous(uv) * 0.3;
}
