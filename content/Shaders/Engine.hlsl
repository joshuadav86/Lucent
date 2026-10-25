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

// A modernised MilkDrop preset's picture was made for a plain 8-bit screen, so before Lucent's look it's
// brought into Lucent's light the way AVS presets are (see AvsDisplay.hlsl): a touch of extra colour, the
// colour the tone-mapping below turns back into the same mid-tones, and the brightest parts pushed past white
// so they bloom and, on an HDR screen, climb towards its peak.
float3 intoLucentLight(float3 shown)
{
    float luminance = dot(shown, float3(0.2126, 0.7152, 0.0722));
    float3 colour = saturate(lerp(luminance.xxx, shown, 1.1));
    float3 c = min(colour * 0.92, 0.98);
    float3 a = 2.43 * c - 2.51;
    float3 b = 0.59 * c - 0.03;
    float3 light = (-b - sqrt(max(b * b - 4.0 * a * (0.14 * c), 0.0))) / (2.0 * a);
    float peak = max(colour.r, max(colour.g, colour.b));
    return light * (1.0 + 0.9 * smoothstep(0.7, 1.0, peak));
}

// Turns a colour around the grey axis: its hue shifts by angle radians, its brightness stays.
float3 turnHue(float3 colour, float angle)
{
    const float3 grey = float3(0.57735, 0.57735, 0.57735);
    float s, c;
    sincos(angle, s, c);
    return colour * c + cross(grey, colour) * s + grey * dot(grey, colour) * (1.0 - c);
}

// How a build-up and its drop move the whole picture, whatever the visual. BuildUp and DropPulse come from the
// song's map (worked out ahead, so the build-up is seen coming) or, more gently, from following the music by ear.
// Through a build-up the picture is drawn in towards the centre, faster and faster on the beat like a snare roll,
// as the edges close in and the colour drains and turns; on the drop it bursts back out in a shock wave.
// Returns where to read the picture from.
float2 songMotion(float2 uv, float buildUp, float drop)
{
    float tension = buildUp * buildUp * (3.0 - 2.0 * buildUp);
    float2 fromCentre = (uv - 0.5) * float2(Aspect, 1.0);

    // Hits once a beat, then twice, then four times a beat over the last quarter, each faster one smaller. Only
    // the picture's size moves this fast, never its brightness.
    float hits = buildUp < 0.5 ? 1.0 : buildUp < 0.75 ? 2.0 : 4.0;
    float hit = pow(1.0 - frac(BeatPhase * hits), 3.0);
    float zoom = 1.0 - 0.09 * tension - 0.03 * tension * hit / hits;

    // The shock wave: a ring rushing out from the centre, pushing the picture outwards as it passes.
    float since = -log(max(drop, 1e-4)) / 1.5;
    float ring = length(fromCentre) - since * 1.6;
    float wave = exp(-ring * ring * 60.0) * 0.05 * drop;
    zoom *= 1.0 - wave - 0.06 * drop;

    return 0.5 + (uv - 0.5) * zoom;
}

float4 PSPresent(VSOut input) : SV_Target
{
    // Classic presets shown as they first looked (MilkDrop's or Winamp's finished picture for a plain 8-bit
    // screen) skip the sharpening, glow, tone-mapping and vignette (all of it, or partly mid-transition), and
    // the build-ups and drops.
    float lucentLook = 1.0 - saturate(PlainOutput);
    float buildUp = saturate(BuildUp) * lucentLook;
    float drop = saturate(DropPulse) * lucentLook;
    float tension = buildUp * buildUp * (3.0 - 2.0 * buildUp);

    float2 uv = songMotion(input.uv, buildUp, drop);
    float2 pixel = RenderScale / Resolution;            // one screen pixel, in uv units
    float2 texel = max(RenderScale, 1.0) / Resolution;  // one pixel of the image being shown
    float3 colour = previous(uv);

    // The colours splitting apart late in a build-up and on the drop, and streaks rushing towards the centre.
    float2 inwards = 0.5 - uv;
    float split = 0.012 * drop + 0.004 * tension * tension;
    if (split > 0.0005)
    {
        colour.r = previous(uv - inwards * split).r;
        colour.b = previous(uv + inwards * split).b;
    }

    float rush = 0.06 * tension * tension;
    if (rush > 0.001)
    {
        float3 streaks = 0.0;
        for (int r = 1; r <= 6; r++)
        {
            streaks += previous(uv + inwards * rush * r / 6.0);
        }

        colour = lerp(colour, streaks / 6.0, 0.6);
    }

    // Sharpen: push each pixel away from its neighbours' average. This undoes the slight blur
    // that builds up as the feedback buffer is re-sampled; supersampled images need less.
    float3 neighbours = (previous(uv + float2(texel.x, 0.0)) + previous(uv - float2(texel.x, 0.0))
                       + previous(uv + float2(0.0, texel.y)) + previous(uv - float2(0.0, texel.y))) * 0.25;
    float sharpen = 0.6 * saturate(1.5 - 0.5 * RenderScale) * lucentLook;
    colour = max(colour + (colour - neighbours) * sharpen, 0.0);
    colour = lerp(colour, intoLucentLight(saturate(colour)), saturate(ClassicLift));

    // Bloom only the brightest parts, so highlights glow but dark areas stay crisp and black.
    float3 bloom = 0.0;
    for (int i = 0; i < 12; i++)
    {
        float angle = i * (2.0 * PI / 12.0);
        float2 offset = float2(cos(angle), sin(angle)) * pixel * 6.0;
        bloom += max(previous(uv + offset) - 0.6, 0.0) + max(previous(uv + offset * 2.5) - 0.6, 0.0);
    }
    colour += bloom / 24.0 * 0.6 * BloomStrength * lucentLook;

    // The drop lands brighter, settling over a second or two.
    colour *= 1.0 + 0.35 * drop;

    // A little extra saturation, and a light vignette. Through a build-up the colour drains and turns and the
    // edges close in; the drop brings the colour back stronger than before, and opens the edges right up.
    float3 display = lerp(saturate(colour), toneMap(colour), lucentLook);
    display = saturate(turnHue(display, 1.2 * tension));
    float luminance = dot(display, float3(0.2126, 0.7152, 0.0722));
    display = saturate(lerp(luminance.xxx, display, 1.0 + 0.15 * lucentLook - 0.45 * tension + 0.6 * drop));
    float2 fromCentre = input.uv - 0.5;
    float edges = (0.35 + 1.3 * tension) * (1.0 - drop);
    display *= saturate(1.0 - dot(fromCentre, fromCentre) * edges * lucentLook);

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
