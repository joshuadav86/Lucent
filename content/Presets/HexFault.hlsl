// Name: Hex Fault
// A white-rimmed hexagon on a dark ground whose inside cuts every four beats between bands of heat colour,
// striped cube faces, a striped disc, and coloured static, while slices of the picture slip sideways and split
// into red and blue on each beat. Flames of stretched colour climb from its top like a spectrum, bass at the left
// and treble at the right. Every sixteen beats the picture wipes to a pale ground, where the hexagon is dark metal crumbling into grain.
// Family: Cyberpunk
// Tags: Geometric, Intense, Colourful, Retro
// Added: 2026-11-16 02:16

static const float HfSize = 0.56;       // the hexagon, from its middle to a corner

float hfLoud(float x)
{
    return saturate((spectrum(x) - 0.56 + 0.5 * x) / 0.4);
}

// Heat colours: violet, blue, white, yellow, orange, red.
float3 hfHeat(float k)
{
    k = saturate(k) * 5.0;
    float3 c = lerp(float3(0.6, 0.05, 1.1), float3(0.1, 0.25, 1.4), saturate(k));
    c = lerp(c, float3(1.25, 1.3, 1.35), saturate(k - 1.0));
    c = lerp(c, float3(1.5, 1.2, 0.08), saturate(k - 2.0));
    c = lerp(c, float3(1.6, 0.5, 0.02), saturate(k - 3.0));
    c = lerp(c, float3(1.3, 0.03, 0.12), saturate(k - 4.0));
    return c;
}

