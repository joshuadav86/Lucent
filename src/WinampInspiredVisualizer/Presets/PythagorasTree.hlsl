// Name: Pythagoras Tree
// A tree of glass squares, each branching into two smaller squares on the sides of a right-angled triangle, swaying as if in a breeze. Its branches bend with the mids, every beat runs a pulse of light up from the trunk, and blossoms of light shimmer at the tips with the music.
// Family: Fractal
// Tags: Geometric, Calm
// Added: 2026-10-17 21:50

static const int TreeMaxDepth = 12;

// The branching angle at each level: a slow sway that travels up the tree like wind through a canopy.
float treeAngle(int depth)
{
    float d = (float)depth;
    float baseAngle = 0.785 + 0.16 * sin(Time * 0.11) + Mid * 0.12;
    return baseAngle + 0.07 * sin(Time * 0.7 - d * 0.55) + 0.03 * sin(Time * 1.3 - d * 1.1);
}

float3 treeBranchColour(float depth01, float hue)
{
    // Deep teal trunk, through jade and sea green, to warm gold near the tips.
    float3 trunk = float3(0.05, 0.32, 0.42);
    float3 middle = float3(0.18, 0.65, 0.55);
    float3 tips = float3(1.0, 0.72, 0.38);
    float3 c = depth01 < 0.6 ? lerp(trunk, middle, depth01 / 0.6) : lerp(middle, tips, (depth01 - 0.6) / 0.4);
    float3 shifted = hsv2rgb(frac(0.47 + hue), 0.6, 0.6);
    return lerp(c, shifted, 0.15);
}

// Draws the whole tree at q (world units, +y up). Walks the tree depth first, skipping any branch whose
// bounding circle is too far away to touch this pixel, and paints each square over its parent.
float3 drawTree(float2 q, float px, float hue, float pulseDepth, int maxDepth)
{
    float2 corner[13];   // bottom-left corner of the square at each level of the current path
    float2 edge[13];     // its bottom edge
    float leafId[13];    // which branch this is, for giving each blossom its own note
    int stage[13];       // 0 = not drawn yet, 1 = left child done next, 2 = right child next, 3 = finished

    float3 colour = 0.0;
    for (int slot = 0; slot < 13; slot++)
    {
        corner[slot] = 0.0;
        edge[slot] = 0.0;
        leafId[slot] = 0.0;
        stage[slot] = 0;
    }
    float baseSide = 0.36;
    corner[0] = float2(-baseSide * 0.5, -0.74);
    edge[0] = float2(baseSide, 0.0);

    int depth = 0;
    int budget = 900;
    [loop]
    for (int visit = 0; visit < budget; visit++)
    {
        float2 o = corner[depth];
        float2 u = edge[depth];
        float2 v = float2(-u.y, u.x);
        float side = length(u);
        int st = stage[depth];

        if (st == 0)
        {
            // Paint this square: translucent glass with a bright rim.
            float2 rel = q - o;
            float2 local = float2(dot(rel, u), dot(rel, v)) / (side * side) - 0.5;   // -0.5..0.5 inside
            float2 box = abs(local) - 0.5;
            float dist = (length(max(box, 0.0)) + min(max(box.x, box.y), 0.0)) * side;   // signed, world units
            float depth01 = (float)depth / (float)maxDepth;
            float3 tint = treeBranchColour(depth01, hue);
            float pulse = exp(-abs((float)depth - pulseDepth) * 0.9) * Beat;
            float inside = saturate(0.5 - dist / px);
            float rimLine = saturate(1.0 - abs(dist) / px * 0.9);
            float halo = exp(-max(dist, 0.0) / (side * 0.08 + px));
            // Glass fill: darker in the middle, lighter towards the rim, lit from the top left.
            float shade = 0.35 + 0.65 * saturate(max(abs(local.x), abs(local.y)) * 2.0);
            shade *= 0.8 + 0.4 * saturate(0.5 - local.x * 0.6 + local.y * 0.6);
            float3 fill = tint * shade * (0.16 + 0.1 * Bass);
            colour = lerp(colour, fill, inside * 0.8);
            colour += tint * rimLine * (0.45 + 0.35 * Bass + 1.2 * pulse) * lerp(1.0, 0.65, depth01);
            colour += tint * halo * 0.03 * (1.0 + 3.0 * pulse);

            // Blossoms of light on the finest twigs.
            if (depth >= maxDepth - 1)
            {
                float2 centre = o + (u + v) * 0.5;
                float r = length(q - centre);
                float note = frac(leafId[depth] * 0.618034);
                float loud = spectrum(0.05 + note * 0.8);
                float twinkle = 0.6 + 0.4 * sin(Time * (2.0 + note * 3.0) + note * 40.0);
                float3 blossom = hsv2rgb(frac(0.9 + note * 0.3 + hue), 0.7, 1.0);
                float radius = side * (0.9 + loud * 0.9);
                colour += blossom * exp(-r * r / (radius * radius)) * (0.25 + 0.75 * loud + 0.4 * Treble * twinkle) * 0.42;
            }
            stage[depth] = 1;

            // Skip this square's branches if they cannot reach this pixel.
            float2 middle = o + (u + v) * 0.5;
            bool farAway = length(q - middle) > side * 5.2 + px * 4.0;
            if (depth >= maxDepth || farAway)
            {
                stage[depth] = 3;
            }
        }
        else if (st == 1 || st == 2)
        {
            float angle = treeAngle(depth);
            float s, c;
            sincos(angle, s, c);
            float2 leftEdge = rotate(u, angle) * c;
            float2 apex = o + v + leftEdge;
            stage[depth] = st + 1;
            int child = depth + 1;
            if (st == 1)
            {
                corner[child] = o + v;
                edge[child] = leftEdge;
                leafId[child] = leafId[depth] * 2.0 + 1.0;
            }
            else
            {
                corner[child] = apex;
                edge[child] = (o + u + v) - apex;
                leafId[child] = leafId[depth] * 2.0 + 2.0;
            }
            stage[child] = 0;
            depth = child;
        }
        else
        {
            if (depth == 0)
            {
                break;
            }
            depth -= 1;
        }
    }
    return colour;
}

