// Name: Supernova Remnant
// Shells of star-stuff laced with glowing filaments burst outward from a spinning pulsar, one with every beat, as its lighthouse beams sweep through the wreckage.

static const int ShellCount = 5;

float novaHash(float3 p)
{
    p = frac(p * float3(0.1031, 0.1030, 0.0973));
    p += dot(p, p.yxz + 33.33);
    return frac((p.x + p.y) * p.z);
}

float novaNoise(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    float3 u = f * f * (3.0 - 2.0 * f);
    float n000 = novaHash(i);
    float n100 = novaHash(i + float3(1.0, 0.0, 0.0));
    float n010 = novaHash(i + float3(0.0, 1.0, 0.0));
    float n110 = novaHash(i + float3(1.0, 1.0, 0.0));
    float n001 = novaHash(i + float3(0.0, 0.0, 1.0));
    float n101 = novaHash(i + float3(1.0, 0.0, 1.0));
    float n011 = novaHash(i + float3(0.0, 1.0, 1.0));
    float n111 = novaHash(i + float3(1.0, 1.0, 1.0));
    return lerp(lerp(lerp(n000, n100, u.x), lerp(n010, n110, u.x), u.y),
                lerp(lerp(n001, n101, u.x), lerp(n011, n111, u.x), u.y), u.z);
}

// Glowing filaments on a shell, at unit direction d: thin ridges where warped noise crosses
// its middle value.
float novaFilaments(float3 d, float seed)
{
    float3 q = d * 2.6 + seed;
    float warp = novaNoise(q * 0.9 + 11.0);
    float n = novaNoise(q * 1.7 + warp * 2.2);
    // Clumpy: some patches of the shell are thick with filaments, others nearly empty.
    float clumps = smoothstep(0.3, 0.7, warp);
    return pow(1.0 - abs(2.0 * n - 1.0), 14.0) * (0.1 + 0.9 * clumps);
}

// Light from a thin shell of radius `radius` and thickness `thick` seen through pixel p:
// the path through the shell grows towards its edge, so it glows brightest at the limb.
// x: brightness of the filaments, y: brightness of the shell's even glow.
float2 novaShell(float2 p, float radius, float thick, float3x3 turn, float seed)
{
    float rr = dot(p, p);
    float outer = sqrt(max(radius * radius - rr, 0.0));
    float innerRadius = radius - thick;
    float inner = sqrt(max(innerRadius * innerRadius - rr, 0.0));
    float path = (outer - inner) / thick;
    if (path <= 0.0)
    {
        return float2(0.0, 0.0);
    }
    float middle = radius - thick * 0.5;
    float depth = sqrt(max(middle * middle - rr, 0.0));
    float3 front = mul(turn, float3(p, -depth) / middle);
    float3 back = mul(turn, float3(p, depth) / middle);
    float threads = novaFilaments(front, seed) + novaFilaments(back, seed) * 0.6;
    return float2(threads, 1.0) * path;
}

