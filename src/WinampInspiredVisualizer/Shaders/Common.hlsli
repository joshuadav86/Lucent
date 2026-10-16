// Shared inputs and helpers available to every preset.
// A preset is a .hlsl file in the Presets folder that defines:
//
//     float3 Render(float2 uv, float2 p)
//
// uv: 0..1 across the screen (0,0 is top-left).
// p:  centred coordinates, -1..1 vertically, wider horizontally to keep circles round.
// Return the colour for this pixel. Call previous(...) to read the last frame;
// warping where you read it from is what creates the classic trails and tunnels.

cbuffer Params : register(b0)
{
    float2 Resolution;  // output size in pixels
    float Time;         // seconds since the visualizer started
    float DeltaTime;    // seconds since the last frame
    float Bass;         // 0..1, smoothed
    float Mid;          // 0..1, smoothed
    float Treble;       // 0..1, smoothed
    float Beat;         // 1 on a beat, fading back to 0
    float Level;        // overall loudness, 0..1
    float BeatCount;    // number of beats so far
    float Aspect;       // width / height
    float ParamsPad0;
    float TransitionProgress;  // 0..1 while blending between two presets (used by the engine)
    float TransitionStyle;     // which blend pattern the engine is using
    float2 ParamsPad1;
};

Texture2D PreviousFrame : register(t0);
Texture2D AudioData : register(t1);
SamplerState LinearClamp : register(s0);

static const float PI = 3.14159265;

struct VSOut
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

// Loudness of the frequency at x (0 = deep bass, 1 = high treble), 0..1.
float spectrum(float x)
{
    return AudioData.SampleLevel(LinearClamp, float2(saturate(x), 0.25), 0).r;
}

// The audio waveform at x (0..1 across the screen), -1..1.
float waveform(float x)
{
    return AudioData.SampleLevel(LinearClamp, float2(saturate(x), 0.75), 0).r;
}

// The colour of the last frame at uv.
float3 previous(float2 uv)
{
    return PreviousFrame.SampleLevel(LinearClamp, uv, 0).rgb;
}

// Converts centred coordinates back to uv.
float2 toUv(float2 p)
{
    return p / (float2(Aspect, 1.0) * 2.0) + 0.5;
}

float2 rotate(float2 v, float angle)
{
    float s, c;
    sincos(angle, s, c);
    return float2(c * v.x - s * v.y, s * v.x + c * v.y);
}

float3 hsv2rgb(float h, float s, float v)
{
    float3 k = saturate(abs(frac(h + float3(0.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0) - 1.0);
    return v * lerp(float3(1.0, 1.0, 1.0), k, s);
}

// Bright at distance 0, falling off quickly. Higher sharpness = thinner line.
float glow(float distance, float sharpness)
{
    return exp(-abs(distance) * sharpness);
}

float hash(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float noise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    float a = hash(i);
    float b = hash(i + float2(1.0, 0.0));
    float c = hash(i + float2(0.0, 1.0));
    float d = hash(i + float2(1.0, 1.0));
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

float fbm(float2 p)
{
    float total = 0.0;
    float amplitude = 0.5;
    for (int octave = 0; octave < 5; octave++)
    {
        total += noise(p) * amplitude;
        p = p * 2.03 + float2(17.0, 9.0);
        amplitude *= 0.5;
    }
    return total;
}
