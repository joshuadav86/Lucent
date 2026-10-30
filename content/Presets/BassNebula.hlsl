// Name: Bass Nebula
// Drift down a winding cavern carved through a glowing nebula: rose and violet gas laced with fine filaments and dark lanes of dust, lit from within by young blue-white stars that turn the gas around them teal. The gas swells and brightens with the bass, every beat surges the flight forward and flares the stars, and the treble sets the far stars twinkling.
// Family: Cosmic
// Tags: Calm, Space, 3D, Colourful
// Added: 2026-10-16 19:56

// The nebula is a cloud of glowing gas filling all of space, with a clear winding channel through it
// that the camera follows. Each pixel's ray marches through the gas adding up the light it gives off
// and how much of the light behind it the gas and dust swallow, the way real emission nebulae look:
// bright where the gas is thick and hot, dark where cold dust lies in front.

// Turns each octave of the turbulence so their patterns never line up.
static const float3x3 OctaveTurn = float3x3(0.00, 0.80, 0.60, -0.80, 0.36, -0.48, -0.60, -0.48, 0.64);
static const float StarCell = 3.0;
static const float StarReach = 0.72;

// The centre of the clear channel at depth z.
float2 channel(float z)
{
    return float2(sin(z * 0.11) * 1.8 + sin(z * 0.047) * 1.2, cos(z * 0.083) * 1.3);
}

// How far the camera has flown: steady drift, plus a surge forward on every beat.
float flight()
{
    return Time * 0.55 + max(BeatCount - Beat, 0.0) * 0.32;
}

float gyroid(float3 p)
{
    return dot(sin(p), cos(p.yzx));
}

// Turbulent gas: octaves of a turned, warped gyroid. billow is the soft large-scale shape (about -1..1),
// the return value is the thin filaments and sheets where each octave crosses zero.
float turbulence(float3 p, int octaves, out float billow)
{
    float amplitude = 1.0;
    float total = 0.0;
    float sheets = 0.0;
    billow = 0.0;
    [loop]
    for (int i = 0; i < octaves; i++)
    {
        float g = gyroid(p);
        billow += g * amplitude;
        float edge = saturate(1.0 - abs(g) * 1.3);
        sheets += edge * edge * edge * edge * amplitude;
        total += amplitude;
        p = mul(OctaveTurn, p) * 2.13 + float3(0.0, Time * 0.025, 1.7);
        p += sin(p.zxy * 0.7) * 0.45;
        amplitude *= 0.55;
    }
    billow /= total * 1.2;
    return sheets / total;
}

struct Gas
{
    float glow;  // how brightly the gas glows here
    float dust;  // how much it swallows the light behind it
    float heat;  // 0..1, coral through rose and magenta to violet
    float threads;  // how much of the glow is fine filaments, which glow warmer
};

Gas sampleGas(float3 p, int octaves)
{
    Gas gas = (Gas)0;
    float clearing = length(p.xy - channel(p.z));
    float walls = smoothstep(1.0, 3.0, clearing) * (0.7 + 0.3 * smoothstep(5.5, 2.5, clearing));
    [branch]
    if (walls <= 0.0)
    {
        return gas;
    }

    // A slow swirl and a large warp keep the turbulence from looking like a repeating pattern.
    float3 q = p * 0.42;
    q.xy = rotate(q.xy, q.z * 0.07 + Time * 0.012);
    q += sin(q.zxy * 0.9 + float3(0.0, Time * 0.05, 2.1)) * 0.75;

    float billow;
    float sheets = turbulence(q, octaves, billow);

    float swell = Bass * 0.35 + DropPulse * 0.25;
    float body = smoothstep(-0.15, 0.85, billow + swell);
    gas.glow = (body * body * 0.3 + sheets * sheets * (0.5 + body * 4.0) * (1.0 + Bass * 0.8)) * walls;
    // Dust gathers where the gas is thickest, in its own lanes, so dark ribbons cross the bright clouds.
    float lanes = saturate(gyroid(q * 1.7 + 4.0) * 0.8 + billow * 0.9 - 0.15);
    gas.dust = (body * 0.5 + lanes * lanes * 2.2) * walls;
    // Broad regions of different colour, drifting slowly.
    gas.heat = saturate(0.45 + sin(dot(p, float3(0.31, 0.23, 0.17)) + Time * 0.04) * cos(p.z * 0.13 - p.x * 0.21) * 0.55 + billow * 0.2 - Mid * 0.12);
    gas.threads = saturate(sheets * sheets * 3.0);
    return gas;
}

// The colour the gas glows: coral and hydrogen rose through magenta to violet.
float3 gasColour(float heat)
{
    float3 coral = float3(1.0, 0.42, 0.26);
    float3 rose = float3(1.0, 0.16, 0.4);
    float3 magenta = float3(1.0, 0.18, 0.72);
    float3 violet = float3(0.42, 0.22, 1.0);
    float3 colour = lerp(coral, rose, smoothstep(0.1, 0.3, heat));
    colour = lerp(colour, magenta, smoothstep(0.55, 0.75, heat));
    colour = lerp(colour, violet, smoothstep(0.82, 1.0, heat));
    return colour;
}

// The star in a cell of space, if it has one: position (xyz) and brightness (w, 0 for none).
float4 cellStar(float3 cell)
{
    float a = hash(cell.xy + cell.z * 17.31);
    float b = hash(cell.yz + cell.x * 11.17 + 3.1);
    float c = hash(cell.zx + cell.y * 7.73 + 8.4);
    // Each star sits well inside its cell, so its light (see StarReach) never crosses into the next one.
    float3 at = (cell + 0.25 + float3(a, b, c) * 0.5) * StarCell;
    // Keep the channel itself clear of stars, so none fly straight through the camera.
    float clearing = length(at.xy - channel(at.z));
    float present = step(0.62, hash(cell.xz * 0.71 + cell.y * 3.3)) * smoothstep(0.9, 1.6, clearing);
    return float4(at, present * (0.4 + c * 0.6));
}

