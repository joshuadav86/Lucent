// Name: Black Hole
// A black hole bends a glowing nebula into rings around itself; its disk burns violet where it rushes towards you and amber where it flees, and spacetime ripples on every beat.
// Family: Cosmic
// Tags: Space, Dark, 3D
// Added: 2026-10-16 23:54

// Units: the event horizon has radius 1. The photon sphere is at 1.5, the shadow's edge at an impact
// parameter of 2.6, and the disk's innermost stable orbit at 3.
static const float DiskInner = 3.0;
static const float DiskOuter = 13.0;

float holeHash(float3 p)
{
    p = frac(p * float3(0.1031, 0.1030, 0.0973));
    p += dot(p, p.yxz + 33.33);
    return frac((p.x + p.y) * p.z);
}

float holeNoise(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    float3 u = f * f * (3.0 - 2.0 * f);
    float n000 = holeHash(i);
    float n100 = holeHash(i + float3(1.0, 0.0, 0.0));
    float n010 = holeHash(i + float3(0.0, 1.0, 0.0));
    float n110 = holeHash(i + float3(1.0, 1.0, 0.0));
    float n001 = holeHash(i + float3(0.0, 0.0, 1.0));
    float n101 = holeHash(i + float3(1.0, 0.0, 1.0));
    float n011 = holeHash(i + float3(0.0, 1.0, 1.0));
    float n111 = holeHash(i + float3(1.0, 1.0, 1.0));
    return lerp(lerp(lerp(n000, n100, u.x), lerp(n010, n110, u.x), u.y),
                lerp(lerp(n001, n101, u.x), lerp(n011, n111, u.x), u.y), u.z);
}

// Turbulent gas sheared into long streaks along its orbits.
float diskStreaks(float radius, float angle, float seed)
{
    float3 q = float3(cos(angle) * 1.3, sin(angle) * 1.3, radius * 1.1 + seed);
    float total = 0.0;
    float amplitude = 0.55;
    [unroll]
    for (int octave = 0; octave < 4; octave++)
    {
        total += holeNoise(q) * amplitude;
        q = q * float3(1.9, 1.9, 2.9) + 7.1;
        amplitude *= 0.52;
    }
    return total;
}

// The current colours: four for the disk's Doppler shift (receding, crossing, approaching,
// fastest approaching) and two for the nebula.
struct HoleScheme
{
    float3 receding;
    float3 crossing;
    float3 approaching;
    float3 fastest;
    float3 nebulaA;
    float3 nebulaB;
};

// Three colour schemes the hole cycles through, cross-fading about once a minute:
// orchid (amber, magenta, violet), fire (crimson, orange, gold) and ice (indigo, teal, aqua).
HoleScheme holeSchemeAt(float scheme)
{
    HoleScheme orchid;
    orchid.receding = float3(1.0, 0.32, 0.05);
    orchid.crossing = float3(1.0, 0.2, 0.55);
    orchid.approaching = float3(0.45, 0.3, 1.0);
    orchid.fastest = float3(0.55, 0.85, 1.0);
    orchid.nebulaA = float3(0.75, 0.08, 0.6);
    orchid.nebulaB = float3(0.12, 0.3, 1.0);

    HoleScheme fire;
    fire.receding = float3(0.9, 0.06, 0.03);
    fire.crossing = float3(1.0, 0.42, 0.06);
    fire.approaching = float3(1.0, 0.78, 0.3);
    fire.fastest = float3(0.85, 0.92, 1.0);
    fire.nebulaA = float3(0.8, 0.12, 0.1);
    fire.nebulaB = float3(0.04, 0.45, 0.6);

    HoleScheme ice;
    ice.receding = float3(0.25, 0.15, 0.9);
    ice.crossing = float3(0.0, 0.62, 0.85);
    ice.approaching = float3(0.45, 1.0, 0.9);
    ice.fastest = float3(0.95, 1.0, 1.0);
    ice.nebulaA = float3(0.45, 0.15, 1.0);
    ice.nebulaB = float3(0.85, 0.15, 0.55);

    HoleScheme chosen = ice;
    if (scheme < 0.5)
    {
        chosen = orchid;
    }
    else if (scheme < 1.5)
    {
        chosen = fire;
    }
    return chosen;
}

