// Name: Kintsugi
// Broken pottery mended with gold: shards of dark glazed ceramic, each tilted a little differently under a moving lamp,
// joined by raised seams of gold that swell with the bass. Each seam listens to its own band of the spectrum, and
// every beat sends a glint running out along the gold from the middle.
// Family: Organic
// Tags: Calm, Dark
// Added: 2026-11-15 17:45

// The shards are the cells of a scatter of points (each shard is everything nearest to its own point), cut on
// a plane that has been bent a little so no break is quite straight. The seams are drawn from the true
// distance to the nearest boundary between two shards, and shaded as a rounded bead of metal.
static const float Shards = 2.7;               // shards across the height of the picture
static const float Seam = 0.02;                // half the width of the gold, in shard units

float2 fkSite(float2 cell)
{
    float2 random = float2(hash(cell * 0.713 + 3.1), hash(cell * 0.427 + 11.7));
    return 0.5 + 0.36 * sin(Time * 0.045 + random * 2.0 * PI);
}

// How loud the music is at x (0 deep bass, 1 high treble), stretched so that ordinary music swings it most of
// the way between 0 and 1 instead of sitting near the top.
float kgLoud(float x)
{
    return saturate((spectrum(x) - 0.56 + 0.5 * x) / 0.4);
}

float3 Render(float2 uv, float2 p)
{
    float2 flat = rotate(p, 0.2 + Time * 0.006) * Shards * 0.5 + float2(Time * 0.01, 3.7);
    float2 q = flat + (float2(fbm(flat * 1.6 + 3.0), fbm(flat * 1.6 + float2(9.1, 4.3))) - 0.47) * 0.55;
    float2 cell = floor(q);
    float2 within = q - cell;

    // Which shard this is.
    float2 nearestCell = 0.0;
    float2 toNearest = 0.0;
    float best = 1e9;
    for (int j = -1; j <= 1; j++)
    {
        for (int i = -1; i <= 1; i++)
        {
            float2 g = float2(i, j);
            float2 r = g + fkSite(cell + g) - within;
            float d = dot(r, r);
            if (d < best)
            {
                best = d;
                toNearest = r;
                nearestCell = g;
            }
        }
    }

    // How far it is from the edge of the shard, which shard is across that edge, and which way that is.
    float toSeam = 1e9;
    float2 across = 0.0;
    float2 acrossWay = float2(1.0, 0.0);
    for (int j2 = -2; j2 <= 2; j2++)
    {
        for (int i2 = -2; i2 <= 2; i2++)
        {
            float2 g = nearestCell + float2(i2, j2);
            float2 r = g + fkSite(cell + g) - within;
            float2 between = r - toNearest;
            float apart = dot(between, between);
            if (apart > 1e-5)
            {
                float2 way = between * rsqrt(apart);
                float d = dot(0.5 * (toNearest + r), way);
                if (d < toSeam)
                {
                    toSeam = d;
                    across = g;
                    acrossWay = way;
                }
            }
        }
    }

    float2 shard = cell + nearestCell;
    float2 other = cell + across;
    float own = hash(shard * 0.173 + 1.7);

    // The glaze: deep blue-black, some shards leaning teal, and now and then a pale one from another bowl.
    float mottle = fbm(flat * 5.0 + own * 31.0);
    float3 glaze = lerp(float3(0.012, 0.03, 0.1), float3(0.008, 0.085, 0.09), smoothstep(0.3, 0.8, own));
    glaze *= 0.55 + 1.0 * mottle;
    float pale = step(0.9, own);
    glaze = lerp(glaze, float3(0.36, 0.38, 0.33) * (0.7 + 0.5 * mottle), pale);
    // A fine crackle in the glaze.
    float crackle = abs(noise(flat * 26.0 + own * 17.0) - 0.5);
    glaze *= 0.78 + 0.22 * smoothstep(0.0, 0.035, crackle);

    // Each shard sits at its own slight tilt, so the lamp's reflection lands differently on each.
    float2 tilt = (float2(hash(shard * 0.311 + 5.3), hash(shard * 0.519 + 8.9)) - 0.5) * 1.2;
    float2 lamp = float2(sin(Time * 0.11) * Aspect * 0.55, cos(Time * 0.083) * 0.5);
    float2 seen = p - lamp + tilt;
    float gloss = exp(-dot(seen, seen) * 2.4);
    float band = kgLoud(0.07 + own * 0.62);
    float3 colour = glaze * (0.6 + band * 0.85) + float3(0.5, 0.68, 0.9) * gloss * (0.1 + 0.16 * mottle);

    // The gold: a raised bead, wider in some places than others, swelling with the bass.
    float width = Seam * (0.8 + 0.8 * noise(flat * 2.7 + 7.0)) * (0.8 + 0.4 * (kgLoud(0.08) + kgLoud(0.14)) + DropPulse * 0.5);
    float pixel = pixelSize() * Shards * 0.5;
    float gold = smoothstep(width + pixel, width - pixel, toSeam);
    // The shard's edge is chipped a shade darker just before the gold.
    colour *= 0.45 + 0.55 * smoothstep(width, width * 3.0, toSeam);

    // The bead's own shape: its top faces the eye, its flanks lean away towards each shard.
    float off = saturate(toSeam / max(width, 1e-4));
    float3 normal = normalize(float3(-acrossWay * off, sqrt(max(1.0 - off * off, 0.0)) + 0.25));
    float3 light = normalize(float3(-0.5, -0.65, 0.6));
    float lit = saturate(dot(normal, light));
    float spark = pow(saturate(dot(normal, normalize(light + float3(0.0, 0.0, 1.0)))), 18.0);
    float grain = fbm(flat * 30.0);

    // A seam belongs to the two shards it joins, and listens to a band of its own.
    float seamBand = kgLoud(0.07 + hash((shard + other) * 0.237 + shard * other * 0.011 + 2.3) * 0.62);
    float out0 = length(p);
    float run = out0 - BeatPhase * 2.0;
    float glint = exp(-run * run * 9.0) * (SongMapped > 0.0 ? 0.35 + Beat * 0.65 : Beat);
    float lampOnGold = exp(-dot(p - lamp, p - lamp) * 1.1);
    float bright = 0.45 + 0.22 * (kgLoud(0.08) + kgLoud(0.14)) + seamBand * 1.2 + glint * 1.0 + lampOnGold * 0.45 + DropPulse * 0.5;
    float3 metal = float3(0.95, 0.56, 0.1) * (0.12 + 0.7 * lit) * (0.7 + 0.5 * grain);
    metal += float3(1.0, 0.9, 0.58) * spark * (0.5 + 0.7 * grain);
    colour = lerp(colour, metal * bright, gold);

    // Flecks of gold dust caught in the glaze beside the seams.
    float fleck = step(0.988, hash(floor(flat * 85.0) * 0.37 + 4.4)) * smoothstep(width * 4.5, width * 1.2, toSeam);
    colour += float3(1.0, 0.75, 0.3) * fleck * (0.2 + 1.2 * kgLoud(0.66)) * (1.0 - gold);

    return colour * (0.9 + Intensity * 0.25) * (1.0 - dot(uv - 0.5, uv - 0.5) * 0.9);
}