// The colour of a star: mostly hot blue-white, a few warm.
float3 starColour(float3 cell)
{
    float warm = hash(cell.yx * 1.3 + cell.z);
    return warm > 0.8 ? float3(1.0, 0.82, 0.62) : float3(0.7, 0.86, 1.0);
}

// Faraway stars behind the nebula, in two layers; the treble sets them twinkling.
float3 farStars(float3 direction)
{
    float3 light = 0.0;
    [unroll]
    for (int layer = 0; layer < 2; layer++)
    {
        float scale = layer == 0 ? 90.0 : 220.0;
        float3 q = direction * scale;
        float3 cell = floor(q);
        float3 offset = float3(hash(cell.xy + cell.z * 3.1), hash(cell.yz + cell.x * 5.7), hash(cell.zx + cell.y * 2.3));
        float3 at = cell + 0.25 + offset * 0.5;
        float d = length(q - at);
        float chance = hash(cell.xz + cell.y * 9.1);
        float present = step(layer == 0 ? 0.9 : 0.8, chance);
        float twinkle = 0.6 + 0.4 * sin(Time * (3.0 + offset.x * 6.0) + offset.y * 40.0) * Treble + Treble * 0.4;
        float size = layer == 0 ? 0.09 : 0.06;
        light += starColour(cell) * present * exp(-d * d / (size * size)) * (layer == 0 ? 1.2 : 0.6) * twinkle;
    }
    return light;
}

float3 render(float3 eye, float3 direction, float jitter)
{
    float3 light = 0.0;
    float transmittance = 1.0;
    float flare = 1.0 + Beat * 1.2 + DropPulse * 1.5;
    float brightness = 0.7 + Bass * 0.6 + Level * 0.15;

    int steps = qualitySteps(52);
    float stepSize = 0.1;
    float t = stepSize * jitter;
    [loop]
    for (int i = 0; i < steps; i++)
    {
        if (transmittance < 0.03 || t > 26.0)
        {
            break;
        }
        float3 p = eye + direction * t;
        // Far away, the finest octaves are smaller than a pixel: leave them out.
        int octaves = t < 4.0 ? 5 : (t < 10.0 ? 4 : 3);
        Gas gas = sampleGas(p, octaves);

        // The star in this cell of space, if there is one, lights the gas near it, turning it teal and
        // white close in. Its light fades out before the cell's edge, so no seams show between cells.
        float3 starLight = 0.0;
        float starPower = 0.0;
        float3 cell = floor(p / StarCell);
        float4 star = cellStar(cell);
        [branch]
        if (star.w > 0.0)
        {
            float3 toStar = star.xyz - p;
            float d2 = dot(toStar, toStar);
            float band = spectrum(hash(cell.xy + cell.z) * 0.6 + 0.02);
            float reach = smoothstep(StarReach * StarReach, 0.0, d2);
            starPower = star.w * (1.0 + band * 1.5) * flare / (1.0 + d2 * 9.0) * reach;
            starLight = starColour(cell) * starPower * starPower;

            // The star itself, added once: when this step passes its closest approach to the ray.
            float along = dot(toStar, direction);
            if (along >= 0.0 && along < stepSize)
            {
                float apart = sqrt(max(d2 - along * along, 0.0));
                float miss = apart / max(t, 0.5);
                float core = exp(-miss * miss * 3e4) * 2.2 + exp(-miss * 120.0) * 0.35 * smoothstep(StarReach, 0.2, apart);
                light += transmittance * starColour(cell) * star.w * core * flare * (1.0 + band);
            }
        }

        float3 lit = lerp(gasColour(gas.heat), float3(1.0, 0.68, 0.38), gas.threads * 0.45) * brightness;
        lit = lerp(lit, float3(0.15, 0.85, 0.95) * 1.2, saturate(starPower * 0.9));
        lit += starLight * 0.6;
        float3 emitted = lit * gas.glow * (1.0 + starPower * 1.5);

        float sigma = gas.dust * 2.0 + gas.glow * 0.2;
        float absorbed = exp(-sigma * stepSize);
        light += transmittance * emitted * stepSize * 0.9;

        transmittance *= absorbed;
        // Stride through the clear channel, step carefully through the gas.
        t += stepSize * (gas.glow + gas.dust > 0.0 ? 1.0 : 1.6);
        stepSize = min(stepSize * 1.05, 0.55);
    }

    return light + farStars(direction) * transmittance;
}

float3 Render(float2 uv, float2 p)
{
    float z = flight();
    float3 eye = float3(channel(z), z);
    float3 ahead = float3(channel(z + 3.0), z + 3.0);
    float roll = sin(Time * 0.031) * 0.25 + (channel(z + 2.0).x - eye.x) * 0.12;
    float3 direction = cameraRay(eye, ahead, float2(p.x, -p.y), 1.2, roll);

    // A fresh offset for each pixel and frame hides the steps of the march; blending in a little of
    // the last frame smooths the grain away.
    float jitter = hash(uv * Resolution + frac(FrameCount * 0.618) * 97.0);
    float3 colour = render(eye, direction, jitter);
    return lerp(previous(uv), colour, FrameCount < 2.0 ? 1.0 : 0.75);
}
