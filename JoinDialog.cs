namespace LanGames;

/// <summary>Lists games found on the LAN (UDP discovery) or lets the user type an IP.</summary>
public class JoinDialog : Form
{
    public string? SelectedIp { get; private set; }

    private readonly ListView list = new();
    private readonly TextBox ipBox = new();
    private readonly CancellationTokenSource cts = new();
    private readonly string gameId;

    public JoinDialog(string gameId, string gameTitle)
    {
        this.gameId = gameId;
        Text = "Join " + gameTitle;
        ClientSize = new Size(480, 440);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = UI.Bg;
        ForeColor = UI.Text;
        Font = UI.F(10);

        Controls.Add(new Label { Text = "Games found on your network", Font = UI.F(14, FontStyle.Bold), Location = new Point(20, 16), AutoSize = true });
        Controls.Add(new Label { Text = "Searching... (the host must be on the same Wi-Fi / LAN)", ForeColor = UI.Muted, Location = new Point(22, 48), AutoSize = true });

        list.Location = new Point(20, 78);
        list.Size = new Size(440, 200);
        list.View = View.Details;
        list.HeaderStyle = ColumnHeaderStyle.None;
        list.FullRowSelect = true;
        list.MultiSelect = false;
        list.BorderStyle = BorderStyle.None;
        list.BackColor = UI.Panel;
        list.ForeColor = UI.Text;
        list.Font = UI.F(11);
        list.Columns.Add("Host", 210);
        list.Columns.Add("IP", 140);
        list.Columns.Add("Max", 70);
        list.DoubleClick += (s, e) => Choose();
        Controls.Add(list);

        Controls.Add(new Label { Text = "...or type the host's IP address:", ForeColor = UI.Muted, Location = new Point(22, 296), AutoSize = true });
        ipBox.Location = new Point(20, 322);
        ipBox.Size = new Size(440, 30);
        ipBox.Font = UI.F(12);
        ipBox.BackColor = UI.Panel;
        ipBox.ForeColor = UI.Text;
        ipBox.BorderStyle = BorderStyle.FixedSingle;
        Controls.Add(ipBox);

        var join = new ModernButton { Text = "Join Game", Accent = UI.Green, Location = new Point(20, 372), Size = new Size(210, 46) };
        join.Click += (s, e) => Choose();
        var cancel = new ModernButton { Text = "Cancel", Accent = UI.Panel2, Location = new Point(250, 372), Size = new Size(210, 46) };
        cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(join);
        Controls.Add(cancel);

        Shown += async (s, e) =>
        {
            try
            {
                await NetworkPeer.DiscoverAsync((name, ip, game, max) =>
                {
                    if (IsDisposed || !IsHandleCreated) return;
                    try { BeginInvoke(() => AddHost(name, ip, game, max)); } catch { }
                }, cts.Token);
            }
            catch { /* discovery is optional - manual IP still works */ }
        };
    }

    private void AddHost(string name, string ip, string game, int max)
    {
        if (game != gameId) return;
        foreach (ListViewItem it in list.Items)
            if ((string)it.Tag! == ip) return;
        var item = new ListViewItem(name) { Tag = ip };
        item.SubItems.Add(ip);
        item.SubItems.Add(max.ToString());
        list.Items.Add(item);
    }

    private void Choose()
    {
        string ip = ipBox.Text.Trim();
        if (ip.Length == 0 && list.SelectedItems.Count > 0) ip = (string)list.SelectedItems[0].Tag!;
        if (ip.Length == 0)
        {
            Sfx.Play("error");
            MessageBox.Show(this, "Select a game from the list or type an IP address.", "Join", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        SelectedIp = ip;
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        cts.Cancel();
        base.OnFormClosed(e);
    }
}
