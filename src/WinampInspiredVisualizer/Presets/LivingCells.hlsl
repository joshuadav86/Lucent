// Name: Living Cells
// Glowing plant cells under the microscope, chloroplasts streaming round and round inside their wavy walls, as waves of light ripple from cell to cell on every beat.
// Family: Organic
// Tags: Calm, Neon

// Where the centre of grid cell `cell` is now, wandering inside its square.
float2 cellCentre(float2 cell)
{
    float2 h = float2(hash(cell), hash(cell + 17.3));
    return 0.5 + 0.3 * sin(Time * (0.15 + 0.12 * h.yx) + 6.2831 * h);
}

// Voronoi with exact distances to the cell walls (after Inigo Quilez).
// Returns: x = distance to the nearest wall, yz = offset from the pixel to its cell's centre,
// w = that cell's id. wallNormal: the direction across the nearest wall.
float4 cellVoronoi(float2 x, out float2 wallNormal)
{
    float2 n = floor(x);
    float2 f = frac(x);
    float2 nearestGrid = 0.0;
    float2 nearestOffset = 0.0;
    float best = 8.0;
    [loop]
    for (int j = -1; j <= 1; j++)
    {
        [unroll]
        for (int i = -1; i <= 1; i++)
        {
            float2 g = float2(i, j);
            float2 r = g + cellCentre(n + g) - f;
            float d = dot(r, r);
            if (d < best)
            {
                best = d;
                nearestOffset = r;
                nearestGrid = g;
            }
        }
    }
    float wall = 8.0;
    wallNormal = float2(1.0, 0.0);
    [loop]
    for (int jj = -1; jj <= 1; jj++)
    {
        [unroll]
        for (int ii = -1; ii <= 1; ii++)
        {
            float2 g = nearestGrid + float2(ii, jj);
            float2 r = g + cellCentre(n + g) - f;
            float2 between = r - nearestOffset;
            if (dot(between, between) > 0.00001)
            {
                float2 normal = normalize(between);
                float d = dot(0.5 * (nearestOffset + r), normal);
                if (d < wall)
                {
                    wall = d;
                    wallNormal = normal;
                }
            }
        }
    }
    float2 id = n + nearestGrid;
    return float4(wall, nearestOffset, id.x + id.y * 1000.0);
}

// A blurry layer of cells further down, out of focus: only the walls, softly.
float cellBlurLayer(float2 x)
{
    float2 n = floor(x);
    float2 f = frac(x);
    float first = 8.0;
    float second = 8.0;
    [loop]
    for (int j = -1; j <= 1; j++)
    {
        [unroll]
        for (int i = -1; i <= 1; i++)
        {
            float2 g = float2(i, j);
            float2 r = g + cellCentre(n + g + 71.0) - f;
            float d = dot(r, r);
            second = min(second, max(d, first));
            first = min(first, d);
        }
    }
    return sqrt(second) - sqrt(first);
}

