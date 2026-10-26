// Turns a finished frame into a picture the video encoder takes (NV12, see Export/VideoFrameConverter.cs):
// brightness (Y) at full size, and colour (Cb and Cr together) at half the width and height.
// BT.709 colours in the 16-235 "TV" range, which is what players expect from an H.264 video.
// Unlike the engine's shaders, this file stands alone (Common.hlsli isn't placed in front of it).

Texture2D<float4> Frame : register(t0);
SamplerState LinearClamp : register(s0);

struct VSOut
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

// A triangle that covers the whole target, as the engine's VSMain does.
VSOut VSMain(uint id : SV_VertexID)
{
    VSOut output;
    float2 uv = float2((id << 1) & 2, id & 2);
    output.position = float4(uv * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
    output.uv = uv;
    return output;
}

static const float3 LumaWeights = float3(0.2126, 0.7152, 0.0722);

// One brightness value per frame pixel.
float PSLuma(VSOut input) : SV_Target
{
    float3 rgb = saturate(Frame.Load(int3(input.position.xy, 0)).rgb);
    return (16.0 + 219.0 * dot(rgb, LumaWeights)) / 255.0;
}

// One colour pair per 2 x 2 frame pixels. Each target pixel's centre sits on the corner the four
// share, so one bilinear tap averages them.
float2 PSChroma(VSOut input) : SV_Target
{
    float3 rgb = saturate(Frame.Sample(LinearClamp, input.uv).rgb);
    float luma = dot(rgb, LumaWeights);
    float2 colour = float2((rgb.b - luma) / 1.8556, (rgb.r - luma) / 1.5748);
    return (128.0 + 224.0 * colour) / 255.0;
}
