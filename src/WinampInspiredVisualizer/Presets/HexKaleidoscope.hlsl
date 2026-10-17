// Name: Hex Kaleidoscope
// A real three-mirror tube kaleidoscope: tumbling cut gems, glass canes and millefiori beads are mirrored
// into an endless hexagonal jewel pattern that turns, jolts on the beat and sparkles with the treble.
// Family: Kaleidoscope
// Tags: Colourful, Geometric

// Per piece: orbit phase x, orbit speed y, size z, spectrum band w.
static const float4 HK_PIECE[12] = {
    float4(0.0, 0.061, 0.30, 0.05), float4(1.7, 0.047, 0.22, 0.6),  float4(3.1, 0.073, 0.26, 0.3),
    float4(4.4, 0.052, 0.18, 0.85), float4(5.9, 0.066, 0.24, 0.15), float4(2.3, 0.081, 0.15, 0.7),
    float4(0.9, 0.057, 0.20, 0.45), float4(3.8, 0.069, 0.28, 0.25), float4(5.2, 0.044, 0.16, 0.95),
    float4(1.2, 0.077, 0.21, 0.4),  float4(4.9, 0.058, 0.13, 0.8),  float4(2.8, 0.064, 0.19, 0.55)
};

float hkStroke(float d, float halfWidth, float px)
{
    float w = max(halfWidth, px * 0.5);
    return saturate((w - abs(d)) / px + 0.5) * (halfWidth / w);
}

float3 hkJewelStop(float k)
{
    // Ruby, amber, emerald, aqua, sapphire, amethyst.
    float3 c = float3(1.0, 0.06, 0.15);
    c = (k > 0.5) ? float3(1.0, 0.45, 0.04) : c;
    c = (k > 1.5) ? float3(0.02, 0.75, 0.42) : c;
    c = (k > 2.5) ? float3(0.05, 0.7, 1.0) : c;
    c = (k > 3.5) ? float3(0.12, 0.22, 1.0) : c;
    c = (k > 4.5) ? float3(0.75, 0.12, 1.0) : c;
    return c;
}

// The jewel palette, blended smoothly so a piece's colour can drift from one stone to the next.
float3 hkJewel(float n)
{
    float k = wrap(n, 6.0);
    float base = floor(k);
    float3 a = hkJewelStop(base);
    float3 b = hkJewelStop(wrap(base + 1.0, 6.0));
    return lerp(a, b, smoothstep(0.0, 1.0, k - base));
}

