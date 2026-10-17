// Name: Kinetic Rain
// Hundreds of polished copper droplets hang on fine wires and rise and fall in slow, choreographed waves, each one mirroring its neighbours and the warm lights of the hall; the waves swell and shift with the spectrum.
// Family: Ray traced
// Tags: Calm, Geometric, 3D

// The droplets hang in a square grid, one per cell. A ray is walked through the grid cell by cell
// (like a line drawn across graph paper), so only the droplet in each cell it crosses needs
// testing, which keeps hundreds of droplets cheap.
static const int Columns = 20;
static const float Spacing = 0.3;
static const float3 DropSize = float3(0.1, 0.135, 0.1);
static const float SlabBottom = -1.15;
static const float SlabTop = 2.6;     // wires are drawn up to here, fading into the dark above
static const float3 Copper = float3(0.96, 0.62, 0.45);

// The angle one pixel covers, set in Render.
static float PixelAngle;

float2 cellCentre(float2 cell)
{
    return (cell - (Columns - 1) * 0.5) * Spacing;
}

// The height of the droplet over q. Three slow patterns (ripples from the middle, criss-crossing
// waves and a sweeping front) fade in and out of one another; each ring of droplets swings
// further when its part of the spectrum is loud.
float dropHeight(float2 q)
{
    float t = Time;
    float r = length(q);
    float ripple = sin(r * 2.4 - t * 1.2);
    float criss = sin(q.x * 1.7 + t * 0.8) * cos(q.y * 1.4 - t * 0.6);
    float2 heading = float2(cos(t * 0.05), sin(t * 0.05));
    float front = sin(dot(q, heading) * 1.8 - t * 1.0);
    float3 weights = 0.5 + 0.5 * sin(t * 0.06 + float3(0.0, 2.1, 4.2));
    weights /= weights.x + weights.y + weights.z;
    float shape = ripple * weights.x + criss * weights.y + front * weights.z;
    float band = saturate(r / (Columns * Spacing * 0.7));
    float amount = 0.35 + spectrum(band * 0.65 + 0.03) * 0.55;
    // Each beat drops a soft ring out from the centre.
    float pulse = Beat * sin(r * 3.5 - (1.0 - Beat) * 5.0) * 0.12;
    return shape * amount * 0.75 + pulse;
}

// Ray against an upright ellipsoid. Returns the distance or -1.
float dropHit(float3 origin, float3 direction, float3 centre)
{
    float3 o = (origin - centre) / DropSize;
    float3 d = direction / DropSize;
    float a = dot(d, d);
    float b = dot(o, d);
    float c = dot(o, o) - 1.0;
    float h = b * b - a * c;
    if (h < 0.0)
    {
        return -1.0;
    }
    float t = (-b - sqrt(h)) / a;
    return t > 1e-3 ? t : -1.0;
}

// Walks the grid. Returns the nearest droplet's distance (or -1) and its centre, and adds up how
// much of each wire the ray grazes along the way.
float traceDrops(float3 origin, float3 direction, out float3 centre, out float wires)
{
    centre = 0.0;
    wires = 0.0;
    // Clip the ray to the box the droplets and wires live in.
    float halfWidth = Columns * Spacing * 0.5;
    float3 boxMin = float3(-halfWidth, SlabBottom, -halfWidth);
    float3 boxMax = float3(halfWidth, SlabTop, halfWidth);
    float3 inverse = 1.0 / (direction + (direction == 0.0) * 1e-6);
    float3 t0 = (boxMin - origin) * inverse;
    float3 t1 = (boxMax - origin) * inverse;
    float3 tNear = min(t0, t1);
    float3 tFar = max(t0, t1);
    float enter = max(max(tNear.x, tNear.y), max(tNear.z, 0.0));
    float leave = min(min(tFar.x, tFar.y), tFar.z);
    if (enter >= leave)
    {
        return -1.0;
    }

    float3 start = origin + direction * (enter + 1e-4);
    float2 grid = (start.xz + halfWidth) / Spacing;
    float2 cell = clamp(floor(grid), 0.0, Columns - 1.0);
    float2 stepDir = sign(direction.xz);
    float2 delta = abs(Spacing / (direction.xz + (direction.xz == 0.0) * 1e-6));
    float2 next = ((cell + max(stepDir, 0.0)) * Spacing - halfWidth - origin.xz) / (direction.xz + (direction.xz == 0.0) * 1e-6);

    int steps = Columns * 2 + 2;
    [loop]
    for (int i = 0; i < steps; i++)
    {
        float2 q = cellCentre(cell);
        float height = dropHeight(q);
        float3 c = float3(q.x, height, q.y);
        float t = dropHit(origin, direction, c);
        if (t > 0.0)
        {
            centre = c;
            return t;
        }
        // The wire: a hair-thin line from the droplet's top straight up, drawn by how close the
        // ray passes to it, at least a pixel wide so it never flickers.
        float2 across = direction.xz;
        float acrossLength = max(length(across), 1e-5);
        float2 toWire = q - origin.xz;
        float along = dot(toWire, across) / (acrossLength * acrossLength);
        float miss = abs(toWire.x * across.y - toWire.y * across.x) / acrossLength;
        float y = origin.y + direction.y * along;
        float width = max(0.004, PixelAngle * along * 0.7);
        float onWire = along > 0.0 && y > height + DropSize.y * 0.9 ? 1.0 : 0.0;
        wires += onWire * saturate(1.0 - miss / width) * 0.004 / width * exp(-max(y - 1.0, 0.0) * 0.8);

        // Step to whichever neighbouring cell the ray reaches first.
        if (next.x < next.y)
        {
            cell.x += stepDir.x;
            next.x += delta.x;
        }
        else
        {
            cell.y += stepDir.y;
            next.y += delta.y;
        }
        if (any(cell < 0.0) || any(cell > Columns - 1.0) || min(next.x, next.y) > leave + Spacing)
        {
            break;
        }
    }
    return -1.0;
}

