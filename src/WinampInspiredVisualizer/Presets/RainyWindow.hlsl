// Name: Rainy Window
// Look out through a misted, rain-streaked window at a neon city melted into big soft circles of light; raindrops cling to the glass and run down it, each one a tiny lens holding a sharp upside-down glimpse of the street; the city lights and signs pulse with the music.
// Family: Ray traced
// Tags: Water, Neon, Calm
// Added: 2026-10-17 17:15

// The camera is focused on the glass, so the city beyond is far out of focus: every light spreads
// into a soft disc (a slightly hexagonal one, the shape of a camera's aperture) and the neon signs
// into blurred but still readable shapes. Each raindrop is a little dome of water. A ray through
// it is bent more the further from its middle it passes, so the drop shows a sharp, shrunken,
// upside-down picture of the street, ringed by a dark edge where the light bends away and lit by
// a bright highlight. Running drops sweep the mist and the small beads off the glass as they go.

// ---- Random numbers ----

float3 random3(float p)
{
    float3 q = frac(float3(p, p, p) * float3(0.1031, 0.11369, 0.13787));
    q += dot(q, q.yzx + 19.19);
    return frac(float3((q.x + q.y) * q.z, (q.x + q.z) * q.y, (q.y + q.z) * q.x));
}

// ---- The city ----

float3 neonColour(float pick)
{
    float3 colour = float3(1.0, 0.22, 0.55);
    colour = pick > 0.22 ? float3(0.2, 0.8, 1.0) : colour;
    colour = pick > 0.42 ? float3(1.0, 0.6, 0.22) : colour;
    colour = pick > 0.66 ? float3(1.0, 0.88, 0.7) : colour;
    colour = pick > 0.84 ? float3(0.6, 0.35, 1.0) : colour;
    colour = pick > 0.94 ? float3(1.0, 0.15, 0.1) : colour;
    return colour;
}

// Out-of-focus points of light. blur 0 is sharp, 1 is the full blur of the misted view; footprint
// is how much city one pixel covers, so lights never shrink below a pixel and sparkle.
// Each light spreads into a soft, faintly hexagonal disc with a brighter rim, dimmer the bigger
// it grows, and pulses with its own part of the spectrum.
float3 bokehLayer(float2 q, float cellSize, float blur, float footprint, float seed, float density,
                  float top, float bottom, float sharpRadius, float blurRadius, float brightness)
{
    float2 cell = floor(q / cellSize);
    float3 total = 0.0;
    [unroll]
    for (int i = 0; i < 9; i++)
    {
        float2 c = cell + float2(i % 3 - 1, i / 3 - 1);
        float3 h = random3(dot(c, float2(17.13, 91.7)) + seed);
        float2 centre = (c + 0.5 + (h.xy - 0.5) * 0.9) * cellSize;
        float present = step(1.0 - density, h.z) * smoothstep(top, top + 0.15, centre.y) * (1.0 - smoothstep(bottom - 0.15, bottom, centre.y));
        float radius = lerp(sharpRadius, blurRadius * lerp(0.55, 1.0, frac(h.x * 7.7)), blur);
        radius = max(radius, footprint * 1.2);
        float2 d = q - centre;
        float hexagon = polygon(rotate(d, 0.26), 6.0, radius * 0.95);
        float shape = lerp(length(d) - radius, hexagon, 0.55);
        float soft = max(radius * lerp(0.05, 0.2, blur), footprint);
        float disc = 1.0 - smoothstep(-soft, soft, shape);
        float rim = 0.75 + 0.45 * smoothstep(-radius * 0.45, 0.0, shape);
        float energy = min(1.0, pow(0.03 / radius, 0.7));
        float band = frac(h.x * 7.3 + h.y * 3.1);
        float pulse = 0.5 + spectrum(band * 0.85 + 0.03) * 1.5;
        total += neonColour(frac(h.y * 13.7 + h.x * 3.3)) * disc * rim * energy * pulse * present * (0.5 + h.z);
    }
    return total * brightness;
}

// A neon tube glowing at distance d from it: sharp and bright in focus, a wide soft halo out of it.
float tube(float d, float spread)
{
    return exp(-max(d, 0.0) / spread) * sqrt(0.006 / (spread + 0.003));
}

float segment(float2 q, float2 a, float2 b)
{
    float2 qa = q - a;
    float2 ba = b - a;
    float h = saturate(dot(qa, ba) / dot(ba, ba));
    return length(qa - ba * h);
}

