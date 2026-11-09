using System.Net;
using System.Net.Sockets;
using System.Text;

namespace LanGames;

/// <summary>
/// Host = hub (seat 0). Up to 3 more players join as clients (seat 1..3).
/// Every message a client sends is relayed by the host to all other players,
/// so 2-player and 4-player games use the same code.
/// </summary>
public class NetworkPeer
{
    public const int GamePort = 47778;
    public const int DiscoveryPort = 47777;

    private class RemoteClient
    {
        public TcpClient Tcp = null!;
        public StreamWriter Writer = null!;
        public int Seat;
    }

    private TcpListener? listener;
    private TcpClient? serverLink;
    private StreamWriter? serverWriter;
    private readonly List<RemoteClient> clients = new();
    private CancellationTokenSource? cts;
    private volatile bool closed;
    private int disconnectedRaised;

    public bool IsHost { get; private set; }
    public int MySeat { get; private set; }
    public int MaxPlayers { get; private set; } = 2;

    public event Action<int>? PlayerJoined;     // number of players now in the room
    public event Action? Connected;             // everybody is in -> game starts
    public event Action<string>? MessageReceived;
    public event Action? Disconnected;

    // ---------------- HOST ----------------
    public async Task StartHostAsync(string hostName, string gameId, int maxPlayers)
    {
        IsHost = true;
        MySeat = 0;
        MaxPlayers = maxPlayers;
        cts = new CancellationTokenSource();
        var token = cts.Token;

        listener = new TcpListener(IPAddress.Any, GamePort);
        listener.Start();

        _ = Task.Run(() => BroadcastLoop(Clean(hostName), gameId, maxPlayers, token));

        try
        {
            while (true)
            {
                int count;
                lock (clients) count = clients.Count;
                if (count >= maxPlayers - 1) break;

                var tcp = await listener.AcceptTcpClientAsync(token);
                var rc = new RemoteClient
                {
                    Tcp = tcp,
                    Seat = count + 1,
                    Writer = new StreamWriter(tcp.GetStream(), new UTF8Encoding(false)) { AutoFlush = true }
                };
                lock (clients) clients.Add(rc);
                lock (rc.Writer) rc.Writer.WriteLine($"SEAT|{rc.Seat}|{maxPlayers}");
                StartHostReceiving(rc);
                PlayerJoined?.Invoke(count + 2);
            }
        }
        catch (Exception) when (token.IsCancellationRequested || closed)
        {
            return;
        }

        cts.Cancel();          // stop broadcasting
        listener.Stop();
        Broadcast("START", null);
        Connected?.Invoke();
    }

    private static string Clean(string s) => s.Replace("|", " ");

    private static async Task BroadcastLoop(string name, string gameId, int max, CancellationToken token)
    {
        using var udp = new UdpClient();
        udp.EnableBroadcast = true;
        var ep = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);
        byte[] data = Encoding.UTF8.GetBytes($"LANGAME|{name}|{GamePort}|{gameId}|{max}");

        while (!token.IsCancellationRequested)
        {
            try { await udp.SendAsync(data, data.Length, ep); } catch { }
            try { await Task.Delay(1000, token); } catch { break; }
        }
    }

    private void StartHostReceiving(RemoteClient rc)
    {
        var reader = new StreamReader(rc.Tcp.GetStream(), Encoding.UTF8);
        _ = Task.Run(async () =>
        {
            try
            {
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    Broadcast(line, rc);              // relay to the other clients
                    MessageReceived?.Invoke(line);    // and deliver to the host itself
                }
            }
            catch { }
            RaiseDisconnected();
        });
    }

    private void Broadcast(string msg, RemoteClient? except)
    {
        RemoteClient[] snap;
        lock (clients) snap = clients.ToArray();
        foreach (var c in snap)
        {
            if (c == except) continue;
            try { lock (c.Writer) c.Writer.WriteLine(msg); } catch { }
        }
    }

    // ---------------- JOIN ----------------
    /// <param name="onFound">name, ip, gameId, maxPlayers</param>
    public static async Task DiscoverAsync(Action<string, string, string, int> onFound, CancellationToken token)
    {
        using var udp = new UdpClient();
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));

        while (!token.IsCancellationRequested)
        {
            try
            {
                var result = await udp.ReceiveAsync(token);
                var parts = Encoding.UTF8.GetString(result.Buffer).Split('|');
                if (parts.Length == 5 && parts[0] == "LANGAME" && int.TryParse(parts[4], out int max))
                    onFound(parts[1], result.RemoteEndPoint.Address.ToString(), parts[3], max);
            }
            catch when (token.IsCancellationRequested) { break; }
            catch { await Task.Delay(200); }
        }
    }

    public async Task ConnectAsync(string ip)
    {
        IsHost = false;
        serverLink = new TcpClient();
        await serverLink.ConnectAsync(ip, GamePort);
        var stream = serverLink.GetStream();
        serverWriter = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
        var reader = new StreamReader(stream, Encoding.UTF8);

        _ = Task.Run(async () =>
        {
            try
            {
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (line.StartsWith("SEAT|"))
                    {
                        var p = line.Split('|');
                        MySeat = int.Parse(p[1]);
                        MaxPlayers = int.Parse(p[2]);
                    }
                    else if (line == "START") Connected?.Invoke();
                    else MessageReceived?.Invoke(line);
                }
            }
            catch { }
            RaiseDisconnected();
        });
    }

    // ---------------- COMMON ----------------
    public void Send(string message)
    {
        if (IsHost) { Broadcast(message, null); return; }
        try { if (serverWriter != null) lock (serverWriter) serverWriter.WriteLine(message); } catch { }
    }

    private void RaiseDisconnected()
    {
        if (closed) return;
        if (Interlocked.Exchange(ref disconnectedRaised, 1) == 0) Disconnected?.Invoke();
    }

    public void Close()
    {
        closed = true;
        try { cts?.Cancel(); } catch { }
        try { listener?.Stop(); } catch { }
        try { serverLink?.Close(); } catch { }
        lock (clients) foreach (var c in clients) { try { c.Tcp.Close(); } catch { } }
    }

    public static string GetLocalIp()
    {
        try
        {
            using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            s.Connect("8.8.8.8", 65530);   // UDP "connect" sends nothing, just picks the right adapter
            return ((IPEndPoint)s.LocalEndPoint!).Address.ToString();
        }
        catch { return "127.0.0.1"; }
    }
}
