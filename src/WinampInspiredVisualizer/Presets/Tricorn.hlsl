// Name: Tricorn
// The Mandelbrot set's three-horned cousin turns slowly in the dark, its edges fringed with feathered combs and glowing lace. The camera drifts down one of its long spines into a tiny three-cornered copy of the whole, then rises out again, colours flowing outward with the music.
// Family: Fractal
// Tags: Calm, Colourful

// The Tricorn (or Mandelbar) follows z -> conj(z)^2 + c: the same rule as the Mandelbrot set
// but with z mirrored before squaring each time. The mirror turns its two-fold symmetry into a
// three-fold one, and fills its edges with combs and wavy fringes instead of spirals of bulbs.

static const float Journey = 64.0;    // seconds for one dive down and back up

// The places we dive towards. Because the Tricorn looks the same after a third of a turn, each
// visit approaches a copy from a different direction.
void diveTarget(float index, out float2 target, out float depth, out float turn)
{
    float k = wrap(index, 3.0);
    turn = k * 2.0 * PI / 3.0;
    if (k < 0.5)
    {
        target = float2(-1.77212, 0.0);       // the tiny Tricorn at the tip of a spine
        depth = 8.0;
    }
    else if (k < 1.5)
    {
        target = float2(-1.0, -0.1);           // little bulbs along the edge of a horn
        depth = 4.5;
    }
    else
    {
        target = float2(-1.77212, 0.0);       // the spine again, stopping where its branches show
        depth = 5.5;
    }
    // Rotate the target onto the copy we're visiting this time.
    target = rotate(target, turn);
}

// The camera: start from the whole shape, sink towards the target (halving the view over and
// over), linger, then rise back out, turning slowly all the while.
void cameraAt(float t, out float2 centre, out float scale, out float spin)
{
    float stage = t / Journey;
    float index = floor(stage);
    float local = frac(stage);
    float2 target;
    float depth;
    float turn;
    diveTarget(index, target, depth, turn);

    // 0 -> 1 -> 0 over the journey, easing in and out and lingering at the deepest point.
    float dive = smoothstep(0.05, 0.5, local) * (1.0 - smoothstep(0.62, 0.98, local));
    float overviewScale = 1.35;
    scale = overviewScale * exp2(-depth * dive);
    // Keep the target at the same place on screen as we zoom (so it doesn't swing away).
    float2 overview = float2(-0.3, 0.0);
    centre = target + (overview - target) * (scale - exp2(-depth) * overviewScale) / (overviewScale - exp2(-depth) * overviewScale);
    spin = t * 0.025;
}

// A deep night palette cycling through violet, teal, pale gold and rose.
float3 nightColour(float t)
{
    float3 violet = float3(0.32, 0.14, 0.9);
    float3 teal = float3(0.08, 0.72, 0.78);
    float3 gold = float3(1.0, 0.74, 0.32);
    float3 rose = float3(0.92, 0.25, 0.5);
    float u = frac(t) * 4.0;
    float blend = smoothstep(0.0, 1.0, frac(u));
    if (u < 1.0) return lerp(violet, teal, blend);
    if (u < 2.0) return lerp(teal, gold, blend);
    if (u < 3.0) return lerp(gold, rose, blend);
    return lerp(rose, violet, blend);
}

float3 shade(float2 c, float scale)
{
    float2 z = 0.0;
    float dr = 0.0;
    float n = 0.0;
    bool escaped = false;
    int limit = qualitySteps(420);
    [loop]
    for (int i = 0; i < limit; i++)
    {
        dr = 2.0 * length(z) * dr + 1.0;
        z = float2(z.x * z.x - z.y * z.y, -2.0 * z.x * z.y) + c;
        if (dot(z, z) > 1.0e4)
        {
            escaped = true;
            break;
        }
        n += 1.0;
    }

    if (!escaped)
    {
        // Inside: a still, dark ink.
        return float3(0.004, 0.006, 0.014);
    }

    float r = length(z);
    // Smooth iteration count: no bands between one count and the next.
    float smoothN = n + 1.0 - log2(log2(r));
    // Distance to the set, in screen units: thin bright lace hugs the edge.
    float distance = r * log(r) / max(dr, 1e-20) / scale;

    // Colour flows outward from the set with time, and each beat nudges it on.
    float flow = Time * 0.03 + (BeatCount - Beat) * 0.02 + Beat * 0.03;
    float t = log2(smoothN + 1.0) * 0.55 - flow;
    float3 colour = nightColour(t);

    float lace = exp(-distance * 160.0);
    float halo = exp(-distance * 28.0);
    float bands = 0.5 + 0.5 * cos(smoothN * 0.35 - Time * 0.4);
    float level = spectrum(saturate(smoothN / 120.0));

    float3 result = colour * (0.03 + 0.5 * halo * (0.55 + 0.45 * bands) * (0.7 + level * 0.8));
    result += lerp(colour, 1.0, 0.3) * lace * (0.75 + Treble * 0.7 + Beat * 0.3);
    return result * (0.85 + Bass * 0.3);
}

float3 Render(float2 uv, float2 p)
{
    float2 centre;
    float scale;
    float spin;
    cameraAt(Time, centre, scale, spin);

    // A different sub-pixel offset each frame, blended with the last frame lined up with the
    // moving camera, smooths the finest lace without blurring it.
    float2 jitter = float2(frac(FrameCount * 0.618034), frac(FrameCount * 0.754878)) - 0.5;
    float2 c = centre + rotate(p + jitter * pixelSize(), spin) * scale;
    float3 colour = shade(c, scale);

    float2 lastCentre;
    float lastScale;
    float lastSpin;
    cameraAt(Time - DeltaTime, lastCentre, lastScale, lastSpin);
    float2 before = rotate((centre + rotate(p, spin) * scale - lastCentre) / lastScale, -lastSpin);
    float2 beforeUv = toUv(before);
    float keep = all(abs(beforeUv - 0.5) < 0.497) ? 0.6 : 0.0;
    return lerp(colour, previous(beforeUv), keep);
}
