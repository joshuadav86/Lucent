// Name: Sierpinski Dive
// Weave between floating Sierpinski tetrahedra, crystal pyramids built from four half-size copies of themselves
// forever, every edge at every size traced in glowing neon so each face shows the famous triangle of triangles.
// The big edges play the bass and the fine ones the treble, and every beat sends light cascading down
// from the largest triangles to the smallest.
// Family: Fly-through
// Tags: Neon, Geometric, 3D
// Added: 2026-10-16 20:36

static const float Spacing = 3.2;
static const float Grow = 1.5;   // each pyramid is 2/Grow across, leaving open lanes between them

// The four faces of the tetrahedron with corners (1,1,1), (-1,-1,1), (1,-1,-1) and (-1,1,-1): a point
// is inside when all four are at most 1.
float4 sdFaces(float3 p)
{
    return float4(-p.x - p.y - p.z, p.x + p.y - p.z, p.x - p.y + p.z, -p.x + p.y + p.z);
}

// Fold into the nearest of the four half-size copies, then grow it back to full size.
float3 sdFold(float3 p)
{
    if (p.x + p.y < 0.0) p.xy = -p.yx;
    if (p.x + p.z < 0.0) p.xz = -p.zx;
    if (p.y + p.z < 0.0) p.yz = -p.zy;
    return p * 2.0 - 1.0;
}

float3 sdCell(float3 position)
{
    return (wrap(position, Spacing) - Spacing * 0.5) * Grow;
}

float pyramid(float3 position)
{
    float3 p = sdCell(position);
    float scale = Grow;
    int depth = fractalDepth(8);
    [loop]
    for (int i = 0; i < depth; i++)
    {
        p = sdFold(p);
        scale *= 2.0;
    }
    float4 faces = sdFaces(p);
    return (max(max(faces.x, faces.y), max(faces.z, faces.w)) - 1.0) * 0.57735 / scale;
}

// The neon lines on the surface: at every size, how close the point is to an edge of the
// tetrahedron it sits in (where the two outermost faces meet). Each size glows with its own band.
float3 pyramidLines(float3 position, float width)
{
    float3 p = sdCell(position);
    float scale = Grow;
    float3 glow = 0.0;
    int depth = fractalDepth(8);
    float sinceBeat = -log(max(Beat, 1e-4)) / 5.0;
    [loop]
    for (int i = 0; i < depth; i++)
    {
        float4 f = sdFaces(p);
        // The second largest face value: 1 right on an edge.
        float top = max(max(f.x, f.y), max(f.z, f.w));
        float second = max(max(min(f.x, f.y), min(f.z, f.w)), min(max(f.x, f.y), max(f.z, f.w)));
        float edge = (1.0 - second) * 0.57735 / scale;
        float level = (float)i / 7.0;
        float band = spectrum(0.03 + level * 0.65);
        float cascade = exp(-pow((level - sinceBeat * 1.6) * 5.0, 2.0)) * Beat;
        float lit = exp(-pow(edge / max(width, 0.0008 / scale), 2.0));
        float3 tint = hsv2rgb(frac(0.47 + saturate(i / 4.0) * 0.6 + sin(position.z * 0.05) * 0.04), 0.85, 1.0);
        glow += tint * lit * (0.35 + band * 1.4 + cascade * 2.0) * pow(0.6, (float)i);
        p = sdFold(p);
        scale *= 2.0;
    }
    return glow;
}

float3 pyramidNormal(float3 p)
{
    float2 e = float2(0.0015, -0.0015) * surfacePrecision();
    return normalize(e.xyy * pyramid(p + e.xyy) + e.yyx * pyramid(p + e.yyx)
                   + e.yxy * pyramid(p + e.yxy) + e.xxx * pyramid(p + e.xxx));
}

float2 divePath(float z)
{
    // Fly the open lanes between the pyramids.
    return float2(Spacing * 0.5 + sin(z * 0.2) * 0.25, Spacing * 0.5 + cos(z * 0.15) * 0.25);
}

float3 Render(float2 uv, float2 p)
{
    float z = Time * 0.9;
    float3 eye = float3(divePath(z), z);
    float3 direction = cameraRay(eye, float3(divePath(z + 2.0) + float2(sin(Time * 0.1) * 0.6, cos(Time * 0.08) * 0.4), z + 2.0), p, 0.95 - Beat * 0.05, sin(Time * 0.13) * 0.6);

    // Detail allows more steps and a closer approach to the crystal; shading stays scaled to Normal's 100.
    const float normalSteps = 100.0;
    int maxSteps = qualitySteps(100);
    float hitPrecision = 0.0005 * surfacePrecision();
    const float farPlane = 18.0;
    float travelled = 0.0;
    float steps = 0.0;
    [loop]
    for (int i = 0; i < maxSteps; i++)
    {
        float d = pyramid(eye + direction * travelled);
        if (d < hitPrecision * travelled || travelled > farPlane)
        {
            break;
        }
        travelled += d;
        steps += 1.0;
    }

    float3 colour = 0.0;
    if (travelled < farPlane)
    {
        float3 hit = eye + direction * travelled;
        float3 normal = pyramidNormal(hit);
        float3 light = normalize(float3(-0.4, 0.7, -0.6));
        float diffuse = saturate(dot(normal, light));
        float specular = pow(saturate(dot(reflect(direction, normal), light)), 40.0);
        float rim = pow(1.0 - saturate(dot(normal, -direction)), 3.0);
        float occlusion = saturate(1.0 - steps / normalSteps);

        // Dark glossy faces, so the neon edges carry the picture.
        float3 face = float3(0.03, 0.03, 0.06) * (0.3 + diffuse) * occlusion + specular * 0.25 * (0.6 + Treble);
        float width = 0.0009 * travelled + 0.0012;
        float3 lines = pyramidLines(hit, width) * (0.6 + occlusion * 0.6);
        colour = face + lines * (0.8 + Intensity * 0.4) * 2.2 + hsv2rgb(frac(0.6 + hit.z * 0.02), 0.7, 1.0) * rim * 0.08 * (0.5 + Mid);
        colour *= exp(-travelled * 0.13);
    }

    colour += float3(0.35, 0.3, 1.0) * min(steps / normalSteps, 1.0) * 0.06 * (0.5 + Bass * 1.5);
    return colour + previous(uv) * 0.12;
}
