// Name: Twin Plumes
// Two jets of glowing smoke, one ember orange and one ice blue, blown at each other across the dark and curling
// together where they meet. The bass feeds the orange jet and the treble the blue, every beat puffs both, and the
// smoke hangs and swirls for a few seconds before it fades.
// Family: Organic
// Tags: Colourful, Fire, Dark
// Added: 2026-11-15 19:47

// The smoke is last frame's picture, carried along by a flow of slowly changing eddies and thinned a little,
// with fresh smoke added at the two nozzles. Each pixel asks where the flow would have brought its smoke from
// and takes last frame's colour there.

// Last frame's colour, blended between the four pixels nearest to uv.
float3 tpLast(float2 uv)
{
    float2 at = uv * Resolution - 0.5;
    float2 corner = floor(at);
    float2 f = at - corner;
    float2 texel = 1.0 / Resolution;
    float2 first = (corner + 0.5) * texel;
    float3 a = previous(first);
    float3 b = previous(first + float2(texel.x, 0.0));
    float3 c = previous(first + float2(0.0, texel.y));
    float3 d = previous(first + texel);
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// A height map whose contour lines the eddies follow.
float tpHills(float2 x)
{
    float broad = noise(x * 1.3 + float2(Time * 0.11, -Time * 0.07));
    float mid = noise(x * 2.9 + float2(-Time * 0.17, Time * 0.13) + 7.3);
    float fine = noise(x * 6.1 + float2(Time * 0.23, Time * 0.19) + 19.1);
    return broad * 0.5 + mid * 0.2 + fine * 0.07;
}

// Flowing along contour lines never piles smoke up or leaves holes.
float2 tpEddies(float2 x)
{
    float e = 0.02;
    float dx = tpHills(x + float2(e, 0.0)) - tpHills(x - float2(e, 0.0));
    float dy = tpHills(x + float2(0.0, e)) - tpHills(x - float2(0.0, e));
    return float2(dy, -dx) / (2.0 * e);
}

float3 Render(float2 uv, float2 p)
{
    float dt = min(DeltaTime, 0.04);
    float beatLight = SongMapped > 0.0 ? 0.35 + Beat * 0.65 : Beat;

    // The two nozzles circle the middle slowly, opposite each other, each aimed a little to one side of it.
    float round0 = Time * 0.07;
    float2 warmAt = float2(cos(round0) * Aspect * 0.55, sin(round0) * 0.6);
    float2 coolAt = -warmAt;
    float2 warmAim = normalize(rotate(-warmAt, 0.42));
    float2 coolAim = normalize(rotate(-coolAt, 0.42));
    float warmPower = 0.4 + Bass * 1.1 + BeatSwell * 0.5 + DropPulse * 0.8;
    float coolPower = 0.4 + Treble * 1.3 + Mid * 0.4 + BeatSwell * 0.3 + DropPulse * 0.8;

    float2 fromWarm = p - warmAt;
    float2 fromCool = p - coolAt;
    // Each jet is a narrow stream that widens and slows as it goes.
    float warmAlong = dot(fromWarm, warmAim);
    float coolAlong = dot(fromCool, coolAim);
    float warmAcross = dot(fromWarm, float2(-warmAim.y, warmAim.x));
    float coolAcross = dot(fromCool, float2(-coolAim.y, coolAim.x));
    float warmJet = exp(-warmAcross * warmAcross / (0.006 + 0.03 * max(warmAlong, 0.0))) * smoothstep(-0.05, 0.05, warmAlong) * exp(-max(warmAlong, 0.0) * 0.8);
    float coolJet = exp(-coolAcross * coolAcross / (0.006 + 0.03 * max(coolAlong, 0.0))) * smoothstep(-0.05, 0.05, coolAlong) * exp(-max(coolAlong, 0.0) * 0.8);

    float2 flow = tpEddies(p) * (0.26 + Level * 0.16);
    flow += warmAim * warmJet * warmPower * 1.5;
    flow += coolAim * coolJet * coolPower * 1.5;
    // A slow breath outwards, so the smoke spreads to the edges instead of knotting in the middle.
    flow += p * 0.035;

    float2 from = p - flow * dt;
    float3 smoke = tpLast(toUv(from));
    smoke *= exp(-dt * (0.3 + 0.4 * dot(uv - 0.5, uv - 0.5)));

    // Fresh smoke at each nozzle.
    float warmPuff = exp(-dot(fromWarm, fromWarm) * 160.0);
    float coolPuff = exp(-dot(fromCool, fromCool) * 160.0);
    float drift = 0.5 + 0.5 * sin(Time * 0.05);
    float3 ember = lerp(float3(1.0, 0.42, 0.08), float3(1.0, 0.25, 0.3), drift * 0.6);
    float3 ice = lerp(float3(0.1, 0.42, 1.0), float3(0.1, 0.8, 0.9), drift * 0.6);
    // Smoke at a nozzle turns towards that nozzle's colour instead of piling up, so it never burns out to white.
    smoke = lerp(smoke, ember * 1.25, saturate(warmPuff * warmPower * dt * 30.0));
    smoke = lerp(smoke, ice * 1.25, saturate(coolPuff * coolPower * dt * 30.0));
    // A drop lights the place where they meet.
    smoke += float3(1.0, 0.9, 0.95) * exp(-dot(p, p) * 30.0) * DropPulse * dt * 5.0;

    return min(smoke, 1.5);
}
