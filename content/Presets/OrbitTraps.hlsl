// Name: Orbit Traps
// The Mandelbrot set turned into stained glass: jewel-coloured panes of amber, sapphire and ruby bloom along its spirals and inside its bulbs, while the camera glides between seahorse valleys and lightning-bolt filaments. The glass is cut by shapes that move with the music, so every beat re-leads the window.
// Family: Fractal
// Tags: Colourful
// Added: 2026-10-17 22:02

// Every point c is followed along its path z -> z^2 + c. Instead of only asking whether the path
// escapes, we watch how close it comes to three "traps": a wandering point, a turning line and a
// circle that breathes with the bass. Each trap has a colour, and a point glows with the colour
// of whichever traps its path brushed past closest. Because the paths repeat the set's own
// structure, the traps come back as countless tiny echoes, like panes in a cathedral window.

static const int Places = 5;
static const float Dwell = 26.0;    // seconds at each place, including the glide to the next

void placeAt(int index, out float2 centre, out float scale, out float spin)
{
    int k = index - Places * (index / Places);
    centre = float2(-0.7436, 0.1318); scale = 0.006; spin = 0.4;      // seahorse valley
    if (k == 1) { centre = float2(-0.1592, 1.0335); scale = 0.03; spin = -0.6; }   // the top antenna's spirals
    if (k == 2) { centre = float2(-1.7549, 0.0); scale = 0.025; spin = 1.2; }     // a tiny copy of the set on its long spike
    if (k == 3) { centre = float2(-0.75, 0.0); scale = 1.25; spin = 1.5708; }     // the whole set, standing upright
    if (k == 4) { centre = float2(-1.25, 0.13); scale = 0.04; spin = 0.0; }        // lace round the period-two bulb
}

// The camera: hold on a place, then glide to the next, rising out a little mid-way so the journey reads.
void cameraAt(float t, out float2 centre, out float scale, out float spin)
{
    float stage = t / Dwell;
    int index = (int)floor(stage);
    float local = frac(stage);
    float2 c0; float s0; float r0;
    float2 c1; float s1; float r1;
    placeAt(index, c0, s0, r0);
    placeAt(index + 1, c1, s1, r1);
    float glide = smoothstep(0.62, 1.0, local);
    float travel = length(c1 - c0);
    float rise = log(max(travel * 1.4 / sqrt(s0 * s1), 1.0)) * 0.6;
    float logScale = lerp(log(s0), log(s1), glide) + rise * sin(PI * glide);
    // While holding, breathe in slowly.
    logScale -= 0.25 * sin(PI * saturate(local / 0.62));
    centre = lerp(c0, c1, smoothstep(0.0, 1.0, glide));
    scale = exp(logScale);
    spin = lerp(r0, r1, glide) + sin(t * 0.05) * 0.15;
}

float3 trapColour(int trap, float hueShift)
{
    float3 amber = float3(1.0, 0.62, 0.15);
    float3 sapphire = float3(0.12, 0.38, 1.0);
    float3 ruby = float3(0.95, 0.1, 0.3);
    float3 base = trap == 0 ? amber : (trap == 1 ? sapphire : ruby);
    // Rotate the jewel tone slightly with how deep in the path the closest pass came.
    float3 shifted = float3(base.y, base.z, base.x);
    return lerp(base, shifted, 0.5 - 0.5 * cos(hueShift * 2.0 * PI)) * 0.85 + base * 0.15;
}

