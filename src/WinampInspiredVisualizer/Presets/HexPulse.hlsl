// Name: Hex Pulse
// A vast honeycomb of glass hexagonal pillars stretching to the horizon: each ring of cells rises and glows
// with its own part of the spectrum, and every beat sends a wave of light rolling outward through the grid.
// Family: Geometric
// Tags: Neon, 3D, Geometric

// The three directions across a hexagon's flat sides. Neighbouring cell centres are one unit apart along them.
static const float2 HP_N0 = float2(1.0, 0.0);
static const float2 HP_N1 = float2(0.5, 0.8660254);
static const float2 HP_N2 = float2(-0.5, 0.8660254);

// Centre of the hexagonal cell containing x (cube-coordinate rounding).
float2 hpCell(float2 x)
{
    float j = x.y / 0.8660254;
    float i = x.x - j * 0.5;
    float k = -i - j;
    float ri = round(i);
    float rj = round(j);
    float rk = round(k);
    float di = abs(ri - i);
    float dj = abs(rj - j);
    float dk = abs(rk - k);
    if (di > dj && di > dk)
        ri = -rj - rk;
    else if (dj > dk)
        rj = -ri - rk;
    return ri * HP_N0 + rj * HP_N1;
}

// Height of the pillar standing on cell centre c: the spectrum spread in rings from the middle,
// a slow swell, and the beat wave rolling outward.
float hpHeight(float2 c, out float band)
{
    float r = length(c);
    band = saturate(r / 22.0);
    float level = spectrum(0.03 + band * 0.75);
    float swell = 0.5 + 0.5 * sin(r * 0.45 - Time * 1.3 + atan2(c.y, c.x) * 2.0);
    float wave = Beat * exp(-pow(r - (1.0 - Beat) * 16.0 - 1.0, 2.0) * 0.12);
    float seed = frac(sin(dot(c, float2(12.9898, 78.233))) * 43758.5453);
    float pick = pow(0.5 + 0.5 * sin(Time * 0.7 + seed * 31.0), 4.0);
    return 0.1 + level * (0.6 + seed * 1.6 + pick * 0.8) + swell * swell * (0.2 + Level * 0.4) + wave * 1.3;
}

// Distance inside a hexagon of apothem 0.5 to its outline.
float hpEdge(float2 local)
{
    return 0.5 - max(max(abs(dot(local, HP_N0)), abs(dot(local, HP_N1))), abs(dot(local, HP_N2)));
}

float3 hpGlowColour(float band)
{
    // Deep cyan in the middle, through electric blue, to magenta at the rim.
    return lerp(lerp(float3(0.1, 0.85, 1.0), float3(0.25, 0.35, 1.0), saturate(band * 2.0)),
                float3(1.0, 0.25, 0.8), saturate(band * 2.0 - 1.0));
}

