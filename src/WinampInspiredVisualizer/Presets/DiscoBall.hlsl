// Name: Disco Ball
// A mirrored disco ball turns slowly in a hazy room, scattering hundreds of coloured spots of light across the walls and floor, with beams cutting through the haze; the spots flash with the beat.
// Family: Ray traced
// Tags: Retro, Colourful, 3D
// Added: 2026-10-17 17:06

static const float3 BallCentre = float3(0.0, 3.3, 0.0);
static const float BallRadius = 0.55;
static const int Rows = 36;               // bands of mirror tiles from pole to pole
static const float3 RoomSize = float3(7.0, 4.6, 7.0);   // half width, height, half depth
static const int Lights = 3;

float ballTurn()
{
    return Time * 0.32 + sin(Time * 0.11) * 0.2;
}

// The three spotlights on the ceiling edges, slowly wandering, each aimed at the ball.
float3 lightPosition(int i)
{
    float angle = i * 2.094 + 0.6 + sin(Time * 0.07 + i) * 0.35;
    return float3(cos(angle) * 5.5, 4.4, sin(angle) * 5.5);
}

// Each spotlight's colour moves on every four beats; on the beat the light flares.
float3 lightColour(int i)
{
    float hue = floor(BeatCount / 4.0) * 0.21 + i * 0.33 + Time * 0.005;
    float flash = 0.55 + Beat * 0.9 + spectrum(0.1 + i * 0.3) * 0.5;
    return hsv2rgb(hue, 0.75, 1.0) * flash;
}

// Each band of tiles: the cos and sin of its middle latitude, and how many tiles go round it.
// Worked out ahead of time, since the haze looks up a tile over a hundred times a pixel.
static const float3 Bands[Rows] =
{
    float3(0.04361939, -0.99904822, 3.0),
    float3(0.13052619, -0.99144486, 9.0),
    float3(0.21643961, -0.97629601, 16.0),
    float3(0.30070580, -0.95371695, 22.0),
    float3(0.38268343, -0.92387953, 28.0),
    float3(0.46174861, -0.88701083, 33.0),
    float3(0.53729961, -0.84339145, 39.0),
    float3(0.60876143, -0.79335334, 44.0),
    float3(0.67559021, -0.73727734, 49.0),
    float3(0.73727734, -0.67559021, 53.0),
    float3(0.79335334, -0.60876143, 57.0),
    float3(0.84339145, -0.53729961, 61.0),
    float3(0.88701083, -0.46174861, 64.0),
    float3(0.92387953, -0.38268343, 67.0),
    float3(0.95371695, -0.30070580, 69.0),
    float3(0.97629601, -0.21643961, 70.0),
    float3(0.99144486, -0.13052619, 71.0),
    float3(0.99904822, -0.04361939, 72.0),
    float3(0.99904822, 0.04361939, 72.0),
    float3(0.99144486, 0.13052619, 71.0),
    float3(0.97629601, 0.21643961, 70.0),
    float3(0.95371695, 0.30070580, 69.0),
    float3(0.92387953, 0.38268343, 67.0),
    float3(0.88701083, 0.46174861, 64.0),
    float3(0.84339145, 0.53729961, 61.0),
    float3(0.79335334, 0.60876143, 57.0),
    float3(0.73727734, 0.67559021, 53.0),
    float3(0.67559021, 0.73727734, 49.0),
    float3(0.60876143, 0.79335334, 44.0),
    float3(0.53729961, 0.84339145, 39.0),
    float3(0.46174861, 0.88701083, 33.0),
    float3(0.38268343, 0.92387953, 28.0),
    float3(0.30070580, 0.95371695, 22.0),
    float3(0.21643961, 0.97629601, 16.0),
    float3(0.13052619, 0.99144486, 9.0),
    float3(0.04361939, 0.99904822, 3.0)
};

// The mirror tile that a direction (in the ball's own turning frame) falls on, as that tile's normal,
// plus a random number for the tile.
float3 tileNormal(float3 local, out float tileRandom)
{
    float bandSize = PI / Rows;
    float latitude = asin(clamp(local.y, -1.0, 1.0));
    float row = clamp(floor((latitude + PI * 0.5) / bandSize), 0.0, Rows - 1.0);
    float3 band = Bands[(int)row];
    float count = band.z;
    float longitude = atan2(local.z, local.x);
    float column = floor((longitude / (2.0 * PI) + 0.5) * count);
    float centreLongitude = ((column + 0.5) / count - 0.5) * 2.0 * PI;
    tileRandom = hash(float2(row, column));
    float sinLongitude, cosLongitude;
    sincos(centreLongitude, sinLongitude, cosLongitude);
    return float3(band.x * cosLongitude, band.y, band.x * sinLongitude);
}

