namespace LanGames;

// =====================================================================================
//  Chess3D.cs - a small real-3D engine written from scratch (no NuGet / OpenGL needed).
//  * pieces are real 3D meshes (lathe-turned profiles + extruded knight head)
//  * perspective camera you can orbit, z-buffer, per-pixel lighting, planar shadows
//  It renders into an int[] pixel buffer; ChessBoard (in ChessForm.cs) shows it on screen.
//  Board coordinates: X = file (a..h  ->  -3.5 .. +3.5), Z = row (row 0 / rank 8 at -3.5,
//  white side at +3.5), Y = up.
// =====================================================================================

public struct PieceInst { public char Type; public bool White; public float X, Z, Lift; }

/// <summary>Flat marker drawn on the board. Shape 0 = square tile, 1 = disc, 2 = ring.</summary>
public struct Decal { public byte Shape; public float X, Z, R1, R2, Cr, Cg, Cb, A; }

public sealed class Mesh3D
{
    public float[] P = Array.Empty<float>();   // xyz per vertex
    public float[] N = Array.Empty<float>();   // normal per vertex
    public int[] I = Array.Empty<int>();       // triangle indices
    public int VCount => P.Length / 3;
}

public struct Cam
{
    public float Px, Py, Pz, Fx, Fy, Fz, Rx, Ry, Rz, Ux, Uy, Uz, Focal, Cx, Cy;
    public const float FovDeg = 35f;

    public static Cam Make(int w, int h, float yaw, float pitch, float dist)
    {
        var c = new Cam();
        float cp = MathF.Cos(pitch), sp = MathF.Sin(pitch);
        const float ty = 0.25f;
        c.Px = dist * cp * MathF.Sin(yaw);
        c.Py = ty + dist * sp;
        c.Pz = dist * cp * MathF.Cos(yaw);
        float fx = -c.Px, fy = ty - c.Py, fz = -c.Pz;
        float fl = MathF.Sqrt(fx * fx + fy * fy + fz * fz);
        c.Fx = fx / fl; c.Fy = fy / fl; c.Fz = fz / fl;
        // right = normalize(cross(f, up))
        float rx = -c.Fz, ry = 0, rz = c.Fx;
        float rl = MathF.Sqrt(rx * rx + rz * rz);
        c.Rx = rx / rl; c.Ry = ry; c.Rz = rz / rl;
        // up = cross(right, f)
        c.Ux = c.Ry * c.Fz - c.Rz * c.Fy;
        c.Uy = c.Rz * c.Fx - c.Rx * c.Fz;
        c.Uz = c.Rx * c.Fy - c.Ry * c.Fx;
        c.Focal = 0.5f * h / MathF.Tan(FovDeg * MathF.PI / 360f);
        c.Cx = w / 2f; c.Cy = h / 2f;
        return c;
    }

    public bool Project(float x, float y, float z, out float sx, out float sy)
    {
        float dx = x - Px, dy = y - Py, dz = z - Pz;
        float vz = dx * Fx + dy * Fy + dz * Fz;
        sx = sy = 0;
        if (vz < 0.2f) return false;
        float vx = dx * Rx + dy * Ry + dz * Rz, vy = dx * Ux + dy * Uy + dz * Uz;
        sx = Cx + Focal * vx / vz;
        sy = Cy - Focal * vy / vz;
        return true;
    }

    /// <summary>World-space ray through a screen pixel.</summary>
    public void Ray(float sx, float sy, out float dx, out float dy, out float dz)
    {
        float a = (sx - Cx) / Focal, b = -(sy - Cy) / Focal;
        dx = Fx + a * Rx + b * Ux; dy = Fy + a * Ry + b * Uy; dz = Fz + a * Rz + b * Uz;
        float l = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        dx /= l; dy /= l; dz /= l;
    }
}

// ------------------------------------------------------------------ mesh building
internal sealed class MeshBuilder
{
    private readonly List<float> p = new(), n = new();
    private readonly List<int> idx = new();

    public int V(float x, float y, float z, float nx, float ny, float nz)
    {
        p.Add(x); p.Add(y); p.Add(z); n.Add(nx); n.Add(ny); n.Add(nz);
        return p.Count / 3 - 1;
    }
    public void Tri(int a, int b, int c) { idx.Add(a); idx.Add(b); idx.Add(c); }
    public Mesh3D Build()
    {
        var P = p.ToArray(); var N = n.ToArray(); var I = idx.ToArray();
        // make every triangle wind "outwards" (matching its vertex normals) so back faces can be culled
        for (int t = 0; t < I.Length; t += 3)
        {
            int a = I[t] * 3, b = I[t + 1] * 3, c = I[t + 2] * 3;
            float ux = P[b] - P[a], uy = P[b + 1] - P[a + 1], uz = P[b + 2] - P[a + 2];
            float vx = P[c] - P[a], vy = P[c + 1] - P[a + 1], vz = P[c + 2] - P[a + 2];
            float gx = uy * vz - uz * vy, gy = uz * vx - ux * vz, gz = ux * vy - uy * vx;
            float nx = N[a] + N[b] + N[c], ny = N[a + 1] + N[b + 1] + N[c + 1], nz = N[a + 2] + N[b + 2] + N[c + 2];
            if (gx * nx + gy * ny + gz * nz < 0) (I[t + 1], I[t + 2]) = (I[t + 2], I[t + 1]);
        }
        return new() { P = P, N = N, I = I };
    }

