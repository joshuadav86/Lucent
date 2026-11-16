// Name: Sunward
// A flight up a river valley toward the sun, drawn like a poster in flat bright colours and black outlines. The
// river runs between stepped banks lined with arcades, and great stone rings pierced with round windows stand
// across it one after another, each carrying four smaller rings, and those four more, down to the least. The
// rings lean and turn as you pass through them. The music plays everywhere: the river comes down in bands, one
// with every beat; the land's colours move a storey down on every beat, and each storey shines with its own part
// of the music, bass in the great rings and treble in the least; the sun is a flower of light that swells on the
// beat, with twelve beams that each follow a part of the music; every beat sends a flash of light up the valley,
// and every eight beats the sky changes, from rose dusk to green dawn to orange noon.
// Family: Fly-through
// Tags: 3D, Colourful, Intense
// Added: 2026-11-16 16:19

static float2 SwLean;                   // cos and sin of how far each storey of rings leans from the one before
static float2 SwTwist;                  // and of how far it turns
static float SwSound;                   // how loud the music is just now, taken across all its parts

// One part of the music, with the high notes made up to stand level with the low ones.
float swPart(float x)
{
    return spectrum(x) * (1.0 + 0.9 * x);
}

// How loud one part of the music is, judged against the music as a whole, so that a quiet song shows as plainly
// as a loud one.
float swLoud(float x)
{
    return saturate((swPart(x) - 0.62 * SwSound) / (0.6 * SwSound + 0.1));
}

// How far the river has wandered to one side at each place along the valley.
float swWander(float z)
{
    return 0.55 * sin(z * 0.31) + 0.25 * sin(z * 0.73 + 1.0);
}

// How far a spot is outside the land. Also which part of it is nearest: a storey of rings (0 for the greatest),
// the riverbed (-1), or one of the three banks (-2, -3, -4), with -0.5 added inside a bank's arcade; and where
// the spot lies on that part.
float swLand(float3 p, out float part, out float3 on)
{
    p.x -= swWander(p.z);
    float across = abs(p.x);

    // The valley: the river in the middle, and three banks rising in steps to either side, each with an arcade
    // of arched doorways let into its face.
    float d = p.y;
    part = -1.0;
    on = p;
    float3 bank = across < 2.05 ? float3(1.5, 0.0, 0.42) : across < 3.3 ? float3(2.6, 0.42, 0.9) : float3(4.0, 0.9, 1.6);
    float tall = bank.z - bank.y;
    float block = max(bank.x - across, p.y - bank.z);
    float2 door = float2(wrap(p.z, tall * 1.7) - tall * 0.85, p.y - bank.y) / tall;
    float opening = max(door.y < 0.38 ? abs(door.x) - 0.3 : length(float2(door.x, door.y - 0.38)) - 0.3, -door.y) * tall;
    float arcade = max(opening, max(across - bank.x - 0.45 * tall, bank.x - 0.2 - across));
    block = max(block, -arcade);
    if (block < d)
    {
        d = block;
        part = (across < 2.05 ? -2.0 : across < 3.3 ? -3.0 : -4.0) - 0.5 * step(opening, 0.0) * step(bank.x + 0.02, across);
    }

    // Great rings stand across the river, one after another, each a flat band of stone pierced with round windows,
    // with a way left clear through the middle for the flight.
    float3 q = float3(across, p.y, wrap(p.z, 6.6) - 3.3) / 1.7;
    float scale = 1.0 / 1.7;
    float clear = 0.62 - length(float2(across, p.y - 0.7));
    [loop]
    for (int i = 0; i < 5; i++)
    {
        float far = length(q.xy);
        float2 band = abs(float2(far - 0.8, q.z)) - float2(0.2, 0.07);
        float ring = length(max(band, 0.0)) + min(max(band.x, band.y), 0.0);
        if (i < 3)
        {
            float round = atan2(q.y, q.x);
            float sector = round - (floor(round / 0.449 + 0.5)) * 0.449;
            ring = max(ring, 0.085 - length(float2(far * sin(sector), far * cos(sector) - 0.8)));
        }

        float arch = max(ring / scale, clear);
        if (arch < d)
        {
            d = arch;
            part = i;
            on = q;
        }

        // The next storey: four smaller rings, set round this one and turned across it.
        q.xz = abs(q.xz) - float2(0.62, 0.5);
        q.xz = float2(SwTwist.x * q.x - SwTwist.y * q.z, SwTwist.y * q.x + SwTwist.x * q.z);
        q.y -= 0.42;
        q.xy = float2(SwLean.x * q.x - SwLean.y * q.y, SwLean.y * q.x + SwLean.x * q.y);
        q *= 1.75;
        scale *= 1.75;
    }

    return d * 0.8;
}

