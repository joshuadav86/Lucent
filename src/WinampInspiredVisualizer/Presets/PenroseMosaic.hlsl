// Name: Penrose Mosaic
// An endless fivefold Penrose quasicrystal of faceted gem tiles laced with glowing curves: tiles flip into new
// arrangements on every beat, the spectrum lights each tile direction and ripples of light run through it.
// Family: Kaleidoscope
// Tags: Colourful, Geometric

static const float2 PM_E[5] = {
    float2(1.0, 0.0),
    float2(0.30901699, 0.95105652),
    float2(-0.80901699, 0.58778525),
    float2(-0.80901699, -0.58778525),
    float2(0.30901699, -0.95105652)
};
// Pairs of grid families, ordered by the rank of how close each family's nearest line is.
static const int PM_RA[10] = { 0, 0, 1, 0, 1, 2, 0, 1, 2, 3 };
static const int PM_RB[10] = { 1, 2, 2, 3, 3, 3, 4, 4, 4, 4 };

float pmStroke(float d, float halfWidth, float px)
{
    float w = max(halfWidth, px * 0.5);
    return saturate((w - abs(d)) / px + 0.5) * (halfWidth / w);
}

// Tests whether tiling point t lies in the rhomb made by line kr of family r crossing line ks of family s.
// On success returns true with the rhomb's local coordinates and corner position.
bool pmTryRhomb(float2 t, int r, int s, float kr, float ks, float gam[5], out float2 local, out float2 corner)
{
    float2 er = PM_E[r];
    float2 es = PM_E[s];
    float det = er.x * es.y - er.y * es.x;
    float c1 = kr - gam[r];
    float c2 = ks - gam[s];
    float2 X = float2(es.y * c1 - er.y * c2, -es.x * c1 + er.x * c2) / det;
    float2 base = float2(0.0, 0.0);
    [unroll]
    for (int j = 0; j < 5; j++)
    {
        float use = (j != r && j != s) ? 1.0 : 0.0;
        base += use * ceil(dot(X, PM_E[j]) + gam[j]) * PM_E[j];
    }
    corner = base + kr * er + ks * es;
    float2 w = t - corner;
    local = float2(es.y * w.x - es.x * w.y, -er.y * w.x + er.x * w.y) / det;
    return local.x >= 0.0 && local.x <= 1.0 && local.y >= 0.0 && local.y <= 1.0;
}