    /// <summary>Turns a (radius, height) profile around the Y axis. Normals are smooth.</summary>
    public void Lathe(IList<(float r, float y)> prof, int seg, float ox = 0, float oz = 0)
    {
        int m = prof.Count, b0 = p.Count / 3, ring = seg + 1;
        for (int j = 0; j < m; j++)
        {
            var a = prof[Math.Max(0, j - 1)]; var b = prof[Math.Min(m - 1, j + 1)];
            float nr = b.y - a.y, ny = -(b.r - a.r);
            float l = MathF.Sqrt(nr * nr + ny * ny);
            if (l < 1e-6f) { nr = 1; ny = 0; l = 1; }
            nr /= l; ny /= l;
            for (int s = 0; s <= seg; s++)
            {
                float t = s * MathF.PI * 2 / seg, c = MathF.Cos(t), sn = MathF.Sin(t);
                V(ox + prof[j].r * c, prof[j].y, oz + prof[j].r * sn, nr * c, ny, nr * sn);
            }
        }
        for (int j = 0; j < m - 1; j++)
            for (int s = 0; s < seg; s++)
            {
                int a = b0 + j * ring + s, b = a + 1, c = a + ring, d = c + 1;
                Tri(a, b, c); Tri(b, d, c);
            }
    }

    public void Sphere(float cx, float cy, float cz, float rad, int seg = 14, int rings = 8)
    {
        var prof = new List<(float, float)>();
        for (int i = 0; i <= rings; i++)
        {
            float a = -MathF.PI / 2 + MathF.PI * i / rings;
            prof.Add((rad * MathF.Cos(a), cy + rad * MathF.Sin(a)));
        }
        Lathe(prof, seg, cx, cz);
    }

    private void FlatQuad(float[] q, float nx, float ny, float nz)
    {
        int a = V(q[0], q[1], q[2], nx, ny, nz), b = V(q[3], q[4], q[5], nx, ny, nz),
            c = V(q[6], q[7], q[8], nx, ny, nz), d = V(q[9], q[10], q[11], nx, ny, nz);
        Tri(a, b, c); Tri(a, c, d);
    }

    public void Box(float cx, float cy, float cz, float sx, float sy, float sz, float rot = 0)
    {
        float hx = sx / 2, hy = sy / 2, hz = sz / 2, c = MathF.Cos(rot), s = MathF.Sin(rot);
        void Face(float nx, float ny, float nz, params float[] q)
        {
            var w = new float[12];
            for (int i = 0; i < 4; i++)
            {
                float x = q[i * 3], y = q[i * 3 + 1], z = q[i * 3 + 2];
                w[i * 3] = cx + x * c + z * s; w[i * 3 + 1] = cy + y; w[i * 3 + 2] = cz - x * s + z * c;
            }
            FlatQuad(w, nx * c + nz * s, ny, -nx * s + nz * c);
        }
        Face(1, 0, 0, hx, -hy, -hz, hx, hy, -hz, hx, hy, hz, hx, -hy, hz);
        Face(-1, 0, 0, -hx, -hy, hz, -hx, hy, hz, -hx, hy, -hz, -hx, -hy, -hz);
        Face(0, 1, 0, -hx, hy, -hz, -hx, hy, hz, hx, hy, hz, hx, hy, -hz);
        Face(0, -1, 0, -hx, -hy, hz, -hx, -hy, -hz, hx, -hy, -hz, hx, -hy, hz);
        Face(0, 0, 1, -hx, -hy, hz, hx, -hy, hz, hx, hy, hz, -hx, hy, hz);
        Face(0, 0, -1, hx, -hy, -hz, -hx, -hy, -hz, -hx, hy, -hz, hx, hy, -hz);
    }

