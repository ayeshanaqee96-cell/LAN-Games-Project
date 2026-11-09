using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace LanGames;

public partial class Form1 : Form
{
    private sealed record GameInfo(string Id, string Title, string Tag, string Desc, Color C1, Color C2, int Icon);

    private static readonly GameInfo[] Games =
    {
        new("ludo", "Ludo", "2 - 4 players  |  sound effects",
            "Classic dice race. Roll, capture opponents and bring all four tokens home first. Up to 4 friends can play together on your LAN.",
            Color.FromArgb(249, 115, 22), Color.FromArgb(219, 39, 119), 0),
        new("chess", "Chess", "2 players  |  live chat",
            "Realistic wooden board with 3D-style pieces, full chess rules, a move list and a chat to talk with your opponent.",
            Color.FromArgb(34, 211, 238), Color.FromArgb(79, 70, 229), 1),
        new("ttt", "Tic-Tac-Toe", "2 players  |  quick match",
            "A fast 1v1 duel. Line up three in a row before your friend does, then play a rematch!",
            Color.FromArgb(167, 139, 250), Color.FromArgb(219, 39, 119), 2),
    };

    // ----- palette: dark neutral base, colour only on hero + the active game -----
    private static readonly Color BaseBg = Color.FromArgb(14, 15, 26);
    private static readonly Color SideBg = Color.FromArgb(11, 12, 22);
    private static readonly Color Surface = Color.FromArgb(23, 24, 40);
    private static readonly Color Field = Color.FromArgb(32, 33, 52);

    private readonly Canvas home = new() { Dock = DockStyle.Fill, BackColor = BaseBg };
    private readonly TextBox nameBox = new();
    private readonly List<(RectangleF r, string id)> hits = new();
    private string hover = "";
    private int sel;
    private int ludoPlayers = 4;

    public Form1()
    {
        InitializeComponent();
        Text = AppInfo.Name;
        ClientSize = new Size(1200, 740);
        MinimumSize = new Size(1000, 680);
        StartPosition = FormStartPosition.CenterScreen;
        Font = UI.F(10);

        // borderless textbox - the rounded field behind it is drawn in PaintHome
        nameBox.Font = UI.F(10.5f);
        nameBox.BackColor = Field;
        nameBox.ForeColor = Color.White;
        nameBox.BorderStyle = BorderStyle.None;
        nameBox.MaxLength = 18;
        nameBox.Text = "Aisha";
        nameBox.TextChanged += (s, e) => home.Invalidate();
        home.Controls.Add(nameBox);

        // no highlighted text / focus on startup
        Shown += (s, e) =>
        {
            nameBox.SelectionStart = nameBox.TextLength;
            nameBox.SelectionLength = 0;
            ActiveControl = null;
        };

        home.Paint += PaintHome;
        home.MouseMove += (s, e) =>
        {
            string h = HitAt(e.Location);
            if (h == hover) return;
            hover = h;
            home.Cursor = h == "" ? Cursors.Default : Cursors.Hand;
            home.Invalidate();
        };
        home.MouseLeave += (s, e) => { hover = ""; home.Invalidate(); };
        home.MouseClick += (s, e) => Act(HitAt(e.Location));
        Controls.Add(home);
    }

    private string PlayerName => string.IsNullOrWhiteSpace(nameBox.Text) ? "Player" : nameBox.Text.Trim().Replace("|", " ");

    private string HitAt(Point p)
    {
        for (int i = hits.Count - 1; i >= 0; i--)
            if (hits[i].r.Contains(p)) return hits[i].id;
        return "";
    }

    private void Act(string id)
    {
        if (id == "") return;
        Sfx.Play("click");
        if (id.StartsWith("sel") || id.StartsWith("nav")) sel = int.Parse(id.Substring(3));
        else if (id.StartsWith("pl")) ludoPlayers = int.Parse(id.Substring(2));
        else if (id == "host") StartHost(Games[sel].Id, sel == 0 ? ludoPlayers : 2);
        else if (id == "join") JoinGame(Games[sel].Id, Games[sel].Title);
        else if (id == "sound") Sfx.Muted = !Sfx.Muted;
        else if (id == "about")
            MessageBox.Show(this,
                $"{AppInfo.Name}\n\n{AppInfo.Credits}\n\nGames: Ludo (up to 4 players), Chess (with live chat), Tic-Tac-Toe\nNetwork: TCP game connection + UDP discovery on your LAN.",
                "About", MessageBoxButtons.OK, MessageBoxIcon.Information);
        home.Invalidate();
    }

