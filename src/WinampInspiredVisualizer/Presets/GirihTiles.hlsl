// Name: Girih Tiles
// An Islamic star pattern of gold strap-work laid over deep enamel, slowly turning and drifting closer:
// the stars open and close with the music, light runs along the straps and every beat ripples out from the centre.
// Family: Geometric
// Tags: Colourful, Geometric

// Anti-aliased band: 1 inside half-width w of the line, fading over one pixel.
float gtStroke(float d, float w, float px)
{
    return saturate((w - abs(d)) / px + 0.5);
}

float gtSegment(float2 q, float2 a, float2 b, out float along)
{
    float2 ab = b - a;
    float h = saturate(dot(q - a, ab) / dot(ab, ab));
    along = h;
    return length(q - a - ab * h);
}

float3 Render(float2 uv, float2 p)
{
    // The pattern is built from a tiling of octagons and small squares. In every tile, two straps leave the
    // middle of each edge at the same "contact angle" and run inward until they meet their neighbours, which
    // draws an eight-pointed star in each octagon and a four-pointed one in each square.
    float zoom = 2.3 + 0.6 * sin(Time * 0.043) - Bass * 0.1;
    float2 q = rotate(p, Time * 0.018) * zoom + float2(Time * 0.05, Time * 0.031);
    float px = pixelSize() * zoom;

    float2 cell = floor(q + 0.5);
    float2 f = q - cell;
    float octagonEdge = max(max(abs(f.x), abs(f.y)), (abs(f.x) + abs(f.y)) * 0.70710678);
    bool inOctagon = octagonEdge < 0.5;

    float sides = 8.0;
    float apothem = 0.5;
    float2 local = f;
    float2 tileId = cell;
    if (!inOctagon)
    {
        float2 corner = float2(f.x < 0.0 ? -0.5 : 0.5, f.y < 0.0 ? -0.5 : 0.5);
        local = rotate(f - corner, -PI * 0.25);
        tileId = cell + corner;
        sides = 4.0;
        apothem = 0.20710678;
    }

    // Fold the tile into one mirrored wedge so only one strap needs drawing.
    float wedge = 2.0 * PI / sides;
    float angle = atan2(local.y, local.x);
    float folded = abs(wrap(angle + wedge * 0.5, wedge) - wedge * 0.5);
    float radius = length(local);
    float2 u = float2(cos(folded), sin(folded)) * radius;

    // Contact angle breathes slowly and opens a little with the mids: wide stars to slender ones.
    float contact = radians(64.0 + 9.0 * sin(Time * 0.11) + Mid * 5.0);
    float tilt = PI * 0.5 - contact;
    float2 mid = float2(apothem, 0.0);
    float2 dir = float2(-cos(tilt), sin(tilt));
    float halfAngle = PI / sides;
    float tanHalf = tan(halfAngle);
    float reach = apothem * tanHalf / (sin(tilt) + cos(tilt) * tanHalf);
    float along = 0.0;
    float strapDist = gtSegment(u, mid, mid + dir * reach, along);
    float2 inward = float2(-sin(tilt), -cos(tilt));
    bool inStar = dot(u - mid, inward) > 0.0;

    // Screen-space distance from the centre drives the beat ripple and the slow colour fields.
    float screenRadius = length(q - float2(Time * 0.05, Time * 0.031)) / zoom;
    float ripple = Beat * exp(-abs(screenRadius - (1.0 - Beat) * 1.9) * 5.0);
    float tileSeed = hash(tileId + 17.0);
    float band = spectrum(0.04 + tileSeed * 0.7);

    // Deep enamel: lapis and turquoise stars, carmine and emerald between them, shifting slowly.
    float drift = Time * 0.013;
    float3 lapis = float3(0.03, 0.08, 0.36);
    float3 turquoise = float3(0.0, 0.26, 0.30);
    float3 carmine = float3(0.34, 0.03, 0.07);
    float3 emerald = float3(0.03, 0.17, 0.12);
    float swing = 0.5 + 0.5 * sin(drift * 6.0 + tileSeed * 0.8 + screenRadius * 0.9);
    float3 enamel;
    if (inOctagon)
        enamel = inStar ? lerp(lapis, turquoise, swing * 0.6) : lerp(carmine, emerald, swing);
    else
        enamel = inStar ? float3(0.38, 0.22, 0.03) : lerp(emerald, lapis, swing);

    // Fired-glass look: brighter away from the straps, with a soft mottle.
    float bed = saturate(strapDist / 0.16);
    float mottle = noise(q * 7.0 + tileSeed * 5.0) * 0.25 + 0.85;
    float3 colour = enamel * (0.35 + 0.75 * bed) * mottle * (0.75 + band * 1.1 + ripple * 0.8 + Level * 0.2);

    // Gold strap-work: a band with two fine bright edges and a warm satin middle.
    float strapWidth = 0.034;
    float strapBody = gtStroke(strapDist, strapWidth, px);
    float strapEdges = gtStroke(strapDist - strapWidth * 0.8, px * 0.6, px);
    float3 gold = float3(1.0, 0.72, 0.32);
    float runner = pow(0.5 + 0.5 * sin((q.x * 0.7 + q.y * 0.5) * 2.1 - Time * 1.6 + along * 3.0), 6.0);
    float strapLight = 0.32 + spectrum(0.08 + frac(along * 0.5 + tileSeed) * 0.6) * 0.55 + runner * 0.35 * (0.4 + Mid);
    float3 strapColour = gold * strapLight * (0.6 + 0.4 * smoothstep(strapWidth, 0.0, strapDist));
    colour = lerp(colour, strapColour * 0.38, strapBody);
    colour += gold * strapEdges * (0.55 + strapLight * 0.6 + ripple * 0.9);

    // A dark lead line shadows each strap so it seems to sit above the enamel.
    float shadow = smoothstep(strapWidth + 0.03, strapWidth, strapDist) * (1.0 - strapBody);
    colour *= 1.0 - shadow * 0.55;

    // Faint construction lines of the octagon-and-square tiling underneath.
    float tileEdge = inOctagon ? (0.5 - octagonEdge) : (apothem - max(abs(local.x), abs(local.y)));
    colour += gold * gtStroke(tileEdge, px * 0.5, px) * 0.05 * (1.0 - strapBody);

    // Treble glints where straps cross at the middle of each edge.
    float crossing = length(u - mid);
    float glintSeed = hash(tileId * 3.1 + floor(Time * 2.0));
    colour += float3(1.0, 0.9, 0.7) * exp(-crossing * crossing / (0.0025 + px * px)) * Treble * 0.6 * step(0.6, glintSeed);

    return colour;
}