    // ---- extrusion of a side-view polygon (z forward, y up) along X, with bevelled caps ----
    public void Extrude(List<(float z, float y)> poly, (float x, float scale)[] layers)
    {
        int n2 = poly.Count;
        float cz = poly.Average(q => q.z), cy = poly.Average(q => q.y);
        (float x, float y, float z) At(int layer, int i)
        {
            var (x, sc) = layers[layer];
            return (x, cy + (poly[i].y - cy) * sc, cz + (poly[i].z - cz) * sc);
        }
        void Quad((float x, float y, float z) a, (float x, float y, float z) b, (float x, float y, float z) c, (float x, float y, float z) d)
        {
            float ux = b.x - a.x, uy = b.y - a.y, uz = b.z - a.z, vx = d.x - a.x, vy = d.y - a.y, vz = d.z - a.z;
            float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            float l = MathF.Sqrt(nx * nx + ny * ny + nz * nz); if (l < 1e-9f) return;
            nx /= l; ny /= l; nz /= l;
            int i0 = V(a.x, a.y, a.z, nx, ny, nz), i1 = V(b.x, b.y, b.z, nx, ny, nz),
                i2 = V(c.x, c.y, c.z, nx, ny, nz), i3 = V(d.x, d.y, d.z, nx, ny, nz);
            Tri(i0, i1, i2); Tri(i0, i2, i3);
        }
        for (int l = 0; l < layers.Length - 1; l++)
            for (int i = 0; i < n2; i++)
            {
                int j = (i + 1) % n2;
                Quad(At(l, i), At(l, j), At(l + 1, j), At(l + 1, i));
            }
        var tris = Triangulate(poly);
        foreach (int cap in new[] { 0, layers.Length - 1 })
        {
            float nxs = cap == 0 ? -1 : 1;
            for (int t = 0; t < tris.Count; t += 3)
            {
                var a = At(cap, tris[t]); var b = At(cap, tris[t + 1]); var c = At(cap, tris[t + 2]);
                int i0 = V(a.x, a.y, a.z, nxs, 0, 0), i1 = V(b.x, b.y, b.z, nxs, 0, 0), i2 = V(c.x, c.y, c.z, nxs, 0, 0);
                Tri(i0, i1, i2);
            }
        }
    }

    private static List<int> Triangulate(List<(float z, float y)> pt)
    {
        int n2 = pt.Count;
        float area = 0;
        for (int i = 0; i < n2; i++) { var a = pt[i]; var b = pt[(i + 1) % n2]; area += a.z * b.y - b.z * a.y; }
        bool ccw = area > 0;
        var idx2 = Enumerable.Range(0, n2).ToList();
        var res = new List<int>();
        int guard = 0;
        while (idx2.Count > 3 && guard++ < 2000)
        {
            bool clipped = false;
            for (int i = 0; i < idx2.Count && !clipped; i++)
            {
                int a = idx2[(i + idx2.Count - 1) % idx2.Count], b = idx2[i], c = idx2[(i + 1) % idx2.Count];
                float cr = (pt[b].z - pt[a].z) * (pt[c].y - pt[b].y) - (pt[b].y - pt[a].y) * (pt[c].z - pt[b].z);
                if ((cr > 0) != ccw || MathF.Abs(cr) < 1e-9f) continue;
                bool inside = false;
                foreach (int q in idx2)
                {
                    if (q == a || q == b || q == c) continue;
                    if (InTri(pt[q], pt[a], pt[b], pt[c])) { inside = true; break; }
                }
                if (inside) continue;
                res.Add(a); res.Add(b); res.Add(c);
                idx2.RemoveAt(i);
                clipped = true;
            }
            if (!clipped) break;
        }
        if (idx2.Count == 3) { res.AddRange(idx2); }
        return res;
    }

    private static bool InTri((float z, float y) p, (float z, float y) a, (float z, float y) b, (float z, float y) c)
    {
        float d1 = Sg(p, a, b), d2 = Sg(p, b, c), d3 = Sg(p, c, a);
        bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(neg && pos);
    }
    private static float Sg((float z, float y) p, (float z, float y) a, (float z, float y) b) =>
        (p.z - b.z) * (a.y - b.y) - (a.z - b.z) * (p.y - b.y);
}

// ------------------------------------------------------------------ the pieces
public static class PieceMeshes
{
    public const float Scale = 0.9f;   // all pieces are drawn at this scale
    public static readonly Dictionary<char, Mesh3D> M = new();
    public static readonly Dictionary<char, Mesh3D> S = new();   // low-poly copies used only for shadows
    public static readonly Dictionary<char, float> Height = new()
    {
        ['p'] = .91f, ['r'] = 1.16f, ['n'] = 1.46f, ['b'] = 1.24f, ['q'] = 1.36f, ['k'] = 1.60f
    };

    private static readonly float[] BaseRing = { 0, 0, .42f, 0, .44f, .03f, .44f, .08f, .40f, .12f, .34f, .16f };

    private static List<(float r, float y)> Prof(params float[][] parts)
    {
        var l = new List<(float, float)>();
        foreach (var part in parts)
            for (int i = 0; i + 1 < part.Length; i += 2) l.Add((part[i], part[i + 1]));
        return l;
    }

    private static float[] Arc(float cy, float rad, float a0, float a1, int n)
    {
        var f = new List<float>();
        for (int i = 0; i <= n; i++)
        {
            float a = (a0 + (a1 - a0) * i / n) * MathF.PI / 180f;
            f.Add(MathF.Max(0, rad * MathF.Cos(a))); f.Add(cy + rad * MathF.Sin(a));
        }
        return f.ToArray();
    }

    static PieceMeshes()
    {
        BuildAll(28, M);
        BuildAll(12, S);
    }

