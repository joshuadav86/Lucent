// Name: Quaternion Julia
// A glossy four-dimensional Julia set sliced into our world, a sculpture of lacquered lobes and curls that slowly melts into new forms. Coloured rim lights follow the bass and treble, and every beat brightens its sheen.
// Family: Fractal
// Tags: 3D, Dark
// Added: 2026-10-17 21:50

// Squares a quaternion.
float4 juliaSquare(float4 a)
{
    return float4(a.x * a.x - dot(a.yzw, a.yzw), 2.0 * a.x * a.yzw);
}

// Distance to the Julia set of z -> z^2 + c, sliced at the fourth coordinate 0.
// The trap remembers how close the orbit came to the origin and to a plane, for colour.
float juliaDistance(float3 position, float4 c, out float2 trap)
{
    float4 z = float4(position, 0.0);
    float derivative = 1.0;
    float magnitude = dot(z, z);
    trap = float2(1.0e3, 1.0e3);
    [loop]
    for (int i = 0; i < 11; i++)
    {
        derivative *= 4.0 * magnitude;
        z = juliaSquare(z) + c;
        magnitude = dot(z, z);
        trap = min(trap, float2(magnitude, abs(z.y)));
        if (magnitude > 64.0)
        {
            break;
        }
    }
    return 0.25 * sqrt(magnitude / derivative) * log(magnitude);
}

float juliaShape(float3 position, float4 c)
{
    float2 unused;
    return juliaDistance(position, c, unused);
}

float3 juliaNormal(float3 position, float4 c, float epsilon)
{
    float2 e = float2(epsilon, -epsilon);
    return normalize(e.xyy * juliaShape(position + e.xyy, c)
                   + e.yyx * juliaShape(position + e.yyx, c)
                   + e.yxy * juliaShape(position + e.yxy, c)
                   + e.xxx * juliaShape(position + e.xxx, c));
}

// The studio around the sculpture: a dark room with a soft overhead light, for reflections.
float3 juliaStudio(float3 direction, float3 warm, float3 cool)
{
    float overhead = pow(saturate(direction.y * 0.5 + 0.5), 3.0);
    float3 colour = lerp(float3(0.01, 0.012, 0.022), float3(0.12, 0.125, 0.15), overhead);
    colour += float3(1.0, 0.97, 0.92) * pow(saturate(dot(direction, normalize(float3(0.4, 1.0, -0.3)))), 24.0) * 0.9;
    colour += warm * pow(saturate(dot(direction, normalize(float3(-1.0, 0.2, 0.6)))), 6.0) * 0.4;
    colour += cool * pow(saturate(dot(direction, normalize(float3(1.0, -0.1, 0.7)))), 6.0) * 0.4;
    return colour;
}

