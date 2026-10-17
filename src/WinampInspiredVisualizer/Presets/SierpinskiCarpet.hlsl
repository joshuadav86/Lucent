// Name: Sierpinski Carpet
// An endless glide into a carpet of embossed stone tiles, each tile carved into smaller tiles around a glowing window, and those into smaller ones again. The windows shine with the music: big ones with the bass, tiny ones with the treble.
// Family: Fractal
// Tags: Geometric

static const float CarpetZoomLength = 24.0;               // seconds per ninefold zoom (two levels of the carpet)
static const float2 CarpetAnchor = float2(0.125, 0.375);  // 0.0101.. and 0.1010.. in base 3: a tile corner that maps onto itself when zoomed ninefold

// Colour of a window by how big it looks: large windows are warm, small ones cool, so colours stay put while zooming.
float3 carpetWindowColour(float sizePx, float hue)
{
    float t = saturate((log2(max(sizePx, 1.0)) - 2.0) / 6.5);   // 0 for windows 4 px across, 1 for ~360 px
    // Deep blue for the largest, through violet and rose to amber for the smallest.
    return hsv2rgb(0.56 + (1.0 - t) * 0.56 + 0.06 * sin(2.0 * PI * hue), 0.62 + 0.2 * t, 1.0);
}

// How loud the window's part of the spectrum is: large windows follow the bass, small ones the treble.
float carpetWindowLoudness(float sizePx)
{
    float t = saturate((log2(max(sizePx, 1.0)) - 2.0) / 6.5);
    return spectrum(0.04 + (1.0 - t) * 0.7);
}

// The carpet's surface at w: every level carves its tiles with a small bevel, so tiles hold smaller
// tiles embossed inside them. Stops at the window (the middle ninth) that holds w, if any.
// Returns the height; window size (px, 0 when on stone) and position inside the window come back too.
float carpetSurface(float2 w, float pixelWorld, out float windowPx, out float2 windowLocal, out float3 spill, float hue)
{
    windowPx = 0.0;
    windowLocal = 0.0;
    spill = 0.0;
    float height = 0.0;
    float size = 1.0;
    float2 q = wrap(w, 1.0);
    int levels = fractalDepth(9);
    [loop]
    for (int k = 0; k < levels; k++)
    {
        float2 cell = floor(q * 3.0);
        float2 f = q * 3.0 - cell;
        float px = size / pixelWorld;                  // this level's parent tile, in pixels
        float subPx = px / 3.0;                        // one ninth tile, in pixels
        if (subPx < 1.2)
        {
            break;
        }
        float fade = saturate((subPx - 2.0) / 10.0);   // finer levels appear gradually, never pop
        // Light from this level's window spilling across the surrounding tiles.
        float2 box = abs(q * 3.0 - 1.5) - 0.5;
        float out2 = length(max(box, 0.0));
        spill += carpetWindowColour(subPx, hue) * exp(-out2 * 3.5) * fade * (0.25 + 2.0 * carpetWindowLoudness(subPx));
        if (cell.x == 1.0 && cell.y == 1.0)
        {
            windowPx = subPx;
            windowLocal = f;
            break;
        }
        // A bevel round each ninth tile: flat on top, sloping down into the joints.
        float2 e = min(f, 1.0 - f);
        float edge = min(e.x, e.y);
        // The bevel is never wider than a few pixels, so big tiles get crisp edges and every level
        // slopes the same way.
        float bevelWidth = min(0.08 * size / 3.0, 4.0 * pixelWorld);
        height += bevelWidth * smoothstep(0.0, 1.0, edge * size / 3.0 / bevelWidth) * fade;
        size /= 3.0;
        q = f;
    }
    return height;
}