    private static readonly string[] Taglines = { "Classic dice race", "Strategy duel", "Quick 1v1 match" };
    private static readonly int[][] Stats = { new[] { 6, 3, 9 }, new[] { 4, 10, 2 }, new[] { 9, 5, 3 } };
    private static readonly string[] StatNames = { "Speed", "Strategy", "Luck" };

    // angular HUD panel: top-left and bottom-right corners are cut
    private static GraphicsPath Cut(RectangleF r, float c)
    {
        var p = new GraphicsPath();
        p.AddPolygon(new[]
        {
            new PointF(r.X + c, r.Y), new PointF(r.Right, r.Y), new PointF(r.Right, r.Bottom - c),
            new PointF(r.Right - c, r.Bottom), new PointF(r.X, r.Bottom), new PointF(r.X, r.Y + c)
        });
        return p;
    }

    private static Font Hud(float size) => new Font("Bahnschrift", size, FontStyle.Bold);

    // Semibold for headings, regular for body
    private static Font Fn(float size, FontStyle st) =>
        st.HasFlag(FontStyle.Bold) ? new Font("Segoe UI Semibold", size, FontStyle.Regular) : new Font("Segoe UI", size, st);

    // ================= drawing =================
    private void PaintHome(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        hits.Clear();
        float W = home.Width, H = home.Height;
        if (W < 300 || H < 300) return;
        var gm = Games[sel];
        var center = StringAlignment.Center;

        void Fill(RectangleF r, float rad, Color c)
        {
            using var p = UI.Round(r, rad);
            using var b = new SolidBrush(c);
            g.FillPath(b, p);
        }
        void Stroke(RectangleF r, float rad, Color c, float w)
        {
            using var p = UI.Round(r, rad);
            using var pen = new Pen(c, w);
            g.DrawPath(pen, p);
        }
        void Grad(RectangleF r, float rad, Color a, Color b2, float ang)
        {
            using var p = UI.Round(r, rad);
            using var b = new LinearGradientBrush(r, a, b2, ang);
            g.FillPath(b, p);
        }
        void Txt(string s, float size, FontStyle st, Color c, RectangleF r, StringAlignment ha = StringAlignment.Near, StringAlignment va = StringAlignment.Near)
        {
            using var f = Fn(size, st);
            using var b = new SolidBrush(c);
            using var sf = new StringFormat { Alignment = ha, LineAlignment = va, Trimming = StringTrimming.EllipsisCharacter };
            g.DrawString(s, f, b, r, sf);
        }
        void Hit(RectangleF r, string id) => hits.Add((r, id));
        void Btn(RectangleF r, string text, string id, Color fill, Color fg, bool outline)
        {
            Color f = hover == id ? UI.Lighten(fill, .14f) : fill;
            if (fill.A < 255 && hover == id) f = Color.FromArgb(Math.Min(255, fill.A + 28), fill.R, fill.G, fill.B);
            Fill(r, 12, f);
            if (outline) Stroke(r, 12, Color.FromArgb(hover == id ? 255 : 200, 255, 255, 255), 2);
            Txt(text, 11, FontStyle.Bold, fg, r, center, center);
            Hit(r, id);
        }

        // ---------- background: flat dark + one soft accent glow ----------
        g.Clear(BaseBg);
        using (var glow = new SolidBrush(Color.FromArgb(22, gm.C1)))
            g.FillEllipse(glow, W * .50f, -H * .45f, W * .70f, H * .80f);

        // ---------- sidebar ----------
        const float SB = 232;
        using (var sbb = new SolidBrush(SideBg)) g.FillRectangle(sbb, 0, 0, SB, H);
        using (var line = new Pen(Color.FromArgb(22, 255, 255, 255))) g.DrawLine(line, SB, 0, SB, H);

        Grad(new RectangleF(24, 28, 40, 40), 12, UI.Cyan, UI.Purple, 45f);
        Txt("L", 17, FontStyle.Bold, Color.White, new RectangleF(24, 28, 40, 40), center, center);
        Txt("LAN Games", 14, FontStyle.Bold, Color.White, new RectangleF(76, 28, 150, 22));
        Txt("Arena", 10, FontStyle.Regular, UI.Cyan, new RectangleF(77, 50, 150, 18));

        Txt("Games", 9.5f, FontStyle.Bold, UI.Muted, new RectangleF(26, 106, 150, 18));
        for (int i = 0; i < Games.Length; i++)
        {
            var r = new RectangleF(14, 132 + i * 54, SB - 28, 46);
            string id = "nav" + i;
            var ac = Games[i].C1;
            if (i == sel)
            {
                Fill(r, 12, Color.FromArgb(38, ac));
                Fill(new RectangleF(r.X, r.Y + 10, 4, r.Height - 20), 2, ac);
            }
            else if (hover == id) Fill(r, 12, Color.FromArgb(16, 255, 255, 255));
            var tile = new RectangleF(r.X + 14, r.Y + 9, 28, 28);
            Fill(tile, 8, ac);
            Txt(Games[i].Title.Substring(0, 1), 11, FontStyle.Bold, Color.White, tile, center, center);
            Txt(Games[i].Title, 11, FontStyle.Bold, i == sel ? Color.White : Color.FromArgb(205, 255, 255, 255),
                new RectangleF(r.X + 54, r.Y, r.Width - 56, r.Height), StringAlignment.Near, center);
            Hit(r, id);
        }

        float by = H - 236;
        Btn(new RectangleF(14, by, SB - 28, 40), Sfx.Muted ? "Sound: off" : "Sound: on", "sound", Color.FromArgb(18, 255, 255, 255), Color.White, false);
        Btn(new RectangleF(14, by + 48, SB - 28, 40), "About", "about", Color.FromArgb(18, 255, 255, 255), Color.White, false);

        // profile card
        var prof = new RectangleF(14, H - 138, SB - 28, 120);
        Fill(prof, 14, Surface);
        Stroke(prof, 14, Color.FromArgb(18, 255, 255, 255), 1);
        var av = new RectangleF(prof.X + 14, prof.Y + 14, 38, 38);
        Grad(av, 19, UI.Cyan, UI.Purple, 45f);
        Txt(PlayerName.Substring(0, 1).ToUpper(), 14, FontStyle.Bold, Color.White, av, center, center);
        Txt("Your name", 9.5f, FontStyle.Bold, UI.Muted, new RectangleF(prof.X + 62, prof.Y + 14, 120, 18));
        using (var dot = new SolidBrush(UI.Green)) g.FillEllipse(dot, prof.X + 63, prof.Y + 38, 8, 8);
        Txt("Online on LAN", 9.5f, FontStyle.Regular, UI.Green, new RectangleF(prof.X + 76, prof.Y + 32, 110, 18));

        var field = new RectangleF(prof.X + 14, prof.Y + 68, prof.Width - 28, 36);
        Fill(field, 10, Field);
        Stroke(field, 10, Color.FromArgb(nameBox.Focused ? 160 : 24, nameBox.Focused ? gm.C1 : Color.White), 1);
        int nx = (int)field.X + 12, nw = (int)field.Width - 24;
        int ny = (int)field.Y + (int)((field.Height - nameBox.Height) / 2);
        if (nameBox.Left != nx || nameBox.Top != ny || nameBox.Width != nw) nameBox.SetBounds(nx, ny, nw, nameBox.Height);

        // ---------- main area ----------
        float mx = SB + 40, mw = W - SB - 80;
        Txt($"Welcome back, {PlayerName}", 22, FontStyle.Bold, Color.White, new RectangleF(mx, 26, mw, 36));
        Txt("Pick a game, host a room or join your friends on the same Wi-Fi / LAN.", 11, FontStyle.Regular, UI.Muted, new RectangleF(mx, 64, mw, 24));

        // ---------- HUD stage: lobby | game art | stats ----------
        const float cardH = 112;
        float cy = H - 52 - cardH;
        float stageY = 106;
        float stageH = Math.Max(300, cy - 38 - 18 - stageY);
        Color acc = gm.C1;
        var lp = new RectangleF(mx, stageY, 210, stageH);
        var rp = new RectangleF(mx + mw - 250, stageY, 250, stageH);
        var cp = new RectangleF(lp.Right + 16, stageY, rp.X - 16 - (lp.Right + 16), stageH);

        void Panel(RectangleF r, float c, Color fill, Color border, float bw)
        {
            using var p = Cut(r, c);
            using var b = new SolidBrush(fill);
            g.FillPath(b, p);
            using var pen = new Pen(border, bw);
            g.DrawPath(pen, p);
        }
        void Ticks(RectangleF r, Color c)
        {
            using var b = new SolidBrush(c);
            g.FillRectangle(b, r.Right - 6, r.Y, 6, 3);
            g.FillRectangle(b, r.X, r.Bottom - 3, 6, 3);
        }
        void HudTxt(string s, float size, Color c, RectangleF r, StringAlignment ha = StringAlignment.Near, StringAlignment va = StringAlignment.Near)
        {
            using var f = Hud(size);
            using var b = new SolidBrush(c);
            using var sf = new StringFormat { Alignment = ha, LineAlignment = va, Trimming = StringTrimming.EllipsisCharacter };
            g.DrawString(s, f, b, r, sf);
        }

        // --- left: lobby slots (player "pics") ---
        Panel(lp, 14, Color.FromArgb(200, 18, 19, 32), Color.FromArgb(40, 255, 255, 255), 1);
        Ticks(lp, acc);
        HudTxt("LOBBY", 10, UI.Muted, new RectangleF(lp.X + 16, lp.Y + 12, 120, 20));
        int slotsShown = sel == 0 ? 4 : 2;
        int slotsOn = sel == 0 ? ludoPlayers : 2;
        float slotH = Math.Min(64, (lp.Height - 52 - 8 * (slotsShown - 1)) / slotsShown);
        for (int i = 0; i < slotsShown; i++)
        {
            var sr = new RectangleF(lp.X + 12, lp.Y + 42 + i * (slotH + 8), lp.Width - 24, slotH);
            bool active = i < slotsOn, you = i == 0;
            string sid = (sel == 0 && i > 0) ? "pl" + (i + 1) : "";
            bool shov = sid != "" && hover == sid;
            Panel(sr, 8, active ? Color.FromArgb(you ? 60 : 34, acc) : Color.FromArgb(shov ? 30 : 12, 255, 255, 255),
                  active ? Color.FromArgb(you ? 255 : 120, acc) : Color.FromArgb(shov ? 120 : 30, 255, 255, 255), you ? 1.6f : 1);
            float ps = slotH - 16;
            var pr = new RectangleF(sr.X + 8, sr.Y + 8, ps, ps);
            if (you) Grad(pr, 6, UI.Cyan, UI.Purple, 45f);
            else Fill(pr, 6, Color.FromArgb(active ? 70 : 28, 255, 255, 255));
            HudTxt(you ? PlayerName.Substring(0, 1).ToUpper() : (active ? "?" : "+"), 14, Color.White, pr, center, center);
            float lx = pr.Right + 10, lw = sr.Right - lx - 6;
            if (you)
            {
                Txt(PlayerName, 10.5f, FontStyle.Bold, Color.White, new RectangleF(lx, sr.Y + 10, lw, 20));
                Txt("You  |  Host", 8.5f, FontStyle.Regular, UI.Muted, new RectangleF(lx, sr.Y + 30, lw, 18));
            }
            else if (active)
            {
                Txt("Open slot", 10.5f, FontStyle.Bold, Color.FromArgb(220, 255, 255, 255), new RectangleF(lx, sr.Y + 10, lw, 20));
                Txt("Waiting...", 8.5f, FontStyle.Regular, UI.Muted, new RectangleF(lx, sr.Y + 30, lw, 18));
            }
            else
            {
                Txt("Add player", 10, FontStyle.Regular, UI.Muted, new RectangleF(lx, sr.Y, lw, sr.Height), StringAlignment.Near, center);
            }
            if (sid != "") Hit(sr, sid);
        }

        // --- center: game art ---
        Panel(cp, 18, Color.FromArgb(235, 16, 17, 29), Color.FromArgb(110, acc), 1.4f);
        using (var cpath = Cut(cp, 18))
        {
            g.SetClip(cpath);
            float artS = Math.Clamp(cp.Height - 190, 100, 190);
            var art = new RectangleF(cp.X + (cp.Width - artS) / 2, cp.Y + 100, artS, artS);
            using (var gl1 = new SolidBrush(Color.FromArgb(34, acc)))
                g.FillEllipse(gl1, art.X - artS * .6f, art.Y - artS * .6f, artS * 2.2f, artS * 2.2f);
            using (var gl2 = new SolidBrush(Color.FromArgb(40, acc)))
                g.FillEllipse(gl2, art.X - artS * .25f, art.Y - artS * .25f, artS * 1.5f, artS * 1.5f);
            using (var lines = new Pen(Color.FromArgb(10, 255, 255, 255), 1))
                for (float lx2 = cp.X - cp.Height; lx2 < cp.Right; lx2 += 26)
                    g.DrawLine(lines, lx2, cp.Bottom, lx2 + cp.Height, cp.Y);
            using (var ring = new Pen(Color.FromArgb(90, acc), 1.5f))
                g.DrawEllipse(ring, art.X - 22, art.Y - 22, artS + 44, artS + 44);
            DrawIcon(g, gm.Icon, art);
            g.ResetClip();
        }
        Ticks(cp, acc);
        HudTxt(gm.Title.ToUpper(), 30, Color.White, new RectangleF(cp.X, cp.Y + 18, cp.Width, 46), center, center);
        Txt(Taglines[sel], 10.5f, FontStyle.Regular, UI.Muted, new RectangleF(cp.X, cp.Y + 64, cp.Width, 22), center, center);
        Txt(gm.Desc, 9.5f, FontStyle.Regular, Color.FromArgb(190, 255, 255, 255),
            new RectangleF(cp.X + 40, cp.Bottom - 56, cp.Width - 80, 44), center, StringAlignment.Far);

        // --- right: stats + actions ---
        Panel(rp, 14, Color.FromArgb(200, 18, 19, 32), Color.FromArgb(40, 255, 255, 255), 1);
        Ticks(rp, acc);
        float rx = rp.X + 18, rw = rp.Width - 36;
        Txt("Players", 10, FontStyle.Regular, UI.Muted, new RectangleF(rx, rp.Y + 14, rw, 18));
        HudTxt(sel == 0 ? ludoPlayers.ToString() : "2", 36, Color.White, new RectangleF(rx, rp.Y + 30, rw, 52));
        for (int si = 0; si < 3; si++)
        {
            float sy = rp.Y + 96 + si * 32;
            Txt(StatNames[si], 9.5f, FontStyle.Regular, Color.FromArgb(210, 255, 255, 255), new RectangleF(rx, sy, rw - 30, 18));
            Txt(Stats[sel][si].ToString(), 9.5f, FontStyle.Bold, Color.White, new RectangleF(rx, sy, rw, 18), StringAlignment.Far);
            using (var tr = new SolidBrush(Color.FromArgb(40, 255, 255, 255))) g.FillRectangle(tr, rx, sy + 21, rw, 3);
            using (var fb = new SolidBrush(acc)) g.FillRectangle(fb, rx, sy + 21, rw * Stats[sel][si] / 10f, 3);
        }
        Txt(gm.Tag, 9, FontStyle.Regular, UI.Muted, new RectangleF(rx, rp.Y + 96 + 3 * 32 + 4, rw, 20));

        void HudBtn(RectangleF r, string text, string id, bool filled)
        {
            bool hv2 = hover == id;
            using (var p = Cut(r, 8))
            {
                using var b = new SolidBrush(filled ? (hv2 ? UI.Lighten(acc, .2f) : acc) : Color.FromArgb(hv2 ? 50 : 0, acc));
                g.FillPath(b, p);
                if (!filled) { using var pen = new Pen(acc, 1.6f); g.DrawPath(pen, p); }
            }
            HudTxt(text.ToUpper(), 10, filled ? Color.FromArgb(14, 15, 26) : Color.White, r, center, center);
            Hit(r, id);
        }
        float bw2 = (rw - 10) / 2, bY = rp.Bottom - 16 - 46;
        HudBtn(new RectangleF(rx, bY, bw2, 46), "Host", "host", true);
        HudBtn(new RectangleF(rx + bw2 + 10, bY, bw2, 46), "Join", "join", false);

        // game cards: dark surface, accent only on tile + active border
        Txt("All games", 14, FontStyle.Bold, Color.White, new RectangleF(mx, cy - 38, 300, 26));
        float gap = 20, cw = (mw - gap * 2) / 3;
        for (int i = 0; i < Games.Length; i++)
        {
            var gi = Games[i];
            var slot = new RectangleF(mx + i * (cw + gap), cy, cw, cardH);
            string id = "sel" + i;
            bool on = i == sel, hv = hover == id;
            var r = hv && !on ? new RectangleF(slot.X, slot.Y - 4, slot.Width, slot.Height) : slot; // hover lift

            if (on || hv)
                Fill(new RectangleF(r.X - 5, r.Y - 5, r.Width + 10, r.Height + 10), 24, Color.FromArgb(on ? 34 : 20, gi.C1));
            Fill(r, 18, Surface);
            Stroke(r, 18, on ? gi.C1 : Color.FromArgb(hv ? 110 : 22, hv ? gi.C1 : Color.White), on ? 2.5f : 1);

            float ts = Math.Min(cardH - 48, 84);
            var tile = new RectangleF(r.X + 22, r.Y + (cardH - ts) / 2, ts, ts);
            Fill(tile, 16, Color.FromArgb(on ? 255 : 70, gi.C1));
            float pad = ts * .18f;
            DrawIcon(g, gi.Icon, new RectangleF(tile.X + pad, tile.Y + pad, ts - pad * 2, ts - pad * 2));

            float txx = tile.Right + 18;
            Txt(gi.Title, 15, FontStyle.Bold, Color.White, new RectangleF(txx, r.Y + cardH / 2 - 30, r.Right - txx - 10, 28));
            Txt(gi.Tag, 9.5f, FontStyle.Regular, UI.Muted, new RectangleF(txx, r.Y + cardH / 2 + 0, r.Right - txx - 10, 40));
            Hit(slot, id);
        }

        Txt(AppInfo.Credits, 9, FontStyle.Regular, Color.FromArgb(130, 255, 255, 255), new RectangleF(SB, H - 32, W - SB, 22), center, center);
    }

