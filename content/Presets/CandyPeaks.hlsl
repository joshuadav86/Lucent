// Name: Candy Peaks
// A cartoon flight over a fractal land made of sweets: sugar-pink, mint, lemon and sky-blue hills and arches,
// inked with dark outlines, under a spiky yellow sun in a peach sky. The sun's spikes jump with the bass, the
// stripes of colour slide along the hills with the song, and each beat rolls a bright band out ahead.
// Family: Fly-through
// Tags: 3D, Colourful, Geometric
// Added: 2026-11-15 19:49

// The land is a fold-and-invert fractal: space is mirrored into one tile, then folded over, turned inside
// out through a sphere and scaled up, again and again, and the hills are what is left of a ball after all
// that. It is drawn like a cartoon: flat colours in two tones, and a dark line wherever the surface creases
// or one hill stands in front of another.
static const float3 FoldA = float3(0.0, 1.0, 0.8);
static const float3 FoldB = float3(0.6, 0.7, 0.4);
static const float Grow = 1.6;
static const float Tile = 4.0;
static const float Height = 4.16;

float2 cpPath(float z)
{
    return float2(sin(z * 0.23) * 0.7 + sin(z * 0.09) * 1.1, Height + sin(z * 0.17) * 0.14);
}

// Distance to the land. trap comes back with how close the folding came to the middle, for colour. The land
// repeats every Tile, so the camera stays inside one tile and gone is how far it has really flown, which only
// the winding of the path needs to know: that keeps the numbers small however long the flight lasts.
float cpMap(float3 at, float gone, int folds, out float trap)
{
    float3 q = at;
    q.xz = abs(wrap(q.xz, Tile) - Tile * 0.5);
    float grown = 1.0;
    trap = 1e9;
    [loop]
    for (int i = 0; i < folds; i++)
    {
        q = abs(q) - FoldA;
        float k = Grow / clamp(dot(q, q), 0.25, 1.0);
        q = q * k - FoldB;
        grown *= k;
        q.xz = float2(q.x * 0.8776 - q.z * 0.4794, q.x * 0.4794 + q.z * 0.8776);
        trap = min(trap, dot(q, q));
    }

    float solid = (length(q) - 1.55) / grown;
    // A clear way through for the camera.
    float2 off = at.xy - cpPath(at.z + gone);
    float way = 0.24 - length(off);
    return max(solid * 0.6, way);
}

float3 cpSky(float3 ray, float3 sunWay, float beatLight)
{
    float3 sky = lerp(float3(1.4, 1.0, 0.6), float3(1.3, 0.52, 0.85), saturate(ray.y * 1.6 + 0.25));
    sky = lerp(sky, float3(0.5, 0.9, 1.3), saturate(-ray.y * 2.0));
    // The sun: a round face with a ring of spikes that jump with the bass, inked like everything else.
    float3 across = normalize(cross(sunWay, float3(0.0, 1.0, 0.0)));
    float3 upward = cross(across, sunWay);
    float2 s = float2(dot(ray, across), dot(ray, upward));
    float facing = dot(ray, sunWay);
    float r = length(s);
    float angle = atan2(s.y, abs(s.x) < 1e-6 ? 1e-6 : s.x);
    float tooth = abs(frac(angle / (2.0 * PI) * 14.0 + Time * 0.03) - 0.5) * 2.0;
    float spikes = 0.15 + (0.045 + Bass * 0.06 + beatLight * 0.02) * (1.0 - tooth);
    float px = 0.004;
    if (facing > 0.0)
    {
        float body = r - 0.115;
        float crown = r - spikes;
        sky = lerp(sky, float3(0.12, 0.03, 0.14), smoothstep(px, -px, crown - 0.012));
        sky = lerp(sky, float3(1.7, 0.85, 0.2), smoothstep(px, -px, crown));
        sky = lerp(sky, float3(0.12, 0.03, 0.14), smoothstep(px, -px, body - 0.012));
        sky = lerp(sky, float3(1.7, 1.35, 0.3), smoothstep(px, -px, body));
    }

    return sky;
}