    private static void BuildAll(int Seg, Dictionary<char, Mesh3D> M)
    {

        // pawn
        var mb = new MeshBuilder();
        mb.Lathe(Prof(BaseRing, new[] { .26f, .22f, .18f, .34f, .15f, .46f, .22f, .50f, .22f, .54f, .14f, .58f, .12f, .62f },
            Arc(.74f, .17f, -48, 90, 10)), Seg);
        M['p'] = mb.Build();

        // rook
        mb = new MeshBuilder();
        mb.Lathe(Prof(BaseRing, new[] { .30f, .20f, .25f, .28f, .23f, .55f, .23f, .80f, .30f, .86f, .34f, .90f, .34f, 1.0f,
            .34f, 1.04f, .26f, 1.04f, .26f, .98f, 0, .98f }), Seg);
        for (int i = 0; i < 6; i++)
        {
            float a = i * MathF.PI / 3;
            mb.Box(.30f * MathF.Cos(a), 1.11f, .30f * MathF.Sin(a), .17f, .14f, .09f, MathF.PI / 2 - a);
        }
        M['r'] = mb.Build();

        // bishop
        mb = new MeshBuilder();
        mb.Lathe(Prof(BaseRing, new[] { .28f, .20f, .18f, .34f, .14f, .50f, .14f, .54f, .26f, .58f, .26f, .64f, .14f, .68f,
            .17f, .72f, .205f, .80f, .215f, .90f, .19f, 1.0f, .12f, 1.08f, .045f, 1.12f, .05f, 1.14f, .07f, 1.18f, .05f, 1.22f, 0, 1.24f }), Seg);
        M['b'] = mb.Build();

        // queen
        mb = new MeshBuilder();
        mb.Lathe(Prof(BaseRing, new[] { .30f, .22f, .20f, .38f, .15f, .62f, .14f, .84f, .22f, .90f, .30f, 1.0f, .34f, 1.10f,
            .36f, 1.18f, .30f, 1.20f, .22f, 1.16f, 0, 1.14f }), Seg);
        for (int i = 0; i < 8; i++)
        {
            float a = i * MathF.PI / 4;
            mb.Sphere(.32f * MathF.Cos(a), 1.22f, .32f * MathF.Sin(a), .055f, 10, 6);
        }
        mb.Sphere(0, 1.25f, 0, .10f, 14, 8);
        M['q'] = mb.Build();

        // king
        mb = new MeshBuilder();
        mb.Lathe(Prof(BaseRing, new[] { .30f, .22f, .20f, .40f, .16f, .66f, .16f, .90f, .26f, .96f, .33f, 1.06f, .35f, 1.16f,
            .28f, 1.20f, 0, 1.18f }), Seg);
        mb.Box(0, 1.40f, 0, .08f, .42f, .08f);
        mb.Box(0, 1.46f, 0, .27f, .08f, .08f);
        M['k'] = mb.Build();

        // knight: pedestal + extruded side silhouette (faces +Z)
        mb = new MeshBuilder();
        mb.Lathe(Prof(BaseRing, new[] { .30f, .20f, .30f, .26f, 0, .26f }), Seg);
        var head = new List<(float z, float y)>
        {
            (-.30f, .24f), (-.32f, .50f), (-.28f, .80f), (-.22f, 1.08f), (-.12f, 1.22f), (-.06f, 1.38f), (.03f, 1.46f),
            (.10f, 1.36f), (.12f, 1.28f), (.28f, 1.14f), (.44f, .90f), (.48f, .76f), (.40f, .66f), (.22f, .66f),
            (.14f, .60f), (.20f, .46f), (.30f, .34f), (.32f, .24f)
        };
        mb.Extrude(head, new[] { (-.13f, .80f), (-.21f, 1f), (.21f, 1f), (.13f, .80f) });
        M['n'] = mb.Build();
    }
}

// ------------------------------------------------------------------ renderer
public sealed class Software3D
{
    public int W, H;
    public int[] Pix = Array.Empty<int>();

    private float[] zb = Array.Empty<float>();
    private byte[] mask = Array.Empty<byte>();
    private Cam cam;

    // vertex pools (all draw calls of one frame live here, so the screen can be shaded in parallel bands)
    private float[] sx = new float[1 << 17], sy = new float[1 << 17], siz = new float[1 << 17];
    private float[] wnx = new float[1 << 17], wny = new float[1 << 17], wnz = new float[1 << 17];
    private int cur;

    private struct Call
    {
        public byte Kind;          // 0 lit mesh, 1 shadow, 2 decal
        public Mesh3D? Mesh;
        public int Off;
        public float Cr, Cg, Cb;
        public float[]? Tab;
        public int[]? Tris;        // decal triangles (absolute indices)
        public Decal D;
    }
    private readonly List<Call> calls = new();

    // light (direction pointing TOWARD the light) and half vector
    private float lx, ly, lz, hx, hy, hz;
    private const float AmbSr = .40f, AmbSg = .42f, AmbSb = .50f, AmbGr = .17f, AmbGg = .15f, AmbGb = .14f;
    private const float LR = 1.0f, LG = .95f, LB = .86f;

