// Name: Marble Run
// Glass marbles roll down a chrome track spiralling round a mirror-bright column, each one bending and reflecting everything around it and casting soft shadows and bright focused spots of light on a polished walnut base; their glowing hearts pulse with the music.
// Family: Ray traced
// Tags: Colourful, Geometric, 3D

// The track is a helix of two chrome rails held out from the column by thin spokes. Marbles
// roll down it, vanish through a hole in the base and come back in at the top, out of sight.
// The rails are drawn by marching a distance function; everything else (the marbles, the
// column, the base) is hit exactly.
static const float TrackRadius = 0.9;
static const float Pitch = 0.72;           // drop per turn
static const float RailGap = 0.11;         // rails sit this far either side of the marbles' path
static const float RailDrop = 0.13;        // and this far below the marbles' centres
static const float RailRadius = 0.02;
static const float Spokes = 6.0;
static const float PillarRadius = 0.26;
static const float MarbleRadius = 0.15;
static const float EndHeight = -0.3;       // the marbles' path ends here, under the base
static const float EndAngle = 0.0;
static const float Turns = 8.0;
static const int MarbleCount = 7;
static const float BaseRadius = 2.1;
static const float BaseDepth = 0.16;

// Materials.
static const int Sky = 0;
static const int Chrome = 1;
static const int Glass = 2;
static const int Wood = 3;
static const int Hole = 4;
static const int Floor = 5;

struct Hit
{
    float t;
    float3 normal;
    int material;
    int marble;
};

float helixHeight(float angle)
{
    return EndHeight + (EndAngle - angle) * Pitch / (2.0 * PI);
}

float marblePhase(int k)
{
    // How far (in turns of the helix) a marble still has to roll; marbles keep a steady spacing.
    return Turns * (1.0 - frac(Time * 0.03 + k / (float)MarbleCount + 0.05 * sin(k * 2.3)));
}

static float4 Marbles[MarbleCount];
static float3 MarbleColours[MarbleCount];
static float MarbleGlow[MarbleCount];

void placeMarbles()
{
    [unroll]
    for (int k = 0; k < MarbleCount; k++)
    {
        float turns = marblePhase(k);
        float angle = EndAngle - turns * 2.0 * PI;
        float3 centre = float3(cos(angle) * TrackRadius, helixHeight(angle), sin(angle) * TrackRadius);
        Marbles[k] = float4(centre, MarbleRadius);
        MarbleColours[k] = hsv2rgb(0.07 + k * 0.143 + 0.03 * sin(k * 1.7), 0.8, 1.0);
        // Each marble's heart glows with its own band of the spectrum.
        MarbleGlow[k] = 0.4 + spectrum(0.04 + k * 0.13) * 2.4 + Beat * 0.5;
    }
}

float trackDistance(float3 q)
{
    float r = length(q.xz);
    float angle = atan2(q.z, q.x);
    float dy = wrap(q.y - helixHeight(angle) + Pitch * 0.5, Pitch) - Pitch * 0.5;
    float2 c = float2(r - TrackRadius, dy);
    float rails = min(length(c - float2(RailGap, -RailDrop)), length(c - float2(-RailGap, -RailDrop))) - RailRadius;

    // Spokes from the column out under the rails, six to a turn.
    float sector = 2.0 * PI / Spokes;
    float spokeAngle = round(angle / sector) * sector;
    float2 radial = float2(cos(spokeAngle), sin(spokeAngle));
    float along = dot(q.xz, radial);
    float sideways = dot(q.xz, float2(-radial.y, radial.x));
    float sy = wrap(q.y - helixHeight(spokeAngle) + Pitch * 0.5, Pitch) - Pitch * 0.5 + RailDrop + RailRadius + 0.008;
    float spoke = length(float3(along - clamp(along, PillarRadius - 0.02, TrackRadius + RailGap), sy, sideways)) - 0.013;

    float d = min(rails, spoke);
    return max(d, -(q.y - EndHeight + 0.15));
}

