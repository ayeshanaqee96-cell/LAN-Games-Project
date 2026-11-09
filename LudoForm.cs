using System.Drawing.Drawing2D;
using System.Drawing.Text;
using WTimer = System.Windows.Forms.Timer;

namespace LanGames;

/// <summary>
/// 15x15 Ludo board. Colors: 0 Red (left), 1 Green (top), 2 Yellow (right), 3 Blue (bottom).
/// Token progress "rel": -1 = in base, 0..50 = on the 52-cell track (relative to own start),
/// 51..55 = home column, 56 = finished.
/// </summary>
public class LudoBoard : Canvas
{
    public static readonly Point[] Track = BuildTrack();
    public static readonly Point[][] Home =
    {
        Enumerable.Range(1, 5).Select(x => new Point(x, 7)).ToArray(),        // red
        Enumerable.Range(1, 5).Select(y => new Point(7, y)).ToArray(),        // green
        Enumerable.Range(0, 5).Select(i => new Point(13 - i, 7)).ToArray(),   // yellow
        Enumerable.Range(0, 5).Select(i => new Point(7, 13 - i)).ToArray(),   // blue
    };
    public static readonly int[] Start = { 0, 13, 26, 39 };
    public static readonly Point[] Quad = { new(0, 0), new(9, 0), new(9, 9), new(0, 9) };
    public static readonly Color[] Col =
    {
        Color.FromArgb(220, 38, 38), Color.FromArgb(22, 163, 74), Color.FromArgb(234, 179, 8), Color.FromArgb(37, 99, 235)
    };
    public static readonly string[] ColName = { "Red", "Green", "Yellow", "Blue" };
    private static readonly (int x, int y)[] Dir = { (-1, 0), (0, -1), (1, 0), (0, 1) };

    public static bool IsSafe(int abs) => abs % 13 == 0 || abs % 13 == 8;

    private static Point[] BuildTrack()
    {
        var t = new List<Point>();
        for (int x = 1; x <= 5; x++) t.Add(new Point(x, 6));
        for (int y = 5; y >= 0; y--) t.Add(new Point(6, y));
        t.Add(new Point(7, 0)); t.Add(new Point(8, 0));
        for (int y = 1; y <= 5; y++) t.Add(new Point(8, y));
        for (int x = 9; x <= 14; x++) t.Add(new Point(x, 6));
        t.Add(new Point(14, 7)); t.Add(new Point(14, 8));
        for (int x = 13; x >= 9; x--) t.Add(new Point(x, 8));
        for (int y = 9; y <= 14; y++) t.Add(new Point(8, y));
        t.Add(new Point(7, 14)); t.Add(new Point(6, 14));
        for (int y = 13; y >= 9; y--) t.Add(new Point(6, y));
        for (int x = 5; x >= 0; x--) t.Add(new Point(x, 8));
        t.Add(new Point(0, 7)); t.Add(new Point(0, 6));
        return t.ToArray();   // 52 cells
    }

    /// <summary>Center of a token in board-cell units.</summary>
    public static PointF Pos(int c, int rel, int k)
    {
        if (rel < 0) return new PointF(Quad[c].X + 2 + (k % 2) * 2, Quad[c].Y + 2 + (k / 2) * 2);
        if (rel <= 50) { var p = Track[(Start[c] + rel) % 52]; return new PointF(p.X + .5f, p.Y + .5f); }
        if (rel <= 55) { var p = Home[c][rel - 51]; return new PointF(p.X + .5f, p.Y + .5f); }
        var d = Dir[c];
        float off = (k - 1.5f) * .3f;
        return new PointF(7.5f + d.x * .95f + (-d.y) * off, 7.5f + d.y * .95f + d.x * off);
    }

    // ----- state shown by the board (set by LudoForm) -----
    public int[][] Tok = Enumerable.Range(0, 4).Select(_ => new[] { -1, -1, -1, -1 }).ToArray();
    public bool[] Active = new bool[4];
    public int MovableColor = -1;
    public List<int> MovableTokens = new();
    public int AnimC = -1, AnimK, AnimRel;
    public Action<int, int>? TokenClicked;