    // ---- static board geometry ----
    private static readonly Mesh3D Tile, Plate, FrameLong, FrameSide, Lip;
    static Software3D()
    {
        var b = new MeshBuilder(); b.Box(0, -.15f, 0, 1f, .3f, 1f); Tile = b.Build();
        b = new MeshBuilder(); b.Box(0, -.42f, 0, 9.5f, .24f, 9.5f); Plate = b.Build();
        b = new MeshBuilder(); b.Box(0, -.12f, 0, 9.2f, .36f, .6f); FrameLong = b.Build();
        b = new MeshBuilder(); b.Box(0, -.12f, 0, .6f, .36f, 8.0f); FrameSide = b.Build();
        b = new MeshBuilder(); b.Box(0, .035f, 0, 8.1f, .07f, .06f); Lip = b.Build();
    }

    private int[] bg = Array.Empty<int>();

    private void Resize(int w, int h)
    {
        if (w == W && h == H && Pix.Length == w * h) return;
        W = w; H = h;
        Pix = new int[w * h]; zb = new float[w * h]; mask = new byte[w * h]; bg = new int[w * h];
        for (int y = 0; y < h; y++)
        {
            float t = y / (float)h;
            for (int x = 0; x < w; x++)
            {
                float dx = (x / (float)w - .5f) * 2, dy = (t - .55f) * 2;
                float g = 1f - (dx * dx * .6f + dy * dy);
                if (g < 0) g = 0;
                g *= g * .5f;
                float r = 11 + 8 * t + 14 * g, gg = 14 + 12 * t + 40 * g, b = 28 + 22 * t + 52 * g;
                bg[y * w + x] = unchecked((int)0xFF000000) | ((int)r << 16) | ((int)gg << 8) | (int)b;
            }
        }
    }

    private void Grow(int need)
    {
        if (cur + need <= sx.Length) return;
        int n = Math.Max(sx.Length * 2, cur + need);
        Array.Resize(ref sx, n); Array.Resize(ref sy, n); Array.Resize(ref siz, n);
        Array.Resize(ref wnx, n); Array.Resize(ref wny, n); Array.Resize(ref wnz, n);
    }

    public void Render(int w, int h, float yaw, float pitch, float dist,
                       IReadOnlyList<PieceInst> pieces, IReadOnlyList<Decal>? decals)
    {
        Resize(w, h);
        cam = Cam.Make(w, h, yaw, pitch, dist);
        calls.Clear(); cur = 0;

        float ll = MathF.Sqrt(.45f * .45f + .8f * .8f + .4f * .4f);
        lx = -.45f / ll; ly = .8f / ll; lz = .4f / ll;
        float vx = -cam.Fx + lx, vy = -cam.Fy + ly, vz = -cam.Fz + lz;
        float vl = MathF.Sqrt(vx * vx + vy * vy + vz * vz);
        hx = vx / vl; hy = vy / vl; hz = vz / vl;

        // ---- queue draw calls in painting order ----
        QueueBoard();
        foreach (var pc in pieces) QueueShadow(pc);
        if (decals != null) foreach (var d in decals) QueueDecal(d);
        foreach (var pc in pieces)
        {
            float rot = 0;
            if (pc.Type == 'n')   // knights look diagonally towards the centre so their head profile shows
            {
                float sg = pc.X < 0 ? 1f : -1f;
                rot = pc.White ? MathF.PI - sg * .75f : sg * .75f;
            }
            else if (pc.White) rot = 0;
            if (pc.White) QueueMesh(PieceMeshes.M[pc.Type], PieceMeshes.Scale, pc.X, pc.Lift, pc.Z, rot, .93f, .89f, .80f, .55f, 45);
            else QueueMesh(PieceMeshes.M[pc.Type], PieceMeshes.Scale, pc.X, pc.Lift, pc.Z, rot, .085f, .085f, .105f, .85f, 70);
        }

        // ---- shade the screen in parallel horizontal bands (each band owns its pixel rows) ----
        int bands = Math.Min(h, Math.Clamp(Environment.ProcessorCount * 2, 2, 16));
        Parallel.For(0, bands, b =>
        {
            int y0 = h * b / bands, y1 = h * (b + 1) / bands - 1;
            Background(y0, y1);
            foreach (var c in calls)
            {
                switch (c.Kind)
                {
                    case 0:
                        for (int t = 0; t < c.Mesh!.I.Length; t += 3)
                            TriLit(c.Off + c.Mesh.I[t], c.Off + c.Mesh.I[t + 1], c.Off + c.Mesh.I[t + 2], c.Cr, c.Cg, c.Cb, c.Tab!, y0, y1);
                        break;
                    case 1:
                        for (int t = 0; t < c.Mesh!.I.Length; t += 3)
                            TriShadow(c.Off + c.Mesh.I[t], c.Off + c.Mesh.I[t + 1], c.Off + c.Mesh.I[t + 2], y0, y1);
                        break;
                    default:
                        for (int t = 0; t < c.Tris!.Length; t += 3)
                            TriDecal(c.Tris[t], c.Tris[t + 1], c.Tris[t + 2], c.D, y0, y1);
                        break;
                }
            }
        });
    }

