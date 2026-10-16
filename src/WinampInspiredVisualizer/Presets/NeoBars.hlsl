// Name: Neo Bars
// The classic spectrum analyser, reimagined with glowing caps, reflections and rising smoke.

float3 Render(float2 uv, float2 p)
{
    // Old frames drift upward and fade, like smoke above the bars.
    float3 trail = previous(uv + float2(0.0, 0.003)) * 0.86;

    const float barCount = 48.0;
    float cell = floor(uv.x * barCount);
    float within = frac(uv.x * barCount);
    float level = spectrum((cell + 0.5) / barCount);

    float floorLine = 0.8;
    float height = level * 0.65;
    float y = floorLine - uv.y;                       // distance above the floor
    float inBar = step(0.12, within) * step(within, 0.88);

    float body = inBar * step(0.0, y) * step(y, height);
    float reflectionDepth = max(height * 0.35, 0.001);
    float reflection = inBar * step(y, 0.0) * step(-reflectionDepth, y) * (1.0 + y / reflectionDepth);
    float cap = inBar * glow(y - height, 260.0);

    float3 colour = hsv2rgb(0.45 - level * 0.45 + cell / barCount * 0.15, 0.85, 1.0);
    return trail + colour * (body * 0.14 + reflection * 0.08) + cap * 0.5;
}
