// Name: Ocean of Light
// Gliding over a night sea whose breaking crests blaze with bioluminescent lace beneath wheeling star trails; every beat sends a ring of light racing across the water.

static const float2x2 SeaTurn = float2x2(1.6, 1.2, -1.2, 1.6);

// One octave of choppy waves (after Alexander Alekseev's "Seascape").
float seaOctave(float2 uv, float choppy)
{
    uv += noise(uv) * 2.0 - 1.0;
    float2 wave = 1.0 - abs(sin(uv));
    float2 swell = abs(cos(uv));
    wave = lerp(wave, swell, wave);
    return pow(1.0 - pow(wave.x * wave.y, 0.65), choppy);
}

float seaHeight(float2 position, int octaves)
{
    float frequency = 0.24;
    float amplitude = 0.5 + Bass * 0.2;
    float choppy = 4.0;
    float2 uv = position * float2(0.75, 1.0);
    float seaTime = 1.0 + Time * 0.9;
    float height = 0.0;
    [loop]
    for (int i = 0; i < octaves; i++)
    {
        float d = seaOctave((uv + seaTime) * frequency, choppy) + seaOctave((uv - seaTime) * frequency, choppy);
        height += d * amplitude;
        uv = mul(SeaTurn, uv);
        frequency *= 1.9;
        amplitude *= 0.22;
        choppy = lerp(choppy, 1.0, 0.2);
    }
    return height;
}

// Star trails: every star draws an arc around the celestial pole, and the arcs creep round.
float3 starTrails(float3 direction, int layers)
{
    float3 pole = normalize(float3(-0.6, 0.55, 0.6));
    float3 east = normalize(cross(pole, float3(0.0, 1.0, 0.0)));
    float3 north = cross(east, pole);
    float reach = acos(clamp(dot(direction, pole), -1.0, 1.0));
    float around = atan2(dot(direction, north), dot(direction, east)) + Time * 0.03;

    float3 colour = 0.0;
    [loop]
    for (int layer = 0; layer < layers; layer++)
    {
        float spacing = 0.006 + layer * 0.0035;
        float band = floor(reach / spacing);
        float seed = band * 3.1 + layer * 17.0;
        float radius = (band + 0.2 + 0.6 * hash(float2(seed, 1.0))) * spacing;
        float start = hash(float2(seed, 2.0)) * 2.0 * PI;
        float span = 0.25 + hash(float2(seed, 3.0)) * 0.6;
        float into = wrap(around - start, 2.0 * PI);
        float arc = step(into, span) * smoothstep(0.0, 0.05, into) * (0.4 + 0.6 * into / span);
        float width = abs(reach - radius) / pixelSize();
        float brightness = pow(hash(float2(seed, 4.0)), 3.0) * 0.9;
        float3 tint = lerp(float3(1.0, 0.75, 0.55), float3(0.6, 0.75, 1.0), hash(float2(seed, 5.0)));
        colour += tint * brightness * arc * exp(-width * width * 1.2);
    }
    return colour;
}

float3 seaSky(float3 direction, float3 moon, int layers)
{
    float height = max(direction.y, 0.0);
    float3 colour = lerp(float3(0.02, 0.05, 0.1), float3(0.0, 0.004, 0.015), pow(height, 0.4));
    colour += float3(0.0, 0.25, 0.3) * exp(-height * 25.0) * 0.12;
    float moonAngle = acos(saturate(dot(direction, moon)));
    colour += float3(0.5, 0.6, 0.8) * exp(-moonAngle * 6.0) * 0.08;
    colour += float3(1.0, 0.97, 0.9) * smoothstep(0.03, 0.027, moonAngle) * 3.0;
    return colour + starTrails(direction, layers) * smoothstep(0.0, 0.05, direction.y) * smoothstep(0.05, 0.12, moonAngle);
}

