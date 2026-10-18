// Name: Tesseract
// A four-dimensional cube turns through a direction we can't point to, its shadow in our world folding endlessly inside out: glowing glass rods and chrome joints, ray traced with reflections and soft shadows on a black mirror floor. Every beat gives the turn a fresh push and the rods glow with the spectrum.
// Family: Ray traced
// Tags: Geometric, Neon, 3D
// Added: 2026-10-17 17:02

// A tesseract has 16 corners, every combination of +-1 in four coordinates (x, y, z, w), and 32
// edges joining corners that differ in just one coordinate. We turn it in 4D, then view it in
// perspective from a little way along w, the way a 3D cube's shadow is drawn on paper: the far
// cube shrinks into the middle and the near one swells round it, and as it turns they trade places.

static const float RodRadius = 0.03;
static const float JointRadius = 0.07;
static const float FloorHeight = -1.55;
static const float3 LightDirection = float3(0.35, 1.0, 0.25);   // towards the main light (normalised in use)

static float3 Corners[16];
static float CornerW[16];

// How far the 4D turn has gone. It creeps on with time, and every beat kicks it a step further:
// BeatCount jumps by one on a beat just as Beat jumps to 1, and Beat then fades, so their
// difference glides smoothly forward instead of jumping.
float turnPhase()
{
    return Time * 0.11 + (BeatCount - Beat) * 0.12;
}

void placeCorners()
{
    float phase = turnPhase();
    float a = phase;
    float b = phase * 0.61 + 1.0;
    float c = phase * 0.37 + 2.0;
    [unroll]
    for (int k = 0; k < 16; k++)
    {
        float4 v = float4((k & 1) ? 1.0 : -1.0, (k & 2) ? 1.0 : -1.0, (k & 4) ? 1.0 : -1.0, (k & 8) ? 1.0 : -1.0) * 0.62;
        // Turns in the x-w, y-w and z-w planes (the ones with no 3D equivalent), plus a slow x-z spin.
        v.xw = rotate(v.xw, a);
        v.yw = rotate(v.yw, b);
        v.zw = rotate(v.zw, c);
        v.xz = rotate(v.xz, Time * 0.07);
        float scale = 1.75 / (2.4 - v.w);
        Corners[k] = v.xyz * scale;
        CornerW[k] = v.w;
    }
}

// The two corners of edge e (0..31): corner k joined to k with one more bit set.
void edgeEnds(int e, out int from, out int to)
{
    int axis = e / 8;
    int rest = e % 8;
    int low = rest & ((1 << axis) - 1);
    int high = (rest >> axis) << (axis + 1);
    from = low | high;
    to = from | (1 << axis);
}

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
            return t;
        }
        float3 oc = y <= 0.0 ? oa : origin - b;
        qb = dot(direction, oc);
        qc = dot(oc, oc) - radius * radius;
        h = qb * qb - qc;
        if (h > 0.0)
        {
            return -qb - sqrt(h);
        }
    }
    return -1.0;
}

float sphereHit(float3 origin, float3 direction, float3 centre, float radius)
{
    float3 oc = origin - centre;
    float b = dot(oc, direction);
    float h = b * b - dot(oc, oc) + radius * radius;
    return h < 0.0 ? -1.0 : -b - sqrt(h);
}

// How close a ray passes to a segment, and how far along the ray that happens.
float2 rayToSegment(float3 origin, float3 direction, float3 a, float3 b)
{
    float3 ba = b - a;
    float3 w = origin - a;
    float bd = dot(ba, direction);
    float bb = dot(ba, ba);
    float wd = dot(w, direction);
    float wb = dot(w, ba);
    float denominator = bb - bd * bd;
    float s = denominator > 1e-6 ? saturate((wb - bd * wd) / denominator) : 0.0;
    float3 onSegment = a + ba * s;
    float t = max(dot(onSegment - origin, direction), 0.0);
    return float2(length(origin + direction * t - onSegment), t);
}