float3 trackNormal(float3 q)
{
    const float e = 0.0015;
    return normalize(float3(
        trackDistance(q + float3(e, 0, 0)) - trackDistance(q - float3(e, 0, 0)),
        trackDistance(q + float3(0, e, 0)) - trackDistance(q - float3(0, e, 0)),
        trackDistance(q + float3(0, 0, e)) - trackDistance(q - float3(0, 0, e))));
}

// Where a ray enters and leaves an upright cylinder round the y axis (x, y), or -1s.
float2 cylinderSpan(float3 origin, float3 direction, float radius)
{
    float a = dot(direction.xz, direction.xz);
    float b = dot(origin.xz, direction.xz);
    float c = dot(origin.xz, origin.xz) - radius * radius;
    float h = b * b - a * c;
    if (h < 0.0 || a < 1e-6)
    {
        return float2(-1.0, -1.0);
    }
    h = sqrt(h);
    return float2((-b - h) / a, (-b + h) / a);
}

float sphereHit(float3 origin, float3 direction, float4 sphere)
{
    float3 oc = origin - sphere.xyz;
    float b = dot(oc, direction);
    float h = b * b - dot(oc, oc) + sphere.w * sphere.w;
    if (h < 0.0)
    {
        return -1.0;
    }
    float t = -b - sqrt(h);
    return t > 1e-3 ? t : -1.0;
}

float3 holeCentre()
{
    float angle = EndAngle - (0.0 - EndHeight) * 2.0 * PI / Pitch;
    return float3(cos(angle) * TrackRadius, 0.0, sin(angle) * TrackRadius);
}

Hit intersect(float3 origin, float3 direction, bool withTrack)
{
    Hit hit;
    hit.t = 1e4;
    hit.normal = float3(0.0, 1.0, 0.0);
    hit.material = Sky;
    hit.marble = -1;

    // The polished base, with a hole where the track dives through, and a dark floor below.
    if (direction.y < 0.0)
    {
        float t = -origin.y / direction.y;
        float3 q = origin + direction * t;
        float fromHole = length(q.xz - holeCentre().xz);
        if (t > 1e-3 && length(q.xz) < BaseRadius && fromHole > 0.2)
        {
            hit.t = t;
            hit.material = Wood;
        }
        float tFloor = (-BaseDepth - origin.y) / direction.y;
        if (tFloor > 1e-3 && tFloor < hit.t)
        {
            float3 f = origin + direction * tFloor;
            if (length(f.xz) > BaseRadius)
            {
                hit.t = tFloor;
                hit.material = Floor;
            }
            else if (length(f.xz - holeCentre().xz) < 0.2)
            {
                hit.t = tFloor;
                hit.material = Hole;
            }
        }
    }
    float2 rim = cylinderSpan(origin, direction, BaseRadius);
    if (rim.x > 1e-3 && rim.x < hit.t)
    {
        float y = origin.y + direction.y * rim.x;
        if (y < 0.0 && y > -BaseDepth)
        {
            hit.t = rim.x;
            float3 q = origin + direction * rim.x;
            hit.normal = float3(q.x, 0.0, q.z) / BaseRadius;
            hit.material = Wood;
        }
    }

    // The column.
    float2 pillar = cylinderSpan(origin, direction, PillarRadius);
    if (pillar.x > 1e-3 && pillar.x < hit.t && origin.y + direction.y * pillar.x > -0.01)
    {
        hit.t = pillar.x;
        float3 q = origin + direction * pillar.x;
        hit.normal = float3(q.x, 0.0, q.z) / PillarRadius;
        hit.material = Chrome;
    }

    [unroll]
    for (int k = 0; k < MarbleCount; k++)
    {
        float t = sphereHit(origin, direction, Marbles[k]);
        if (t > 0.0 && t < hit.t)
        {
            hit.t = t;
            hit.normal = (origin + direction * t - Marbles[k].xyz) / MarbleRadius;
            hit.material = Glass;
            hit.marble = k;
        }
    }

    // The rails: march only through the ring of space they can be in.
    if (withTrack)
    {
        float2 span = cylinderSpan(origin, direction, TrackRadius + RailGap + RailRadius + 0.01);
        float start = max(span.x, 1e-3);
        float stop = min(span.y, hit.t);
        if (span.y > 0.0 && start < stop)
        {
            float t = start;
            int steps = qualitySteps(72);
            [loop]
            for (int i = 0; i < steps; i++)
            {
                float3 q = origin + direction * t;
                float d = trackDistance(q);
                if (d < 4e-4 * surfacePrecision() * (1.0 + t))
                {
                    hit.t = t;
                    hit.normal = trackNormal(q);
                    hit.material = Chrome;
                    hit.marble = -1;
                    break;
                }
                t += d * 0.85;
                if (t > stop)
                {
                    break;
                }
            }
        }
    }
    return hit;
}

