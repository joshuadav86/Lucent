// Name: Glass Prism
// A beam of white light splits through a floating glass prism into a rainbow, ray traced wavelength by wavelength; every colour of the spectrum glows with its own part of the music.
// Family: Ray traced
// Tags: 3D, Colourful, Dark

static const float PrismRadius = 0.78;   // centre to corner of the triangle
static const float PrismLength = 0.9;    // half its length along z
static const float3 PrismCentre = float3(0.0, 1.6, 0.0);
static const float SlitWidth = 0.45;     // half the width of the incoming sheet of light
static const int MaxWavelengths = 20;

static const float3 ScreenCentre = float3(3.2, 0.95, -0.4);
static const float3 ScreenNormal = float3(-0.857, 0.0, 0.514);
static const float2 ScreenSize = float2(1.5, 0.9);

// Materials.
static const int Nothing = 0;
static const int Floor = 1;
static const int Screen = 2;
static const int Lamp = 3;

float prismTurn()
{
    return 0.09 * sin(Time * 0.17) + 0.04 * sin(Time * 0.43);
}

// The prism's five faces, each an outward normal (xyz) and its distance from the origin (w); set once per pixel.
static float4 Faces[5];

void placePrism()
{
    float turn = prismTurn();
    [unroll]
    for (int i = 0; i < 3; i++)
    {
        float angle = turn + (30.0 + 120.0 * i) * PI / 180.0;
        float3 normal = float3(cos(angle), sin(angle), 0.0);
        Faces[i] = float4(normal, PrismRadius * 0.5 + dot(normal, PrismCentre));
    }
    Faces[3] = float4(0.0, 0.0, 1.0, PrismLength);
    Faces[4] = float4(0.0, 0.0, -1.0, PrismLength);
}

// Ray against the prism. Returns where the ray enters (x) and leaves (y), and those faces' normals.
float2 prismHit(float3 origin, float3 direction, out float3 enterNormal, out float3 leaveNormal)
{
    float enter = -1e5;
    float leave = 1e5;
    enterNormal = 0.0;
    leaveNormal = 0.0;
    [unroll]
    for (int i = 0; i < 5; i++)
    {
        float4 plane = Faces[i];
        float along = dot(plane.xyz, direction);
        float outside = dot(plane.xyz, origin) - plane.w;
        if (abs(along) < 1e-6)
        {
            if (outside > 0.0)
            {
                return float2(1.0, -1.0);
            }
        }
        else
        {
            float t = -outside / along;
            if (along < 0.0)
            {
                if (t > enter)
                {
                    enter = t;
                    enterNormal = plane.xyz;
                }
            }
            else if (t < leave)
            {
                leave = t;
                leaveNormal = plane.xyz;
            }
        }
    }
    return float2(enter, leave);
}

// How strongly glass bends each colour: x runs from red (0) to violet (1). Exaggerated so the rainbow fans out wide.
float glassIndex(float x)
{
    return 1.45 + 0.17 * x * (0.4 + 0.6 * x);
}

float3 spectralColour(float x)
{
    // Red, orange, yellow, green, cyan, blue, violet, with full brightness through the middle.
    float3 colour = hsv2rgb(x * 0.78, 1.0, 1.0);
    return colour * (0.6 + 0.4 * smoothstep(0.0, 0.3, x) * smoothstep(1.0, 0.7, x)) + 0.05;
}

// How bright each colour of the rainbow is: bass is red, treble is violet.
float bandLevel(float x)
{
    float s = spectrum(0.03 + x * 0.8) * 0.6 + spectrum(0.03 + x * 0.8 + 0.02) * 0.4;
    return 0.12 + saturate(s * 1.3);
}

// The lamp and its aim: it sweeps gently so the rainbow drifts across the screen.
float3 lampPosition()
{
    return float3(-3.4, 0.5 + 0.08 * sin(Time * 0.23), 0.0);
}

// The path of the light through the prism for one colour: xy where it leaves the prism, zw its direction after.
// Light inside stays in the xy plane because every side face of the prism is upright in z.
// Where the lamp's beam enters the prism (xyz) and the face it enters by.
static float3 BeamEntry;
static float3 BeamAim;
static float3 EntryNormal;

void aimBeam()
{
    float3 lamp = lampPosition();
    BeamAim = normalize(PrismCentre + float3(-0.36, 0.12, 0.0) - lamp);
    float3 leaveNormal;
    float2 span = prismHit(lamp, BeamAim, EntryNormal, leaveNormal);
    BeamEntry = lamp + BeamAim * span.x;
}