    private float phase;
    private readonly WTimer pulse = new() { Interval = 60 };

    public LudoBoard()
    {
        pulse.Tick += (s, e) => { phase += .3f; if (MovableTokens.Count > 0) Invalidate(); };
        pulse.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) pulse.Dispose();
        base.Dispose(disposing);
    }

    private int VisRel(int c, int k) => c == AnimC && k == AnimK ? AnimRel : Tok[c][k];

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (MovableColor < 0 || MovableTokens.Count == 0) return;
        float cs = Math.Min(Width, Height) / 15f;
        int best = -1;
        float bd = cs * .7f;
        foreach (int k in MovableTokens)
        {
            var p = Pos(MovableColor, VisRel(MovableColor, k), k);
            float d = (float)Math.Sqrt(Math.Pow(e.X - p.X * cs, 2) + Math.Pow(e.Y - p.Y * cs, 2));
            if (d < bd) { bd = d; best = k; }
        }
        if (best >= 0) TokenClicked?.Invoke(MovableColor, best);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        float cs = Math.Min(Width, Height) / 15f;
        if (cs < 5) return;
        DrawBoard(g, cs);
        DrawTokens(g, cs);
    }

    private void DrawBoard(Graphics g, float cs)
    {
        using var boardPath = UI.Round(new RectangleF(0, 0, 15 * cs, 15 * cs), cs * .6f);
        g.SetClip(boardPath);
        g.FillRectangle(Brushes.White, 0, 0, 15 * cs, 15 * cs);

        // four corner yards
        for (int c = 0; c < 4; c++)
        {
            var q = Quad[c];
            var rect = new RectangleF(q.X * cs, q.Y * cs, 6 * cs, 6 * cs);
            Color baseCol = Active[c] ? Col[c] : Color.FromArgb(120, 130, 150);
            using (var br = new LinearGradientBrush(rect, UI.Lighten(baseCol, .12f), UI.Darken(baseCol, .18f), 45f))
                g.FillRectangle(br, rect);
            using (var ip = UI.Round(new RectangleF((q.X + 1) * cs, (q.Y + 1) * cs, 4 * cs, 4 * cs), cs * .5f))
                g.FillPath(Brushes.White, ip);
            for (int k = 0; k < 4; k++)
            {
                var pt = Pos(c, -1, k);
                using var sb = new SolidBrush(UI.Lighten(baseCol, .55f));
                using var pn = new Pen(baseCol, 2);
                g.FillEllipse(sb, (pt.X - .6f) * cs, (pt.Y - .6f) * cs, 1.2f * cs, 1.2f * cs);
                g.DrawEllipse(pn, (pt.X - .6f) * cs, (pt.Y - .6f) * cs, 1.2f * cs, 1.2f * cs);
            }
        }

        // track cells
        using var grid = new Pen(Color.FromArgb(205, 210, 222), 1f);
        using var starFont = new Font("Segoe UI Symbol", cs * .55f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        for (int i = 0; i < 52; i++)
        {
            var t = Track[i];
            var r = new RectangleF(t.X * cs, t.Y * cs, cs, cs);
            Color fill = Color.White;
            bool isStart = false;
            for (int c = 0; c < 4; c++) if (Start[c] == i) { fill = Col[c]; isStart = true; }
            using (var fb = new SolidBrush(fill)) g.FillRectangle(fb, r);
            g.DrawRectangle(grid, r.X, r.Y, r.Width, r.Height);
            if (IsSafe(i))
                g.DrawString("\u2605", starFont, isStart ? Brushes.White : Brushes.Gray, r, sf);
        }

        // home columns
        for (int c = 0; c < 4; c++)
            foreach (var h in Home[c])
            {
                using var fb = new SolidBrush(UI.Lighten(Col[c], .05f));
                g.FillRectangle(fb, h.X * cs, h.Y * cs, cs, cs);
                g.DrawRectangle(grid, h.X * cs, h.Y * cs, cs, cs);
            }

        // center triangles
        PointF C(float x, float y) => new(x * cs, y * cs);
        var mid = C(7.5f, 7.5f);
        var tris = new[]
        {
            new[] { C(6, 6), C(6, 9), mid },   // red
            new[] { C(6, 6), C(9, 6), mid },   // green
            new[] { C(9, 6), C(9, 9), mid },   // yellow
            new[] { C(6, 9), C(9, 9), mid },   // blue
        };
        for (int c = 0; c < 4; c++)
        {
            using var tb = new SolidBrush(Active[c] ? Col[c] : Color.FromArgb(150, 160, 175));
            g.FillPolygon(tb, tris[c]);
        }

        g.ResetClip();
        using var border = new Pen(Color.FromArgb(30, 41, 59), 3);
        g.DrawPath(border, boardPath);
    }

    private void DrawTokens(Graphics g, float cs)
    {
        var items = new List<(int c, int k, PointF p, int rel, float scale)>();
        for (int c = 0; c < 4; c++)
        {
            if (!Active[c]) continue;
            for (int k = 0; k < 4; k++)
            {
                int rel = VisRel(c, k);
                items.Add((c, k, Pos(c, rel, k), rel, rel >= 56 ? .55f : 1f));
            }
        }

        // spread tokens that share a cell
        var basePos = items.Select(i => i.p).ToArray();
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            if (it.rel < 0 || it.rel >= 56) continue;
            int cnt = 0, idx = 0;
            for (int j = 0; j < items.Count; j++)
            {
                if (items[j].rel < 0 || items[j].rel >= 56) continue;
                if (Math.Abs(basePos[j].X - basePos[i].X) < .01f && Math.Abs(basePos[j].Y - basePos[i].Y) < .01f)
                {
                    if (j < i) idx++;
                    cnt++;
                }
            }
            if (cnt > 1)
            {
                float off = (idx - (cnt - 1) / 2f) * .24f;
                it.p = new PointF(it.p.X + off, it.p.Y - off * .3f);
                it.scale = .78f;
                items[i] = it;
            }
        }

        foreach (var it in items.OrderBy(t => t.p.Y))
        {
            bool hl = AnimC < 0 && MovableColor == it.c && MovableTokens.Contains(it.k);
            DrawToken(g, cs, it.p, .38f * it.scale, Col[it.c], hl);
        }
    }

    private void DrawToken(Graphics g, float cs, PointF pos, float radius, Color col, bool highlight)
    {
        float cx = pos.X * cs, cy = pos.Y * cs, rad = radius * cs;
        using (var sh = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
            g.FillEllipse(sh, cx - rad * .9f, cy + rad * .45f, rad * 1.8f, rad * .8f);

        using (var gp = new GraphicsPath())
        {
            gp.AddEllipse(cx - rad, cy - rad, rad * 2, rad * 2);
            using var pg = new PathGradientBrush(gp)
            {
                CenterPoint = new PointF(cx - rad * .35f, cy - rad * .4f),
                CenterColor = UI.Lighten(col, .55f),
                SurroundColors = new[] { UI.Darken(col, .25f) }
            };
            g.FillPath(pg, gp);
        }
        using (var ring = new Pen(Color.White, Math.Max(2f, rad * .18f))) g.DrawEllipse(ring, cx - rad, cy - rad, rad * 2, rad * 2);
        using (var inner = new Pen(UI.Darken(col, .35f), 1.5f)) g.DrawEllipse(inner, cx - rad * .55f, cy - rad * .55f, rad * 1.1f, rad * 1.1f);
        using (var shine = new SolidBrush(Color.FromArgb(120, 255, 255, 255)))
            g.FillEllipse(shine, cx - rad * .55f, cy - rad * .78f, rad * .75f, rad * .45f);

        if (highlight)
        {
            float a = .5f + .5f * (float)Math.Sin(phase);
            using var glow = new Pen(Color.FromArgb((int)(110 + 130 * a), 255, 235, 90), 3.5f);
            g.DrawEllipse(glow, cx - rad - 4, cy - rad - 4, rad * 2 + 8, rad * 2 + 8);
        }
    }
}

