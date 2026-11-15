// Name: Fable Fractal Canyon
// Flying low through an endless canyon city folded out of a fractal, drawn as a blueprint in light: every ledge,
// arch and tower is dark but its edges glow, cyan on the rooftops and magenta in the depths, lit band by band by the
// spectrum, with a wall of light sweeping out ahead of you on every beat.
// Family: Fly-through
// Tags: Geometric, Neon, 3D
// Added: 2026-11-15 17:00

// The canyon is a fold-and-invert fractal: space is mirrored into one tile, then folded over, turned inside out
// through a sphere and scaled up, again and again, and the walls are what is left of a slab after all that.
// Only the creases of the surface are lit: where the distance to it bends sharply, there is an edge.
static const float3 FoldA = float3(0.0, 1.0, 0.8);
static const float3 FoldB = float3(0.6, 0.7, 0.4);
static const float Grow = 1.6;
static const float Tile = 4.0;
static const float Height = 4.16;

float2 fcPath(float z)
{
    return float2(sin(z * 0.23) * 0.7 + sin(z * 0.09) * 1.1, Height + sin(z * 0.17) * 0.14);
}

// Distance to the canyon's surface. trap comes back with how close the folding came to the middle, for colour.
float fcMap(float3 at, int folds, out float trap)
{
    float3 p = at;
    p.xz = abs(wrap(p.xz, Tile) - Tile * 0.5);
    float grown = 1.0;
    trap = 1e9;
    [loop]
    for (int i = 0; i < folds; i++)
    {
        p = abs(p) - FoldA;
        float k = Grow / clamp(dot(p, p), 0.25, 1.0);
        p = p * k - FoldB;
        grown *= k;
        p.xz = float2(p.x * 0.8776 - p.z * 0.4794, p.x * 0.4794 + p.z * 0.8776);
        trap = min(trap, dot(p, p));
    }

    float3 box = abs(p) - 1.35;
    float solid = max(box.x, max(box.y, box.z)) / grown;
    // A clear way through for the camera.
    float2 off = at.xy - fcPath(at.z);
    float way = 0.22 - length(off);
    return max(solid * 0.6, way);
}

float3 Render(float2 uv, float2 p)
{
    int folds = fractalDepth(7);
    float travelled = Time * 0.35 + BeatGlide * 0.12;
    float3 eye = float3(fcPath(travelled), travelled);
    float3 target = float3(fcPath(travelled + 1.2), travelled + 1.2);
    target.y -= 0.42;
    float3 ray = cameraRay(eye, target, float2(p.x, -p.y), 1.25 - DropPulse * 0.1, sin(Time * 0.11) * 0.12);

    int steps = qualitySteps(90);
    float t = 0.02;
    float trap = 0.0;
    float near = 1e9;
    bool hit = false;
    float touch = 0.0;
    [loop]
    for (int i = 0; i < steps; i++)
    {
        float d = fcMap(eye + ray * t, folds, trap);
        touch = 0.0012 * surfacePrecision() * (1.0 + t * 1.2);
        if (d < touch)
        {
            hit = true;
            break;
        }

        near = min(near, d / t);
        t += d;
        if (t > 14.0)
        {
            break;
        }
    }

    float beatLight = SongMapped > 0.0 ? 0.35 + Beat * 0.65 : Beat;
    float3 colour = 0.0;
    if (hit)
    {
        float3 at = eye + ray * t;
        float e = touch * 1.6;
        float unused;
        float here = fcMap(at, folds, unused);
        float x0 = fcMap(at - float3(e, 0.0, 0.0), folds, unused);
        float x1 = fcMap(at + float3(e, 0.0, 0.0), folds, unused);
        float y0 = fcMap(at - float3(0.0, e, 0.0), folds, unused);
        float y1 = fcMap(at + float3(0.0, e, 0.0), folds, unused);
        float z0 = fcMap(at - float3(0.0, 0.0, e), folds, unused);
        float z1 = fcMap(at + float3(0.0, 0.0, e), folds, unused);
        float3 normal = normalize(float3(x1 - x0, y1 - y0, z1 - z0) + 1e-9);
        // A flat face's distance changes evenly; a crease's doesn't. That unevenness is the edge.
        float bend = abs(x0 + x1 - 2.0 * here) + abs(y0 + y1 - 2.0 * here) + abs(z0 + z1 - 2.0 * here);
        float edge = smoothstep(0.08, 0.5, bend / e);

        // How far below the rooftops: 0 on the plateau, 1 deep in the canyons.
        float height = saturate((3.85 - at.y) / 2.4);
        float band = spectrum(0.03 + height * 0.7);
        float sweep = t - BeatPhase * 9.0;
        float wall = exp(-sweep * sweep * 0.9) * beatLight;
        float hue = 0.5 + height * 0.4 + sqrt(saturate(trap * 0.25)) * 0.1 + sin(Time * 0.03) * 0.05;
        float3 neonColour = hsv2rgb(frac(hue), 0.85, 1.0);
        float fog = exp(-t * 0.26);

        // Faces stay nearly black, with just enough sheen to give them shape.
        float sheen = pow(saturate(1.0 + dot(normal, ray)), 3.0);
        colour = neonColour * (0.012 + sheen * 0.05) * (0.6 + band);
        colour += neonColour * edge * (0.5 + band * 1.6 + wall * 1.8 + DropPulse * 0.5);
        colour *= fog;
        colour += float3(0.035, 0.012, 0.085) * (1.0 - fog) * (0.6 + Bass * 0.6);
    }
    else
    {
        // Open sky: a deep violet dusk, brighter towards the horizon ahead.
        float horizon = exp(-abs(ray.y) * 5.0);
        colour = float3(0.035, 0.012, 0.085) * (0.6 + Bass * 0.6) + float3(0.5, 0.12, 0.5) * horizon * (0.1 + Mid * 0.2);
    }

    // Light that leaks round the silhouettes of things the view only just misses.
    colour += float3(0.2, 0.5, 1.0) * exp(-near * 260.0) * (hit ? 0.0 : 0.25);
    return colour * (0.9 + Intensity * 0.3);
}
