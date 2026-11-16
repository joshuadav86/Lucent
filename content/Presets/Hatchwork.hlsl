// Name: Hatchwork
// A sheet hatched edge to edge in crisp black and white stripes, cut into patches whose stripes each run their
// own way, so the joins between them read like a maze. The patches drift and reshape slowly while every stripe
// creeps sideways. The music brings the colour: patches flood from white into red, saffron, or blue as their
// part of the music sounds, and each beat sends a ring of gold light out across the sheet.
// Family: Minimal
// Tags: Geometric, Minimal, Retro
// Added: 2026-11-16 04:44

float hwLoud(float x)
{
    return saturate((spectrum(x) - 0.42 + 0.5 * x) / 0.7);
}

float3 Render(float2 uv, float2 p)
{
    float since = frac(BeatGlide);
    float flash = smoothstep(0.0, 0.1, since) * (1.0 - since);
    float e = pixelSize();

    // Two slow fields of hills. Each cuts the sheet into bands by height, and a patch is one band of each.
    float2 q = rotate(p, 0.15 * sin(Time * 0.05)) * 0.55;
    float hillsA = fbm(q + float2(Time * 0.021, -Time * 0.013));
    float hillsB = fbm(q * 1.35 + float2(31.7 - Time * 0.017, 12.3 + Time * 0.011));
    float2 patch = float2(floor(hillsA * 9.0), floor(hillsB * 6.0));
    float h1 = hash(patch * 1.7 + 4.1);
    float h2 = hash(patch * 2.9 + 23.5);

    // The patch's stripes: one of eight directions, creeping one way or the other.
    float angle = floor(h1 * 8.0) * PI / 8.0;
    float2 across = float2(cos(angle), sin(angle));
    float pitch = 18.0;
    float place = dot(p, across) * pitch + Time * 0.12 * (h2 > 0.5 ? 1.0 : -1.0);
    float fromEdge = (abs(frac(place) - 0.5) - 0.25) / pitch;
    float ink = saturate(0.5 + fromEdge / e);

    // The patch's own colour floods its white stripes as its part of the music sounds.
    float band = (patch.x * 3.0 + patch.y * 5.0) / 16.0;
    float loud = hwLoud(0.08 + 0.6 * frac(band));
    float pick = hash(patch * 4.3 + 61.7);
    float3 dye = pick < 0.34 ? float3(1.35, 0.3, 0.2) : (pick < 0.67 ? float3(1.3, 0.95, 0.12) : float3(0.15, 0.45, 1.4));
    float3 paper = lerp(float3(0.95, 0.94, 0.9), dye, 0.85 * smoothstep(0.2, 0.95, loud) * step(0.35, h2));
    float3 dark = float3(0.02, 0.02, 0.03);

    // Each beat sends a ring of gold light out across the white of the sheet.
    float ring = length(p) - since * 2.2 * max(Aspect, 1.0);
    paper = lerp(paper, float3(1.5, 1.0, 0.25), 0.75 * flash * exp(-ring * ring * 60.0));
    return lerp(paper, dark, ink) * (1.0 - 0.12 * dot(p, p) / (1.0 + Aspect * Aspect));
}
