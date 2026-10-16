// The visualizer's own shaders: a fullscreen triangle, and the final pass
// that adds glow and tone-maps the feedback buffer onto the screen.
// Common.hlsli is placed in front of this file before compiling.

VSOut VSMain(uint id : SV_VertexID)
{
    VSOut output;
    float2 uv = float2((id << 1) & 2, id & 2);
    output.position = float4(uv * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
    output.uv = uv;
    return output;
}

float4 PSPresent(VSOut input) : SV_Target
{
    float2 uv = input.uv;
    float3 colour = previous(uv);

    // A cheap bloom: average a ring of nearby samples and add it back on top.
    float3 bloom = 0.0;
    float2 pixel = 1.0 / Resolution;
    for (int i = 0; i < 12; i++)
    {
        float angle = i * (2.0 * PI / 12.0);
        float2 offset = float2(cos(angle), sin(angle)) * pixel * 9.0;
        bloom += previous(uv + offset) + previous(uv + offset * 2.5);
    }
    colour += bloom / 24.0 * 0.45;

    // Soft tone-mapping so bright trails roll off instead of clipping.
    colour = 1.0 - exp(-colour * 1.2);

    float2 fromCentre = uv - 0.5;
    colour *= 1.0 - dot(fromCentre, fromCentre) * 0.7;
    return float4(colour, 1.0);
}
