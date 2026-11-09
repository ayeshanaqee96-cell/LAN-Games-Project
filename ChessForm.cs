using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace LanGames;

/// <summary>
/// Real-3D chess board. All geometry/shading is done by Software3D (Chess3D.cs);
/// this control only feeds it the position, handles the mouse and shows the result.
///   left-click = select / move      left- or right-drag = rotate camera
///   mouse wheel = zoom              right-click (no drag) = reset camera
/// </summary>
public class ChessBoard : Canvas
{
    public ChessGame Game = new();
    public (int r, int c)? Selected;
    public List<Move> Targets = new();
    public Action<int, int>? SquareClicked;

    private bool whiteBottom = true;
    public bool WhiteBottom
    {
        get => whiteBottom;
        set { whiteBottom = value; yaw = value ? 0f : MathF.PI; Touch(); }
    }

    private readonly Software3D r3 = new();
    private readonly List<PieceInst> pieces = new();
    private readonly List<Decal> decals = new();
    private Bitmap? bmp;

    private float yaw, pitch = 52f * MathF.PI / 180f, zoom = 1f;
    private (int r, int c)? hov;
    private bool hq = true;                 // next paint is the slower, anti-aliased one
    private readonly System.Windows.Forms.Timer settle = new() { Interval = 140 };

    // move animation
    private readonly System.Windows.Forms.Timer anim = new() { Interval = 15 };
    private bool animating;
    private int aFR, aFC, aTR, aTC;
    private char animPiece = '.';
    private long animT0;
    private const int AnimMs = 260;

    // mouse state
    private Point downPt, lastPt;
    private bool leftDown, rightDown, dragging;