// A dark studio: a big warm softbox overhead, a cool strip light to one side, a dim floor glow.
float3 environment(float3 direction)
{
    float3 colour = lerp(float3(0.01, 0.009, 0.008), float3(0.025, 0.027, 0.035), saturate(direction.y * 0.7 + 0.4));
    float3 box = direction / max(abs(direction.y), 1e-3);
    if (direction.y > 0.0)
    {
        float2 q = abs(box.xz - float2(0.6, -0.5)) - float2(0.9, 0.55);
        colour += float3(1.0, 0.92, 0.82) * 3.2 * smoothstep(0.08, -0.05, max(q.x, q.y));
    }
    float strip = smoothstep(0.06, 0.0, abs(direction.y - 0.25)) * smoothstep(0.3, 0.6, dot(normalize(direction.xz), normalize(float2(-1.0, 0.4))));
    colour += float3(0.45, 0.65, 1.0) * strip * (0.8 + Treble * 1.5);
    // Tall softboxes standing round the set, for the chrome to catch.
    float azimuth = atan2(direction.z, direction.x);
    float panel = pow(saturate(cos(azimuth * 3.0 + 0.8)), 24.0) * smoothstep(0.65, 0.4, abs(direction.y - 0.15));
    colour += lerp(float3(1.0, 0.85, 0.7), float3(0.7, 0.8, 1.0), step(0.0, sin(azimuth * 1.5 + 0.4))) * panel * 1.4;
    float hue = Time * 0.02;
    colour += hsv2rgb(hue + 0.6, 0.6, 1.0) * 0.05 * exp(-abs(direction.y - 0.05) * 6.0) * (0.5 + Level);
    return colour;
}

// Seen straight on, the studio is a dark backdrop with a soft wash of colour on the far wall
// that drifts through the hues and swells with the music; the lights only show in reflections.
float3 backdrop(float3 direction)
{
    float3 colour = lerp(float3(0.008, 0.008, 0.01), float3(0.03, 0.028, 0.03), saturate(direction.y * 0.8 + 0.5));
    float wash = exp(-pow(direction.y - 0.05, 2.0) * 5.0);
    float3 washColour = lerp(hsv2rgb(Time * 0.015 + 0.55, 0.6, 1.0), hsv2rgb(Time * 0.015 + 0.85, 0.6, 1.0), saturate(direction.x * 0.5 + 0.5));
    return colour + washColour * wash * (0.03 + Level * 0.05 + Bass * 0.03);
}

static const float3 KeyLight = float3(1.8, 6.0, -1.5);

float trackShadow(float3 origin, float3 direction)
{
    float2 span = cylinderSpan(origin, direction, TrackRadius + RailGap + RailRadius + 0.01);
    if (span.y < 0.0)
    {
        return 1.0;
    }
    float t = max(span.x, 0.01);
    float shade = 1.0;
    [loop]
    for (int i = 0; i < 20; i++)
    {
        float d = trackDistance(origin + direction * t);
        shade = min(shade, 10.0 * d / t);
        t += clamp(d, 0.02, 0.3);
        if (shade < 0.01 || t > span.y)
        {
            break;
        }
    }
    return saturate(shade);
}