float4 beamExit(float x)
{
    float eta = glassIndex(x);
    float2 direction = refract(BeamAim, EntryNormal, 1.0 / eta).xy;
    float2 position = BeamEntry.xy;
    // Inside the glass, in the flat cross-section: find the side face the light reaches first.
    // If it meets it too steeply to get out, it reflects and tries again.
    [unroll]
    for (int bounce = 0; bounce < 3; bounce++)
    {
        float nearest = 1e5;
        float2 face = 0.0;
        [unroll]
        for (int i = 0; i < 3; i++)
        {
            float along = dot(Faces[i].xy, direction);
            float t = (Faces[i].w - dot(Faces[i].xy, position)) / along;
            if (along > 1e-4 && t > 1e-4 && t < nearest)
            {
                nearest = t;
                face = Faces[i].xy;
            }
        }
        position += direction * nearest;
        float2 outward = refract(float3(direction, 0.0), -float3(face, 0.0), eta).xy;
        if (dot(outward, outward) > 0.5)
        {
            direction = outward;
            break;
        }
        direction = reflect(direction, -face);
    }
    return float4(position, normalize(direction));
}

// The light's path, worked out once per pixel in Render: where each colour leaves the prism and its direction then.
static float4 ExitRays[MaxWavelengths];
static float4 MiddleExit;
static float Levels[MaxWavelengths];
static int Wavelengths;

// Distance between a ray and a line segment (x), and how far along the ray that closest point is (y).
float2 raySegment(float3 origin, float3 direction, float3 a, float3 b)
{
    float3 ba = b - a;
    float3 oa = origin - a;
    float d = dot(direction, ba);
    float denominator = dot(ba, ba) - d * d;
    float s = saturate((dot(oa, ba) - dot(oa, direction) * d) / max(denominator, 1e-6));
    float3 onSegment = a + ba * s;
    float t = max(dot(onSegment - origin, direction), 0.0);
    return float2(length(origin + direction * t - onSegment), t);
}

struct Hit
{
    float distance;
    float3 normal;
    int material;
};

Hit traceOpaque(float3 origin, float3 direction)
{
    Hit hit;
    hit.distance = 1e4;
    hit.normal = float3(0.0, 1.0, 0.0);
    hit.material = Nothing;
    if (direction.y < 0.0)
    {
        float t = -origin.y / direction.y;
        if (t > 1e-3)
        {
            hit.distance = t;
            hit.material = Floor;
        }
    }
    float facing = dot(direction, ScreenNormal);
    if (abs(facing) > 1e-4)
    {
        float t = dot(ScreenCentre - origin, ScreenNormal) / facing;
        float3 q = origin + direction * t - ScreenCentre;
        float3 across = normalize(cross(float3(0.0, 1.0, 0.0), ScreenNormal));
        float2 local = abs(float2(dot(q, across), q.y)) - ScreenSize;
        if (t > 1e-3 && t < hit.distance && max(local.x, local.y) < 0.0)
        {
            hit.distance = t;
            hit.normal = ScreenNormal * -sign(facing);
            hit.material = Screen;
        }
    }
    // The lamp: a small dark housing with a bright window facing the prism.
    float3 oc = origin - lampPosition();
    float b = dot(oc, direction);
    float h = b * b - dot(oc, oc) + 0.04;
    if (h > 0.0)
    {
        float t = -b - sqrt(h);
        if (t > 1e-3 && t < hit.distance)
        {
            hit.distance = t;
            hit.normal = normalize(oc + direction * t);
            hit.material = Lamp;
        }
    }
    return hit;
}

// The rainbow that lands on the screen (or anywhere in the fan): each colour lights a band whose
// width across the screen follows its loudness, like the bars of a spectrum analyser.
float3 fanLight(float3 position)
{
    float3 light = 0.0;
    [unroll]
    for (int i = 0; i < MaxWavelengths; i++)
    {
        if (i >= Wavelengths)
        {
            break;
        }
        float x = (i + 0.5) / Wavelengths;
        float4 exitRay = ExitRays[i];
        float2 offset = position.xy - exitRay.xy;
        float along = dot(offset, exitRay.zw);
        float apart = abs(offset.x * exitRay.w - offset.y * exitRay.z);
        float level = Levels[i];
        float width = SlitWidth * (0.25 + level * 0.9);
        // Each colour's beam spreads a little as it travels, so neighbouring colours blend.
        float thickness = 0.02 + 0.012 * along;
        float beam = exp(-apart * apart / (thickness * thickness)) * step(0.0, along);
        beam *= smoothstep(width + 0.02, width - 0.02, abs(position.z));
        light += spectralColour(x) * level * beam;
    }
    return light * 5.0 / Wavelengths;
}

