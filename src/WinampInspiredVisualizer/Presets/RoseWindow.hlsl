// Name: Rose Window
// A gothic rose window of carved stone tracery and hundreds of jewel-toned panes, lit by a light show behind
// it: spirals of light and waves of colour sweep through the glass and every beat ripples out from the heart.
// Family: Kaleidoscope
// Tags: Colourful, Geometric

float rwHash(float2 p)
{
    p = frac(p * float2(443.897, 441.423));
    p += dot(p, p.yx + 19.19);
    return frac((p.x + p.y) * p.x);
}

// Signed distance to a pointed (equilateral) arch opening in a wedge of half-angle halfAngle, with
// straight radial sides inset by mullion m, sill at radius sill and springing at radius spring.
// q: folded coordinates (x along the opening's axis, y >= 0).
float rwArch(float2 q, float halfAngle, float m, float sill, float spring)
{
    float r = length(q);
    float a = atan2(q.y, q.x);
    float side = m - r * sin(halfAngle - a);
    float bottom = sill - r;
    float halfSpan = spring * tan(halfAngle) - m / cos(halfAngle);
    float2 centre = float2(spring, -halfSpan);
    float head = (q.x > spring) ? length(q - centre) - 2.0 * halfSpan : -1.0;
    return max(max(side, bottom), head);
}

// Turns a colour around the grey axis: a hue rotation that keeps brightness.
float3 rwHueTurn(float3 c, float angle)
{
    float3 k = float3(0.57735027, 0.57735027, 0.57735027);
    float s = sin(angle);
    float co = cos(angle);
    return c * co + cross(k, c) * s + k * dot(k, c) * (1.0 - co);
}

