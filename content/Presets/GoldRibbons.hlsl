// Name: Gold Ribbons
// Six ribbons of gold light cross and twist in a dark amber room, burning to white where they meet. Each
// ribbon belongs to one part of the spectrum and widens when it sounds, the deepest lowest, and every beat
// sends a bright pulse running along them from left to right.
// Family: Music-shaped
// Tags: Calm, Fire, Dark
// Added: 2026-11-15 20:46

static const int Ribbons = 6;

float3 Render(float2 uv, float2 p)
{
    float flow = Time * 0.35 + BeatGlide * 0.3;
    float beatLight = SongMapped > 0.0 ? 0.35 + Beat * 0.65 : Beat;
    float pulse = p.x + Aspect + 0.7 - BeatPhase * (2.0 * Aspect + 1.5);
    float running = exp(-pulse * pulse * 5.0) * beatLight;

    float light = 0.0;
    float hot = 0.0;

    [unroll]
    for (int i = 0; i < Ribbons; i++)
    {
        float n = (float)i;
        float heard = (spectrum(0.03 + n * 0.11) + spectrum(0.06 + n * 0.11) + spectrum(0.09 + n * 0.11)) / 3.0;

        // The ribbon's path: a long wave whose height itself swells and shrinks along the way.
        float sway = 0.55 + 0.45 * sin(p.x * 0.45 + n * 1.3 - flow * 0.3);
        float y = 0.62 * sway * sin(p.x * (0.8 + 0.17 * n) + flow * (0.5 + 0.09 * n) + n * 1.9) + (2.5 - n) * 0.07;

        // It twists as it goes, so it shows now its face and now its edge.
        float twist = abs(cos(p.x * (1.1 + 0.2 * n) - flow * 0.8 + n * 2.3));
        float face = (0.011 + 0.06 * heard + 0.02 * running) * (0.22 + 0.78 * twist);
        float d = abs(p.y - y);

        light += face / (d + face * 0.5 + 0.004);
        hot += smoothstep(face * 1.2, 0.0, d) * (0.4 + 0.6 * heard);
    }

    // The room: dark amber, a little lighter where the ribbons pass.
    float3 room = float3(0.2, 0.1, 0.04) * (1.2 - 0.75 * length(p * float2(0.5, 0.9)));
    float3 colour = max(room, 0.0) + float3(1.0, 0.66, 0.24) * light * (0.26 + 0.05 * Level + 0.12 * running);
    colour += float3(1.0, 0.9, 0.7) * hot * (0.35 + 0.3 * running + 0.2 * DropPulse);
    return colour * (1.0 - dot(uv - 0.5, uv - 0.5) * 0.6);
}