    public ChessBoard()
    {
        SetStyle(ControlStyles.Selectable, true);
        settle.Tick += (s, e) => { settle.Stop(); hq = true; Invalidate(); };
        anim.Tick += (s, e) =>
        {
            if (Environment.TickCount64 - animT0 >= AnimMs) { animating = false; anim.Stop(); }
            Touch(.8f);
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { settle.Dispose(); anim.Dispose(); bmp?.Dispose(); }
        base.Dispose(disposing);
    }

    private float liveScale = 1f;   // resolution used while things are moving
    private void Touch(float scale = 1f) { liveScale = scale; hq = false; Invalidate(); }

    public void Animate(Move m, char piece)
    {
        aFR = m.FR; aFC = m.FC; aTR = m.TR; aTC = m.TC;
        animPiece = piece;
        animT0 = Environment.TickCount64;
        animating = true;
        anim.Start();
        Touch();
    }

    public void ResetView()
    {
        yaw = whiteBottom ? 0f : MathF.PI;
        pitch = 52f * MathF.PI / 180f;
        zoom = 1f;
        Touch();
    }

    private float Dist()
    {
        float aspect = Width / (float)Math.Max(1, Height);
        float t = MathF.Tan(Cam.FovDeg * MathF.PI / 360f);
        return Math.Max(16f, 6.4f / (t * aspect)) * zoom;
    }

    // ================= scene =================
    private static Decal D(byte shape, float x, float z, float r1, float r2, float cr, float cg, float cb, float a) =>
        new() { Shape = shape, X = x, Z = z, R1 = r1, R2 = r2, Cr = cr, Cg = cg, Cb = cb, A = a };

    private void BuildScene()
    {
        pieces.Clear(); decals.Clear();

        for (int r = 0; r < 8; r++)
            for (int c = 0; c < 8; c++)
            {
                char p = Game.B[r, c];
                if (p == '.') continue;
                if (animating && r == aTR && c == aTC) continue;   // drawn by the animation below
                bool sel = Selected.HasValue && Selected.Value.r == r && Selected.Value.c == c;
                pieces.Add(new PieceInst { Type = char.ToLower(p), White = ChessGame.IsWhite(p), X = c - 3.5f, Z = r - 3.5f, Lift = sel ? .2f : 0f });
            }

        if (animating)
        {
            float t = Math.Clamp((Environment.TickCount64 - animT0) / (float)AnimMs, 0f, 1f);
            float e = 1 - (1 - t) * (1 - t) * (1 - t);
            pieces.Add(new PieceInst
            {
                Type = char.ToLower(animPiece), White = ChessGame.IsWhite(animPiece),
                X = (aFC - 3.5f) + (aTC - aFC) * e, Z = (aFR - 3.5f) + (aTR - aFR) * e,
                Lift = MathF.Sin(t * MathF.PI) * .7f
            });
        }

        // markers on the board
        if (Game.Last is Move lm)
        {
            decals.Add(D(0, lm.FC - 3.5f, lm.FR - 3.5f, .5f, 0, 1f, .88f, .25f, .38f));
            decals.Add(D(0, lm.TC - 3.5f, lm.TR - 3.5f, .5f, 0, 1f, .88f, .25f, .38f));
        }
        if (hov.HasValue && hov != Selected)
            decals.Add(D(0, hov.Value.c - 3.5f, hov.Value.r - 3.5f, .5f, 0, 1f, 1f, 1f, .18f));
        if (Selected.HasValue)
            decals.Add(D(0, Selected.Value.c - 3.5f, Selected.Value.r - 3.5f, .5f, 0, .25f, .85f, .40f, .55f));
        var kp = Game.KingPos(Game.WhiteToMove);
        if (kp.HasValue && Game.IsInCheck(Game.WhiteToMove))
        {
            decals.Add(D(1, kp.Value.c - 3.5f, kp.Value.r - 3.5f, .62f, 0, 1f, .15f, .15f, .45f));
            decals.Add(D(2, kp.Value.c - 3.5f, kp.Value.r - 3.5f, .62f, .52f, 1f, .2f, .2f, .9f));
        }
        foreach (var m in Targets)
        {
            bool capture = Game.B[m.TR, m.TC] != '.' || (m.FC != m.TC && char.ToLower(Game.B[m.FR, m.FC]) == 'p');
            if (capture) decals.Add(D(2, m.TC - 3.5f, m.TR - 3.5f, .47f, .37f, .95f, .25f, .25f, .9f));
            else decals.Add(D(1, m.TC - 3.5f, m.TR - 3.5f, .17f, 0, .2f, .75f, .35f, .8f));
        }
    }

    // ================= painting =================
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        if (Width < 100 || Height < 100) { g.Clear(UI.Bg); return; }

        bool sharp = hq;
        float ss = sharp ? 2f : liveScale;
        if ((long)(Width * ss) * (long)(Height * ss) > 2_600_000) ss = 1f;
        int rw = Math.Max(2, (int)(Width * ss)), rh = Math.Max(2, (int)(Height * ss));

        BuildScene();
        r3.Render(rw, rh, yaw, pitch, Dist(), pieces, decals);

        if (bmp == null || bmp.Width != rw || bmp.Height != rh)
        {
            bmp?.Dispose();
            bmp = new Bitmap(rw, rh, PixelFormat.Format32bppPArgb);
        }
        var bd = bmp.LockBits(new Rectangle(0, 0, rw, rh), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        Marshal.Copy(r3.Pix, 0, bd.Scan0, rw * rh);
        bmp.UnlockBits(bd);

        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.InterpolationMode = ss > 1f ? InterpolationMode.HighQualityBilinear : (ss < 1f ? InterpolationMode.Bilinear : InterpolationMode.NearestNeighbor);
        g.DrawImage(bmp, 0, 0, Width, Height);

        DrawLabels(g);

        // quick (aliased) frames are followed by one sharp frame as soon as things stop moving
        if (!sharp) { settle.Stop(); settle.Start(); }
    }

    private void DrawLabels(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var cam = Cam.Make(Width, Height, yaw, pitch, Dist());
        using var f = UI.F(10, FontStyle.Bold);
        using var br = new SolidBrush(Color.FromArgb(232, 205, 160));
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        float zf = whiteBottom ? 4.32f : -4.32f, xr = whiteBottom ? -4.32f : 4.32f;
        for (int i = 0; i < 8; i++)
        {
            if (cam.Project(i - 3.5f, .07f, zf, out float fx, out float fy))
                g.DrawString(((char)('a' + i)).ToString(), f, br, fx, fy, sf);
            if (cam.Project(xr, .07f, i - 3.5f, out float rx, out float ry))
                g.DrawString((8 - i).ToString(), f, br, rx, ry, sf);
        }
        using var hf = UI.F(8.5f);
        using var hb = new SolidBrush(Color.FromArgb(120, 148, 163, 184));
        g.DrawString("Drag: rotate   |   Scroll: zoom   |   Right-click: reset view", hf, hb, 12, Height - 22);
    }

    // ================= picking (ray casting) =================
    private (int r, int c)? Pick(int mx, int my)
    {
        var cam = Cam.Make(Width, Height, yaw, pitch, Dist());
        cam.Ray(mx, my, out float dx, out float dy, out float dz);
        float best = float.MaxValue;
        (int r, int c)? res = null;

        for (int r = 0; r < 8; r++)
            for (int c = 0; c < 8; c++)
            {
                char p = Game.B[r, c];
                if (p == '.') continue;
                float cx = c - 3.5f, cz = r - 3.5f, rad = .40f;
                float h = PieceMeshes.Height[char.ToLower(p)] * PieceMeshes.Scale;
                float ox = cam.Px - cx, oz = cam.Pz - cz;
                float a = dx * dx + dz * dz;
                if (a < 1e-6f) continue;
                float b = 2 * (ox * dx + oz * dz), cc = ox * ox + oz * oz - rad * rad;
                float disc = b * b - 4 * a * cc;
                if (disc < 0) continue;
                float t = (-b - MathF.Sqrt(disc)) / (2 * a);
                if (t < 0) continue;
                float y = cam.Py + dy * t;
                if (y < 0 || y > h) continue;
                if (t < best) { best = t; res = (r, c); }
            }

        if (dy < -1e-6f)
        {
            float t = -cam.Py / dy;
            float x = cam.Px + dx * t, z = cam.Pz + dz * t;
            if (x > -4 && x < 4 && z > -4 && z < 4 && t < best)
                res = ((int)MathF.Floor(z + 4), (int)MathF.Floor(x + 4));
        }
        return res;
    }

    // ================= mouse =================
    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        if (FindForm()?.ActiveControl is not TextBoxBase) Focus();   // so the mouse wheel zooms
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        downPt = lastPt = e.Location;
        dragging = false;
        if (e.Button == MouseButtons.Left) leftDown = true;
        if (e.Button == MouseButtons.Right) rightDown = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (leftDown || rightDown)
        {
            if (!dragging && Math.Abs(e.X - downPt.X) + Math.Abs(e.Y - downPt.Y) > 6) dragging = true;
            if (dragging)
            {
                yaw -= (e.X - lastPt.X) * .008f;
                pitch = Math.Clamp(pitch + (e.Y - lastPt.Y) * .006f, 18f * MathF.PI / 180f, 88f * MathF.PI / 180f);
                lastPt = e.Location;
                Touch(.75f);
            }
            return;
        }
        var h = Pick(e.X, e.Y);
        if (h != hov)
        {
            hov = h;
            Cursor = h.HasValue ? Cursors.Hand : Cursors.Default;
            Touch();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left && leftDown)
        {
            leftDown = false;
            if (!dragging)
            {
                var h = Pick(e.X, e.Y);
                if (h.HasValue) SquareClicked?.Invoke(h.Value.r, h.Value.c);
            }
        }
        else if (e.Button == MouseButtons.Right && rightDown)
        {
            rightDown = false;
            if (!dragging) ResetView();
        }
        dragging = false;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (hov != null) { hov = null; Touch(); }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        zoom = Math.Clamp(zoom * (e.Delta > 0 ? .92f : 1.08f), .55f, 1.5f);
        Touch(.8f);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Touch(.7f);
    }
}

