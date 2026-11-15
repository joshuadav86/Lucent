// Name: Silk Cave
// Following a will-o'-the-wisp down a cave whose walls are sheets of lit silk, amber, rose and violet, over still
// water that mirrors them. The wisp flares on every beat, the walls brighten band by band with the spectrum, and
// the bass makes the whole cave glow.
// Family: Fly-through
// Tags: 3D, Colourful, Calm
// Added: 2026-11-15 19:44

// The walls are a gyroid: a single smooth sheet that winds through space and divides it into two tangled
// tunnels. Nothing is solid. Each ray is walked through the cave and every time it nears the sheet it picks
// up light, more the closer it passes, which is what makes the walls look like cloth lit from behind.
static const float WaterLine = -0.42;          // height of the water below the eye

float scSheet(float3 p, float size)
{
    return dot(sin(p * size), cos(p.zxy * size)) / size;
}

// How far the point is from the silk.
float scMap(float3 p)
{
    // The cave twists slowly along its length.
    p.xy = rotate(p.xy, p.z * 0.11);
    float broad = scSheet(p, 1.25);
    float fine = scSheet(p + broad * 0.6, 4.1);
    return abs(broad - fine * 0.42 + 0.12) * 0.7;
}

float3 Render(float2 uv, float2 p)
{
    float travelled = Time * 0.26 + BeatGlide * 0.11;
    float3 eye = float3(0.0, 0.0, travelled);
    float3 ray = normalize(float3(rotate(float2(p.x, -p.y), sin(Time * 0.07) * 0.18), 1.35));
    float beatLight = SongMapped > 0.0 ? 0.35 + Beat * 0.65 : Beat;

    // The wisp leads the way, bobbing as it goes.
    float3 wisp = eye + float3(0.22 * sin(Time * 0.31), 0.06 + 0.1 * sin(Time * 0.43), 2.3);
    float flare = 0.85 + BeatSwell * 0.5 + Bass * 0.06 + DropPulse * 0.5;
    float hueDrift = Time * 0.03 + BeatGlide * 0.05;

    float3 gathered = 0.0;
    float t = 0.0;
    // Start each ray a different fraction of a step along, so the layers don't show as bands.
    t += 0.03 * frac(52.9829189 * frac(dot(floor(uv * Resolution), float2(0.06711056, 0.00583715))));
    int steps = qualitySteps(72);
    [loop]
    for (int i = 0; i < steps; i++)
    {
        float3 at = eye + ray * t;
        // Below the water line the cave is seen mirrored, dimmer, and rippling.
        float below = at.y < WaterLine ? 1.0 : 0.0;
        if (below > 0.5)
        {
            at.y = 2.0 * WaterLine - at.y;
            at.x += 0.02 * sin(at.z * 9.0 + Time * 1.1) * (1.0 + Treble);
        }

        float d = scMap(at);
        float3 toWisp = at - wisp;
        float nearWisp = 1.0 / (0.35 + dot(toWisp, toWisp) * 0.5);
        // White and amber by the wisp, orange around it, violet in the far reaches of the cave.
        float depth = at.z - eye.z;
        float warmth = saturate(nearWisp * 0.6 + 0.22 * cos(depth * 0.55 + at.y * 0.9 + at.x * 0.4 + hueDrift * 6.2832));
        float3 silk = lerp(float3(0.36, 0.14, 0.78), float3(0.96, 0.3, 0.3), smoothstep(0.0, 0.4, warmth));
        silk = lerp(silk, float3(1.0, 0.56, 0.14), smoothstep(0.3, 0.75, warmth));
        silk = lerp(silk, float3(1.0, 0.84, 0.6), smoothstep(0.7, 1.0, warmth));
        float band = spectrum(0.03 + frac(at.z * 0.09) * 0.6);
        float lit = (0.12 + nearWisp * flare * 0.4) * (0.86 + band * 0.3 + Bass * 0.06);
        float soft = smoothstep(0.15, 0.9, t) * exp(-t * 0.22);
        float stride = max(d, 0.012) * 0.8 + 0.01;
        gathered += silk * lit * soft * (below > 0.5 ? 0.3 : 1.0) * stride * 0.0042 / (d * d + 0.0009);
        t += stride;
        if (t > 11.0)
        {
            break;
        }
    }

    float3 colour = 1.0 - exp(-gathered * 1.1);

    // The wisp itself: a small sun with a twinkle, and its glint on the water.
    float3 toward = wisp - eye;
    float2 on = float2(toward.x, toward.y) / toward.z * 1.35;
    on = rotate(on, -sin(Time * 0.07) * 0.18);
    float2 off = float2(p.x, -p.y) - on;
    float core = exp(-dot(off, off) * 900.0) + 0.16 * exp(-dot(off, off) * 60.0);
    float twinkle = exp(-abs(off.x) * 55.0) * exp(-abs(off.y) * 7.0) + exp(-abs(off.y) * 55.0) * exp(-abs(off.x) * 7.0);
    colour += float3(1.0, 0.8, 0.45) * (core + twinkle * 0.22 * (0.4 + Treble + beatLight * 0.6)) * flare;
    float2 glint = float2(off.x, -p.y - (2.0 * (WaterLine / toward.z * 1.35) - on.y));
    colour += float3(1.0, 0.7, 0.35) * exp(-glint.x * glint.x * 260.0 - glint.y * glint.y * 40.0) * 0.22 * flare;

    return colour * (0.9 + Intensity * 0.2) * (1.0 - dot(uv - 0.5, uv - 0.5) * 0.7);
}
