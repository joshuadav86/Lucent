// Name: Cathedral Light
// Walk slowly down a hazy gothic nave as sunlight pours through tall stained glass windows in shafts of coloured light, laying jewelled patterns across the stone floor; the glass glows brighter where the music is loudest, and specks of dust glint as they drift through the beams.
// Family: Ray traced
// Tags: Calm, Colourful, 3D
// Added: 2026-10-17 17:12
// Particles: 131072

// The nave runs along z, repeating every bay: a pair of stone columns each side and a tall
// pointed window in the outer wall between them. The sun is low on the left, so its light
// crosses the nave through the left-hand windows. Whether a point is lit, and in what colour, is
// found by following a straight line from it towards the sun to the left wall: if that line
// reaches a window without passing a column, the point gets that piece of glass's colour.
// The same test lights the floor, the columns, the haze in the air and the drifting dust.

static const float NaveWidth = 5.0;       // walls at x = -5 and +5
static const float ColumnRow = 3.3;       // columns at x = -3.3 and +3.3
static const float ColumnRadius = 0.4;
static const float BaySize = 4.0;         // columns every 4 along z
static const float RoofHeight = 13.0;
static const float Bays = 8.0;            // the windows' designs repeat every 8 bays
static const float3 SunTravel = float3(0.6, -0.72, 0.3);   // the direction sunlight travels

float3 sunTravel()
{
    return normalize(SunTravel + float3(0.0, 0.0, sin(Time * 0.03) * 0.08));
}

float3 cameraEye()
{
    float z = wrap(Time * 0.32, BaySize * Bays);
    return float3(0.9 + sin(Time * 0.061) * 0.8, 1.7 + 0.25 * sin(Time * 0.083), z);
}

float3 cameraTarget()
{
    float3 eye = cameraEye();
    return eye + float3(-1.6 + sin(Time * 0.047) * 1.2, 0.9 + 0.5 * sin(Time * 0.037), 6.0);
}

static const float Zoom = 1.3;

// ---- Stained glass ----

// Rich glass colours: deep blue, ruby, gold, emerald, violet.
float3 glassPalette(float pick)
{
    if (pick < 0.28) return float3(0.15, 0.3, 1.0);
    if (pick < 0.46) return float3(1.0, 0.15, 0.12);
    if (pick < 0.68) return float3(1.0, 0.72, 0.25);
    if (pick < 0.84) return float3(0.2, 0.85, 0.45);
    return float3(0.65, 0.3, 1.0);
}

// The colour of light coming through the window at (along the wall, height). Black where there's
// stone or lead.
float3 windowGlass(float z, float y)
{
    float bay = floor(z / BaySize);
    float design = wrap(bay, Bays);
    float u = wrap(z, BaySize) - BaySize * 0.5;
    float halfWidth = 0.78;
    const float sill = 2.6;
    const float spring = 6.8;   // where the pointed arch begins
    if (y < sill || abs(u) > halfWidth)
    {
        return 0.0;
    }
    // The pointed arch: inside two overlapping circles.
    float arch = 1.6;
    if (y > spring && (length(float2(u - (arch - halfWidth), y - spring)) > arch || length(float2(u + (arch - halfWidth), y - spring)) > arch))
    {
        return 0.0;
    }
    // Stone tracery: a central mullion splitting two lancets, and a round rose above them.
    float2 rose = float2(u, y - (spring + 0.75));
    float roseRing = abs(length(rose) - 0.42);
    bool inRose = length(rose) < 0.42;
    if (abs(u) < 0.05 && y < spring + 0.25 && !inRose)
    {
        return 0.0;
    }
    if (roseRing < 0.04)
    {
        return 0.0;
    }
    // Small panes in a diamond lattice held by dark lead.
    float2 lattice = inRose ? float2(atan2(rose.y, rose.x) * 1.9, length(rose) * 7.0) : rotate(float2(u, y), PI * 0.25) * 5.5;
    float2 cell = floor(lattice);
    float2 inPane = abs(frac(lattice) - 0.5);
    float lead = smoothstep(0.42, 0.47, max(inPane.x, inPane.y));
    // Each pane's colour; the panes listen to the spectrum from the sill (bass) up to the arch (treble).
    float pick = hash(cell + design * 17.0 + (inRose ? 50.0 : 0.0));
    float band = saturate((y - sill) / 5.6);
    float3 colour = glassPalette(frac(pick + floor(Time * 0.05 + pick) * 0.37));
    float level = 0.35 + spectrum(0.03 + band * 0.75) * 1.6;
    // The glass is uneven, so its light is too.
    colour *= level * (0.75 + 0.5 * hash(cell * 1.7 + 3.0)) * (inRose ? 1.3 : 1.0);
    return colour * (1.0 - lead);
}