float3 Render(float2 uv, float2 p)
{
    float px = pixelSize();

    // Grid offsets step forward with each beat (summing to 1 keeps it a true Penrose tiling), so tiles
    // flip into new arrangements in the instant just after the beat.
    float snap = Beat * Beat;
    float drift = (BeatCount - snap * snap) * 0.022;
    float gam[5] = { 0.0, 0.0, 0.0, 0.0, 0.0 };
    float2 offset = float2(0.0, 0.0);
    [unroll]
    for (int i = 0; i < 5; i++)
    {
        gam[i] = 0.2 + 0.3 * sin(drift + i * 2.5132741) + 0.12 * sin(drift * 0.644 + i * 5.0265482);
        offset += gam[i] * PM_E[i];
    }

    float scale = 3.5 + 0.5 * sin(Time * 0.037) - Bass * 0.25;
    float2 t = rotate(p, Time * 0.02) * scale;
    float2 x0 = (t - offset) / 2.5;

    // Rank the five families by how close the nearest line of each lies (tiny sorting network).
    float dist[5] = { 0.0, 0.0, 0.0, 0.0, 0.0 };
    int ord[5] = { 0, 1, 2, 3, 4 };
    float nearLine[5] = { 0.0, 0.0, 0.0, 0.0, 0.0 };
    [unroll]
    for (int n = 0; n < 5; n++)
    {
        float f = dot(x0, PM_E[n]) + gam[n];
        nearLine[n] = floor(f + 0.5);
        dist[n] = abs(f - nearLine[n]);
        ord[n] = n;
    }
    [unroll]
    for (int sweep = 0; sweep < 4; sweep++)
    {
        [unroll]
        for (int m = 0; m < 4 - sweep; m++)
        {
            if (dist[ord[m]] > dist[ord[m + 1]])
            {
                int tmp = ord[m];
                ord[m] = ord[m + 1];
                ord[m + 1] = tmp;
            }
        }
    }

    float2 local = float2(0.0, 0.0);
    float2 corner = float2(0.0, 0.0);
    int fr = 0;
    int fs = 1;
    float found = 0.0;
    [loop]
    for (int c = 0; c < 10; c++)
    {
        int a = ord[PM_RA[c]];
        int b = ord[PM_RB[c]];
        int r = min(a, b);
        int s = max(a, b);
        if (pmTryRhomb(t, r, s, nearLine[r], nearLine[s], gam, local, corner))
        {
            fr = r;
            fs = s;
            found = 1.0;
            break;
        }
    }
    if (found < 0.5)
    {
        // Rare fallback: try the other neighbouring lines of every pair.
        [loop]
        for (int c2 = 0; c2 < 40; c2++)
        {
            int pairIndex = c2 / 4;
            int r = ord[PM_RA[pairIndex]];
            int s = ord[PM_RB[pairIndex]];
            int lo = min(r, s);
            int hi = max(r, s);
            float fLo = dot(x0, PM_E[lo]) + gam[lo];
            float fHi = dot(x0, PM_E[hi]) + gam[hi];
            float kr = floor(fLo) + (float)(c2 % 2);
            float ks = floor(fHi) + (float)((c2 / 2) % 2);
            if (pmTryRhomb(t, lo, hi, kr, ks, gam, local, corner))
            {
                fr = lo;
                fs = hi;
                found = 1.0;
                break;
            }
        }
    }

    float2 er = PM_E[fr];
    float2 es = PM_E[fs];
    float det = abs(er.x * es.y - er.y * es.x);
    float cosAngle = dot(er, es);
    bool thin = (fs - fr == 2) || (fs - fr == 3);
    float u = local.x;
    float v = local.y;

    // Each rhomb is cut like a gem: a flat table in the middle, four bevels sloping to the edges.
    float2 nU = normalize(er - cosAngle * es);
    float2 nV = normalize(es - cosAngle * er);
    float eu0 = u;
    float eu1 = 1.0 - u;
    float ev0 = v;
    float ev1 = 1.0 - v;
    float edgeLocal = min(min(eu0, eu1), min(ev0, ev1));
    float2 outward = (eu0 == edgeLocal) ? -nU : ((eu1 == edgeLocal) ? nU : ((ev0 == edgeLocal) ? -nV : nV));
    const float tableInset = 0.2;
    bool onTable = edgeLocal > tableInset;
    float3 facetNormal = onTable ? float3(0.0, 0.0, 1.0) : normalize(float3(outward * 0.7, 1.0));
    float lightAngle = Time * 0.21;
    float3 lightDir = normalize(float3(cos(lightAngle), sin(lightAngle), 1.1));
    float diffuse = saturate(dot(facetNormal, lightDir));
    float3 halfVec = normalize(lightDir + float3(0.0, 0.0, 1.0));
    float glint = onTable ? 0.0 : pow(saturate(dot(facetNormal, halfVec)), 70.0);

    // Tile colour: thick rhombs in sea-blues and violets, thin ones in ruby and gold; the palette drifts.
    int pairId = fr * 2 + fs;
    float band = spectrum(0.05 + (pairId % 10) * 0.09);
    float hue = thin ? (0.97 + 0.07 * sin(Time * 0.017 + 1.0) + fr * 0.025)
                     : (0.62 + 0.12 * sin(Time * 0.021) + fr * 0.03);
    float3 gem = hsv2rgb(hue, 0.85, 1.0);
    float2 centre = corner + 0.5 * er + 0.5 * es;
    float centreRadius = length(centre) / scale;

    // Beat ripple: a ring of tiles lights up and runs outward.
    float ripple = Beat * exp(-abs(centreRadius - (1.0 - Beat) * 1.7) * 7.0);

    // A sheen of light slides across the tables like a reflection on glass.
    float sheenCoord = dot(t, float2(0.8, 0.6)) * 0.35 - Time * 0.25;
    float sheen = exp(-pow(frac(sheenCoord) - 0.5, 2.0) * 60.0);
    float bevelLight = 0.02 + 0.13 * diffuse * diffuse;
    float tableLight = 0.05 + sheen * 0.16;
    float fill = (onTable ? tableLight : bevelLight) + band * 0.05;
    float3 colour = gem * (fill + ripple * 0.35) + lerp(gem, float3(1.0, 1.0, 1.0), 0.5) * glint * (0.7 + Treble * 1.3);

    // Gold edges, and a finer engraved line around each table.
    float halfWidth = max(px * 0.5, 0.0013);
    float edgeDist = edgeLocal * det / scale;
    float edges = pmStroke(edgeDist, halfWidth, px) + exp(-edgeDist / 0.004) * 0.15;
    float tableLine = pmStroke((edgeLocal - tableInset) * det / scale, halfWidth * 0.7, px);
    colour += float3(1.0, 0.78, 0.45) * edges * (0.35 + Level * 0.3) + gem * tableLine * 0.35;

    // Curves: arcs about two opposite corners join edge midpoints into endless loops of flowing light.
    float2 pos = u * er + v * es;
    bool acuteAtOrigin = cosAngle > 0.0;
    float2 c0 = acuteAtOrigin ? float2(0.0, 0.0) : er;
    float2 c1 = acuteAtOrigin ? er + es : es;
    float arc = min(abs(length(pos - c0) - 0.5), abs(length(pos - c1) - 0.5)) / scale;
    float flow = pow(0.5 + 0.5 * sin(length(t) * 1.1 - Time * 1.5), 4.0);
    float curves = pmStroke(arc, halfWidth * 1.2, px) + exp(-arc / 0.004) * 0.25;
    float3 curveColour = hsv2rgb(0.5 + 0.12 * sin(Time * 0.031), 0.45, 1.0);
    colour += curveColour * curves * (0.45 + flow * 1.6) * (0.6 + Mid * 0.8);

    // Treble sparkles flash briefly on the tiles' corners, each corner in its own rhythm.
    float2 vertexPos = corner + ((u > 0.5) ? er : float2(0.0, 0.0)) + ((v > 0.5) ? es : float2(0.0, 0.0));
    float vertexDist = length(t - vertexPos) / scale;
    float vertexSeed = frac(sin(dot(vertexPos, float2(12.9898, 78.233))) * 43758.5453);
    float twinklePhase = frac(Time * 0.6 + vertexSeed * 13.0) - 0.5;
    float twinkle = saturate(1.0 - abs(twinklePhase) * 14.0) * step(0.55, frac(vertexSeed * 7.31));
    colour += float3(1.0, 0.95, 0.9) * exp(-vertexDist * vertexDist / (px * px * 3.0)) * twinkle * Treble * 2.5;

    return colour + previous(uv) * 0.08;
}
