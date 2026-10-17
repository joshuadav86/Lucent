// Name: Plasma Orb
// Forking tendrils of plasma crawl from a glowing heart to the inside of a glass globe, each one carrying a band of the spectrum and flaring on the beat.
// Family: Music-shaped
// Tags: Dark, Neon

static const float GlobeRadius = 0.78;
static const int Tendrils = 14;

float plasmaHash(float n)
{
    return frac(sin(n * 91.345 + 7.13) * 47453.5453);
}

// Smooth wandering value in -1..1 for seed s at time t.
float plasmaWander(float s, float t)
{
    return sin(t * (0.31 + 0.17 * plasmaHash(s)) + s * 5.1) * 0.6 + sin(t * (0.53 + 0.2 * plasmaHash(s + 3.0)) + s * 2.3) * 0.4;
}

float3 Render(float2 uv, float2 p)
{
    float px = pixelSize();
    float r = length(p);
    float angle = atan2(p.y, p.x);

    float3 colour = 0.0;
    float coreSize = 0.07 + Bass * 0.025 + Beat * 0.02;

    if (r < GlobeRadius * 1.02)
    {
        [loop]
        for (int k = 0; k < Tendrils; k++)
        {
            float fk = k;
            float seed = fk * 1.618;
            // Where the tendril touches the glass, wandering over the globe. Tendrils that point
            // towards or away from us look shorter.
            float heading = fk / Tendrils * 2.0 * PI + plasmaWander(seed, Time) * 1.2;
            float tiltOut = 0.25 + 0.75 * abs(plasmaWander(seed + 11.0, Time * 0.7));
            float reach = GlobeRadius * tiltOut;
            // Each tendril carries one band of the spectrum.
            float band = spectrum(0.04 + fk / Tendrils * 0.8);
            float energy = band * band * 1.6 + Beat * 0.4;
            // The tendril's path: it writhes more the further it gets from the heart.
            float along = saturate((r - coreSize * 0.5) / max(reach - coreSize * 0.5, 0.01));
            float writhe = sin(along * (5.0 + 3.0 * plasmaHash(seed)) - Time * (3.0 + 2.0 * plasmaHash(seed + 5.0)) + seed)
                         * (0.12 + 0.12 * Treble) * along;
            // A fine, fast crackle on top of the slow writhing.
            float crackle = sin(along * 13.0 + Time * 9.0 + seed * 3.0) * sin(along * 21.0 - Time * 11.0) * 0.012 * along;
            float pathAngle = heading + writhe + crackle;
            float diff = angle - pathAngle;
            diff -= floor(diff / (2.0 * PI) + 0.5) * 2.0 * PI;
            float across = abs(diff) * r;
            // Near the glass the tendril splits in two.
            float forkAt = 0.62;
            float split = max(along - forkAt, 0.0) * (0.35 + 0.3 * plasmaHash(seed + 9.0));
            // The two branches bend apart: one each side of the path.
            float forkDiff = diff - split;
            forkDiff -= floor(forkDiff / (2.0 * PI) + 0.5) * 2.0 * PI;
            float mainDiff = diff + split;
            mainDiff -= floor(mainDiff / (2.0 * PI) + 0.5) * 2.0 * PI;
            across = abs(mainDiff) * r;
            float acrossFork = abs(forkDiff) * r;
            float width = px * (0.8 + 1.6 * energy * along) + 0.002;
            float inReach = smoothstep(reach + 0.02, reach - 0.01, r) * step(coreSize * 0.3, r);
            float strand = (exp(-across * across / (width * width)) + exp(-acrossFork * acrossFork / (width * width)) * step(forkAt, along) * 0.8) * inReach;
            float haze = (exp(-across / (0.015 + 0.03 * energy)) + exp(-acrossFork / (0.015 + 0.03 * energy)) * step(forkAt, along) * 0.6) * inReach;
            float3 hue = 0.5 + 0.5 * cos(2.0 * PI * (fk * 0.07 + along * 0.25 + Time * 0.03 + float3(0.55, 0.75, 0.95)));
            hue = hue * hue + 0.05;
            float flicker = 0.75 + 0.25 * sin(Time * 37.0 + seed * 13.0);
            colour += (lerp(hue, 1.0, 0.55) * strand * 1.2 + hue * haze * 0.2) * (0.25 + energy) * flicker;
            // Where it touches the glass it spreads into a bright spot.
            float2 tipAt = float2(cos(pathAngle + split), sin(pathAngle + split)) * reach;
            float tip = exp(-dot(p - tipAt, p - tipAt) / (0.0008 + 0.002 * energy));
            colour += lerp(hue, 1.0, 0.3) * tip * (0.3 + energy) * smoothstep(0.75, 0.95, tiltOut);
        }
    }

    // The glowing heart.
    colour += lerp(float3(1.0, 0.75, 1.0), 1.0, 0.4) * exp(-r * r / (coreSize * coreSize)) * (1.0 + Beat);
    colour += float3(0.7, 0.3, 1.0) * exp(-r / 0.25) * 0.15 * (0.6 + Level);

    // The glass: a thin bright rim and a soft reflection.
    float rim = exp(-abs(r - GlobeRadius) / (px * 1.2));
    float inner = smoothstep(GlobeRadius * 0.82, GlobeRadius, r) * step(r, GlobeRadius);
    float litSide = 0.5 + 0.5 * cos(angle + 2.2);
    colour += lerp(float3(0.35, 0.3, 0.8), float3(0.9, 0.8, 1.0), litSide) * (rim * (0.15 + 0.35 * litSide) + inner * inner * 0.05);
    // A curved window reflection on the glass.
    float arc = exp(-abs(r - GlobeRadius * 0.86) / 0.012) * smoothstep(0.5, 0.0, abs(angle + 2.3)) * 0.12;
    colour += float3(0.85, 0.9, 1.0) * arc;
    colour += float3(0.25, 0.1, 0.4) * smoothstep(GlobeRadius, GlobeRadius * 0.6, r) * 0.04;
    return colour;
}
