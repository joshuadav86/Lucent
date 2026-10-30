// Name: AVS display
// Puts a Winamp AVS preset's picture into Lucent's picture. AVS presets draw on the CPU (Visualizer/Avs in
// Lucent.Core); each frame's picture arrives in AvsPicture and is drawn like a visual of Lucent's own, with
// Common.hlsli in front and PresetMain.hlsli after. The picture's alpha says how to show it:
//
//   0:   as Winamp drew it: its pixels stretched to the screen as they are, and (the engine sets PlainOutput)
//        without Lucent's glow and tone-mapping.
//   255: modernised: smoothly enlarged, then brought into Lucent's light, so it goes through the same
//        sharpening, glow, tone-mapping and transitions as Lucent's own visuals and its brightest parts bloom.

Texture2D AvsPicture : register(t3);

// The picture between its pixels with a Catmull-Rom curve rather than straight lines, for edges that stay
// crisp when enlarged. Nine bilinear reads stand in for the sixteen pixels it weighs.
float3 smoothPicture(float2 uv, float2 size)
{
    float2 position = uv * size - 0.5;
    float2 start = floor(position) + 0.5;
    float2 f = position - floor(position);

    float2 w0 = f * (-0.5 + f * (1.0 - 0.5 * f));
    float2 w1 = 1.0 + f * f * (-2.5 + 1.5 * f);
    float2 w2 = f * (0.5 + f * (2.0 - 1.5 * f));
    float2 w3 = f * f * (-0.5 + 0.5 * f);
    float2 w12 = w1 + w2;
    float2 offset12 = w2 / w12;

    float2 at0 = (start - 1.0) / size;
    float2 at3 = (start + 2.0) / size;
    float2 at12 = (start + offset12) / size;

    float3 colour = 0.0;
    colour += AvsPicture.SampleLevel(LinearClamp, float2(at0.x, at0.y), 0).rgb * w0.x * w0.y;
    colour += AvsPicture.SampleLevel(LinearClamp, float2(at12.x, at0.y), 0).rgb * w12.x * w0.y;
    colour += AvsPicture.SampleLevel(LinearClamp, float2(at3.x, at0.y), 0).rgb * w3.x * w0.y;
    colour += AvsPicture.SampleLevel(LinearClamp, float2(at0.x, at12.y), 0).rgb * w0.x * w12.y;
    colour += AvsPicture.SampleLevel(LinearClamp, float2(at12.x, at12.y), 0).rgb * w12.x * w12.y;
    colour += AvsPicture.SampleLevel(LinearClamp, float2(at3.x, at12.y), 0).rgb * w3.x * w12.y;
    colour += AvsPicture.SampleLevel(LinearClamp, float2(at0.x, at3.y), 0).rgb * w0.x * w3.y;
    colour += AvsPicture.SampleLevel(LinearClamp, float2(at12.x, at3.y), 0).rgb * w12.x * w3.y;
    colour += AvsPicture.SampleLevel(LinearClamp, float2(at3.x, at3.y), 0).rgb * w3.x * w3.y;
    return saturate(colour);
}

// The engine tone-maps every visual on its way to the screen; this is the colour that comes out as the
// given one, so AVS's mid-tones keep their brightness (see Presets/ClassicSpectrum.hlsl).
float3 beforeToneMap(float3 shown)
{
    float3 colour = min(shown, 0.98);
    float3 a = 2.43 * colour - 2.51;
    float3 b = 0.59 * colour - 0.03;
    float3 c = 0.14 * colour;
    return (-b - sqrt(max(b * b - 4.0 * a * c, 0.0))) / (2.0 * a);
}

float3 Render(float2 uv, float2 p)
{
    float width, height;
    AvsPicture.GetDimensions(width, height);
    float2 size = float2(width, height);

    if (AvsPicture.Load(int3(0, 0, 0)).a < 0.5)
    {
        // As Winamp drew it: each of its pixels a little block on screen.
        int2 pixel = min(int2(uv * size), int2(size) - 1);
        return AvsPicture.Load(int3(pixel, 0)).rgb;
    }

    float3 colour = smoothPicture(uv, size);

    // AVS's 8-bit colours as light: a touch of extra colour (AVS's palettes were made for CRTs), then the
    // brightest parts pushed past white so Lucent's bloom picks them out.
    float luminance = dot(colour, float3(0.2126, 0.7152, 0.0722));
    colour = saturate(lerp(luminance.xxx, colour, 1.1));
    float3 light = beforeToneMap(colour * 0.92);
    float peak = max(colour.r, max(colour.g, colour.b));
    return light * (1.0 + 0.9 * smoothstep(0.7, 1.0, peak));
}
