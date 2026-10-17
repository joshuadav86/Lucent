// Name: Electric Storm
// White-hot lightning cracks through a churning storm on every beat, leaving afterimages that ripple outward and melt through the spectrum.
// Family: Organic
// Tags: Intense, Dark

// Sideways wander of a bolt at x (in units of the bolt's length), from octave `first` up to `last`.
float stormWander(float x, float seed, int first, int last)
{
    float total = 0.0;
    [unroll]
    for (int octave = 0; octave < 6; octave++)
    {
        if (octave >= first && octave <= last)
        {
            float frequency = pow(2.3, octave);
            total += (noise(float2(x * frequency, seed + octave * 7.3)) - 0.5) * pow(0.56, octave);
        }
    }
    return total;
}

// Distance from p to a jagged bolt from start to end. Far from the bolt only the broad shape is
// worked out; close to it the fine zig-zags are added, measured square to the bolt so steep
// zig-zags keep the same width.
float boltDistance(float2 p, float2 start, float2 end, float roughness, float seed)
{
    float2 along = end - start;
    float span = length(along);
    float2 direction = along / span;
    float2 d = p - start;
    float t = dot(d, direction) / span;
    float across = dot(d, float2(-direction.y, direction.x));
    float beyond = t < 0.0 ? length(d) : (t > 1.0 ? length(p - end) : 0.0);
    float taper = sin(PI * saturate(t));
    float scale = span * roughness * taper;
    float x = t * 4.0;

    float broad = stormWander(x, seed, 0, 1) * scale;
    float gap = abs(across - broad);
    if (gap < 0.32 * scale + 0.06)
    {
        const float h = 0.002;
        float detail = stormWander(x, seed, 2, 5) * scale;
        float detailAhead = (stormWander(x + h, seed, 0, 5) - stormWander(x - h, seed, 0, 5)) * scale;
        float slope = detailAhead / (2.0 * h / 4.0 * span);
        float vertical = abs(across - broad - detail);
        gap = vertical / lerp(sqrt(1.0 + slope * slope), 1.0, saturate(vertical / 0.012));
    }
    return max(gap, beyond);
}

// Turns a colour around the grey axis.
float3 stormHueTurn(float3 colour, float angle)
{
    const float3 grey = float3(0.57735, 0.57735, 0.57735);
    float s, c;
    sincos(angle, s, c);
    return colour * c + cross(grey, colour) * s + grey * dot(grey, colour) * (1.0 - c);
}

// A forked bolt: a main channel and three branches. Returns the distance to the nearest part
// (x) and a light value with crisp cores (y).
float2 forkedBolt(float2 p, float2 start, float2 end, float roughness, float seed, float pixel, float thickness)
{
    float core = boltDistance(p, start, end, roughness, seed);
    float light = exp(-core / (pixel * thickness)) * 2.5 + exp(-core * 28.0) * 0.4;
    float nearest = core;

    float2 axis = normalize(end - start);
    float2 side = float2(-axis.y, axis.x);
    float span = length(end - start);
    [unroll]
    for (int b = 0; b < 3; b++)
    {
        float k = b;
        float at = 0.18 + 0.25 * k + hash(float2(seed, 4.0 + k)) * 0.12;
        float2 root = lerp(start, end, at) + side * stormWander(at * 4.0, seed, 0, 5) * span * roughness * sin(PI * at);
        float turn = (hash(float2(seed, 9.0 + k)) > 0.5 ? 1.0 : -1.0) * (0.5 + hash(float2(seed, 11.0 + k)) * 0.6);
        float reach = span * (0.16 + hash(float2(seed, 13.0 + k)) * 0.2);
        float branch = boltDistance(p, root, root + rotate(axis, turn) * reach, roughness * 1.4, seed + 17.0 + k);
        light += (exp(-branch / (pixel * thickness * 0.7)) * 1.4 + exp(-branch * 40.0) * 0.15) * (1.0 - k * 0.2);
        nearest = min(nearest, branch);
    }
    return float2(nearest, light);
}

