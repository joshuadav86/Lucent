// Name: Fable Doyle Spiral
// A Doyle spiral, where every circle touches six neighbours and each is a scaled copy of the last, poured from one pole
// into another: neon rings stream out of the bass end and wind down into the treble end, each one a small meter for its
// own band of the spectrum, with a wave of light crossing from pole to pole on every beat.
// Family: Kaleidoscope
// Tags: Geometric, Neon, Colourful
// Added: 2026-11-15 17:00

// A Doyle spiral of type (9, 24): circles centred at A^i B^j with radius r |A^i B^j|, where each circle touches
// the six around it and going 9 steps along A lands on the same circle as 24 steps along B, one turn round.
// A, B and r are the solution of the three tangency conditions and that closing condition, here as logarithms.
static const float2 LogA = float2(0.297241024, -0.042742443);
static const float2 LogB = float2(0.111465384, -0.277827804);
static const float CircleRadius = 0.149042095;
static const float StepsA = 9.0;
static const float StepsB = 24.0;

float2 fdDivide(float2 a, float2 b)
{
    return float2(a.x * b.x + a.y * b.y, a.y * b.x - a.x * b.y) / max(dot(b, b), 1e-9);
}

float2 fdExp(float2 z)
{
    return exp(z.x) * float2(cos(z.y), sin(z.y));
}

// The light of one circle of the spiral at the point w. flowed is how far along A the circle has streamed
// (a whole number plus the flow), j its place along B; scale is how much bigger the spiral's plane is drawn
// than the screen here, so widths can be given in screen units.
float3 fdCircle(float2 w, float flowed, float index, float j, float scale)
{
    float2 centre = fdExp(flowed * LogA + j * LogB);
    float size = length(centre);
    float radius = CircleRadius * size;
    float onScreen = radius / scale;
    float pixel = pixelSize();
    // Circles too small to draw as rings fade out, so the poles stay dark instead of burning white.
    float shown = smoothstep(3.0, 12.0, onScreen / pixel);
    if (shown <= 0.0)
    {
        return 0.0;
    }

    float away = length(w - centre) / radius;   // 0 at its middle, 1 on its edge
    if (away > 1.25)
    {
        return 0.0;
    }

    // Which band it listens to: bass at the pole the circles pour out of, treble at the one they wind into.
    float along = log(size);
    float band = saturate(0.5 + along * 0.15);
    float level = spectrum(0.02 + band * 0.75);
    float wave = (along + 3.2) / 6.4 - BeatPhase;
    float pulse = exp(-wave * wave * 30.0) * (SongMapped > 0.0 ? 0.35 + Beat * 0.65 : Beat);

    // Its colour comes from which arm of each family it sits on, so it keeps it as it streams along.
    float armA = wrap(index, StepsA) / StepsA;
    float armB = wrap(j, StepsB) / StepsB;
    float hue = 0.69 + 0.24 * sin(2.0 * PI * (armB + armA) + Time * 0.05);
    float3 colour = hsv2rgb(frac(hue), 0.82, 1.0);
    float3 gold = float3(1.0, 0.72, 0.25);

    // The rim, drawn a little inside the circle so touching neighbours stay two rings.
    float rim = (away - 0.93) * onScreen;
    float line0 = glow(rim, 0.85 / pixel) + glow(rim, 9.0 / max(onScreen, pixel)) * 0.22;
    // A gold ring inside that opens and closes with the band, and a soft fill.
    float inner = (away - (0.16 + level * 0.62)) * onScreen;
    float line1 = glow(inner, 1.1 / pixel) * step(away, 0.9);
    float fill = saturate(1.0 - away) * saturate(1.0 - away);

    float3 light = colour * line0 * (0.55 + level * 1.5 + pulse * 1.6 + DropPulse * 0.6);
    light += gold * line1 * (0.25 + level * 1.3 + pulse * 0.8);
    light += colour * fill * (0.05 + level * 0.3 + pulse * 0.35);
    return light * shown * (0.55 + 0.45 * shown);
}

float3 Render(float2 uv, float2 p)
{
    // The two poles circle the middle slowly, drifting nearer and further apart.
    float turn = Time * 0.025 + BeatGlide * 0.008;
    float reach = 0.6 + 0.14 * sin(Time * 0.06) - DropPulse * 0.08;
    float2 from = rotate(float2(-reach, 0.0), turn);
    float2 into = -from;

    // A Mobius map sends one pole to 0 and the other to infinity; the spiral lives in that plane.
    float2 toInto = p - into;
    float2 w = fdDivide(p - from, toInto);
    float scale = length(from - into) / max(dot(toInto, toInto), 1e-6);
    float size2 = dot(w, w);
    if (size2 < 1e-10 || size2 > 1e10)
    {
        return 0.0;
    }

    // In logarithms the spiral is a plain lattice with sides LogA and LogB: find which cell the point is in.
    float2 logW = float2(0.5 * log(size2), atan2(w.y, w.x));
    float det = LogA.x * LogB.y - LogB.x * LogA.y;
    float i = (logW.x * LogB.y - LogB.x * logW.y) / det;
    float j = (LogA.x * logW.y - logW.x * LogA.y) / det;

    // The circles stream along A, from one pole to the other. (Colours repeat every 9 steps, so the flow can
    // wrap there and stay exact however long the visual has been open.)
    float flow = wrap(Time * 0.16 + BeatGlide * 0.07, StepsA);
    float first = floor(i - flow);
    float firstJ = floor(j);

    // The cell's four corners are the only circles that can reach into it.
    float3 colour = 0.0;
    colour += fdCircle(w, first + flow, first, firstJ, scale);
    colour += fdCircle(w, first + 1.0 + flow, first + 1.0, firstJ, scale);
    colour += fdCircle(w, first + flow, first, firstJ + 1.0, scale);
    colour += fdCircle(w, first + 1.0 + flow, first + 1.0, firstJ + 1.0, scale);

    // A little light at each pole, where the circles shrink past seeing.
    float poles = exp(-length(p - from) * 22.0) * (0.5 + Bass * 1.2) + exp(-length(p - into) * 22.0) * (0.5 + Treble * 1.6);
    colour += float3(0.75, 0.55, 1.0) * poles * 0.22;
    return colour * (0.85 + Intensity * 0.3);
}