// What one spot of the picture shows before the faults are laid over it.
// kind: which of the four fills the hexagon holds. pale: 0 on the dark ground, 1 on the pale one.
// slow and fast: two counts that tick on, one with the beat and one many times a second.
float3 hfPicture(float2 q, float kind, float pale, float slow, float fast)
{
    // The hexagon stands on a corner on the dark ground and lies on a side on the pale one.
    float turn = 0.1 * sin(Time * 0.31) + 0.5236 * pale;
    float2 right = float2(cos(turn), sin(turn));
    float reachIn = 0.866 * HfSize;

    // Where the hexagon's top and bottom lie straight above and below the spot.
    float top = -1e3;
    float bottom = 1e3;
    [unroll]
    for (int k = 0; k < 6; k++)
    {
        float a = turn + k * 1.0472;
        float2 n = float2(cos(a), sin(a));
        if (n.y > 1e-3)
            bottom = min(bottom, (reachIn - n.x * q.x) / n.y);
        else if (n.y < -1e-3)
            top = max(top, (reachIn - n.x * q.x) / n.y);
        else if (n.x * q.x > reachIn)
            top = 1e3;
    }

    // Outside the hexagon the picture is stretched in thin columns, up from its top on the dark ground and down
    // from its foot on the pale one, each column as far as its own part of the music is loud.
    float column = floor(q.x * 17.0);
    float loud = hfLoud(0.08 + 0.6 * saturate((column + 0.5) / 17.0 / (1.74 * HfSize) + 0.5));
    float reach = 0.03 + 0.85 * loud * (0.35 + 0.65 * hash(float2(column, slow * 0.37 + 3.3)));
    reach = floor(reach * (0.88 + 0.12 * hash(float2(floor(q.x * 68.0), slow + 1.7))) / 0.04) * 0.04;
    float2 at = q;
    float stretched = 0.0;
    if (top < bottom)
    {
        float rise = top - q.y;
        float drop = q.y - bottom;
        if (pale < 0.5 && rise > 0.0 && rise < reach)
        {
            at.y = top + 0.02 + 0.5 * rise;
            stretched = rise / max(reach, 1e-3);
        }

        if (pale > 0.5 && drop > 0.0 && drop < reach)
        {
            at.y = bottom - 0.02 - 0.5 * drop;
            stretched = drop / max(reach, 1e-3);
        }
    }

    // How far outside the hexagon the spot is, and which side is nearest.
    float2 h = float2(dot(at, right), dot(at, float2(-right.y, right.x)));
    float d = -1e3;
    float2 nearest = float2(1.0, 0.0);
    [unroll]
    for (int m = 0; m < 6; m++)
    {
        float a2 = turn + m * 1.0472;
        float2 n2 = float2(cos(a2), sin(a2));
        float over = dot(n2, at) - reachIn;
        if (over > d)
        {
            d = over;
            nearest = n2;
        }
    }

    float3 ground = lerp(float3(0.062, 0.062, 0.07), float3(0.92, 0.92, 0.9) * (1.0 - 0.07 * dot(q, q)), pale);
    float3 colour = ground;

    if (d < 0.0)
    {
        float3 inner = float3(0.02, 0.02, 0.03);
        float bass = hfLoud(0.09);
        float mid = hfLoud(0.3);
        float treble = hfLoud(0.6);
        if (kind < 0.5)
        {
            // Bands of heat colour in a zigzag.
            float zig = abs(frac(h.x / HfSize * 0.9 + 0.25 + Time * 0.11) - 0.5) * 2.0 - 0.5;
            float v = (h.y + (0.3 + 0.3 * bass) * HfSize * zig) / (1.05 * HfSize) + 0.5;
            if (v > 0.0 && v < 1.0)
                inner = hfHeat(floor(v * 6.0) / 5.0) * step(0.14, frac(v * 6.0));
        }
        else if (kind < 1.5)
        {
            // Three faces of a cube, each striped as wide as its part of the music is loud.
            float ang = atan2(h.x, -h.y + 1e-6);
            float3 tint = float3(1.35, 0.85, 1.1);
            float2 across = float2(0.0, 1.0);
            float duty = treble;
            if (ang > PI / 3.0)
            {
                tint = float3(0.75, 0.7, 1.4);
                across = float2(0.866, -0.5);
                duty = mid;
            }
            else if (ang < -PI / 3.0)
            {
                tint = float3(1.3, 1.25, 1.3);
                across = float2(0.866, 0.5);
                duty = bass;
            }

            float stripe = frac(dot(h, across) * 22.0 / HfSize * 0.5 + Time * 0.4);
            inner = tint * (stripe < 0.2 + 0.6 * duty ? 1.0 : 0.22);
            float seam = length(h) * min(min(abs(sin(ang - PI / 3.0)), abs(sin(ang + PI / 3.0))), abs(sin(ang)) + step(abs(ang), 1.6));
            inner = lerp(float3(1.3, 1.3, 1.35), inner, smoothstep(0.004, 0.009, seam));
        }
        else if (kind < 2.5)
        {
            // A white field holding a striped disc, heat-coloured blocks along its foot.
            inner = float3(1.2, 1.2, 1.25) * (frac(h.y * 9.0 / HfSize - Time * 0.25) < 0.42 + 0.3 * bass ? 1.0 : 0.12);
            if (length(h) < (0.36 + 0.12 * mid) * HfSize)
                inner = frac((h.x + h.y) * 15.0 / HfSize + Time * 0.6) < 0.3 + 0.4 * treble ? float3(0.06, 0.05, 0.09) : float3(1.35, 0.5, 0.85);
            else if (h.y > 0.5 * HfSize)
                inner = hfHeat(hash(float2(floor(h.x * 18.0), slow)));
        }
        else
        {
            // Coloured static round a white-hot middle.
            float2 block = floor(at * 70.0);
            float n1 = hash(block + fast * 1.7);
            float n2 = hash(block * 1.3 + 5.0 + fast * 3.1);
            inner = lerp(float3(1.25, 0.6, 0.95), float3(0.5, 1.0, 1.3), n1);
            inner = lerp(inner, float3(1.3, 1.2, 0.5), step(0.72, n2)) * (0.5 + 0.6 * n2);
            float hot = exp(-dot(h, h) / (0.012 + 0.03 * bass));
            inner = lerp(inner, float3(1.5, 1.45, 1.35), saturate(hot * 1.6));
        }

        // Fine lines across the fill, as on a screen.
        inner *= 0.8 + 0.2 * step(0.5, frac(q.y / (3.0 * pixelSize())));

        // On the pale ground the fill is grey and the rim dark metal, each side lit differently.
        float3 rim = float3(1.25, 1.25, 1.3);
        if (pale > 0.5)
        {
            inner = lerp(0.06, 0.8, saturate(dot(inner, float3(0.3, 0.5, 0.2)) / 1.2)) * float3(0.95, 0.97, 1.0);
            rim = float3(0.1, 0.1, 0.12) + 0.26 * saturate(0.5 + 0.5 * dot(nearest, float2(-0.5, -0.86)));
        }

        colour = d > -0.085 * HfSize ? rim : inner;

        // A stretched column burns out through the heat colours on the dark ground and crumbles to grain on the pale one.
        if (pale < 0.5)
            colour = lerp(colour, hfHeat(0.3 + 0.7 * stretched), smoothstep(0.55, 0.95, stretched)) * (stretched > 0.0 ? 0.55 + 0.45 * step(0.4, frac(q.y * 34.0)) : 1.0);
        else if (hash(floor(q / (2.0 * pixelSize())) + fast) < stretched)
            colour = ground;
    }

    return colour;
}