// What stays the same for every pixel in a frame, worked out once at the start of Render: how far
// the ball has turned, and where each spotlight is, which way its light hits the ball and its colour.
// The haze asks for all of it at every step, which made it most of the visual's work.
static float TurnSin = 0.0;
static float TurnCos = 1.0;
static float3 SpotPosition[Lights];
static float3 SpotIncoming[Lights];
static float3 SpotColour[Lights];

void setUpFrame()
{
    sincos(ballTurn(), TurnSin, TurnCos);
    [unroll]
    for (int i = 0; i < Lights; i++)
    {
        SpotPosition[i] = lightPosition(i);
        SpotIncoming[i] = normalize(BallCentre - SpotPosition[i]);
        SpotColour[i] = lightColour(i);
    }
}

float3 toBall(float3 v)
{
    v.xz = float2(TurnCos * v.x + TurnSin * v.z, -TurnSin * v.x + TurnCos * v.z);
    return v;
}

float3 fromBall(float3 v)
{
    v.xz = float2(TurnCos * v.x - TurnSin * v.z, TurnSin * v.x + TurnCos * v.z);
    return v;
}

// How much of a spotlight's beam, bounced off the right tile of the ball, heads in direction d
// (from the ball's centre). width sets how sharp the spots are.
float3 reflectedSpots(float3 d, float width)
{
    float3 total = 0.0;
    [unroll]
    for (int i = 0; i < Lights; i++)
    {
        float3 incoming = SpotIncoming[i];
        // The tile that would send this light our way faces halfway between.
        float3 wanted = normalize(d - incoming);
        float tileRandom;
        float3 tile = fromBall(tileNormal(toBall(wanted), tileRandom));
        float3 bounced = reflect(incoming, tile);
        float miss = 1.0 - dot(bounced, d);
        float lit = step(0.0, -dot(tile, incoming));
        // Some tiles are slightly tarnished or askew, so spots vary; a few twinkle with the treble.
        float twinkle = 0.6 + 0.4 * tileRandom + step(0.93, frac(tileRandom * 13.7 + Time * 0.5)) * Treble * 1.5;
        total += SpotColour[i] * exp(-miss / width) * lit * twinkle;
    }
    return total;
}

// Ray against the inside of the room box.
float roomHit(float3 origin, float3 direction, out float3 normal)
{
    float3 lowCorner = float3(-RoomSize.x, 0.0, -RoomSize.z);
    float3 highCorner = float3(RoomSize.x, RoomSize.y, RoomSize.z);
    float3 inverse = 1.0 / direction;
    float3 t1 = (lowCorner - origin) * inverse;
    float3 t2 = (highCorner - origin) * inverse;
    float3 farHit = max(t1, t2);
    float t = min(min(farHit.x, farHit.y), farHit.z);
    normal = -sign(direction) * step(farHit, float3(t, t, t) + 1e-4);
    return t;
}

float sphereHit(float3 origin, float3 direction, float3 centre, float radius)
{
    float3 oc = origin - centre;
    float b = dot(oc, direction);
    float h = b * b - dot(oc, oc) + radius * radius;
    if (h < 0.0)
    {
        return -1.0;
    }
    float t = -b - sqrt(h);
    return t > 1e-3 ? t : -1.0;
}