// Haze that the light beams pass through, so the beams show in the air.
float haze(float3 position)
{
    float2 q = position.xy * 1.7 + float2(Time * 0.12, position.z * 1.3 - Time * 0.05);
    return 0.35 + 0.75 * noise(q) + 0.45 * noise(q * 2.3 + 5.0);
}

// Glow seen along a ray up to distance limit: the white beam from the lamp, the light inside the
// prism, and the coloured fan of light from the prism to the screen.
float3 beamGlow(float3 origin, float3 direction, float limit)
{
    float3 glow = 0.0;
    float intensity = 0.55 + Level * 0.9 + Beat * 0.4;
    float3 entry = BeamEntry;
    float4 middle = MiddleExit;

    // The white beam: a thin upright ribbon of light from the lamp to the prism, with a soft halo in the haze.
    float3 lamp = lampPosition();
    float2 aim = normalize(entry.xy - lamp.xy);
    float reach = length(entry.xy - lamp.xy);
    float3 normal = float3(-aim.y, aim.x, 0.0);
    float facing = dot(direction, normal);
    float t = dot(float3(lamp.xy, 0.0) - origin, normal) / facing;
    float3 c = origin + direction * t;
    float along = dot(c.xy - lamp.xy, aim);
    if (t > 0.0 && t < limit && along > 0.0 && along < reach)
    {
        float edge = smoothstep(SlitWidth, SlitWidth * 0.6, abs(c.z));
        glow += float3(1.0, 0.97, 0.92) * edge * min(0.05 / abs(facing), 1.2) * haze(c) * intensity * 0.6;
    }
    float2 nearest = raySegment(origin, direction, lamp, entry);
    glow += float3(0.8, 0.85, 1.0) * exp(-nearest.x * nearest.x * 40.0) * 0.12 * intensity * step(nearest.y, limit);

    // Inside the glass the light is still nearly white.
    float2 inside = normalize(middle.xy - entry.xy);
    normal = float3(-inside.y, inside.x, 0.0);
    facing = dot(direction, normal);
    t = dot(float3(entry.xy, 0.0) - origin, normal) / facing;
    c = origin + direction * t;
    along = dot(c.xy - entry.xy, inside);
    if (t > 0.0 && t < limit && along > 0.0 && along < length(middle.xy - entry.xy))
    {
        float edge = smoothstep(SlitWidth, SlitWidth * 0.6, abs(c.z));
        glow += float3(0.9, 0.95, 1.0) * edge * min(0.05 / abs(facing), 1.2) * intensity * 0.9;
    }

    // The rainbow fan: a ribbon of light for every colour, each as wide as that colour is loud,
    // ending where it lands on the screen. The haze is looked up once, where the middle colour crosses.
    normal = float3(-middle.w, middle.z, 0.0);
    t = dot(float3(middle.xy, 0.0) - origin, normal) / dot(direction, normal);
    float mist = haze(origin + direction * clamp(t, 0.0, limit));
    float3 fan = 0.0;
    [unroll]
    for (int i = 0; i < MaxWavelengths; i++)
    {
        if (i >= Wavelengths)
        {
            break;
        }
        float4 exitRay = ExitRays[i];
        normal = float3(-exitRay.w, exitRay.z, 0.0);
        facing = dot(direction, normal);
        t = dot(float3(exitRay.xy, 0.0) - origin, normal) / facing;
        c = origin + direction * t;
        along = dot(c.xy - exitRay.xy, exitRay.zw);
        float3 travel = float3(exitRay.zw, 0.0);
        float toScreen = dot(ScreenCentre - float3(exitRay.xy, c.z), ScreenNormal) / dot(travel, ScreenNormal);
        float level = Levels[i];
        float width = SlitWidth * (0.25 + level * 0.9);
        if (t > 0.0 && t < limit && along > 0.0 && along < toScreen && c.y > 0.0)
        {
            float edge = smoothstep(width, width * 0.6, abs(c.z));
            fan += spectralColour((i + 0.5) / Wavelengths) * level * edge * min(0.05 / abs(facing), 1.2);
        }
    }
    // Between the ribbons, a soft sheet of colour fills the whole fan, so it reads as one rainbow.
    float crossing = -origin.z / direction.z;
    if (crossing > 0.0 && crossing < limit)
    {
        float2 q = (origin + direction * crossing).xy - MiddleExit.xy;
        float2 red = ExitRays[0].zw;
        float2 violet = MiddleExit.zw * 2.0 - red;
        float spread = red.x * violet.y - red.y * violet.x;
        float x = (red.x * q.y - red.y * q.x) / (length(q) * spread + 1e-5);
        float reach = dot(ScreenCentre.xy - MiddleExit.xy, MiddleExit.zw);
        float inside = smoothstep(-0.05, 0.05, x) * smoothstep(1.05, 0.95, x) * step(0.0, dot(q, MiddleExit.zw));
        inside *= smoothstep(reach + 0.3, reach - 0.3, dot(q, MiddleExit.zw));
        float level = bandLevel(saturate(x));
        glow += spectralColour(saturate(x)) * level * inside * min(0.15 / abs(direction.z), 1.0) * mist * intensity * 3.0;
    }
    return glow + fan * mist * intensity * 7.0 / Wavelengths;
}

