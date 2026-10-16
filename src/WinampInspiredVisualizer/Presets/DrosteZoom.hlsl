// Name: Droste Zoom
// An endless Escher-style spiral staircase of interlaced eight-pointed stars: the pattern holds a smaller
// copy of itself, which holds another, falling forever inward along a logarithmic spiral that pulses with the beat.

float dzLine(float d, float halfWidth, float px)
{
    float w = max(halfWidth, px * 0.5);
    return saturate((w - abs(d)) / px + 0.5) * (halfWidth / w);
}

float3 Render(float2 uv, float2 p)
{
    float px = pixelSize();
    float r = max(length(p), 1e-5);
    float theta = atan2(p.y, p.x);

    // Log-polar coordinates, sheared so one turn around the centre is one step of the staircase.
    const float N = 10.0;
    float cellSize = 2.0 * PI / N;
    float shear = 1.0 / N;
    float zoom = Time * 0.15 + (BeatCount - Beat) * 0.02;
    float u = log(r) - zoom;
    float v = theta + Time * 0.025;
    float2 w = float2(u + shear * v, v - shear * u) / cellSize;

    float2 idx = floor(w);
    float2 f = w - idx - 0.5;                       // -0.5..0.5 inside one cell
    float pxc = px * sqrt(1.0 + shear * shear) / (r * cellSize);
    float hw = max(pxc * 0.5, 0.006);
    float fade = saturate(1.6 - pxc * 4.0);         // detail dissolves into the vanishing point

    // Fold the cell into one eighth: x >= y >= 0.
    float2 m = abs(f);
    m = (m.y > m.x) ? m.yx : m;

    // The octagram: two squares, one turned 45 degrees, whose points touch the neighbouring stars.
    float size = 0.3536 + spectrum(0.2) * 0.02;
    float squareA = m.x - size;
    float squareB = (m.x + m.y) * 0.7071 - size;
    float star = min(squareA, squareB);
    float octagon = max(squareA, squareB);

    // Interlaced double straps along both squares' outlines.
    float strapA = dzLine(squareA, hw, pxc) + dzLine(squareA + 0.035, hw * 0.7, pxc) * 0.7;
    float strapB = dzLine(squareB, hw, pxc) + dzLine(squareB + 0.035, hw * 0.7, pxc) * 0.7;
    float straps = strapA + strapB;

    // A rosette at the heart of each star and small diamonds where four stars meet.
    float sr = length(f);
    float petals = sr - (0.1 + 0.025 * cos(atan2(f.y, f.x) * 8.0 + Time * 0.5));
    float rosette = dzLine(petals, hw, pxc) + dzLine(sr - 0.035, hw, pxc);
    float2 cornerOffset = abs(f) - 0.5;
    float diamond = (abs(cornerOffset.x) + abs(cornerOffset.y)) - 0.11;
    float corners = dzLine(diamond, hw, pxc);

    // Colour flows around and down the spiral; each step of the staircase is a new hue.
    float depth = idx.x + idx.y / N;
    float band = spectrum(frac(idx.x * 0.17 + 0.05));
    float hue = depth * 0.06 + Time * 0.02;
    float3 gold = hsv2rgb(hue + 0.12, 0.6, 1.0);
    float3 jewel = hsv2rgb(hue + 0.55, 0.9, 1.0);
    float3 heart = hsv2rgb(hue + 0.85, 0.85, 1.0);
    float3 ground = hsv2rgb(hue + 0.35, 0.9, 1.0);

    // Enamel fills: the star, its inner octagon, the ground between stars.
    float3 fill = (octagon < 0.0) ? heart * 0.09 : ((star < 0.0) ? jewel * 0.06 : ground * 0.025 * step(0.0, diamond));
    fill += (diamond < 0.0) ? gold * 0.08 : float3(0.0, 0.0, 0.0);

    float3 colour = fill * (0.7 + band * 0.8);
    colour += gold * straps * (0.45 + band * 0.6);
    colour += heart * rosette * 0.8 + jewel * corners * 0.9;

    // Spiral staircase: each step is lit at its outer edge and falls into shadow toward the next.
    float stepRise = frac(w.x);
    float stepShade = 0.45 + 0.55 * smoothstep(0.0, 1.0, stepRise) * saturate((1.0 - stepRise) / pxc);
    colour *= stepShade;

    // A ring of light sweeps in from beyond the edge and falls into the centre on every beat.
    float pulsePosition = log(3.0) - (1.0 - Beat) * 4.0;
    float pulse = Beat * exp(-abs(log(r) - pulsePosition) * 3.5);
    colour *= 1.0 + pulse * 1.3;

    // Treble sparkles at the stars' hearts.
    colour += float3(1.0, 0.95, 0.9) * exp(-sr * sr / (pxc * pxc * 2.0 + 0.0004)) * (0.3 + Treble * 1.5);

    colour *= fade;
    colour += hsv2rgb(Time * 0.02, 0.5, 1.0) * (1.0 - fade) * 0.1;
    colour += float3(1.0, 0.9, 0.8) * exp(-r * 25.0) * 0.6;

    return colour;
}