    private void Background(int y0, int y1)
    {
        int n = (y1 - y0 + 1) * W;
        Array.Clear(zb, y0 * W, n);
        Array.Clear(mask, y0 * W, n);
        Array.Copy(bg, y0 * W, Pix, y0 * W, n);
    }

    private void QueueBoard()
    {
        QueueMesh(Plate, 1, 0, 0, 0, 0, .13f, .08f, .05f, .15f, 20);
        for (int r = 0; r < 8; r++)
            for (int c = 0; c < 8; c++)
            {
                bool light = (r + c) % 2 == 0;
                float v = ((r * 13 + c * 7) % 5 - 2) * .008f;   // tiny per-square wood variation
                if (light) QueueMesh(Tile, 1, c - 3.5f, 0, r - 3.5f, 0, .90f + v, .78f + v, .59f + v, .22f, 30);
                else QueueMesh(Tile, 1, c - 3.5f, 0, r - 3.5f, 0, .46f + v, .29f + v, .17f + v, .22f, 30);
            }
        const float fr = .30f, fg = .17f, fb = .09f;
        QueueMesh(FrameLong, 1, 0, 0, -4.3f, 0, fr, fg, fb, .30f, 25);
        QueueMesh(FrameLong, 1, 0, 0, 4.3f, 0, fr, fg, fb, .30f, 25);
        QueueMesh(FrameSide, 1, -4.3f, 0, 0, 0, fr, fg, fb, .30f, 25);
        QueueMesh(FrameSide, 1, 4.3f, 0, 0, 0, fr, fg, fb, .30f, 25);
        QueueMesh(Lip, 1, 0, 0, -4.03f, 0, .80f, .62f, .35f, .6f, 40);
        QueueMesh(Lip, 1, 0, 0, 4.03f, 0, .80f, .62f, .35f, .6f, 40);
        QueueMesh(Lip, 1, -4.03f, 0, 0, MathF.PI / 2, .80f, .62f, .35f, .6f, 40);
        QueueMesh(Lip, 1, 4.03f, 0, 0, MathF.PI / 2, .80f, .62f, .35f, .6f, 40);
    }

    // transform vertices (and normals) of a mesh into screen space, appended to the vertex pool
    private int Prep(Mesh3D m, float scale, float ox, float oy, float oz, float rot, bool shadow)
    {
        int n = m.VCount, off = cur;
        Grow(n);
        float c = MathF.Cos(rot), s = MathF.Sin(rot);
        for (int i = 0; i < n; i++)
        {
            int k = off + i;
            float x = m.P[i * 3] * scale, y = m.P[i * 3 + 1] * scale, z = m.P[i * 3 + 2] * scale;
            float wx = ox + x * c + z * s, wy = oy + y, wz = oz - x * s + z * c;
            if (shadow)
            {
                float t = wy / ly;
                wx -= lx * t; wz -= lz * t; wy = .004f;
            }
            else
            {
                float nx = m.N[i * 3], ny = m.N[i * 3 + 1], nz = m.N[i * 3 + 2];
                wnx[k] = nx * c + nz * s; wny[k] = ny; wnz[k] = -nx * s + nz * c;
            }
            float dx = wx - cam.Px, dy = wy - cam.Py, dz = wz - cam.Pz;
            float vz = dx * cam.Fx + dy * cam.Fy + dz * cam.Fz;
            if (vz < .2f) { sx[k] = float.NaN; continue; }
            float vx = dx * cam.Rx + dy * cam.Ry + dz * cam.Rz, vy = dx * cam.Ux + dy * cam.Uy + dz * cam.Uz;
            float iz = 1f / vz;
            sx[k] = cam.Cx + cam.Focal * vx * iz;
            sy[k] = cam.Cy - cam.Focal * vy * iz;
            siz[k] = iz;
        }
        cur += n;
        return off;
    }

    private void QueueMesh(Mesh3D m, float scale, float ox, float oy, float oz, float rot,
                           float cr, float cg, float cb, float spec, float shin)
    {
        int off = Prep(m, scale, ox, oy, oz, rot, false);
        var table = new float[256];
        for (int i = 0; i < 256; i++) table[i] = MathF.Pow(i / 255f, shin) * spec;
        calls.Add(new Call { Kind = 0, Mesh = m, Off = off, Cr = cr, Cg = cg, Cb = cb, Tab = table });
    }

    private void QueueShadow(PieceInst pc)
    {
        var m = PieceMeshes.S[pc.Type];
        int off = Prep(m, PieceMeshes.Scale, pc.X, pc.Lift, pc.Z, 0, true);
        calls.Add(new Call { Kind = 1, Mesh = m, Off = off });
    }

