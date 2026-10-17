// Name: Prism Mandala
// A many-ringed mandala of white-hot line work seen through a prism: each beat splits every line into
// rainbow fringes that slowly reconverge, while layered rings turn against each other and keep reshaping.

float pmLine(float d, float halfWidth, float px)
{
    float w = max(halfWidth, px * 0.5);
    return saturate((w - abs(d)) / px + 0.5) * (halfWidth / w);
}

// Distance to n straight lines tangent to a circle of radius d0 (the chords of a star polygon).
float pmStarChords(float r, float theta, float n, float d0)
{
    float wedge = 2.0 * PI / n;
    float c = acos(saturate(d0 / max(r, 1e-4)));
    float psi1 = floor((theta - c) / wedge + 0.5) * wedge;
    float psi2 = floor((theta + c) / wedge + 0.5) * wedge;
    return min(abs(r * cos(theta - psi1) - d0), abs(r * cos(theta - psi2) - d0));
}

// Folds an angle into one mirrored wedge of an n-fold pattern; x runs along the wedge's bisector.
float2 pmFold(float r, float theta, float n)
{
    float wedge = 2.0 * PI / n;
    float a = abs(wrap(theta, wedge) - wedge * 0.5);
    return float2(cos(a), sin(a)) * r;
}

// The mandala's line work in polar coordinates, returned in three groups that take different tints.
// shape: petal bulge, chain radius, star depth, scallop radius. rings: radii of rings 1-4 (ring 5 in r5).
float3 pmMandala(float r, float theta, float4 shape, float4 rings, float r5, float clock, float hw, float px)
{
    float r1 = rings.x;
    float r2 = rings.y;
    float r3 = rings.z;
    float r4 = rings.w;

    // Core: an {8/3} star inside a circle.
    float core = pmLine(pmStarChords(r, theta + clock * 0.3, 8.0, r1 * 0.38), hw, px) * step(r, r1 * 0.95);
    core += pmLine(r - r1 * 0.95, hw, px);

    // Ring 1: twelve vesica petals with midribs.
    float2 f1 = pmFold(r, theta - clock * 0.11, 12.0);
    float span = (r2 * 0.97 - r1) * 0.5;
    float bulge = span * shape.x;
    float rho = (span * span + bulge * bulge) / (2.0 * bulge);
    float2 mid = float2(r1 + span, 0.0);
    float lens = max(length(f1 - mid - float2(0.0, bulge - rho)), length(f1 - mid + float2(0.0, bulge - rho))) - rho;
    float petals = pmLine(lens, hw, px) + pmLine(f1.y, hw * 0.7, px) * step(lens, 0.0) * 0.5;
    petals += step(lens, 0.0) * 0.04;

    // Ring 2: a chain of sixteen interlaced circles.
    float2 f2 = pmFold(r, theta + clock * 0.07, 16.0);
    float halfWedge = PI / 16.0;
    float rr = r2 + 0.09;
    float2 cA = float2(rr * cos(halfWedge), rr * sin(halfWedge));
    float2 cB = float2(cA.x, -cA.y);
    float circles = pmLine(min(abs(length(f2 - cA) - shape.y), abs(length(f2 - cB) - shape.y)), hw, px);

    // Ring 3: a 24-pointed star polygon between two circles.
    float star = pmLine(pmStarChords(r, theta - clock * 0.05, 24.0, r3 * cos(PI * shape.z)), hw, px) * step(r, r3);
    star += pmLine(r - r3, hw, px) + pmLine(r - r2 - 0.19, hw, px) * 0.6;

    // Ring 4: a bead chain and scalloped arcs.
    float wedge4 = 2.0 * PI / 36.0;
    float a4 = theta + clock * 0.09;
    float nearest = floor(a4 / wedge4 + 0.5) * wedge4;
    float2 bead = float2(r * cos(a4 - nearest) - (r3 + r4) * 0.5, r * sin(a4 - nearest));
    float beads = pmLine(length(bead) - 0.016, hw, px) * 0.8;
    float2 f4 = pmFold(r, theta - clock * 0.04, 18.0);
    float scallop = pmLine(length(f4 - float2(r4, 0.0)) - shape.w, hw, px) * step(r4 - 0.005, r);

    // Ring 5: fine radial hatching between two circles.
    float wedge5 = 2.0 * PI / 96.0;
    float a5 = theta - clock * 0.02;
    float hatchAngle = a5 - floor(a5 / wedge5 + 0.5) * wedge5;
    float hatch = pmLine(r * sin(hatchAngle), hw * 0.8, px) * step(r4 + 0.1, r) * step(r, r5) * 0.6;
    hatch += pmLine(r - r5, hw, px) + pmLine(r - r4 - 0.1, hw, px);

    // Beyond: great lotus petals reaching off the screen.
    float2 f6 = pmFold(r, theta + clock * 0.025, 10.0);
    float lotus = pmLine(length(f6 - float2(r5 + 0.05, 0.0)) - (0.3 + shape.x * 0.15), hw, px) * step(r5, r) * 0.8;

    return float3(core * 1.2 + circles + scallop, petals + beads + lotus, star + hatch);
}

