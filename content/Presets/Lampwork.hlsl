// Name: Lampwork
// Two shells of thick blown glass, cut into winding ribbons, turn slowly round a glowing striped bead: emerald
// glass outside and honey glass within, both bending the view of what lies behind them and catching the studio
// lights along every rim. The music plays in the light: each stripe of the bead burns from rose to gold with its
// own part of the music, bass at its foot and treble at its crown, the glass glows with it, and each beat sends a
// shell of light out through the ribbons.
// Family: Ray traced
// Tags: 3D, Colourful, Dark
// Added: 2026-11-16 02:52

static const float LwHeart = 0.44;      // how large the heart is
static const float LwOuter = 1.34;      // the middle of the outer shell's wall
static const float LwInner = 0.86;      // the middle of the inner shell's wall

static float3x3 LwOuterTurn;            // how each shell and the heart have turned so far
static float3x3 LwInnerTurn;
static float3x3 LwHeartTurn;
static float LwWide;                    // how far the outer shell is stretched sideways to suit the window

float lwLoud(float x)
{
    return saturate((spectrum(x) - 0.42 + 0.5 * x) / 0.7);
}

// How loud the music is for a spot at this height, bass at the foot and treble at the crown, read at ten fixed
// places and blended between them.
float lwLoudAt(float height)
{
    float k = saturate(0.5 + 0.5 * height) * 9.0;
    float low = floor(k);
    return lerp(lwLoud(0.08 + 0.06 * low), lwLoud(0.14 + 0.06 * low), smoothstep(0.0, 1.0, k - low));
}

float3x3 lwTurn(float a, float b)
{
    float sa, ca, sb, cb;
    sincos(a, sa, ca);
    sincos(b, sb, cb);
    return float3x3(ca, 0.0, -sa,
                    -sa * sb, cb, -ca * sb,
                    sa * cb, sb, ca * cb);
}

// A turn about an upright axis after a fixed lean, so that the axis itself stays where it is.
float3x3 lwSpin(float a, float b)
{
    float sa, ca, sb, cb;
    sincos(a, sa, ca);
    sincos(b, sb, cb);
    return float3x3(ca * cb, -ca * sb, -sa,
                    sb, cb, 0.0,
                    sa * cb, -sa * sb, ca);
}

float lwSmoothMax(float a, float b, float k)
{
    float h = max(k - abs(a - b), 0.0) / k;
    return max(a, b) + h * h * k * 0.25;
}

// A round shell cut into winding ribbons with rounded edges, thicker in some places than others, as blown glass is.
float lwRibbons(float3 q, float radius, float thick, float pitch, float drift, float level)
{
    float3 g = q * pitch;
    float weave = sin(g.x + drift) * cos(g.y) + sin(g.y - drift) * cos(g.z) + sin(g.z) * cos(g.x + drift);
    float lumps = sin(g.x * 2.7 + 1.0) * sin(g.y * 2.7) * sin(g.z * 2.7 - drift);
    float wall = abs(length(q) - radius) - thick * (1.0 + 0.5 * lumps);
    return lwSmoothMax(wall, (level - weave) / (pitch * 1.5), 0.13);
}

// Which shell a spot is nearer: true for the outer one. Also gives the spot in that shell's own turned space, the
// shell's make (the middle of its wall, how thick it is, how tightly it is wound, and where its ribbons are cut),
// how far its winding has drifted, and the least the other shell could be away. Neither shell can be nearer than
// its wall at its thickest, which takes no winding to work out, and the two never touch, so the shell that could
// be nearer by that measure is the nearer one wherever there is a surface close by.
bool lwNearerShell(float3 p, out float3 q, out float4 make, out float drift, out float otherLeast)
{
    float3 wide = float3(p.x / LwWide, p.y, p.z);
    float outerLeast = abs(length(wide) - LwOuter) - 0.13 * 1.5;
    float innerLeast = abs(length(p) - LwInner) - 0.085 * 1.5;
    bool outer = outerLeast < innerLeast;
    q = outer ? mul(LwOuterTurn, wide) : mul(LwInnerTurn, p);
    make = outer ? float4(LwOuter, 0.13, 2.3, 0.0) : float4(LwInner, 0.085, 3.5, 0.1);
    drift = outer ? Time * 0.2 : -Time * 0.17;
    otherLeast = outer ? innerLeast : outerLeast;
    return outer;
}

