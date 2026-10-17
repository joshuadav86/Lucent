// Name: Newton's Cradle
// A chrome Newton's cradle clacks on every beat under studio lights, its mirror-polished balls reflecting each other, ray traced with soft shadows and coloured lights that follow the music.
// Family: Ray traced
// Tags: 3D, Calm, Geometric

static const int BallCount = 5;
static const float BallRadius = 0.3;
static const float PivotHeight = 2.55;
static const float StringLength = 1.6;
static const float RailOffset = 0.62;
static const int PostCount = 6;

// Materials.
static const int Backdrop = 0;
static const int Floor = 1;
static const int Base = 2;
static const int Chrome = 3;
static const int Frame = 4;
static const int Softbox = 5;

struct Hit
{
    float distance;
    float3 normal;
    int material;
    float3 glow;
};

float sinceBeat()
{
    return -log(max(Beat, 1e-4)) / 5.0;
}

// The swing: each beat knocks the end ball (or two) on alternate sides out and back, landing a moment
// before the next beat would, the way a real cradle passes the knock through to the other end.
static const float SwingTime = 0.44;

float swingAngle(int ball, float age)
{
    float side = wrap(BeatCount, 2.0) < 0.5 ? -1.0 : 1.0;
    float knocked = frac(BeatCount * 0.618) > 0.72 ? 2.0 : 1.0;
    float amplitude = 0.42 + 0.35 * saturate(Bass * 1.2);
    float swing = age < SwingTime ? sin(PI * age / SwingTime) : 0.0;
    // How far this ball is from the end being knocked: 0 for the end ball.
    float fromEnd = side < 0.0 ? ball : (BallCount - 1 - ball);
    float swinging = fromEnd < knocked ? 1.0 : 0.0;
    // A tiny shiver runs through the resting balls when the swinging one lands.
    float shiver = (1.0 - swinging) * sin(age * 70.0) * exp(-abs(age - SwingTime) * 25.0) * 0.006;
    return side * amplitude * swing * swinging * step(1e-3, Beat) + shiver;
}

float3 pivotOf(int ball)
{
    return float3((ball - (BallCount - 1) * 0.5) * BallRadius * 2.0, PivotHeight, 0.0);
}

float3 ballCentre(int ball, float age)
{
    float angle = swingAngle(ball, age);
    return pivotOf(ball) + StringLength * float3(sin(angle), -cos(angle), 0.0);
}