HoleScheme holeScheme()
{
    float phase = Time / 55.0;
    float scheme = wrap(floor(phase), 3.0);
    float fade = smoothstep(0.7, 1.0, frac(phase));
    HoleScheme a = holeSchemeAt(scheme);
    HoleScheme b = holeSchemeAt(wrap(scheme + 1.0, 3.0));
    HoleScheme blend;
    blend.receding = lerp(a.receding, b.receding, fade);
    blend.crossing = lerp(a.crossing, b.crossing, fade);
    blend.approaching = lerp(a.approaching, b.approaching, fade);
    blend.fastest = lerp(a.fastest, b.fastest, fade);
    blend.nebulaA = lerp(a.nebulaA, b.nebulaA, fade);
    blend.nebulaB = lerp(a.nebulaB, b.nebulaB, fade);
    return blend;
}

// The disk's colour, an exaggerated Doppler shift: one hue where the gas flees from us, others
// where it crosses our view and where it rushes towards us. The hottest gas burns white.
float3 dopplerColour(HoleScheme scheme, float shift, float heat)
{
    float3 colour = lerp(scheme.receding, scheme.crossing, smoothstep(0.7, 1.0, shift));
    colour = lerp(colour, scheme.approaching, smoothstep(1.0, 1.3, shift));
    colour = lerp(colour, scheme.fastest, smoothstep(1.3, 1.7, shift));
    return lerp(colour, float3(1.0, 0.96, 0.93), smoothstep(0.8, 1.6, heat)) * smoothstep(0.0, 0.25, heat);
}

// Light and opacity of the disk where a ray crosses it.
float4 diskSample(float3 hit, float3 rayVelocity, HoleScheme scheme)
{
    float radius = length(hit.xz);
    float angle = atan2(hit.z, hit.x);

    // Inner gas orbits faster than outer gas. Two copies of the texture take turns fading in
    // and out, so the shearing never winds the streaks up too tightly.
    float spin = 2.4 * pow(radius, -1.5);
    const float cycle = 8.0;
    float phaseA = frac(Time / cycle);
    float phaseB = frac(Time / cycle + 0.5);
    float weightA = 1.0 - abs(phaseA * 2.0 - 1.0);
    float streaks = diskStreaks(radius, angle + spin * phaseA * cycle, 0.0) * weightA
                  + diskStreaks(radius, angle + spin * phaseB * cycle, 19.3) * (1.0 - weightA);
    // Blending two textures flattens the contrast mid-way; restore it.
    streaks = 0.53 + (streaks - 0.53) * rsqrt(weightA * weightA + (1.0 - weightA) * (1.0 - weightA));
    streaks = saturate((streaks - 0.25) * 1.8);

    // A few narrow gaps, like the divisions in a planet's rings.
    float gaps = smoothstep(0.0, 0.12, abs(frac(radius * 0.37 + 0.2) - 0.5))
               * smoothstep(0.0, 0.05, abs(frac(radius * 1.13) - 0.5) + 0.03);

    // A shock wave that rolls out through the disk after each beat.
    float shock = exp(-abs(radius - DiskInner - (1.0 - Beat) * 8.0) * 2.5) * Beat;

    float inner = smoothstep(DiskInner, DiskInner + 0.35, radius);
    float outer = smoothstep(DiskOuter, DiskOuter - 6.0, radius);
    float profile = inner * outer * pow(DiskInner / radius, 1.3);
    float density = profile * (0.3 + 1.1 * streaks) * (0.4 + 0.6 * gaps) + shock * inner * 0.6;

    // Relativistic beaming: gas moving towards us is brighter, receding gas dimmer.
    float speed = min(sqrt(0.5 / radius), 0.6);
    float3 orbit = normalize(float3(-hit.z, 0.0, hit.x));
    float towardsUs = dot(orbit, -normalize(rayVelocity));
    float shift = sqrt(1.0 - speed * speed) / (1.0 - speed * towardsUs) * sqrt(saturate(1.0 - 1.0 / radius));

    float heat = (pow(DiskInner / radius, 1.1) * (0.7 + streaks * 0.4) + Bass * 0.15 + shock * 0.6) * shift;
    float3 colour = dopplerColour(scheme, shift, heat) * density * pow(shift, 2.5) * (1.7 + Bass * 1.2);
    return float4(colour, saturate(density * 1.3));
}