// The hall: a warm, dim atrium with rows of small lamps in the ceiling, a band of glowing
// windows round the walls and a large soft light overhead. All of it shows in the copper.
float3 hall(float3 direction)
{
    float up = direction.y;
    float3 colour = lerp(float3(0.018, 0.012, 0.009), float3(0.006, 0.005, 0.006), saturate(up * 1.5 + 0.3));
    // A polished floor far below holds a blurred, warm copy of the lamps.
    colour += float3(0.3, 0.17, 0.09) * smoothstep(0.02, -0.25, up) * (0.4 + 0.6 * exp(-abs(up + 0.2) * 4.0));

    // Ceiling lamps.
    if (up > 0.15)
    {
        float2 onCeiling = direction.xz / up * 3.0;
        float2 cell = floor(onCeiling);
        float2 inCell = frac(onCeiling) - 0.5;
        float lamp = smoothstep(0.12, 0.04, length(inCell)) * step(0.45, hash(cell));
        float warmth = hash(cell + 17.0);
        colour += lerp(float3(1.0, 0.55, 0.25), float3(1.0, 0.8, 0.55), warmth) * lamp * (2.0 + Level * 2.5) * saturate((up - 0.15) * 4.0);
    }
    // A big soft panel straight up.
    colour += float3(1.0, 0.85, 0.65) * smoothstep(0.9, 0.96, up) * 2.5;

    // Far round the walls, a scatter of soft amber lights, brightening with the mids.
    float around = atan2(direction.z, direction.x) / (2.0 * PI) * 60.0;
    float slot = floor(around);
    float lampHeight = 0.02 + hash(float2(slot, 9.0)) * 0.22;
    float2 offset = float2((frac(around) - 0.5) * 0.105, up - lampHeight);
    float wallLamp = smoothstep(0.03, 0.022, length(offset)) * step(0.35, hash(float2(slot, 4.0)));
    colour += float3(1.0, 0.55, 0.25) * wallLamp * (0.6 + Mid * 1.2);
    return colour;
}

// Copper's mirror colour: its own tint face on, turning white at grazing angles.
float3 copperReflect(float3 normal, float3 direction)
{
    float facing = saturate(dot(normal, -direction));
    return lerp(Copper, 1.0, pow(1.0 - facing, 5.0));
}

float3 dropNormal(float3 position, float3 centre)
{
    return normalize((position - centre) / (DropSize * DropSize));
}

float3 shadeDrop(float3 position, float3 centre, float3 direction, bool deep)
{
    float3 normal = dropNormal(position, centre);
    float3 mirrored = reflect(direction, normal);
    float3 seen = hall(mirrored);
    if (deep)
    {
        // One more bounce: the droplet sees its neighbours, and they show the hall in turn.
        float3 otherCentre;
        float wires;
        float t = traceDrops(position + normal * 2e-3, mirrored, otherCentre, wires);
        if (t > 0.0)
        {
            float3 there = position + normal * 2e-3 + mirrored * t;
            float3 otherNormal = dropNormal(there, otherCentre);
            seen = copperReflect(otherNormal, mirrored) * hall(reflect(mirrored, otherNormal)) * 0.9;
        }
        seen += Copper * wires * 0.3;
    }
    // Droplets low in the grid sit a little in the shadow of those above.
    float shade = 0.75 + 0.25 * saturate(centre.y + 0.8);
    // Sharp highlights from three warm spotlights, and a soft glow of copper all round.
    float3 lit = Copper * 0.08;
    float3 lights[3] = { normalize(float3(0.5, 0.8, 0.3)), normalize(float3(-0.7, 0.5, -0.2)), normalize(float3(0.1, -0.4, -1.0)) };
    float3 tints[3] = { float3(1.0, 0.8, 0.6), float3(1.0, 0.55, 0.3), float3(0.9, 0.7, 0.55) };
    [unroll]
    for (int i = 0; i < 3; i++)
    {
        lit += tints[i] * pow(saturate(dot(mirrored, lights[i])), 90.0) * 6.0 * (i == 2 ? 0.5 + Bass : 1.0);
    }
    return copperReflect(normal, direction) * (seen + lit) * shade;
}

float3 Render(float2 uv, float2 p)
{
    // Wandering slowly round beneath the sculpture, looking up into it.
    float orbit = Time * 0.04 + 0.6;
    float3 eye = float3(sin(orbit) * 4.6, -2.7 + sin(Time * 0.05) * 0.3, cos(orbit) * 4.6);
    float3 target = float3(0.0, 0.1, 0.0);
    const float zoom = 1.45;
    float3 direction = cameraRay(eye, target, float2(p.x, -p.y), zoom, 0.0);
    PixelAngle = pixelSize() / zoom;

    float3 colour = hall(direction) * 0.6;
    float3 centre;
    float wires;
    float t = traceDrops(eye, direction, centre, wires);
    if (t > 0.0)
    {
        colour = shadeDrop(eye + direction * t, centre, direction, true);
    }
    // The wires catch a warm glint from the lamps.
    colour += float3(1.0, 0.7, 0.45) * wires * (0.5 + Treble * 0.6);
    return colour;
}