// How a column row blocks the line from a point towards the sun (1 clear, 0 blocked).
float columnShade(float3 position, float3 towardsSun, float rowX)
{
    float t = (rowX - position.x) / towardsSun.x;
    if (t <= 0.0)
    {
        return 1.0;
    }
    float3 crossing = position + towardsSun * t;
    float apart = abs(wrap(crossing.z, BaySize) - BaySize * 0.5);
    float distanceToColumn = BaySize * 0.5 - apart;
    return smoothstep(ColumnRadius - 0.05, ColumnRadius + 0.15, distanceToColumn * abs(towardsSun.x) / length(towardsSun.xz));
}

// The sunlight reaching a point inside the nave: coloured by the window it came through.
float3 sunlightAt(float3 position)
{
    float3 towardsSun = -sunTravel();
    float t = (-NaveWidth - position.x) / towardsSun.x;
    float3 onWall = position + towardsSun * t;
    float3 glass = windowGlass(onWall.z, onWall.y);
    if (dot(glass, glass) < 1e-6)
    {
        return 0.0;
    }
    float shade = 1.0;
    if (position.x > -ColumnRow)
    {
        shade *= columnShade(position, towardsSun, -ColumnRow);
    }
    if (position.x > ColumnRow)
    {
        shade *= columnShade(position, towardsSun, ColumnRow);
    }
    return glass * shade * (5.0 + Level * 2.0);
}

// ---- Dust motes (particles) ----

static const float3 DustBox = float3(9.6, 9.0, 16.0);

float3 dustOrigin()
{
    return cameraEye() + float3(-DustBox.x * 0.5 + 0.3, -1.7, -1.0);
}

void Spawn(inout Particle particle, uint id)
{
    particle.position = dustOrigin() + float3(random(id, 1.0), random(id, 2.0), random(id, 3.0)) * DustBox;
    particle.velocity = 0.0;
    particle.life = lerp(20.0, 60.0, random(id, 4.0));
    particle.data = float4(fixedRandom(id, 5.0), 0.0, 0.0, 0.0);
}

void Simulate(inout Particle particle, uint id)
{
    float dt = min(DeltaTime, 0.05);
    float3 drift = curlNoise(particle.position * 0.3 + float3(0.0, Time * 0.02, 0.0)) * 0.05 + float3(0.0, -0.01, 0.0);
    particle.velocity = drift * (1.0 + Mid * 0.8);
    float3 position = particle.position + particle.velocity * dt;
    // Keep them in a box that travels with the camera.
    float3 local = position - dustOrigin();
    local = local - DustBox * floor(local / DustBox);
    particle.position = dustOrigin() + local;
}

float3 toScreen(float3 position)
{
    float3 seen = project(position, cameraEye(), cameraTarget(), Zoom, 0.0);
    return float3(seen.x, -seen.y, seen.z);
}

Sprite Show(Particle particle, uint id)
{
    Sprite sprite = (Sprite)0;
    float3 seen = toScreen(particle.position);
    if (seen.z < 0.3 || abs(particle.position.x) > ColumnRow + 1.0)
    {
        return sprite;
    }
    // Dust only shows where a sunbeam catches it, flashing as it tumbles.
    float3 light = sunlightAt(particle.position);
    float tumble = pow(saturate(sin(Time * lerp(1.5, 5.0, particle.data.x) + particle.data.x * 40.0)), 6.0);
    sprite.position = seen.xy;
    sprite.size = 1.1;
    sprite.colour = (light * 0.3 + 0.8) * light * (0.006 + tumble * 0.1 * (1.0 + Treble)) * saturate(particle.age);
    focus(sprite, seen.z, 5.0, 0.05);
    return sprite;
}

// ---- The nave itself ----

float columnDistance(float3 position)
{
    float2 q = float2(abs(position.x) - ColumnRow, wrap(position.z, BaySize) - BaySize * 0.5);
    q.y = BaySize * 0.5 - abs(q.y);
    // Fluted shafts.
    float angle = atan2(q.y, q.x);
    return length(q) - ColumnRadius - 0.015 * cos(angle * 16.0);
}

float sceneDistance(float3 position)
{
    float walls = NaveWidth - abs(position.x);
    float floorAndRoof = min(position.y, RoofHeight - position.y);
    return min(min(walls, floorAndRoof), columnDistance(position));
}

float3 sceneNormal(float3 position)
{
    const float2 e = float2(0.003, -0.003);
    return normalize(e.xyy * sceneDistance(position + e.xyy) + e.yyx * sceneDistance(position + e.yyx)
                   + e.yxy * sceneDistance(position + e.yxy) + e.xxx * sceneDistance(position + e.xxx));
}