public class LudoForm : GameForm
{
    private static readonly int[][] Pips =
    {
        Array.Empty<int>(), new[] { 4 }, new[] { 0, 8 }, new[] { 0, 4, 8 },
        new[] { 0, 2, 6, 8 }, new[] { 0, 2, 4, 6, 8 }, new[] { 0, 2, 3, 5, 6, 8 }
    };

    private readonly LudoBoard board = new() { BackColor = UI.Bg };
    private readonly Canvas boardHost = new() { Dock = DockStyle.Fill, BackColor = UI.Bg };
    private readonly Canvas side = new() { Dock = DockStyle.Right, Width = 320, BackColor = UI.Panel };
    private readonly Canvas players = new() { Location = new Point(10, 12), Size = new Size(300, 280), BackColor = UI.Panel };
    private readonly Canvas diceBox = new() { Location = new Point(95, 304), Size = new Size(130, 130), BackColor = UI.Panel };
    private readonly ModernButton rollBtn = new() { Text = "ROLL DICE", Accent = UI.Orange, Location = new Point(30, 450), Size = new Size(260, 56), Font = UI.F(14, FontStyle.Bold), Enabled = false };
    private readonly Label status = new() { Location = new Point(14, 522), Size = new Size(292, 110), TextAlign = ContentAlignment.TopCenter, ForeColor = UI.Text, Font = UI.F(11) };
    private readonly Random rng = new();
    private readonly Queue<string[]> pending = new();
    private readonly WTimer animTimer = new() { Interval = 140 };

