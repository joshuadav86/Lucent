// Name: Folded Metropolis
// Flying down an impossible city folded into a twisting square tunnel, its towers reaching in from all four sides with windows full of flowing colour.

static const float CityHalfWidth = 3.0;   // from the tunnel's axis to each street level
static const float BlockSize = 1.0;

float cityHash(float2 p)
{
    float3 q = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
    q += dot(q, q.yzx + 33.33);
    return frac((q.x + q.y) * q.z);
}

float cityTwist(float z)
{
    return z * 0.11 + sin(z * 0.045) * 1.2;
}

// A tower's footprint half-sizes (xy), height (z, 0 for an open plaza) and crown height (w).
float4 cityTower(float seed)
{
    float2 footprint = float2(0.28, 0.28) + 0.12 * frac(seed * float2(7.13, 3.71));
    float tall = frac(seed * 13.7);
    float height = (0.2 + 1.5 * tall * tall * tall + 0.15 * tall) * step(0.22, frac(seed * 5.3));
    float crown = height + 0.22 * step(0.6, tall) * step(0.01, height);
    return float4(footprint, height, crown);
}

float cityBox(float3 p, float3 size)
{
    float3 q = abs(p) - size;
    return length(max(q, 0.0)) + min(max(q.x, max(q.y, q.z)), 0.0);
}

// Distance to the city. info: x = tower seed, y = which wall, z = 1 on a tower and 0 on the street;
// local returns the point in its block's frame (x across, y up from the street, z along).
float cityDE(float3 pos, out float3 info, out float3 local)
{
    float2 q = rotate(pos.xy, cityTwist(pos.z));
    // Fold onto the nearest of the four walls: across = position along that wall, up = height.
    float2 a = abs(q);
    bool sideways = a.x > a.y;
    float up = CityHalfWidth - max(a.x, a.y);
    float across = sideways ? q.y * sign(q.x) : -q.x * sign(q.y);
    float wallId = sideways ? (q.x > 0.0 ? 0.0 : 2.0) : (q.y > 0.0 ? 1.0 : 3.0);
    info = float3(0.0, wallId, 0.0);
    local = float3(across, up, pos.z);

    // Above the tallest towers only the open channel remains.
    if (up > 2.2)
    {
        return (up - 2.1) * 0.8;
    }

    float2 grid = float2(across, pos.z) / BlockSize;
    float2 cell = floor(grid);
    float2 within = (grid - cell - 0.5) * BlockSize;
    float seed = cityHash(cell + wallId * 31.0);
    float4 tower = cityTower(seed);
    float body = cityBox(float3(within.x, up - tower.z * 0.5, within.y), float3(tower.x, tower.z * 0.5, tower.y));
    float crown = cityBox(float3(within.x, up - tower.w * 0.5, within.y), float3(tower.x * 0.55, tower.w * 0.5, tower.y * 0.55));
    float building = tower.z > 0.0 ? min(body, crown) : 1e3;
    // Don't step past the block's edge: the next tower may be just beyond it.
    float edge = BlockSize * 0.5 - max(abs(within.x), abs(within.y));
    info = float3(seed, wallId, building < up ? 1.0 : 0.0);
    local = float3(within.x, up, within.y);
    return min(min(building, up), max(edge, 0.0) + 0.1) * 0.85;   // neighbouring towers are at least 0.1 beyond the edge
}

