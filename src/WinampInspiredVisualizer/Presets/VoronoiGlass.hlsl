// Name: Voronoi Glass
// A stained-glass window of irregular jewel-coloured panes held in rounded lead: the panes slowly shift
// and reshape, each one glowing from behind with its own part of the music, while light glints along the lead.
// Family: Geometric
// Tags: Colourful, Geometric

float2 vgHash2(float2 p)
{
    p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
    return frac(sin(p) * 43758.5453);
}

// Where the seed of grid cell g sits, drifting slowly on its own small orbit.
float2 vgSeed(float2 g)
{
    float2 h = vgHash2(g);
    return 0.5 + 0.38 * sin(Time * (0.11 + h.yx * 0.08) + h * 6.2831);
}

// Jewel colours for the glass: ruby, sapphire, emerald, amber, amethyst, teal.
float3 vgJewel(float k)
{
    float i = floor(k * 6.0);
    if (i < 1.0) return float3(0.85, 0.06, 0.1);
    if (i < 2.0) return float3(0.08, 0.2, 0.95);
    if (i < 3.0) return float3(0.05, 0.7, 0.25);
    if (i < 4.0) return float3(1.0, 0.55, 0.05);
    if (i < 5.0) return float3(0.55, 0.12, 0.85);
    return float3(0.0, 0.6, 0.7);
}

float3 Render(float2 uv, float2 p)
{
    float zoom = 3.1 + 0.3 * sin(Time * 0.05);
    float2 x = rotate(p, Time * 0.01) * zoom + float2(Time * 0.04, 0.0);
    float px = pixelSize() * zoom;

    // Nearest seed, then the exact distance to the nearest pane edge (two passes).
    float2 cell = floor(x);
    float2 f = x - cell;
    float2 nearestOffset = float2(0.0, 0.0);
    float2 nearestCell = float2(0.0, 0.0);
    float best = 8.0;
    [unroll]
    for (int j = -1; j <= 1; j++)
    {
        [unroll]
        for (int i = -1; i <= 1; i++)
        {
            float2 g = float2(i, j);
            float2 r = g + vgSeed(cell + g) - f;
            float d = dot(r, r);
            if (d < best)
            {
                best = d;
                nearestOffset = r;
                nearestCell = g;
            }
        }
    }
    float edgeDist = 8.0;
    float2 edgeNormal = float2(1.0, 0.0);
    [unroll]
    for (int j2 = -2; j2 <= 2; j2++)
    {
        [unroll]
        for (int i2 = -2; i2 <= 2; i2++)
        {
            float2 g = nearestCell + float2(i2, j2);
            float2 r = g + vgSeed(cell + g) - f;
            float2 diff = r - nearestOffset;
            if (dot(diff, diff) > 1e-5)
            {
                float2 n = normalize(diff);
                float d = dot(0.5 * (nearestOffset + r), n);
                if (d < edgeDist)
                {
                    edgeDist = d;
                    edgeNormal = n;
                }
            }
        }
    }

    float2 paneId = cell + nearestCell;
    float2 paneHash = vgHash2(paneId + 31.0);
    float band = spectrum(0.03 + paneHash.x * 0.8);

    // The light behind the window: a broad warm glow that wanders, with a ripple of light on each beat.
    float2 sunPos = float2(sin(Time * 0.07) * 0.9, cos(Time * 0.05) * 0.45);
    float sun = exp(-dot(p - sunPos, p - sunPos) * 1.3);
    float2 paneCentre = (x + nearestOffset - float2(Time * 0.04, 0.0)) / zoom;
    float ripple = Beat * exp(-abs(length(paneCentre) - (1.0 - Beat) * 1.6) * 5.0);

    // Glass: hand-made, so the colour is uneven, with streaks, seeds and a soft brightening in the middle.
    float3 glass = vgJewel(paneHash.y);
    float streak = noise(float2(dot(x, float2(0.9, 0.4)) * 6.0, dot(x, float2(-0.4, 0.9)) * 1.2) + paneHash * 9.0);
    float mottle = fbm(x * 2.5 + paneHash * 7.0 + Time * 0.02 * (0.5 + Mid));
    float body = 0.55 + 0.35 * streak + 0.4 * mottle;
    float inner = smoothstep(0.0, 0.35, edgeDist);
    float lightThrough = (0.05 + sun * 0.85 + band * 0.6 + ripple * 0.6 + Level * 0.08) * body * (0.6 + 0.4 * inner);
    float3 colour = glass * lightThrough;
    // Where the glass is thin it lets a little white light through.
    colour += glass * glass * pow(saturate(streak * mottle * 1.6), 3.0) * (0.2 + band * 0.5);

    // Rounded lead came along every edge, lit from the front by a slowly circling lamp.
    const float halfWidth = 0.055;
    float leadCover = saturate((halfWidth - edgeDist) / px + 0.5);
    float u = saturate(edgeDist / halfWidth);
    float3 leadNormal = normalize(float3(-edgeNormal * u * 0.9, sqrt(max(1.0 - u * u * 0.81, 0.0))));
    float lampAngle = Time * 0.23;
    float3 lampDir = normalize(float3(cos(lampAngle) * 0.7, sin(lampAngle) * 0.7, 0.6));
    float diffuse = saturate(dot(leadNormal, lampDir));
    float3 halfVec = normalize(lampDir + float3(0.0, 0.0, 1.0));
    float shine = pow(saturate(dot(leadNormal, halfVec)), 40.0);
    float3 lead = float3(0.03, 0.032, 0.036) + float3(0.12, 0.12, 0.13) * diffuse + float3(0.9, 0.85, 0.75) * shine * (0.35 + Treble * 0.6);
    // Glass darkens right next to the lead where the putty sits.
    colour *= 0.4 + 0.6 * smoothstep(halfWidth, halfWidth + 0.06, edgeDist);
    colour = lerp(colour, lead, leadCover);

    return colour;
}
