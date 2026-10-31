// Name: Chrome Blobs
// A drop of liquid chrome with an oil-slick rainbow sheen holds eight smaller drops at the corners of a turning cube; with the music they swell, fly out and melt back into the heart in perfect symmetry, and their mirror surfaces reflect the shimmering seven-fold pattern of a quasicrystal.
// Family: Ray traced
// Tags: Colourful, Dark, 3D, Geometric
// Added: 2026-10-16 23:54

static const int BlobCount = 9;

// Where blob i is now (xyz), and how big (w). Blob 0 is the heart; the others sit at the corners of
// a cube round it that turns slowly, breathing out with the music and in again so they melt back
// into the heart. Opposite corners listen to the same band, so the shape stays symmetric.
float4 blobAt(int i)
{
    if (i == 0)
    {
        return float4(0.0, 0.0, 0.0, 0.5 + Bass * 0.12 + Beat * 0.04);
    }
    int corner = i - 1;
    float3 c = float3((corner & 1) ? 1.0 : -1.0, (corner & 2) ? 1.0 : -1.0, (corner & 4) ? 1.0 : -1.0);
    float pair = (float)min(corner, 7 - corner);
    float level = spectrum(0.05 + pair * 0.18);
    float breathe = 0.5 + 0.5 * sin(Time * 0.35);
    float reach = 0.45 + 0.55 * breathe + level * 0.35 + Beat * 0.08;
    c.xz = rotate(c.xz, Time * 0.21);
    c.xy = rotate(c.xy, Time * 0.13);
    return float4(normalize(c) * reach, 0.26 + level * 0.14);
}

float blobSmoothMin(float a, float b, float k)
{
    float h = max(k - abs(a - b), 0.0) / k;
    return min(a, b) - h * h * k * 0.25;
}

// Distance to the merged surface of all the blobs (worked out once per pixel and passed in).
float blobField(float3 p, float4 blobs[BlobCount], float merge)
{
    float d = 10.0;
    [unroll]
    for (int i = 0; i < BlobCount; i++)
    {
        d = blobSmoothMin(d, length(p - blobs[i].xyz) - blobs[i].w, merge);
    }
    return d;
}

float3 blobNormal(float3 p, float4 blobs[BlobCount], float merge)
{
    const float2 e = float2(0.0012, -0.0012);
    return normalize(e.xyy * blobField(p + e.xyy, blobs, merge) + e.yyx * blobField(p + e.yyx, blobs, merge)
                   + e.yxy * blobField(p + e.yxy, blobs, merge) + e.xxx * blobField(p + e.xxx, blobs, merge));
}

// The light the chrome reflects: a quasicrystal, seven sets of waves crossing at even angles, so it
// never quite repeats; it drifts on its own and surges a little on every beat. (The drift must never be
// Time times a music level: Time keeps growing, so every small change in the level would jump the pattern
// a long way between frames and make it flicker.)
float blobWave(float3 direction)
{
    float drift = Time * 0.4 + (BeatCount - Beat) * 0.25;
    float2 u = direction.xz / (1.2 + abs(direction.y)) * 9.0;
    float total = 0.0;
    [unroll]
    for (int k = 0; k < 7; k++)
    {
        float angle = k * PI / 7.0 + Time * 0.01;
        total += cos(dot(u, float2(cos(angle), sin(angle))) + drift);
    }
    return total / 7.0 * 2.2 + direction.y * 1.5 - Time * 0.1;
}

// What the chrome reflects: pale strip lights of uneven widths, a glowing horizon and a crisp
// white panel overhead, over a dark floor. blur is how far the stripe pattern moves per pixel.
float3 blobStudio(float3 direction, float wave, float blur)
{
    float height = direction.y;
    float width = 0.35 + 0.3 * hash(float2(floor(wave), 3.0));
    float across = abs(frac(wave) - 0.5) * 2.0;
    float soft = max(blur * 2.0, 0.02);
    float stripe = smoothstep(1.0 - width - soft, 1.0 - width + soft, across);
    float3 colour = float3(0.85, 0.9, 1.0) * stripe * 1.3;

    // Glowing horizon and a dark floor.
    colour += float3(0.6, 0.7, 1.0) * exp(-abs(height) * 30.0) * 0.8;
    colour *= smoothstep(-0.35, 0.05, height) * 0.9 + 0.1;

    // Key light: a big panel overhead with a crisp edge.
    float2 panel = abs(float2(direction.x - 0.2, direction.z + 0.1)) - float2(0.35, 0.25);
    colour += float3(1.0, 0.96, 0.9) * smoothstep(0.02, 0.0, max(panel.x, panel.y)) * step(0.6, height) * 2.5;
    return colour;
}