    private int n = 2, turn, dice, shown, rollerColor;
    private int[] seatColor = { 0, 2 };
    private bool rolling, animating;
    private int animC, animTarget;
    private string lastInfo = "";

    public LudoForm(NetworkPeer peer, string name, bool host, int maxPlayers)
        : base(peer, name, host, maxPlayers, "Ludo", UI.Orange)
    {
        ClientSize = new Size(1100, 790);
        MinimumSize = new Size(980, 700);

        board.TokenClicked = (c, k) =>
        {
            if (animating || GameOver || dice == 0 || turn != Peer.MySeat || c != seatColor[turn]) return;
            LocalMove(k);
        };
        boardHost.Controls.Add(board);
        boardHost.Resize += (s, e) => FitBoard();

        players.Paint += PaintPlayers;
        diceBox.Paint += PaintDice;
        rollBtn.Click += (s, e) => RollClicked();
        animTimer.Tick += (s, e) => AnimTick();

        side.Controls.AddRange(new Control[] { players, diceBox, rollBtn, status });
        Body.Controls.Add(boardHost);
        Body.Controls.Add(side);
        Shown += (s, e) => FitBoard();
    }

    private void FitBoard()
    {
        int s = Math.Max(250, Math.Min(boardHost.Width, boardHost.Height) - 16);
        board.Size = new Size(s, s);
        board.Location = new Point((boardHost.Width - s) / 2, (boardHost.Height - s) / 2);
    }

    protected override void OnGameStart()
    {
        n = Math.Clamp(Peer.MaxPlayers, 2, 4);
        seatColor = n == 2 ? new[] { 0, 2 } : n == 3 ? new[] { 0, 1, 2 } : new[] { 0, 1, 2, 3 };
        foreach (int c in seatColor) board.Active[c] = true;
        turn = 0; dice = 0; shown = 0; rollerColor = seatColor[0];
        lastInfo = "";
        FitBoard();
        RefreshUi();
    }

    protected override void OnNamesChanged() { players.Invalidate(); RefreshUi(); }

    // ---------- rolling ----------
    private void RollClicked()
    {
        if (!Started || GameOver || rolling || animating || dice != 0 || turn != Peer.MySeat) return;
        rolling = true;
        Sfx.Play("dice");
        int ticks = 0;
        var tm = new WTimer { Interval = 70 };
        tm.Tick += (s, e) =>
        {
            ticks++;
            shown = rng.Next(1, 7);
            diceBox.Invalidate();
            if (ticks < 9) return;
            tm.Stop(); tm.Dispose();
            rolling = false;
            int v = rng.Next(1, 7);
            Peer.Send($"ROLL|{Peer.MySeat}|{v}");
            ApplyRoll(Peer.MySeat, v);
        };
        tm.Start();
        RefreshUi();
    }

