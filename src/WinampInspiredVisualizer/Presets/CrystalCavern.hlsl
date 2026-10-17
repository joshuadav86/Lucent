// Name: Crystal Cavern
// Gliding through a winding geode whose walls bristle with glowing faceted crystals that ring with the music.
// Family: Fly-through
// Tags: Intense, Colourful, 3D

static const float Sectors = 11.0;        // large crystals around the cave
static const float RowLength = 0.7;       // spacing of crystal rows along the cave
static const float DruseAround = 48.0;    // small crystal points around the cave
static const float DruseSize = 0.23;      // their size along the cave

// Jewel tones: emerald, aqua, sapphire, amethyst, magenta, ruby and gold (never lime).
float3 crystalJewel(float t, float saturation, float value)
{
    return hsv2rgb(0.38 + 0.76 * abs(frac(t) * 2.0 - 1.0), saturation, value);
}

float crystalHash(float2 p)
{
    float3 q = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
    q += dot(q, q.yzx + 33.33);
    return frac((q.x + q.y) * q.z);
}

// The cave's centre line (xy) and radius (z): narrow passages open into wide chambers.
float3 crystalCave(float z)
{
    float wind = sin(z * 0.19);
    float slow = sin(z * 0.07);
    float rise = cos(z * 0.15);
    return float3(wind * 0.9 + slow * 0.5, rise * 0.7, 1.75 + 0.35 * wind * rise + 0.1 * slow);
}

// Nearest cell of a hexagonal grid: xy = offset from its centre, zw = the cell's id.
float4 crystalHexCell(float2 q)
{
    const float2 spacing = float2(1.0, 1.7320508);
    float4 centres = floor(float4(q, q - float2(0.5, 1.0)) / spacing.xyxy) + 0.5;
    float4 offsets = float4(q - centres.xy * spacing, q - (centres.zw + 0.5) * spacing);
    return dot(offsets.xy, offsets.xy) < dot(offsets.zw, offsets.zw)
         ? float4(offsets.xy, centres.xy) : float4(offsets.zw, centres.zw + 0.5);
}

// Hexagonal "radius" of p (x) and how far p is from the nearest ridge between two faces (y).
float2 crystalHexFaces(float2 p)
{
    p = abs(p);
    float slanted = p.x * 0.8660254 + p.y * 0.5;
    return float2(max(slanted, p.y), min(abs(slanted - p.y), p.y));
}

// Distance to the cave: rock lined with a carpet of small crystal points, plus large hexagonal
// crystals rooted in the wall pointing inward. info: x = the crystal's random seed, y = 1 on a
// large crystal and 0 on the lining, z = distance to the nearest facet edge, w = 0 at a crystal's
// root to 1 at its tip. On the lining z is instead the distance to the nearest large crystal.
float crystalDE(float3 pos, out float4 info)
{
    float3 cave = crystalCave(pos.z);
    float2 offset = pos.xy - cave.xy;
    float radius = length(offset);
    float wall = cave.z - radius;
    float grow = cave.z / 1.75;
    info = float4(0.0, 0.0, 1.0, 0.0);

    // In the open middle of the cave nothing can be closer than this.
    float open = wall - grow;
    if (open > 0.1)
    {
        return open;
    }

    float around = atan2(offset.y, offset.x + 1e-5) / (2.0 * PI) + 0.5;

    // The lining: hexagonal pyramids of random height packed across the rock.
    float4 hex = crystalHexCell(float2(around * DruseAround, pos.z / DruseSize));
    float druseSeed = crystalHash(float2(wrap(hex.z, DruseAround), hex.w));
    float2 druseFaces = crystalHexFaces(hex.xy);
    float rise = saturate(1.0 - druseFaces.x * 2.0);
    float lining = (wall - (0.3 + 0.7 * druseSeed) * rise * DruseSize * 0.8) * 0.7;

    // One large crystal per cell, kept inside its cell so only this one needs checking.
    float2 grid = float2(around * Sectors, pos.z / RowLength);
    float2 cell = floor(grid);
    float seed = crystalHash(cell + 0.37);
    float3 rnd = frac(seed * float3(17.13, 39.71, 71.29));
    float2 within = (grid - cell - 0.5) * float2(2.0 * PI / Sectors * radius, RowLength);
    float depth = wall + 0.05;
    float2 across = within - depth * (rnd.zx - 0.5) * float2(0.3, 0.35);
    float2 turn = normalize(frac(seed * float2(3.17, 7.73)) - 0.5 + 1e-4);    // a random twist of the facets
    across = float2(turn.x * across.x - turn.y * across.y, turn.y * across.x + turn.x * across.y);
    float lengthAlong = (0.25 + 0.55 * frac(seed * 5.71)) * grow;
    float width = 0.07 + 0.08 * frac(seed * 3.33);
    float2 faces = crystalHexFaces(across);
    float prism = faces.x - width;
    float tip = prism + (depth - lengthAlong) * 0.6;
    float crystal = max(max(prism, tip), -depth) * 0.75;
    crystal = rnd.y < 0.85 ? crystal : 1e3;     // a few cells have no large crystal

    // Never step past the cell's edge: the neighbouring crystal may be just beyond it.
    float2 halfCell = float2(PI / Sectors * radius, RowLength * 0.5);
    float edge = min(halfCell.x - abs(within.x), halfCell.y - abs(within.y));

    if (crystal < lining)
    {
        info = float4(seed, 1.0, min(faces.y, abs(depth - lengthAlong) * 0.5), saturate(depth / (lengthAlong + width * 1.6)));
    }
    else
    {
        info = float4(druseSeed, 0.0, crystal, rise);
    }
    return min(min(crystal, lining), max(edge, 0.0) + 0.03);
}

