// Name: Neon Square
// A single neon square hangs in a dark room, turning slowly above a polished floor that mirrors it.
// The tube glows brighter and wider with the bass and flickers ever so slightly with the treble.
// Family: Minimal
// Tags: Neon, Minimal, Dark, 3D
// Added: 2026-10-17 21:41

static const float TubeRadius = 0.011;

// The square's four corners in the room, turning slowly about the vertical and rocking gently.
void nsCorners(out float3 c0, out float3 c1, out float3 c2, out float3 c3)
{
    float yaw = Time * 0.16;
    float tilt = sin(Time * 0.11) * 0.12;
    float roll = Time * 0.05;
    float3 centre = float3(0.0, 0.9 + sin(Time * 0.23) * 0.03, 0.0);
    float2 local[4] = { float2(-0.5, -0.5), float2(0.5, -0.5), float2(0.5, 0.5), float2(-0.5, 0.5) };
    float3 corner[4];
    [unroll]
    for (int i = 0; i < 4; i++)
    {
        float2 v = rotate(local[i] * 1.15, roll);
        float3 w = float3(v.x, v.y, 0.0);
        w.yz = rotate(w.yz, tilt);
        w.xz = rotate(w.xz, yaw);
        corner[i] = centre + w;
    }
    c0 = corner[0];
    c1 = corner[1];
    c2 = corner[2];
    c3 = corner[3];
}

// Closest distance between a ray and a segment; also returns how far along the ray that is.
float nsRaySegment(float3 ro, float3 rd, float3 a, float3 b, out float along)
{
    float3 ba = b - a;
    float3 oa = ro - a;
    float baba = dot(ba, ba);
    float bard = dot(ba, rd);
    float baoa = dot(ba, oa);
    float rdoa = dot(rd, oa);
    float denom = baba - bard * bard;
    float s = denom > 1e-6 ? saturate((baoa - bard * rdoa) / denom) : 0.0;
    float3 onSegment = a + ba * s;
    along = max(dot(onSegment - ro, rd), 0.0);
    return length(ro + rd * along - onSegment);
}

// How much neon light reaches the eye along a ray, up to maxT; blur widens the glow for glossy reflections.
float nsGlow(float3 ro, float3 rd, float maxT, float blur, float3 c0, float3 c1, float3 c2, float3 c3, out float core)
{
    float3 a[4] = { c0, c1, c2, c3 };
    float3 b[4] = { c1, c2, c3, c0 };
    float halo = 0.0;
    core = 0.0;
    [unroll]
    for (int i = 0; i < 4; i++)
    {
        float along = 0.0;
        float d = nsRaySegment(ro, rd, a[i], b[i], along);
        float visible = along < maxT ? 1.0 : 0.0;
        float spread = TubeRadius + blur * along;
        d = sqrt(d * d + blur * blur * along * along);
        float tube = saturate(1.0 - d / spread);
        core = max(core, tube * tube * visible * (TubeRadius / spread));
        // A tight bright halo and a wide soft one, as light scatters in the room's air.
        halo += visible * (0.0016 / (d * d + 0.0016) * 0.4 + exp(-d * 5.0) * 0.07) * (TubeRadius / spread + 0.3) / 1.3;
    }
    return halo;
}

// Light from the whole tube falling on a surface point with normal n (a few points along each side).
float nsIrradiance(float3 pos, float3 n, float3 c0, float3 c1, float3 c2, float3 c3)
{
    float3 a[4] = { c0, c1, c2, c3 };
    float3 b[4] = { c1, c2, c3, c0 };
    float total = 0.0;
    [unroll]
    for (int i = 0; i < 4; i++)
    {
        [unroll]
        for (int k = 0; k < 4; k++)
        {
            float3 l = lerp(a[i], b[i], (k + 0.5) / 4.0) - pos;
            float r2 = dot(l, l);
            total += saturate(dot(n, l * rsqrt(r2))) / (r2 + 0.05);
        }
    }
    return total / 16.0;
}

float3 Render(float2 uv, float2 p)
{
    float3 c0;
    float3 c1;
    float3 c2;
    float3 c3;
    nsCorners(c0, c1, c2, c3);

    // Slow cinematic drift around the square.
    float orbit = sin(Time * 0.05) * 0.35;
    float3 eye = float3(sin(orbit) * 3.4, 0.6 + sin(Time * 0.07) * 0.08, -cos(orbit) * 3.4);
    float3 target = float3(0.0, 0.6, 0.0);
    float3 rd = cameraRay(eye, target, float2(p.x, -p.y), 1.45, 0.0);

    // The tube: brighter with the bass, a subtle flicker with the treble, and a breath on the beat.
    float flicker = 1.0 - Treble * 0.18 * step(0.62, noise(float2(Time * 23.0, 3.7))) - Treble * 0.05 * noise(float2(Time * 60.0, 1.3));
    float power = (0.75 + Bass * 0.55 + Beat * 0.15) * flicker;
    float hue = Time * 0.004;
    float3 neonColour = lerp(float3(1.0, 0.16, 0.42), float3(0.25, 0.45, 1.0), 0.5 - 0.5 * cos(hue * 6.2831853));
    float3 coreColour = lerp(neonColour, float3(1.0, 0.92, 0.95), 0.6);

    // The room: a polished floor at height 0 and a dark back wall far behind the square.
    float floorT = rd.y < -1e-4 ? -eye.y / rd.y : 1e6;
    float wallT = rd.z > 1e-4 ? (1.8 - eye.z) / rd.z : 1e6;
    float hitT = min(floorT, wallT);

    float3 colour = float3(0.0, 0.0, 0.0);
    float3 pos = eye + rd * hitT;
    float lit = 0.0;
    if (floorT < wallT)
    {
        // Polished concrete: dim diffuse light from the tube plus a glossy, slightly smudged reflection.
        lit = nsIrradiance(pos, float3(0.0, 1.0, 0.0), c0, c1, c2, c3);
        float smudge = fbm(pos.xz * 3.0);
        colour = neonColour * lit * power * (0.28 + 0.12 * smudge);
        float3 mirrored = reflect(rd, float3(0.0, 1.0, 0.0));
        float cosine = saturate(-rd.y);
        float fresnel = 0.05 + 0.95 * pow(1.0 - cosine, 5.0);
        float blur = 0.012 + 0.02 * smudge;
        float reflCore = 0.0;
        float reflHalo = nsGlow(pos, mirrored, 1e6, blur, c0, c1, c2, c3, reflCore);
        colour += (neonColour * reflHalo + coreColour * reflCore * 1.2) * power * (0.3 + 0.6 * fresnel);
    }
    else if (wallT < 1e5)
    {
        lit = nsIrradiance(pos, float3(0.0, 0.0, -1.0), c0, c1, c2, c3);
        colour = neonColour * lit * power * 0.5 * (0.75 + 0.5 * fbm(pos.xy * 2.5));
    }
    // Faint ambient so the room's shape never disappears.
    colour += float3(0.004, 0.004, 0.006);

    // The tube itself, and the glow in the air between it and the eye.
    float core = 0.0;
    float halo = nsGlow(eye, rd, hitT, 0.0, c0, c1, c2, c3, core);
    colour += neonColour * halo * power * (0.9 + Bass * 0.4);
    colour = lerp(colour, coreColour * (0.9 + 0.3 * power), saturate(core * 1.5));
    return colour;
}