// The sky behind the hole, looked up by the direction a ray escapes in. The screen-space
// derivatives of that direction turn each star into a crisp dot wherever the lensing moves it,
// brightened where the lens magnifies it.
float3 holeSky(float3 direction, float pixelAngle, HoleScheme scheme)
{
    float3 a = abs(direction);
    float2 face = direction.xy / a.z;
    float faceId = direction.z > 0.0 ? 0.0 : 1.0;
    if (a.x > a.y && a.x > a.z)
    {
        face = direction.yz / a.x;
        faceId = direction.x > 0.0 ? 2.0 : 3.0;
    }
    else if (a.y > a.z)
    {
        face = direction.xz / a.y;
        faceId = direction.y > 0.0 ? 4.0 : 5.0;
    }

    // Map a small step on the sky back to a step in pixels.
    float2 alongX = ddx(face);
    float2 alongY = ddy(face);
    float area = alongX.x * alongY.y - alongX.y * alongY.x;
    float safeArea = abs(area) < 1e-12 ? 1e-12 : area;
    float2x2 toPixels = float2x2(alongY.y, -alongY.x, -alongX.y, alongX.x) / safeArea;
    float magnify = clamp(pixelAngle * pixelAngle / (abs(area) + 1e-12), 0.3, 3.0);

    // A nebula of fine glowing filaments, which the lensing wraps into rings around the hole.
    float3 q = direction * 2.0 + float3(0.0, Time * 0.01, 0.0);
    float warp = holeNoise(q * 1.3 + 4.0);
    float cloud = holeNoise(q * 1.8 + warp * 1.5) * 0.65 + holeNoise(q * 3.9 + warp) * 0.35;
    float filaments = pow(1.0 - abs(cloud * 2.0 - 1.0), 12.0);
    float fine = pow(1.0 - abs(holeNoise(q * 8.0 + warp * 2.0) * 2.0 - 1.0), 10.0);
    float glowField = smoothstep(0.35, 0.8, holeNoise(q * 0.7 + 2.0));
    float3 tint = lerp(scheme.nebulaA, scheme.nebulaB, holeNoise(q * 0.9 + 9.0));
    float3 colour = tint * glowField * (filaments * 0.3 + fine * 0.18 + 0.012);

    [unroll]
    for (int layer = 0; layer < 2; layer++)
    {
        float density = layer == 0 ? 42.0 : 11.0;
        float2 grid = face * density + faceId * 13.7 + layer * 5.3;
        float2 cell = floor(grid);
        float chance = hash(cell + faceId);
        float2 spot = float2(hash(cell + 3.1), hash(cell + 7.7)) * 0.8 + 0.1;
        float2 pixels = mul(toPixels, (spot - frac(grid)) / density);
        float brightness = layer == 0 ? pow(chance, 40.0) * 2.0 : pow(chance, 12.0) * 4.0;
        float twinkle = 1.0 + 0.5 * sin(Time * (3.0 + chance * 7.0) + chance * 40.0) * Treble;
        float3 starTint = lerp(float3(1.0, 0.78, 0.6), float3(0.65, 0.78, 1.0), hash(cell + 11.0));
        colour += starTint * brightness * twinkle * exp(-dot(pixels, pixels) * 1.4) * magnify;
    }
    return colour;
}