float3 Render(float2 uv, float2 p)
{
    float z = Time * 0.5 + (BeatCount - Beat) * 0.1;
    float2 drift = float2(sin(Time * 0.13), cos(Time * 0.11)) * 0.18;
    float3 eye = float3(crystalCave(z).xy + drift, z);
    float3 ahead = float3(crystalCave(z + 2.0).xy + drift * 0.3, z + 2.0);
    float3 dir = cameraRay(eye, ahead, p, 0.9 - Beat * 0.07, sin(Time * 0.1) * 0.5);

    const float farPlane = 12.0;
    int maxSteps = qualitySteps(80);
    float travelled = 0.0;
    float4 info = 0.0;
    bool landed = false;
    [loop]
    for (int i = 0; i < maxSteps; i++)
    {
        float d = crystalDE(eye + dir * travelled, info);
        if (d < 0.0015 * travelled)
        {
            landed = true;
            break;
        }
        travelled += d;
        if (travelled > farPlane)
        {
            break;
        }
    }

    float gem = z * 0.02 + Time * 0.012;
    float3 haze = crystalJewel(gem + 0.3, 0.7, 0.05) * (0.6 + Bass * 0.8);
    float fog = landed ? exp(-travelled * 0.14) : 0.0;
    float3 colour = haze;
    if (landed)
    {
        float3 hit = eye + dir * travelled;
        float4 info2 = 0.0;
        float e1 = 0.0004 + 0.001 * travelled;
        float2 e = float2(e1, -e1);
        float3 normal = normalize(e.xyy * crystalDE(hit + e.xyy, info2) + e.yyx * crystalDE(hit + e.yyx, info2)
                                + e.yxy * crystalDE(hit + e.yxy, info2) + e.xxx * crystalDE(hit + e.xxx, info2));
        float occlusion = saturate(0.35 + crystalDE(hit + normal * 0.15, info2) / 0.15 * 0.65);

        float isCrystal = info.y;
        float band = frac(info.x * 9.17);
        float energy = spectrum(band);

        // Each region of the cave has its own family of gem colours (with the odd contrasting
        // stone), and waves of colour flow along the walls.
        float accent = step(0.88, frac(info.x * 31.7));
        float hue = gem + (band - 0.5) * 0.16 + accent * 0.45 + sin(hit.z * 0.21 - Time * 0.35) * 0.07;
        float3 tint = crystalJewel(hue, 0.8, 1.0);

        // A lantern travels with us; each facet catches it differently and throws back a glint.
        float3 toLamp = eye + float3(0.0, -0.3, 0.4) - hit;
        float lampDist2 = dot(toLamp, toLamp);
        toLamp *= rsqrt(lampDist2);
        float lamp = 1.0 / (1.0 + lampDist2 * 0.3);
        float diffuse = saturate(dot(normal, toLamp));
        float facing = saturate(dot(normal, -dir));
        float3 bounce = reflect(dir, normal);
        float glint = pow(saturate(dot(bounce, toLamp)), 40.0);
        float fresnel = pow(1.0 - facing, 3.0);
        float3 mirrored = crystalJewel(hue + bounce.y * 0.25 + bounce.x * 0.15, 0.6, 1.0);

        // Light glows inside the crystals, strongest towards their tips, each tuned to a band.
        float tipLight = info.w * info.w;
        float3 inner = tint * (0.12 + energy * 1.8) * (0.6 + Bass * 0.7) * tipLight * (0.4 + isCrystal * 0.6);

        // Bright lines along every facet edge, kept about a pixel wide.
        float pixel = travelled * 1.2 * pixelSize() / 0.9;
        float edgeLine = saturate(1.0 - info.z / (pixel * 1.3)) * lerp(saturate(2.2 - travelled * 0.3), 1.0, isCrystal);

        // After each beat a wave of light rolls away down the cave through the crystals.
        float wave = glow(length(hit - eye) - (1.0 - Beat) * 7.0, 3.0) * Beat;

        // The lining is smoky quartz that sparkles; the large crystals glow like lanterns.
        float3 smoky = crystalJewel(hue + 0.1, 0.5, 0.5);
        float twinkle = pow(saturate(sin(Time * 2.5 + info.x * 60.0)), 16.0) * step(0.6, frac(info.x * 7.7)) * info.w
                      * pow(saturate(dot(bounce, toLamp)), 4.0);
        float3 lining = smoky * (diffuse * lamp * 0.6 + 0.02) * occlusion
                      + tint * (glint * lamp + twinkle * 1.6) * (0.6 + Treble * 3.0)
                      + crystalJewel(gem, 0.8, 1.0) * exp(-info.z * 7.0) * (0.12 + energy * 0.4) * occlusion;
        float3 crystal = tint * (diffuse * lamp * 0.5 + 0.02)
                       + tint * (0.2 + energy * 2.0) * (0.6 + Bass * 0.7) * (0.25 + 0.75 * tipLight)
                         * (0.7 + 0.5 * sin(info.w * 9.0 - Time * 5.0 + info.x * 40.0))
                       + tint * fresnel * 0.6 + mirrored * fresnel * 0.3
                       + glint * lamp * (0.8 + Treble * 2.5)
                       + tint * wave * 1.5;
        colour = lerp(lining, crystal, isCrystal) * lerp(occlusion, 1.0, 0.5)
               + lerp(tint, 1.0, 0.5) * edgeLine * (0.15 + lamp * 1.0 + energy * 0.8) * isCrystal;
        colour = lerp(haze, colour, fog);
    }
    return colour + previous(uv) * 0.08;
}
