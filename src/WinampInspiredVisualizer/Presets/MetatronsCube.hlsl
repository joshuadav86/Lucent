// Name: Metatron's Cube
// The sacred figure of thirteen circles and seventy-eight lines, its hidden solids (cube, star tetrahedron,
// octahedron) tumbling out in 3D and snapping back into place before a vast counter-turning echo of itself.
// Family: Kaleidoscope
// Tags: Neon, Geometric, 3D
// Added: 2026-10-16 23:54

float mcStroke(float d, float halfWidth, float px)
{
    float w = max(halfWidth, px * 0.5);
    return saturate((w - abs(d)) / px + 0.5) * (halfWidth / w);
}

float mcSegment(float2 p, float2 a, float2 b)
{
    float2 ab = b - a;
    float t = saturate(dot(p - a, ab) / max(dot(ab, ab), 1e-6));
    return length(p - a - ab * t);
}

// The flat figure's lines that pass through one 30-degree wedge (the rest are mirror images),
// with each run of overlapping lines merged into one (unit = circle radius).
static const float4 MC_FLAT[19] = {
    float4(-4.0, 0.0, 4.0, 0.0),           float4(-2.0, -3.4641, 2.0, 3.4641),   float4(-2.0, 3.4641, 2.0, -3.4641),
    float4(1.0, 1.7321, 2.0, 0.0),         float4(-1.0, 1.7321, 2.0, 0.0),        float4(-1.0, -1.7321, 2.0, 0.0),
    float4(1.0, -1.7321, 2.0, 0.0),        float4(2.0, -3.4641, 2.0, 3.4641),     float4(-2.0, 3.4641, 2.0, 0.0),
    float4(-2.0, -3.4641, 2.0, 0.0),       float4(1.0, -1.7321, 1.0, 1.7321),     float4(-2.0, 3.4641, 4.0, 0.0),
    float4(1.0, 1.7321, 2.0, -3.4641),     float4(-1.0, 1.7321, 4.0, 0.0),        float4(-1.0, -1.7321, 4.0, 0.0),
    float4(-2.0, -3.4641, 4.0, 0.0),       float4(1.0, -1.7321, 2.0, 3.4641),     float4(2.0, 3.4641, 4.0, 0.0),
    float4(2.0, -3.4641, 4.0, 0.0)
};

// Line work of the flat figure (lines, circles) at q, in units of the circle radius.
float2 mcFlat(float2 q, float spin, float hwq, float pxq)
{
    float qa = atan2(q.y, q.x) - spin;
    float fa = abs(wrap(qa + PI / 6.0, PI / 3.0) - PI / 6.0);
    float2 fq = float2(cos(fa), sin(fa)) * length(q);
    float flatLines = 0.0;
    // Every line ends within four radii of the centre.
    if (length(q) < 4.0 + hwq + pxq * 2.0)
    {
        [loop]
        for (int i = 0; i < 19; i++)
        {
            float4 seg = MC_FLAT[i];
            flatLines += mcStroke(mcSegment(fq, seg.xy, seg.zw), hwq, pxq);
        }
    }
    float flatCircles = mcStroke(length(fq) - 1.0, hwq, pxq) + mcStroke(length(fq - float2(2.0, 0.0)) - 1.0, hwq, pxq)
                      + mcStroke(length(fq - float2(1.0, 1.7321)) - 1.0, hwq, pxq) + mcStroke(length(fq - float2(4.0, 0.0)) - 1.0, hwq, pxq);
    return float2(flatLines, flatCircles);
}