    private void QueueDecal(Decal d)
    {
        const float y = .009f;
        int start = cur;
        Grow(80);
        int n = cur;
        int PV(float x, float z)
        {
            if (cam.Project(x, y, z, out float px, out float py))
            {
                sx[n] = px; sy[n] = py;
                float dx = x - cam.Px, dy = y - cam.Py, dz = z - cam.Pz;
                siz[n] = 1f / (dx * cam.Fx + dy * cam.Fy + dz * cam.Fz);
            }
            else sx[n] = float.NaN;
            return n++;
        }
        var tris = new List<int>();
        if (d.Shape == 0)
        {
            int a = PV(d.X - d.R1, d.Z - d.R1), b = PV(d.X + d.R1, d.Z - d.R1), c = PV(d.X + d.R1, d.Z + d.R1), e = PV(d.X - d.R1, d.Z + d.R1);
            tris.AddRange(new[] { a, b, c, a, c, e });
        }
        else if (d.Shape == 1)
        {
            const int N = 28;
            int ctr = PV(d.X, d.Z), first = n;
            for (int i = 0; i < N; i++) { float a = i * MathF.PI * 2 / N; PV(d.X + d.R1 * MathF.Cos(a), d.Z + d.R1 * MathF.Sin(a)); }
            for (int i = 0; i < N; i++) tris.AddRange(new[] { ctr, first + i, first + (i + 1) % N });
        }
        else
        {
            const int N = 32;
            int o0 = n;
            for (int i = 0; i < N; i++) { float a = i * MathF.PI * 2 / N; PV(d.X + d.R1 * MathF.Cos(a), d.Z + d.R1 * MathF.Sin(a)); }
            int i0 = n;
            for (int i = 0; i < N; i++) { float a = i * MathF.PI * 2 / N; PV(d.X + d.R2 * MathF.Cos(a), d.Z + d.R2 * MathF.Sin(a)); }
            for (int i = 0; i < N; i++)
            {
                int j = (i + 1) % N;
                tris.AddRange(new[] { o0 + i, o0 + j, i0 + j, o0 + i, i0 + j, i0 + i });
            }
        }
        cur = n;
        calls.Add(new Call { Kind = 2, Tris = tris.ToArray(), D = d });
    }

    // ---------------------------------------------------------------- rasterizers
    private bool Setup(int a, int b, int c, int ymin, int ymax, out float ax, out float ay, out float bx, out float by, out float cx, out float cy,
                       out int x0, out int x1, out int y0, out int y1, out float inv)
    {
        ax = sx[a]; ay = sy[a]; bx = sx[b]; by = sy[b]; cx = sx[c]; cy = sy[c];
        x0 = x1 = y0 = y1 = 0; inv = 0;
        if (float.IsNaN(ax + bx + cx)) return false;
        float area = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
        if (MathF.Abs(area) < 1e-6f) return false;
        y0 = Math.Max(ymin, (int)MathF.Floor(MathF.Min(ay, MathF.Min(by, cy))));
        y1 = Math.Min(ymax, (int)MathF.Ceiling(MathF.Max(ay, MathF.Max(by, cy))));
        if (y0 > y1) return false;
        x0 = Math.Max(0, (int)MathF.Floor(MathF.Min(ax, MathF.Min(bx, cx))));
        x1 = Math.Min(W - 1, (int)MathF.Ceiling(MathF.Max(ax, MathF.Max(bx, cx))));
        if (x0 > x1) return false;
        inv = 1f / area;
        return true;
    }

    private const int CullSign = 1;   // triangles whose screen area has this sign face away from the camera