// Each edge's colour follows how deep in w its middle sits, so the inner and outer cubes glow in
// different hues; each also listens to its own slice of the spectrum.
float3 edgeColour(int e)
{
    int from;
    int to;
    edgeEnds(e, from, to);
    float w = (CornerW[from] + CornerW[to]) * 0.5;
    float3 colour = neon(w * 0.45 + 0.35 + Time * 0.01);
    return colour * (0.7 + spectrum((e + 0.5) / 32.0 * 0.8) * 2.0 + Beat * 0.4);
}

// Materials.
static const int Nothing = 0;
static const int Floor = 1;
static const int Rod = 2;
static const int Joint = 3;

struct Hit
{
    float distance;
    float3 normal;
    int material;
    int index;
};

Hit traceScene(float3 origin, float3 direction)
{
    Hit hit;
    hit.distance = 1e5;
    hit.normal = float3(0.0, 1.0, 0.0);
    hit.material = Nothing;
    hit.index = 0;
    if (direction.y < 0.0)
    {
        hit.distance = (FloorHeight - origin.y) / direction.y;
        hit.material = Floor;
    }
    [loop]
    for (int e = 0; e < 32; e++)
    {
        int from;
        int to;
        edgeEnds(e, from, to);
        float t = capsuleHit(origin, direction, Corners[from], Corners[to], RodRadius);
        if (t > 1e-3 && t < hit.distance)
        {
            float3 position = origin + direction * t;
            float3 ba = Corners[to] - Corners[from];
            float s = saturate(dot(position - Corners[from], ba) / dot(ba, ba));
            hit.distance = t;
            hit.normal = normalize(position - (Corners[from] + ba * s));
            hit.material = Rod;
            hit.index = e;
        }
    }
    [loop]
    for (int k = 0; k < 16; k++)
    {
        float t = sphereHit(origin, direction, Corners[k], JointRadius);
        if (t > 1e-3 && t < hit.distance)
        {
            hit.distance = t;
            hit.normal = normalize(origin + direction * t - Corners[k]);
            hit.material = Joint;
            hit.index = k;
        }
    }
    return hit;
}

// A dark studio: a soft panel above, and two broad coloured softboxes behind and to either side
// that rim the rods and joints with pink and cyan. Seen directly they are only a faint glow in the
// dark, so the tesseract keeps the stage.
static const float3 RimA = float3(-0.75, 0.25, -0.6);   // towards the pink softbox (normalised in use)
static const float3 RimB = float3(0.8, 0.3, -0.5);      // towards the cyan softbox

float3 rimColourA()
{
    return float3(1.0, 0.3, 0.65) * (1.0 + Mid * 1.5);
}

float3 rimColourB()
{
    return float3(0.25, 0.7, 1.0) * (1.0 + Treble * 1.5);
}

float3 studio(float3 direction, float panels)
{
    float3 colour = float3(0.004, 0.004, 0.008) + float3(0.02, 0.015, 0.035) * saturate(direction.y);
    float3 light = normalize(LightDirection);
    colour += float3(1.0, 0.95, 0.9) * smoothstep(0.9, 0.97, dot(direction, light)) * 3.0 * panels;
    // Soft-edged round softboxes rather than thin strips, so their reflections read as glows.
    colour += rimColourA() * smoothstep(0.8, 0.98, dot(direction, normalize(RimA))) * 2.0 * panels;
    colour += rimColourB() * smoothstep(0.8, 0.98, dot(direction, normalize(RimB))) * 2.0 * panels;
    return colour;
}

// Light on the floor: the overhead panel, softened by the rods' shadows, plus a coloured glow
// from the tesseract itself.
float3 floorLight(float3 position)
{
    float3 light = normalize(LightDirection);
    float shade = 1.0;
    [loop]
    for (int e = 0; e < 32; e++)
    {
        int from;
        int to;
        edgeEnds(e, from, to);
        float2 near = rayToSegment(position, light, Corners[from], Corners[to]);
        shade = min(shade, saturate((near.x - RodRadius) / (0.02 + near.y * 0.12)));
    }
    float3 toCentre = -position;
    float3 glowing = neon(Time * 0.01 + 0.4) * (0.15 + Bass * 0.25) / (1.0 + dot(toCentre, toCentre));
    float pool = exp(-dot(position.xz, position.xz) * 0.25);
    return float3(1.0, 0.96, 0.92) * 0.45 * (0.3 + 0.7 * shade) * (0.5 + pool) + glowing * 2.0;
}

