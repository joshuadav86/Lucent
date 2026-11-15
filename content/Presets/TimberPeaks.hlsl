// Name: Timber Peaks
// A floor of six-sided wooden posts, pale ash to dark walnut with a few lacquered red, lit by low sun. The
// posts are a spectrum: bass on the left, treble on the right, each one rising when its part of the music
// sounds. Waves of height roll away from you on the beat, and the red posts always stand a little taller.
// Family: Music-shaped
// Tags: 3D, Geometric, Calm
// Added: 2026-11-15 20:58

static const float Tallest = 3.3;

// The post that a point on the floor belongs to: `centre` is its middle, and the result is how far the point
// is from the post's edge (0 at the middle of the post, 0.5 at the edge).
float tpPost(float2 g, out float2 centre)
{
    float2 grid = float2(1.0, 1.7320508);
    float2 a = g - grid * floor(g / grid + 0.5);
    float2 b = g - grid * 0.5 - grid * floor((g - grid * 0.5) / grid + 0.5);
    float2 local = dot(a, a) < dot(b, b) ? a : b;
    centre = g - local;
    local = abs(local);
    return max(local.x, dot(local, float2(0.5, 0.8660254)));
}

// What the post is: x is its height, y picks its wood. `centre` is in the sliding floor's own coordinates.
float2 tpKind(float2 centre, float slide)
{
    // Whole numbers for each post, so its wood and its resting height never waver.
    float2 id = float2(floor(centre.x * 2.0 + 0.5), wrap(floor(centre.y / 0.8660254 + 0.5), 200.0));
    float wood = hash(id * float2(0.731, 0.377) + 2.7);
    float across = saturate(centre.x / 13.0 + 0.5);
    float heard = (spectrum(across * 0.75 + 0.01) + spectrum(across * 0.75 + 0.035)) * 0.5;
    float wave = 0.62 + 0.38 * sin((centre.y - slide) * 0.8 - BeatGlide * PI);
    float height = 0.15 + 0.35 * hash(id * float2(0.37, 0.613) + 4.4) + 3.4 * heard * wave + 0.12 * BeatSwell;
    if (wood > 0.9) height += 0.3;
    return float2(min(height, Tallest - 0.05), wood);
}

// How far a ray may safely move from x. `kind` describes the post under x.
float tpMap(float3 x, float slide, out float2 kind, out float2 centre, out float post)
{
    float rim = tpPost(x.xz + float2(0.0, slide), centre);
    kind = tpKind(centre, slide);
    float2 d = float2(rim - 0.46, x.y - kind.x);
    post = min(max(d.x, d.y), 0.0) + length(max(d, 0.0));
    // Don't step past the edge of this post's cell: the next post may be taller.
    return max(min(post, 0.5 - rim + 0.02), x.y - Tallest);
}

float3 tpWood(float wood)
{
    if (wood > 0.9) return float3(1.5, 0.1, 0.08);
    if (wood < 0.3) return float3(1.15, 0.88, 0.6);
    if (wood < 0.62) return float3(0.92, 0.62, 0.36);
    if (wood < 0.8) return float3(0.36, 0.22, 0.13);
    return float3(0.11, 0.085, 0.075);
}

float3 Render(float2 uv, float2 p)
{
    float slide = wrap(Time * 0.22 + BeatGlide * 0.18, 173.20508);
    float3 eye = float3(0.6 * sin(Time * 0.09), 6.6 + 0.3 * sin(Time * 0.13), -5.6);
    float3 ray = cameraRay(eye, float3(0.0, 0.6, 0.6), float2(p.x, -p.y), 1.5, 0.05 * sin(Time * 0.11));
    float3 sun = normalize(float3(-0.55, 0.62, -0.3));
    float3 sky = float3(1.25, 1.02, 0.84);

    float t = max((eye.y - Tallest) / max(-ray.y, 1e-3), 0.0);
    float2 kind = float2(0.0, 0.0);
    float2 centre = float2(0.0, 0.0);
    bool hit = false;
    int steps = qualitySteps(120);

    [loop]
    for (int i = 0; i < steps; i++)
    {
        float3 x = eye + ray * t;
        float post;
        float d = tpMap(x, slide, kind, centre, post);
        if (post < 0.003 * surfacePrecision())
        {
            hit = true;
            break;
        }

        t += d;
        if (t > 40.0 || x.y < -0.2) break;
    }

    if (!hit) return sky * (1.0 - dot(uv - 0.5, uv - 0.5) * 0.4);

    float3 at = eye + ray * t;
    float2 floorAt = at.xz + float2(0.0, slide);
    float2 local = floorAt - centre;
    bool top = at.y > kind.x - 0.012;
    float3 n = float3(0.0, 1.0, 0.0);
    if (!top)
    {
        // The side that was hit is the one of the six whose direction the point lies furthest along.
        float2 side = abs(local.x) > dot(abs(local), float2(0.5, 0.8660254)) ? float2(sign(local.x), 0.0) : float2(0.5 * sign(local.x), 0.8660254 * sign(local.y));
        n = float3(side.x, 0.0, side.y);
    }

    // Grain: rings on the sawn top, long streaks down the sides.
    float3 wood = tpWood(kind.y);
    float turn = hash(centre * 0.71 + 9.0) * 6.2832;
    float rings = sin(length(local - 0.25 * float2(cos(turn), sin(turn))) * 60.0 + 3.0 * noise(floorAt * 9.0));
    float streaks = sin(dot(local, float2(n.z, -n.x)) * 70.0 + 2.5 * noise(float2(at.y * 3.0, turn)));
    wood *= kind.y > 0.9 ? 1.0 : 0.9 + 0.1 * (top ? rings : streaks);

    // Low sun, with the shade of the taller posts next door.
    float shade = 1.0;
    float reach = 0.05;
    float2 unusedKind;
    float2 unusedCentre;
    [loop]
    for (int k = 0; k < 28; k++)
    {
        float post;
        float d = tpMap(at + n * 0.01 + sun * reach, slide, unusedKind, unusedCentre, post);
        shade = min(shade, saturate(post * 9.0 / reach + 0.25));
        reach += max(d, 0.03);
        if (shade < 0.3 || reach > 6.0) break;
    }

    float lit = saturate(dot(n, sun)) * smoothstep(0.25, 1.0, shade);
    float depth = top ? 1.0 : 0.45 + 0.55 * smoothstep(-0.9, 0.0, at.y - kind.x);
    float3 colour = wood * (float3(0.28, 0.3, 0.36) * depth + float3(1.25, 1.05, 0.82) * lit);
    colour = lerp(colour, sky, 1.0 - exp(-max(t - 6.0, 0.0) * 0.07));
    return colour * (0.94 + 0.08 * Level) * (1.0 - dot(uv - 0.5, uv - 0.5) * 0.4);
}