// What a ray sees if it misses the prism: the room, the screen with the rainbow on it, the lamp, and the beams in the air.
float3 shadeOpaque(float3 origin, float3 direction, bool withGlow, out Hit hit)
{
    hit = traceOpaque(origin, direction);
    float3 position = origin + direction * hit.distance;
    float3 colour = 0.0;
    if (hit.material == Nothing)
    {
        // A dark room with a faint glow high on the far wall.
        // A dark room, with a dim violet glow on the far wall behind the prism.
        float2 behind = direction.xy / max(-direction.z, 0.05) - float2(0.05, 0.25);
        colour = float3(0.006, 0.006, 0.01) + float3(0.03, 0.02, 0.06) * exp(-dot(behind, behind) * 1.5) * (0.6 + Level);
    }
    else if (hit.material == Screen)
    {
        float3 lit = fanLight(position);
        colour = float3(0.02, 0.02, 0.024) + lit * 0.9;
        // A thin pale edge round the screen.
        float3 q = position - ScreenCentre;
        float3 across = normalize(cross(float3(0.0, 1.0, 0.0), ScreenNormal));
        float2 local = abs(float2(dot(q, across), q.y)) - ScreenSize;
        colour += 0.05 * smoothstep(-0.03, 0.0, max(local.x, local.y));
    }
    else if (hit.material == Lamp)
    {
        float3 lamp = lampPosition();
        float3 aim = normalize(PrismCentre - lamp);
        float window = smoothstep(0.8, 0.95, dot(hit.normal, aim));
        colour = float3(0.03, 0.03, 0.035) * (0.5 + 0.5 * hit.normal.y) + float3(1.0, 0.97, 0.9) * window * 6.0;
    }
    else
    {
        // The floor: black, lit faintly by the rainbow spilling off the screen.
        float3 toScreen = ScreenCentre - position;
        float spill = exp(-length(toScreen) * 1.2) * 0.4;
        float pool = exp(-dot(position.xz - PrismCentre.xz, position.xz - PrismCentre.xz) * 0.25);
        colour = float3(0.006, 0.006, 0.008) + float3(0.02, 0.018, 0.03) * pool + fanLight(ScreenCentre + float3(0.0, position.y - 0.4, 0.0)) * spill * 0.15;
    }
    if (withGlow)
    {
        colour += beamGlow(origin, direction, hit.distance);
    }
    return colour;
}

