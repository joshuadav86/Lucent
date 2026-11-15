// Name: Fable Honeycomb
// Flying through a honeycomb of cubes in curved space, where five cubes meet round every edge instead of four and the
// rooms multiply without end the further you look: neon edges with gold corners, light running along every edge on
// the beat, the near rooms answering the bass and the far ones the treble.
// Family: Fly-through
// Tags: Geometric, Neon, 3D
// Added: 2026-11-15 17:00

// The order-5 cubic honeycomb {4,3,5} of hyperbolic space, seen from inside. All of hyperbolic space fits in
// a ball (the Poincare ball); the cube round its centre has faces that are parts of spheres meeting the ball's
// rim at right angles. Mirroring a point across the cube's own planes of symmetry and across one face, over and
// over, brings any point of space back into one small wedge of the central cube, where the single piece of
// edge and the single corner it holds stand for every edge and corner there is.
static const float FaceCentre = 2.0581710;     // the face sphere sits this far out along x
static const float FaceRadius2 = 3.2360680;    // its radius squared (1 + sqrt 5)
static const float EdgeRadius = 1.0573713;     // two faces meet in a circle of this radius round (d/2, d/2, 0)
static const float Corner = 0.3154617;         // the cube's corner is (c, c, c)
static const float HalfRoom = 0.5306375;       // from a cube's centre to its face, measured in the curved space
static const float LineWidth = 0.011;
static const float Reach = 6.2;                // how far it looks

// Slides the whole space so its centre moves to a (a move that keeps every distance of the curved space).
float3 fhSlide(float3 x, float3 a)
{
    float xa = dot(x, a);
    float xx = dot(x, x);
    float aa = dot(a, a);
    return ((1.0 + 2.0 * xa + xx) * a + (1.0 - aa) * x) / (1.0 + 2.0 * xa + aa * xx);
}

float3 fhSort(float3 p)
{
    p = abs(p);
    if (p.x < p.y)
    {
        p.xy = p.yx;
    }
    if (p.x < p.z)
    {
        p.xz = p.zx;
    }
    if (p.y < p.z)
    {
        p.yz = p.zy;
    }
    return p;
}

// Folds a point of the ball into the central cube's wedge.
float3 fhFold(float3 p, int folds)
{
    p = fhSort(p);
    [loop]
    for (int i = 0; i < folds; i++)
    {
        float3 v = p - float3(FaceCentre, 0.0, 0.0);
        float d2 = dot(v, v);
        if (d2 >= FaceRadius2)
        {
            break;
        }

        p = fhSort(float3(FaceCentre, 0.0, 0.0) + v * (FaceRadius2 / d2));
    }

    return p;
}

float3 Render(float2 uv, float2 p)
{
    // The camera rides down the line through the middles of the rooms' faces, looking around as it goes.
    // Moving one whole room along that line puts the honeycomb back exactly where it was, so the ride only ever
    // needs the camera's place within one room and can go on for ever.
    float travelled = Time * 0.1 + BeatGlide * 0.045;
    float within = wrap(travelled + HalfRoom, 2.0 * HalfRoom) - HalfRoom;
    float3 along = float3(tanh(within * 0.5), 0.0, 0.0);
    float3 aside = float3(0.0, sin(Time * 0.083), cos(Time * 0.067)) * 0.09;

    float3 ray = normalize(float3(1.15 - DropPulse * 0.1, p.x, -p.y));
    ray.yz = rotate(ray.yz, Time * 0.03 + BeatGlide * 0.012);
    ray.xy = rotate(ray.xy, sin(Time * 0.061) * 0.5);
    ray.xz = rotate(ray.xz, sin(Time * 0.047 + 1.3) * 0.32);

    int folds = fractalDepth(9);
    int steps = qualitySteps(84);
    float pixel = pixelSize() / 1.15;
    float t = 0.04 + hash(uv * Resolution) * 0.08;
    float3 colour = 0.0;
    float beatLight = SongMapped > 0.0 ? 0.35 + Beat * 0.65 : Beat;

    [loop]
    for (int i = 0; i < steps; i++)
    {
        // A point t along the ray (rays from the ball's centre are straight), carried to where the camera is.
        float3 q = fhFold(fhSlide(fhSlide(ray * tanh(t * 0.5), aside), along), folds);
        float stretch = 2.0 / max(1.0 - dot(q, q), 0.05);

        // How far it is from the wedge's piece of edge, and from its corner.
        float u = (q.x + q.y - FaceCentre) * 0.70710678;
        float v = (q.x - q.y) * 0.70710678;
        float edge = length(float2(length(float2(u, q.z)) - EdgeRadius, v)) * stretch;
        float corner = length(q - Corner) * stretch;

        // Far lines are kept at least a pixel wide and dimmed to match, so they stay fine instead of sparkling.
        float width = LineWidth + sinh(t) * pixel * 0.9;
        float thin = LineWidth / width;
        float stride = clamp(edge * 0.55, width * 0.5, 0.4);

        float toCorner = saturate(q.z / Corner);   // along the edge: 0 at its middle, 1 at the corner
        float depth = t / Reach;
        float band = spectrum(0.03 + depth * 0.7);
        float run = toCorner - BeatPhase;
        float pulse = exp(-run * run * 14.0) * beatLight;
        float ripple = t - BeatPhase * 5.0;
        float shell = exp(-ripple * ripple * 1.6) * beatLight;

        float lineLight = (exp(-edge * edge / (width * width)) + exp(-edge / (width * 5.0)) * 0.02) * thin * thin;
        float3 lineColour = hsv2rgb(frac(0.47 + depth * 0.5 + toCorner * 0.1 + sin(Time * 0.05) * 0.06), 0.86, 1.0);
        float cornerWidth = width * 2.2;
        float cornerLight = exp(-corner * corner / (cornerWidth * cornerWidth)) * thin * thin;

        float fog = exp(-t * 0.46);
        float3 light = lineColour * lineLight * (0.5 + band * 1.3 + pulse * 1.8 + shell * 0.8);
        light += float3(1.0, 0.7, 0.25) * cornerLight * (0.35 + Treble * 1.6 + shell * 0.6);
        colour += light * fog * stride;

        t += stride;
        if (t > Reach)
        {
            break;
        }
    }

    colour *= 62.0 * (0.85 + Intensity * 0.3 + DropPulse * 0.3);
    // A breath of violet in the far distance, lifting with the bass.
    colour += float3(0.16, 0.05, 0.3) * (0.05 + Bass * 0.05) * exp(-dot(p, p) * 0.8);
    return colour;
}