// How far a spot is outside the glass (less than nothing inside it). Only the nearer shell is measured in full;
// the other is taken at the least it could be, which is never the smaller of the two near a surface. Measuring
// both in full at every step made this the heaviest visual of all to draw, several times over.
float lwGlass(float3 p)
{
    float3 q;
    float4 make;
    float drift, otherLeast;
    lwNearerShell(p, q, make, drift, otherLeast);
    return min(lwRibbons(q, make.x, make.y, make.z, drift, make.w), otherLeast);
}

// How far a spot is outside the bead at the heart.
float lwHeart(float3 p)
{
    return length(p) - LwHeart;
}

// Which way a shell's surface faces at a spot, from the slopes of the same sums that shape it: keep the two in
// step with lwRibbons. One reckoning does it, where feeling for the surface on every side took six.
float3 lwRibbonsFacing(float3 q, float radius, float thick, float pitch, float drift, float level)
{
    float3 g = q * pitch;
    float sx, cx, sy, cy, sd, cd, sz, cz;
    sincos(g.x + drift, sx, cx);
    sincos(g.y, sy, cy);
    sincos(g.y - drift, sd, cd);
    sincos(g.z, sz, cz);
    float weave = sx * cy + sd * cz + sz * cx;
    float3 weaveSlope = float3(cx * cy - sz * sx, cd * cz - sx * sy, cz * cx - sd * sz);
    float3 turns = g * 2.7 + float3(1.0, 0.0, -drift);
    float3 st = sin(turns);
    float3 ct = cos(turns);
    float lumps = st.x * st.y * st.z;
    float3 lumpsSlope = 2.7 * float3(ct.x * st.y * st.z, st.x * ct.y * st.z, st.x * st.y * ct.z);
    float far = max(length(q), 1e-4);
    float wall = abs(far - radius) - thick * (1.0 + 0.5 * lumps);
    float3 wallSlope = (far > radius ? 1.0 : -1.0) * q / far - 0.5 * thick * pitch * lumpsSlope;
    float cut = (level - weave) / (pitch * 1.5);
    float3 cutSlope = -weaveSlope / 1.5;

    // Where the wall meets the cut edge the two are blended (see lwSmoothMax), and so are their slopes.
    float h = max(0.13 - abs(wall - cut), 0.0) / 0.13;
    return lerp(cutSlope, wallSlope, wall > cut ? 1.0 - 0.5 * h : 0.5 * h);
}

float3 lwGlassNormal(float3 p)
{
    float3 q;
    float4 make;
    float drift, otherLeast;
    bool outer = lwNearerShell(p, q, make, drift, otherLeast);
    float3 slope = lwRibbonsFacing(q, make.x, make.y, make.z, drift, make.w);

    // Back from the shell's own turned (and, for the outer shell, stretched) space to the room's.
    float3 n = outer ? mul(slope, LwOuterTurn) : mul(slope, LwInnerTurn);
    n.x /= outer ? LwWide : 1.0;
    return normalize(n);
}