float swLandOnly(float3 p)
{
    float part;
    float3 on;
    return swLand(p, part, on);
}

// The colours the land is painted in, a wheel that comes round again.
float3 swPaint(float h)
{
    float3 c = 0.5 + 0.5 * cos(6.2832 * (h + float3(0.0, 0.33, 0.67)));
    return 0.06 + 1.1 * c * c * (3.0 - 2.0 * c);
}

float3 Render(float2 uv, float2 p)
{
    float beats = wrap(BeatGlide, 4096.0);
    float swell = BeatSwell;
    float beatTurn = 6.2832 * BeatPhase;
    float barTurn = 6.2832 * BarPhase;
    // How far through the beat the music is: read from the beat being followed or, with no steady beat, from how
    // far the last one has faded.
    float since = SongMapped > 0.25 ? BeatPhase : saturate(-log(max(Beat, 0.004)) / 2.75);
    float flash = smoothstep(0.0, 0.08, since) * (1.0 - since) * (1.0 - since);

    SwSound = 0.0;
    [unroll]
    for (int k = 0; k < 6; k++)
        SwSound += swPart(0.08 + 0.12 * k) / 6.0;

    sincos(0.5 + 0.08 * sin(Time * 0.13) + 0.07 * sin(barTurn), SwLean.y, SwLean.x);
    sincos(1.5708 + 0.16 * sin(Time * 0.09), SwTwist.y, SwTwist.x);

    // The flight: low over the middle of the river, rising and sinking, looking a little way ahead.
    float gone = Time * 1.5;
    float3 eye = float3(swWander(gone) + 0.2 * sin(Time * 0.37), 0.62 + 0.13 * sin(Time * 0.29) + 0.04 * sin(barTurn), gone);
    float3 ahead = float3(swWander(gone + 2.5), 0.74, gone + 2.5);
    float3 ray = cameraRay(eye, ahead, float2(p.x, -p.y), 1.2, 0.08 * sin(Time * 0.21) + 0.03 * sin(barTurn + 1.0));

    float t = 0.05;
    float struck = 0.0;
    float grazed = 0.0;
    float last = 1e3;
    float part = 0.0;
    float3 on = float3(0.0, 0.0, 0.0);
    int steps = qualitySteps(120);
    [loop]
    for (int i = 0; i < steps; i++)
    {
        float d = swLand(eye + ray * t, part, on);
        if (d < 0.001 * t)
        {
            struck = 1.0;
            break;
        }

        // A ray that comes this close to an edge and moves away again draws the land's outline.
        if (d > last && last < 0.005 * t)
            grazed = 1.0;

        last = d;
        t += d;
        if (t > 34.0)
            break;
    }

    // The sky changes every eight beats: rose dusk, green dawn, and orange noon.
    float phrase = beats / 8.0;
    float sort = wrap(floor(phrase), 3.0);
    float3 low = sort < 0.5 ? float3(1.0, 0.3, 0.55) : sort < 1.5 ? float3(0.25, 0.85, 0.6) : float3(1.15, 0.6, 0.15);
    float3 high = sort < 0.5 ? float3(0.14, 0.05, 0.36) : sort < 1.5 ? float3(0.03, 0.1, 0.3) : float3(0.3, 0.05, 0.3);

    // The sun the valley runs toward: a flower of light whose petals turn, swelling on every beat, with twelve
    // beams round it, each as bright as its own part of the music.
    float3 sunward = normalize(float3(swWander(gone + 40.0) - eye.x, 10.0, 40.0));
    float3 side = normalize(float3(sunward.z, 0.0, -sunward.x));
    float3 up = cross(sunward, side);
    float2 inSky = float2(dot(ray, side), dot(ray, up)) / max(dot(ray, sunward), 0.05);
    float fromSun = length(inSky);
    float round = atan2(inSky.y, inSky.x) + Time * 0.08;
    float3 sky = lerp(low, high, smoothstep(-0.1, 0.5, ray.y));
    float whichBeam = floor(round / 0.5236 + 0.5);
    float inBeam = abs(round - whichBeam * 0.5236) * fromSun;
    float beamLoud = swLoud(0.08 + 0.06 * wrap(whichBeam, 12.0));
    sky += float3(1.3, 0.85, 0.4) * (0.1 + 0.6 * beamLoud + 0.2 * swell) * smoothstep(0.035 + 0.05 * fromSun, 0.0, inBeam) * exp(-fromSun * 1.6);
    float petals = 0.2 * (1.0 + 0.07 * cos(beatTurn) + 0.1 * swell) * (1.0 + (0.17 + 0.08 * swell) * cos(round * 9.0 - Time * 0.7));
    float sunEdge = fromSun - petals;
    sky = lerp(sky, float3(0.0, 0.0, 0.0), smoothstep(0.014, 0.006, abs(sunEdge)));
    float3 sunFace = lerp(float3(1.7, 1.5, 0.6), float3(1.75, 0.8, 0.15), smoothstep(0.0, 0.2, fromSun));
    sunFace = lerp(sunFace, float3(1.8, 1.7, 1.3), smoothstep(0.03, 0.0, abs(fromSun - 0.07 - 0.13 * since)) * flash);
    sky = lerp(sky, sunFace * (0.85 + 0.3 * swell), smoothstep(0.0, -0.006, sunEdge));

    float3 colour = sky;
    float haze = smoothstep(14.0, 34.0, t);
    if (struck > 0.5)
    {
        float3 at = eye + ray * t;
        float2 h = float2(0.0015 * t, 0.0);
        float3 dx = float3(swLandOnly(at + h.xyy), swLandOnly(at + h.yxy), swLandOnly(at + h.yyx));
        float3 dn = float3(swLandOnly(at - h.xyy), swLandOnly(at - h.yxy), swLandOnly(at - h.yyx));
        float3 n = normalize(dx - dn);
        float bent = abs(dx.x + dx.y + dx.z + dn.x + dn.y + dn.z - 6.0 * swLandOnly(at)) / h.x;
        float lit = smoothstep(-0.15, 0.25, dot(n, normalize(float3(0.25, 0.75, 0.6))));

        // Every beat the land's colours move one storey down, from the great rings to the least.
        float3 paint;
        float loud;
        if (part > -0.5)
        {
            loud = swLoud(0.08 + 0.15 * part);
            paint = swPaint(0.17 * part + 0.2 * atan2(on.y, on.x) / 3.1416 - 0.085 * beats);
        }
        else if (part > -1.5)
        {
            // The river: bands of water coming down toward the eye, one with every beat.
            loud = swLoud(0.1);
            float flow = frac((at.z - gone) * 0.45 + 0.6 * abs(on.x) * abs(on.x) + BeatPhase + Time * 0.35);
            paint = lerp(float3(0.05, 0.3, 0.75), float3(0.1, 0.75, 0.95), step(0.5, flow));
            paint = lerp(paint, float3(1.2, 1.25, 1.2), step(0.93, flow));

            // The sun lies along it in a broken streak.
            float streak = smoothstep(0.5, 0.0, abs(on.x + (eye.x - swWander(gone)) * 0.3)) * step(0.35, frac(flow * 3.0 + 0.2));
            paint = lerp(paint, float3(1.7, 1.2, 0.4), 0.75 * streak * smoothstep(1.5, 7.0, t));
        }
        else
        {
            float inside = step(0.25, frac(-part));
            float which = -2.0 - floor(part + 0.75);
            loud = swLoud(0.2 + 0.2 * which);
            paint = swPaint(0.6 + 0.14 * which + 0.045 * floor(at.z / (which < 0.5 ? 0.714 : which < 1.5 ? 0.816 : 1.19)) - 0.085 * beats);
            paint = lerp(paint, float3(0.03, 0.02, 0.07) + 0.25 * paint, inside);
        }

        // Each beat sends a flash of light up the valley.
        float wash = flash * smoothstep(3.0, 0.0, abs(t - 2.0 - 26.0 * since));
        colour = paint * (0.5 + 0.3 * lit + (part > -0.5 ? 0.42 : 0.16) * loud) + float3(1.0, 0.9, 0.7) * 0.3 * wash;
        colour = lerp(colour, float3(0.0, 0.0, 0.0), smoothstep(0.3, 0.7, bent));
        colour = lerp(colour, sky, haze);
    }

    colour = lerp(colour, float3(0.0, 0.0, 0.0), grazed * (1.0 - haze));
    return colour * (1.0 - 0.3 * dot(p, p) / (1.0 + Aspect * Aspect));
}