float3 Render(float2 uv, float2 p)
{
    float px = pixelSize();
    float hw = max(px * 0.5, 0.0012);
    float scale = 0.205 * (1.0 + Beat * 0.03 + Bass * 0.03);   // screen size of one circle radius

    // The flat figure turns slowly; fold the plane into one 30-degree wedge of its sixfold symmetry.
    float flatSpin = Time * 0.03;
    float2 q = p / scale;
    float pxq = px / scale;
    float hwq = hw / scale;
    float2 figure = mcFlat(q, flatSpin, hwq, pxq);
    float flatLines = figure.x;
    float flatCircles = figure.y;

    // A vast, faint copy of the figure turns the other way behind everything.
    float2 vast = mcFlat(q / 3.0, -flatSpin * 0.7 + 0.5, hwq / 3.0, pxq / 3.0);

    // The hidden solids, cube corners at (+-1,+-1,+-1). Seen along a body diagonal the cube lands on the
    // figure's outer hexagon, the star tetrahedron on its great hexagram and the octahedron on the inner star.
    float tumble = smoothstep(-0.92, -0.45, sin(Time * 0.11 - 1.0));
    float yaw = tumble * sin(Time * 0.19) * 1.4;
    float pitch = tumble * sin(Time * 0.13 + 1.0) * 1.1;
    float roll = flatSpin;

    float reach = 0.0;
    float3 pts[14] = {
        float3(0.0, 0.0, 0.0), float3(0.0, 0.0, 0.0),
        float3(0.0, 0.0, 0.0), float3(0.0, 0.0, 0.0),
        float3(0.0, 0.0, 0.0), float3(0.0, 0.0, 0.0),
        float3(0.0, 0.0, 0.0), float3(0.0, 0.0, 0.0),
        float3(0.0, 0.0, 0.0), float3(0.0, 0.0, 0.0),
        float3(0.0, 0.0, 0.0), float3(0.0, 0.0, 0.0),
        float3(0.0, 0.0, 0.0), float3(0.0, 0.0, 0.0)
    };
    [loop]
    for (int k = 0; k < 14; k++)
    {
        float3 v = float3(0.0, 0.0, 0.0);
        if (k < 8)
        {
            v = float3((k & 1) != 0 ? 1.0 : -1.0, (k & 2) != 0 ? 1.0 : -1.0, (k & 4) != 0 ? 1.0 : -1.0);
        }
        else
        {
            int a6 = k - 8;
            float sgn = (a6 & 1) != 0 ? -1.0 : 1.0;
            int axisIndex = a6 / 2;
            v = (axisIndex == 0) ? float3(sgn, 0.0, 0.0) : ((axisIndex == 1) ? float3(0.0, sgn, 0.0) : float3(0.0, 0.0, sgn));
        }
        v.xy = rotate(v.xy, -PI * 0.25);
        v.xz = rotate(v.xz, -0.9553166);       // (1,1,1) now faces the viewer
        v.xy = rotate(v.xy, roll);
        v.yz = rotate(v.yz, pitch);
        v.xz = rotate(v.xz, yaw);
        // Perspective fades out as the solids settle, so they lock exactly onto the flat figure.
        float z = v.z * 2.0 * tumble + 9.0;
        pts[k] = float3(v.xy * 2.4495 * (9.0 / z), v.z * 2.0 + 9.0);
        reach = max(reach, length(pts[k].xy));
    }

    float cubeGlow = 0.0;
    float starGlow = 0.0;
    float octaGlow = 0.0;
    float depthSum = 0.0;
    float weightSum = 0.0;
    float corner = 10.0;
    // Only pixels near the solids need their edges.
    if (length(q) < reach + 0.4)
    {
        [loop]
        for (int i2 = 0; i2 < 8; i2++)
        {
            // (Constant loop bounds: fxc misreads inner loops that start at i + 1.)
            [loop]
            for (int j2 = 0; j2 < 8; j2++)
            {
                int diff = i2 ^ j2;
                int bits = (diff & 1) + ((diff >> 1) & 1) + ((diff >> 2) & 1);
                if (j2 > i2 && bits < 3)
                {
                    float d = mcSegment(q, pts[i2].xy, pts[j2].xy);
                    float s = mcStroke(d, hwq, pxq) + 0.06 / (1.0 + d * d * 2500.0);
                    cubeGlow += (bits == 1) ? s : 0.0;
                    starGlow += (bits == 2) ? s : 0.0;
                    float zMid = (pts[i2].z + pts[j2].z) * 0.5;
                    depthSum += s * zMid;
                    weightSum += s;
                }
            }
        }
        [loop]
        for (int i3 = 8; i3 < 14; i3++)
        {
            [loop]
            for (int j3 = 8; j3 < 14; j3++)
            {
                if (j3 > i3 && ((j3 - i3 != 1) || ((i3 - 8) % 2 != 0)))
                {
                    float d = mcSegment(q, pts[i3].xy, pts[j3].xy);
                    float s = mcStroke(d, hwq, pxq) + 0.06 / (1.0 + d * d * 2500.0);
                    octaGlow += s;
                    depthSum += s * (pts[i3].z + pts[j3].z) * 0.5;
                    weightSum += s;
                }
            }
        }
        [loop]
        for (int c = 0; c < 14; c++)
        {
            corner = min(corner, length(q - pts[c].xy));
        }
    }

    // Colours: the flat figure in pale gold, the solids shifting with depth (near warm, far cool).
    float depth = (weightSum > 1e-4) ? depthSum / weightSum - 9.0 : 0.0;
    float hue = Time * 0.02;
    float level = 0.6 + Level * 0.6;
    float3 colour = hsv2rgb(hue + 0.12, 0.45, 1.0) * (flatLines * 0.18 + flatCircles * 0.35) * level;
    colour += hsv2rgb(hue + 0.62, 0.6, 1.0) * (vast.x * 0.11 + vast.y * 0.2) * level;
    colour += hsv2rgb(hue + 0.5 + depth * 0.05, 0.8, 1.0) * cubeGlow * (0.7 + spectrum(0.1) * 1.2);
    colour += hsv2rgb(hue + 0.83 + depth * 0.05, 0.8, 1.0) * starGlow * (0.6 + spectrum(0.45) * 1.2);
    colour += hsv2rgb(hue + 0.13 + depth * 0.05, 0.75, 1.0) * octaGlow * (0.6 + spectrum(0.8) * 1.2);

    // Corners glint with the treble.
    colour += float3(1.0, 0.95, 0.9) * exp(-corner * corner / (pxq * pxq * 4.0)) * (0.4 + Treble * 2.0);

    // Echoes recede into the depths with a slow colour drift.
    float3 echo = previous(toUv(rotate(p, 0.006) * 1.025));
    return colour + echo.gbr * 0.06 + echo * 0.45;
}