// The studio as the glass mirrors it: a tall window over the left shoulder, a long strip lamp to the right, a
// cool wash across the back wall, and two coloured lamps behind that light the rims.
float3 lwRoom(float3 dir)
{
    float3 room = float3(0.01, 0.015, 0.03);
    float3 toWindow = normalize(float3(-0.55, 0.6, -0.58));
    float3 windowSide = normalize(cross(toWindow, float3(0.0, 1.0, 0.0)));
    float3 windowUp = cross(windowSide, toWindow);
    float2 pane = float2(dot(dir, windowSide), dot(dir, windowUp)) / max(dot(dir, toWindow), 0.05);
    float lit = smoothstep(0.34, 0.3, abs(pane.x)) * smoothstep(0.5, 0.46, abs(pane.y)) * step(0.0, dot(dir, toWindow));
    lit *= smoothstep(0.012, 0.03, abs(pane.x)) * smoothstep(0.012, 0.03, abs(pane.y - 0.1));
    room += float3(1.7, 1.8, 1.9) * lit;
    room += float3(0.9, 1.1, 1.5) * smoothstep(0.06, 0.03, abs(dir.x - 0.66)) * smoothstep(0.7, 0.5, abs(dir.y)) * step(dir.z, 0.0);
    room += float3(0.12, 0.3, 0.42) * smoothstep(0.1, 0.95, dir.z);
    room += float3(0.3, 0.9, 1.2) * pow(saturate(dot(dir, normalize(float3(0.62, 0.3, 0.72)))), 4.0);
    room += float3(1.1, 0.75, 0.3) * pow(saturate(dot(dir, normalize(float3(-0.7, -0.35, 0.62)))), 4.0);
    return room;
}

