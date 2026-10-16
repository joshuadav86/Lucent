// Name: Spectrum Galaxy
// A spiral galaxy swirls in the dark, lit ring by ring by the spectrum: bass glows in its heart, treble sparkles at its rim, and every beat sends a wave of starlight outward.

float galaxyHash(float2 cell)
{
    float3 p3 = frac(float3(cell.xyx) * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

// How bright the spectrum makes the galaxy at radius r: bass in the middle, treble at the rim.
float galaxyBand(float r)
{
    float x = 0.03 + 0.85 * saturate(r / 1.35);
    float s = spectrum(x) * 0.5 + (spectrum(x - 0.02) + spectrum(x + 0.02)) * 0.25;
    return s * s;
}

// A hand-picked loop of colours: magenta, violet, sky blue, gold and coral.
float3 galaxyPalette(float t)
{
    float k = frac(t) * 5.0;
    float i = floor(k);
    float f = smoothstep(0.0, 1.0, k - i);
    float3 c0 = i < 0.5 ? float3(1.0, 0.25, 0.75) : i < 1.5 ? float3(0.55, 0.25, 1.0) : i < 2.5 ? float3(0.2, 0.7, 1.0)
              : i < 3.5 ? float3(1.0, 0.72, 0.3) : float3(1.0, 0.35, 0.4);
    float3 c1 = i < 0.5 ? float3(0.55, 0.25, 1.0) : i < 1.5 ? float3(0.2, 0.7, 1.0) : i < 2.5 ? float3(1.0, 0.72, 0.3)
              : i < 3.5 ? float3(1.0, 0.35, 0.4) : float3(1.0, 0.25, 0.75);
    return lerp(c0, c1, f);
}

// Angular speed of the stars at radius r: the inside turns faster, like a real galaxy.
float galaxySpin(float r)
{
    return 0.32 / (0.25 + r);
}

// Stars in rings: every ring turns at its own speed, so the field never shears apart.
float3 galaxyStars(float r, float angle, float cellSize, float seed, float armLight, float px)
{
    float ring = floor(r / cellSize);
    float ringR = (ring + 0.5) * cellSize;
    float cells = max(floor(2.0 * PI * ringR / cellSize), 1.0);
    float turned = angle - galaxySpin(ringR) * Time;
    float a = turned / (2.0 * PI) * cells;
    float cell = floor(a);
    cell = cell - floor(cell / cells) * cells;
    float2 id = float2(ring, cell) + seed;
    float h = galaxyHash(id);
    float h2 = galaxyHash(id + 17.0);
    float h3 = galaxyHash(id + 41.0);
    // Star position inside its cell (kept away from the edges, so it is never cut).
    float starR = (ring + 0.25 + 0.5 * h2) * cellSize;
    float starA = (floor(a) + 0.25 + 0.5 * h3) / cells * 2.0 * PI;
    float da = (turned - starA);
    da = da - floor(da / (2.0 * PI) + 0.5) * 2.0 * PI;
    float2 offset = float2(r - starR, da * r);
    float d = length(offset);
    // Stars crowd into the arms.
    float present = step(1.0 - (0.15 + 0.85 * armLight), h);
    float size = cellSize * 0.05 + px * 0.55;
    float star = present * exp(-d * d / (size * size)) * (0.3 + 0.7 * h2 * h3);
    return float3(star, h, h3);
}

float3 Render(float2 uv, float2 p)
{
    float px = pixelSize();

    // The galaxy is tipped away from us; its tilt and heading drift slowly.
    float tilt = 0.95 + 0.2 * sin(Time * 0.05);
    float heading = Time * 0.02;
    float2 g = rotate(p, heading);
    g.y /= cos(tilt);
    g *= 1.05;
    float r = length(g) + 1e-4;
    float angle = atan2(g.y, g.x);
    float pxPlane = px / cos(tilt);

    // Two spiral arms: a density wave turning slower than the stars passing through it.
    float2 drift = rotate(g, -Time * 0.06);
    float cloud = fbm(drift * 3.5 + 3.0);
    // Each beat twists the arms a little tighter; they relax as it fades.
    float pattern = angle - Time * 0.06 + log(r) * (2.6 + Beat * 0.35);
    float arms = 0.5 + 0.5 * cos(2.0 * pattern + (cloud - 0.5) * 2.5);
    float armLight = arms * arms * arms;

    float band = galaxyBand(r);
    // A wave of starlight rushes out from the core on every beat.
    float sinceBeat = -log(max(Beat, 0.002)) / 5.0;
    float front = r - sinceBeat * 2.4;
    float wave = exp(-front * front * 30.0) * pow(Beat, 0.35) * step(0.003, Beat);
    float energy = 0.2 + 3.0 * band + wave * 2.5;

    // Colour runs from a warm heart out through the spectrum to the rim, and drifts with time.
    float3 hue = galaxyPalette(r * 0.45 - Time * 0.025);
    hue = hue * hue;

    // The beat's wave lights up the space between the arms too.
    float3 waveLight = hue * wave * 0.12 * exp(-r * 0.8);

    // Unresolved starlight: a soft disc, brightest along the arms, mottled with clouds.
    float disc = exp(-r * 1.8);
    float gas = (armLight * (0.35 + 1.1 * cloud * cloud) + 0.12) * disc;
    float3 colour = hue * gas * 1.6 * energy;
    // Knots of hot young stars strung along the arms.
    float knots = smoothstep(0.62, 0.8, noise(drift * 11.0 + 5.0)) * armLight * smoothstep(1.4, 0.3, r);
    colour += lerp(galaxyPalette(r * 0.45 - Time * 0.025 + 0.4), 1.0, 0.5) * knots * 0.6 * energy;
    // Dark dust lanes on the inner edge of each arm.
    float dust = 0.5 + 0.5 * cos(2.0 * pattern - 0.9 + (cloud - 0.5) * 3.0);
    colour *= 1.0 - 0.75 * pow(dust, 6.0) * smoothstep(0.1, 0.35, r);

    // Stars: a fine dusting and a scattering of bright ones.
    float3 fine = galaxyStars(r, angle, 0.014, 0.0, armLight, pxPlane);
    float3 bright = galaxyStars(r, angle, 0.05, 50.0, armLight * 0.3 - 0.1, pxPlane);
    float twinkle = 0.6 + 0.4 * sin(Time * 3.0 + fine.z * 40.0);
    colour += lerp(hue, 1.0, 0.6) * fine.x * fine.y * 0.35 * twinkle * energy * smoothstep(1.6, 0.4, r);
    colour += lerp(galaxyPalette(bright.z), 1.0, 0.5) * bright.x * 1.5 * energy * smoothstep(1.8, 0.4, r);

    // The bright core, swelling with the bass.
    float core = exp(-r * r * 45.0) * (0.6 + Bass * 0.8 + Beat * 0.4) + exp(-r * 6.0) * 0.3;
    colour += float3(1.0, 0.8, 0.6) * core;

    colour += waveLight;

    // Long-exposure trails: the last frame, turned with the stars and fading.
    float2 back = rotate(g / 1.05, -galaxySpin(r) * DeltaTime);
    back.y *= cos(tilt);
    float3 trail = previous(toUv(rotate(back, -heading)));
    colour = max(colour, trail * exp(-DeltaTime * 3.0));
    return colour;
}
