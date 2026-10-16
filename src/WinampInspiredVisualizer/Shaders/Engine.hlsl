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
    float2 pixel = 1.0 / Resolution;
    float3 colour = previous(uv);

    // Sharpen: push each pixel away from its neighbours' average. This undoes
    // the slight blur that builds up as the feedback buffer is re-sampled.
    float3 neighbours = (previous(uv + float2(pixel.x, 0.0)) + previous(uv - float2(pixel.x, 0.0))
                       + previous(uv + float2(0.0, pixel.y)) + previous(uv - float2(0.0, pixel.y))) * 0.25;
    colour = max(colour + (colour - neighbours) * 0.6, 0.0);

    // Bloom only the brightest parts, so highlights glow but dark areas stay crisp and black.
    float3 bloom = 0.0;
    for (int i = 0; i < 12; i++)
    {
        float angle = i * (2.0 * PI / 12.0);
        float2 offset = float2(cos(angle), sin(angle)) * pixel * 6.0;
        bloom += max(previous(uv + offset) - 0.6, 0.0) + max(previous(uv + offset * 2.5) - 0.6, 0.0);
    }
    colour += bloom / 24.0 * 0.6;

    // Filmic tone-mapping: deep blacks, punchy midtones, highlights that roll off.
    colour = saturate((colour * (2.51 * colour + 0.03)) / (colour * (2.43 * colour + 0.59) + 0.14));

    // A little extra saturation, and a light vignette.
    float luminance = dot(colour, float3(0.2126, 0.7152, 0.0722));
    colour = saturate(lerp(luminance.xxx, colour, 1.15));
    float2 fromCentre = uv - 0.5;
    colour *= 1.0 - dot(fromCentre, fromCentre) * 0.35;
    return float4(colour, 1.0);
}

// While switching presets, the old preset's output is in PreviousFrame and the
// new one's in NextFrame. This reveals the new one with one of a few patterns.
Texture2D NextFrame : register(t2);

float4 PSBlend(VSOut input) : SV_Target
{
    float2 uv = input.uv;
    float3 from = PreviousFrame.SampleLevel(LinearClamp, uv, 0).rgb;
    float3 to = NextFrame.SampleLevel(LinearClamp, uv, 0).rgb;
    float t = TransitionProgress;
    float2 p = (uv - 0.5) * float2(Aspect, 1.0) * 2.0;
    int style = (int)TransitionStyle;

    float mask;
    if (style == 1)
    {
        // A ring expanding out from the centre.
        float reach = length(float2(Aspect, 1.0)) + 0.3;
        mask = 1.0 - smoothstep(t * reach - 0.3, t * reach, length(p));
    }
    else if (style == 2)
    {
        // A cloudy dissolve.
        float n = fbm(p * 2.5 + 3.0);
        mask = smoothstep(n - 0.1, n + 0.1, t * 1.2 - 0.1);
    }
    else if (style == 3)
    {
        // A spiral sweep.
        float sweep = frac(atan2(p.y, p.x) / (2.0 * PI) + 0.5 + length(p) * 0.35);
        mask = smoothstep(sweep - 0.15, sweep + 0.15, t * 1.3 - 0.15);
    }
    else
    {
        // A plain crossfade.
        mask = t;
    }

    // A glowing seam along the edge of the reveal.
    float seam = style == 0 ? 0.0 : mask * (1.0 - mask) * 4.0;
    float3 seamColour = hsv2rgb(Time * 0.1, 0.6, 0.35) * seam;
    return float4(lerp(from, to, mask) + seamColour, 1.0);
}