public class ChessForm : GameForm
{
    private readonly ChessGame game = new();
    private readonly ChessBoard board = new() { Dock = DockStyle.Fill };
    private readonly Panel boardHost = new() { Dock = DockStyle.Fill, BackColor = UI.Bg, Padding = new Padding(8) };
    private readonly Panel side = new() { Dock = DockStyle.Right, Width = 360, BackColor = UI.Panel };
    private readonly Canvas oppCard = new() { Dock = DockStyle.Top, Height = 66, BackColor = UI.Panel };
    private readonly Canvas meCard = new() { Dock = DockStyle.Bottom, Height = 66, BackColor = UI.Panel };
    private readonly Label status = new()
    {
        Dock = DockStyle.Top, Height = 42, TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = UI.Text, Font = UI.F(12, FontStyle.Bold), BackColor = UI.Panel2
    };
    private readonly ListBox moves = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = UI.Panel, ForeColor = UI.Text, Font = new Font("Consolas", 11) };
    private readonly Panel chatPanel = new() { Dock = DockStyle.Bottom, Height = 300, BackColor = UI.Bg };
    private readonly RichTextBox chatBox = new() { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = UI.Bg, ForeColor = UI.Text, Font = UI.F(10) };
    private readonly Panel inputRow = new() { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(8, 7, 8, 7), BackColor = UI.Panel };
    private readonly TextBox chatInput = new() { Dock = DockStyle.Fill, Font = UI.F(11), BackColor = UI.Panel2, ForeColor = UI.Text, BorderStyle = BorderStyle.FixedSingle, Enabled = false };
    private readonly ModernButton sendBtn = new() { Text = "Send", Accent = UI.Cyan, Dock = DockStyle.Right, Width = 84, Enabled = false };

    private bool MyTurn => Started && !GameOver && (game.WhiteToMove ? 0 : 1) == Peer.MySeat;

    public ChessForm(NetworkPeer peer, string name, bool host)
        : base(peer, name, host, 2, "Chess", UI.Cyan)
    {
        ClientSize = new Size(1180, 810);
        MinimumSize = new Size(1000, 700);

        board.Game = game;
        board.BackColor = UI.Bg;
        board.SquareClicked = OnSquare;
        boardHost.Controls.Add(board);

        oppCard.Paint += (s, e) => PaintPlayer(e.Graphics, oppCard, 1 - Peer.MySeat);
        meCard.Paint += (s, e) => PaintPlayer(e.Graphics, meCard, Peer.MySeat);

        var chatHeader = new Label
        {
            Text = "  LIVE CHAT", Dock = DockStyle.Top, Height = 28, ForeColor = UI.Muted,
            Font = UI.F(9, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, BackColor = UI.Panel2
        };
        inputRow.Controls.Add(chatInput);
        inputRow.Controls.Add(sendBtn);
        chatPanel.Controls.Add(chatBox);
        chatPanel.Controls.Add(inputRow);
        chatPanel.Controls.Add(chatHeader);
        chatInput.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SendChat(); } };
        sendBtn.Click += (s, e) => SendChat();

        side.Controls.Add(moves);
        side.Controls.Add(meCard);
        side.Controls.Add(chatPanel);
        side.Controls.Add(status);
        side.Controls.Add(oppCard);

        Body.Controls.Add(boardHost);
        Body.Controls.Add(side);
    }

    protected override void OnGameStart()
    {
        board.WhiteBottom = Peer.MySeat == 0;
        chatInput.Enabled = true;
        sendBtn.Enabled = true;
        AppendChat("System", "Game started. White moves first. Use the chat to talk to your opponent!", UI.Muted);
        UpdateStatus();
        board.Invalidate();
    }

    protected override void OnNamesChanged() { oppCard.Invalidate(); meCard.Invalidate(); }

    // ---------- moving ----------
    private void OnSquare(int r, int c)
    {
        if (!MyTurn) return;
        if (board.Selected.HasValue)
        {
            int i = board.Targets.FindIndex(m => m.TR == r && m.TC == c);
            if (i >= 0)
            {
                var mv = board.Targets[i];
                board.Selected = null;
                board.Targets = new List<Move>();
                DoMove(mv, true);
                return;
            }
        }
        char p = game.B[r, c];
        if (p != '.' && ChessGame.IsWhite(p) == (Peer.MySeat == 0))
        {
            board.Selected = (r, c);
            board.Targets = game.LegalMovesFrom(r, c);
            Sfx.Play("click");
        }
        else { board.Selected = null; board.Targets = new List<Move>(); }
        board.Invalidate();
    }

    private void DoMove(Move m, bool local)
    {
        char mover = game.B[m.FR, m.FC];   // remember which piece moves, for the hop animation
        string note = game.Make(m, out bool capture);
        board.Animate(m, mover);
        Sfx.Play(capture ? "capture" : "move");

        int n = game.History.Count;
        moves.Items.Add(n % 2 == 1 ? $"{(n + 1) / 2,3}.  {note}" : $"     ...  {note}");
        moves.TopIndex = Math.Max(0, moves.Items.Count - 1);

        if (local) Peer.Send($"MOVE|{m.FR}|{m.FC}|{m.TR}|{m.TC}");

        var st = game.State();
        if (st == ChessState.Checkmate)
        {
            GameOver = true;
            bool whiteWon = !game.WhiteToMove;
            bool iWon = whiteWon == (Peer.MySeat == 0);
            status.Text = iWon ? "CHECKMATE - You win!" : "CHECKMATE - You lose";
            Sfx.Play(iWon ? "win" : "error");
        }
        else if (st == ChessState.Stalemate || st == ChessState.Draw)
        {
            GameOver = true;
            status.Text = st == ChessState.Stalemate ? "Stalemate - draw" : "Draw";
            Sfx.Play("check");
        }
        else
        {
            if (st == ChessState.Check) Sfx.Play("check");
            UpdateStatus();
        }
        oppCard.Invalidate();
        meCard.Invalidate();
        board.Invalidate();
    }

    private void UpdateStatus()
    {
        if (!Started || GameOver) return;
        string who = MyTurn ? "Your turn" : $"{NameOf(1 - Peer.MySeat)}'s turn";
        status.Text = game.IsInCheck(game.WhiteToMove) ? "CHECK!   " + who : who;
        oppCard.Invalidate();
        meCard.Invalidate();
    }

    protected override void OnMessage(string[] p)
    {
        if (p[0] == "MOVE" && p.Length >= 5 && !MyTurn && !GameOver)
        {
            if (!int.TryParse(p[1], out int fr) || !int.TryParse(p[2], out int fc) ||
                !int.TryParse(p[3], out int tr) || !int.TryParse(p[4], out int tc)) return;
            if (fr < 0 || fr > 7 || fc < 0 || fc > 7) return;
            var legal = game.LegalMovesFrom(fr, fc);
            int i = legal.FindIndex(m => m.TR == tr && m.TC == tc);
            if (i >= 0) DoMove(legal[i], false);
        }
        else if (p[0] == "CHAT" && p.Length >= 3)
        {
            AppendChat(p[1], string.Join("|", p.Skip(2)), UI.Pink);
            Sfx.Play("chat");
        }
    }

    // ---------- chat ----------
    private void SendChat()
    {
        string text = chatInput.Text.Trim();
        if (text.Length == 0 || !Started) return;
        Peer.Send($"CHAT|{MyName}|{text}");
        AppendChat("You", text, UI.Cyan);
        chatInput.Clear();
    }

    private void AppendChat(string who, string text, Color c)
    {
        chatBox.SelectionStart = chatBox.TextLength;
        chatBox.SelectionColor = c;
        chatBox.SelectionFont = UI.F(10, FontStyle.Bold);
        chatBox.AppendText(who + ":  ");
        chatBox.SelectionColor = UI.Text;
        chatBox.SelectionFont = UI.F(10);
        chatBox.AppendText(text + "\n");
        chatBox.ScrollToCaret();
    }

    // ---------- player cards ----------
    private void PaintPlayer(Graphics g, Control card, int seat)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        bool white = seat == 0;
        bool turn = Started && !GameOver && (game.WhiteToMove ? 0 : 1) == seat;
        if (turn)
        {
            using var hl = new SolidBrush(Color.FromArgb(40, 60, 200, 90));
            g.FillRectangle(hl, card.ClientRectangle);
        }
        using (var disc = new LinearGradientBrush(new Rectangle(14, 12, 42, 42), white ? Color.White : Color.FromArgb(90, 90, 100), white ? Color.FromArgb(170, 165, 150) : Color.Black, 60f))
            g.FillEllipse(disc, 14, 12, 42, 42);
        using var nf = UI.F(12, FontStyle.Bold);
        using var sf = UI.F(9);
        using var mb = new SolidBrush(UI.Muted);
        g.DrawString(NameOf(seat) + (seat == Peer.MySeat ? "  (you)" : ""), nf, Brushes.White, 68, 11);
        g.DrawString(white ? "White" : "Black", sf, mb, 68, 36);
        if (turn)
        {
            using var tb = new SolidBrush(UI.Green);
            g.FillEllipse(tb, card.Width - 30, 26, 14, 14);
        }
    }
}