// The signs: a pink ring over a bar, a tall cyan sign with stacked letters, an amber arrow
// pointing down the street and a green chemist's cross. Each swells with its own part of the music.
float3 signs(float2 q, float blur, float footprint)
{
    float spread = max(0.003 + blur * 0.04, footprint);
    float flicker = 0.88 + 0.12 * step(0.1, frac(sin(floor(Time * 11.0)) * 4375.85));

    float ring = abs(length(q - float2(-1.05, -0.32)) - 0.13);
    float3 colour = float3(1.0, 0.2, 0.55) * tube(ring, spread) * (1.2 + Bass * 1.6) * flicker;

    float2 v = q - float2(0.62, -0.28);
    float box = abs(length(max(abs(v) - float2(0.045, 0.24), 0.0)) - 0.012);
    float letters = 1e3;
    [unroll]
    for (int k = 0; k < 4; k++)
    {
        float y = -0.18 + k * 0.12;
        float w = 0.018 + 0.012 * frac(k * 0.37 + 0.2);
        letters = min(letters, segment(v, float2(-w, y), float2(w, y)));
        letters = min(letters, segment(v, float2(0.0, y - 0.035), float2(0.0, y + 0.035)));
    }
    colour += float3(0.2, 0.85, 1.0) * (tube(box, spread) + tube(letters, spread) * 0.8) * (1.0 + spectrum(0.45) * 1.8);

    float2 a = q - float2(1.32, 0.1);
    float arrow = min(segment(a, float2(-0.16, 0.0), float2(0.12, 0.0)), min(segment(a, float2(0.12, 0.0), float2(0.04, -0.07)), segment(a, float2(0.12, 0.0), float2(0.04, 0.07))));
    float chase = 0.6 + 0.4 * step(0.5, frac(a.x * 6.0 - Time * 1.5));
    colour += float3(1.0, 0.55, 0.15) * tube(arrow, spread) * chase * (1.0 + spectrum(0.2) * 1.5);

    float2 x = q - float2(-0.32, 0.12);
    float chemist = abs(min(length(max(abs(x) - float2(0.085, 0.026), 0.0)), length(max(abs(x) - float2(0.026, 0.085), 0.0))) - 0.004);
    colour += float3(0.25, 1.0, 0.45) * tube(chemist, spread) * (0.8 + Treble * 1.5);
    return colour;
}

// Dark towers against the glowing sky, their lit windows melted into a faint warm glow.
float3 towers(float2 q, float blur, float footprint, float layer)
{
    float width = lerp(0.3, 0.45, layer);
    float column = floor(q.x / width);
    float3 h = random3(column * 3.17 + layer * 51.0);
    float roof = lerp(-0.75, -0.05, h.x) + layer * 0.25;
    float soft = 0.02 + blur * 0.22 + footprint;
    float inside = smoothstep(roof - soft, roof + soft, q.y);
    // Blurred, the windows melt into an even glow; sharp, a few show as tiny lit squares.
    float2 windowId = floor(q / float2(0.025, 0.035));
    float2 w = frac(q / float2(0.025, 0.035)) - 0.5;
    float lit = step(0.65, hash(windowId + column * 7.0 + layer * 3.0));
    float sharpWindow = (1.0 - smoothstep(0.2, 0.35, abs(w.x))) * (1.0 - smoothstep(0.2, 0.35, abs(w.y))) * lit;
    float melt = smoothstep(0.0, 0.25, blur + footprint * 8.0);
    float windows = lerp(sharpWindow, 0.12, melt);
    float3 wall = float3(0.008, 0.008, 0.016) + float3(0.04, 0.015, 0.03) * smoothstep(-0.2, 0.8, q.y);
    return inside * (wall + float3(1.0, 0.7, 0.4) * windows * 0.25 * (0.8 + Mid * 0.4));
}