float sphereHit(float3 origin, float3 direction, float4 sphere)
{
    float3 oc = origin - sphere.xyz;
    float b = dot(oc, direction);
    float c = dot(oc, oc) - sphere.w * sphere.w;
    float h = b * b - c;
    if (h < 0.0)
    {
        return -1.0;
    }
    float t = -b - sqrt(h);
    return t > 1e-3 ? t : -1.0;
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

float boxHit(float3 origin, float3 direction, float3 centre, float3 size, out float3 normal)
{
    float3 m = 1.0 / direction;
    float3 n = m * (origin - centre);
    float3 k = abs(m) * size;
    float3 t1 = -n - k;
    float3 t2 = -n + k;
    float entry = max(max(t1.x, t1.y), t1.z);
    float leave = min(min(t2.x, t2.y), t2.z);
    normal = -sign(direction) * step(t1.yzx, t1.xyz) * step(t1.zxy, t1.xyz);
    return (entry > leave || entry < 1e-3) ? -1.0 : entry;
}

// Ray against a rod with rounded ends from a to b.
float capsuleHit(float3 origin, float3 direction, float3 a, float3 b, float radius)
{
    float3 ba = b - a;
    float3 oa = origin - a;
    float baba = dot(ba, ba);
    float bard = dot(ba, direction);
    float baoa = dot(ba, oa);
    float rdoa = dot(direction, oa);
    float oaoa = dot(oa, oa);
    float qa = baba - bard * bard;
    float qb = baba * rdoa - baoa * bard;
    float qc = baba * oaoa - baoa * baoa - radius * radius * baba;
    float h = qb * qb - qa * qc;
    if (h >= 0.0)
    {
        float t = (-qb - sqrt(h)) / qa;
        float y = baoa + t * bard;
        if (y > 0.0 && y < baba)
        {
            return t > 1e-3 ? t : -1.0;
        }
        float3 oc = (y <= 0.0) ? oa : origin - b;
        qb = dot(direction, oc);
        qc = dot(oc, oc) - radius * radius;
        h = qb * qb - qc;
        if (h > 0.0)
        {
            t = -qb - sqrt(h);
            return t > 1e-3 ? t : -1.0;
        }
    }
    return -1.0;
}

float3 capsuleNormal(float3 position, float3 a, float3 b, float radius)
{
    float3 ba = b - a;
    float3 pa = position - a;
    float h = saturate(dot(pa, ba) / dot(ba, ba));
    return (pa - h * ba) / radius;
}

// The frame: two rails along the top and four posts down to the base.
void framePiece(int i, out float3 a, out float3 b)
{
    float halfWidth = BallRadius * BallCount + 0.45;
    if (i < 2)
    {
        float z = i == 0 ? -RailOffset : RailOffset;
        a = float3(-halfWidth, PivotHeight, z);
        b = float3(halfWidth, PivotHeight, z);
    }
    else
    {
        float x = (i & 1) == 0 ? -halfWidth : halfWidth;
        float z = i < 4 ? -RailOffset : RailOffset;
        a = float3(x, 0.3, z);
        b = float3(x, PivotHeight, z);
    }
}

// Light panels round the set: their colours change with the beats and brighten with the mids and treble.
// The colour scheme moves on every fourth beat, blending over half a second.
float sceneHue()
{
    float settle = wrap(BeatCount, 4.0) < 0.5 ? 1.0 - saturate(sinceBeat() * 2.0) : 0.0;
    return (floor(BeatCount / 4.0) - settle) * 0.13;
}

float3 softboxColour(int panel)
{
    float hue = sceneHue();
    if (panel == 0)
    {
        return float3(1.0, 0.96, 0.9) * 2.6;
    }
    if (panel == 1)
    {
        return hsv2rgb(hue + 0.55, 0.75, 1.0) * (1.0 + Mid * 2.0);
    }
    return hsv2rgb(hue + 0.9, 0.7, 1.0) * (1.0 + Treble * 2.4);
}

// Ray against an upright or overhead glowing rectangle.
float panelHit(float3 origin, float3 direction, int panel, out float3 glow)
{
    float3 centre = panel == 0 ? float3(0.0, 6.0, 0.6) : float3(panel == 1 ? -3.6 : 3.6, 2.0, 3.2);
    float3 normal = panel == 0 ? float3(0.0, -1.0, 0.0) : normalize(float3(panel == 1 ? 1.0 : -1.0, 0.0, -0.8));
    float3 axisA = panel == 0 ? float3(1.0, 0.0, 0.0) : float3(0.0, 1.0, 0.0);
    float3 axisB = normalize(cross(normal, axisA));
    float2 size = panel == 0 ? float2(2.6, 1.4) : float2(1.6, 0.7);
    glow = 0.0;
    float facing = dot(direction, normal);
    if (facing >= 0.0)
    {
        return -1.0;
    }
    float t = dot(centre - origin, normal) / facing;
    float3 q = origin + direction * t - centre;
    float2 local = abs(float2(dot(q, axisA), dot(q, axisB))) - size;
    if (t < 1e-3 || max(local.x, local.y) > 0.0)
    {
        return -1.0;
    }
    // Brighter in the middle, like diffusing fabric.
    glow = softboxColour(panel) * (0.75 + 0.25 * smoothstep(0.0, -0.4, max(local.x, local.y)));
    return t;
}

Hit traceScene(float3 origin, float3 direction, float4 balls[BallCount], bool withFrame)
{
    Hit hit;
    hit.distance = 1e4;
    hit.normal = float3(0.0, 1.0, 0.0);
    hit.material = Backdrop;
    hit.glow = 0.0;

    if (direction.y < 0.0)
    {
        float t = -origin.y / direction.y;
        if (t > 1e-3)
        {
            hit.distance = t;
            hit.material = Floor;
        }
    }

    float3 boxNormal;
    float t = boxHit(origin, direction, float3(0.0, 0.15, 0.0), float3(BallRadius * BallCount + 0.75, 0.15, RailOffset + 0.35), boxNormal);
    if (t > 0.0 && t < hit.distance)
    {
        hit.distance = t;
        hit.normal = boxNormal;
        hit.material = Base;
    }

    [unroll]
    for (int i = 0; i < BallCount; i++)
    {
        t = sphereHit(origin, direction, balls[i]);
        if (t > 0.0 && t < hit.distance)
        {
            hit.distance = t;
            hit.normal = (origin + direction * t - balls[i].xyz) / balls[i].w;
            hit.material = Chrome;
        }
    }

    if (withFrame)
    {
        [unroll]
        for (int j = 0; j < PostCount; j++)
        {
            float3 a, b;
            framePiece(j, a, b);
            t = capsuleHit(origin, direction, a, b, 0.045);
            if (t > 0.0 && t < hit.distance)
            {
                hit.distance = t;
                hit.normal = capsuleNormal(origin + direction * t, a, b, 0.045);
                hit.material = Frame;
            }
        }
    }

    [unroll]
    for (int k = 0; k < 3; k++)
    {
        float3 glow;
        t = panelHit(origin, direction, k, glow);
        if (t > 0.0 && t < hit.distance)
        {
            hit.distance = t;
            hit.material = Softbox;
            hit.glow = glow;
        }
    }
    return hit;
}

// The dark studio backdrop, with a soft pool of colour behind the cradle.
float3 backdrop(float3 direction)
{
    float hue = sceneHue();
    // A curved studio wall: a wash of colour that brightens with the music, darker towards the floor and the top.
    float3 wash = lerp(hsv2rgb(hue + 0.7, 0.55, 1.0), hsv2rgb(hue + 0.5, 0.5, 1.0), saturate(direction.x * 0.5 + 0.5));
    float height = saturate(direction.y * 1.6 + 0.35);
    float band = exp(-abs(direction.y - 0.05) * 4.0);
    float3 wall = wash * (0.06 + band * (0.2 + Level * 0.35)) * (0.4 + 0.6 * height);
    // Behind the camera the studio is darker, with a pale strip light along the top of the wall.
    float behind = smoothstep(-0.2, 0.6, direction.z);
    float3 strip = float3(0.9, 0.92, 1.0) * smoothstep(0.03, 0.0, abs(direction.y - 0.35)) * 1.2;
    return float3(0.012, 0.012, 0.016) + lerp(wall, wall * 0.3 + strip, behind);
}

// Light reaching a diffuse point: the overhead softbox (with soft ball shadows) and the two coloured side panels.
static const float3 LightPositions[3] = { float3(0.0, 6.0, 0.6), float3(-3.6, 2.0, 3.2), float3(3.6, 2.0, 3.2) };
static const float LightSoftness[3] = { 4.0, 6.0, 6.0 };

float3 directLight(float3 position, float3 normal, float4 balls[BallCount])
{
    float3 colour = 0.0;
    [unroll]
    for (int l = 0; l < 3; l++)
    {
        float3 toLight = LightPositions[l] - position;
        float distance = length(toLight);
        float3 direction = toLight / distance;
        float shadow = 1.0;
        [unroll]
        for (int i = 0; i < BallCount; i++)
        {
            shadow *= sphereShadow(position, direction, balls[i], LightSoftness[l]);
        }
        float strength = l == 0 ? 0.6 : 0.12;
        colour += softboxColour(l) * strength * saturate(dot(normal, direction)) * shadow * 30.0 / (6.0 + distance * distance);
    }
    return colour;
}

float3 trace(float3 origin, float3 direction, float4 balls[BallCount])
{
    float3 colour = 0.0;
    float3 throughput = 1.0;
    int bounces = qualitySteps(5);
    [loop]
    for (int depth = 0; depth < bounces; depth++)
    {
        // The thin frame only matters for the first two bounces; skipping it after keeps deep reflections cheap.
        Hit hit = traceScene(origin, direction, balls, depth < 2);
        float3 position = origin + direction * hit.distance;
        float facing = saturate(dot(hit.normal, -direction));

        if (hit.material == Backdrop)
        {
            colour += throughput * backdrop(direction);
            break;
        }
        if (hit.material == Softbox)
        {
            colour += throughput * hit.glow;
            break;
        }

        float3 normal = hit.normal;
        if (hit.material == Chrome)
        {
            throughput *= lerp(float3(0.82, 0.84, 0.88), 1.0, pow(1.0 - facing, 5.0));
        }
        else if (hit.material == Frame)
        {
            // Brushed steel: duller than the balls.
            throughput *= lerp(float3(0.5, 0.52, 0.56), 1.0, pow(1.0 - facing, 5.0));
            colour += throughput * directLight(position, normal, balls) * 0.04;
        }
        else
        {
            // The base is black lacquer; the floor is dark satin that fades into the distance.
            bool isBase = hit.material == Base;
            float3 albedo = isBase ? float3(0.012, 0.012, 0.015) : float3(0.12, 0.115, 0.125);
            float fresnel = (isBase ? 0.05 : 0.03) + 0.95 * pow(1.0 - facing, 5.0);
            // The floor melts into the studio wall in the distance, so there's no hard horizon.
            float fog = isBase ? 0.0 : smoothstep(5.0, 16.0, length(position.xz));
            float3 surface = albedo * (directLight(position, normal, balls) + 0.02) * (1.0 - fresnel);
            colour += throughput * lerp(surface, backdrop(direction), fog);
            throughput *= isBase ? fresnel + 0.08 : (fresnel + 0.05) * (1.0 - fog);
        }

        direction = reflect(direction, normal);
        origin = position + normal * 1e-3;
        if (max(throughput.x, max(throughput.y, throughput.z)) < 0.02)
        {
            break;
        }
    }
    return colour;
}

// Distance between a ray and a line segment, and how far along the ray that closest point is.
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

// Where the two rays per pixel go on High and Ultra, in pixels.
static const float2 SampleOffsets[2] = { float2(0.25, -0.25), float2(-0.25, 0.25) };

float3 Render(float2 uv, float2 p)
{
    float age = sinceBeat();
    float4 balls[BallCount];
    [unroll]
    for (int b = 0; b < BallCount; b++)
    {
        balls[b] = float4(ballCentre(b, age), BallRadius);
    }

    float orbit = sin(Time * 0.06) * 0.6;
    float3 eye = float3(sin(orbit) * 4.2, 1.55 + 0.3 * sin(Time * 0.09), cos(orbit) * 4.2);
    float3 target = float3(0.0, 1.25, 0.0);
    const float zoom = 1.9;
    float3 direction = cameraRay(eye, target, float2(p.x, -p.y), zoom, 0.0);

    int samples = Quality > 1.5 ? 2 : 1;
    float3 colour = 0.0;
    [loop]
    for (int s = 0; s < samples; s++)
    {
        float2 jitter = samples > 1 ? SampleOffsets[s] * pixelSize() : 0.0;
        colour += trace(eye, cameraRay(eye, target, float2(p.x, -p.y) + jitter, zoom, 0.0), balls);
    }
    colour /= samples;

    // The fine wires each ball hangs from, drawn smooth at any resolution: two from each ball up to the rails.
    Hit first = traceScene(eye, direction, balls, true);
    float pixelAngle = pixelSize() / zoom;
    [unroll]
    for (int w = 0; w < BallCount * 2; w++)
    {
        int ball = w / 2;
        float3 top = pivotOf(ball) + float3(0.0, 0.0, (w & 1) == 0 ? -RailOffset : RailOffset);
        float3 bottom = balls[ball].xyz + normalize(top - balls[ball].xyz) * BallRadius * 0.9;
        float2 closest = raySegment(eye, direction, top, bottom);
        float width = max(0.004, pixelAngle * closest.y * 0.6);
        float coverage = saturate(0.004 / width) * smoothstep(width, 0.0, closest.x) * step(closest.y, first.distance);
        float3 wire = float3(0.5, 0.52, 0.56) * (0.25 + 0.75 * saturate(dot(normalize(top - bottom), float3(0.0, 1.0, 0.0))));
        colour = lerp(colour, wire * 0.6, coverage);
    }

    // A bright spark where the swinging ball lands against the others.
    float3 contactLeft = (balls[0].xyz + balls[1].xyz) * 0.5;
    float3 contactRight = (balls[BallCount - 2].xyz + balls[BallCount - 1].xyz) * 0.5;
    float flash = exp(-abs(age - SwingTime) * 30.0) * step(1e-3, Beat);
    [unroll]
    for (int c = 0; c < 2; c++)
    {
        float3 contact = c == 0 ? contactLeft : contactRight;
        float along = dot(contact - eye, direction);
        float off = length(eye + direction * along - contact);
        colour += float3(1.0, 0.85, 0.6) * flash * exp(-off * 40.0) * 2.0 * step(along, first.distance + 0.2);
    }
    return colour;
}
