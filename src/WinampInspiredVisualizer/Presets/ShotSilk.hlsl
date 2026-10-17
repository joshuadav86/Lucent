// Name: Shot Silk
// Billowing folds of shot silk that flash between two colours as they turn, with sheen gliding along every fold and a shiver running through the cloth on each beat.

// Height of the silk at p: long folds bending into S-curves, merging and parting as they flow.
float silkHeight(float2 p, float2 along, float depth)
{
    float2 bent = p + 0.28 * float2(sin(p.y * 1.3 + Time * 0.23 + sin(p.x * 0.7)), sin(p.x * 1.1 - Time * 0.19 + sin(p.y * 0.6)));
    float u = dot(bent, along) * 8.0;
    float v = dot(bent, float2(-along.y, along.x)) * 2.2;
    // The folds are deep in some places and almost smooth in others.
    float billow = 0.7 + 0.6 * noise(bent * 0.9 + float2(Time * 0.05, 0.0));
    float h = 0.55 * sin(u + 0.9 * sin(v * 1.2 + Time * 0.35))
            + 0.3 * sin(1.9 * u + 1.4 * sin(v * 0.8 - Time * 0.3) + 2.0)
            + 0.12 * sin(3.7 * u + 2.1 * sin(v * 1.6 + Time * 0.5) + 4.0);
    // A second, gentler set of folds across the first.
    h = h * billow + 0.25 * sin(dot(bent, float2(along.y, -along.x)) * 4.0 + 1.5 * sin(u * 0.4 + Time * 0.2) - Time * 0.25);
    // Round crests, tight creases.
    h = h - 0.3 * h * h;

    // A shiver runs through the cloth on each beat.
    float front = (1.0 - Beat) * 14.0 - 6.0;
    h += Beat * 0.3 * sin(u * 3.0 - front * 3.0) * exp(-(u * 0.3 - front) * (u * 0.3 - front) * 0.8);
    return h * depth;
}

// Turns a colour around the grey axis (a hue shift that keeps brightness).
float3 silkHueTurn(float3 colour, float angle)
{
    const float3 grey = float3(0.57735, 0.57735, 0.57735);
    float s, c;
    sincos(angle, s, c);
    return colour * c + cross(grey, colour) * s + grey * dot(grey, colour) * (1.0 - c);
}

float3 Render(float2 uv, float2 p)
{
    float pixel = pixelSize();
    float turn = 0.6 + 0.35 * sin(Time * 0.045);
    float2 along = float2(cos(turn), sin(turn));
    float depth = 0.07 + Bass * 0.02;

    float h = silkHeight(p, along, depth);
    const float e = 0.002;
    float hx = silkHeight(p + float2(e, 0.0), along, depth);
    float hy = silkHeight(p + float2(0.0, e), along, depth);
    float2 slope = float2(hx - h, hy - h) / e;
    float3 normal = normalize(float3(-slope, 1.0));

    // Lights: a warm key low across the cloth, circling slowly, and a cool fill opposite.
    float keyAngle = turn + 0.5 * sin(Time * 0.09);
    float3 key = normalize(float3(cos(keyAngle), sin(keyAngle), 1.2));
    float3 fill = normalize(float3(-key.x, -key.y, 0.9));
    const float3 view = float3(0.0, 0.0, 1.0);

    // Crisp shadows where a fold stands between this point and the key light.
    float2 towards = normalize(key.xy);
    float shadow = 1.0;
    [unroll]
    for (int s = 1; s <= 3; s++)
    {
        float reach = s * 0.035;
        float blocker = silkHeight(p + towards * reach, along, depth);
        float rise = blocker - h - reach * key.z / length(key.xy);
        shadow = min(shadow, saturate(1.0 - rise / (pixel * 2.5)));
    }

    // Shot silk: two colours woven together, one where the cloth faces us, the other where it turns away.
    float phase = Time / 45.0;
    float blend = smoothstep(0.7, 1.0, frac(phase));
    float pick = wrap(floor(phase), 3.0);
    float3 faceA = float3(0.4, 0.04, 0.55);
    float3 faceB = float3(0.04, 0.12, 0.55);
    float3 faceC = float3(0.5, 0.03, 0.1);
    float3 turnA = float3(1.0, 0.55, 0.12);
    float3 turnB = float3(1.0, 0.35, 0.55);
    float3 turnC = float3(0.1, 0.8, 0.8);
    float3 faceTint = pick < 0.5 ? lerp(faceA, faceB, blend) : (pick < 1.5 ? lerp(faceB, faceC, blend) : lerp(faceC, faceA, blend));
    float3 turnTint = pick < 0.5 ? lerp(turnA, turnB, blend) : (pick < 1.5 ? lerp(turnB, turnC, blend) : lerp(turnC, turnA, blend));
    float tilt = saturate(0.5 - dot(slope, along) * 1.2);
    float3 cloth = lerp(faceTint, turnTint * 0.5, smoothstep(0.3, 1.0, tilt) * 0.5);
    // The dye drifts in hue across the cloth, and the sheen shimmers from fold to fold.
    float dye = sin(dot(p, float2(0.9, -0.6)) * 1.3 + Time * 0.12);
    cloth = max(silkHueTurn(cloth, dye * 0.35), 0.0);
    float3 sheenTint = max(silkHueTurn(turnTint, -0.35 - 0.35 * sin(dot(p, along) * 3.0 - Time * 0.4)), 0.0);

    // Soft light, deep shade in the creases.
    float wrapKey = saturate((dot(normal, key) + 0.2) / 1.2);
    float wrapFill = saturate((dot(normal, fill) + 0.2) / 1.2);
    float crease = smoothstep(-depth * 0.9, depth * 0.3, h) * 0.85 + 0.15;
    float3 colour = cloth * (wrapKey * shadow * 0.9 + wrapFill * 0.25) * crease * crease;

    // Sheen along the threads, which run across the folds.
    float3 thread = normalize(float3(along, dot(along, slope)));
    float3 halfKey = normalize(key + view);
    float3 halfFill = normalize(fill + view);
    float th = dot(thread, halfKey);
    float tf = dot(thread, halfFill);
    float sheenKey = pow(saturate(1.0 - th * th), 350.0);
    float sheenFill = pow(saturate(1.0 - tf * tf), 120.0);
    float broad = pow(saturate(1.0 - th * th), 25.0);
    colour += sheenTint * broad * 0.25 * shadow * crease;
    colour += lerp(sheenTint, float3(1.0, 1.0, 1.0), 0.35) * sheenKey * 1.6 * shadow * crease;
    colour += lerp(faceTint, float3(0.6, 0.8, 1.0), 0.5) * sheenFill * 0.35 * crease;

    // Metallic threads glint in the sheen with the treble.
    float2 grid = p * 70.0;
    float2 cell = floor(grid);
    float2 spot = float2(hash(cell + 0.5), hash(cell + 3.3)) * 0.6 + 0.2;
    float gap = length(frac(grid) - spot) / (70.0 * pixel);
    float glint = step(0.97, hash(cell + 7.7)) * exp(-gap * gap * 1.2) * saturate(broad * 3.0 - 2.0) * shadow;
    colour += float3(1.0, 0.95, 0.85) * glint * (0.3 + Treble * 3.0);
    return colour;
}
