// Name: Jewel Bulb
// A bulb turned like a piece of jewellery: a gold body set with green studs, each stud set with red ones, and
// those with gold again, glossy under studio lights against a pale backdrop. The studs swell with the bass,
// mid and treble, the smallest ones creep round their parents as the song plays, and the lights warm on every beat.
// Family: Fractal
// Tags: 3D, Colourful, Geometric
// Added: 2026-11-15 19:48

// Every ball carries twenty-six smaller balls: one on each face, edge and corner of the cube it would fit in.
// To find the nearest stud, a point is mirrored into one forty-eighth of the ball, where only three studs
// can be nearest, and then moved into that stud's own space, where the same question is asked again.
static const float3 Face = float3(1.0, 0.0, 0.0);
static const float3 Edge = float3(0.70711, 0.70711, 0.0);
static const float3 Corner = float3(0.57735, 0.57735, 0.57735);

// One ball of radius 1 and its three generations of studs. tier comes back as 0 on the ball and 1, 2, 3 on
// each generation, blended where they join.
float jbBall(float3 x, float3 swell, float creep, out float tier)
{
    float d = length(x) - 1.0;
    tier = 0.0;
    float3 q = x;
    float size = 1.0;
    [unroll]
    for (int i = 0; i < 3; i++)
    {
        q = abs(q);
        q.xy = q.x < q.y ? q.yx : q.xy;
        q.xz = q.x < q.z ? q.zx : q.xz;
        q.yz = q.y < q.z ? q.zy : q.yz;
        float onFace = dot(q, Face);
        float onEdge = dot(q, Edge);
        float onCorner = dot(q, Corner);
        float3 axis = Face;
        float girth = 0.3;
        if (onEdge > onFace && onEdge > onCorner)
        {
            axis = Edge;
            girth = 0.2;
        }
        else if (onCorner > onFace)
        {
            axis = Corner;
            girth = 0.24;
        }

        girth *= 1.0 + (i == 0 ? swell.x : (i == 1 ? swell.y : swell.z));
        // Each stud sits a little sunk into its parent.
        q = (q - axis * (1.0 + girth * 0.5)) / girth;
        size *= girth;
        float stud = (length(q) - 1.0) * size;
        float join = 0.1 * size;
        float h = saturate(0.5 + 0.5 * (d - stud) / join);
        tier = lerp(tier, (float)(i + 1), h);
        d = lerp(d, stud, h) - join * h * (1.0 - h);
        // The next generation is turned, more the smaller it is, so it creeps round as the song plays.
        q.xy = rotate(q.xy, creep * (float)(i + 1));
        q.yz = rotate(q.yz, 0.6);
    }

    return d;
}

// The whole jewel: a body, a head half its size and a tip half as big again, along one line.
float jbMap(float3 x, float3 swell, float creep, out float tier)
{
    float tierHead;
    float tierTip;
    float body = jbBall(x, swell, creep, tier);
    float head = jbBall((x + float3(1.52, 0.0, 0.0)) / 0.5, swell, creep + 1.0, tierHead) * 0.5;
    float tip = jbBall((x + float3(2.3, 0.0, 0.0)) / 0.25, swell, creep + 2.0, tierTip) * 0.25;
    tier = head < body ? tierHead : tier;
    body = min(body, head);
    tier = tip < body ? tierTip : tier;
    return min(body, tip) * 0.85;
}

