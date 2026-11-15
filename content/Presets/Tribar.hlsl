// Name: Tribar
// The triangle that can't be built, in white, grey, and black on pure red: three bars, each one plainly in
// front of the next, all the way round. Beads run along every face in an endless loop. The bars thicken
// with the bass, each side's beads swell with its own part of the music, and rings spread out on the beat.
// Family: Geometric
// Tags: Geometric, Minimal, Retro
// Added: 2026-11-15 21:07

static const float Inner = 0.56;        // from the middle of the triangle to the middle of a side

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
float3 tbFace(float2 q, float wide, float3 heard, float run)
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
    float3 bead = face == 0 ? float3(0.03, 0.03, 0.04) : face == 1 ? float3(1.1, 1.08, 1.02) : float3(1.5, 0.07, 0.1);

    // Beads along the middle of the strip, all running the same way round.
    float2 along = side == 0 ? float2(1.0, 0.0) : side == 1 ? float2(-0.5, 0.8660254) : float2(-0.5, -0.8660254);
    float across = (side == 0 ? u.x : side == 1 ? u.y : u.z) - (Inner - (inside ? 1.5 : 0.5) * wide);
    float loud = side == 0 ? heard.x : side == 1 ? heard.y : heard.z;
    float gap = 0.2;
    float s = wrap(dot(q, along) - run * gap, gap) - gap * 0.5;
    float beadAt = length(float2(s, across)) - (0.022 + 0.04 * loud) * (wide / 0.19);
    return lerp(paint, bead, smoothstep(0.004, -0.004, beadAt));
}

float3 Render(float2 uv, float2 p)
{
    float3 heard = float3(spectrum(0.04), spectrum(0.2), spectrum(0.5));
    float wide = 0.17 + 0.04 * Bass;
    float run = BeatGlide * 0.5 + Time * 0.25;
    float turn = 0.22 * sin(Time * 0.17);
    float2 q = rotate(float2(p.x, -p.y) + float2(0.0, 0.14), turn);

    // Red paper, with rings spreading from the triangle on the beat and its shadow beneath it.
    float3 colour = float3(1.5, 0.07, 0.1) * (1.0 - dot(uv - 0.5, uv - 0.5) * 0.45);
    float3 u = tbSides(q);
    float out1 = max(u.x, max(u.y, u.z)) - Inner;
    float ring = abs(wrap(out1 * 5.0 - BeatGlide * 0.5, 1.0) - 0.5);
    colour *= 1.0 - 0.3 * smoothstep(0.07, 0.02, ring) * saturate(out1 * 30.0) * exp(-out1 * 1.6);
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
        figure += tbFace(at, wide, heard, run) * inside;
        cover += inside;
    }

    colour = lerp(colour, figure / max(cover, 1.0), cover * 0.25);
    return colour * (0.94 + 0.08 * Level);
}
