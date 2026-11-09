using System.Drawing.Drawing2D;

namespace LanGames;

public class TicTacToeForm : GameForm
{
    private static readonly int[][] Lines =
    {
        new[] {0,1,2}, new[] {3,4,5}, new[] {6,7,8},
        new[] {0,3,6}, new[] {1,4,7}, new[] {2,5,8},
        new[] {0,4,8}, new[] {2,4,6}
    };

    private readonly char[] b = new char[9];
    private readonly Canvas board = new() { Size = new Size(450, 450) };
    private readonly Label status = new();
    private readonly Label scoreLbl = new();
    private readonly ModernButton restart = new() { Text = "Play Again", Accent = UI.Pink, Size = new Size(170, 46) };
    private readonly int[] score = new int[3]; // X wins, O wins, draws
    private int turn, hover = -1;
    private int[]? win;
    private bool over;

    public TicTacToeForm(NetworkPeer peer, string name, bool host)
        : base(peer, name, host, 2, "Tic-Tac-Toe", UI.Pink)
    {
        ClientSize = new Size(540, 700);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = true;

        status.SetBounds(0, 10, 540, 44);
        status.TextAlign = ContentAlignment.MiddleCenter;
        status.Font = UI.F(16, FontStyle.Bold);
        status.ForeColor = UI.Text;

        board.Location = new Point(45, 64);
        board.BackColor = UI.Bg;
        board.Paint += PaintBoard;
        board.MouseMove += (s, e) => { int i = CellAt(e.Location); if (i != hover) { hover = i; board.Invalidate(); } };
        board.MouseLeave += (s, e) => { hover = -1; board.Invalidate(); };
        board.MouseClick += (s, e) => CellClicked(CellAt(e.Location));

        scoreLbl.SetBounds(0, 530, 540, 30);
        scoreLbl.TextAlign = ContentAlignment.MiddleCenter;
        scoreLbl.ForeColor = UI.Muted;
        scoreLbl.Font = UI.F(11);

        restart.Location = new Point(185, 580);
        restart.Enabled = false;
        restart.Click += (s, e) =>
        {
            int starter = Peer.MySeat == 0 ? 1 : 0;   // the other player starts the next round
            Peer.Send($"RESET|{starter}");
            Reset(starter);
        };

        Body.Controls.AddRange(new Control[] { status, board, scoreLbl, restart });
    }

    protected override void OnGameStart() { Reset(0); }
    protected override void OnNamesChanged() { UpdateUi(); }

    private void Reset(int starter)
    {
        Array.Fill(b, '\0');
        win = null;
        over = false;
        turn = starter;
        UpdateUi();
    }

    private int CellAt(Point p)
    {
        int x = p.X / 150, y = p.Y / 150;
        return x is >= 0 and < 3 && y is >= 0 and < 3 ? y * 3 + x : -1;
    }

    private void CellClicked(int i)
    {
        if (!Started || over || i < 0 || b[i] != '\0' || turn != Peer.MySeat) return;
        Peer.Send($"MOVE|{i}|{Peer.MySeat}");
        Place(i, Peer.MySeat);
    }

    protected override void OnMessage(string[] p)
    {
        if (p[0] == "MOVE" && p.Length >= 3 && int.TryParse(p[1], out int i) && int.TryParse(p[2], out int seat))
        {
            if (!over && i is >= 0 and < 9 && b[i] == '\0' && seat == turn) Place(i, seat);
        }
        else if (p[0] == "RESET" && p.Length >= 2 && int.TryParse(p[1], out int st))
        {
            Reset(st);
        }
    }

    private void Place(int i, int seat)
    {
        b[i] = seat == 0 ? 'X' : 'O';
        Sfx.Play("move");

        foreach (var l in Lines)
        {
            if (b[l[0]] != '\0' && b[l[0]] == b[l[1]] && b[l[1]] == b[l[2]])
            {
                win = l;
                over = true;
                score[seat]++;
                Sfx.Play(seat == Peer.MySeat ? "win" : "error");
                UpdateUi();
                return;
            }
        }
        if (b.All(c => c != '\0'))
        {
            over = true;
            score[2]++;
            Sfx.Play("check");
        }
        else turn = 1 - seat;
        UpdateUi();
    }

    private void UpdateUi()
    {
        if (!Started) { status.Text = ""; return; }
        if (over)
        {
            status.Text = win == null ? "It's a draw!" : (b[win[0]] == 'X' ? NameOf(0) : NameOf(1)) + " wins!";
            status.ForeColor = win == null ? UI.Muted : UI.Green;
        }
        else
        {
            status.Text = turn == Peer.MySeat ? "Your turn" : $"{NameOf(turn)}'s turn";
            status.ForeColor = turn == Peer.MySeat ? UI.Cyan : UI.Text;
        }
        scoreLbl.Text = $"X  {NameOf(0)}: {score[0]}      O  {NameOf(1)}: {score[1]}      Draws: {score[2]}";
        restart.Enabled = over;
        board.Invalidate();
    }

    private void PaintBoard(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        for (int i = 0; i < 9; i++)
        {
            var r = new RectangleF((i % 3) * 150 + 6, (i / 3) * 150 + 6, 138, 138);
            using var path = UI.Round(r, 20);
            bool canClick = Started && !over && b[i] == '\0' && turn == Peer.MySeat && i == hover;
            using var br = new LinearGradientBrush(r, canClick ? UI.Panel2 : UI.Panel, UI.Darken(UI.Panel, .25f), 90f);
            g.FillPath(br, path);

            var inner = RectangleF.Inflate(r, -34, -34);
            if (b[i] == 'X')
            {
                using var pen = new Pen(UI.Cyan, 12) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLine(pen, inner.Left, inner.Top, inner.Right, inner.Bottom);
                g.DrawLine(pen, inner.Right, inner.Top, inner.Left, inner.Bottom);
            }
            else if (b[i] == 'O')
            {
                using var pen = new Pen(UI.Pink, 12);
                g.DrawEllipse(pen, inner);
            }
        }
        if (win != null)
        {
            PointF Center(int idx) => new((idx % 3) * 150 + 75, (idx / 3) * 150 + 75);
            using var wp = new Pen(Color.FromArgb(230, 255, 240, 120), 10) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(wp, Center(win[0]), Center(win[2]));
        }
    }
}