    protected override void OnMessage(string[] p)
    {
        if (animating) { pending.Enqueue(p); return; }   // keep game logic strictly in order
        HandleMsg(p);
    }

    // (was called "Handle", which hid Control.Handle and caused warning CS0108)
    private void HandleMsg(string[] p)
    {
        if (GameOver || p.Length < 3) return;
        if (!int.TryParse(p[1], out int seat) || !int.TryParse(p[2], out int v)) return;
        if (p[0] == "ROLL") { Sfx.Play("dice"); ApplyRoll(seat, v); }
        else if (p[0] == "MOVE") ApplyMove(seat, v);
    }

    private List<int> Movable(int c, int d)
    {
        var l = new List<int>();
        for (int k = 0; k < 4; k++)
        {
            int r = board.Tok[c][k];
            if (r < 0) { if (d == 6) l.Add(k); }
            else if (r < 56 && r + d <= 56) l.Add(k);
        }
        return l;
    }

    private void ApplyRoll(int seat, int v)
    {
        if (seat != turn || dice != 0 || v < 1 || v > 6) return;
        int c = seatColor[seat];
        dice = v; shown = v; rollerColor = c;
        var mv = Movable(c, v);
        if (mv.Count == 0)
        {
            lastInfo = $"{NameOf(seat)} rolled {v} - no move possible.";
            dice = 0;
            board.MovableTokens = new List<int>();
            board.MovableColor = -1;
            turn = (turn + 1) % n;
        }
        else
        {
            lastInfo = "";
            board.MovableColor = c;
            board.MovableTokens = mv;
            if (seat == Peer.MySeat && mv.Count == 1) AutoMove(mv[0]);
        }
        RefreshUi();
    }

    private void AutoMove(int k)
    {
        var tm = new WTimer { Interval = 500 };
        tm.Tick += (s, e) =>
        {
            tm.Stop(); tm.Dispose();
            if (!GameOver && !animating && turn == Peer.MySeat && dice > 0) LocalMove(k);
        };
        tm.Start();
    }

    private void LocalMove(int k)
    {
        Peer.Send($"MOVE|{Peer.MySeat}|{k}");
        ApplyMove(Peer.MySeat, k);
    }

    // ---------- moving a token ----------
    private void ApplyMove(int seat, int k)
    {
        if (seat != turn || dice == 0 || k < 0 || k > 3) return;
        int c = seatColor[seat];
        if (!Movable(c, dice).Contains(k)) return;

        int from = board.Tok[c][k];
        int to = from < 0 ? 0 : from + dice;
        board.Tok[c][k] = to;
        board.MovableTokens = new List<int>();
        board.MovableColor = -1;

        animating = true;
        animC = c;
        animTarget = to;
        board.AnimC = c; board.AnimK = k; board.AnimRel = from;
        animTimer.Start();
        RefreshUi();
    }

    private void AnimTick()
    {
        if (board.AnimRel < animTarget)
        {
            board.AnimRel = board.AnimRel < 0 ? 0 : board.AnimRel + 1;
            Sfx.Play(board.AnimRel == animTarget ? "move" : "step");
            board.Invalidate();
        }
        if (board.AnimRel >= animTarget)
        {
            animTimer.Stop();
            board.AnimC = -1;
            FinishMove();
        }
    }