float3 Render(float2 uv, float2 p)
{
    float since = frac(BeatGlide);
    float flash = smoothstep(0.0, 0.1, since) * (1.0 - since) * (1.0 - since);
    LwOuterTurn = lwTurn(Time * 0.11, Time * 0.07);
    LwInnerTurn = lwTurn(1.0 - Time * 0.09, -Time * 0.13);
    LwHeartTurn = lwSpin(Time * 0.21, 0.4);
    LwWide = clamp(0.85 * Aspect, 1.0, 1.5);

    float3 eye = float3(0.3 * sin(Time * 0.13), 0.2 * sin(Time * 0.1), -2.75);
    float3 ray = cameraRay(eye, float3(0.0, 0.0, 0.0), float2(p.x, -p.y), 1.45, 0.0);
    float3 backdrop = float3(0.006, 0.008, 0.018) + float3(0.015, 0.03, 0.045) * exp(-dot(p, p) * 0.5);

    // Everything lies inside a ball; rays that miss it see only the dark of the room.
    float reach = (LwOuter + 0.24) * LwWide;
    float toward = -dot(eye, ray);
    float gap = dot(eye, eye) - toward * toward;
    if (gap > reach * reach)
        return backdrop;

    float t = max(toward - sqrt(reach * reach - gap), 0.0);
    float3 colour = float3(0.0, 0.0, 0.0);
    float3 through = float3(1.0, 1.0, 1.0);
    float within = 0.0;
    float outerGlass = 1.0;
    float walls = 0.0;
    float3 from = eye;
    int steps = qualitySteps(120);
    [loop]
    for (int i = 0; i < steps; i++)
    {
        float3 at = from + ray * t;
        float glass = lwGlass(at);
        float heart = lwHeart(at);

        // Where the ray meets the glass, going in or coming out, its facing is worked out here, in one place.
        bool enters = within < 0.5 && glass < 0.003 && glass <= heart;
        bool leaves = within > 0.5 && glass > -0.002 && t > 0.02;
        float3 n = float3(0.0, 1.0, 0.0);
        if (enters || leaves)
            n = lwGlassNormal(at);

        if (within < 0.5)
        {
            if (min(glass, heart) < 0.003)
            {
                if (heart < glass)
                {
                    // The heart: a glass bead wound with stripes, each burning from rose to gold with its own
                    // part of the music.
                    float3 hn = at / max(length(at), 1e-4);
                    float3 c = mul(LwHeartTurn, hn);
                    float wind = c.y + 0.13 * sin(atan2(c.z, c.x + 1e-6) * 3.0 + c.y * 5.0);
                    float k = saturate(0.5 + 0.5 * wind) * 9.0;
                    float low = floor(k);
                    float part = k - low;
                    float loud = lerp(lwLoud(0.08 + 0.06 * low), lwLoud(0.14 + 0.06 * low), smoothstep(0.8, 1.0, part));
                    float seam = smoothstep(0.0, 0.16, part) * smoothstep(1.0, 0.84, part);
                    float limb = saturate(-dot(ray, hn));
                    float heat = (0.1 + 1.0 * loud + 0.35 * flash) * (0.45 + 0.55 * seam) * (0.45 + 0.55 * limb);
                    float3 glow = lerp(float3(0.5, 0.03, 0.12), float3(1.7, 0.3, 0.22), smoothstep(0.0, 0.3, heat));
                    glow = lerp(glow, float3(1.9, 1.1, 0.3), smoothstep(0.25, 0.6, heat));
                    glow = lerp(glow, float3(2.0, 1.8, 1.4), smoothstep(0.6, 1.0, heat));
                    glow += lwRoom(reflect(ray, hn)) * (0.05 + 0.95 * pow(1.0 - limb, 5.0));
                    colour += through * glow;
                    through = float3(0.0, 0.0, 0.0);
                    break;
                }

                // Into the glass: its face mirrors the room and the heart, and the rest of the light bends in.
                float facing = saturate(-dot(ray, n));
                float mirror = 0.04 + 0.96 * pow(1.0 - facing, 5.0);
                float3 bounce = reflect(ray, n);
                float along = -dot(at, bounce);
                float miss = sqrt(max(dot(at, at) - along * along, 0.0));
                float nearHeart = length(at);
                float3 seen = lwRoom(bounce) + float3(1.9, 0.75, 0.3) * smoothstep(LwHeart * 2.2, LwHeart * 0.5, miss) * step(0.0, along);
                colour += through * mirror * seen;

                // The side of the glass that looks at the heart is warm with its light.
                float warm = saturate(dot(n, -at / nearHeart));
                float shine = 0.3 + 1.3 * lwLoudAt(at.y / nearHeart) + 2.0 * flash * exp(-(nearHeart - LwHeart - since * 1.6) * (nearHeart - LwHeart - since * 1.6) * 9.0);
                colour += through * float3(1.5, 0.5, 0.18) * warm * warm * shine * 0.16 / (0.3 + (nearHeart - LwHeart) * (nearHeart - LwHeart));
                through *= 1.0 - mirror;
                float3 bent = refract(ray, n, 0.77);
                from = at;
                ray = dot(bent, bent) > 0.5 ? bent : ray;
                t = 0.012;
                within = 1.0;
                outerGlass = step(1.1, length(float3(at.x / LwWide, at.y, at.z)));
                walls += 1.0;
                continue;
            }

            t += min(glass, heart) * 0.85;
            if (dot(at, at) > reach * reach * 1.05 && dot(at, ray) > 0.0)
                break;
        }
        else
        {
            if (leaves)
            {
                // Out of the glass again: it has taken its colour from the light, and glows with the heart's.
                float thick = t;
                float3 soak = lerp(float3(0.25, 0.75, 2.6), float3(2.3, 0.3, 1.05), outerGlass);
                float nearHeart2 = length(at);
                float loud2 = lwLoudAt(at.y / max(nearHeart2, 0.2));
                float pulse = flash * exp(-(nearHeart2 - LwHeart - since * 1.6) * (nearHeart2 - LwHeart - since * 1.6) * 9.0);
                float3 lit2 = lerp(float3(1.7, 0.7, 0.16), float3(0.9, 1.1, 0.35), outerGlass) * (0.15 + 1.6 * loud2 + 2.8 * pulse);
                colour += through * lit2 * (1.0 - exp(-thick * 3.0)) * 0.62 / (1.0 + 4.0 * (nearHeart2 - LwHeart) * (nearHeart2 - LwHeart));
                through *= exp(-soak * thick * 1.1);
                float3 bent2 = refract(ray, -n, 1.3);
                float out2 = step(0.5, dot(bent2, bent2));

                // The far face of the glass mirrors a little of the room too.
                colour += through * 0.12 * lwRoom(reflect(ray, n)) * out2;
                from = at;
                ray = out2 > 0.5 ? bent2 : reflect(ray, -n);
                t = 0.012;
                within = 1.0 - out2;
                if (walls > 3.5)
                    break;
                continue;
            }

            t += max(-glass, 0.012) * 0.9;
        }
    }

    return colour + through * backdrop;
}
