// Name: Eclipse
// The moon slides in front of the sun, leaving a black disc ringed by a pearly corona whose long silken streamers
// stir with the music, while beads of sunlight flash along the edge on the beat.
// Family: Minimal
// Tags: Space, Minimal, Calm, Dark

// Fine silky streamer texture at an angle round the sun and a height above its edge.
float ecStreamers(float angle, float height, float drift)
{
    float s = fbm(float2(angle * 3.0 + 0.3 * sin(drift * 3.0), height * 1.2 - drift)) * 0.65
            + noise(float2(angle * 11.0, height * 2.0 - drift * 2.0)) * 0.35;
    return s;
}

// Prominence noise, angle only.
float ecFlames(float angle)
{
    return noise(float2(angle * 7.0, Time * 0.03));
}

float3 Render(float2 uv, float2 p)
{
    float px = pixelSize();

    // The sun stays put; the moon drifts slowly across it, never quite uncovering the bright face.
    float sunRadius = 0.3;
    float2 moonOffset = float2(0.012 * sin(Time * 0.05), 0.008 * sin(Time * 0.037 + 1.0));
    float moonRadius = sunRadius * 1.015;
    float2 q = p;
    float r = length(q);
    float angle = atan2(q.y, q.x);

    // Corona: soft radial streamers, brighter where the matching slice of the spectrum is loud.
    // Angle is folded so the spectrum reads round the disc in a mirror, low notes at the poles.
    float folded = abs(abs(angle) / PI - 0.5) * 2.0;
    float band = spectrum(0.03 + folded * 0.6);
    float height = max(r - sunRadius, 0.0);
    float drift = Time * 0.012;
    // Two copies of the angle with their seams on opposite sides, blended, so the texture wraps without a join.
    float angleB = wrap(angle, 2.0 * PI);
    float seamBlend = smoothstep(0.3, 0.7, abs(angle) / PI);
    float streamers = lerp(ecStreamers(angle, height, drift), ecStreamers(angleB, height, drift), seamBlend);
    streamers = pow(saturate(streamers), 2.0) * 2.0;
    // Equatorial streamers reach further than polar ones, like the real corona near solar minimum.
    float equator = 0.55 + 0.45 * pow(abs(cos(angle - 0.25)), 2.0);
    float reach = 0.05 + 0.2 * equator * (0.7 + band * 0.9 + Bass * 0.2);
    float falloff = exp(-height / reach);
    float inner = exp(-height / 0.025);
    float corona = falloff * (0.1 + streamers * (0.45 + band * 0.6)) + inner * 0.7;
    corona *= 1.0 - exp(-height / 0.004);

    // Pearly white with a cool edge and a warm heart.
    float3 pearl = lerp(float3(0.75, 0.82, 1.0), float3(1.0, 0.94, 0.86), inner);
    float3 colour = pearl * corona * (0.75 + Level * 0.3);

    // A faint outer haze and a few stars far behind.
    colour += float3(0.06, 0.08, 0.13) * exp(-r * 2.2) * 0.4;
    float2 starCell = floor(p * 90.0);
    float star = step(0.994, hash(starCell)) * smoothstep(0.35, 0.0, length(frac(p * 90.0) - 0.5));
    colour += float3(0.5, 0.55, 0.7) * star * (0.25 + 0.15 * sin(Time * 0.7 + hash(starCell + 3.0) * 30.0)) * smoothstep(0.5, 0.9, r);

    // Pink prominences peek over the limb in a few places.
    float prominence = smoothstep(0.62, 0.9, lerp(ecFlames(angle), ecFlames(angleB), seamBlend)) * exp(-height / 0.012);
    colour += float3(1.0, 0.25, 0.35) * prominence * (0.5 + Mid * 0.6) * step(sunRadius, r);

    // Baily's beads: sunlight through lunar valleys, flashing along the edge where the moon sits thinnest.
    float2 toMoon = q - moonOffset;
    float moonDistance = length(toMoon);
    float thinSide = atan2(-moonOffset.y, -moonOffset.x);
    // The moon itself: the deepest black, its edge crisp, faintly lit by earthshine.
    float moon = 1.0 - smoothstep(moonRadius - px, moonRadius + px, moonDistance);
    float3 earthshine = float3(0.006, 0.008, 0.012) * (1.0 - moonDistance / moonRadius * 0.5);
    colour = lerp(colour, earthshine, moon);

    // Beads are short arcs of light hugging the moon's edge, each one a valley on the lunar limb.
    float moonAngle = atan2(toMoon.y, toMoon.x);
    float rimHeight = moonDistance - moonRadius;
    float beads = 0.0;
    for (int i = 0; i < 5; i++)
    {
        float beadAngle = thinSide + (i - 2.0) * 0.13 + 0.03 * sin(Time * 0.3 + i);
        float gap = abs(wrap(moonAngle - beadAngle + PI, 2.0 * PI) - PI);
        float strength = 0.4 + 0.6 * hash(float2(i * 3.0, floor(BeatCount)));
        beads += exp(-gap * gap / 0.0012) * strength;
    }
    float hug = exp(-max(rimHeight, 0.0) / 0.008) * smoothstep(-px, px, rimHeight);
    colour += float3(1.0, 0.96, 0.88) * beads * hug * (Beat * 1.8 + Treble * 0.2);
    return colour;
}