float3 Render(float2 uv, float2 p)
{
    float px = pixelSize();
    // A slow zoom cycle: sometimes the whole window, sometimes close in on its heart.
    float scale = 1.0 + 0.45 * smoothstep(-0.3, 1.0, sin(Time * 0.045)) + Beat * 0.006;
    float2 w = rotate(p, Time * 0.01) / scale;
    float pxw = px / scale;
    float r = length(w);
    float theta = atan2(w.y, w.x);

    // ---- Tracery geometry -------------------------------------------------------------------------
    // Twelve lancets: fold into a 30-degree sector mirrored about the lancet's axis.
    float sector = 2.0 * PI / 12.0;
    float sectorIndex = floor(theta / sector + 0.5);
    float a = abs(theta - sectorIndex * sector);
    float2 q = float2(cos(a), sin(a)) * r;
    float lancet = rwArch(q, sector * 0.5, 0.012, 0.185, 0.47);
    float lancetHead = length(q - float2(0.6, 0.0)) - 0.046;          // a round light in the arch head

    // 24 roundels between the lancets' heads and the arcade.
    float sector24 = 2.0 * PI / 24.0;
    float index24 = floor(theta / sector24 + 0.5);
    float a24 = abs(theta - index24 * sector24);
    float2 q24 = float2(cos(a24), sin(a24)) * r;
    float2 roundelCentre = float2(0.728, 0.0);
    float roundel = length(q24 - roundelCentre) - 0.068;

    // 48 small pointed arches in the outer arcade.
    float sector48 = 2.0 * PI / 48.0;
    float index48 = floor(theta / sector48 + 0.5);
    float a48 = abs(theta - index48 * sector48);
    float2 q48 = float2(cos(a48), sin(a48)) * r;
    float arcade = rwArch(q48, sector48 * 0.5, 0.006, 0.83, 0.87);

    float oculus = r - 0.155;

    // Stone: bands around every opening, the ring courses, and the foils inside the openings.
    float foil = abs(r - (0.1 + 0.02 * cos(6.0 * theta))) - 0.005;
    float quatreAngle = atan2(q24.y - roundelCentre.y, q24.x - roundelCentre.x);
    float quatre = abs(length(q24 - roundelCentre) - (0.036 + 0.011 * cos(4.0 * quatreAngle))) - 0.004;
    float stone = abs(lancet) - 0.006;
    stone = min(stone, abs(lancetHead) - 0.005);
    stone = min(stone, abs(roundel) - 0.006);
    stone = min(stone, (r > 0.81) ? abs(arcade) - 0.004 : 1.0);
    stone = min(stone, abs(oculus) - 0.008);
    stone = min(stone, abs(r - 0.815) - 0.007);
    stone = min(stone, min(foil, quatre));
    stone = min(stone, 0.935 - r);
    float stoneCover = saturate(0.5 - stone / pxw);

    // ---- Glass: which pane, and the lead that divides it into pieces ---------------------------------
    bool inOculus = oculus < 0.0;
    bool inLancet = lancet < 0.0;
    bool inHead = lancetHead < 0.0;
    bool inRoundel = roundel < 0.0;
    bool inArcade = (arcade < 0.0) && (r > 0.82);

    // Diamond quarries in the lancets, radial leads in the roundels, rings and spokes elsewhere.
    float2 lancetLocal = float2(q.x - 0.185, q.y) / 0.042;
    float2 diamonds = float2(lancetLocal.x + lancetLocal.y, lancetLocal.x - lancetLocal.y);
    float2 diamondCell = floor(diamonds);
    float2 diamondFrac = abs(frac(diamonds) - 0.5);
    float leadDistance = (0.5 - max(diamondFrac.x, diamondFrac.y)) * 0.042 * 0.7071;
    float border = abs(lancet + 0.018);                                   // a border strip inside each lancet
    leadDistance = min(leadDistance, border);
    float2 pieceId = diamondCell + sectorIndex * 17.0;

    float2 rq = q24 - roundelCentre;
    float roundelAngle = atan2(rq.y, rq.x);
    if (inRoundel)
    {
        float spoke = length(rq) * abs(sin(roundelAngle * 4.0)) * 0.25;
        leadDistance = min(abs(length(rq) - 0.052), spoke);
        pieceId = float2(index24, floor(roundelAngle * 1.27) + step(0.052, length(rq)) * 9.0);
    }
    if (!inLancet && !inRoundel && !inOculus)
    {
        // Background panes: a polar grid of irregular pieces.
        float ringStep = floor(r * 22.0);
        float spokes = theta * 48.0 / (2.0 * PI) + rwHash(float2(ringStep, 3.0));
        float ringLead = abs(frac(r * 22.0 + 0.5) - 0.5) / 22.0;
        float spokeLead = abs(frac(spokes + 0.5) - 0.5) * 2.0 * PI * r / 48.0;
        leadDistance = min(ringLead, spokeLead);
        pieceId = float2(ringStep, floor(spokes));
    }
    if (inOculus)
    {
        // Two rings of lead and six spokes.
        leadDistance = min(min(abs(r - 0.045), abs(r - 0.075)), r * abs(sin(theta * 3.0)) / 3.0);
        pieceId = float2(floor(theta * 0.95), floor(r * 30.0));
    }
    float lead = saturate(0.5 - (leadDistance - max(pxw * 0.45, 0.0016)) / pxw);

    // Colours: the classic blues and rubies with gold and emerald accents.
    float3 ruby = float3(1.0, 0.07, 0.09);
    float3 sapphire = float3(0.08, 0.22, 1.0);
    float3 gold = float3(1.0, 0.62, 0.1);
    float3 emerald = float3(0.08, 0.8, 0.35);
    float3 amethyst = float3(0.55, 0.12, 1.0);
    float pieceRandom = rwHash(pieceId + 0.37);
    float3 glass = sapphire * 0.8;
    if (inLancet)
    {
        bool borderStrip = lancet > -0.018;
        float pick = rwHash(diamondCell + float2(sectorIndex, 1.0));
        glass = (wrap(sectorIndex, 2.0) < 0.5) ? ruby : sapphire;
        glass = (pick > 0.93) ? gold : ((pick > 0.88) ? emerald : glass);
        glass = borderStrip ? ((wrap(sectorIndex, 2.0) < 0.5) ? gold : ruby) : glass;
    }
    glass = inHead ? gold : glass;
    glass = inRoundel ? ((length(rq) < 0.036 + 0.011 * cos(4.0 * roundelAngle)) ? gold : ((wrap(index24, 2.0) < 0.5) ? ruby : emerald)) : glass;
    glass = inArcade ? ((wrap(index48, 2.0) < 0.5) ? ruby : amethyst) : glass;
    glass = inOculus ? ((r < 0.045) ? float3(1.0, 0.95, 0.85) * 1.8 : ((r < 0.075) ? ruby : gold * 1.3)) : glass;
    glass = (!inLancet && !inRoundel && !inOculus && !inArcade && pieceRandom > 0.9) ? ruby : glass;

    // Waves of colour sweep outward through the glass in a slow pinwheel, turning every pane through
    // the jewel colours as they pass.
    float wave = sin(r * 6.0 - Time * 0.4 + sectorIndex * 0.5);
    glass = max(rwHueTurn(glass, wave * 0.3 + sin(Time * 0.031) * 0.3), 0.0);

    // Light from behind: a wandering sun, each sector's part of the spectrum, a ripple on each beat.
    float2 sun = float2(sin(Time * 0.07), cos(Time * 0.053)) * 0.55;
    float sunLight = 0.4 + 1.1 * exp(-dot(w - sun, w - sun) * 2.2);
    float band = spectrum(frac((sectorIndex + 12.0) / 12.0) * 0.9 + 0.05);
    float ripple = Beat * exp(-abs(r - (1.0 - Beat) * 1.1) * 9.0);
    float mottle = 0.75 + 0.5 * noise(w * 60.0 + pieceRandom * 10.0);
    // Bands of light spiral out through the panes, as if a light show were playing behind the window.
    float sweep = 0.5 + 0.5 * sin(r * 11.0 - theta * 2.0 - Time * 0.9);
    float light = (sunLight * (0.6 + band * 0.7) * (0.55 + 0.9 * sweep * sweep) + ripple * 1.2)
                * (0.6 + 0.6 * pieceRandom) * mottle;
    float glassMask = step(r, 0.935);
    float3 colour = glass * light * 0.85 * glassMask;

    // Lead and stone are dark, with a thin bevel catching the sun.
    colour *= (1.0 - lead) * (1.0 - stoneCover);
    float bevel = saturate(1.0 - abs(stone) / pxw) * 0.18 * sunLight;
    colour += float3(1.0, 0.82, 0.55) * bevel + float3(0.02, 0.016, 0.014) * stoneCover;

    // Outside the window: dark wall with a faint carved ring.
    float wall = step(0.935, r);
    colour += wall * float3(0.012, 0.01, 0.009) * (1.0 + 2.0 * saturate(1.0 - abs(r - 0.97) / 0.01));

    return colour + previous(uv) * 0.05;
}
