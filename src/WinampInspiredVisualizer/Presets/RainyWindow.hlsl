// Name: Rainy Window
// Look out through a rain-streaked window at a neon city blurred into soft circles of light; raindrops cling to the glass and run down it, merging as they go, each one a tiny lens holding a sharp upside-down glimpse of the street; the city lights pulse with the music.
// Family: Ray traced
// Tags: Water, Neon, Calm

// The glass carries a height map of water: small drops that bead up and slowly evaporate, and
// bigger drops that slide down in wavering paths, leaving trails of droplets and clearing the
// mist as they go. For each pixel we take the water's surface normal, bend the view ray through
// it as real water would (index 1.33), and follow it out to the city. The city is drawn out of
// focus, the way a camera focused on the glass sees it; through a drop it comes back into focus.

// ---- Random numbers ----

float3 random3(float p)
{
    float3 q = frac(float3(p, p, p) * float3(0.1031, 0.11369, 0.13787));
    q += dot(q, q.yzx + 19.19);
    return frac(float3((q.x + q.y) * q.z, (q.x + q.z) * q.y, (q.y + q.z) * q.x));
}

// Rises quickly to 1 at b, then falls slowly back to 0 at 1.
float sawtooth(float b, float t)
{
    return smoothstep(0.0, b, t) * (1.0 - smoothstep(b, 1.0, t));
}

// ---- Water on the glass ----

// Small beads of water that form and slowly evaporate.
float staticDrops(float2 uv, float t)
{
    uv *= 40.0;
    float2 id = floor(uv);
    uv = frac(uv) - 0.5;
    float3 n = random3(id.x * 107.45 + id.y * 3543.654);
    float2 spot = (n.xy - 0.5) * 0.7;
    float d = length(uv - spot);
    float fade = sawtooth(0.025, frac(t + n.z));
    return (1.0 - smoothstep(0.0, 0.3, d)) * frac(n.z * 10.0) * fade;
}

// Bigger drops sliding down the pane in columns, each wavering a little from side to side and
// leaving a trail of droplets behind it. Returns the water (x) and the cleared trail (y).
float2 runningDrops(float2 uv, float t)
{
    float2 original = uv;
    uv.y += t * 0.75;
    float2 shape = float2(6.0, 1.0);
    float2 grid = shape * 2.0;
    float2 id = floor(uv * grid);
    uv.y += random3(id.x).x;
    id = floor(uv * grid);
    float3 n = random3(id.x * 35.2 + id.y * 2376.1);
    float2 st = frac(uv * grid) - float2(0.5, 0.0);

    float x = n.x - 0.5;
    float y = original.y * 20.0;
    float wiggle = sin(y + sin(y));
    x += wiggle * (0.5 - abs(x)) * (n.z - 0.5);
    x *= 0.7;
    float ti = frac(t + n.z);
    y = (sawtooth(0.85, ti) - 0.5) * 0.9 + 0.5;
    float2 dropAt = float2(x, y);

    float d = length((st - dropAt) * shape.yx);
    float mainDrop = 1.0 - smoothstep(0.0, 0.4, d);

    float r = sqrt(1.0 - smoothstep(y, 1.0, st.y));
    float across = abs(st.x - x);
    float trail = 1.0 - smoothstep(0.15 * r * r, 0.23 * r, across);
    float trailFront = smoothstep(-0.02, 0.02, st.y - y);
    trail *= trailFront * r * r;

    y = frac(original.y * 10.0) + (st.y - 0.5);
    float droplets = 1.0 - smoothstep(0.0, 0.3, length(st - float2(x, y)));
    float water = mainDrop + droplets * r * trailFront;
    return float2(water, trail);
}

float2 waterOnGlass(float2 uv, float t, float beads, float big, float small)
{
    float s = staticDrops(uv, t) * beads;
    float2 m1 = runningDrops(uv, t) * big;
    float2 m2 = runningDrops(uv * 1.85, t) * small;
    // Adding the drops before thresholding makes neighbours bulge together and merge.
    float c = smoothstep(0.3, 1.0, s + m1.x + m2.x);
    return float2(c, max(m1.y * beads, m2.y * big));
}

// ---- The city ----

float3 neonColour(float pick)
{
    float3 colour = float3(1.0, 0.25, 0.6);
    colour = pick > 0.2 ? float3(0.2, 0.85, 1.0) : colour;
    colour = pick > 0.4 ? float3(1.0, 0.62, 0.25) : colour;
    colour = pick > 0.62 ? float3(1.0, 0.92, 0.8) : colour;
    colour = pick > 0.8 ? float3(0.65, 0.35, 1.0) : colour;
    colour = pick > 0.92 ? float3(1.0, 0.18, 0.12) : colour;
    return colour;
}

