// Name: Vasarely Sphere
// A flat chequerboard swells into a great sphere that bulges out of the page, op-art style, breathing with the
// bass while waves of colour sweep across the squares with the music.
// Family: Geometric
// Tags: Geometric, Colourful, Retro
// Added: 2026-10-17 21:37

// Anti-aliased chequer of a grid coordinate: 1 on one colour, 0 on the other, grey where squares get too fine.
float vsChequer(float2 g)
{
    float2 h = g * 0.5 + 0.25;
    float2 w = max(fwidth(h), 1e-4);
    float2 s = clamp((abs(frac(h) - 0.5) - 0.25) / w, -0.5, 0.5);
    float c = 0.5 - 2.0 * s.x * s.y;
    float fine = saturate(max(w.x, w.y) * 1.5 - 0.4);
    return lerp(c, 0.5, fine);
}

// Distance (in grid units) to the nearest grid line, for thin dividing lines.
float vsGridLine(float2 g)
{
    float2 f = abs(frac(g + 0.5) - 0.5) / max(fwidth(g), 1e-4);
    return min(f.x, f.y);
}

float3 Render(float2 uv, float2 p)
{
    // The sphere wanders slowly across the canvas and breathes with the bass.
    float2 centre = float2(0.35 * sin(Time * 0.071), 0.12 * sin(Time * 0.053 + 1.0));
    float radius = 0.62 + Bass * 0.09 + Beat * 0.025 + 0.04 * sin(Time * 0.2);
    float bulge = 0.85 + 0.15 * sin(Time * 0.11);

    // Inside the disc the grid is wrapped over a sphere: arc length along the surface replaces flat distance,
    // so squares shrink towards the rim and swell in the middle, as if pushed up from behind.
    float2 d = p - centre;
    float r = length(d);
    float inside = 1.0 - smoothstep(radius - pixelSize(), radius + pixelSize(), r);
    float rn = saturate(r / radius);
    float height = sqrt(max(1.0 - rn * rn, 0.0));
    // Arc length over the sphere, scaled to meet the flat grid at the rim: the middle squares swell by
    // about half again and the squares crowd together towards the edge.
    float stretched = radius * asin(min(rn, 0.9999)) * (2.0 / PI);
    float2 bent = centre + (r > 1e-4 ? d / r : float2(0.0, 0.0)) * lerp(r, stretched, bulge);

    // A slow turn of the whole grid keeps the field alive.
    float cells = 13.0;
    float2 g = rotate(r < radius ? bent : p, 0.06 * sin(Time * 0.04)) * cells * 0.5 + float2(Time * 0.15, 0.0);
    float cheq = vsChequer(g);

    // Colour runs diagonally across the field; each column borrows the loudness of its own band.
    float2 cell = floor(g);
    float column = frac((cell.x + 40.0) / 28.0);
    float band = spectrum(column * 0.75 + 0.03);
    // Rings of colour spread out from the sphere, kept to the cyan, blue, violet, magenta and coral side of the wheel.
    float ripple = length(cell + 0.5 - (rotate(centre, 0.06 * sin(Time * 0.04)) * cells * 0.5 + float2(Time * 0.15, 0.0)));
    float hue = 0.78 + 0.3 * sin(ripple * 0.22 - Time * 0.35 + band * 0.6) + Mid * 0.04;
    float hue2 = hue + 0.42 + 0.05 * sin(Time * 0.09);
    float3 light = hsv2rgb(hue, 0.7, 0.7 + band * 0.4);
    float3 dark = hsv2rgb(hue2, 0.75, 0.05 + band * 0.08);
    float3 colour = lerp(dark, light, cheq);

    // Shade the sphere from the upper left, and give it a quiet rim so it lifts off the page.
    float3 normal = float3(d / max(radius, 1e-3), height);
    float lambert = saturate(dot(normal, normalize(float3(-0.45, 0.55, 0.7))));
    float sphereShade = lerp(0.55, 1.15, lambert);
    float spec = pow(saturate(dot(reflect(float3(0.0, 0.0, -1.0), normal), normalize(float3(-0.45, 0.55, 0.7)))), 24.0);
    float3 onSphere = colour * sphereShade + float3(1.0, 0.95, 0.9) * spec * (0.18 + Beat * 0.15);
    colour = lerp(colour * (0.8 - 0.25 * smoothstep(radius * 1.25, radius, r)), onSphere, inside);

    // Fine dark seams between squares.
    float seam = saturate(vsGridLine(g) - 0.3);
    colour *= lerp(0.55, 1.0, seam);

    // The rim catches the treble.
    float rim = exp(-abs(r - radius) / (pixelSize() * 2.0 + 0.004));
    colour += hsv2rgb(hue, 0.25, 1.0) * rim * (0.1 + Treble * 0.4);

    return colour;
}
