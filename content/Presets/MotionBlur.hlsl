// Name: Motion Blur
// Yellow and cyan discs whirl about on a grey card, photographed with a slow shutter so that each one smears
// into a soft capsule along its path. The bass holds the shutter open longer, every beat hurries the discs on,
// each disc grows with its own part of the spectrum, and a drop flings them all outward.
// Family: Minimal
// Tags: Minimal, Colourful, Retro
// Added: 2026-11-15 20:42

static const int Discs = 16;

// Where disc `id` is when its clock reads `phase`: each one circles a home of its own.
float2 mbPlace(float id, float phase)
{
    float a = hash(float2(id, 1.7));
    float b = hash(float2(id, 5.3));
    float c = hash(float2(id, 9.1));
    // Homes are spread over a four-by-four grid so the card stays evenly filled.
    float2 slot = float2(wrap(id, 4.0), floor(id / 4.0));
    float2 home = ((slot + 0.2 + 0.6 * float2(a, b)) / 4.0 - 0.5) * float2(Aspect * 1.7, 1.6);
    float reach = (0.2 + 0.26 * c) * (1.0 + 0.7 * DropPulse);
    float turn = phase * (c > 0.45 ? 1.0 : -1.0) + a * 6.2832;
    return home + reach * float2(cos(turn), sin(turn) * (0.6 + 0.4 * b));
}

float3 Render(float2 uv, float2 p)
{
    // The card.
    float3 colour = float3(0.15, 0.155, 0.17) * (1.0 - 0.25 * uv.y) + (hash(p * 311.0) - 0.5) * 0.012;

    // How far each disc travels while the shutter is open.
    float shutter = 0.3 + 0.55 * Bass + 0.3 * BeatSwell;

    [unroll]
    for (int i = 0; i < Discs; i++)
    {
        float id = (float)i;
        float pace = 0.55 + 0.9 * hash(float2(id, 3.9));
        float phase = (Time * 0.8 + BeatGlide * 1.3) * pace + id * 2.4;
        float2 from = mbPlace(id, phase - shutter * pace);
        float2 travel = mbPlace(id, phase) - from;
        float size = 0.04 + 0.045 * hash(float2(id, 7.7)) + 0.055 * spectrum(0.03 + 0.045 * id);

        // The part of the exposure during which the disc covered this point of the card.
        float2 gap = p - from;
        float a = max(dot(travel, travel), 1e-6);
        float b = dot(travel, gap);
        float h = b * b - a * (dot(gap, gap) - size * size);
        float covered = 0.0;
        if (h > 0.0)
        {
            h = sqrt(h);
            covered = max(min((b + h) / a, 1.0) - max((b - h) / a, 0.0), 0.0);
        }

        float kind = hash(float2(id, 12.3));
        float3 ink = kind < 0.46 ? float3(1.55, 1.25, 0.18) : (kind < 0.92 ? float3(0.18, 1.3, 1.4) : float3(1.3, 1.3, 1.25));
        colour = lerp(colour, ink, saturate(covered * 1.9));
    }

    return colour * (0.92 + 0.1 * Level) * (1.0 - dot(uv - 0.5, uv - 0.5) * 0.6);
}