// A simpler, larger ghost of the mandala for the background layer.
float pmGhost(float r, float theta, float4 shape, float clock, float hw, float px)
{
    float star = pmLine(pmStarChords(r, theta - clock * 0.05, 20.0, 0.55 * cos(PI * shape.z)), hw, px) * step(r, 0.55);
    float2 f2 = pmFold(r, theta + clock * 0.07, 14.0);
    float2 cA = float2(0.78 * cos(PI / 14.0), 0.78 * sin(PI / 14.0));
    float circles = pmLine(abs(length(f2 - cA) - shape.y * 2.0), hw, px);
    float2 f6 = pmFold(r, theta + clock * 0.025, 8.0);
    float lotus = pmLine(length(f6 - float2(0.95, 0.0)) - 0.3 - shape.x * 0.2, hw, px) * step(0.9, r);
    return star + circles + lotus + pmLine(r - 0.55, hw, px) + pmLine(r - 0.9, hw, px);
}

float3 Render(float2 uv, float2 p)
{
    float px = pixelSize();
    float hw = max(px * 0.5, 0.0012);
    float2 q = rotate(p, Time * 0.02) * (1.05 - Bass * 0.04);
    float radius0 = length(q);
    float theta0 = atan2(q.y, q.x);

    // Each ring reshapes on its own slow clock.
    float4 shape = float4(0.3 + 0.18 * sin(Time * 0.071), 0.07 + 0.035 * sin(Time * 0.053 + 1.0),
                          0.35 + 0.2 * sin(Time * 0.041 + 2.0), 0.08 + 0.03 * sin(Time * 0.067 + 3.0));

    // Dispersion: lines split into a spectrum on each beat and settle back to white.
    float dispersion = 0.012 + Beat * 0.07 + Treble * 0.015;
    float twist = 0.006 + Beat * 0.035;

    // Ring radii breathe with their parts of the spectrum.
    float4 rings = float4(0.16 + spectrum(0.1) * 0.02, 0.34 + spectrum(0.25) * 0.025,
                          0.52 + spectrum(0.45) * 0.03, 0.7 + spectrum(0.65) * 0.03);
    float r5 = 0.9 + spectrum(0.85) * 0.03;

    // Pale tints that drift around the rings, so the white light flows with colour.
    float hue = Time * 0.03;
    float3 tintA = hsv2rgb(hue, 0.3, 1.0);
    float3 tintB = hsv2rgb(hue + 0.33, 0.3, 1.0);
    float3 tintC = hsv2rgb(hue + 0.66, 0.3, 1.0);

    float3 colour = float3(0.0, 0.0, 0.0);
    float3 ghost = float3(0.0, 0.0, 0.0);
    float3 weightSum = float3(0.0, 0.0, 0.0);
    int wavelengths = qualitySteps(5);
    [loop]
    for (int i = 0; i < wavelengths; i++)
    {
        float lambda = (i + 0.5) / wavelengths;
        float3 w = hsv2rgb((1.0 - lambda) * 0.76, 1.0, 1.0);
        float shift = lambda - 0.5;
        float r = radius0 * (1.0 + dispersion * shift);
        float theta = theta0 + twist * shift;
        float3 m = pmMandala(r, theta, shape, rings, r5, Time, hw, px);
        colour += w * (tintA * m.x + tintB * m.y + tintC * m.z);

        // A larger ghost of the mandala behind, turning the other way and splitting much wider.
        float rg = radius0 * 0.58 * (1.0 + (dispersion * 3.0 + 0.03) * shift);
        ghost += w * pmGhost(rg, -theta0 + twist * 3.0 * shift + 0.3, shape, -Time * 0.7, hw * 0.58, px * 0.58);
        weightSum += w;
    }
    colour = (colour + ghost * hsv2rgb(Time * 0.03 + 0.5, 0.4, 0.3)) / weightSum * (0.85 + Level * 0.6);

    // A glint at the heart.
    colour += float3(1.0, 0.95, 0.9) * exp(-radius0 * 30.0) * (0.4 + Beat * 0.6);

    // Echoes unfold outward from the centre.
    return colour + previous(toUv(p * 0.985)) * 0.3;
}
