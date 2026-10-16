// Name: Liquid Chrome
// Rippling liquid metal, warped by the bass and lit by a light that circles overhead.

float3 Render(float2 uv, float2 p)
{
    float2 q = p * 1.3;
    float2 warp = float2(fbm(q + Time * 0.07), fbm(q + 5.2 - Time * 0.06));
    float2 r = q + warp * (1.2 + Bass * 1.5) + Time * 0.03;
    float surface = fbm(r);

    // Estimate the surface's slope to get a shiny, lit look.
    const float delta = 0.01;
    float slopeX = (fbm(r + float2(delta, 0.0)) - surface) / delta;
    float slopeY = (fbm(r + float2(0.0, delta)) - surface) / delta;
    float3 normal = normalize(float3(-slopeX, -slopeY, 2.0));

    float3 light = normalize(float3(cos(Time * 0.4), sin(Time * 0.3), 0.8));
    float diffuse = saturate(dot(normal, light));
    float highlight = pow(saturate(dot(reflect(-light, normal), float3(0.0, 0.0, 1.0))), 24.0);

    float3 tint = hsv2rgb(0.55 + surface * 0.4 + Treble * 0.15, 0.5, 1.0);
    float3 colour = (tint * (0.15 + diffuse * 0.6) + highlight * (0.8 + Beat)) * (0.5 + Level * 0.8);

    // Blend with a slightly zoomed last frame for a smooth, syrupy motion.
    return lerp(previous(toUv(p * 0.995)), colour, 0.35);
}
