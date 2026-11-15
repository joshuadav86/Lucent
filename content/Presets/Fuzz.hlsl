// Name: Fuzz
// A ball of long fur in every colour of the rainbow, turning under one lamp in a dark blue room. Every hair
// is drawn, dark at the root and bright at the tip. The fur stands up taller with the bass, waves comb
// through it as the music plays, and the whole ball puffs out on the beat.
// Family: Organic
// Tags: 3D, Colourful, Dark
// Added: 2026-11-15 21:03

static const float Skin = 0.72;

float fzCorner(float3 c)
{
    return hash(float2(c.x + c.z * 37.17, c.y - c.z * 11.71) * 0.731 + 3.1);
}

// Smooth noise in space: one number for every direction, so each hair has a place to grow.
float fzNoise(float3 x)
{
    float3 c = floor(x);
    float3 f = x - c;
    f = f * f * (3.0 - 2.0 * f);
    float near = lerp(lerp(fzCorner(c), fzCorner(c + float3(1.0, 0.0, 0.0)), f.x),
                      lerp(fzCorner(c + float3(0.0, 1.0, 0.0)), fzCorner(c + float3(1.0, 1.0, 0.0)), f.x), f.y);
    float far = lerp(lerp(fzCorner(c + float3(0.0, 0.0, 1.0)), fzCorner(c + float3(1.0, 0.0, 1.0)), f.x),
                     lerp(fzCorner(c + float3(0.0, 1.0, 1.0)), fzCorner(c + float3(1.0, 1.0, 1.0)), f.x), f.y);
    return lerp(near, far, f.z);
}

// How much hair there is at x. `height` is 0 at the skin and 1 at the tips.
float fzHair(float3 x, float fur, float3 heard, float comb, out float height, out float3 way)
{
    float r = length(x);
    way = x / max(r, 1e-4);
    height = saturate((r - Skin) / fur);

    // Hairs droop a little, and waves of the music comb them sideways.
    float wave = heard.x * sin(way.y * 5.0 + comb) + heard.y * sin(way.x * 7.0 - comb * 1.3) + heard.z * sin(way.z * 11.0 + comb * 1.7);
    float3 bent = way + height * height * (float3(0.0, 0.22, 0.0) + 0.3 * wave * float3(way.z, 0.0, -way.x));
    float strand = fzNoise(normalize(bent) * 42.0);
    float hair = saturate((strand - 0.3 - 0.55 * height) * 7.0);
    return r > Skin + fur ? 0.0 : hair;
}

float3 Render(float2 uv, float2 p)
{
    float3 room = float3(0.025, 0.05, 0.16) * (0.6 + 1.3 * exp(-dot(p - float2(0.0, 0.25), p - float2(0.0, 0.25)) * 0.9));
    room *= 1.0 - dot(uv - 0.5, uv - 0.5) * 0.8;

    float3 heard = float3(spectrum(0.04), spectrum(0.18), spectrum(0.45));
    float fur = 0.46 + 0.1 * Bass + 0.05 * BeatSwell;
    float comb = Time * 0.7 + BeatGlide * 1.2;
    float turn = Time * 0.2 + BeatGlide * 0.12;

    float3 eye = float3(0.0, 0.0, -3.6);
    float3 ray = normalize(float3(p.x, -p.y, 2.5));
    eye.yz = rotate(eye.yz, 0.2);
    ray.yz = rotate(ray.yz, 0.2);
    eye.xz = rotate(eye.xz, turn);
    ray.xz = rotate(ray.xz, turn);
    float3 lamp = normalize(float3(-0.45, 0.8, -0.4));
    lamp.xz = rotate(lamp.xz, turn);

    float reach = Skin + fur;
    float b = dot(eye, ray);
    float h = b * b - dot(eye, eye) + reach * reach;
    if (h <= 0.0) return room;
    h = sqrt(h);
    float leave = -b + h;
    float skinHit = b * b - dot(eye, eye) + Skin * Skin;
    if (skinHit > 0.0) leave = -b - sqrt(skinHit);

    // Walk through the fur, adding up the hairs in front of one another.
    float pace = 0.028 / detailScale();
    float t = max(-b - h, 0.0) + pace * hash(p * 311.0 + 7.0);
    float3 colour = float3(0.0, 0.0, 0.0);
    float covered = 0.0;
    int steps = qualitySteps(80);

    [loop]
    for (int i = 0; i < steps; i++)
    {
        if (t > leave || covered > 0.97) break;
        float height;
        float3 way;
        float hair = fzHair(eye + ray * t, fur, heard, comb, height, way);
        if (hair > 0.01)
        {
            float hue = frac(0.55 + 0.34 * way.y + 0.12 * sin(3.0 * atan2(way.z, way.x + 1e-5)) + Time * 0.015);
            float3 dye = hsv2rgb(hue, 0.82, 1.0) * float3(1.45, 1.3, 1.5);
            float lit = (0.12 + 0.88 * height * height) * (0.35 + 0.65 * saturate(dot(way, lamp) * 0.6 + 0.45));
            float sheen = pow(height, 4.0) * pow(1.0 - saturate(-dot(way, ray)), 2.0);
            float cover = hair * 0.4 * (1.0 - covered);
            colour += cover * (dye * lit + float3(0.5, 0.5, 0.55) * sheen);
            covered += cover;
        }

        t += pace;
    }

    if (skinHit > 0.0) covered = lerp(covered, 1.0, 0.92);       // the dark skin under the fur
    return (colour + room * (1.0 - covered)) * (0.94 + 0.08 * Level);
}
