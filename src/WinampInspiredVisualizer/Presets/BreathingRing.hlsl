// Name: Breathing Ring
// One thick ring of light floats in the dark, slowly breathing in and out with the bass, its rim gently rippled
// by the music, mirrored softly in a still black floor below.
// Family: Minimal
// Tags: Minimal, Calm, Dark

// Ring radius at an angle: a slow breath, the bass, and a soft ripple from the spectrum (mirrored left and right).
float brRadius(float angle)
{
    float folded = abs(angle) / PI;
    float band = spectrum(0.03 + folded * 0.55);
    float breath = 0.03 * sin(Time * 0.4);
    return 0.38 + breath + Bass * 0.07 + Beat * 0.012 + band * 0.035;
}

// The ring's light at point q: a glowing tube with a bright core and a soft halo.
float3 brRing(float2 q, float3 tint, float px)
{
    float r = length(q);
    float angle = atan2(q.x, -q.y);
    float radius = brRadius(angle);
    float thickness = 0.055 + Bass * 0.012;
    float across = (r - radius) / thickness;

    // Shade it as a lit tube: brightest along the top-left of its cross-section.
    float inside = saturate(1.0 - across * across);
    float edge = 1.0 - smoothstep(1.0 - px / thickness, 1.0 + px / thickness, abs(across));
    float facing = sqrt(inside);
    float2 outward = q / max(r, 1e-4);
    float lit = saturate(0.5 + 0.5 * dot(outward * clamp(across, -1.0, 1.0), normalize(float2(-0.5, -0.8))));
    float3 colour = tint * edge * (0.04 + 0.32 * pow(facing, 1.5)) * (0.5 + 0.7 * lit);
    // A hot filament runs through the middle of the tube, whiter where it is brightest.
    float filament = exp(-across * across * 7.0) * (0.75 + Bass * 0.35 + Beat * 0.15);
    colour += lerp(tint, float3(1.0, 0.9, 0.78), 0.55) * filament * edge;
    colour += float3(1.0, 0.95, 0.9) * edge * pow(facing, 8.0) * pow(lit, 6.0) * 0.25;

    // A soft halo that swells a little on the beat.
    float gap = max(abs(r - radius) - thickness, 0.0);
    colour += tint * exp(-gap / (0.035 + Beat * 0.02)) * (0.12 + Level * 0.1);
    colour += tint * exp(-gap / 0.25) * 0.05;
    return colour;
}

float3 Render(float2 uv, float2 p)
{
    float px = pixelSize();

    // Hue drifts very slowly between a warm amber and a cool rose.
    float hue = 0.04 + 0.05 * sin(Time * 0.03) + Mid * 0.02;
    float3 tint = hsv2rgb(hue, 0.7, 1.0) * (0.9 + Level * 0.3);

    // The ring floats a little above centre; the floor line sits below it.
    float2 centre = float2(0.0, -0.14 + 0.015 * sin(Time * 0.21));
    float floorY = 0.5;
    float3 colour = float3(0.006, 0.006, 0.01);
    // A faint warm glow behind the ring.
    colour += tint * exp(-length(p - centre) * 2.2) * 0.04;

    if (p.y < floorY)
    {
        colour += brRing(p - centre, tint, px);
    }
    else
    {
        // The reflection: mirrored, gently rippled, fading and blurring with distance from the floor line.
        float depth = p.y - floorY;
        float2 mirrored = float2(p.x, 2.0 * floorY - p.y);
        mirrored.x += 0.012 * sin(p.y * 45.0 - Time * 1.2) * depth;
        float fade = exp(-depth * 3.0) * 0.35;
        colour += brRing(mirrored - centre, tint, px + depth * 0.04) * fade;
        colour += float3(0.004, 0.004, 0.006);
    }

    // The floor line itself catches a whisper of the ring's light.
    float horizon = exp(-abs(p.y - floorY) / (px * 1.5)) * exp(-abs(p.x) * 2.0);
    colour += tint * horizon * 0.06;
    return colour;
}