float3 Render(float2 uv, float2 p)
{
    // A slow orbit that drifts between a nearly edge-on view and a view from above.
    float azimuth = Time * 0.05;
    float elevation = 0.05 + 0.28 * (0.5 + 0.5 * sin(Time * 0.043 - 1.0));
    float range = 15.0 + 2.5 * sin(Time * 0.031);
    float3 eye = range * float3(cos(azimuth) * cos(elevation), sin(elevation), sin(azimuth) * cos(elevation));
    // Each beat sends a ripple through spacetime, rolling outward from the hole.
    float rippleRadius = (1.0 - Beat) * 2.2;
    float fromCentre = length(p);
    float ripple = exp(-(fromCentre - rippleRadius) * (fromCentre - rippleRadius) * 25.0) * Beat;
    float2 view = p * (1.0 + 0.03 * ripple * sin((fromCentre - rippleRadius) * 40.0));

    const float zoom = 1.6;
    float3 direction = cameraRay(eye, float3(0.0, 0.0, 0.0), view, zoom, sin(Time * 0.07) * 0.15);
    float pixelAngle = 2.0 / (Resolution.y * zoom);

    HoleScheme scheme = holeScheme();

    // Trace the light ray backwards through the curved space around the hole.
    float3 position = eye;
    float3 velocity = direction;
    float3 angular = cross(position, velocity);
    float bend = 1.5 * dot(angular, angular);

    float3 colour = 0.0;
    float opacity = 0.0;
    float captured = 0.0;
    int steps = qualitySteps(100);
    [loop]
    for (int i = 0; i < steps; i++)
    {
        float r2 = dot(position, position);
        float radius = sqrt(r2);
        float stepSize = clamp(radius - 1.0, 0.05, 3.0) * 0.2;
        velocity -= bend * position / (r2 * r2 * radius) * stepSize;
        float3 next = position + velocity * stepSize;

        // Crossing the disk plane: add the disk's light in front of whatever lies behind it.
        if (position.y * next.y < 0.0)
        {
            float3 hit = lerp(position, next, position.y / (position.y - next.y));
            float hitRadius = length(hit.xz);
            if (hitRadius > DiskInner && hitRadius < DiskOuter)
            {
                float4 disk = diskSample(hit, velocity, scheme);
                colour += disk.rgb * (1.0 - opacity);
                opacity += disk.a * (1.0 - opacity);
            }
        }

        // Jets along the spin axis. Where this step passes closest to the axis, add the
        // beam's light integrated across its width, so the beam stays crisp at any step size.
        float2 across = next.xz - position.xz;
        float acrossLength = max(dot(across, across), 1e-8);
        float t = -dot(position.xz, across) / acrossLength;
        float axis = length(position.xz + across * t);
        float height = abs(lerp(position.y, next.y, t));
        float width = 0.04 + height * 0.025;
        if (t > 0.0 && t <= 1.0 && axis < width * 4.0 && height > 1.2)
        {
            float slant = stepSize * length(velocity) / sqrt(acrossLength);
            float knots = 0.25 + pow(0.5 + 0.5 * sin(height * 1.4 - Time * 5.0), 6.0);
            float beatKnot = exp(-abs(height - 1.5 - (1.0 - Beat) * 14.0) * 1.0) * Beat * 5.0;
            float beam = exp(-axis * axis / (width * width)) * width * 1.77 * min(slant, 30.0);
            float fade = smoothstep(1.2, 3.0, height) * exp(-height * 0.09);
            colour += float3(0.45, 0.65, 1.0) * beam * fade * (knots * (0.6 + Bass * 1.2) + beatKnot) * (1.0 - opacity);
        }

        position = next;
        if (radius < 1.0)
        {
            captured = 1.0;
            break;
        }
        // Stop once the ray is escaping: nothing but sky and jets lies further out.
        if (radius > 40.0 || opacity > 0.98 || (radius > 24.0 && dot(position, velocity) > 0.0))
        {
            break;
        }
    }

    // The sky behind everything (the derivatives must be taken outside any branch).
    colour += holeSky(normalize(velocity), pixelAngle, scheme) * (1.0 - opacity) * (1.0 - captured);

    // The photon ring: light that has circled the hole, hugging the edge of the shadow.
    // It is brightest on the side where the disk comes towards us, and ripples with the spectrum.
    float impact = length(angular);
    float edge = abs(impact - 2.62) / (pixelAngle * range);
    float2 screen = normalize(p + 0.0001);
    float side = 0.5 - 0.5 * screen.x;
    float band = spectrum(0.1 + 0.6 * abs(atan2(screen.y, screen.x)) / PI);
    float ring = exp(-edge * edge * 0.6) * (0.6 + band * 1.2) + exp(-edge * 0.25) * 0.12;
    float3 ringColour = dopplerColour(scheme, lerp(0.75, 1.45, side), 0.6 + side * 0.5);
    colour += ringColour * ring * (0.5 + side * 1.2) * (1.0 - opacity * 0.6);

    return colour;
}