float3 trace(float3 origin, float3 direction, out float firstDistance)
{
    firstDistance = 1e5;
    float3 colour = 0.0;
    float3 throughput = 1.0;
    bool fromFloor = false;
    [loop]
    for (int bounce = 0; bounce < 3; bounce++)
    {
        Hit hit = traceScene(origin, direction);
        if (bounce == 0)
        {
            firstDistance = hit.distance;
        }
        if (hit.material == Nothing)
        {
            // Seen straight on, or in the floor, the studio lights stay dim so they don't upstage
            // the tesseract; the chrome and glass show them at full strength.
            colour += throughput * studio(direction, bounce == 0 ? 0.0 : (fromFloor && bounce == 1 ? 0.03 : 1.0));
            break;
        }
        float3 position = origin + direction * hit.distance;
        float3 normal = hit.normal;
        float facing = saturate(dot(normal, -direction));
        float fresnel = 0.04 + 0.96 * pow(1.0 - facing, 5.0);
        if (hit.material == Floor)
        {
            // Polished black stone: a faint light of its own and a dim mirror image.
            // Polished black stone: a pool of light beneath the tesseract, faint rings cut into it,
            // and a clear mirror image that dims towards the edge of the stage.
            float fade = exp(-length(position.xz) * 0.12);
            float rings = smoothstep(0.04, 0.0, abs(frac(length(position.xz) * 1.2) - 0.5) - 0.46);
            colour += throughput * (float3(0.1, 0.1, 0.11) + rings * 0.05) * floorLight(position) * fade;
            throughput *= (0.4 + fresnel * 0.5) * fade;
            fromFloor = true;
        }
        else if (hit.material == Rod)
        {
            // Glass round a glowing core: the core shows brightest straight on, the glass's
            // reflection takes over at the edges.
            float3 core = edgeColour(hit.index);
            colour += throughput * core * (0.4 + 1.6 * pow(facing, 3.0)) * (1.0 - fresnel);
            // The softboxes behind catch the glass along its edges.
            float edge = pow(1.0 - facing, 2.0);
            colour += throughput * edge * (rimColourA() * saturate(dot(normal, normalize(RimA))) + rimColourB() * saturate(dot(normal, normalize(RimB)))) * 0.5;
            throughput *= fresnel;
        }
        else
        {
            // Chrome joints, tinted faintly by the hue of their corner.
            float3 tint = lerp(float3(0.9, 0.92, 0.95), neon(CornerW[hit.index] * 0.45 + 0.35), 0.15);
            throughput *= lerp(tint, 1.0, pow(1.0 - facing, 5.0));
        }
        direction = reflect(direction, normal);
        origin = position + normal * 2e-3;
        if (max(throughput.x, max(throughput.y, throughput.z)) < 0.02)
        {
            break;
        }
    }
    return colour;
}

float3 Render(float2 uv, float2 p)
{
    placeCorners();
    float orbit = Time * 0.045;
    float3 eye = float3(sin(orbit) * 3.4, 0.6 + sin(Time * 0.06) * 0.4, cos(orbit) * 3.4);
    float3 target = float3(0.0, -0.4, 0.0);
    float3 direction = cameraRay(eye, target, float2(p.x, -p.y), 1.95, 0.0);

    float firstDistance;
    float3 colour = trace(eye, direction, firstDistance);

    // The rods' glow in the air round them.
    float3 haze = 0.0;
    [loop]
    for (int e = 0; e < 32; e++)
    {
        int from;
        int to;
        edgeEnds(e, from, to);
        float2 near = rayToSegment(eye, direction, Corners[from], Corners[to]);
        if (near.y < firstDistance)
        {
            haze += edgeColour(e) * exp(-near.x * 40.0);
        }
    }
    return colour + haze * 0.05;
}