float3 Render(float2 uv, float2 p)
{
    float px = pixelSize();
    float beatPhase = BeatCount - Beat;

    // Turn the tube slowly; the hexagonal mirror cells repeat across the screen.
    float spacing = 1.2 + 0.18 * sin(Time * 0.043);
    float2 view = rotate(p, Time * 0.03) / spacing;

    // Nearest hexagon centre on a triangular lattice (spacing 1).
    float2 s = float2(1.0, 1.7320508);
    float4 hc = floor(float4(view, view - float2(0.5, 1.0)) / s.xyxy) + 0.5;
    float4 h = float4(view - hc.xy * s, view - (hc.zw + 0.5) * s);
    float2 local = (dot(h.xy, h.xy) < dot(h.zw, h.zw)) ? h.xy : h.zw;

    // Three mirrors through the hexagon's corners fold it into one triangle.
    float2 folded = kaleidoscope(rotate(local, -PI / 6.0), 3.0);
    float side = 0.57735027;
    float2 o = folded / side;                      // triangle (0,0) (1,0) (0.5,0.87)

    // The object chamber tumbles around the triangle's middle, with a jolt on each beat.
    float2 hub = float2(0.5, 0.288675);
    float chamber = Time * 0.09 + beatPhase * 0.06 + 0.5 * sin(Time * 0.061);
    float2 q = rotate(o - hub, chamber);
    float pxo = px / (spacing * side);              // one pixel in object units
    float sparkleAngle = Time * 0.7;

    // Dark backing with a faint frosted texture.
    float3 colour = float3(0.025, 0.018, 0.05) * (0.6 + noise(q * 30.0) * 0.8);

    // Tiny seed beads drifting loose behind the big pieces.
    float2 qb = q * 9.0 + float2(Time * 0.15, sin(Time * 0.05) * 0.8);
    float2 beadCell = floor(qb);
    float beadRandom = frac(sin(dot(beadCell, float2(127.1, 311.7))) * 43758.5453);
    float2 beadPos = beadCell + 0.3 + 0.4 * float2(beadRandom, frac(beadRandom * 7.13));
    float beadRadius = 0.12 + 0.12 * frac(beadRandom * 3.7);
    float beadDist = length(qb - beadPos) - beadRadius;
    float beadPx = pxo * 9.0;
    float3 beadTint = hsv2rgb(Time * 0.012 + floor(beadRandom * 3.0) * 0.33, 0.8, 1.0);
    float beadShape = saturate(0.5 - beadDist / beadPx) * step(0.35, beadRandom);
    float beadShade = sqrt(saturate(-beadDist / beadRadius));
    colour += beadTint * beadShape * (0.06 + 0.25 * beadShade) * (0.6 + spectrum(0.9) * 0.8);

    [loop]
    for (int i = 0; i < 12; i++)
    {
        float4 piece = HK_PIECE[i];
        float fi = i;
        float orbit = Time * piece.y + piece.x;
        float2 centre = float2(sin(orbit), cos(orbit * 1.37 + fi)) * float2(0.42, 0.36);
        float band = spectrum(piece.w);
        float size = piece.z * (0.7 + band * 0.22 + Beat * 0.04);
        float2 d = rotate(q - centre, orbit * 2.3 + fi);
        float r = length(d);
        float kind = wrap(fi, 3.0);
        // Every piece is its own stone, and slowly drifts through the jewel colours.
        float3 tint = hkJewel(fi * 3.7 + Time * 0.025);

        float sd = r - size;
        float shade = 1.0;
        float detail = 0.0;
        if (kind < 0.5)
        {
            // Cut gem: a polygon whose triangular facets each catch the light differently.
            float sides = 5.0 + wrap(fi, 4.0);
            float segment = 2.0 * PI / sides;
            float angle = atan2(d.y, d.x);
            float facet = floor(angle / segment + 0.5);
            sd = cos(facet * segment - angle) * r - size;
            float table = cos(facet * segment - angle) * r - size * 0.45;
            float facetLight = 0.5 + 0.5 * cos(facet * 2.39996 + sparkleAngle + fi);
            shade = (table < 0.0) ? 0.45 + 0.3 * facetLight : 0.05 + 1.1 * facetLight * facetLight * facetLight;
            float spoke = abs(sin(angle - (facet + 0.5) * segment)) * r;
            detail = max(hkStroke(table, 0.006, pxo), hkStroke(spoke, 0.005, pxo) * step(0.0, table));
        }
        else if (kind < 1.5)
        {
            // Glass cane: a rounded rod with a twisted stripe running through it.
            float halfLength = size * 1.5;
            float along = clamp(d.x, -halfLength, halfLength);
            float radius = size * 0.26;
            sd = length(d - float2(along, 0.0)) - radius;
            float across = d.y / radius;
            shade = 0.45 + 0.55 * sqrt(saturate(1.0 - across * across));
            float stripe = abs(frac((d.x * 2.0 + d.y) / radius * 0.5) - 0.5);
            detail = hkStroke(stripe * radius, 0.012, pxo) * 0.8;
        }
        else
        {
            // Millefiori bead: rings and a petal flower sliced through a glass cane.
            float petals = 6.0 + wrap(fi, 3.0) * 2.0;
            float rn = r / size;
            float angle = atan2(d.y, d.x);
            float flower = rn - (0.42 + 0.12 * cos(angle * petals));
            shade = 0.5 + 0.5 * sqrt(saturate(1.0 - rn * rn));
            detail = hkStroke(flower * size, 0.007, pxo) + hkStroke((rn - 0.78) * size, 0.006, pxo)
                   + hkStroke((rn - 0.16) * size, 0.006, pxo);
        }

        float inside = saturate(0.5 - sd / pxo);
        float3 body = tint * (0.03 + 0.2 * shade * shade) * (0.7 + band * 0.8);
        float3 lines = lerp(tint, float3(1.0, 1.0, 1.0), 0.35) * detail * (0.35 + band * 0.8);
        float rim = hkStroke(sd, 0.008, pxo);
        float2 shine = d / size - float2(-0.3, 0.32);
        float glint = exp(-dot(shine, shine) * 260.0) * (0.4 + Treble * 2.5);

        // Glass over glass: each piece filters what is behind it and adds its own glow.
        float3 pieceColour = body + lines + glint * float3(1.0, 0.95, 0.85);
        colour = lerp(colour, colour * tint * 0.5 + pieceColour, inside * 0.9);
        colour += lerp(tint, float3(1.0, 1.0, 1.0), 0.5) * rim * 0.45;
    }

    return colour + previous(uv) * 0.06;
}