float3 Render(float2 uv, float2 p)
{
    float4 blobs[BlobCount];
    [unroll]
    for (int b = 0; b < BlobCount; b++)
    {
        blobs[b] = blobAt(b);
    }
    float merge = 0.4 + Bass * 0.3;

    float orbit = Time * 0.1;
    float3 eye = float3(sin(orbit) * 3.2, 0.5 + 0.5 * sin(Time * 0.13), cos(orbit) * 3.2);
    const float zoom = 1.45;
    float3 direction = cameraRay(eye, float3(0.0, 0.0, 0.0), p, zoom, 0.0);
    float pixelAngle = 2.0 / (Resolution.y * zoom);

    // March to the chrome.
    float travelled = 1.5;
    float nearest = 10.0;
    float hit = 0.0;
    int steps = qualitySteps(64);
    [loop]
    for (int i = 0; i < steps; i++)
    {
        float d = blobField(eye + direction * travelled, blobs, merge);
        nearest = min(nearest, d / travelled);
        if (d < 0.001)
        {
            hit = 1.0;
            break;
        }
        travelled += d;
        if (travelled > 7.5)
        {
            break;
        }
    }

    // Shade every pixel the same way (so the stripe anti-aliasing can use screen derivatives),
    // then keep the result only where the ray found chrome.
    float3 position = eye + direction * travelled;
    float3 normal = blobNormal(position, blobs, merge);
    float3 mirrored = reflect(direction, normal);
    float facing = saturate(dot(normal, -direction));
    float wave = blobWave(mirrored);
    float3 reflection = blobStudio(mirrored, wave, fwidth(wave));

    // Behind the blobs, the room itself: the same flowing stripes, dim and tinted.
    float roomWave = blobWave(direction);
    float3 room = blobStudio(direction, roomWave, fwidth(roomWave));
    float3 roomTint = lerp(float3(0.45, 0.12, 1.0), float3(0.0, 0.6, 1.0), 0.5 + 0.5 * sin(floor(roomWave) * 1.7 + Time * 0.1));
    float3 colour = room * roomTint * 0.14 + float3(0.008, 0.006, 0.016);
    // A pool of coloured light on the backdrop behind the blobs.
    colour += lerp(float3(0.25, 0.06, 0.5), float3(0.0, 0.25, 0.45), 0.5 + 0.5 * sin(Time * 0.07)) * exp(-dot(p, p) * 1.2) * 0.12;
    colour += float3(0.35, 0.4, 0.6) * exp(-nearest / pixelAngle * 0.8) * 0.15 * (1.0 - hit);

    if (hit > 0.5)
    {
        // A second, shorter march: where the blobs see each other, the reflection darkens.
        float bounce = 0.02;
        float shade = 1.0;
        int bounceSteps = qualitySteps(24);
        [loop]
        for (int j = 0; j < bounceSteps; j++)
        {
            float d = blobField(position + mirrored * bounce, blobs, merge);
            if (d < 0.002)
            {
                shade = 0.2;
                break;
            }
            bounce += d;
            if (bounce > 3.0)
            {
                break;
            }
        }

        // Thin-film colours: an oily rainbow that slides over the surface with the viewing angle.
        float film = facing * 1.6 + dot(normal, float3(0.3, 0.5, 0.2)) * 0.8 + Time * 0.04;
        float3 sheen = 0.5 + 0.5 * cos(2.0 * PI * (film + float3(0.0, 0.33, 0.67)));
        sheen = lerp(float3(0.5, 0.5, 0.5), sheen, 0.85) * 1.5;
        float fresnel = 0.6 + 0.4 * pow(1.0 - facing, 3.0);
        colour = reflection * sheen * fresnel * shade;

        // Deep colour in the dark parts of the reflection, so the chrome never looks grey.
        colour += sheen * 0.04 * shade;

        // A crisp rim where the surface turns away.
        colour += sheen * pow(1.0 - facing, 6.0) * (0.25 + Treble * 0.6);
    }
    return colour;
}