float3 Render(float2 uv, float2 p)
{
    float zoom = Time / CarpetZoomLength;
    float u = frac(zoom);
    float viewScale = 0.55 * exp2(-u * 3.169925);   // 9^-u: one level of 9 per cycle
    float spin = Time * 0.018;
    float2 view = rotate(float2(p.x, -p.y), spin);
    float2 w = CarpetAnchor + view * viewScale;
    float pixelWorld = pixelSize() * viewScale;
    float hue = Time * 0.008 + (BeatCount - Beat) * 0.012;

    float windowPx;
    float2 windowLocal;
    float3 spill;
    float h = carpetSurface(w, pixelWorld, windowPx, windowLocal, spill, hue);

    // Normal from the height field, with the step one pixel across.
    float3 colour = 0.0;
    float dummyPx;
    float2 dummyLocal;
    float3 dummySpill;
    float2 stepX = rotate(float2(1.0, 0.0), spin) * pixelWorld;
    float2 stepY = rotate(float2(0.0, 1.0), spin) * pixelWorld;
    float hx = carpetSurface(w + stepX, pixelWorld, dummyPx, dummyLocal, dummySpill, hue);
    float hy = carpetSurface(w + stepY, pixelWorld, dummyPx, dummyLocal, dummySpill, hue);
    float relief = 0.9;   // how deep the joints look
    float2 slope = float2(hx - h, hy - h) / pixelWorld * relief;
    float3 normal = normalize(float3(-slope, 1.0));

    // A slow key light sweeping round, plus a cool fill.
    float lightAngle = Time * 0.13;
    float3 lightDir = normalize(float3(cos(lightAngle), sin(lightAngle), 0.9));
    float3 viewDir = float3(0.0, 0.0, 1.0);
    float diffuse = saturate(dot(normal, lightDir));
    float3 halfway = normalize(lightDir + viewDir);
    float specular = pow(saturate(dot(normal, halfway)), 60.0);
    float fill = saturate(dot(normal, normalize(float3(-lightDir.xy, 0.6))));

    float3 stone = float3(0.17, 0.165, 0.18);
    if (windowPx > 0.0)
    {
        // A window: a pane of coloured light set back from the stone, darkest in the shadow of its rim.
        float3 tint = carpetWindowColour(windowPx, hue);
        float loud = carpetWindowLoudness(windowPx);
        float2 e = min(windowLocal, 1.0 - windowLocal);
        float rimPx = min(e.x, e.y) * windowPx;
        float inset = saturate(rimPx / max(windowPx * 0.12, 1.0));
        float2 fromCentre = windowLocal - 0.5;
        float shadowSide = saturate(0.5 - dot(fromCentre, lightDir.xy) * 1.2);
        // Large panes are deep glass lit from far behind; small ones are bright jewels.
        float size01 = saturate(log2(max(windowPx, 1.0)) / 9.0);
        float pane = (0.1 + 0.6 * loud + Beat * 0.08) * (0.3 + 0.7 * inset) * lerp(1.0, 0.7, shadowSide * (1.0 - inset));
        float core = 1.0 - saturate(length(fromCentre) * 1.5);
        float depthGlow = lerp(1.0, 0.25 + 1.1 * core * core, size01);
        pane *= saturate((windowPx - 1.2) / 4.0);   // the tiniest windows fade into the stone
        colour = stone * 0.3 * (1.0 - saturate((windowPx - 1.2) / 4.0)) + tint * pane * depthGlow * (1.3 - 0.6 * size01) + (tint * 0.5 + 0.5) * core * core * core * loud * 0.35 * size01;
        // A thin bright lip where the pane meets the stone.
        colour += tint * exp(-rimPx * 0.9) * (0.25 + loud * 0.6);
    }
    else
    {
        colour = stone * (0.18 + 0.9 * diffuse + 0.25 * fill)
               + float3(0.9, 0.92, 1.0) * specular * (0.35 + Treble * 0.5);
        // Joints catch less of the light.
        float flatness = saturate(normal.z * 1.2 - 0.2);
        colour *= 0.7 + 0.3 * flatness;
    }

    // Coloured light from nearby windows washing over the stone and its bevels.
    colour += spill * (windowPx > 0.0 ? 0.04 : 0.09) * (0.6 + 0.4 * diffuse) * (1.0 + Beat * 0.4);

    // Reproject the last frame along the zoom for a touch of smoothing on the finest tiles.
    float frameZoom = exp2(-3.169925 * DeltaTime / CarpetZoomLength);
    float3 history = previous(toUv(rotate(p, -0.018 * DeltaTime) * frameZoom));
    return lerp(colour, history, 0.25);
}
