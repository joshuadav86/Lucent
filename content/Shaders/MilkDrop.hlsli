// What MilkDrop presets' shaders can use, as MilkDrop 2 gives it to them. Lucent puts this in
// front of every MilkDrop 2 warp and composite shader (see MilkShaderTranslator) and of
// MilkDrop.hlsl, the engine's own MilkDrop passes. The constants are filled by
// MilkPresetRunner.WriteConstants, in this order.

cbuffer MilkParams : register(b0)
{
    float4 aspect;          // aspectx, aspecty, 1 / aspectx, 1 / aspecty
    float4 texsize;         // the main image: width, height, 1 / width, 1 / height
    float time;
    float fps;
    float frame;
    float progress;
    float bass;
    float mid;
    float treb;
    float vol;
    float bass_att;
    float mid_att;
    float treb_att;
    float vol_att;
    float4 rand_frame;      // new random numbers every frame
    float4 rand_preset;     // random numbers chosen when the preset starts
    float4 roam_cos;        // slowly wandering 0..1 values
    float4 roam_sin;
    float4 slow_roam_cos;
    float4 slow_roam_sin;
    float4 _qa;             // q1..q32, four at a time
    float4 _qb;
    float4 _qc;
    float4 _qd;
    float4 _qe;
    float4 _qf;
    float4 _qg;
    float4 _qh;
    float blur1_min;
    float blur1_max;
    float blur2_min;
    float blur2_max;
    float blur3_min;
    float blur3_max;
    float md_decay;         // the classic (shaderless) warp and composite's settings
    float md_gamma;
    float md_echo_zoom;
    float md_echo_alpha;
    float md_echo_orient;
    float md_fshader;
    float md_brighten;
    float md_darken;
    float md_solarize;
    float md_invert;
    float md_wrap;
    float md_mesh_x;        // warp mesh cells across and down
    float md_mesh_y;
    float blur1_edge_darken;
    float4 md_hue[4];       // hue_shader's colours at the four corners
    row_major float4x3 rot_s1;
    row_major float4x3 rot_s2;
    row_major float4x3 rot_s3;
    row_major float4x3 rot_s4;
    row_major float4x3 rot_d1;
    row_major float4x3 rot_d2;
    row_major float4x3 rot_d3;
    row_major float4x3 rot_d4;
    row_major float4x3 rot_f1;
    row_major float4x3 rot_f2;
    row_major float4x3 rot_f3;
    row_major float4x3 rot_f4;
    row_major float4x3 rot_vf1;
    row_major float4x3 rot_vf2;
    row_major float4x3 rot_vf3;
    row_major float4x3 rot_vf4;
    row_major float4x3 rot_uf1;
    row_major float4x3 rot_uf2;
    row_major float4x3 rot_uf3;
    row_major float4x3 rot_uf4;
    row_major float4x3 rot_rand1;
    row_major float4x3 rot_rand2;
    row_major float4x3 rot_rand3;
    row_major float4x3 rot_rand4;
};

Texture2D MdMain : register(t0);        // the main image (last frame in the warp, this frame in the composite)
Texture2D MdBlur1 : register(t1);       // blurred copies of it, each blurrier and smaller
Texture2D MdBlur2 : register(t2);
Texture2D MdBlur3 : register(t3);
Texture2D MdNoiseLq : register(t4);     // 256 x 256 random colours
Texture2D MdNoiseLqLite : register(t5); // 32 x 32 random colours
Texture2D MdNoiseMq : register(t6);     // 256 x 256, smooth at a 4-pixel scale
Texture2D MdNoiseHq : register(t7);     // 256 x 256, smooth at an 8-pixel scale
Texture3D MdNoiseVolLq : register(t8);  // 32 x 32 x 32 random colours
Texture3D MdNoiseVolHq : register(t9);  // 32 x 32 x 32, smooth at a 4-pixel scale
SamplerState MdLinearWrap : register(s0);
SamplerState MdLinearClamp : register(s1);
SamplerState MdPointWrap : register(s2);
SamplerState MdPointClamp : register(s3);