// What a ray sees, letting it through the prism: each colour channel bends by its own amount, so
// edges seen through the glass split into colour.
float3 shadeRay(float3 origin, float3 direction)
{
    float3 enterNormal, leaveNormal;
    float2 span = prismHit(origin, direction, enterNormal, leaveNormal);
    Hit hit;
    float3 behind = shadeOpaque(origin, direction, true, hit);
    if (span.x > span.y || span.y < 0.0 || span.x > hit.distance || span.x < 0.0)
    {
        return behind;
    }

    float3 entry = origin + direction * span.x;
    float facing = saturate(dot(-direction, enterNormal));
    float fresnel = 0.04 + 0.96 * pow(1.0 - facing, 5.0);
    float3 colour = beamGlow(origin, direction, span.x);

    // The sheen on the glass: what the face reflects.
    float3 mirrored = reflect(direction, enterNormal);
    colour += fresnel * shadeOpaque(entry + enterNormal * 1e-3, mirrored, true, hit);
    // Bright edges where the faces meet catch the light.
    float edge = 0.0;
    [unroll]
    for (int i = 0; i < 5; i++)
    {
        float4 plane = Faces[i];
        float gap = abs(dot(plane.xyz, entry) - plane.w);
        edge = max(edge, smoothstep(0.02, 0.0, gap) * step(1e-4, gap));
    }
    colour += edge * float3(0.5, 0.55, 0.6) * (0.4 + Treble * 0.8);

    // Through the glass, one colour channel at a time.
    [unroll]
    for (int channel = 0; channel < 3; channel++)
    {
        float eta = glassIndex(channel == 0 ? 0.1 : (channel == 1 ? 0.5 : 0.9));
        float3 d = refract(direction, enterNormal, 1.0 / eta);
        float3 position = entry;
        float carried = 1.0 - fresnel;
        [unroll]
        for (int bounce = 0; bounce < 3; bounce++)
        {
            float2 inside = prismHit(position - d * 4.0, d, enterNormal, leaveNormal);
            position += d * (inside.y - 4.0);
            float3 outward = refract(d, -leaveNormal, eta);
            if (dot(outward, outward) > 0.5)
            {
                d = outward;
                carried *= 0.96;
                break;
            }
            d = reflect(d, -leaveNormal);
        }
        float3 seen = shadeOpaque(position + d * 2e-3, d, false, hit);
        colour[channel] += seen[channel] * carried;
        if (channel == 1)
        {
            // The beams seen through the glass are looked up once, along the middle (green) path.
            colour += beamGlow(position + d * 2e-3, d, hit.distance) * carried;
        }
    }
    return colour;
}

float3 Render(float2 uv, float2 p)
{
    // More colours on higher Detail settings, for a smoother rainbow.
    placePrism();
    aimBeam();
    Wavelengths = min(qualitySteps(13), MaxWavelengths);
    [unroll]
    for (int i = 0; i < MaxWavelengths; i++)
    {
        if (i >= Wavelengths)
        {
            break;
        }
        ExitRays[i] = beamExit((i + 0.5) / Wavelengths);
        Levels[i] = bandLevel((i + 0.5) / Wavelengths);
    }
    MiddleExit = beamExit(0.5);
    float swing = sin(Time * 0.05) * 0.35;
    float3 eye = float3(0.4 + sin(swing) * 5.6, 1.75 + 0.3 * sin(Time * 0.08), cos(swing) * 5.6);
    float3 target = float3(0.35, 1.15, 0.0);
    // Mirrored so the lamp is on the left, as the camera looks back along z.
    float3 direction = cameraRay(eye, target, -p, 1.95, 0.0);

    float3 colour;
    Hit hit = traceOpaque(eye, direction);
    float3 enterNormal, leaveNormal;
    float2 span = prismHit(eye, direction, enterNormal, leaveNormal);
    bool prismFirst = span.x < span.y && span.x > 0.0 && span.x < hit.distance;
    if (hit.material == Floor && !prismFirst)
    {
        // Polished black floor: a little of the light it gives back, the rest a mirror image.
        float3 position = eye + direction * hit.distance;
        float facing = saturate(-direction.y);
        float fresnel = 0.08 + 0.92 * pow(1.0 - facing, 5.0);
        Hit ignored;
        float3 base = shadeOpaque(eye, direction, true, ignored);
        float3 mirrored = reflect(direction, float3(0.0, 1.0, 0.0));
        colour = base + fresnel * shadeRay(position + float3(0.0, 1e-3, 0.0), mirrored) * 0.8;
    }
    else
    {
        colour = shadeRay(eye, direction);
    }

    // The lamp's glow in the haze.
    float3 toLamp = lampPosition() - eye;
    float along = dot(toLamp, direction);
    float off = length(toLamp - direction * along);
    colour += float3(1.0, 0.95, 0.85) * (exp(-off * 9.0) * 0.35 + exp(-off * 40.0) * 0.8) * (0.7 + Level * 0.6 + Beat * 0.3);
    return colour;
}
