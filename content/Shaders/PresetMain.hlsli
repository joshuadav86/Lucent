// Appended after a preset's code to turn its Render() into a pixel shader.

float4 PSMain(VSOut input) : SV_Target
{
    float2 uv = input.uv;
    float2 p = (uv - 0.5) * float2(Aspect, 1.0) * 2.0;
    return float4(max(Render(uv, p), 0.0), 1.0);
}
