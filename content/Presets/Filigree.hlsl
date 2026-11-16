// Name: Filigree
// A horn of beaded lace: scalloped rings of fine wire, each a rosette with smaller scallops along its lobes, grow
// from a point and widen toward the eye while the horn sways and slowly twists. Gold at the mouth, rose and violet
// along its length, sea blue at the tip. The music plays in the beads: each stretch of the horn shines with its
// own part of the music, bass at the tip and treble at the mouth, and each beat sends a gleam up from the tip.
// Family: Geometric
// Tags: Geometric, Colourful, Dark, Calm
// Added: 2026-11-16 04:40

static const int FgRings = 60;          // rings along the horn

float fgLoud(float x)
{
    return saturate((spectrum(x) - 0.42 + 0.5 * x) / 0.7);
}

// How loud the music is at one place along the horn, bass at the tip (0) and treble at the mouth (1), read at
// ten fixed places and blended between them.
float fgLoudAt(float along)
{
    float k = saturate(along) * 9.0;
    float low = floor(k);
    return lerp(fgLoud(0.08 + 0.06 * low), fgLoud(0.14 + 0.06 * low), smoothstep(0.0, 1.0, k - low));
}

float3 fgTint(float along)
{
    float3 c = lerp(float3(0.15, 0.85, 1.25), float3(0.6, 0.35, 1.5), smoothstep(0.0, 0.35, along));
    c = lerp(c, float3(1.5, 0.3, 0.65), smoothstep(0.3, 0.65, along));
    c = lerp(c, float3(1.6, 0.95, 0.22), smoothstep(0.6, 0.95, along));
    return c;
}

float3 Render(float2 uv, float2 p)
{
    float since = frac(BeatGlide);
    float flash = smoothstep(0.0, 0.1, since) * (1.0 - since) * (1.0 - since);
    float e = pixelSize();

    // The horn's mouth rests near the middle; its tip wanders, and the horn bows between them.
    float2 mouth = float2(0.12 * sin(Time * 0.11), 0.08 * sin(Time * 0.14 + 2.0));
    float2 tip = float2(0.62 * Aspect * sin(Time * 0.19 + 0.6), 0.6 * sin(Time * 0.27 + 1.9));
    float2 reach = tip - mouth;
    float2 bow = float2(-reach.y, reach.x) * 0.45 * sin(Time * 0.13);
    float flow = frac(Time * 0.3);
    float twist = 0.9 * sin(Time * 0.17);

    float3 colour = float3(0.006, 0.012, 0.03) + float3(0.01, 0.03, 0.05) * exp(-dot(p, p) * 0.7);
    [loop]
    for (int i = 0; i < FgRings; i++)
    {
        // This ring: how far along the horn it has grown, where its middle is, and how wide it is.
        float along = (i + flow) / FgRings;
        float back = 1.0 - along;
        float2 middle = mouth + reach * back * (0.35 + 0.65 * back) + bow * sin(PI * along) * back;
        float wide = 1.05 * exp(-2.9 * back);
        float2 v = p - middle;
        float far = length(v);
        if (far < wide * 1.45 && far > wide * 0.6)
        {
            // The ring's edge at this bearing: seven round lobes, each edged with smaller ones.
            float bearing = atan2(v.y, v.x + 1e-7) - twist * back - Time * 0.04;
            float s1 = sin(3.5 * bearing);
            float s2 = sin(10.5 * bearing);
            float s3 = sin(31.5 * bearing);
            float edge = 0.8 + 0.2 * abs(s1) + 0.07 * abs(s2) + 0.024 * abs(s3);
            float slope = 0.7 * cos(3.5 * bearing) * sign(s1) + 0.735 * cos(10.5 * bearing) * sign(s2) + 0.756 * cos(31.5 * bearing) * sign(s3);
            float off = abs(far - wide * edge) / sqrt(1.0 + slope * slope * wide * wide / max(far * far, 1e-6));

            // Fine wire strung with beads, and the soft light round it.
            float bead = pow(0.5 + 0.5 * cos(63.0 * bearing), 5.0);
            float wire = saturate(1.0 - off / (e * (1.0 + 1.6 * along)));
            float halo = exp(-off * off / (wide * wide * 0.0016 + e * e * 6.0));
            float loud = fgLoudAt(along);
            float gleam = flash * exp(-(along - since) * (along - since) * 40.0);
            float shine = 0.15 + 1.25 * loud + 2.0 * gleam;

            // Rings fade in at the tip and away at the mouth.
            float show = smoothstep(0.0, 0.12, along) * smoothstep(1.0, 0.86, along) * (0.4 + 0.6 * along);
            float3 tint = fgTint(saturate(along + 0.14 * sin(Time * 0.05)));
            colour += tint * show * (wire * (0.16 + 0.3 * loud + 1.3 * bead * shine) + halo * (0.03 + 0.16 * loud + 0.3 * gleam));
        }
    }

    return colour;
}