// The stone: floor flags, wall blocks and columns, lit by the coloured sun and a dim cool glow.
float3 stone(float3 position, float3 normal, float3 direction)
{
    float3 albedo = float3(0.42, 0.38, 0.32);
    if (normal.y > 0.5)
    {
        // Big worn flagstones.
        float2 tile = position.xz * 0.8;
        float2 seam = abs(frac(tile) - 0.5);
        albedo *= (0.75 + 0.35 * hash(floor(tile))) * (0.85 + 0.3 * fbm(position.xz * 2.0));
        albedo *= lerp(0.45, 1.0, smoothstep(0.47, 0.44, max(seam.x, seam.y)));
    }
    else if (abs(normal.x) > 0.9 && abs(position.x) > NaveWidth - 0.01)
    {
        float2 block = float2(position.z * 1.1, position.y * 2.2);
        block.x += floor(block.y) * 0.5;
        float2 seam = abs(frac(block) - 0.5);
        albedo *= (0.7 + 0.3 * hash(floor(block))) * lerp(0.6, 1.0, smoothstep(0.48, 0.44, max(seam.x, seam.y))) * 0.6;
    }
    else
    {
        albedo *= 0.9 + 0.2 * noise(position.yz * 3.0);
    }
    float3 light = sunlightAt(position + normal * 0.01) * saturate(dot(normal, -sunTravel()));
    // Light bouncing round the nave: a faint warm glow off the sunny floor, and cool skylight from above.
    float3 fill = float3(0.11, 0.12, 0.16) * (0.55 + 0.45 * normal.y) + float3(0.12, 0.08, 0.07) * (0.4 + Level * 0.4) * saturate(1.0 - position.y * 0.09) * saturate(0.6 - normal.y);
    // Coloured light bouncing up off the sunlit patches of floor nearby.
    float3 floorBelow = float3(position.x + normal.x * 0.8, 0.01, position.z + normal.z * 0.8);
    float3 bounce = sunlightAt(floorBelow) * 0.06 * exp(-position.y * 0.35) * saturate(1.0 - normal.y);
    return albedo * (light + fill + bounce);
}

// What the eye sees at a surface: stone, or glowing glass where a window is.
float3 surface(float3 position, float3 normal, float3 direction)
{
    if (abs(position.x) > NaveWidth - 0.01)
    {
        float3 glass = windowGlass(position.z, position.y);
        if (dot(glass, glass) > 1e-6)
        {
            // The sunny side's windows blaze; the shaded side's only glow.
            float sunny = position.x < 0.0 ? 1.0 : 0.12;
            return glass * (0.9 + Level * 0.5) * sunny * 1.4;
        }
    }
    if (position.y > RoofHeight - 0.05)
    {
        return float3(0.006, 0.006, 0.008);
    }
    return stone(position, normal, direction);
}

float3 Render(float2 uv, float2 p)
{
    float3 eye = cameraEye();
    float3 direction = cameraRay(eye, cameraTarget(), float2(p.x, -p.y), Zoom, 0.0);

    // Find the first surface.
    float t = 0.0;
    int steps = qualitySteps(90);
    [loop]
    for (int i = 0; i < steps; i++)
    {
        float d = sceneDistance(eye + direction * t);
        if (d < 0.002 * t || t > 80.0)
        {
            break;
        }
        t += d;
    }
    t = min(t, 80.0);
    float3 position = eye + direction * t;
    float3 colour = surface(position, sceneNormal(position), direction);

    // The haze: march through the air adding the sunlight scattered towards us at each step.
    // Each pixel starts at a slightly different random spot every frame, and blending with the
    // last frame smooths that into soft beams.
    float3 towardsSun = -sunTravel();
    float cosine = dot(direction, towardsSun);
    float g = 0.55;
    float phase = (1.0 - g * g) / pow(1.0 + g * g - 2.0 * g * cosine, 1.5) * 0.25 + 0.15;
    int samples = qualitySteps(40);
    float reach = min(t, 40.0);
    float stepLength = reach / samples;
    float jitter = hash(uv * Resolution + frac(FrameCount * 0.618) * 97.0);
    float3 scattered = 0.0;
    float transmittance = 1.0;
    [loop]
    for (int s = 0; s < samples; s++)
    {
        float3 at = eye + direction * (stepLength * (s + jitter));
        // Thicker haze low down, with slow drifting swirls of incense.
        float density = 0.09 * (0.5 + 0.8 * noise(at.xz * 0.35 + at.y * 0.2 + Time * 0.03)) * exp(-at.y * 0.06);
        scattered += sunlightAt(at) * density * transmittance * stepLength;
        transmittance *= exp(-density * stepLength * 0.6);
    }
    colour = colour * transmittance + scattered * phase * (1.0 + Beat * 0.15);
    // A faint cool haze in the distance so the nave fades into shadow.
    colour += float3(0.03, 0.03, 0.045) * (1.0 - transmittance);

    colour += particles(uv);
    return lerp(previous(uv), colour, FrameCount < 2.0 ? 1.0 : 0.4);
}
