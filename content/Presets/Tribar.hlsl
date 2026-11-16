// Name: Tribar
// The triangle that can't be built, in white, grey, and black on pure red: three bars, each one plainly in
// front of the next, all the way round. Beads run along every face in an endless loop, and light up like a
// marquee: a chase of lamps runs along every bar once a beat, each side's lamps blaze brighter with its own part
// of the music, and the grey face's lamps change colour every eight beats. The triangle rocks with each bar and
// swells on the beat, and a bright line spreads out from it across the red paper on every beat.
// Family: Geometric
// Tags: Geometric, Minimal, Retro
// Added: 2026-11-15 21:07

static const float Inner = 0.56;        // from the middle of the triangle to the middle of a side

static float TbSound;                   // how loud the music is just now, taken across all its parts

// One part of the music, with the high notes made up to stand level with the low ones.
float tbPart(float x)
{
    return spectrum(x) * (1.0 + 0.9 * x);
}

// How loud one part of the music is, judged against the music as a whole, so that a quiet song shows as plainly
// as a loud one. It listens to a broad part and answers gently, so the lamps swell and never jitter.
float tbLoud(float x)
{
    float heard = (tbPart(x - 0.03) + tbPart(x) + tbPart(x + 0.03)) / 3.0;
    return smoothstep(0.0, 1.0, saturate((heard - 0.55 * TbSound) / (0.8 * TbSound + 0.12)));
}

// The grey face's lamps, a new colour every eight beats.
float3 tbLamp(float which)
{
    float k = wrap(which, 4.0);
    if (k < 0.5) return float3(1.5, 0.07, 0.1);
    if (k < 1.5) return float3(1.5, 0.95, 0.1);
    if (k < 2.5) return float3(0.1, 1.2, 1.5);
    return float3(1.4, 0.1, 1.1);
}

float3 tbSides(float2 q)
{
    return float3(dot(q, float2(0.0, -1.0)), dot(q, float2(0.8660254, 0.5)), dot(q, float2(-0.8660254, 0.5)));
}

// Negative inside the figure: the big triangle with its corners cut off and its middle cut out.
float tbShape(float2 q, float wide)
{
    float3 u = tbSides(q);
    float most = max(u.x, max(u.y, u.z));
    float least = min(u.x, min(u.y, u.z));
    return max(max(most - Inner, -(2.0 * Inner - wide) - least), (Inner - 2.0 * wide) - most);
}

// The colour of the figure at q. Each side shows two strips; the outer strip of one side and the inner strip
// of the side before it are the same face, and that is the whole trick.
float3 tbFace(float2 q, float wide, float3 heard, float run, float chase, float3 lamp)
{
    float3 u = tbSides(q);
    bool3 outer = u > Inner - wide;
    bool3 inner = u > Inner - 2.0 * wide;
    int face = 0;
    int side = 0;
    bool inside = false;

    if (outer.x && outer.y) { face = 0; side = 0; }
    else if (outer.y && outer.z) { face = 1; side = 1; }
    else if (outer.z && outer.x) { face = 2; side = 2; }
    else if (outer.x) { face = 0; side = 0; }
    else if (outer.y) { face = 1; side = 1; }
    else if (outer.z) { face = 2; side = 2; }
    else
    {
        inside = true;
        if (inner.x && inner.y) { face = 1; side = 0; }
        else if (inner.y && inner.z) { face = 2; side = 1; }
        else if (inner.z && inner.x) { face = 0; side = 2; }
        else if (inner.x) { face = 1; side = 0; }
        else if (inner.y) { face = 2; side = 1; }
        else { face = 0; side = 2; }
    }

    float3 paint = face == 0 ? float3(1.1, 1.08, 1.02) : face == 1 ? float3(0.03, 0.03, 0.04) : float3(0.42, 0.42, 0.45);
    float3 bead = face == 0 ? float3(0.03, 0.03, 0.04) : face == 1 ? float3(1.1, 1.08, 1.02) : lamp;
    // What a bead shines with when it lights: the white face's black beads light red, the others their own
    // colour, brighter.
    float3 shining = face == 0 ? float3(1.9, 0.12, 0.16) : face == 1 ? float3(1.9, 1.75, 1.45) : lamp * 1.45;

    // Beads along the middle of the strip, all running the same way round.
    float2 along = side == 0 ? float2(1.0, 0.0) : side == 1 ? float2(-0.5, 0.8660254) : float2(-0.5, -0.8660254);
    float across = (side == 0 ? u.x : side == 1 ? u.y : u.z) - (Inner - (inside ? 1.5 : 0.5) * wide);
    float loud = side == 0 ? heard.x : side == 1 ? heard.y : heard.z;
    float gap = 0.2;
    float travelled = dot(q, along) - run * gap;
    float s = wrap(travelled, gap) - gap * 0.5;
    float which = floor(travelled / gap);

    // The chase: every fourth bead is lit at a time, and the light moves on four beads each beat, fading
    // smoothly from one bead to the next.
    float chased = 0.5 + 0.5 * cos(6.2832 * (which / 4.0 - chase));
    float lit = chased * chased * chased * chased * (0.4 + 0.6 * loud) + 0.25 * loud;
    float size = (0.03 + 0.012 * lit) * (wide / 0.19);
    float beadAt = length(float2(s, across)) - size;
    float3 shown = lerp(paint, lerp(bead, shining, saturate(lit)), smoothstep(0.004, -0.004, beadAt));
    // A lit bead's light spills onto the face round it.
    shown += shining * 0.22 * saturate(lit) * exp(-max(beadAt, 0.0) / (0.025 * wide / 0.19));
    return shown;
}