float sphereShadow(float3 origin, float3 direction, float4 sphere, float softness)
{
    float3 oc = origin - sphere.xyz;
    float b = dot(oc, direction);
    float c = dot(oc, oc) - sphere.w * sphere.w;
    float h = b * b - c;
    float d = sqrt(max(0.0, sphere.w * sphere.w - h)) - sphere.w;
    float t = -b - sqrt(max(h, 0.0));
    return (t < 0.0) ? 1.0 : smoothstep(0.0, 1.0, 2.5 * softness * d / t);
}

// Light falling on the walnut: the key light (shadowed by marbles, column and rails, with bright
// spots where each glass marble focuses it), plus the coloured glow of the marbles' hearts.
float3 woodLight(float3 position, float3 normal)
{
    float3 toKey = KeyLight - position;
    float keyDistance = length(toKey);
    float3 keyDirection = toKey / keyDistance;
    float shadow = trackShadow(position + normal * 0.002, keyDirection);
    float3 caustics = 0.0;
    [unroll]
    for (int k = 0; k < MarbleCount; k++)
    {
        float s = sphereShadow(position, keyDirection, Marbles[k], 8.0);
        shadow *= lerp(0.35, 1.0, s);
        // A glass ball focuses light into a small bright spot in the middle of its shadow.
        float3 oc = Marbles[k].xyz - position;
        float along = dot(oc, keyDirection);
        float off = length(oc - keyDirection * along);
        caustics += MarbleColours[k] * exp(-off * off * 900.0) * step(0.0, along) * 3.0;
    }
    // The column's shadow.
    float2 toAxis = -position.xz;
    float2 flat = normalize(keyDirection.xz);
    float alongFlat = dot(toAxis, flat);
    float offAxis = length(toAxis - flat * alongFlat);
    shadow *= alongFlat > 0.0 ? lerp(0.1, 1.0, smoothstep(PillarRadius - 0.04, PillarRadius + 0.05 + alongFlat * 0.08, offAxis)) : 1.0;

    float3 colour = float3(1.0, 0.92, 0.82) * 3.0 * saturate(dot(normal, keyDirection)) * (shadow + caustics) * 20.0 / (keyDistance * keyDistance);
    [unroll]
    for (int j = 0; j < MarbleCount; j++)
    {
        float3 toMarble = Marbles[j].xyz - position;
        float d2 = dot(toMarble, toMarble);
        colour += MarbleColours[j] * MarbleGlow[j] * 0.03 * saturate(dot(normal, toMarble * rsqrt(d2))) / (d2 + 0.02);
    }
    return colour + float3(0.03, 0.035, 0.05);
}

float3 woodColour(float3 position)
{
    float grain = fbm(float2(position.x * 1.2, position.z * 14.0)) ;
    float figure = noise(float2(position.x * 0.6 + grain * 2.0, position.z * 3.0));
    float3 dark = float3(0.07, 0.035, 0.018);
    float3 light = float3(0.2, 0.1, 0.045);
    return lerp(dark, light, saturate(grain * 0.9 + figure * 0.5 - 0.2));
}