float3 Render(float2 uv, float2 p)
{
    float beats = wrap(BeatGlide, 4096.0);
    float since = frac(beats);
    float bar = floor(beats / 4.0);
    float fast = wrap(floor(Time * 12.0), 1024.0);
    float slow = floor(beats * 2.0);

    // The fill changes every four beats, block by block, so that each of the four shows once in sixteen.
    float kind = wrap(bar + floor(hash(float2(floor(bar / 4.0), 9.1)) * 4.0), 4.0);
    float last = wrap(bar - 1.0 + floor(hash(float2(floor((bar - 1.0) / 4.0), 9.1)) * 4.0), 4.0);
    if (hash(floor(p * 9.0) + bar) > (beats - bar * 4.0) / 0.35)
        kind = last;

    // How hard the picture is faulting: hardest as the fill changes, a little on every beat.
    float fault = saturate(exp(-(beats - bar * 4.0) * 4.0) + 0.3 * exp(-since * 6.0) + 0.8 * DropPulse);

    // Every sixteen beats a ragged edge sweeps down the picture, swapping the dark ground for the pale one.
    float span = beats / 16.0;
    float pale = wrap(floor(span), 2.0);
    float sweep = lerp(-1.2, 1.2, smoothstep(0.7, 1.0, frac(span)));
    if (p.y + 0.1 * (hash(float2(floor(p.x * 14.0), fast)) - 0.5) < sweep)
        pale = 1.0 - pale;

    // Slices of the picture slip sideways.
    float band = floor(p.y * 7.0 + 5.0 * hash(float2(fast, 1.3)));
    float thin = floor(p.y * 38.0);
    float slip = 0.0;
    if (hash(float2(band, fast)) > 1.0 - 0.5 * fault)
        slip += (hash(float2(band, fast + 7.0)) - 0.5) * 0.6 * fault;
    if (hash(float2(thin, fast + 3.0)) > 1.0 - 0.3 * fault)
        slip += (hash(float2(thin, fast + 11.0)) - 0.5) * 0.15;

    // Red and blue pull apart.
    float2 q = p + float2(slip, 0.0);
    float2 split = float2(0.003 + 0.035 * fault, 0.0);
    float3 colour = float3(hfPicture(q - split, kind, pale, slow, fast).r,
                           hfPicture(q, kind, pale, slow, fast).g,
                           hfPicture(q + split, kind, pale, slow, fast).b);

    // Now and then a bar of noise lies right across the picture.
    float barAt = (hash(float2(slow, 5.5)) - 0.5) * 1.5;
    float barWide = 0.004 + 0.012 * hash(float2(slow, 6.6));
    if (hash(float2(slow, 8.8)) < 0.15 + 0.6 * fault && abs(p.y - barAt) < barWide)
    {
        float n = hash(float2(floor(p.x * 40.0), fast + slow));
        float3 noisy = lerp(float3(1.0, 0.9, 0.45), float3(1.0, 0.5, 0.75), n) * (0.35 + 0.5 * hash(float2(floor(p.x * 160.0), fast)));
        colour = lerp(noisy, float3(0.3, 0.3, 0.33) * (0.5 + n), pale);
    }

    colour += (hash(p * 311.0 + fast) - 0.5) * 0.035;
    return max(colour, float3(0.0, 0.0, 0.0));
}