float3 Render(float2 uv, float2 p)
{
    float pixel = pixelSize();

    // Colours drift slowly: violet and electric blue, then magenta and cyan.
    float hue = 0.74 + 0.08 * sin(Time * 0.043);
    float3 violet = hsv2rgb(hue, 0.7, 1.0);
    float3 electric = hsv2rgb(hue - 0.2, 0.75, 1.0);

    // Churning storm clouds with crisp, curling edges, lit faintly from within.
    float2 drift = float2(Time * 0.03, Time * 0.01);
    float2 warp = float2(fbm(p * 1.3 + drift), fbm(p * 1.3 + 5.1 - drift)) - 0.5;
    float cloud = fbm(p * 2.0 + warp * 1.8 + drift * 2.0);
    float billow = smoothstep(0.42, 0.62, cloud);
    float rim = exp(-abs(cloud - 0.52) * 40.0);
    float3 colour = violet * (billow * 0.05 + rim * 0.07);

    // Crawlers: webs of lightning that creep across the sky, each living a few seconds.
    float glowLight = 0.0;
    [unroll]
    for (int c = 0; c < 1; c++)
    {
        float k = c;
        float life = Time * 0.4 + k * 0.5;
        float generation = floor(life);
        float age = frac(life);
        float seed = k * 31.0 + generation * 3.0;
        float2 centre = float2((hash(float2(seed, 21.0)) - 0.5) * 1.6, (hash(float2(seed, 22.0)) - 0.5) * 1.2);
        float2 heading = rotate(float2(1.0, 0.0), (hash(float2(seed, 23.0)) - 0.5) * 1.2);
        float span = 1.3 + hash(float2(seed, 24.0)) * 1.2;
        float2 bolt = forkedBolt(p, centre - heading * span * 0.5, centre + heading * span * 0.5, 0.28, seed + age * 0.35, pixel, 0.8);
        float level = spectrum(0.35 + k * 0.3);
        float pulse = (0.3 + level * 1.0) * smoothstep(0.0, 0.12, age) * smoothstep(1.0, 0.8, age);
        colour += lerp(electric, float3(1.0, 1.0, 1.0), saturate(bolt.y * 0.25)) * bolt.y * pulse * 0.6;
        glowLight += exp(-bolt.x * 6.0) * pulse * 0.4;
    }

    // Strikes: on every beat a fresh bolt cracks down through the clouds, flickering as it fades.
    float flicker = 0.55 + 0.45 * step(0.35, hash(float2(floor(Time * 30.0), BeatCount)));
    float strike = Beat * Beat * flicker;
    float seed = BeatCount * 1.37;
    float2 top = float2((hash(float2(seed, 1.0)) - 0.5) * 2.4, -1.15);
    float2 bottom = top + float2((hash(float2(seed, 2.0)) - 0.5) * 1.4, 2.35);
    float2 strikeBolt = forkedBolt(p, top, bottom, 0.22, seed, pixel, 1.4);
    colour += lerp(violet, float3(1.0, 1.0, 1.0), saturate(strikeBolt.y * 0.4)) * strikeBolt.y * strike * 1.5;
    glowLight += exp(-strikeBolt.x * 3.0) * strike * (1.0 + Bass);

    // The lightning lights up the clouds around it.
    colour += lerp(violet, electric, 0.3) * (billow * 0.5 + rim * 0.7 + 0.05) * glowLight;

    // Afterimages: each strike stays burned in where it struck, slowly turning round the colour
    // wheel as it fades. Reading last frame exactly in place keeps the old bolts sharp, and taking
    // the brighter of old and new keeps the fresh lightning on top.
    float3 echo = stormHueTurn(previous(uv), DeltaTime * 1.6) * exp(-DeltaTime * 2.2);
    return max(colour, max(echo, 0.0));
}