    private static void DrawIcon(Graphics g, int kind, RectangleF r)
    {
        float u = r.Width / 100f;
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        switch (kind)
        {
            case 0: // dice
            {
                using var p = UI.Round(r, 20 * u);
                g.FillPath(Brushes.White, p);
                using var dark = new SolidBrush(Color.FromArgb(60, 20, 90));
                foreach (var (gx, gy) in new[] { (0, 0), (2, 0), (1, 1), (0, 2), (2, 2) })
                    g.FillEllipse(dark, r.X + r.Width * (0.25f + 0.25f * gx) - 8 * u, r.Y + r.Height * (0.25f + 0.25f * gy) - 8 * u, 16 * u, 16 * u);
                break;
            }
            case 1: // chess knight
            {
                using var f = new Font("Segoe UI Symbol", 84f * u, GraphicsUnit.Pixel);
                g.DrawString("\u265E", f, Brushes.White, r, sf);
                break;
            }
            default: // tic tac toe
            {
                using var pen = new Pen(Color.White, 7 * u) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                float a = r.X + r.Width / 3f, b = r.X + 2 * r.Width / 3f, c = r.Y + r.Height / 3f, d = r.Y + 2 * r.Height / 3f;
                g.DrawLine(pen, a, r.Y, a, r.Bottom);
                g.DrawLine(pen, b, r.Y, b, r.Bottom);
                g.DrawLine(pen, r.X, c, r.Right, c);
                g.DrawLine(pen, r.X, d, r.Right, d);
                using var xp = new Pen(Color.FromArgb(255, 230, 120), 6 * u) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                float m = 6 * u;
                g.DrawLine(xp, r.X + m, r.Y + m, a - m, c - m);
                g.DrawLine(xp, a - m, r.Y + m, r.X + m, c - m);
                g.DrawEllipse(xp, a + m, c + m, b - a - 2 * m, d - c - 2 * m);
                break;
            }
        }
    }