float3 trace(float3 origin, float3 direction)
{
    float3 colour = 0.0;
    float3 throughput = 1.0;
    int bounces = Quality > 1.5 ? 4 : 3;
    [loop]
    for (int depth = 0; depth < bounces; depth++)
    {
        Hit hit = intersect(origin, direction, true);
        if (hit.material == Sky)
        {
            // Seen straight on, the studio is a dark backdrop; the lights only show in reflections.
            colour += throughput * (depth == 0 ? backdrop(direction) : environment(direction));
            break;
        }
        float3 position = origin + direction * hit.t;
        float3 normal = hit.normal;
        float facing = saturate(dot(normal, -direction));

        if (hit.material == Hole)
        {
            colour += throughput * float3(0.006, 0.006, 0.008);
            break;
        }
        if (hit.material == Floor)
        {
            // A dark satin floor that fades away into the studio.
            float fresnel = 0.03 + 0.97 * pow(1.0 - facing, 5.0);
            float fade = smoothstep(9.0, 3.0, length(position.xz));
            colour += throughput * float3(0.16, 0.16, 0.18) * woodLight(position, normal) * 0.25 * fade * (1.0 - fresnel);
            colour += throughput * backdrop(direction) * (1.0 - fade);
            throughput *= fresnel * 0.6 * fade;
            direction = reflect(direction, normal);
            origin = position + normal * 2e-3;
            continue;
        }
        if (hit.material == Chrome)
        {
            // Polished steel: mostly mirror, with a faint sheen of its own from the key light.
            float3 toKey = normalize(KeyLight - position);
            colour += throughput * float3(1.0, 0.95, 0.9) * pow(saturate(dot(reflect(direction, normal), toKey)), 40.0) * 0.6;
            colour += throughput * float3(0.03, 0.03, 0.035) * saturate(dot(normal, toKey) * 0.5 + 0.5);
            throughput *= lerp(float3(0.78, 0.8, 0.84), 1.0, pow(1.0 - facing, 5.0));
            direction = reflect(direction, normal);
            origin = position + normal * 2e-3;
            continue;
        }
        if (hit.material == Wood)
        {
            float fresnel = 0.04 + 0.96 * pow(1.0 - facing, 5.0);
            colour += throughput * woodColour(position) * woodLight(position, normal) * (1.0 - fresnel);
            throughput *= fresnel * 0.9;
            direction = reflect(direction, normal);
            origin = position + normal * 2e-3;
            continue;
        }

        // Glass: a glint of reflection, then bend through the ball, picking up its glowing heart.
        int k = hit.marble;
        float4 marble = float4(0.0, 0.0, 0.0, MarbleRadius);
        float3 tint = 1.0;
        float glowAmount = 0.0;
        [unroll]
        for (int m = 0; m < MarbleCount; m++)
        {
            if (m == k)
            {
                marble = Marbles[m];
                tint = MarbleColours[m];
                glowAmount = MarbleGlow[m];
            }
        }
        float fresnel = 0.04 + 0.96 * pow(1.0 - facing, 5.0);
        colour += throughput * environment(reflect(direction, normal)) * fresnel;
        float3 inside = refract(direction, normal, 1.0 / 1.5);
        float3 oc = position - marble.xyz;
        float chord = max(-2.0 * dot(oc, inside), 0.0);
        float3 exitPoint = position + inside * chord;
        // The heart: a glowing swirl at the centre, brightest where the ray passes closest to it.
        float closest = length(position + inside * (chord * 0.5) - marble.xyz) / MarbleRadius;
        float swirl = 0.6 + 0.4 * sin(atan2(oc.y, oc.x) * 3.0 + Time * 1.5);
        colour += throughput * (1.0 - fresnel) * tint * glowAmount * (exp(-closest * closest * 9.0) * 1.6 * swirl + 0.08);
        throughput *= (1.0 - fresnel) * lerp(1.0, tint, 0.55);
        float3 exitNormal = (exitPoint - marble.xyz) / MarbleRadius;
        float3 outside = refract(inside, -exitNormal, 1.5);
        if (dot(outside, outside) < 1e-4)
        {
            outside = reflect(inside, -exitNormal);
        }
        direction = outside;
        origin = exitPoint + outside * 2e-3;
        if (max(throughput.x, max(throughput.y, throughput.z)) < 0.02)
        {
            break;
        }
    }
    return colour;
}

float3 Render(float2 uv, float2 p)
{
    placeMarbles();
    float orbit = Time * 0.05;
    float3 eye = float3(sin(orbit) * 3.7, 1.6 + sin(Time * 0.07) * 0.4, -cos(orbit) * 3.7);
    float3 target = float3(0.0, 0.85 + sin(Time * 0.05) * 0.15, 0.0);
    float3 direction = cameraRay(eye, target, float2(p.x, -p.y), 1.9, 0.0);
    float3 colour = trace(eye, direction);
    // A touch of the last frame smooths the glints on rolling glass.
    return lerp(colour, previous(uv), 0.2);
}
