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

// Shrinks the drawn image to the window when drawing above window resolution (supersampling):
// each screen pixel becomes the average of every drawn pixel under it, for smooth edges and
// fine detail without shimmer. Each bilinear tap already averages 2 x 2 drawn pixels.
float4 PSDownsample(VSOut input) : SV_Target
{
    float scale = max(RenderScale, 1.0);
    int taps = clamp((int)ceil(scale * 0.5), 1, 4);
    float2 footprint = scale / Resolution;  // one screen pixel, in uv
    float3 total = 0.0;
    [loop]
    for (int y = 0; y < taps; y++)
    {
        [loop]
        for (int x = 0; x < taps; x++)
        {
            float2 offset = ((float2(x, y) + 0.5) / taps - 0.5) * footprint;
            total += previous(input.uv + offset);
        }
    }

    return float4(total / (taps * taps), 1.0);
}

// Filmic tone-mapping: deep blacks, punchy midtones, highlights that roll off.
float3 toneMap(float3 colour)
{
    return saturate((colour * (2.51 * colour + 0.03)) / (colour * (2.43 * colour + 0.59) + 0.14));
}

float4 PSPresent(VSOut input) : SV_Target
{
    float2 uv = input.uv;
    float2 pixel = RenderScale / Resolution;            // one screen pixel, in uv units
    float2 texel = max(RenderScale, 1.0) / Resolution;  // one pixel of the image being shown
    float3 colour = previous(uv);

    // MilkDrop presets made their own finished picture for a plain 8-bit screen, so they skip
    // the sharpening, glow, tone-mapping and vignette (all of it, or partly mid-transition).
    float lucentLook = 1.0 - saturate(PlainOutput);

    // Sharpen: push each pixel away from its neighbours' average. This undoes the slight blur
    // that builds up as the feedback buffer is re-sampled; supersampled images need less.
    float3 neighbours = (previous(uv + float2(texel.x, 0.0)) + previous(uv - float2(texel.x, 0.0))
                       + previous(uv + float2(0.0, texel.y)) + previous(uv - float2(0.0, texel.y))) * 0.25;
    float sharpen = 0.6 * saturate(1.5 - 0.5 * RenderScale) * lucentLook;
    colour = max(colour + (colour - neighbours) * sharpen, 0.0);

    // Bloom only the brightest parts, so highlights glow but dark areas stay crisp and black.
    float3 bloom = 0.0;
    for (int i = 0; i < 12; i++)
    {
        float angle = i * (2.0 * PI / 12.0);
        float2 offset = float2(cos(angle), sin(angle)) * pixel * 6.0;
        bloom += max(previous(uv + offset) - 0.6, 0.0) + max(previous(uv + offset * 2.5) - 0.6, 0.0);
    }
    colour += bloom / 24.0 * 0.6 * BloomStrength * lucentLook;

    // A little extra saturation, and a light vignette.
    float3 display = lerp(saturate(colour), toneMap(colour), lucentLook);
    float luminance = dot(display, float3(0.2126, 0.7152, 0.0722));
    display = saturate(lerp(luminance.xxx, display, 1.0 + 0.15 * lucentLook));
    float2 fromCentre = uv - 0.5;
    display *= 1.0 - dot(fromCentre, fromCentre) * 0.35 * lucentLook;

    if (HdrOutput < 0.5)
    {
        return float4(display, 1.0);
    }

    // HDR screens: the same picture at the screen's normal white, but the brightest parts of the
    // scene (beyond what a normal screen can show) climb towards the screen's peak brightness.
    float3 shown = pow(display, 2.2) * HdrWhite;
    float overWhite = max(max(colour.r, max(colour.g, colour.b)) - 1.0, 0.0);
    float boost = lerp(1.0, max(HdrPeak / HdrWhite, 1.0), 1.0 - exp(-overWhite * 0.7));
    return float4(shown * boost, 1.0);
}

// While switching presets, the old preset's output is in PreviousFrame and the
// new one's in NextFrame. Styles 0-3 reveal the new one with a moving pattern;
// styles 4-7 morph one into the other by warping both pictures as they blend.
Texture2D NextFrame : register(t2);

float3 fromFrame(float2 uv)
{
    return PreviousFrame.SampleLevel(LinearClamp, uv, 0).rgb;
}

float3 toFrame(float2 uv)
{
    return NextFrame.SampleLevel(LinearClamp, uv, 0).rgb;
}

float brightnessOf(float3 colour)
{
    return dot(colour, float3(0.2126, 0.7152, 0.0722));
}

float4 PSBlend(VSOut input) : SV_Target
{
    float2 uv = input.uv;
    float t = TransitionProgress;
    float2 p = (uv - 0.5) * float2(Aspect, 1.0) * 2.0;
    int style = (int)TransitionStyle;

    if (style == 4)
    {
        // Liquid: both pictures melt along a swirling flow while one dissolves into the other.
        float2 flow = float2(fbm(p * 1.5 + Time * 0.2), fbm(p * 1.5 + 7.3 - Time * 0.2)) - 0.5;
        float2 offset = flow * 0.35 * sin(PI * t);
        float n = fbm(p * 2.0 + 11.0);
        float mask = smoothstep(n - 0.25, n + 0.25, t * 1.5 - 0.25);
        return float4(lerp(fromFrame(uv + offset), toFrame(uv - offset), mask), 1.0);
    }

    if (style == 5)
    {
        // Light: the brightest parts of the new visual shine through first, then the rest follows.
        float3 from = fromFrame(uv);
        float3 to = toFrame(uv);
        float key = brightnessOf(to) - brightnessOf(from) * 0.5;
        float threshold = lerp(1.2, -0.6, t);
        float mask = max(smoothstep(threshold, threshold + 0.35, key), smoothstep(0.75, 1.0, t));
        return float4(lerp(from, to, mask), 1.0);
    }

    if (style == 6)
    {
        // Fly-through: the old visual rushes past as a portal to the new one opens in the centre.
        float2 c = uv - 0.5;
        float reach = length(float2(Aspect, 1.0));
        float radius = max(t * t * reach * 1.15, 0.001);
        float3 from = fromFrame(0.5 + c / (1.0 + t * 2.0)) * (1.0 - t * 0.5);
        float3 to = toFrame(uv);
        float r = length(p);
        float mask = 1.0 - smoothstep(radius * 0.8, radius, r);
        float rim = glow(r - radius * 0.9, 18.0) * sin(PI * t) * 0.5;
        return float4(lerp(from, to, mask) + hsv2rgb(Time * 0.1, 0.5, rim), 1.0);
    }

    if (style == 7)
    {
        // Kaleidoscope: the picture folds into a kaleidoscope halfway through, and unfolds as the new visual.
        float amount = sin(PI * t);
        float2 folded = toUv(rotate(kaleidoscope(p, 6.0), t * PI * 0.5));
        float2 warped = lerp(uv, folded, amount);
        float mask = smoothstep(0.3, 0.7, t);
        return float4(lerp(fromFrame(warped), toFrame(warped), mask), 1.0);
    }

    float3 from = fromFrame(uv);
    float3 to = toFrame(uv);
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