float3 Render(float2 uv, float2 p)
{
    float3 swell = float3(Bass * 0.09 + BeatSwell * 0.07, Mid * 0.14, Treble * 0.18) + 0.04 * sin(Time * float3(0.31, 0.43, 0.57));
    float creep = Time * 0.08 + BeatGlide * 0.12;
    float beatLight = SongMapped > 0.0 ? 0.35 + Beat * 0.65 : Beat;

    float turn = Time * 0.12;
    float tilt = 0.3 + 0.2 * sin(Time * 0.061);
    float3 middle = float3(-0.62, 0.0, 0.0);
    float3 eye = middle + float3(sin(turn) * cos(tilt), sin(tilt), cos(turn) * cos(tilt)) * 6.4;
    float3 ray = cameraRay(eye, middle, float2(p.x, -p.y), 3.1 - DropPulse * 0.12, 0.0);

    // A studio backdrop: white above, falling to grey, with the jewel's soft shadow on the floor beneath it.
    float3 backdrop = lerp(float3(1.05, 1.05, 1.04), float3(0.42, 0.43, 0.46), smoothstep(-0.9, 1.1, p.y + 0.25 * dot(p, p)));
    float pool = exp(-(p.x * p.x * 0.7 + (p.y - 0.78) * (p.y - 0.78) * 18.0));
    backdrop *= 1.0 - 0.5 * pool;
    backdrop *= 0.94 + Level * 0.1;

    // Only rays that pass near the jewel need walking.
    float3 from = eye - float3(-0.75, 0.0, 0.0);
    float b = dot(from, ray);
    float reached = b * b - (dot(from, from) - 2.5 * 2.5);
    if (reached <= 0.0)
    {
        return backdrop * (0.92 + Intensity * 0.12);
    }

    float t = max(-b - sqrt(reached), 0.0);
    float end = -b + sqrt(reached);
    bool hit = false;
    float tier = 0.0;
    float touch = 0.0;
    int steps = qualitySteps(90);
    [loop]
    for (int i = 0; i < steps; i++)
    {
        float d = jbMap(eye + ray * t, swell, creep, tier);
        touch = 0.0005 * surfacePrecision() * t;
        if (d < touch)
        {
            hit = true;
            break;
        }

        t += d;
        if (t > end)
        {
            break;
        }
    }

    if (!hit)
    {
        return backdrop * (0.92 + Intensity * 0.12);
    }

    float3 x = eye + ray * t;
    float unused;
    float2 e = float2(1.0, -1.0) * 0.0025;
    float3 normal = normalize(
        e.xyy * jbMap(x + e.xyy, swell, creep, unused) +
        e.yyx * jbMap(x + e.yyx, swell, creep, unused) +
        e.yxy * jbMap(x + e.yxy, swell, creep, unused) +
        e.xxx * jbMap(x + e.xxx, swell, creep, unused));

    // Gold body, green studs, red studs on those, and gold again on the smallest.
    float3 gold = float3(1.0, 0.7, 0.2);
    float3 green = float3(0.04, 0.5, 0.26);
    float3 red = float3(0.8, 0.05, 0.1);
    float3 paint = lerp(gold, green, smoothstep(0.35, 0.65, tier));
    paint = lerp(paint, red, smoothstep(1.35, 1.65, tier));
    paint = lerp(paint, gold, smoothstep(2.35, 2.65, tier));
    float band = spectrum(0.03 + saturate(tier / 3.0) * 0.7);
    paint *= 0.9 + band * 0.35;

    float3 forward = normalize(middle - eye);
    float3 right = normalize(float3(forward.z, 0.0, -forward.x));
    float3 up = cross(forward, right);
    float3 key = normalize(-forward * 0.5 + right * 0.55 + up * 0.75);
    float3 fill = normalize(-forward * 0.6 - right * 0.7 + up * 0.1);
    float lit = saturate(dot(normal, key));
    float filled = saturate(dot(normal, fill) * 0.5 + 0.5);
    float sky = 0.5 + 0.5 * dot(normal, up);
    float crevice = saturate(jbMap(x + normal * 0.06, swell, creep, unused) / 0.06);
    float shade = 0.3 + 0.7 * crevice;
    float warm = 1.0 + beatLight * 0.12 + DropPulse * 0.2;
    float3 colour = paint * (0.14 + 0.3 * sky + 0.2 * filled + 0.85 * lit * warm) * shade;
    // Polished: a hard glint from the key light and the white studio mirrored at grazing angles.
    float3 half0 = normalize(key - ray);
    colour += float3(1.0, 0.96, 0.9) * pow(saturate(dot(normal, half0)), 90.0) * 1.3 * shade;
    float3 mirrored = reflect(ray, normal);
    float glance = pow(saturate(1.0 + dot(normal, ray)), 3.0);
    colour += lerp(paint, float3(1.0, 1.0, 1.0), 0.6) * (0.5 + 0.5 * mirrored.y) * (0.08 + 0.6 * glance) * shade;
    return colour * (0.92 + Intensity * 0.12);
}
