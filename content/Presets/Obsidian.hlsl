// Name: Obsidian
// A broad band of wet black beads, twisted three half-turns and joined into a loop, seen so close that its near side
// fills the frame while its far side sinks into the dark. The beads stream slowly round the band and mirror the
// room; under them the band is molten, and its light shows in every gap. The music plays in that light, each
// stretch of the loop glowing with its own part of the music from dull red up to yellow-white, and each beat sends
// a pulse of heat round the loop both ways.
// Family: Ray traced
// Tags: 3D, Dark, Fire
// Added: 2026-11-16 08:37

static const float ObLoop = 1.0;        // how wide the loop is, from its middle to the middle of the band
static const float ObWide = 0.385;       // half the band's width
static const float ObThick = 0.085;     // half its thickness
static const float ObBead = 0.058;      // how big a bead is
static const float ObAlong = 64.0;      // how many beads lie along the loop

float obLoud(float x)
{
    return saturate((spectrum(x) - 0.42 + 0.5 * x) / 0.7);
}

// How loud the music is for one stretch of the loop, bass at the near side and treble at the far side, the same
// both ways round, read at ten fixed places and blended between them.
float obLoudAt(float round)
{
    float k = saturate(abs(round) / PI) * 9.0;
    float low = floor(k);
    return lerp(obLoud(0.08 + 0.06 * low), obLoud(0.14 + 0.06 * low), smoothstep(0.0, 1.0, k - low));
}

// Where a spot lies on the band: how far round the loop, how far across the band, and how far above its middle,
// the band turning over as it goes round.
float3 obOnBand(float3 p)
{
    float round = atan2(p.z, p.x + 1e-7);
    float2 cut = float2(length(p.xz) - ObLoop, p.y);
    float s, c;
    sincos(round * 1.5 + Time * 0.07, s, c);
    return float3(round, cut.x * c + cut.y * s, cut.y * c - cut.x * s);
}

// How far a spot is outside the band; also how far it is from the middle of its nearest bead.
float obBand(float3 p, out float fromBead)
{
    float3 on = obOnBand(p);
    float2 slab = abs(on.yz) - float2(ObWide - 0.07, ObThick - 0.07);
    float core = length(max(slab, 0.0)) + min(max(slab.x, slab.y), 0.0) - 0.07;

    // The beads sit in rows across both faces, each row set half a bead along from the last, and all of them
    // stream slowly round the loop.
    float pitch = 6.2832 * ObLoop / ObAlong;
    float along = (on.x / 6.2832 + frac(Time * 0.004)) * ObAlong;
    float row = floor(on.y / (pitch * 0.866) + 0.5);
    float rowAt = clamp(row, -4.0, 4.0);
    float inRow = along + 0.5 * wrap(rowAt, 2.0);
    float3 off = float3((inRow - floor(inRow + 0.5)) * pitch * (1.0 + (length(p.xz) - ObLoop) / ObLoop),
                        on.y - rowAt * pitch * 0.866,
                        abs(on.z) - ObThick + 0.006);
    fromBead = length(off);
    float beads = fromBead - ObBead;

    float h = max(0.012 - abs(core - beads), 0.0) / 0.012;
    return (min(core, beads) - h * h * 0.003) * 0.75;
}

float obBandOnly(float3 p)
{
    float unused;
    return obBand(p, unused);
}

float3 obNormal(float3 p)
{
    float2 h = float2(0.002, 0.0);
    return normalize(float3(obBandOnly(p + h.xyy) - obBandOnly(p - h.xyy),
                            obBandOnly(p + h.yxy) - obBandOnly(p - h.yxy),
                            obBandOnly(p + h.yyx) - obBandOnly(p - h.yyx)));
}

// The light of the molten band at one heat: dull red, through orange, to yellow-white.
float3 obHot(float heat)
{
    float3 c = lerp(float3(0.45, 0.02, 0.0), float3(1.5, 0.3, 0.02), smoothstep(0.05, 0.45, heat));
    c = lerp(c, float3(1.9, 0.95, 0.2), smoothstep(0.4, 0.8, heat));
    return lerp(c, float3(2.0, 1.7, 1.1), smoothstep(0.75, 1.15, heat)) * (0.2 + heat);
}

