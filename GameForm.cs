using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace LanGames;

/// <summary>
/// Base for the game window (Ludo / Chess / Tic-Tac-Toe).
/// Shows header + "waiting for players" screen, wires the network events,
/// and tells the main window how many games are running.
/// </summary>
public abstract class GameForm : Form
{
    private static int active;
    /// <summary>Main window checks this before it allows itself to close.</summary>
    public static int ActiveGames => active;

    protected readonly NetworkPeer Peer;
    protected readonly string MyName;
    protected readonly string[] Names = new string[4];
    protected bool Started;
    protected bool GameOver;

    /// <summary>Subclasses put their controls in Body.</summary>
    protected readonly Panel Body = new() { Dock = DockStyle.Fill, BackColor = UI.Bg };

    private readonly Panel waitPanel = new() { Dock = DockStyle.Fill, BackColor = UI.Bg };
    private readonly Label waitLabel = new();
    private readonly ModernButton cancelBtn = new() { Text = "Cancel", Accent = UI.Panel2, Size = new Size(160, 44) };
    private readonly bool isHostRole;
    private readonly int hostMax;

    protected GameForm(NetworkPeer peer, string myName, bool isHost, int maxPlayers, string title, Color accent)
    {
        Interlocked.Increment(ref active);
        Peer = peer;
        MyName = myName;
        isHostRole = isHost;
        hostMax = maxPlayers;

        Text = title + " - " + AppInfo.Name;
        BackColor = UI.Bg;
        ForeColor = UI.Text;
        Font = UI.F(10);
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;

        // header bar
        var header = new Canvas { Dock = DockStyle.Top, Height = 58 };
        header.Paint += (s, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using var br = new LinearGradientBrush(header.ClientRectangle, accent, UI.Darken(accent, .45f), 0f);
            g.FillRectangle(br, header.ClientRectangle);
            using var f = UI.F(18, FontStyle.Bold);
            using var f2 = UI.F(10);
            g.DrawString(title, f, Brushes.White, 18, 11);
            var sf = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
            g.DrawString("Playing as  " + MyName, f2, Brushes.White, new RectangleF(0, 0, header.Width - 18, header.Height), sf);
        };

        // waiting screen
        waitLabel.Dock = DockStyle.Fill;
        waitLabel.TextAlign = ContentAlignment.MiddleCenter;
        waitLabel.Font = UI.F(15);
        waitLabel.ForeColor = UI.Text;
        cancelBtn.Click += (s, e) => Close();
        waitPanel.Controls.Add(cancelBtn);
        waitPanel.Controls.Add(waitLabel);
        waitPanel.Resize += (s, e) => cancelBtn.Location = new Point((waitPanel.Width - cancelBtn.Width) / 2, waitPanel.Height - 90);
        UpdateWait(1);

        Controls.Add(Body);
        Controls.Add(waitPanel);
        Controls.Add(header);
        waitPanel.BringToFront();

        // network events (they arrive on background threads)
        Peer.PlayerJoined += n => Ui(() => UpdateWait(n));
        Peer.Connected += () => Ui(() =>
        {
            waitPanel.Visible = false;
            Started = true;
            Names[Peer.MySeat] = MyName;
            Peer.Send($"HELLO|{Peer.MySeat}|{MyName}");
            OnNamesChanged();
            OnGameStart();
        });
        Peer.MessageReceived += m => Ui(() => Dispatch(m));
        Peer.Disconnected += () => Ui(OnPeerLeft);
    }

    protected void Ui(Action a)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(a); } catch { }
    }

    protected string NameOf(int seat) =>
        seat >= 0 && seat < 4 && !string.IsNullOrEmpty(Names[seat]) ? Names[seat] : $"Player {seat + 1}";

    private void UpdateWait(int joined)
    {
        waitLabel.Text = isHostRole
            ? $"Waiting for players...\n\n{joined} / {hostMax} joined\n\nYour IP address:  {NetworkPeer.GetLocalIp()}\n\nFriends: open the app -> Join Game (same Wi-Fi / LAN)"
            : "Connecting to the host...";
    }

    private void Dispatch(string msg)
    {
        var p = msg.Split('|');
        if (p[0] == "HELLO" && p.Length >= 3 && int.TryParse(p[1], out int seat) && seat >= 0 && seat < 4)
        {
            Names[seat] = p[2];
            OnNamesChanged();
            return;
        }
        OnMessage(p);
    }

    protected virtual void OnGameStart() { }
    protected virtual void OnMessage(string[] p) { }
    protected virtual void OnNamesChanged() { }

    protected virtual void OnPeerLeft()
    {
        if (GameOver) return;
        GameOver = true;
        Sfx.Play("error");
        MessageBox.Show(this, "A player left the game. The match has ended.", "Player disconnected",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (Started && !GameOver && e.CloseReason == CloseReason.UserClosing)
        {
            var r = MessageBox.Show(this, "Do you want to leave the running game?", "Leave game",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) { e.Cancel = true; return; }
        }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Interlocked.Decrement(ref active);
        Peer.Close();
        base.OnFormClosed(e);
    }
}
