// Name: Chain Mail
// A sheet of mail seen close and from above: flat steel rings joined by upright links, with diamonds of brass rings
// worked through it, rolling in slow swells like cloth on water. Light glows up from beneath it and shines through
// every ring. The music plays in that light, bass at the left and treble at the right, blue where it is quiet and
// white where it is loud, and each beat sends a ring of light spreading out under the mail.
// Family: Ray traced
// Tags: 3D, Dark, Calm
// Added: 2026-11-16 05:05

static const float CmCell = 0.2;        // from one flat ring to the next
static const float CmDeep = 0.5;        // how far beneath the mail the glowing floor lies

float cmLoud(float x)
{
    return saturate((spectrum(x) - 0.42 + 0.5 * x) / 0.7);
}

// How loud the music is at one place across the sheet, bass at the left and treble at the right, read at ten
// fixed places and blended between them.
float cmLoudAt(float across)
{
    float k = saturate(0.5 + 0.5 * across) * 9.0;
    float low = floor(k);
    return lerp(cmLoud(0.08 + 0.06 * low), cmLoud(0.14 + 0.06 * low), smoothstep(0.0, 1.0, k - low));
}

// How high the sheet lies at each place as its swells roll through.
float cmHeight(float2 at)
{
    return 0.13 * sin(at.x * 1.1 + Time * 0.31 + 0.8 * sin(at.y * 0.7 - Time * 0.2))
         + 0.08 * sin(at.y * 1.7 + Time * 0.27 + at.x * 0.6);
}

// How far a spot is outside the metal; which says whether the nearest ring is a flat one (1) or a link (0).
float cmMail(float3 p, out float flatRing)
{
    float3 u = float3(p.x, p.y - cmHeight(p.xz), p.z) / CmCell;
    float2 whole = floor(u.xz + 0.5);
    float2 between = floor(u.xz) + 0.5;

    float3 a = float3(u.x - whole.x, u.y, u.z - whole.y);
    float lying = length(float2(length(a.xz) - 0.36, a.y)) - 0.068;
    float3 b = float3(u.x - between.x, u.y, u.z - whole.y);
    float linkAcross = length(float2(length(b.xy) - 0.3, b.z)) - 0.056;
    float3 c = float3(u.x - whole.x, u.y, u.z - between.y);
    float linkAlong = length(float2(length(c.zy) - 0.3, c.x)) - 0.056;

    float links = min(linkAcross, linkAlong);
    flatRing = step(lying, links);
    return min(lying, links) * CmCell * 0.8;
}

float cmMailOnly(float3 p)
{
    float unused;
    return cmMail(p, unused);
}

float3 cmNormal(float3 p)
{
    float2 h = float2(0.0015, 0.0);
    return normalize(float3(cmMailOnly(p + h.xyy) - cmMailOnly(p - h.xyy),
                            cmMailOnly(p + h.yxy) - cmMailOnly(p - h.yxy),
                            cmMailOnly(p + h.yyx) - cmMailOnly(p - h.yyx)));
}

// The light glowing on the floor beneath the mail at one place.
float3 cmGlow(float2 at, float since, float flash)
{
    float loud = cmLoudAt(at.x / (1.2 * max(Aspect, 1.0)));
    float cloud = fbm(at * 0.9 + float2(Time * 0.05, -Time * 0.04));
    float ring = length(at - float2(0.0, 0.6)) - since * 3.4;
    float heat = 0.05 + 0.3 * cloud * cloud + (0.1 + 0.42 * cloud) * loud + 0.26 * flash * exp(-ring * ring * 5.0);
    float3 deep = lerp(float3(0.03, 0.05, 0.42), float3(0.22, 0.03, 0.4), 0.5 + 0.5 * sin(at.x * 0.8 + at.y * 0.5 + Time * 0.07));
    float3 glow = lerp(deep, float3(0.05, 0.5, 1.25), smoothstep(0.15, 0.6, heat));
    return lerp(glow, float3(1.1, 1.3, 1.45), smoothstep(0.55, 1.25, heat)) * (0.12 + heat);
}

// The dark room the metal mirrors: one long soft lamp overhead and a dim window to the side.
float3 cmRoom(float3 dir)
{
    float lamp = smoothstep(0.35, 0.85, dir.y) * smoothstep(0.9, 0.2, abs(dir.x + 0.25));
    float window = smoothstep(0.7, 0.95, dot(dir, normalize(float3(0.8, 0.35, 0.5))));
    return float3(1.15, 0.95, 0.72) * lamp + float3(0.3, 0.4, 0.62) * window + float3(0.05, 0.055, 0.07) * (0.3 + 0.7 * dir.y);
}

float3 Render(float2 uv, float2 p)
{
    float since = frac(BeatGlide);
    float flash = smoothstep(0.0, 0.1, since) * (1.0 - since) * (1.0 - since);

    float3 eye = float3(0.3 * sin(Time * 0.05), 1.3, -0.95 + 0.1 * sin(Time * 0.037));
    float3 ray = cameraRay(eye, float3(0.0, 0.0, 0.05), float2(p.x, -p.y), 1.6, 0.0);

    // Start where the ray first comes near the sheet, and walk it down through the rings.
    float t = (eye.y - 0.3) / max(-ray.y, 0.05);
    float tEnd = (eye.y + 0.3) / max(-ray.y, 0.05);
    float struck = 0.0;
    float flatRing = 0.0;
    int steps = qualitySteps(90);
    [loop]
    for (int i = 0; i < steps; i++)
    {
        float d = cmMail(eye + ray * t, flatRing);
        if (d < 0.0012)
        {
            struck = 1.0;
            break;
        }

        t += d;
        if (t > tEnd)
            break;
    }

    // Whatever the ray does not strike, it sees the glowing floor through.
    float tFloor = (eye.y + CmDeep) / max(-ray.y, 0.05);
    float3 under = cmGlow((eye + ray * tFloor).xz, since, flash);
    float3 colour = under;
    if (struck > 0.5)
    {
        float3 at = eye + ray * t;
        float3 n = cmNormal(at);
        float3 glow = cmGlow(at.xz, since, flash);

        // Brass rings stand in diamonds among the steel.
        float2 whole = floor(at.xz / CmCell + 0.5);
        float2 inDiamond = abs(wrap(whole, 12.0) - 6.0);
        float rung = inDiamond.x + inDiamond.y;
        float brass = flatRing * step(abs(rung - 4.0), 0.5) + flatRing * step(rung, 0.5);
        float3 metal = lerp(float3(0.78, 0.8, 0.84), float3(1.15, 0.7, 0.2), brass);

        // How open the spot is among its neighbours.
        float open = saturate(0.35 + cmMailOnly(at + n * 0.03) / 0.045);

        // The metal mirrors the room above and the glow beneath, and catches the glow on every face turned down.
        float3 bounce = reflect(ray, n);
        float3 seen = bounce.y > 0.0 ? cmRoom(bounce) * open : glow * 0.8;
        float facing = saturate(-dot(ray, n));
        float3 lit = metal * (seen * (0.75 + 0.25 * pow(1.0 - facing, 3.0)) + float3(0.2, 0.17, 0.14) * saturate(0.3 + 0.7 * n.y) * open);
        lit += metal * glow * (0.3 * saturate(-n.y) + 0.06);
        colour = lit;
    }

    // The far end of the sheet sinks into the dark.
    float far = struck > 0.5 ? t : tFloor;
    colour = lerp(colour, float3(0.004, 0.008, 0.02), smoothstep(2.6, 4.6, far));
    return colour * (1.0 - 0.14 * dot(p, p) / (1.0 + Aspect * Aspect));
}