float3 Render(float2 uv, float2 p)
{
    // Gentle drift and breathing so the tree never sits still in the frame.
    float2 q = float2(p.x, -p.y);
    q = q * (1.02 + 0.05 * sin(Time * 0.05)) + float2(0.06 * sin(Time * 0.037), 0.03 * sin(Time * 0.029) + 0.02);
    float px = pixelSize() * (1.02 + 0.05 * sin(Time * 0.05));
    float hue = Time * 0.006;
    float pulseDepth = (1.0 - Beat) * 14.0 - 1.0;   // each beat climbs from the trunk to the tips
    int maxDepth = min(fractalDepth(11), TreeMaxDepth);

    // Night sky: deep blue, with a soft glow behind the canopy.
    float3 sky = lerp(float3(0.006, 0.01, 0.03), float3(0.02, 0.035, 0.07), saturate(q.y * 0.5 + 0.6));
    sky += float3(0.05, 0.08, 0.1) * exp(-length(q - float2(0.0, 0.1)) * 1.6) * (0.6 + Level * 0.6);

    float3 colour = sky;
    float groundY = -0.74;
    if (q.y >= groundY)
    {
        colour += drawTree(q, px, hue, pulseDepth, maxDepth);
    }
    else
    {
        // A still, dark lake below the trunk reflecting the tree, rippling slightly.
        float below = groundY - q.y;
        float2 mirrored = float2(q.x + 0.004 * sin(q.y * 140.0 + Time * 2.0) * (0.5 + Bass), groundY + below);
        colour = sky * 0.5 + drawTree(mirrored, px, hue, pulseDepth, maxDepth) * 0.28 * exp(-below * 6.0);
        colour += float3(0.2, 0.5, 0.6) * exp(-below / px * 0.8) * 0.15;
    }
    return lerp(colour, previous(uv), 0.15);
}