float3 Render(float2 uv, float2 p)
{
    const float scale = 2.3;
    float px = pixelSize() * scale;            // one pixel in cell units

    // Colours drift through three stains: walls, chloroplasts, nuclei.
    float phase = Time / 40.0;
    float blend = smoothstep(0.7, 1.0, frac(phase));
    float pick = wrap(floor(phase), 3.0);
    float wallHue = pick < 0.5 ? lerp(0.5, 0.72, blend) : (pick < 1.5 ? lerp(0.72, 0.42, blend) : lerp(0.42, 0.5, blend));
    float3 plastA = float3(1.0, 0.15, 0.3);
    float3 plastB = float3(1.0, 0.55, 0.1);
    float3 plastC = float3(1.0, 0.25, 0.8);
    float3 plastTint = pick < 0.5 ? lerp(plastA, plastB, blend) : (pick < 1.5 ? lerp(plastB, plastC, blend) : lerp(plastC, plastA, blend));
    float3 coreTint = hsv2rgb(wallHue - 0.2, 0.6, 1.0);

    // The tissue drifts slowly; its walls wriggle like jigsaw pieces.
    float2 drift = float2(Time * 0.035, Time * 0.015);
    float2 x = p * scale + drift;
    x += 0.07 * sin(x.yx * 6.0 + float2(Time * 0.3, Time * 0.23));

    // Below, a second layer of cells out of focus.
    float2 deep = p * scale * 0.7 + drift * 0.6 + 40.0;
    float blurWall = cellBlurLayer(deep);
    float3 colour = hsv2rgb(wallHue + 0.12, 0.7, 1.0) * (exp(-blurWall * 7.0) * 0.1 + 0.004);

    float2 wallNormal;
    float4 cell = cellVoronoi(x, wallNormal);
    float wall = cell.x;
    float2 q = -cell.yz;                                // the pixel relative to its cell's centre
    float id = cell.w;
    float seed = hash(float2(id * 0.0137, 3.7));
    float seed2 = hash(float2(id * 0.0213, 8.1));

    // A calcium wave: on each beat a ring spreads from somewhere, lighting cell after cell.
    float2 centreOnScreen = (x + q - drift) / scale;
    float2 origin = float2(hash(float2(BeatCount, 1.3)) - 0.5, hash(float2(BeatCount, 2.9)) - 0.5) * float2(2.0 * Aspect, 2.0) * 0.6;
    float waveRadius = (1.0 - Beat) * 2.6;
    float flash = exp(-pow((length(centreOnScreen - origin) - waveRadius) / 0.25, 2.0)) * Beat;
    float lift = 1.0 + flash * 2.0;

    // Wall colour flows across the tissue in slow waves.
    float3 wallTint = hsv2rgb(wallHue + 0.08 * sin(centreOnScreen.x * 1.3 + centreOnScreen.y * 0.9 - Time * 0.3), 0.65, 1.0);

    // Each cell's clear vacuole shows the blurry layer below; a faint glow lines its wall.
    float inside = smoothstep(0.0, 0.05, wall);
    colour += wallTint * (0.004 + 0.01 * seed + exp(-wall * 10.0) * 0.05) * lift * inside;
    colour += lerp(wallTint, float3(1.0, 1.0, 1.0), 0.3) * flash * (0.12 + exp(-wall * 6.0) * 0.25) * inside;

    // Walls: each cell's own wall, so neighbours show a crisp double line with a soft halo.
    float membrane = abs(wall - 0.022) / px;
    colour += wallTint * (exp(-membrane * membrane * 0.7) * (0.75 + 0.35 * seed2) + exp(-membrane * 0.4) * 0.08) * lift;

    // Chloroplasts streaming round each cell just inside its wall, in two lanes. Each sits on the
    // line from the cell's centre through its slot, where that line meets the wall.
    float around = atan2(q.y, q.x);
    float radius = length(q);
    float slant = max(dot(q / max(radius, 0.001), wallNormal), 0.3);
    float reach = radius + wall / slant;
    float direction = seed > 0.5 ? 1.0 : -1.0;
    float streaming = Time * (0.2 + 0.12 * seed2 + Mid * 0.15) * direction;
    [unroll]
    for (int lane = 0; lane < 2; lane++)
    {
        float k = lane;
        float depth = 0.065 + k * 0.065;
        const float slots = 20.0;
        float turn = wrap(around - streaming * (1.0 - k * 0.3) + k * 0.16, 2.0 * PI) / (2.0 * PI) * slots;
        float slot = floor(turn);
        float present = step(0.2, hash(float2(slot + k * 50.0, id)));
        float slotAngle = around - (frac(turn) - 0.5) * 2.0 * PI / slots;
        float2 plastAt = float2(cos(slotAngle), sin(slotAngle)) * max(reach - depth / slant, 0.05);
        float size = 0.03 + 0.012 * hash(float2(slot, id + 3.0));
        float2 fromPlast = q - plastAt;
        float disc = (length(fromPlast) - size) / px;
        float body = 1.0 - smoothstep(-1.0, 0.5, disc);
        float edge = exp(-disc * disc * 0.5);
        float shine = exp(-dot(fromPlast + size * 0.35, fromPlast + size * 0.35) / (px * px) * 0.8);
        float3 plast = plastTint * (body * 0.35 + edge * 0.5 + exp(-max(disc, 0.0) * 0.5) * 0.05) + float3(1.0, 0.9, 0.8) * shine * body * 0.6;
        colour += plast * present * smoothstep(0.4, 0.6, slant) * lift;
    }

    // Some cells show their nucleus, pressed to one side: a crisp envelope, grainy chromatin and
    // a bright nucleolus, swelling with the bass.
    float2 nucleusAt = float2(cos(seed * 6.2831), sin(seed * 6.2831)) * 0.2;
    float2 nq = rotate(q - nucleusAt, seed2 * PI) / (float2(0.15, 0.11) * (1.0 + Bass * 0.12));
    float nucleus = length(nq);
    float hasNucleus = step(0.35, seed2);
    float envelope = abs(nucleus - 1.0) * 0.11 / px;
    float inCell = smoothstep(0.04, 0.08, wall) * hasNucleus;
    float fill = (1.0 - smoothstep(0.85, 1.0, nucleus)) * inCell;
    float chromatin = noise(nq * 3.0 + id) * 0.6 + noise(nq * 7.0 - id) * 0.4;
    colour = lerp(colour, coreTint * (0.04 + chromatin * chromatin * 0.4) * lift, fill * 0.9);
    colour += coreTint * exp(-envelope * envelope * 0.8) * 0.4 * inCell * lift;
    float nucleolus = length(nq - float2(0.3, -0.2)) * 0.11 / px;
    colour += lerp(coreTint, float3(1.0, 1.0, 1.0), 0.5) * exp(-nucleolus * nucleolus * 0.5) * fill * (0.5 + Bass * 0.8);

    // Specks drifting in the cytoplasm, twinkling with the treble.
    float2 grid = x * 12.0;
    float2 speckCell = floor(grid);
    float2 spot = float2(hash(speckCell + 0.3), hash(speckCell + 5.1)) * 0.6 + 0.2;
    float gap = length(frac(grid) - spot) / (12.0 * px);
    float speck = step(0.88, hash(speckCell + 2.2)) * exp(-gap * gap * 0.6) * (1.0 - fill) * smoothstep(0.05, 0.1, wall);
    colour += lerp(wallTint, float3(1.0, 1.0, 1.0), 0.5) * speck * (0.25 + Treble * 1.2);
    return colour;
}
