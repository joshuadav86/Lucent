// Name: Semaphore
// A wall of black crosses on mustard yellow, like a page of plus signs. Each column is one part of the music,
// bass on the left and treble on the right, and its crosses grow as that part sounds, the loudest turning
// white. On the beat a wave of quarter turns spreads out from the middle, so pluses become crosses and back.
// Family: Music-shaped
// Tags: Minimal, Geometric, Retro
// Added: 2026-11-15 21:13

static const float Pitch = 0.2;         // from one cross to the next

float3 Render(float2 uv, float2 p)
{
    float3 mustard = float3(1.5, 1.02, 0.1);
    float3 colour = mustard * (1.0 - dot(uv - 0.5, uv - 0.5) * 0.4);

    float2 g = (p + float2(0.0, 0.03 * sin(Time * 0.2))) / Pitch;
    float2 cell = floor(g);
    float2 q = (g - cell - 0.5) * Pitch;
    float2 middle = (cell + 0.5) * Pitch;

    // This column's part of the music.
    float across = saturate(middle.x / (2.0 * Aspect) + 0.5);
    float heard = spectrum(0.02 + 0.6 * across);

    // Quarter turns, eased, starting later the further the cross is from the middle of the wall.
    float wave = BeatGlide * 0.5 - length(middle) * 0.45;
    float whole = floor(wave);
    float part = smoothstep(0.0, 0.55, wave - whole);
    float angle = (whole + part) * 0.5 * PI;
    q = rotate(q, angle);

    float arm = Pitch * (0.14 + 0.24 * saturate(heard * 1.2) + 0.03 * BeatSwell);
    float thick = Pitch * (0.055 + 0.03 * saturate(heard * 1.2));
    float2 a = abs(q);
    float cross = min(max(a.x - arm, a.y - thick), max(a.y - arm, a.x - thick));

    float edge = pixelSize() * 1.2;
    float3 ink = lerp(float3(0.03, 0.03, 0.04), float3(1.12, 1.1, 1.0), smoothstep(0.62, 0.66, heard));
    colour *= 1.0 - 0.18 * smoothstep(edge * 6.0, 0.0, cross - 0.004) * (1.0 - smoothstep(edge, -edge, cross));
    colour = lerp(colour, ink, smoothstep(edge, -edge, cross));
    return colour * (0.94 + 0.08 * Level);
}