float3 Render(float2 uv, float2 p)
{
    TbSound = 0.0;
    [unroll]
    for (int part = 0; part < 6; part++)
        TbSound += tbPart(0.08 + 0.12 * part) / 6.0;
    float3 heard = float3(tbLoud(0.08), tbLoud(0.3), tbLoud(0.56));

    float swell = BeatSwell;
    float wide = 0.17 + 0.025 * swell;
    float run = wrap(Time * 0.3, 4096.0);
    float turn = 0.2 * sin(Time * 0.17) + 0.08 * sin(6.2832 * BarPhase);
    // With a steady beat the chase keeps time with it; without one it runs on at its own pace.
    float chase = SongMapped > 0.25 ? BeatPhase : frac(Time * 0.9);
    float since = SongMapped > 0.25 ? BeatPhase : saturate(-log(max(Beat, 0.004)) / 2.75);

    // The grey face's lamps: red, gold, cyan, and magenta, a new one every eight beats.
    float phrase = wrap(BeatGlide, 4096.0) / 8.0;
    float3 lamp = lerp(tbLamp(floor(phrase) - 1.0), tbLamp(floor(phrase)), smoothstep(0.0, 0.12, frac(phrase)));
    float2 q = rotate(float2(p.x, -p.y) + float2(0.0, 0.14), turn);

    // Red paper, with rings spreading from the triangle on the beat and its shadow beneath it.
    float3 colour = float3(1.5, 0.07, 0.1) * (1.0 - dot(uv - 0.5, uv - 0.5) * 0.45);
    float3 u = tbSides(q);
    float out1 = max(u.x, max(u.y, u.z)) - Inner;
    // Faint dark rings ripple out slowly all the time, and each beat sends a bright one racing out.
    float ring = abs(wrap(out1 * 5.0 - Time * 0.2, 1.0) - 0.5);
    colour *= 1.0 - 0.22 * smoothstep(0.07, 0.02, ring) * saturate(out1 * 30.0) * exp(-out1 * 1.6);
    float crest = out1 - 0.03 - since * 1.3;
    float beatRing = exp(-crest * crest * 500.0) * smoothstep(0.0, 0.08, since) * (1.0 - since) * saturate(out1 * 30.0);
    colour *= 1.0 + beatRing * (0.3 + 0.3 * heard.x) * float3(0.6, 2.5, 2.0);
    colour *= 1.0 - 0.45 * smoothstep(0.03, -0.03, tbShape(q - rotate(float2(0.045, -0.06), turn), wide));

    // Four looks at each dot of the picture, so the edges between the faces come out smooth.
    float px = pixelSize() * 0.5;
    float3 figure = float3(0.0, 0.0, 0.0);
    float cover = 0.0;
    [unroll]
    for (int k = 0; k < 4; k++)
    {
        float2 nudge = float2(k < 2 ? -0.5 : 0.5, (k == 0 || k == 3) ? -0.25 : 0.25) * px;
        float2 at = q + nudge;
        float inside = tbShape(at, wide) < 0.0 ? 1.0 : 0.0;
        figure += tbFace(at, wide, heard, run, chase, lamp) * inside;
        cover += inside;
    }

    colour = lerp(colour, figure / max(cover, 1.0), cover * 0.25);
    return colour * (0.94 + 0.06 * Intensity);
}