    private void FinishMove()
    {
        animating = false;
        int c = animC, to = animTarget;
        bool captured = false;

        if (to <= 50)
        {
            int abs = (LudoBoard.Start[c] + to) % 52;
            if (!LudoBoard.IsSafe(abs))
                foreach (int o in seatColor)
                {
                    if (o == c) continue;
                    for (int k = 0; k < 4; k++)
                    {
                        int r = board.Tok[o][k];
                        if (r >= 0 && r <= 50 && (LudoBoard.Start[o] + r) % 52 == abs)
                        {
                            board.Tok[o][k] = -1;
                            captured = true;
                        }
                    }
                }
        }

        if (captured) Sfx.Play("capture");
        else if (to == 56) Sfx.Play("home");

        string who = NameOf(turn);
        lastInfo = captured ? $"{who} captured a token!" : to == 56 ? $"{who} got a token home!" : "";

        bool won = board.Tok[c].All(r => r == 56);
        bool extra = dice == 6 || captured || to == 56;
        dice = 0;

        if (won)
        {
            GameOver = true;
            Sfx.Play("win");
            RefreshUi();
            MessageBox.Show(this, $"{who} ({LudoBoard.ColName[c]}) wins the game!", "Game over",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (extra) lastInfo = (lastInfo + "  Roll again!").Trim();
        else turn = (turn + 1) % n;

        RefreshUi();
        while (pending.Count > 0 && !animating) HandleMsg(pending.Dequeue());
    }

    // ---------- UI ----------
    private void RefreshUi()
    {
        bool mine = Started && !GameOver && turn == Peer.MySeat;
        rollBtn.Enabled = mine && dice == 0 && !rolling && !animating;

        string t;
        if (GameOver) t = "Game over";
        else if (!Started) t = "";
        else
        {
            t = $"{NameOf(turn)}'s turn ({LudoBoard.ColName[seatColor[turn]]})";
            if (mine) t += dice == 0 ? "\nTap ROLL DICE!" : "\nTap a glowing token";
        }
        status.Text = (lastInfo.Length > 0 ? lastInfo + "\n" : "") + t;

        diceBox.Invalidate();
        players.Invalidate();
        board.Invalidate();
    }

    private void PaintPlayers(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        using var nf = UI.F(11.5f, FontStyle.Bold);
        using var sf = UI.F(9);
        using var mb = new SolidBrush(UI.Muted);
        for (int i = 0; i < n && Started; i++)
        {
            int c = seatColor[i];
            var r = new RectangleF(0, i * 68, 298, 60);
            using (var path = UI.Round(r, 14))
            {
                using (var fb = new SolidBrush(UI.Panel2)) g.FillPath(fb, path);
                if (i == turn && !GameOver)
                    using (var pen = new Pen(LudoBoard.Col[c], 3)) g.DrawPath(pen, path);
            }
            using (var cb = new SolidBrush(LudoBoard.Col[c])) g.FillEllipse(cb, 14, r.Y + 14, 32, 32);
            int home = board.Tok[c].Count(x => x == 56);
            g.DrawString(NameOf(i) + (i == Peer.MySeat ? "  (you)" : ""), nf, Brushes.White, 58, r.Y + 8);
            g.DrawString($"{LudoBoard.ColName[c]}   |   Home: {home}/4", sf, mb, 58, r.Y + 33);
        }
    }

    private void PaintDice(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int ox = rolling ? rng.Next(-4, 5) : 0, oy = rolling ? rng.Next(-4, 5) : 0;
        int dc = rolling && Started ? seatColor[Peer.MySeat] : rollerColor;
        var r = new RectangleF(10 + ox, 10 + oy, 110, 110);

        using (var sh = new SolidBrush(Color.FromArgb(70, 0, 0, 0))) g.FillEllipse(sh, r.X + 8, r.Bottom - 6, 96, 14);
        using var path = UI.Round(r, 24);
        using (var br = new LinearGradientBrush(r, Color.White, Color.FromArgb(205, 210, 222), 60f)) g.FillPath(br, path);
        using (var pen = new Pen(LudoBoard.Col[dc], 5)) g.DrawPath(pen, path);

        if (shown == 0)
        {
            using var f = UI.F(34, FontStyle.Bold);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("?", f, Brushes.Gray, r, sf);
            return;
        }
        using var dark = new SolidBrush(Color.FromArgb(30, 41, 59));
        foreach (int idx in Pips[Math.Clamp(shown, 0, 6)])
        {
            int gx = idx % 3, gy = idx / 3;
            g.FillEllipse(dark, r.X + r.Width * (.25f + .25f * gx) - 9, r.Y + r.Height * (.25f + .25f * gy) - 9, 18, 18);
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        animTimer.Stop();
        base.OnFormClosed(e);
    }
}