float3 Render(float2 uv, float2 p)
{
    // The constant behaves like a complex number x + i r, with r pointing along a slowly turning axis.
    // x + i r wanders round the plump inner edge of the Mandelbrot set, and turning the axis changes
    // which slice of the four-dimensional shape we see, so it morphs forever.
    float t = Time * 0.05;
    float real = -0.46 + 0.07 * sin(t * 0.61);
    float imaginary = 0.55 + 0.06 * sin(t * 0.43 + 1.0);
    float3 axis = normalize(float3(cos(t * 0.37), 0.8 * sin(t * 0.29) + 0.3, 0.6 + 0.5 * sin(t * 0.21 + 2.0)));
    float4 c = float4(real, axis * imaginary);

    float orbit = Time * 0.07;
    float3 eye = float3(sin(orbit) * 2.9, 0.7 * sin(Time * 0.05) + 0.4, cos(orbit) * 2.9);
    float3 target = float3(0.0, 0.0, 0.0);
    float3 direction = cameraRay(eye, target, float2(p.x, -p.y), 2.3, 0.08 * sin(Time * 0.04));

    // Rim light colours follow the low and high parts of the spectrum.
    float low = spectrum(0.08);
    float high = spectrum(0.6);
    float3 warm = float3(1.0, 0.45, 0.18) * (0.5 + 1.2 * low);
    float3 cool = float3(0.2, 0.55, 1.0) * (0.5 + 1.2 * high);

    // Backdrop: a soft pool of light behind the sculpture.
    float3 colour = juliaStudio(direction, warm * 0.3, cool * 0.3) * 0.25
                  + lerp(float3(0.05, 0.03, 0.08), float3(0.02, 0.05, 0.09), uv.x) * exp(-dot(p, p) * 0.9) * (0.7 + Level * 0.5);
    // Only march inside the bounding sphere.
    float radius = 1.6;
    float b = dot(eye, direction);
    float disc = b * b - (dot(eye, eye) - radius * radius);
    float closest = 1.0e3;
    if (disc > 0.0)
    {
        float travelled = max(-b - sqrt(disc), 0.0);
        float exitDistance = -b + sqrt(disc);
        bool hit = false;
        float2 trap = 0.0;
        int maxSteps = qualitySteps(110);
        float precision = 0.0007 * surfacePrecision();
        [loop]
        for (int i = 0; i < maxSteps; i++)
        {
            float d = juliaDistance(eye + direction * travelled, c, trap);
            closest = min(closest, d);
            if (d < precision * travelled)
            {
                hit = true;
                break;
            }
            travelled += d * 0.9;
            if (travelled > exitDistance)
            {
                break;
            }
        }

        if (hit)
        {
            float3 position = eye + direction * travelled;
            float3 normal = juliaNormal(position, c, 0.0005 * travelled);

            // Ambient occlusion: how open the space is just above the surface.
            float occlusion = 0.0;
            float weight = 1.0;
            for (int k = 1; k <= 5; k++)
            {
                float h = 0.02 + 0.05 * k;
                occlusion += (h - juliaShape(position + normal * h, c)) * weight;
                weight *= 0.6;
            }
            occlusion = saturate(1.0 - occlusion * 2.2);

            // Lacquer: deep coloured base, sharp highlight and studio reflection, with Fresnel.
            float hue = 0.5 + 0.7 * sqrt(trap.x) + 0.12 * sin(Time * 0.03);
            float3 base = hsv2rgb(frac(hue), 0.55, 0.8);
            base = lerp(base, float3(0.75, 0.62, 0.45), saturate(1.0 - trap.y * 3.0) * 0.5);
            float3 keyDir = normalize(float3(0.4, 1.0, -0.3));
            float diffuse = saturate(dot(normal, keyDir));
            float wrapLight = saturate(dot(normal, keyDir) * 0.5 + 0.5);
            float3 halfway = normalize(keyDir - direction);
            float specular = pow(saturate(dot(normal, halfway)), 90.0);
            float facing = saturate(dot(normal, -direction));
            float fresnel = 0.04 + 0.96 * pow(1.0 - facing, 5.0);
            float3 reflected = reflect(direction, normal);

            // Rim lights from either side, coloured by the music.
            float rimLeft = pow(saturate(dot(normal, normalize(float3(-1.0, 0.2, 0.6)))), 2.0);
            float rimRight = pow(saturate(dot(normal, normalize(float3(1.0, -0.1, 0.7)))), 2.0);
            float rimEdge = pow(1.0 - facing, 2.0);

            colour = base * (0.08 + 1.1 * diffuse + 0.3 * wrapLight) * occlusion
                   + (warm * rimLeft + cool * rimRight) * (0.15 + rimEdge) * 0.8 * occlusion
                   + juliaStudio(reflected, warm, cool) * fresnel * (0.8 + Beat * 0.4) * occlusion
                   + float3(1.0, 0.97, 0.92) * specular * (0.5 + Beat * 0.4 + Treble * 0.4) * occlusion;
        }
        else
        {
            // A faint glow hugging the silhouette.
            colour += lerp(warm, cool, 0.5) * exp(-closest * 30.0) * 0.12;
        }
    }
    return lerp(colour, previous(uv), 0.12);
}
