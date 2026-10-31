// The engine's own passes for MilkDrop presets: the warp mesh, waves and shapes, the blurs, and
// the classic warp and composite that MilkDrop 1 presets (and MilkDrop 2 shaders that don't
// compile) use. MilkDrop.hlsli is placed in front of this file before compiling.

// The warp mesh's points, and the waves' and shapes' triangles, uploaded each frame by the CPU.
struct MdMeshPoint
{
    float2 position;
    float2 uv;
    float2 uvOrig;
    float2 radAng;
};

struct MdDrawPoint
{
    float2 position;
    float2 uv;
    float4 colour;
};

StructuredBuffer<MdMeshPoint> MdMesh : register(t12);
StructuredBuffer<MdDrawPoint> MdDraw : register(t13);

// Each blur pass: the step between taps in the source (xy) and how to unpack the source (zw),
// then how to pack the result (xy).
cbuffer MdBlurParams : register(b1)
{
    float4 md_blur_step;
    float4 md_blur_pack;
};

struct MdDrawVertex
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
    float4 colour : COLOR0;
};

// Two triangles per mesh cell, made from the vertex number so no index buffer is needed.
MdWarpVertex VSWarp(uint id : SV_VertexID)
{
    uint across = (uint)md_mesh_x;
    uint cell = id / 6;
    uint corner = id % 6;
    uint2 offset = uint2(corner == 1 || corner == 4 || corner == 5 ? 1 : 0, corner == 2 || corner == 3 || corner == 5 ? 1 : 0);
    uint2 at = uint2(cell % across, cell / across) + offset;
    MdMeshPoint p = MdMesh[at.y * (across + 1) + at.x];

    MdWarpVertex output;
    output.position = float4(p.position, 0.0, 1.0);
    output.uv = float4(p.uv, p.uvOrig);
    output.radAng = p.radAng;
    return output;
}

MdScreenVertex VSScreen(uint id : SV_VertexID)
{
    MdScreenVertex output;
    float2 uv = float2((id << 1) & 2, id & 2);
    output.position = float4(uv * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
    output.uv = uv;
    return output;
}

// Where this batch of waves and shapes starts in MdDraw. Metal's vertex_id counts from the draw's first
// vertex, but Direct3D's SV_VertexID starts at 0 in every draw, so the Windows app (which defines
// MD_DRAW_FIRST) says where each batch starts. Without it, every batch after the first drew the first
// one's triangles again, so a wave drawn after motion vectors came out as the motion vectors.
#ifdef MD_DRAW_FIRST
cbuffer MdDrawParams : register(b2)
{
    uint md_draw_first;
};
#else
static const uint md_draw_first = 0;
#endif

MdDrawVertex VSDraw(uint id : SV_VertexID)
{
    MdDrawPoint p = MdDraw[id + md_draw_first];
    MdDrawVertex output;
    output.position = float4(p.position, 0.0, 1.0);
    output.uv = p.uv;
    output.colour = p.colour;
    return output;
}

// MilkDrop 1's warp: last frame, read through the mesh and faded by decay. With decay below 1
// it rounds down like MilkDrop's 8-bit blending did, so trails fade all the way to black.
float4 PSWarpClassic(MdWarpVertex input) : SV_Target
{
    float3 colour = md_wrap > 0.5
        ? MdMain.SampleLevel(MdLinearWrap, input.uv.xy, 0).rgb
        : MdMain.SampleLevel(MdLinearClamp, input.uv.xy, 0).rgb;
    float roundDown = md_decay < 0.9999 ? 0.5 / 255.0 : 0.0;
    return float4(max(colour * md_decay - roundDown, 0.0), 1.0);
}

// MilkDrop 1's composite: video echo, gamma, the hue shader, then brighten, darken, solarize and invert.
float4 PSCompClassic(MdScreenVertex input) : SV_Target
{
    float2 uv = input.uv;
    float3 colour = MdMain.SampleLevel(MdLinearClamp, uv, 0).rgb;
    if (md_echo_alpha > 0.001)
    {
        float2 echo = (uv - 0.5) / md_echo_zoom;
        int orient = (int)md_echo_orient;
        if (orient == 1 || orient == 3)
        {
            echo.x = -echo.x;
        }

        if (orient >= 2)
        {
            echo.y = -echo.y;
        }

        colour = lerp(colour, MdMain.SampleLevel(MdLinearWrap, echo + 0.5, 0).rgb, md_echo_alpha);
    }

    colour *= md_gamma;
    if (md_fshader >= 1.0)
    {
        colour *= MdHueShader(uv);
    }
    else if (md_fshader > 0.001)
    {
        colour *= (1.0 - md_fshader) + md_fshader * MdHueShader(uv);
    }

    colour = saturate(colour);
    if (md_brighten > 0.5)
    {
        colour = sqrt(colour);
    }

    if (md_darken > 0.5)
    {
        colour = colour * colour;
    }

    if (md_solarize > 0.5)
    {
        colour = colour * (1.0 - colour) * 4.0;
    }

    if (md_invert > 0.5)
    {
        colour = 1.0 - colour;
    }

    return float4(colour, 1.0);
}

float4 PSDraw(MdDrawVertex input) : SV_Target
{
    return input.colour;
}

// Textured shapes show last frame inside them, tinted by their colour.
float4 PSDrawTextured(MdDrawVertex input) : SV_Target
{
    return MdMain.SampleLevel(MdLinearWrap, input.uv, 0) * input.colour;
}

// The blur passes: a 9-tap Gaussian (5 bilinear reads) across, then down. The source is read
// from MdMain (t0) and unpacked; the second pass packs its result into the blur's min..max range.
float3 blurTap(float2 uv)
{
    return MdMain.SampleLevel(MdLinearClamp, uv, 0).rgb * md_blur_step.z + md_blur_step.w;
}

float3 blur9(float2 uv, float2 step)
{
    return blurTap(uv) * 0.2270270
        + (blurTap(uv + step * 1.3846154) + blurTap(uv - step * 1.3846154)) * 0.3162162
        + (blurTap(uv + step * 3.2307692) + blurTap(uv - step * 3.2307692)) * 0.0702703;
}

float4 PSBlurAcross(MdScreenVertex input) : SV_Target
{
    return float4(blur9(input.uv, float2(md_blur_step.x, 0.0)), 1.0);
}

float4 PSBlurDown(MdScreenVertex input) : SV_Target
{
    float3 colour = blur9(input.uv, float2(0.0, md_blur_step.y));
    return float4(saturate(colour * md_blur_pack.x + md_blur_pack.y), 1.0);
}