float3 city(float2 q, float blur, float footprint)
{
    // A low night sky lit violet and amber by the streets below.
    float3 colour = lerp(float3(0.015, 0.012, 0.04), float3(0.12, 0.04, 0.09), smoothstep(-1.1, 0.4, q.y));
    colour += float3(0.18, 0.07, 0.02) * smoothstep(0.0, 0.7, q.y) * (0.7 + Bass * 0.4);

    float3 far = towers(q * 1.3 + float2(Time * 0.002, -0.05), blur, footprint, 0.0);
    float3 near = towers(q + float2(7.0, 0.0), blur, footprint, 1.0);
    colour = lerp(colour, far, saturate(dot(far, 30.0)) * 0.8);
    colour = lerp(colour, near, saturate(dot(near, 30.0)));

    // Distant lights: windows, lamps and the glow of far streets.
    colour += bokehLayer(q, 0.16, blur, footprint, 1.0, 0.16, -0.5, 0.8, 0.005, 0.05, 0.4);
    // Nearer, bigger lights in neon colours.
    colour += bokehLayer(q + float2(3.7, 0.0), 0.42, blur, footprint, 2.0, 0.4, -0.8, 1.2, 0.008, 0.21, 1.1);
    // Traffic on the street: white headlights one way, red tail lights the other.
    float3 heads = bokehLayer(q - float2(Time * 0.1, 0.0), 0.16, blur, footprint, 5.0, 0.3, 0.4, 0.62, 0.006, 0.06, 1.0);
    float3 tails = bokehLayer(q + float2(Time * 0.08, 0.0), 0.15, blur, footprint, 9.0, 0.3, 0.55, 0.78, 0.006, 0.06, 1.0);
    colour += dot(heads, 0.33) * float3(1.0, 0.94, 0.85) + dot(tails, 0.33) * float3(1.0, 0.1, 0.06);
    colour += signs(q, blur, footprint);
    // The wet street shines with all of it.
    colour += float3(0.12, 0.05, 0.08) * smoothstep(0.55, 1.0, q.y) * (0.6 + Level * 0.6);
    return colour;
}

// ---- Water on the glass ----

// The drop nearest the viewer at this spot: its centre, radius and where we are inside it (in
// units of its radius). Drops further from the glass's surface win, so neighbours overlap cleanly.
struct Drop
{
    float2 centre;
    float radius;
    float2 local;
    float height;
};

void consider(inout Drop best, float2 p, float2 centre, float radius, float stretch)
{
    float2 d = (p - centre) / max(radius, 1e-4);
    // Running drops sag into a teardrop: rounder below, drawn out above.
    d.y /= d.y < 0.0 ? stretch : 1.0;
    float height = radius * sqrt(saturate(1.0 - dot(d, d)));
    if (height > best.height && radius > 1e-4 && dot(d, d) < 1.0)
    {
        best.centre = centre;
        best.radius = radius;
        best.local = d;
        best.height = height;
    }
}

// Where a running drop's path crosses height y: each column wavers a little from side to side.
float pathX(float column, float width, float3 h, float y)
{
    return (column + 0.5) * width + (h.x - 0.5) * width * 0.35 + sin(y * 5.0 + h.y * 20.0) * 0.012 + sin(y * 13.0 + h.z * 9.0) * 0.005;
}

// Big drops sliding down the pane in jerky starts and stops, leaving a cleared trail dotted with
// tiny droplets. Returns how clear the glass is here.
float runningDrops(inout Drop best, float2 p, float rain)
{
    float width = 0.3;
    float column = floor(p.x / width);
    float3 h = random3(column * 31.7 + 4.0);
    float active = step(0.4, h.z) * step(h.y, rain);
    float radius = lerp(0.05, 0.072, h.x);
    float s = Time * lerp(0.45, 0.8, h.y) + h.z * 17.0;
    float progress = floor(s) + smoothstep(0.15, 1.0, frac(s));
    float y = -1.3 + wrap(progress * 0.3 + h.x * 4.0, 2.9);
    float2 centre = float2(pathX(column, width, h, y), y);
    consider(best, p, centre, radius * active, 1.6);

    // The trail it has wiped clean, narrowing and slowly misting over again further back.
    float behind = y - p.y;
    float fade = exp(-behind / 0.9) * step(0.0, behind) * step(-1.3, p.y);
    float trail = 1.0 - smoothstep(radius * 0.45, radius * 0.85, abs(p.x - pathX(column, width, h, p.y)));
    float cleared = trail * fade * active;

    // Droplets left behind along the trail.
    float spacing = 0.05;
    float k = floor(p.y / spacing);
    float3 g = random3(k * 7.31 + column * 113.0);
    float dy = (k + 0.5 + (g.x - 0.5) * 0.5) * spacing;
    float2 bead = float2(pathX(column, width, h, dy) + (g.y - 0.5) * radius * 0.6, dy);
    float beadAge = y - dy;
    float beadRadius = radius * lerp(0.18, 0.34, g.z) * step(0.55, g.x) * step(radius * 1.8, beadAge) * saturate(1.3 - beadAge * 0.8) * active;
    consider(best, p, bead, beadRadius, 1.0);
    return saturate(cleared);
}

