// Name: Apollonian Spheres
// Drifting through an endless foam of pearly spheres packed inside spheres, each gap holding smaller ones forever. Light glows warmly in the hollows between them and swells with the music.
// Family: Fractal
// Tags: 3D, Calm

// Distance to an Apollonian packing of spheres, folding space into a repeating cell and inverting it
// in a sphere over and over. The trap remembers the orbit for colour and occlusion.
float apollonianDistance(float3 position, float squeeze, out float4 trap)
{
    float scale = 1.0;
    trap = 1.0e3;
    [loop]
    for (int i = 0; i < 8; i++)
    {
        position = -1.0 + 2.0 * frac(0.5 * position + 0.5);
        float r2 = dot(position, position);
        trap = min(trap, float4(abs(position), r2));
        float k = squeeze / max(r2, 1.0e-6);
        position *= k;
        scale *= k;
    }
    return 0.25 * abs(position.y) / scale;
}

float apollonianShape(float3 position, float squeeze)
{
    float4 unused;
    return apollonianDistance(position, squeeze, unused);
}

float3 Render(float2 uv, float2 p)
{
    // A slow, looping flight through the foam (a path known to stay in the open hollows).
    float t = Time * 0.16;
    float3 eye = float3(2.8 * cos(0.1 + 0.33 * t), 0.4 + 0.3 * cos(0.37 * t), 2.8 * cos(0.5 + 0.35 * t));
    float3 target = float3(1.9 * cos(1.2 + 0.41 * t), 0.4 + 0.1 * cos(0.27 * t), 1.9 * cos(2.0 + 0.38 * t));
    float3 direction = cameraRay(eye, target, float2(p.x, -p.y), 1.6, 0.2 * cos(0.1 * t));
    float squeeze = 1.1 + 0.4 * smoothstep(-0.3, 0.3, cos(0.05 * Time));   // the packing slowly loosens and tightens

    float3 warm = float3(1.0, 0.62, 0.32);
    float3 cool = float3(0.35, 0.55, 1.0);
    float glowLevel = 0.5 + 0.7 * Bass + 0.3 * Beat;

    // March, gathering a soft glow wherever the ray passes close to the spheres.
    float travelled = 0.0;
    float glowSum = 0.0;
    bool hit = false;
    float4 trap = 0.0;
    float maxDistance = 12.0;
    int maxSteps = qualitySteps(110);
    float precision = 0.0004 * surfacePrecision();
    [loop]
    for (int i = 0; i < maxSteps; i++)
    {
        float d = apollonianDistance(eye + direction * travelled, squeeze, trap);
        glowSum += exp(-d * 60.0) * exp(-travelled * 0.35);
        if (d < precision * travelled)
        {
            hit = true;
            break;
        }
        travelled += d;
        if (travelled > maxDistance)
        {
            break;
        }
    }

    float hue = Time * 0.01;
    float3 fogColour = lerp(float3(0.01, 0.012, 0.03), float3(0.04, 0.035, 0.07), saturate(direction.y * 0.5 + 0.5));
    float3 colour = fogColour;
    if (hit)
    {
        float3 position = eye + direction * travelled;
        float e = 0.0005 * travelled;
        float2 k = float2(1.0, -1.0);
        float3 normal = normalize(k.xyy * apollonianShape(position + k.xyy * e, squeeze)
                                + k.yyx * apollonianShape(position + k.yyx * e, squeeze)
                                + k.yxy * apollonianShape(position + k.yxy * e, squeeze)
                                + k.xxx * apollonianShape(position + k.xxx * e, squeeze));

        // Occlusion from the trap: orbits that dive deep belong to tight crevices.
        float occlusion = saturate(trap.w * 1.4);
        occlusion *= occlusion;

        // Pearl: soft base colour from the trap with a cool sheen.
        float3 pearl = lerp(float3(0.8, 0.78, 0.76), hsv2rgb(frac(0.55 + hue + trap.y * 0.9), 0.5, 0.9), 0.35 + 0.4 * saturate(trap.x * 2.0));
        float3 keyDir = normalize(float3(-0.5, 0.8, -0.3));
        float diffuse = saturate(dot(normal, keyDir));
        float sky = saturate(0.5 + 0.5 * normal.y);
        float3 halfway = normalize(keyDir - direction);
        float specular = pow(saturate(dot(normal, halfway)), 40.0);
        float facing = saturate(dot(normal, -direction));
        float fresnel = pow(1.0 - facing, 4.0);

        // Warm light pooled in the hollows: strongest where the surface is most enclosed.
        float hollow = 1.0 - saturate(trap.w * 2.0);
        colour = pearl * (0.04 + 0.75 * diffuse * (0.4 + 0.6 * occlusion) + 0.25 * sky * occlusion)
               + cool * fresnel * 0.35 * occlusion
               + float3(1.0, 0.95, 0.9) * specular * occlusion * (0.4 + Treble * 0.5)
               + warm * hollow * hollow * hollow * 0.5 * glowLevel;

        // Distance fog.
        float fog = 1.0 - exp(-travelled * travelled * 0.06);
        colour = lerp(colour, fogColour, fog);
    }
    // Glow drifting in the gaps between the spheres.
    float3 gapColour = lerp(warm, hsv2rgb(frac(0.95 + hue), 0.6, 1.0), 0.3);
    colour += gapColour * glowSum * 0.012 * glowLevel;
    return lerp(colour, previous(uv), 0.15);
}
