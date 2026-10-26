// Name: Origami Folds
// A great sheet of paper folds itself into a field of crisp triangular facets and opens out again in slow waves,
// lit by a low warm lamp; the bass deepens the folds and every beat runs a glint of light along the creases.
// Family: Geometric
// Tags: Calm, 3D, Geometric, Minimal
// Added: 2026-10-17 21:37

static const float OfSpacing = 1.0;
static const float OfSqrt3 = 1.7320508;

// How deeply the paper is folded at a place: a slow wave that rolls across the sheet, pushed by the bass.
float ofFoldAmount(float2 xz)
{
    float wave = 0.5 + 0.5 * sin(dot(xz, float2(0.23, 0.17)) - Time * 0.35);
    float swell = 0.5 + 0.5 * sin(length(xz - float2(3.0, 8.0)) * 0.35 - Time * 0.22);
    return (0.1 + 0.42 * wave * swell) * (0.8 + Bass * 0.5);
}

// Height of a lattice vertex: one of three sublattices rises into pyramid peaks, the others sink,
// slowly giving way to pleats (alternate rows up and down).
float ofVertexHeight(float2 lattice, float2 xz, float pleat)
{
    float third = wrap(lattice.x - lattice.y, 3.0);
    float pyramid = third < 0.5 ? 1.0 : -0.5;
    float rows = wrap(lattice.y, 2.0) < 0.5 ? 0.7 : -0.7;
    return ofFoldAmount(xz) * lerp(pyramid, rows, pleat);
}

float2 ofLatticeToWorld(float2 lattice)
{
    return float2(lattice.x + lattice.y * 0.5, lattice.y * OfSqrt3 * 0.5) * OfSpacing;
}

// The paper's surface over xz: which triangle we're in, its three corners and the height there.
float ofSurface(float2 xz, float pleat, out float3 a, out float3 b, out float3 c, out float3 bary)
{
    float2 lattice = float2(xz.x / OfSpacing - xz.y / (OfSqrt3 * OfSpacing), xz.y * 2.0 / (OfSqrt3 * OfSpacing));
    float2 cell = floor(lattice);
    float2 f = lattice - cell;
    float2 la = cell;
    float2 lb = cell + float2(1.0, 0.0);
    float2 lc = cell + float2(0.0, 1.0);
    if (f.x + f.y > 1.0)
    {
        la = cell + float2(1.0, 1.0);
        f = 1.0 - f;
        lb = cell + float2(0.0, 1.0);
        lc = cell + float2(1.0, 0.0);
    }
    float2 wa = ofLatticeToWorld(la);
    float2 wb = ofLatticeToWorld(lb);
    float2 wc = ofLatticeToWorld(lc);
    a = float3(wa.x, ofVertexHeight(la, wa, pleat), wa.y);
    b = float3(wb.x, ofVertexHeight(lb, wb, pleat), wb.y);
    c = float3(wc.x, ofVertexHeight(lc, wc, pleat), wc.y);
    bary = float3(1.0 - f.x - f.y, f.x, f.y);
    return a.y * bary.x + b.y * bary.y + c.y * bary.z;
}

float ofHeight(float2 xz, float pleat)
{
    float3 a, b, c, bary;
    return ofSurface(xz, pleat, a, b, c, bary);
}

float3 Render(float2 uv, float2 p)
{
    // Slow drift round the sheet, looking down at it.
    float orbit = Time * 0.025;
    float3 target = float3(Time * 0.12, 0.0, Time * 0.05);
    float3 eye = target + float3(sin(orbit) * 3.6, 4.6 + 0.3 * sin(Time * 0.07), -cos(orbit) * 3.6);
    float3 dir = cameraRay(eye, target, float2(p.x, -p.y), 1.5, 0.0);

    float pleat = smoothstep(0.2, 0.8, 0.5 + 0.5 * sin(Time * 0.045));
    float3 sun = normalize(float3(-0.7, 0.42, 0.35));
    float3 sky = lerp(float3(0.03, 0.035, 0.07), float3(0.14, 0.1, 0.13), saturate(1.0 + dir.y * 2.0));

    // March the ray between two flat planes that bound the folds, then refine the crossing.
    float top = 0.9;
    float bottom = -0.6;
    float3 colour = sky;
    if (dir.y < -0.02)
    {
        float t0 = (top - eye.y) / dir.y;
        float t1 = min((bottom - eye.y) / dir.y, 40.0);
        int steps = qualitySteps(64);
        float dt = (t1 - t0) / steps;
        float tPrev = t0;
        float t = t0;
        bool hit = false;
        [loop]
        for (int i = 1; i <= steps; i++)
        {
            t = t0 + dt * i;
            float3 pos = eye + dir * t;
            if (pos.y < ofHeight(pos.xz, pleat))
            {
                hit = true;
                break;
            }
            tPrev = t;
        }
        if (hit)
        {
            float lo = tPrev;
            float hi = t;
            for (int k = 0; k < 6; k++)
            {
                float mid = 0.5 * (lo + hi);
                float3 pos = eye + dir * mid;
                if (pos.y < ofHeight(pos.xz, pleat))
                {
                    hi = mid;
                }
                else
                {
                    lo = mid;
                }
            }
            float3 pos = eye + dir * hi;
            float3 a, b, c, bary;
            ofSurface(pos.xz, pleat, a, b, c, bary);
            float3 normal = normalize(cross(c - a, b - a));
            normal = normal.y < 0.0 ? -normal : normal;

            // Soft shadow: step towards the lamp and see how close the paper comes to blocking it.
            float shadow = 1.0;
            float st = 0.05;
            [loop]
            for (int s = 0; s < 14; s++)
            {
                float3 sp = pos + sun * st;
                float clearance = sp.y - ofHeight(sp.xz, pleat);
                shadow = min(shadow, saturate(clearance * 6.0 / st + 0.15));
                st += 0.12;
            }

            // Paper: soft warm white, faintly tinted by a slowly drifting pastel.
            float3 paper = lerp(float3(0.9, 0.88, 0.85), hsv2rgb(0.55 + 0.45 * sin(Time * 0.02), 0.3, 0.9), 0.2);
            float diffuse = saturate(dot(normal, sun));
            float fill = 0.5 + 0.5 * normal.y;
            float3 lit = paper * (float3(1.0, 0.84, 0.66) * diffuse * shadow * 1.55
                                  + float3(0.22, 0.3, 0.55) * fill * 0.38);
            // Light passing through thin paper glows warmly on the shaded side.
            lit += paper * float3(0.9, 0.55, 0.3) * saturate(-dot(normal, sun)) * 0.08;

            // Creases: thin lines along the triangle edges, catching a glint of light on the beat.
            float edge = min(bary.x, min(bary.y, bary.z));
            float pxWorld = hi * pixelSize() / 1.6;
            float crease = 1.0 - smoothstep(0.0, pxWorld * 1.5 + 0.004, edge * OfSpacing * 0.87);
            float ridge = saturate(dot(normal, sun) + 0.3);
            lit *= 1.0 - crease * 0.15;
            lit += float3(1.0, 0.86, 0.66) * crease * ridge * (Beat * 0.6 + Treble * 0.12);

            // Fade into the evening haze with distance.
            float haze = 1.0 - exp(-max(hi - 3.0, 0.0) * 0.09);
            colour = lerp(lit, sky, haze);
        }
    }
    // A warm glow low in the sky where the lamp sits.
    colour += float3(0.5, 0.3, 0.18) * pow(saturate(dot(dir, sun)), 12.0) * 0.12;
    return colour;
}