float3 Render(float2 uv, float2 p)
{
    int folds = fractalDepth(4);
    float travelled = Time * 0.35 + BeatGlide * 0.12;
    float within = wrap(travelled, Tile);
    float gone = travelled - within;
    float3 eye = float3(cpPath(travelled), within);
    float3 target = float3(cpPath(travelled + 1.2), within + 1.2);
    target.y -= 0.3;
    float3 ray = cameraRay(eye, target, float2(p.x, -p.y), 1.2 - DropPulse * 0.1, sin(Time * 0.11) * 0.1);
    float beatLight = SongMapped > 0.0 ? 0.35 + Beat * 0.65 : Beat;
    float3 sunWay = normalize(float3(sin(Time * 0.05) * 0.35, 0.2, 1.0));
    float3 ink = float3(0.12, 0.03, 0.14);

    int steps = qualitySteps(90);
    float t = 0.02;
    float trap = 0.0;
    bool hit = false;
    float touch = 0.0;
    float last = 1e9;
    float inked = 0.0;
    [loop]
    for (int i = 0; i < steps; i++)
    {
        float d = cpMap(eye + ray * t, gone, folds, trap);
        touch = 0.0012 * surfacePrecision() * (1.0 + t * 1.2);
        if (d < touch)
        {
            hit = true;
            break;
        }

        // The ray came very close to something and is now moving away from it: that is the edge of a hill.
        if (d > last && last < touch * 4.5)
        {
            inked = 1.0;
        }

        last = d;
        t += d;
        if (t > 14.0)
        {
            break;
        }
    }

    float3 sky = cpSky(ray, sunWay, beatLight);
    float3 colour = sky;
    if (hit)
    {
        float3 at = eye + ray * t;
        float e = touch * 1.6;
        float unused;
        float here = cpMap(at, gone, folds, unused);
        float x0 = cpMap(at - float3(e, 0.0, 0.0), gone, folds, unused);
        float x1 = cpMap(at + float3(e, 0.0, 0.0), gone, folds, unused);
        float y0 = cpMap(at - float3(0.0, e, 0.0), gone, folds, unused);
        float y1 = cpMap(at + float3(0.0, e, 0.0), gone, folds, unused);
        float z0 = cpMap(at - float3(0.0, 0.0, e), gone, folds, unused);
        float z1 = cpMap(at + float3(0.0, 0.0, e), gone, folds, unused);
        float3 normal = normalize(float3(x1 - x0, y1 - y0, z1 - z0) + 1e-9);
        // A smooth slope's distance changes evenly; a crease's doesn't. That unevenness is where the ink goes.
        float bend = abs(x0 + x1 - 2.0 * here) + abs(y0 + y1 - 2.0 * here) + abs(z0 + z1 - 2.0 * here);
        float crease = smoothstep(0.25, 0.7, bend / e) * saturate(1.35 - t * 0.13);

        // Four sweet colours, in layers like a cake, that follow the folding and slide along with the song.
        float stripe = frac(at.y * 0.9 + sqrt(trap) * 0.8 + BeatGlide * 0.06 + Time * 0.02) * 4.0;
        float3 sweet = float3(1.5, 0.42, 0.75);
        sweet = lerp(sweet, float3(0.4, 1.3, 0.8), smoothstep(0.96, 1.04, stripe));
        sweet = lerp(sweet, float3(1.5, 1.15, 0.28), smoothstep(1.96, 2.04, stripe));
        sweet = lerp(sweet, float3(0.35, 0.8, 1.6), smoothstep(2.96, 3.04, stripe));
        sweet = lerp(sweet, float3(1.5, 0.42, 0.75), smoothstep(3.96, 4.0, stripe));
        float band = spectrum(0.03 + frac(stripe * 0.25) * 0.7);
        // Two flat tones, like a cel.
        float lit = dot(normal, sunWay);
        float tone = lerp(0.6, 1.0, smoothstep(0.02, 0.1, lit));
        tone = lerp(tone, 1.12, smoothstep(0.82, 0.88, lit));
        float sweep = t - BeatPhase * 9.0;
        float rolling = exp(-sweep * sweep * 1.4) * beatLight;
        colour = sweet * tone * (0.9 + band * 0.25 + rolling * 0.3);
        colour = lerp(colour, ink, crease * 0.9);
        float haze = exp(-t * 0.08);
        colour = lerp(float3(1.35, 0.85, 0.7), colour, haze);
    }

    colour = lerp(colour, ink, inked * exp(-t * 0.1) * 0.9);
    return colour * (0.9 + Intensity * 0.16);
}