// Out-of-focus points of light: each spreads into a soft disc with a slightly brighter rim,
// bigger and dimmer the blurrier the view. Each light pulses with its own part of the spectrum.
float3 bokehLayer(float2 q, float cellSize, float blur, float seed, float top, float bottom)
{
    float2 cell = floor(q / cellSize);
    float3 total = 0.0;
    [unroll]
    for (int i = 0; i < 9; i++)
    {
        float2 c = cell + float2(i % 3 - 1, i / 3 - 1);
        float3 h = random3(dot(c, float2(17.13, 91.7)) + seed);
        float2 centre = (c + 0.5 + (h.xy - 0.5) * 0.8) * cellSize;
        float present = step(0.7, h.z) * smoothstep(top, top + 0.15, centre.y) * (1.0 - smoothstep(bottom - 0.1, bottom, centre.y));
        float radius = cellSize * (0.05 + blur * lerp(0.35, 0.75, frac(h.y * 5.1)));
        float d = length(q - centre);
        float disc = 1.0 - smoothstep(radius * (1.0 - 0.25 * blur) - 0.002, radius, d);
        float rim = 0.8 + 0.35 * smoothstep(radius * 0.4, radius, d);
        float energy = 1.0 / (1.0 + blur * blur * 6.0);
        float band = frac(h.x * 7.3 + h.y * 3.1);
        float pulse = 0.45 + spectrum(band * 0.85 + 0.03) * 1.6;
        total += neonColour(frac(h.x * 13.7)) * disc * rim * energy * pulse * present * (0.6 + h.z);
    }
    return total;
}

// Tower blocks against the glowing night sky, their windows lit here and there.
float3 skyline(float2 q, float blur, float layer)
{
    float width = layer < 0.5 ? 0.22 : 0.33;
    float column = floor(q.x / width);
    float3 h = random3(column * 3.17 + layer * 51.0);
    float roof = lerp(-0.1, 0.45, h.x) - layer * 0.2;
    float soft = 0.004 + blur * 0.05;
    float inside = smoothstep(roof - soft, roof + soft, q.y);
    float2 windowCell = float2(0.016, 0.022);
    float2 w = frac(q / windowCell) - 0.5;
    float2 windowId = floor(q / windowCell);
    float lit = step(0.62, hash(windowId + column * 7.0 + layer * 3.0));
    float sharpWindow = (1.0 - smoothstep(0.25, 0.4, abs(w.x))) * (1.0 - smoothstep(0.2, 0.35, abs(w.y))) * lit;
    // Blurred, the windows melt into an even glow.
    float melt = smoothstep(0.0, 0.22, blur);
    float windows = lerp(sharpWindow, 0.08, melt);
    float3 windowColour = lerp(float3(1.0, 0.75, 0.45), float3(0.6, 0.85, 1.0), step(0.75, hash(windowId * 1.3 + 2.0)));
    windowColour = lerp(windowColour, float3(0.9, 0.75, 0.6), melt);
    // Towers glow a little brighter low down, lit by the street.
    float3 wall = float3(0.006, 0.006, 0.014) * (1.0 + layer) + float3(0.05, 0.02, 0.03) * smoothstep(0.2, 1.0, q.y);
    return inside * (wall + windowColour * windows * 0.7 * (0.8 + Mid * 0.4));
}