// Beads of water that form on the glass and slowly evaporate; a running drop sweeps them away.
void beads(inout Drop best, float2 p, float cellSize, float seed, float density, float minRadius, float maxRadius, float cleared)
{
    float2 id = floor(p / cellSize);
    float3 h = random3(dot(id, float2(12.9, 78.2)) + seed);
    float life = frac(Time * 0.03 + h.z * 7.0);
    float grow = smoothstep(0.0, 0.08, life) * sqrt(saturate((1.0 - life) * 1.3));
    float radius = lerp(minRadius, maxRadius, frac(h.x * 9.1)) * grow * step(1.0 - density, h.z) * (1.0 - step(0.15, cleared));
    float2 centre = (id + 0.5) * cellSize + (h.xy - 0.5) * (cellSize - maxRadius * 2.2);
    consider(best, p, centre, radius, 1.0);
}

float3 Render(float2 uv, float2 p)
{
    float px = pixelSize();
    float rain = 0.75 + 0.2 * sin(Time * 0.05);

    // ---- The water on the glass ----
    Drop best;
    best.centre = 0.0;
    best.radius = 0.0;
    best.local = 0.0;
    best.height = 0.0;
    float cleared = runningDrops(best, p, rain);
    beads(best, p, 0.2, 1.0, 0.6, 0.022, 0.06, cleared);
    beads(best, p + 0.37, 0.08, 2.0, 0.22, 0.009, 0.018, cleared);

    // ---- The city through bare glass: blurred, and hazed by the mist ----
    float3 colour = city(p, 1.0, px);
    // Mist gathers unevenly, thicker towards the edges of the pane.
    float vignette = dot(p / float2(Aspect, 1.0), p / float2(Aspect, 1.0));
    float mist = saturate(0.55 + (fbm(p * 2.5 + 3.0) - 0.5) * 0.7 + vignette * 0.25) * (1.0 - cleared);
    float brightness = dot(colour, float3(0.3, 0.5, 0.2));
    float3 haze = float3(0.07, 0.05, 0.085) * (0.7 + Level * 0.5) + colour * 0.35 + brightness * float3(0.25, 0.2, 0.25);
    // Fine condensation scatters a little light, giving the frosted look.
    haze *= 0.92 + 0.16 * noise(p * 90.0);
    colour = lerp(colour, haze, mist * 0.7);

    // ---- Through a drop: a sharp, upside-down picture of the street ----
    if (best.height > 0.0)
    {
        float2 d = best.local;
        float r2 = dot(d, d);
        float rim = sqrt(r2);
        float facing = sqrt(saturate(1.0 - r2));
        float3 normal = float3(d.x, d.y, facing);
        // The lens flips the view through its centre and squeezes a wide view into a small disc.
        float reach = 0.5;
        float2 look = best.centre - d * reach * (1.0 + 0.4 * r2) + float2(0.0, -0.15);
        float3 inside = city(look, 0.1, px * reach / best.radius);
        // Light bends away from the steep edge, leaving a dark ring.
        inside = (inside * 1.4 + colour * 0.45) * smoothstep(0.0, 0.6, facing);
        // Light gathered by the drop brightens the lower rim.
        inside += (colour * 1.2 + 0.02) * smoothstep(0.6, 0.95, rim) * saturate(d.y + 0.2) * 0.8;
        // A sharp glint from the lamp behind us, and a faint reflection along the top edge.
        float3 toLamp = normalize(float3(-0.45, -0.65, 0.6));
        float3 bounce = reflect(float3(0.0, 0.0, -1.0), normal);
        float glint = pow(saturate(dot(bounce, toLamp)), 180.0) * 4.0;
        float edgeShine = smoothstep(0.75, 0.95, rim) * (1.0 - smoothstep(0.95, 1.0, rim)) * saturate(-d.y * 0.8 - d.x * 0.3 + 0.1);
        inside += float3(1.0, 0.92, 0.85) * glint * (0.8 + Beat * 0.4) + float3(0.6, 0.5, 0.7) * edgeShine * 0.25;
        // Soften the outline over a pixel.
        float edge = 1.0 - smoothstep(1.0 - 1.5 * px / best.radius, 1.0, rim);
        colour = lerp(colour, inside, edge);
    }
    return colour;
}