float3 shade(float2 c, float scale)
{
    // The traps, moving with the music.
    float pointTurn = Time * 0.21 + (BeatCount - Beat) * 0.35;
    float2 trapPoint = float2(cos(pointTurn), sin(pointTurn)) * (0.32 + 0.06 * sin(Time * 0.37));
    float lineTurn = Time * 0.09 + Mid * 0.6;
    float2 lineNormal = float2(cos(lineTurn), sin(lineTurn));
    float lineOffset = 0.15 * sin(Time * 0.13);
    float ring = 0.62 + Bass * 0.22 + Beat * 0.06;

    float3 best = 1e3;          // closest pass to each trap
    float3 when = 0.0;          // and on which hop it happened
    float2 z = 0.0;
    float2 dz = 0.0;            // how fast z changes with c, for the distance to the set
    float n = 0.0;
    bool escaped = false;
    int limit = qualitySteps(200);
    [loop]
    for (int i = 0; i < limit; i++)
    {
        dz = 2.0 * float2(z.x * dz.x - z.y * dz.y, z.x * dz.y + z.y * dz.x) + float2(1.0, 0.0);
        z = float2(z.x * z.x - z.y * z.y, 2.0 * z.x * z.y) + c;
        float r2 = dot(z, z);
        if (r2 > 256.0)
        {
            escaped = true;
            break;
        }
        float3 d = float3(length(z - trapPoint), abs(dot(z, lineNormal) - lineOffset), abs(sqrt(r2) - ring));
        when = d < best ? i : when;
        best = min(best, d);
        n += 1.0;
    }

    // Each trap's closest pass, scaled so all three cut panes of a similar size.
    float3 score = best * float3(1.0, 1.6, 1.3);
    float nearest = min(score.x, min(score.y, score.z));
    // The pane takes the colour of the trap passed closest, blending softly where two meet,
    // and a dark line of lead runs where they're equally close.
    float3 share = exp(-(score - nearest) * 60.0);
    share /= share.x + share.y + share.z;
    float3 colour = trapColour(0, when.x * 0.045 + Time * 0.01) * share.x
                  + trapColour(1, when.y * 0.045 + 0.33) * share.y
                  + trapColour(2, when.z * 0.045 + 0.66) * share.z;
    float lead = 1.0 - max(share.x, max(share.y, share.z));
    // Deeper in, paths brush past the traps more closely, so the panes are cut finer to match.
    float light = exp(-nearest * (7.0 + 4.0 * max(-log2(scale), 0.0)));
    colour *= light * (1.0 - smoothstep(0.25, 0.5, lead) * 0.85) * 0.7;

    // The very closest passes catch the light like bevelled glass edges.
    colour += pow(light, 24.0) * float3(1.0, 0.9, 0.75) * (0.5 + Treble * 0.8);

    if (escaped)
    {
        // Outside the set, the glass fades with distance from its edge (measured in screen
        // space, so it looks the same at every zoom), leaving a faint violet halo.
        float r = length(z);
        float distance = r * log(r) / max(length(dz), 1e-20) / scale;
        float near = exp(-distance * 10.0);
        colour *= near;
        colour += float3(0.1, 0.05, 0.22) * exp(-distance * 2.5) * 0.12;
    }
    else
    {
        // Inside, the glass is lit from behind, dimmer and richer.
        colour *= 0.5;
    }
    return colour;
}

float3 Render(float2 uv, float2 p)
{
    float2 centre;
    float scale;
    float spin;
    cameraAt(Time, centre, scale, spin);

    // A different sub-pixel offset each frame, blended with the last frame (lined up with the
    // moving camera), smooths the finest filaments without blurring them.
    float2 jitter = float2(frac(FrameCount * 0.618034), frac(FrameCount * 0.754878)) - 0.5;
    float2 q = p + jitter * pixelSize();
    float2 c = centre + rotate(q, spin) * scale;

    float3 colour = shade(c, scale);
    colour *= 0.9 + Level * 0.3 + Beat * 0.15;

    // Where was this point of the plane on screen last frame?
    float2 lastCentre;
    float lastScale;
    float lastSpin;
    cameraAt(Time - DeltaTime, lastCentre, lastScale, lastSpin);
    float2 before = rotate((centre + rotate(p, spin) * scale - lastCentre) / lastScale, -lastSpin);
    float2 beforeUv = toUv(before);
    float keep = all(abs(beforeUv - 0.5) < 0.497) ? 0.6 : 0.0;
    return lerp(colour, previous(beforeUv), keep);
}