struct MdWarpVertex
{
    float4 position : SV_Position;
    float4 uv : TEXCOORD0;       // where to read last frame from (xy), and where this point is (zw)
    float2 radAng : TEXCOORD1;
};

struct MdScreenVertex
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

#define M_PI 3.14159265359
#define M_PI_2 6.28318530718
#define M_INV_PI_2 0.159154943091895
#define lum(x) (dot((x), float3(0.32, 0.49, 0.29)))

#define q1 _qa.x
#define q2 _qa.y
#define q3 _qa.z
#define q4 _qa.w
#define q5 _qb.x
#define q6 _qb.y
#define q7 _qb.z
#define q8 _qb.w
#define q9 _qc.x
#define q10 _qc.y
#define q11 _qc.z
#define q12 _qc.w
#define q13 _qd.x
#define q14 _qd.y
#define q15 _qd.z
#define q16 _qd.w
#define q17 _qe.x
#define q18 _qe.y
#define q19 _qe.z
#define q20 _qe.w
#define q21 _qf.x
#define q22 _qf.y
#define q23 _qf.z
#define q24 _qf.w
#define q25 _qg.x
#define q26 _qg.y
#define q27 _qg.z
#define q28 _qg.w
#define q29 _qh.x
#define q30 _qh.y
#define q31 _qh.z
#define q32 _qh.w

// DirectX 9's tex2D and friends, with the sampler split into a texture and a sampler state.
// Always the top level: these textures have no smaller copies, and it keeps loops legal.
float4 md_tex2D(Texture2D t, SamplerState s, float2 uv)
{
    return t.SampleLevel(s, uv, 0);
}

float4 md_tex2Dlod(Texture2D t, SamplerState s, float4 uv)
{
    return t.SampleLevel(s, uv.xy, 0);
}

float4 md_tex2Dbias(Texture2D t, SamplerState s, float4 uv)
{
    return t.SampleLevel(s, uv.xy, 0);
}

float4 md_tex2Dproj(Texture2D t, SamplerState s, float4 uv)
{
    return t.SampleLevel(s, uv.xy / (abs(uv.w) < 1e-6 ? 1e-6 : uv.w), 0);
}

float4 md_tex3D(Texture3D t, SamplerState s, float3 uvw)
{
    return t.SampleLevel(s, uvw, 0);
}

#define GetMain(uv) (MdMain.SampleLevel(MdLinearWrap, (uv), 0).xyz)
// MilkDrop 2's include.fx reads GetPixel through sampler_main too, so it wraps like GetMain.
// Clamping it collapsed tunnel shaders such as martin - starfield sectors onto the edge pixels.
#define GetPixel(uv) (MdMain.SampleLevel(MdLinearWrap, (uv), 0).xyz)
#define GetBlur1(uv) (MdBlur1.SampleLevel(MdLinearClamp, (uv), 0).xyz * (blur1_max - blur1_min) + blur1_min)
#define GetBlur2(uv) (MdBlur2.SampleLevel(MdLinearClamp, (uv), 0).xyz * (blur2_max - blur2_min) + blur2_min)
#define GetBlur3(uv) (MdBlur3.SampleLevel(MdLinearClamp, (uv), 0).xyz * (blur3_max - blur3_min) + blur3_min)

// hue_shader: the four corner colours blended across the screen.
float3 MdHueShader(float2 uv)
{
    return lerp(lerp(md_hue[0], md_hue[1], uv.x), lerp(md_hue[2], md_hue[3], uv.x), uv.y).rgb;
}

// rad and ang for a point on screen, as the warp mesh has them: 0 at the middle, about 1 in the corners.
float2 MdCentred(float2 uv)
{
    return float2(uv.x - 0.5, 0.5 - uv.y) * 2.0 * aspect.xy;
}

float MdRad(float2 uv)
{
    return length(MdCentred(uv)) * 0.7071067;
}

float MdAng(float2 uv)
{
    float2 p = MdCentred(uv);
    return atan2(p.y, p.x);
}
