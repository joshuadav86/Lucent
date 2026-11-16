// Name: Guilloche
// The engraved lacework of a banknote, drawn in navy, oxblood and green ink on cream paper: five rings of
// interlaced wavy lines round a small star. Each ring widens with its own part of the music and turns one notch
// with every beat, alternate rings going opposite ways, and the outermost line is the sound wave itself.
// Family: Geometric
// Tags: Geometric, Minimal, Retro
// Added: 2026-11-16 00:03

static const float GuLobes[5] = { 5.0, 8.0, 12.0, 16.0, 22.0 };
static const float GuHears[5] = { 0.03, 0.1, 0.2, 0.34, 0.52 };

// How loud the music is at x (0 deep bass, 1 high treble), stretched so that ordinary music swings it most of
// the way between 0 and 1 instead of sitting near the top.
float guLoud(float x)
{
    return saturate((spectrum(x) - 0.56 + 0.5 * x) / 0.4);
}

float3 Render(float2 uv, float2 p)
{
    float e = pixelSize();
    float r = length(p);
    float around = atan2(p.y, p.x + 1e-5);

    // Paper, with a little grain, and a faint ground of wavy lines like the background of a banknote.
    float3 colour = float3(1.16, 1.02, 0.74) * (0.96 + 0.05 * fbm(p * 38.0)) * (1.0 - dot(uv - 0.5, uv - 0.5) * 0.3);
    float row = p.y / 0.04;
    float rowId = floor(row + 0.5);
    float wave = 0.2 * sin(p.x * 16.0 + rowId * 1.3 + Time * 0.4) * (0.5 + Mid);
    float ground = abs(row - rowId - wave) * 0.04;
    colour = lerp(colour, float3(0.4, 0.75, 0.5), smoothstep(e * 1.3, 0.0, ground - 0.0012) * 0.5 * smoothstep(0.92, 0.96, r));

    float3 navy = float3(0.04, 0.09, 0.42);
    float3 oxblood = float3(0.62, 0.06, 0.1);
    float3 green = float3(0.03, 0.36, 0.22);

    [unroll]
    for (int j = 0; j < 5; j++)
    {
        float f = (float)j;
        float heard = guLoud(GuHears[j]);
        float middle = 0.17 + 0.135 * f;
        float swing = 0.016 + 0.062 * heard;
        float lobes = GuLobes[j];
        float turn = (fmod(f, 2.0) < 0.5 ? 1.0 : -1.0) * BeatGlide * PI / 3.0 + Time * 0.05 * (f - 2.0);
        float v = r - middle;
        float nearest = 1.0;

        // Six copies of one wavy ring, each a sixth of a wave on from the last, make the lace.
        [unroll]
        for (int m = 0; m < 6; m++)
        {
            float phase = lobes * around + turn + (float)m * PI / 3.0;
            float slope = swing * lobes * cos(phase) / max(r, 0.02);
            nearest = min(nearest, abs(v - swing * sin(phase)) / sqrt(1.0 + slope * slope));
        }

        // A plain ring either side holds the lace in.
        nearest = min(nearest, abs(abs(v) - swing - 0.012));
        float3 ink = j == 1 ? oxblood : (j == 3 ? green : navy);
        colour = lerp(colour, ink, smoothstep(e * 1.2, 0.0, nearest - 0.0016) * 0.92);
    }

    // The star at the heart: four nested eight-petalled outlines that swell with the bass.
    float petals = 0.6 + 0.4 * cos(8.0 * around - BeatGlide * PI * 0.25);
    float star = 1.0;
    [unroll]
    for (int s = 0; s < 4; s++)
    {
        float size = (0.085 + 0.05 * guLoud(0.1)) * (1.0 - 0.22 * (float)s);
        star = min(star, abs(r - size * petals));
    }

    colour = lerp(colour, oxblood, smoothstep(e * 1.2, 0.0, star - 0.0016) * 0.92);

    // The sound wave, wrapped into the outermost ring (mirrored left to right so that its ends meet).
    float waveX = abs(around) / PI;
    float wavy = 0.84 + 0.05 * (waveform(waveX - 0.009) + waveform(waveX - 0.003) + waveform(waveX + 0.003) + waveform(waveX + 0.009)) * 0.25;
    colour = lerp(colour, oxblood, smoothstep(e * 1.4, 0.0, abs(r - wavy) - 0.0022) * 0.92);
    colour = lerp(colour, navy, smoothstep(e * 1.2, 0.0, abs(abs(r - 0.84) - 0.06) - 0.0014) * 0.9);

    // A lace border round the edge of the note: three interlaced waves between two plain lines.
    float2 fromSide = abs(p) - float2(Aspect - 0.07, 0.93) + 0.06;
    float border = length(max(fromSide, 0.0)) + min(max(fromSide.x, fromSide.y), 0.0) - 0.06 + 0.07;
    float sway = 0.014 + 0.016 * Level;
    float laceEdge = 1.0;
    [unroll]
    for (int n = 0; n < 3; n++)
    {
        float phase = 44.0 * (p.x + p.y) + BeatGlide * PI * 0.5 + (float)n * PI * 2.0 / 3.0;
        float slope = sway * 44.0 * 1.4 * cos(phase);
        laceEdge = min(laceEdge, abs(border - sway * sin(phase)) / sqrt(1.0 + slope * slope));
    }

    laceEdge = min(laceEdge, abs(abs(border) - 0.042));
    colour = lerp(colour, navy, smoothstep(e * 1.2, 0.0, laceEdge - 0.0016) * 0.92);

    return colour * (0.97 + 0.04 * Level);
}