float3 Render(float2 uv, float2 p)
{
    float travel = Time * 1.6;
    float3 eye = float3(sin(Time * 0.05) * 3.0, 3.4 + 0.4 * sin(Time * 0.11), travel);
    float3 target = eye + float3(sin(Time * 0.07) * 1.5, -0.75 - 0.1 * sin(Time * 0.09), 4.0);
    // (p.y grows downward on screen, so flip it to keep the sky up.)
    float3 direction = cameraRay(eye, target, float2(p.x, -p.y), 1.3, sin(Time * 0.13) * 0.05);
    float3 moon = normalize(float3(-0.35 + 0.1 * sin(Time * 0.02), 0.2, 1.0));

    float3 colour = 0.0;
    if (direction.y >= 0.0)
    {
        colour = seaSky(direction, moon, qualitySteps(3));
    }
    else
    {
        // Find the water between the highest a wave can reach and the lowest trough, closing in
        // on it from both sides.
        int traceOctaves = qualitySteps(2);
        const float crestTop = 1.8;
        float near = (eye.y - crestTop) / -direction.y;
        float far = min(eye.y / -direction.y, 600.0);
        float3 nearPoint = eye + direction * near;
        float heightNear = nearPoint.y - seaHeight(nearPoint.xz, traceOctaves);
        float3 farPoint = eye + direction * far;
        float heightFar = farPoint.y - seaHeight(farPoint.xz, traceOctaves);
        float travelled = far;
        int steps = qualitySteps(4);
        [loop]
        for (int i = 0; i < steps; i++)
        {
            travelled = lerp(near, far, heightNear / (heightNear - heightFar));
            float3 probe = eye + direction * travelled;
            float heightMid = probe.y - seaHeight(probe.xz, traceOctaves);
            if (heightMid < 0.0)
            {
                far = travelled;
                heightFar = heightMid;
            }
            else
            {
                near = travelled;
                heightNear = heightMid;
            }
        }
        float3 hit = eye + direction * travelled;

        // Surface normal from the detailed waves.
        int detail = qualitySteps(4);
        float e = 0.002 * travelled + 0.01;
        float level = seaHeight(hit.xz, detail);
        float3 normal = normalize(float3(level - seaHeight(hit.xz + float2(e, 0.0), detail), e,
                                         level - seaHeight(hit.xz + float2(0.0, e), detail)));

        // The sky and moon mirrored in the water: a glittering path of moonlight.
        float facing = saturate(dot(normal, -direction));
        float fresnel = 0.04 + 0.96 * pow(1.0 - facing, 5.0);
        float3 mirrored = reflect(direction, normal);
        mirrored.y = abs(mirrored.y);
        float glint = pow(saturate(dot(mirrored, moon)), 900.0) * 6.0 + pow(saturate(dot(mirrored, moon)), 60.0) * 0.25;
        float3 water = float3(0.0, 0.012, 0.025) + (seaSky(mirrored, moon, 0) + float3(1.0, 0.95, 0.85) * glint) * fresnel;

        // Bioluminescence: the sharp cusps of the big waves blaze, and crisp foam lace glows
        // across the crests.
        float2 swellUv = hit.xz * float2(0.75, 1.0) * 0.24;
        float seaTime = (1.0 + Time * 0.9) * 0.24;
        float swell = (seaOctave(swellUv + seaTime, 4.0) + seaOctave(swellUv - seaTime, 4.0)) * 0.5;
        float crestLine = pow(saturate(swell), 14.0);
        float crest = smoothstep(0.85, 1.35, level);
        float lace = 0.0;
        if (crest > 0.0)
        {
            float2 foamUv = hit.xz * 2.2 + float2(Time * 0.3, 0.0);
            float foamField = fbm(foamUv + noise(foamUv * 0.6 - Time * 0.2) * 1.4);
            lace = pow(1.0 - abs(foamField * 2.0 - 1.0), 14.0);
        }
        // The plankton's colour rolls across the sea in long slow waves, sea-green to violet.
        float hue = 0.6 + 0.16 * sin(hit.x * 0.11 + hit.z * 0.045 - Time * 0.2) + 0.05 * sin(hit.z * 0.13 + Time * 0.07);
        float3 plankton = hsv2rgb(hue, 0.85, 1.0);
        float glowing = crestLine * 1.6 + crest * (lace * 1.8 + 0.03);
        water += plankton * glowing * (0.8 + Mid * 1.2);

        // Points of light flaring in the foam with the treble.
        float2 cell = floor(hit.xz * 6.0);
        float2 spot = float2(hash(cell + 1.3), hash(cell + 7.1)) * 0.8 + 0.1;
        float dotSize = length(frac(hit.xz * 6.0) - spot);
        float sparkle = step(0.8, hash(cell + floor(Time * 6.0))) * exp(-dotSize * dotSize * 400.0) * crest * (0.4 + Treble * 2.0);
        water += plankton * sparkle * 2.0 * smoothstep(5.0, 10.0, travelled);

        // A ring of light races out across the water on every beat.
        float2 origin = eye.xz + float2(sin(BeatCount * 2.3) * 4.0, 10.0 + 3.0 * sin(BeatCount * 1.7));
        float ring = abs(length(hit.xz - origin) - (1.0 - Beat) * 14.0);
        water += plankton * exp(-ring * 3.0) * Beat * (0.4 + crest * 2.0) * 1.5;

        // Fade into the night haze at the horizon.
        float haze = exp(-travelled * 0.012);
        colour = lerp(seaSky(float3(direction.x, 0.0, direction.z), moon, 0), water, haze);
    }
    return colour;
}
