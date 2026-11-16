// Name: Spiral Lantern
// A globe of bronze ribs and coloured glass stands on a dark stone floor with one bulb at its crown. The ribs are
// rhumb lines, spirals that cross every meridian at the same slant, wound both ways from foot to crown, and the
// bulb throws their shadow across the floor as a web of perfect spirals, every pane a patch of ruby, amber,
// emerald, aqua, sapphire, or violet light with a white heart. The globe turns slowly, so the spirals sweep round
// the room. The music plays in the glass: each ring of panes glows with its own part of the music, bass at the
// foot and treble at the crown, and each beat sends a wave of light up the globe and out across the floor.
// Family: Ray traced
// Tags: 3D, Geometric, Colourful
// Added: 2026-11-16 08:46

static const float SlSpirals = 12.0;    // how many ribs wind each way round the globe
static const float SlGlass = 1.5;       // how far the glass reaches toward the foot and the crown
static const float3 SlMiddle = float3(0.0, 1.0, 0.0);
static const float3 SlBulb = float3(0.0, 1.93, 0.0);

float slLoud(float x)
{
    return saturate((spectrum(x) - 0.42 + 0.5 * x) / 0.7);
}

// How loud the music is for one ring of panes, bass at the foot and treble at the crown, read at ten fixed
// places and blended between them.
float slLoudAt(float up01)
{
    float k = saturate(up01) * 9.0;
    float low = floor(k);
    return lerp(slLoud(0.08 + 0.06 * low), slLoud(0.14 + 0.06 * low), smoothstep(0.0, 1.0, k - low));
}

// The colour of the glass along one spiral: amber on every other one, ruby and sapphire by turns between.
float3 slPane(float spiral)
{
    float k = wrap(spiral, 4.0);
    float3 c = lerp(float3(1.5, 0.09, 0.13), float3(1.65, 0.68, 0.06), step(0.5, k));
    c = lerp(c, float3(0.1, 0.36, 1.55), step(1.5, k));
    return lerp(c, float3(1.65, 0.68, 0.06), step(2.5, k));
}

// The light one spot of the globe lets through, and how open the spot is: nothing at a rib, the pane's colour in
// the glass, white at the pane's clear heart. The spot is given by how far up the globe it lies, on the scale that
// makes every rib a straight line, and how far round. The same holds for the spot's shadow on the floor.
float4 slLight(float up, float angle, float soft, float leaded, float since, float flash)
{
    float a = SlSpirals * (angle + up) / 6.2832;
    float b = SlSpirals * (angle - up) / 6.2832;
    float2 which = floor(float2(a, b));
    float2 f = float2(a, b) - which;
    float toRib = min(min(f.x, 1.0 - f.x), min(f.y, 1.0 - f.y));
    float toHeart = abs(f.x - 0.5) + abs(f.y - 0.5);

    float open = smoothstep(0.04, 0.04 + soft, toRib) * smoothstep(SlGlass, SlGlass - 0.05 - soft, abs(up))
               * (1.0 - leaded * smoothstep(0.02 + soft, 0.0, abs(toHeart - 0.2) - 0.012));
    float3 glass = lerp(slPane(which.x), float3(1.5, 1.25, 0.9), smoothstep(0.2 + 2.0 * soft, 0.2 - 2.0 * soft, toHeart))
                 * (0.7 + 0.6 * smoothstep(0.04, 0.4, toRib));

    // This ring's part of the music, and the wave each beat sends up the globe.
    float up01 = saturate(0.5 + 3.1416 * (which.x - which.y) / SlSpirals / (2.0 * SlGlass));
    float wave = flash * exp(-(up01 - since * 1.3) * (up01 - since * 1.3) * 18.0);
    return float4(glass * (0.26 + 0.6 * slLoudAt(up01) + 0.28 * wave), open);
}

// How far up the globe a spot lies, on the scale that makes every rib a straight line.
float slUp(float height)
{
    return log(sqrt(max(1.0 - height * height, 1e-8)) / max(1.0 - height, 1e-4));
}

// What the lantern shows where a line of sight goes in at one spot and would come out at another.
float3 slGlobe(float3 near, float3 far, float3 dir, float soft, float turned, float since, float flash)
{
    float4 front = slLight(slUp(near.y), atan2(near.z, near.x + 1e-7) - turned, soft, 1.0, since, flash);
    float4 back = slLight(slUp(far.y), atan2(far.z, far.x + 1e-7) - turned, soft * 0.7, 1.0, since, flash);

    // The far side from within: bright bronze under the bulb, and its glass aglow.
    float3 toBulb = SlBulb - SlMiddle - far;
    float3 inner = lerp(float3(1.0, 0.62, 0.26) * 0.9 / (0.3 + dot(toBulb, toBulb)), back.rgb * 0.8, back.a);

    // The near side from without: dark bronze catching the room's one lamp, or glass with the far side showing
    // dimly through it.
    float3 lamp = normalize(float3(-0.4, 0.8, -0.45));
    float3 bounce = reflect(dir, near);
    float shine = pow(saturate(dot(bounce, lamp)), 40.0);
    float crown = smoothstep(SlGlass - 0.05, SlGlass, abs(slUp(near.y)));
    float3 bronze = lerp(float3(0.045, 0.03, 0.018), float3(0.3, 0.18, 0.06) * (0.8 + 0.2 * sin(slUp(near.y) * 26.0)), crown) * (0.4 + 0.9 * saturate(dot(near, lamp)))
                  + float3(1.0, 0.8, 0.5) * shine * (0.35 + 0.5 * crown);
    float facing = saturate(-dot(near, dir));
    float3 glass = front.rgb * (0.75 + 0.6 * facing) + inner * 0.5 + float3(0.9, 0.95, 1.0) * shine * 0.5;
    return lerp(bronze, glass, front.a);
}