// The room's surfaces: dark painted walls, a dark glossy dance floor in squares, a black ceiling.
float3 surfaceColour(float3 position, float3 normal)
{
    float3 albedo = float3(0.16, 0.15, 0.17);
    if (normal.y > 0.5)
    {
        float2 square = floor(position.xz / 1.2);
        float checker = frac((square.x + square.y) * 0.5) * 2.0;
        albedo = lerp(float3(0.1, 0.09, 0.1), float3(0.2, 0.19, 0.21), checker);
    }
    else if (normal.y < -0.5)
    {
        albedo = float3(0.04, 0.04, 0.045);
    }
    // Soft fill from the ball's general glow, darker in corners.
    float3 toBallCentre = BallCentre - position;
    float distance = length(toBallCentre);
    float3 d = -toBallCentre / distance;
    float facing = saturate(dot(normal, -d));
    float3 spots = reflectedSpots(d, 0.00016) * facing * 130.0 / (6.0 + distance * distance);
    float3 ambient = float3(0.04, 0.035, 0.05) * (0.6 + 0.4 * facing);
    return albedo * (spots + ambient);
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

float3 Render(float2 uv, float2 p)
{
    setUpFrame();
    float orbit = Time * 0.05;
    float3 eye = float3(sin(orbit) * 4.6, 1.5 + sin(Time * 0.09) * 0.3, cos(orbit) * 4.6);
    float3 target = BallCentre + float3(0.0, -1.0 + sin(Time * 0.06) * 0.3, 0.0);
    const float zoom = 1.25;
    float3 direction = cameraRay(eye, target, float2(p.x, -p.y), zoom, 0.0);

    float3 normal;
    float distance = roomHit(eye, direction, normal);
    float3 position = eye + direction * distance;
    float3 colour = surfaceColour(position, normal);

    // The glossy dance floor mirrors the room faintly.
    if (normal.y > 0.5)
    {
        float3 mirrored = reflect(direction, normal);
        float3 mirrorNormal;
        float mirrorDistance = roomHit(position, mirrored, mirrorNormal);
        float fresnel = 0.04 + 0.5 * pow(1.0 - saturate(-direction.y), 5.0);
        float3 seen = surfaceColour(position + mirrored * mirrorDistance, mirrorNormal);
        float ballInMirror = sphereHit(position, mirrored, BallCentre, BallRadius);
        seen += ballInMirror > 0.0 ? float3(0.15, 0.15, 0.17) : 0.0;
        colour += seen * fresnel * 1.5;
    }

    // The ball: mirror tiles, each reflecting whatever light lands on it, with dark grout between.
    float ballDistance = sphereHit(eye, direction, BallCentre, BallRadius);
    if (ballDistance > 0.0)
    {
        distance = ballDistance;
        float3 hit = eye + direction * ballDistance;
        float3 sphereNormal = normalize(hit - BallCentre);
        float tileRandom;
        float3 localNormal = toBall(sphereNormal);
        float3 tile = fromBall(tileNormal(localNormal, tileRandom));
        float3 mirrored = reflect(direction, tile);
        float3 mirrorNormal;
        float mirrorDistance = roomHit(hit, mirrored, mirrorNormal);
        // Silver tiles: a dim grey sheen of the room, and the spots the room is covered in.
        float3 ball = surfaceColour(hit + mirrored * mirrorDistance, mirrorNormal) * 0.8 + float3(0.05, 0.05, 0.06) * (0.5 + 0.5 * mirrored.y);
        [unroll]
        for (int i = 0; i < Lights; i++)
        {
            float3 toLight = normalize(SpotPosition[i] - hit);
            ball += SpotColour[i] * 25.0 * exp(-(1.0 - dot(mirrored, toLight)) / 0.012) * (0.5 + tileRandom);
        }
        // Grout: the thin dark lines between tiles.
        float bandSize = PI / Rows;
        float latitude = asin(clamp(localNormal.y, -1.0, 1.0));
        float rowEdge = abs(frac((latitude + PI * 0.5) / bandSize) - 0.5);
        float centreLatitude = (floor((latitude + PI * 0.5) / bandSize) + 0.5) * bandSize - PI * 0.5;
        float count = max(floor(2.0 * Rows * cos(centreLatitude) + 0.5), 3.0);
        float columnEdge = abs(frac((atan2(localNormal.z, localNormal.x) / (2.0 * PI) + 0.5) * count) - 0.5);
        float grout = smoothstep(0.5, 0.42, max(rowEdge, columnEdge));
        colour = ball * (0.25 + 0.75 * grout) * (0.7 + 0.3 * tileRandom);
    }

    // Haze: march through the room adding the light scattered from every beam we pass through.
    // Each frame jitters where the samples fall and blends with the last, which smooths the beams.
    int steps = qualitySteps(36);
    float stride = min(distance, 16.0) / steps;
    float jitter = frac(hash(p * 517.0 + frac(Time * 7.3) * 31.0) + FrameCount * 0.618);
    float3 haze = 0.0;
    [loop]
    for (int s = 0; s < steps; s++)
    {
        float3 q = eye + direction * ((s + jitter) * stride);
        float3 fromCentre = q - BallCentre;
        float reach = length(fromCentre);
        if (reach > BallRadius)
        {
            haze += reflectedSpots(fromCentre / reach, 0.0005) / (1.0 + reach * reach * 0.12);
        }
    }
    float density = 0.025 + Level * 0.015;
    colour += haze * stride * density;

    // The spotlights' own beams onto the ball, and their lamps glowing at the ceiling.
    [unroll]
    for (int i = 0; i < Lights; i++)
    {
        float3 lamp = SpotPosition[i];
        float2 beam = raySegment(eye, direction, lamp, BallCentre);
        float along = saturate(dot(eye + direction * beam.y - lamp, normalize(BallCentre - lamp)) / length(BallCentre - lamp));
        float width = 0.08 + along * BallRadius;
        colour += SpotColour[i] * 0.12 * exp(-beam.x * beam.x / (width * width)) * step(beam.y, distance);
        float2 bulb = raySegment(eye, direction, lamp, lamp);
        colour += SpotColour[i] * 3.0 * exp(-bulb.x * bulb.x / 0.006) * step(bulb.y, distance + 0.1);
    }
    // The ball's own soft glow in the haze.
    float2 core = raySegment(eye, direction, BallCentre, BallCentre);
    colour += float3(0.25, 0.22, 0.3) * 0.06 * exp(-max(core.x - BallRadius, 0.0) * 2.0);

    return lerp(colour, previous(uv), 0.25);
}