    // ================= start / join =================
    private GameForm CreateGame(string id, NetworkPeer peer, bool host, int max) => id switch
    {
        "ludo" => new LudoForm(peer, PlayerName, host, max),
        "chess" => new ChessForm(peer, PlayerName, host),
        _ => new TicTacToeForm(peer, PlayerName, host)
    };

    private async void StartHost(string id, int max)
    {
        var peer = new NetworkPeer();
        var game = CreateGame(id, peer, true, max);
        game.Show();
        try
        {
            await peer.StartHostAsync(PlayerName, id, max);
        }
        catch (Exception ex)
        {
            game.Close();
            MessageBox.Show(this, "Could not start hosting:\n" + ex.Message + "\n\n(Is another game already hosted on this PC? Also check the firewall.)",
                "Host error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async void JoinGame(string id, string title)
    {
        using var dlg = new JoinDialog(id, title);
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.SelectedIp == null) return;

        var peer = new NetworkPeer();
        var game = CreateGame(id, peer, false, 2);
        game.Show();
        try
        {
            await peer.ConnectAsync(dlg.SelectedIp);
        }
        catch (Exception ex)
        {
            game.Close();
            MessageBox.Show(this, "Could not connect:\n" + ex.Message, "Join error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // main window must stay open while a game is running
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (GameForm.ActiveGames > 0 && e.CloseReason != CloseReason.WindowsShutDown)
        {
            e.Cancel = true;
            Sfx.Play("error");
            MessageBox.Show(this, "You cannot close the application while a game is running.", "Game in progress",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        base.OnFormClosing(e);
    }
}