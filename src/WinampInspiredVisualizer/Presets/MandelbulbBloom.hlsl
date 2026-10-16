// Name: Mandelbulb Bloom
// A ray-marched Mandelbulb turning in the dark while each of its levels slowly twists, so its petals coil, unfurl and bloom again.

// Distance to a power-8 Mandelbulb (trig-free form), with an orbit trap for colour. Each iteration is
// pre-rotated by a slowly changing angle, which twists the petals so the bulb keeps blooming.
float bulbDistance(float3 position, float twist, out float4 trap)
{
    float3 w = position;
    float m = dot(w, w);
    float dz = 1.0;
    trap = float4(abs(w), m);
    float s, c;
    sincos(twist, s, c);
    [loop]
    for (int i = 0; i < 5; i++)
    {
        if (m > 4.0)
        {
            break;
        }
        dz = 8.0 * m * m * m * sqrt(m) * dz + 1.0;
        w.xz = float2(c * w.x - s * w.z, s * w.x + c * w.z);
        float x = w.x; float x2 = x * x; float x4 = x2 * x2;
        float y = w.y; float y2 = y * y; float y4 = y2 * y2;
        float z = w.z; float z2 = z * z; float z4 = z2 * z2;
        float k3 = x2 + z2;
        float k2 = rsqrt(max(k3 * k3 * k3 * k3 * k3 * k3 * k3, 1.0e-30));
        float k1 = x4 + y4 + z4 - 6.0 * y2 * z2 - 6.0 * x2 * y2 + 2.0 * z2 * x2;
        float k4 = x2 - y2 + z2;
        w.x = position.x + 64.0 * x * y * z * (x2 - z2) * k4 * (x4 - 6.0 * x2 * z2 + z4) * k1 * k2;
        w.y = position.y - 16.0 * y2 * k3 * k4 * k4 + k1 * k1;
        w.z = position.z - 8.0 * y * k4 * (x4 * x4 - 28.0 * x4 * x2 * z2 + 70.0 * x4 * z4 - 28.0 * x2 * z2 * z4 + z4 * z4) * k1 * k2;
        m = dot(w, w);
        trap = min(trap, float4(abs(w), m));
    }
    return 0.25 * log(m) * sqrt(m) / dz;
}

float3 Render(float2 uv, float2 p)
{
    float twist = 0.5 * sin(Time * 0.11) + Mid * 0.1;   // how far each level is turned
    float orbit = Time * 0.09;
    float distanceToBulb = 1.5 + 0.26 * sin(Time * 0.05);
    float3 eye = float3(sin(orbit), 0.45 * sin(Time * 0.07), cos(orbit)) * distanceToBulb;
    float3 target = float3(0.25 * sin(Time * 0.043), 0.15 * cos(Time * 0.037), 0.0);
    float3 direction = cameraRay(eye, target, p, 1.1 - Beat * 0.04, 0.2 * sin(Time * 0.05));

    // March, keeping the closest approach for a halo around the silhouette.
    float travelled = 0.0;
    float closest = 1.0e3;
    float steps = 0.0;
    bool hit = false;
    float4 trap = 0.0;
    int maxSteps = qualitySteps(56);
    // Only march inside the bounding sphere.
    float b = dot(eye, direction);
    float c = dot(eye, eye) - 1.25 * 1.25;
    float disc = b * b - c;
    if (disc > 0.0)
    {
        travelled = max(-b - sqrt(disc), 0.0);
        float exitDistance = -b + sqrt(disc);
        [loop]
        for (int i = 0; i < maxSteps; i++)
        {
            float d = bulbDistance(eye + direction * travelled, twist, trap);
            closest = min(closest, d);
            if (d < 0.004 * travelled)
            {
                hit = true;
                break;
            }
            travelled += d * 0.95;
            steps += 1.0;
            if (travelled > exitDistance)
            {
                break;
            }
        }
        // Rays that ran out of steps were creeping along the petals: count them as hits.
        hit = hit || (steps >= (float)maxSteps && travelled < exitDistance);
    }

    float hue = (BeatCount - Beat) * 0.02 + Time * 0.01;
    float3 colour = 0.0;
    if (hit)
    {
        float3 position = eye + direction * travelled;
        float2 e = float2(0.001 * travelled, -0.001 * travelled);
        float4 unused;
        float3 normal = normalize(e.xyy * bulbDistance(position + e.xyy, twist, unused)
                                + e.yyx * bulbDistance(position + e.yyx, twist, unused)
                                + e.yxy * bulbDistance(position + e.yxy, twist, unused)
                                + e.xxx * bulbDistance(position + e.xxx, twist, unused));

        float3 lightDir = normalize(float3(0.6, 0.7, -0.4));
        float diffuse = saturate(dot(normal, lightDir));
        float fill = saturate(dot(normal, normalize(float3(-0.7, -0.2, 0.5)))) * 0.25;
        float3 halfway = normalize(lightDir - direction);
        float specular = pow(saturate(dot(normal, halfway)), 48.0);
        float rim = pow(1.0 - saturate(dot(normal, -direction)), 3.0);

        // Orbit trap: orbits that dive close to the centre belong to deep creases, which stay shadowed
        // and glow with a second colour; the outer petals catch the light.
        float depth = sqrt(trap.w);
        float crease = saturate(1.0 - depth * 1.4);
        float occlusion = saturate(depth * 1.6 - 0.15) * saturate(1.0 - steps / (float)maxSteps);
        float3 petal = palette(hue + depth * 0.9 + 0.05);
        float3 inner = neon(hue + 0.45 + depth * 0.5);
        colour = petal * (0.06 + 1.1 * diffuse + fill) * occlusion
               + inner * crease * crease * (0.35 + Bass * 0.5)
               + float3(1.0, 0.95, 0.9) * specular * occlusion * (0.7 + Treble * 1.2)
               + inner * rim * (0.3 + Bass * 0.5);
    }

    // Bloom: a thin glow hugging the silhouette from rays that skimmed past it.
    float halo = exp(-closest * 45.0) * (hit ? 0.0 : 1.0);
    colour += neon(hue + 0.45) * halo * (0.2 + Bass * 0.4);
    return colour + previous(uv) * 0.12;
}
