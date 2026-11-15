// Name: Voxel Globe
// A ball built of little cubes, coloured by where each one sits: red to the east, green to the north, blue
// towards you, like a colour chart rolled into a planet. It turns on a grey studio floor. The bass swells its
// south, the mids its waist, and the treble its north, so cubes pop in and out of its skin as the music moves.
// Family: Ray traced
// Tags: 3D, Colourful, Geometric
// Added: 2026-11-15 20:57

static const float Cube = 0.15;

// Whether the cube numbered `cell` is part of the globe.
bool vgSolid(float3 cell)
{
    float3 centre = (cell + 0.5) * Cube;
    float r = length(centre);
    float3 way = centre / max(r, 1e-4);

    // South listens to the bass, the waist to the mids, north to the treble.
    float south = saturate(-way.y);
    float north = saturate(way.y);
    float heard = Bass * south * south + Treble * north * north + Mid * (1.0 - south * south - north * north);
    float skin = 0.95 + 0.42 * heard + 0.07 * BeatSwell + 0.12 * DropPulse;
    skin += 0.07 * sin(way.x * 5.0 + Time * 0.9) * sin(way.z * 4.0 - Time * 0.7 + way.y * 3.0);
    return r < skin;
}

float3 Render(float2 uv, float2 p)
{
    // The studio: a grey sweep, with the globe's shadow on the floor.
    float3 colour = float3(0.62, 0.63, 0.66) * (1.05 - 0.4 * uv.y) * (1.0 - dot(uv - 0.5, uv - 0.5) * 0.9);
    float2 floorAt = (p - float2(0.1, 0.9)) * float2(0.55, 3.2);
    colour *= 1.0 - 0.55 * exp(-dot(floorAt, floorAt) * 2.2);

    // The camera looks at the globe; the globe turns, so turn the ray the other way instead.
    float spin = Time * 0.22 + BeatGlide * 0.12;
    float3 eye = float3(0.0, 0.0, -4.6);
    float3 ray = normalize(float3(p.x, -p.y, 2.75));
    eye.yz = rotate(eye.yz, 0.38);
    ray.yz = rotate(ray.yz, 0.38);
    eye.xz = rotate(eye.xz, spin);
    ray.xz = rotate(ray.xz, spin);
    float3 lamp = normalize(float3(-0.55, 0.75, -0.4));
    lamp.xz = rotate(lamp.xz, spin);

    // Skip to where the ray enters the ball that holds the whole globe.
    float reach = 1.75;
    float b = dot(eye, ray);
    float h = b * b - dot(eye, eye) + reach * reach;
    if (h <= 0.0) return colour;
    h = sqrt(h);
    float t = max(-b - h, 0.0);
    float leave = -b + h;

    // Walk from cube to cube along the ray.
    ray = float3(abs(ray.x) < 1e-4 ? 1e-4 : ray.x, abs(ray.y) < 1e-4 ? 1e-4 : ray.y, abs(ray.z) < 1e-4 ? 1e-4 : ray.z);
    float3 start = eye + ray * (t + 0.001);
    float3 cell = floor(start / Cube);
    float3 way = sign(ray);
    float3 perCell = Cube / abs(ray);
    float3 next = ((cell + max(way, 0.0)) * Cube - start) / ray;
    float3 face = float3(0.0, 0.0, 0.0);
    float gone = 0.0;
    bool hit = false;

    [loop]
    for (int i = 0; i < 64; i++)
    {
        if (vgSolid(cell))
        {
            hit = true;
            break;
        }

        face = step(next.xyz, next.yzx) * step(next.xyz, next.zxy);
        gone = dot(next, face);
        next += face * perCell;
        cell += face * way;
        if (t + gone > leave) break;
    }

    if (!hit) return colour;

    float3 at = start + ray * gone;
    float3 n = dot(face, face) > 0.5 ? -face * way : normalize(start);
    float3 centre = (cell + 0.5) * Cube;

    // Colour by place: east red, up green, near blue.
    float3 chart = saturate(centre / 1.25 * 0.5 + 0.5);
    float3 paint = 0.06 + 1.45 * chart * chart;

    // A dark seam round each face shows the cubes apart.
    float3 within = abs((at - centre) / Cube);
    float3 flat = within * (1.0 - abs(n));
    float seam = smoothstep(0.5, 0.42, max(flat.x, max(flat.y, flat.z)));

    float lit = 0.32 + 0.78 * saturate(dot(n, lamp)) + 0.12 * saturate(n.y);
    float3 globe = paint * lit * (0.55 + 0.45 * seam);
    globe += 0.12 * pow(saturate(dot(reflect(ray, n), lamp)), 20.0);
    return globe * (0.92 + 0.1 * Level);
}