float3 city(float2 q, float blur)
{
    // A low night sky lit violet and amber by the streets below.
    float3 colour = lerp(float3(0.03, 0.02, 0.06), float3(0.16, 0.06, 0.12), smoothstep(-1.0, 0.4, q.y));
    colour += float3(0.2, 0.08, 0.02) * smoothstep(0.0, 0.6, q.y) * (0.7 + Bass * 0.4);

    float3 farTowers = skyline(q + float2(Time * 0.002, 0.0), blur, 0.0);
    float3 nearTowers = skyline(q * 0.8 + float2(3.0, 0.1), blur, 1.0);
    float farCover = step(1e-4, dot(farTowers, 1.0));
    float nearCover = step(1e-4, dot(nearTowers, 1.0));
    colour = lerp(colour, farTowers, farCover);
    colour = lerp(colour, nearTowers, nearCover);

    // Shop signs, street lamps and distant lights.
    colour += bokehLayer(q, 0.13, blur, 1.0, -0.35, 0.9) * 0.9;
    colour += bokehLayer(q + 7.0, 0.26, blur, 2.0, 0.05, 1.0) * 1.1;

    // Traffic on the street below: white headlights one way, red tail lights the other.
    float2 lane = q - float2(Time * 0.12, 0.0);
    float3 headlights = bokehLayer(float2(lane.x, q.y), 0.09, blur, 5.0, 0.62, 0.78);
    float2 back = q + float2(Time * 0.09, 0.0);
    float3 tail = bokehLayer(float2(back.x, q.y), 0.08, blur, 9.0, 0.74, 0.9);
    colour += dot(headlights, 0.33) * float3(1.0, 0.95, 0.85) * 0.8 + dot(tail, 0.33) * float3(1.0, 0.1, 0.08) * 0.9;

    // Two big neon signs, flickering softly and swelling with the bass.
    float2 sign1 = q - float2(-0.75, -0.05);
    float tube1 = abs(length(max(abs(sign1) - float2(0.03, 0.22), 0.0)) - 0.012);
    float2 sign2 = q - float2(0.95, 0.12);
    float tube2 = abs(length(max(abs(sign2) - float2(0.18, 0.035), 0.0)) - 0.01);
    float spread = 0.004 + blur * 0.08;
    float flicker = 0.85 + 0.15 * step(0.08, frac(sin(floor(Time * 9.0)) * 4375.85));
    colour += float3(1.0, 0.2, 0.55) * exp(-tube1 / spread) * (0.8 + Bass * 1.0) * flicker * 0.9;
    colour += float3(0.2, 0.9, 1.0) * exp(-tube2 / spread) * (0.8 + spectrum(0.5) * 1.4) * 0.9;
    return colour;
}

float3 Render(float2 uv, float2 p)
{
    float t = Time * 0.2;
    // How hard it's raining drifts slowly.
    float rain = 0.75 + 0.2 * sin(Time * 0.05);
    float beads = smoothstep(-0.5, 1.0, rain) * 2.0;
    float big = smoothstep(0.25, 0.75, rain);
    float small = smoothstep(0.0, 0.5, rain);

    // Water coordinates: y up, so drops fall down the screen.
    float2 q = float2(p.x, -p.y) * 0.3;
    float2 water = waterOnGlass(q, t, beads, big, small);
    float e = 0.0012;
    float waterX = waterOnGlass(q + float2(e, 0.0), t, beads, big, small).x;
    float waterY = waterOnGlass(q + float2(0.0, e), t, beads, big, small).x;
    float2 slope = float2(waterX - water.x, waterY - water.x) / e;

    // Bend the view ray through the water's surface and follow it out to the street.
    // Each drop is a little lens: its curved surface tips the ray outwards more the further from
    // its middle, so through it you see a wide, upside-down view of the street.
    float3 normal = normalize(float3(-slope.x * 0.02, slope.y * 0.02, 1.0));
    float3 view = float3(0.0, 0.0, -1.0);
    float3 bent = refract(view, normal, 1.0 / 1.33);
    float2 shift = bent.xy / max(-bent.z, 0.2) * 2.2;
    shift *= min(1.0, 0.35 / max(length(shift), 1e-4));

    // Bare glass is misted and the city is soft; drops and their trails are clear and sharp.
    float inDrop = smoothstep(0.1, 0.25, water.x);
    float cleared = saturate(water.y);
    float blur = lerp(lerp(0.8, 0.5, cleared), 0.2, inDrop);
    float2 look = p + shift * inDrop;
    float3 colour = city(look, blur) * (1.0 + inDrop * 0.8);

    // Mist on the glass lifts the blacks a little and softens the colour, except where cleared.
    float mist = (1.0 - cleared) * (1.0 - inDrop);
    colour = lerp(colour, colour * 0.85 + float3(0.03, 0.025, 0.04), mist * 0.6);

    // The drops' rims catch the room light behind us, and steep edges go dark as light bends away.
    float steep = length(slope) * 0.02;
    float3 toLamp = normalize(float3(-0.5, 0.6, 1.0));
    float shine = pow(saturate(dot(reflect(view, normal), toLamp)), 60.0);
    colour += float3(1.0, 0.85, 0.65) * shine * inDrop * 0.8;
    colour *= 1.0 - saturate(steep * 0.3) * 0.25 * inDrop;
    return colour;
}
