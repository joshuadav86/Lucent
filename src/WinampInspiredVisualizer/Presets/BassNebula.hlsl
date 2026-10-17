// Name: Bass Nebula
// Swirling clouds that billow with the bass and flare on every beat.
// Family: Cosmic
// Tags: Calm, Space

float3 Render(float2 uv, float2 p)
{
    float2 drift = float2(sin(Time * 0.21), cos(Time * 0.17)) * 0.002;
    float2 source = rotate(p, 0.004 + Mid * 0.006) * 0.996 + drift;
    float3 trail = previous(toUv(source)) * 0.96;

    float2 flow = p * 1.6 + float2(Time * 0.05, -Time * 0.04);
    float clouds = fbm(flow + fbm(p * 3.0 - Time * 0.1) * (0.4 + Bass * 0.8));
    float energy = smoothstep(0.55 - Bass * 0.25, 0.95, clouds);

    float3 colour = hsv2rgb(0.72 + clouds * 0.3 + Treble * 0.2, 0.75, 1.0);
    return trail + colour * energy * (0.03 + Bass * 0.1 + Beat * 0.2);
}