    private void TriLit(int a, int b, int c, float cr, float cg, float cb, float[] spec, int ymin, int ymax)
    {
        if (!Setup(a, b, c, ymin, ymax, out float ax, out float ay, out float bx, out float by, out float cx, out float cy,
                   out int x0, out int x1, out int y0, out int y1, out float inv)) return;
        if (CullSign > 0 ? inv > 0 : inv < 0) return;
        float za = siz[a], zbv = siz[b], zc = siz[c];
        float nax = wnx[a], nay = wny[a], naz = wnz[a], nbx = wnx[b], nby = wny[b], nbz = wnz[b], ncx = wnx[c], ncy = wny[c], ncz = wnz[c];
        float dw0 = (by - cy) * inv, dw1 = (cy - ay) * inv;
        const float eps = -1e-4f;
        for (int y = y0; y <= y1; y++)
        {
            float py = y + .5f, px0 = x0 + .5f;
            float w0 = ((bx - px0) * (cy - py) - (by - py) * (cx - px0)) * inv;
            float w1 = ((cx - px0) * (ay - py) - (cy - py) * (ax - px0)) * inv;
            bool seen = false;
            int id = y * W + x0;
            for (int x = x0; x <= x1; x++, id++, w0 += dw0, w1 += dw1)
            {
                float w2 = 1f - w0 - w1;
                if (w0 < eps || w1 < eps || w2 < eps) { if (seen) break; continue; }
                seen = true;
                float iz = w0 * za + w1 * zbv + w2 * zc;
                if (iz <= zb[id]) continue;
                zb[id] = iz;

                float nx = w0 * nax + w1 * nbx + w2 * ncx;
                float ny = w0 * nay + w1 * nby + w2 * ncy;
                float nz = w0 * naz + w1 * nbz + w2 * ncz;
                float il = 1f / MathF.Sqrt(nx * nx + ny * ny + nz * nz + 1e-12f);
                nx *= il; ny *= il; nz *= il;
                if (nx * cam.Fx + ny * cam.Fy + nz * cam.Fz > 0) { nx = -nx; ny = -ny; nz = -nz; }   // two-sided
                float dif = nx * lx + ny * ly + nz * lz; if (dif < 0) dif = 0;
                float hs = nx * hx + ny * hy + nz * hz;
                float sp = hs <= 0 ? 0 : spec[(int)(hs * 255f)];
                float k = ny * .5f + .5f;
                float r = cr * (AmbGr + (AmbSr - AmbGr) * k + dif * LR * .85f) + sp;
                float g = cg * (AmbGg + (AmbSg - AmbGg) * k + dif * LG * .85f) + sp;
                float bl = cb * (AmbGb + (AmbSb - AmbGb) * k + dif * LB * .85f) + sp;
                int ri = r >= 1 ? 255 : (int)(r * 255), gi = g >= 1 ? 255 : (int)(g * 255), bi = bl >= 1 ? 255 : (int)(bl * 255);
                Pix[id] = unchecked((int)0xFF000000) | (ri << 16) | (gi << 8) | bi;
            }
        }
    }

    private void TriShadow(int a, int b, int c, int ymin, int ymax)
    {
        if (!Setup(a, b, c, ymin, ymax, out float ax, out float ay, out float bx, out float by, out float cx, out float cy,
                   out int x0, out int x1, out int y0, out int y1, out float inv)) return;
        float za = siz[a], zbv = siz[b], zc = siz[c];
        float dw0 = (by - cy) * inv, dw1 = (cy - ay) * inv;
        const float eps = -1e-4f;
        for (int y = y0; y <= y1; y++)
        {
            float py = y + .5f, px0 = x0 + .5f;
            float w0 = ((bx - px0) * (cy - py) - (by - py) * (cx - px0)) * inv;
            float w1 = ((cx - px0) * (ay - py) - (cy - py) * (ax - px0)) * inv;
            bool seen = false;
            int id = y * W + x0;
            for (int x = x0; x <= x1; x++, id++, w0 += dw0, w1 += dw1)
            {
                float w2 = 1f - w0 - w1;
                if (w0 < eps || w1 < eps || w2 < eps) { if (seen) break; continue; }
                seen = true;
                if (mask[id] != 0 || zb[id] <= 0) continue;
                float iz = w0 * za + w1 * zbv + w2 * zc;
                if (iz < zb[id]) continue;       // hidden by the frame / something nearer
                mask[id] = 1;
                int p = Pix[id];
                int r = (int)(((p >> 16) & 255) * .50f), g = (int)(((p >> 8) & 255) * .50f), bl = (int)((p & 255) * .56f);
                Pix[id] = unchecked((int)0xFF000000) | (r << 16) | (g << 8) | bl;
            }
        }
    }

    private void TriDecal(int a, int b, int c, Decal d, int ymin, int ymax)
    {
        if (!Setup(a, b, c, ymin, ymax, out float ax, out float ay, out float bx, out float by, out float cx, out float cy,
                   out int x0, out int x1, out int y0, out int y1, out float inv)) return;
        float za = siz[a], zbv = siz[b], zc = siz[c];
        float dw0 = (by - cy) * inv, dw1 = (cy - ay) * inv;
        const float eps = -1e-4f;
        int dr = (int)(d.Cr * 255), dg = (int)(d.Cg * 255), db = (int)(d.Cb * 255);
        float al = d.A, ia = 1 - d.A;
        for (int y = y0; y <= y1; y++)
        {
            float py = y + .5f, px0 = x0 + .5f;
            float w0 = ((bx - px0) * (cy - py) - (by - py) * (cx - px0)) * inv;
            float w1 = ((cx - px0) * (ay - py) - (cy - py) * (ax - px0)) * inv;
            bool seen = false;
            int id = y * W + x0;
            for (int x = x0; x <= x1; x++, id++, w0 += dw0, w1 += dw1)
            {
                float w2 = 1f - w0 - w1;
                if (w0 < eps || w1 < eps || w2 < eps) { if (seen) break; continue; }
                seen = true;
                float iz = w0 * za + w1 * zbv + w2 * zc;
                if (iz < zb[id]) continue;
                int p = Pix[id];
                int r = (int)(((p >> 16) & 255) * ia + dr * al), g = (int)(((p >> 8) & 255) * ia + dg * al), bl = (int)((p & 255) * ia + db * al);
                Pix[id] = unchecked((int)0xFF000000) | (r << 16) | (g << 8) | bl;
            }
        }
    }
}