// The dim room the wet beads mirror: a long soft lamp overhead and a warm glow low to one side.
float3 obRoom(float3 dir)
{
    float lamp = smoothstep(0.5, 0.85, dir.y) * smoothstep(0.8, 0.2, abs(dir.x - 0.2));
    float low = smoothstep(0.75, 0.95, dot(dir, normalize(float3(-0.8, 0.1, -0.55))));
    return float3(0.85, 0.8, 0.75) * lamp + float3(0.45, 0.22, 0.1) * low + float3(0.02, 0.014, 0.01);
}

float3 Render(float2 uv, float2 p)
{
    float since = frac(BeatGlide);
    float flash = smoothstep(0.0, 0.22, since) * (1.0 - since) * (1.0 - since);

    float3 eye = float3(0.2 * sin(Time * 0.06), 0.5 + 0.07 * sin(Time * 0.045), -1.78);
    float3 ray = cameraRay(eye, float3(0.1, -0.02, -0.8), float2(p.x, -p.y), 1.25, 0.05 * sin(Time * 0.05));

    // The room beyond the band: brown dark, a little lighter low down.
    float3 beyond = lerp(float3(0.05, 0.028, 0.016), float3(0.012, 0.007, 0.006), smoothstep(-0.6, 0.7, ray.y));

    float t = 0.2;
    float struck = 0.0;
    float fromBead = 1.0;
    float nearest = 1e3;
    float nearestAt = 0.0;
    int steps = qualitySteps(100);
    [loop]
    for (int i = 0; i < steps; i++)
    {
        float d = obBand(eye + ray * t, fromBead);
        if (d < nearest)
        {
            nearest = d;
            nearestAt = t;
        }

        if (d < 0.0012)
        {
            struck = 1.0;
            break;
        }

        t += d;
        if (t > 4.6)
            break;
    }

    // The band's heat shows as a glow in the air close round it.
    float3 near3 = eye + ray * nearestAt;
    float roundNear = atan2(near3.z, near3.x + 1e-7);
    float pulseNear = flash * exp(-(abs(roundNear + 1.5708) - since * 3.3) * (abs(roundNear + 1.5708) - since * 3.3) * 5.0);
    float3 colour = beyond + obHot(0.12 + 0.6 * obLoudAt(roundNear + 1.5708) + 0.4 * pulseNear) * 0.06 * exp(-max(nearest, 0.0) * 16.0);
    if (struck > 0.5)
    {
        float3 at = eye + ray * t;
        float3 n = obNormal(at);
        float3 on = obOnBand(at);

        // How hot the band is here: its own stretch of the music, and the pulse each beat sends round from the
        // near side.
        float fromNear = abs(atan2(at.z, at.x + 1e-7) + 1.5708);
        fromNear = min(fromNear, 6.2832 - fromNear);
        float pulse = flash * exp(-(fromNear - since * 3.3) * (fromNear - since * 3.3) * 5.0);
        float heat = 0.14 + 0.55 * obLoudAt(fromNear) + 0.27 * pulse;

        // Between the beads the molten band shows; the beads themselves are black and wet, mirroring the room, with
        // the band's light caught round their feet.
        float gap = smoothstep(ObBead * 1.02, ObBead * 1.1, fromBead);
        float lift = (abs(on.z) - ObThick) / ObBead;
        float foot = 1.0 - smoothstep(0.1, 0.75, lift);
        float facing = saturate(-dot(ray, n));
        float mirror = 0.06 + 0.94 * pow(1.0 - facing, 5.0);
        float3 bead = float3(0.012, 0.011, 0.012) + obRoom(reflect(ray, n)) * (mirror + 0.12) + obHot(heat) * (0.75 * foot * foot * foot + 0.01);
        float3 molten = obHot(heat * (0.75 + 0.35 * noise(float2(on.x * 40.0, on.y * 30.0))));
        colour = lerp(bead, molten, gap);

        // The far side of the loop sinks into the dark.
        colour = lerp(colour, beyond, smoothstep(1.7, 3.3, t));
    }

    return colour * (1.0 - 0.16 * dot(p, p) / (1.0 + Aspect * Aspect));
}