float3 Render(float2 uv, float2 p)
{
    float z = Time * 0.8 + (BeatCount - Beat) * 0.2;
    float3 eye = float3(sin(Time * 0.21) * 0.3, cos(Time * 0.17) * 0.3, z);
    float3 dir = cameraRay(eye, float3(sin(Time * 0.21 + 0.5) * 0.2, cos(Time * 0.17 + 0.5) * 0.2, z + 3.0), p, 0.8 - Beat * 0.07, Time * 0.02);

    const float farPlane = 18.0;
    int maxSteps = qualitySteps(72);
    float travelled = 0.0;
    float3 info = 0.0;
    float3 local = 0.0;
    bool landed = false;
    [loop]
    for (int i = 0; i < maxSteps; i++)
    {
        float d = cityDE(eye + dir * travelled, info, local);
        if (d < 0.002 * travelled)
        {
            landed = true;
            break;
        }
        travelled += d;
        if (travelled > farPlane)
        {
            break;
        }
    }

    float flow = Time * 0.05;
    // The far end of the tunnel glows with the light of the whole city.
    float3 distant = palette(flow + 0.2) * 0.12 * (0.6 + Bass * 0.6);
    float3 colour = distant;
    if (landed)
    {
        float3 hit = eye + dir * travelled;
        float3 i2 = 0.0;
        float3 l2 = 0.0;
        float e1 = 0.0005 + 0.001 * travelled;
        float2 e = float2(e1, -e1);
        float3 normal = normalize(e.xyy * cityDE(hit + e.xyy, i2, l2) + e.yyx * cityDE(hit + e.yyx, i2, l2)
                                + e.yxy * cityDE(hit + e.yxy, i2, l2) + e.xxx * cityDE(hit + e.xxx, i2, l2));
        float facing = saturate(dot(normal, -dir));
        float pixel = travelled * 2.5 / (Resolution.y * 0.8);

        float seed = info.x;
        float4 tower = cityTower(seed);
        float band = frac(seed * 3.1);
        float energy = spectrum(band);
        // Colour flows through the city in slow waves, different on each of the four walls.
        float hue = flow + hit.z * 0.025 + local.y * 0.15 + info.y * 0.25;
        float3 tint = hsv2rgb(hue, 0.75, 1.0);

        float3 surface = 0.0;
        if (info.z > 0.5)
        {
            // Which face: a wall facing across, along, or the roof.
            float crownLevel = local.y > tower.z + 0.002 ? 1.0 : 0.0;
            float2 halfSize = tower.xy * lerp(1.0, 0.55, crownLevel);
            float2 faceDist = halfSize - abs(local.xz);
            bool roof = min(faceDist.x, faceDist.y) > 0.004 + pixel * 0.5;
            float sideCoord = faceDist.x < faceDist.y ? local.z : local.x;

            // Windows: a grid of panes, each lit or dark, switching now and then.
            float2 paneGrid = float2(sideCoord / 0.07, local.y / 0.085);
            float2 pane = floor(paneGrid);
            float2 inPane = abs(frac(paneGrid) - 0.5);
            float paneOn = step(0.45, cityHash(pane + seed * 91.0 + floor(Time * 0.2 + seed * 7.0) * 3.0));
            float glass = step(inPane.x, 0.32) * step(inPane.y, 0.3);
            // Far away the panes blur into their average glow.
            float fine = saturate(0.07 / pixel - 1.0);
            float windows = lerp(0.3, glass * paneOn, fine);
            float3 windowColour = hsv2rgb(hue + (cityHash(pane + seed * 13.0) - 0.5) * 0.15, 0.6, 1.0);

            // Neon trim around every roof.
            float trim = saturate(1.0 - min(faceDist.x, faceDist.y) / (pixel * 1.2)) * (roof ? 1.0 : 0.0);
            float topEdge = saturate(1.0 - abs(local.y - (crownLevel > 0.5 ? tower.w : tower.z)) / (pixel * 1.2)) * (roof ? 0.0 : 1.0);
            // A beacon on each roof, blinking to its own band of the spectrum.
            float beacon = glow(length(local.xz), 1.0 / max(pixel, 0.004)) * (roof ? 1.0 : 0.0);

            surface = float3(0.02, 0.02, 0.03) * (0.3 + facing)
                    + (roof ? 0.0 : 1.0) * windowColour * windows * (0.25 + energy * 1.2) * (0.6 + Bass * 0.4)
                    + lerp(tint, 1.0, 0.3) * max(trim, topEdge) * (0.6 + energy * 1.5)
                    + hsv2rgb(hue + 0.5, 0.7, 1.0) * beacon * (0.3 + energy * 2.0);
        }
        else
        {
            // Streets: dark, with light streaming along their centre lines.
            float2 toStreet = 0.5 - abs(local.xz);
            float lane = min(toStreet.x, toStreet.y);
            float traffic = pow(0.5 + 0.5 * sin((toStreet.x < toStreet.y ? local.z : local.x) * 6.0 - Time * 4.0 + seed * 20.0), 8.0);
            surface = float3(0.015, 0.015, 0.02) + tint * saturate(1.0 - lane / (pixel * 1.5)) * (0.15 + traffic * (0.5 + energy));
        }

        // After each beat a wave of light sweeps away down the tunnel.
        float wave = glow(hit.z - z - (1.0 - Beat) * 14.0, 1.2) * Beat;
        colour = surface + tint * wave * 0.4 * info.z;
        colour = lerp(distant, colour, exp(-travelled * 0.09));
    }
    return colour + previous(uv) * 0.08;
}