float3 Render(float2 uv, float2 p)
{
    float since = frac(BeatGlide);
    float flash = smoothstep(0.0, 0.22, since) * (1.0 - since) * (1.0 - since);
    float turned = Time * 0.05;

    float3 eye = float3(0.45 * sin(Time * 0.04), 3.9 + 0.12 * sin(Time * 0.031), -3.6);
    float3 ray = cameraRay(eye, float3(0.0, 0.3, 0.5), float2(p.x, -p.y), 1.7, 0.0);
    float grain = pixelSize() / 1.7;

    // The room beyond the floor.
    float3 colour = float3(0.012, 0.01, 0.014);

    // The floor: dark stone, lit only by what the globe lets through, and polished enough to mirror the globe.
    if (ray.y < -0.001)
    {
        float reach = -eye.y / ray.y;
        float3 at = eye + ray * reach;
        float out2 = dot(at.xz, at.xz);
        float outward = sqrt(max(out2, 1e-6));
        float soft = 0.035 + 0.07 * saturate(outward / 9.0) + 1.5 * reach * grain * SlSpirals / 6.2832 / (outward * max(-ray.y, 0.15));
        float4 thrown = slLight(log(outward * 0.5), atan2(at.z, at.x + 1e-7) - turned, soft, 0.0, since, flash);
        float3 stone = float3(0.52, 0.49, 0.47) * (0.78 + 0.22 * fbm(at.xz * 2.5));
        float3 onFloor = stone * (thrown.rgb * thrown.a * 1.7 / (1.0 + 0.07 * out2) + float3(0.011, 0.01, 0.013));

        float3 up = float3(ray.x, -ray.y, ray.z);
        float3 fromMiddle = at - SlMiddle;
        float toward = dot(fromMiddle, up);
        float under = toward * toward - dot(fromMiddle, fromMiddle) + 1.0;
        if (under > 0.0 && toward < 0.0)
        {
            float half01 = sqrt(under);
            onFloor += slGlobe(fromMiddle + up * (-toward - half01), fromMiddle + up * (-toward + half01), up, 0.03, turned, since, flash)
                     * (0.07 + 0.5 * pow(1.0 - saturate(-ray.y), 4.0)) * saturate(under * 30.0);
        }

        colour = lerp(onFloor, colour, smoothstep(11.0, 24.0, reach));
    }

    // Dust in the air shows the light on its way from the bulb to the floor, a faint shaft from every pane.
    float3 back = eye - SlMiddle;
    float nearing = dot(back, ray);
    float within = nearing * nearing - dot(back, back) + 1.0;
    float ends = ray.y < -0.001 ? min(-eye.y / ray.y, 13.0) : 13.0;
    if (within > 0.0)
        ends = min(ends, -nearing - sqrt(within));

    float3 shafts = float3(0.0, 0.0, 0.0);
    float jog = 0.3 + 0.4 * hash(p * 213.7 + 3.1);
    int steps = qualitySteps(18);
    [loop]
    for (int i = 0; i < steps; i++)
    {
        float t = ends * (i + jog) / steps;
        float3 dust = eye + ray * t;
        float3 fromBulb = dust - float3(0.0, 2.0, 0.0);
        if (fromBulb.y < -0.02 && dot(dust - SlMiddle, dust - SlMiddle) > 1.0)
        {
            float2 lands = fromBulb.xz * (2.0 / -fromBulb.y);
            float4 passing = slLight(log(max(length(lands), 1e-4) * 0.5), atan2(lands.y, lands.x + 1e-7) - turned, 0.09, 0.0, since, flash);
            shafts += passing.rgb * passing.a / (0.5 + dot(fromBulb, fromBulb));
        }
    }

    colour += shafts * ends / steps * 0.17;

    // The globe itself.
    if (within > 0.0)
    {
        float chord = sqrt(within);
        float3 near = back + ray * (-nearing - chord);
        float3 far = back + ray * (-nearing + chord);
        float span = -nearing * grain;
        float soft = 0.012 + 1.5 * span * SlSpirals / 6.2832 / max(sqrt(max(1.0 - near.y * near.y, 0.0)) * saturate(-dot(near, ray)), 0.12);
        float3 globe = slGlobe(near, far, ray, soft, turned, since, flash);

        // The bulb at the crown, seen through the glass.
        float3 toBulb = SlBulb - eye;
        float along = dot(toBulb, ray);
        float miss = dot(toBulb, toBulb) - along * along;
        float open = slLight(slUp(near.y), atan2(near.z, near.x + 1e-7) - turned, soft, 1.0, since, flash).a;
        globe += float3(1.6, 1.3, 0.85) * exp(-miss / 0.01) * (0.15 + 0.85 * open);

        colour = lerp(colour, globe, saturate(within / (2.0 * span)));
    }

    return colour * (1.0 - 0.2 * dot(p, p) / (1.0 + Aspect * Aspect));
}