float3 Render(float2 uv, float2 p)
{
    float orbit = Time * 0.035;
    float3 eye = float3(sin(orbit) * 11.0, 7.0 + 0.8 * sin(Time * 0.07), -cos(orbit) * 11.0);
    float3 target = float3(sin(Time * 0.05) * 2.0, 0.0, cos(Time * 0.04) * 2.0);
    float zoom = 2.1;
    float3 rd = cameraRay(eye, target, float2(p.x, -p.y), zoom, 0.04 * sin(Time * 0.06));
    float pixelAngle = pixelSize() / zoom;

    float3 haze = float3(0.015, 0.02, 0.05) + float3(0.05, 0.03, 0.09) * (0.6 + Bass * 0.5);
    float3 sky = lerp(haze, float3(0.004, 0.004, 0.012), saturate(rd.y * 3.0));
    if (rd.y > -0.002)
        return sky;

    const float maxHeight = 4.4;
    float t = max((maxHeight - eye.y) / rd.y, 0.0);
    float2 d2 = rd.xz;
    float3 hitNormal = float3(0.0, 1.0, 0.0);
    float2 entryNormal = float2(0.0, 0.0);
    float2 hitCell = float2(0.0, 0.0);
    float hitHeight = 0.0;
    float hitBand = 0.0;
    float hitT = -1.0;
    bool hitTop = true;

    // Walk the ray from cell to cell across the honeycomb, testing each pillar exactly.
    int steps = qualitySteps(110);
    [loop]
    for (int i = 0; i < steps; i++)
    {
        float3 pos = eye + rd * t;
        float2 centre = hpCell(pos.xz + d2 * 1e-4);
        float band = 0.0;
        float h = hpHeight(centre, band);
        if (pos.y <= h)
        {
            hitT = t;
            hitTop = false;
            hitCell = centre;
            hitHeight = h;
            hitBand = band;
            hitNormal = float3(entryNormal.x, 0.0, entryNormal.y);
            break;
        }
        // Where the ray leaves this cell, through which side.
        float2 rel = pos.xz - centre;
        float exitT = 1e9;
        float2 exitNormal = float2(0.0, 0.0);
        float dn0 = dot(d2, HP_N0);
        float dn1 = dot(d2, HP_N1);
        float dn2 = dot(d2, HP_N2);
        float t0 = (sign(dn0) * 0.5 - dot(rel, HP_N0)) / (abs(dn0) > 1e-6 ? dn0 : 1e-6);
        float t1 = (sign(dn1) * 0.5 - dot(rel, HP_N1)) / (abs(dn1) > 1e-6 ? dn1 : 1e-6);
        float t2 = (sign(dn2) * 0.5 - dot(rel, HP_N2)) / (abs(dn2) > 1e-6 ? dn2 : 1e-6);
        exitT = t0;
        exitNormal = HP_N0 * sign(dn0);
        if (t1 < exitT) { exitT = t1; exitNormal = HP_N1 * sign(dn1); }
        if (t2 < exitT) { exitT = t2; exitNormal = HP_N2 * sign(dn2); }
        exitT = max(exitT, 0.0);
        float topT = (h - pos.y) / rd.y;
        if (topT <= exitT)
        {
            hitT = t + topT;
            hitTop = true;
            hitCell = centre;
            hitHeight = h;
            hitBand = band;
            break;
        }
        // Entering the next cell through its side facing back at us.
        entryNormal = -exitNormal;
        t += exitT + 1e-4;
        if (t > 90.0)
            break;
    }

    if (hitT < 0.0)
        return sky;

    float3 hit = eye + rd * hitT;
    float2 local = hit.xz - hitCell;
    float lineWidth = max(0.018, pixelAngle * hitT * 0.9);
    float aa = pixelAngle * hitT;
    float3 glowColour = hpGlowColour(hitBand + 0.06 * sin(Time * 0.1));
    float energy = saturate((hitHeight - 0.12) / 2.2);
    float3 colour;

    if (hitTop)
    {
        // Top: dark smoked glass lit from within by the cell's height, with a crisp neon rim and a fainter inner rim.
        float edge = hpEdge(local);
        float rim = saturate((lineWidth - edge) / aa + 0.5);
        float inner = saturate((lineWidth * 0.6 - abs(edge - 0.09)) / aa + 0.5);
        float centreGlow = exp(-dot(local, local) * 6.0);
        colour = float3(0.012, 0.014, 0.03) + glowColour * (energy * energy * 0.55 + centreGlow * energy * 0.35);
        colour += glowColour * rim * (0.35 + energy * 1.5) + glowColour * inner * energy * 0.6;
        // Sky reflection on the glass.
        float fresnel = pow(1.0 - saturate(-rd.y), 4.0);
        colour += haze * fresnel * 0.8;
    }
    else
    {
        // Sides: light bleeding down from the glowing top, a bright lip along the upper rim,
        // and darker towards the floor between pillars.
        float fromTop = hitHeight - hit.y;
        float lip = saturate((lineWidth - fromTop) / aa + 0.5);
        float bleed = exp(-fromTop * 2.2);
        float facing = 0.4 + 0.6 * saturate(dot(hitNormal, normalize(float3(0.4, 0.6, -0.7))));
        colour = float3(0.008, 0.009, 0.02) * facing + glowColour * (bleed * energy * 0.5 * facing + lip * (0.3 + energy * 1.3));
        // Vertical seams at the hexagon's corners.
        float2 sideLocal = local - hitNormal.xz * dot(local, hitNormal.xz);
        float seam = saturate((lineWidth * 0.7 - abs(length(sideLocal) - 0.288675)) / aa + 0.5);
        colour += glowColour * seam * energy * 0.35 * bleed;
    }

    // Distance haze, so the honeycomb dissolves into the dark towards the horizon.
    float fog = 1.0 - exp(-max(hitT - 10.0, 0.0) * 0.07);
    colour = lerp(colour, sky, fog);
    return colour;
}