float3 Render(float2 uv, float2 p)
{
    // Drift slowly around the remnant, closer and further away.
    float zoom = 1.0 + 0.18 * sin(Time * 0.05);
    p = (p - float2(0.12 * sin(Time * 0.031), 0.06 * sin(Time * 0.043))) * zoom;
    float pixel = pixelSize() * zoom;
    float rho = length(p);
    float angle = atan2(p.y, p.x);

    // A slowly turning view, so the filaments roll around the shells.
    float yaw = Time * 0.07;
    float pitch = 0.5 * sin(Time * 0.037);
    float sy, cy, sp, cp;
    sincos(yaw, sy, cy);
    sincos(pitch, sp, cp);
    float3x3 turn = float3x3(cy, 0.0, sy,
                             sp * sy, cp, -sp * cy,
                             -cp * sy, sp, cp * cy);

    // Colours: filaments, shock fronts and the pulsar's haze drift through three schemes.
    float phase = Time / 50.0;
    float blend = smoothstep(0.75, 1.0, frac(phase));
    float scheme = floor(phase);
    float3 threadA = float3(1.0, 0.18, 0.4);
    float3 threadB = float3(1.0, 0.5, 0.12);
    float3 threadC = float3(0.65, 0.25, 1.0);
    float3 shockA = float3(0.2, 0.85, 1.0);
    float3 shockB = float3(0.3, 0.45, 1.0);
    float3 shockC = float3(0.25, 1.0, 0.75);
    float pick = wrap(scheme, 3.0);
    float3 thread = pick < 0.5 ? lerp(threadA, threadB, blend) : (pick < 1.5 ? lerp(threadB, threadC, blend) : lerp(threadC, threadA, blend));
    float3 shock = pick < 0.5 ? lerp(shockA, shockB, blend) : (pick < 1.5 ? lerp(shockB, shockC, blend) : lerp(shockC, shockA, blend));

    // Background stars, pin-sharp.
    float3 colour = float3(0.004, 0.003, 0.012);
    [unroll]
    for (int layer = 0; layer < 2; layer++)
    {
        float scale = 40.0 + layer * 35.0;
        float2 grid = p * scale + layer * 13.7;
        float2 cell = floor(grid);
        float2 spot = float2(hash(cell + 0.7), hash(cell + 5.3)) * 0.8 + 0.1;
        float gap = length(frac(grid) - spot) / (scale * pixel);
        float bright = step(0.98, hash(cell + 2.9)) * pow(hash(cell + 4.1), 2.0) * 1.4;
        colour += lerp(float3(1.0, 0.85, 0.7), float3(0.7, 0.8, 1.0), hash(cell + 7.7)) * bright * exp(-gap * gap * 1.5);
    }

    // The pulsar's lighthouse beams sweep round, lighting whatever they cross.
    float spin = Time * (1.1 + Treble * 0.6);
    float beamAngle = wrap(angle - spin, PI) - PI * 0.5;
    float beam = exp(-beamAngle * beamAngle * 40.0);

    // Shells: one is thrown off with every beat and pushed outward by each beat after it.
    float push = 1.0 - Beat * Beat;
    [loop]
    for (int n = 0; n < ShellCount; n++)
    {
        float age = n + push;                                 // in beats
        float birth = BeatCount - n;
        float radius = 0.1 + 0.42 * pow(age, 0.8);
        float fade = pow(saturate(1.0 - age / ShellCount), 1.5);
        float seed = hash(float2(birth, 3.1)) * 40.0;
        float2 light = novaShell(p, radius, radius * 0.07, turn, seed);
        float3 hot = lerp(float3(1.0, 1.0, 1.0), shock, saturate(age * 2.0));
        float3 tint = lerp(hot, thread, saturate(age / ShellCount * 1.6 - 0.2));
        // A young shell's leading edge is a crisp, bright shock front.
        float front = max(light.y - 2.0, 0.0) * 0.3 * exp(-age * 2.0);
        colour += tint * (light.x * 0.9 + light.y * 0.04 + front) * fade * (1.0 + beam * 2.0);
    }

    // The pulsar wind nebula: a swirl of blue haze around the star.
    float2 swirl = rotate(p, rho * 3.0 - Time * 0.2);
    float wisps = fbm(swirl * 4.0 + 3.0);
    colour += lerp(shock, float3(0.6, 0.5, 1.0), 0.5) * exp(-rho * 5.0) * (0.15 + wisps * 0.5) * (0.6 + Bass * 0.8);

    // The beams themselves, and the pulsar: a pin-sharp star with crisp spikes.
    float across = abs(sin(angle - spin)) * rho;
    float cone = across / (pixel + rho * 0.04);
    colour += float3(0.8, 0.9, 1.0) * exp(-cone * cone * 0.5) * exp(-rho * 3.5) * 1.5;
    float2 spikeSpace = abs(rotate(p, 0.3));
    float spikes = exp(-spikeSpace.x / pixel * 0.9) * exp(-spikeSpace.y * 6.0) + exp(-spikeSpace.y / pixel * 0.9) * exp(-spikeSpace.x * 6.0);
    colour += float3(0.9, 0.95, 1.0) * (exp(-rho / pixel * 0.7) * 4.0 + spikes * 0.6) * (0.8 + Beat * 1.5);
    return colour;
}
