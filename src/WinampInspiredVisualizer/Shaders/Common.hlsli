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
    float2 Resolution;  // size of the image being drawn, in pixels (larger than the window when supersampling)
    float Time;         // seconds since the visualizer started
    float DeltaTime;    // seconds since the last frame
    float Bass;         // 0..1, smoothed
    float Mid;          // 0..1, smoothed
    float Treble;       // 0..1, smoothed
    float Beat;         // 1 on a beat, fading back to 0
    float Level;        // overall loudness, 0..1
    float BeatCount;    // number of beats so far
    float Aspect;       // width / height
    float Quality;      // detail setting from the options menu: 0 low, 1 normal, 2 high, 3 ultra
    float TransitionProgress;  // 0..1 while blending between two presets (used by the engine)
    float TransitionStyle;     // which blend pattern the engine is using
    float BloomStrength;       // glow amount from the options menu (used by the engine)
    float RenderScale;         // drawn size / window size: above 1 when supersampling (used by the engine)
    float HdrOutput;           // 1 when drawing for an HDR screen (used by the engine)
    float HdrWhite;            // that screen's normal white, where 1 = 80 nits (used by the engine)
    float HdrPeak;             // that screen's brightest white, same units (used by the engine)
    float FrameCount;          // frames drawn so far
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

// Vivid colour gradients. t wraps around every 1.0.
float3 palette(float t)
{
    return 0.5 + 0.5 * cos(2.0 * PI * (t + float3(0.0, 0.33, 0.67)));
}

// Neon gradient: magentas, cyans and electric blues.
float3 neon(float t)
{
    return 0.5 + 0.5 * cos(2.0 * PI * (float3(1.0, 1.0, 0.5) * t + float3(0.8, 0.9, 0.3)));
}

// Floored modulo that also works for negative numbers (fmod keeps the sign).
float wrap(float x, float m)
{
    return x - m * floor(x / m);
}

float2 wrap(float2 x, float m)
{
    return x - m * floor(x / m);
}

float3 wrap(float3 x, float m)
{
    return x - m * floor(x / m);
}

// Mirrors the plane into a number of identical wedges, like a kaleidoscope.
float2 kaleidoscope(float2 p, float segments)
{
    float wedge = 2.0 * PI / segments;
    float angle = abs(wrap(atan2(p.y, p.x), wedge) - wedge * 0.5);
    return float2(cos(angle), sin(angle)) * length(p);
}

// Distance from p to the outline of a regular polygon with the given number of sides.
float polygon(float2 p, float sides, float radius)
{
    float segment = 2.0 * PI / sides;
    float angle = atan2(p.y, p.x);
    return cos(floor(0.5 + angle / segment) * segment - angle) * length(p) - radius;
}

// Ray direction for a camera at eye looking towards target, for fly-through presets.
// zoom above 1 narrows the view; roll tilts the horizon.
float3 cameraRay(float3 eye, float3 target, float2 p, float zoom, float roll)
{
    float3 forward = normalize(target - eye);
    float3 side = normalize(float3(forward.z, 0.0, -forward.x));
    float3 up = cross(forward, side);
    p = rotate(p, roll);
    return normalize(forward * zoom + p.x * side + p.y * up);
}

// How much extra work the Detail setting asks for: 0.6 on Low, 1 on Normal, 1.5 on High, 2 on Ultra.
float detailScale()
{
    return 0.6 + Quality * 0.4 + max(Quality - 1.0, 0.0) * 0.1;
}

// Scales a loop count (ray-march steps, fractal iterations) by the Detail setting.
// Only use it for [loop] loops.
int qualitySteps(int normalSteps)
{
    return max((int)(normalSteps * detailScale()), 1);
}

// How close a ray must get to count as touching a surface, relative to Normal: 1.67x on Low
// down to 0.5x on Ultra, so finer surface detail shows up at higher settings.
float surfacePrecision()
{
    return 1.0 / detailScale();
}

// Fractal depth for the Detail setting: one level fewer on Low, one more on Ultra.
// Only use it for [loop] loops.
int fractalDepth(int normalDepth)
{
    return max(normalDepth + (Quality > 2.5 ? 1 : 0) - (Quality < 0.5 ? 1 : 0), 1);
}

// The size of one screen pixel in p units. Lines drawn this many pixels wide look the same
// at every resolution setting; drawing above window resolution just makes them smoother.
float pixelSize()
{
    return 2.0 * max(RenderScale, 1.0) / Resolution.y;
}
