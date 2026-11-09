namespace LanGames;

public readonly record struct Move(int FR, int FC, int TR, int TC, char Promo = '\0');

public enum ChessState { Normal, Check, Checkmate, Stalemate, Draw }

/// <summary>
/// Board is B[row, col]; row 0 = rank 8 (black side). Upper-case = white, lower-case = black, '.' = empty.
/// </summary>
public class ChessGame
{
    public char[,] B = new char[8, 8];
    public bool WhiteToMove = true;
    public Move? Last;
    public List<string> History = new();

    private bool wK = true, wQ = true, bK = true, bQ = true;
    private (int r, int c)? ep;

    private static readonly int[] DR8 = { -1, -1, -1, 0, 0, 1, 1, 1 }, DC8 = { -1, 0, 1, -1, 1, -1, 0, 1 };
    private static readonly int[] KNR = { -2, -2, -1, -1, 1, 1, 2, 2 }, KNC = { -1, 1, -2, 2, -2, 2, -1, 1 };
    private static readonly int[] DGR = { -1, -1, 1, 1 }, DGC = { -1, 1, -1, 1 };
    private static readonly int[] ORR = { -1, 1, 0, 0 }, ORC = { 0, 0, -1, 1 };

    public ChessGame() { Reset(); }

    public void Reset()
    {
        string[] rows = { "rnbqkbnr", "pppppppp", "........", "........", "........", "........", "PPPPPPPP", "RNBQKBNR" };
        for (int r = 0; r < 8; r++)
            for (int c = 0; c < 8; c++) B[r, c] = rows[r][c];
        WhiteToMove = true;
        wK = wQ = bK = bQ = true;
        ep = null;
        Last = null;
        History.Clear();
    }

    public static bool IsWhite(char p) => p != '.' && char.IsUpper(p);
    private static bool In(int r, int c) => r >= 0 && r < 8 && c >= 0 && c < 8;
    private bool IsEp(int r, int c) => ep.HasValue && ep.Value.r == r && ep.Value.c == c;
    private static string Sq(int r, int c) => $"{(char)('a' + c)}{8 - r}";

    // ---------- move generation ----------
    private List<Move> Pseudo(int r, int c)
    {
        var list = new List<Move>();
        char p = B[r, c];
        if (p == '.') return list;
        bool white = IsWhite(p);

        void Add(int tr, int tc) => list.Add(new Move(r, c, tr, tc));
        void Slide(int[] dr, int[] dc)
        {
            for (int i = 0; i < dr.Length; i++)
            {
                int tr = r + dr[i], tc = c + dc[i];
                while (In(tr, tc))
                {
                    char q = B[tr, tc];
                    if (q == '.') Add(tr, tc);
                    else { if (IsWhite(q) != white) Add(tr, tc); break; }
                    tr += dr[i]; tc += dc[i];
                }
            }
        }

        switch (char.ToLower(p))
        {
            case 'p':
            {
                int d = white ? -1 : 1, start = white ? 6 : 1, last = white ? 0 : 7;
                void P(int tr, int tc) => list.Add(new Move(r, c, tr, tc, tr == last ? (white ? 'Q' : 'q') : '\0'));
                if (In(r + d, c) && B[r + d, c] == '.')
                {
                    P(r + d, c);
                    if (r == start && B[r + 2 * d, c] == '.') Add(r + 2 * d, c);
                }
                for (int dc = -1; dc <= 1; dc += 2)
                {
                    int tr = r + d, tc = c + dc;
                    if (!In(tr, tc)) continue;
                    if (B[tr, tc] != '.' && IsWhite(B[tr, tc]) != white) P(tr, tc);
                    else if (IsEp(tr, tc)) Add(tr, tc);
                }
                break;
            }
            case 'n':
                for (int i = 0; i < 8; i++)
                {
                    int tr = r + KNR[i], tc = c + KNC[i];
                    if (In(tr, tc) && (B[tr, tc] == '.' || IsWhite(B[tr, tc]) != white)) Add(tr, tc);
                }
                break;
            case 'b': Slide(DGR, DGC); break;
            case 'r': Slide(ORR, ORC); break;
            case 'q': Slide(DGR, DGC); Slide(ORR, ORC); break;
            case 'k':
            {
                for (int i = 0; i < 8; i++)
                {
                    int tr = r + DR8[i], tc = c + DC8[i];
                    if (In(tr, tc) && (B[tr, tc] == '.' || IsWhite(B[tr, tc]) != white)) Add(tr, tc);
                }
                int row = white ? 7 : 0;
                bool ks = white ? wK : bK, qs = white ? wQ : bQ;
                char rook = white ? 'R' : 'r';
                if (r == row && c == 4 && (ks || qs) && !Attacked(row, 4, !white))
                {
                    if (ks && B[row, 5] == '.' && B[row, 6] == '.' && B[row, 7] == rook
                        && !Attacked(row, 5, !white) && !Attacked(row, 6, !white)) Add(row, 6);
                    if (qs && B[row, 1] == '.' && B[row, 2] == '.' && B[row, 3] == '.' && B[row, 0] == rook
                        && !Attacked(row, 3, !white) && !Attacked(row, 2, !white)) Add(row, 2);
                }
                break;
            }
        }
        return list;
    }

    public bool Attacked(int r, int c, bool byWhite, char[,]? bd = null)
    {
        bd ??= B;
        char pawn = byWhite ? 'P' : 'p';
        int pr = byWhite ? r + 1 : r - 1;
        foreach (int dc in new[] { -1, 1 })
            if (In(pr, c + dc) && bd[pr, c + dc] == pawn) return true;

        char kn = byWhite ? 'N' : 'n', k = byWhite ? 'K' : 'k';
        for (int i = 0; i < 8; i++)
        {
            int tr = r + KNR[i], tc = c + KNC[i];
            if (In(tr, tc) && bd[tr, tc] == kn) return true;
            tr = r + DR8[i]; tc = c + DC8[i];
            if (In(tr, tc) && bd[tr, tc] == k) return true;
        }

        char bi = byWhite ? 'B' : 'b', ro = byWhite ? 'R' : 'r', qu = byWhite ? 'Q' : 'q';
        bool Ray(int[] dr, int[] dc, char slider)
        {
            for (int i = 0; i < dr.Length; i++)
            {
                int tr = r + dr[i], tc = c + dc[i];
                while (In(tr, tc))
                {
                    char q = bd[tr, tc];
                    if (q != '.') { if (q == slider || q == qu) return true; break; }
                    tr += dr[i]; tc += dc[i];
                }
            }
            return false;
        }
        return Ray(DGR, DGC, bi) || Ray(ORR, ORC, ro);
    }

    private bool Safe(Move m)
    {
        var bd = (char[,])B.Clone();
        char p = bd[m.FR, m.FC];
        bd[m.TR, m.TC] = m.Promo != '\0' ? m.Promo : p;
        bd[m.FR, m.FC] = '.';
        if (char.ToLower(p) == 'p' && m.FC != m.TC && B[m.TR, m.TC] == '.') bd[m.FR, m.TC] = '.'; // en passant
        bool white = IsWhite(p);
        char king = white ? 'K' : 'k';
        for (int r = 0; r < 8; r++)
            for (int c = 0; c < 8; c++)
                if (bd[r, c] == king) return !Attacked(r, c, !white, bd);
        return false;
    }

    public List<Move> LegalMovesFrom(int r, int c) => Pseudo(r, c).Where(Safe).ToList();

    public (int r, int c)? KingPos(bool white)
    {
        char king = white ? 'K' : 'k';
        for (int r = 0; r < 8; r++)
            for (int c = 0; c < 8; c++)
                if (B[r, c] == king) return (r, c);
        return null;
    }

    public bool IsInCheck(bool white)
    {
        var k = KingPos(white);
        return k.HasValue && Attacked(k.Value.r, k.Value.c, !white);
    }

    private bool HasAnyMove(bool white)
    {
        for (int r = 0; r < 8; r++)
            for (int c = 0; c < 8; c++)
                if (B[r, c] != '.' && IsWhite(B[r, c]) == white && LegalMovesFrom(r, c).Count > 0) return true;
        return false;
    }

    public ChessState State()
    {
        bool check = IsInCheck(WhiteToMove);
        if (!HasAnyMove(WhiteToMove)) return check ? ChessState.Checkmate : ChessState.Stalemate;
        int pieces = 0;
        foreach (char ch in B) if (ch != '.' && char.ToLower(ch) != 'k') pieces++;
        if (pieces == 0) return ChessState.Draw;
        return check ? ChessState.Check : ChessState.Normal;
    }

    // ---------- make a move ----------
    public string Make(Move m, out bool capture)
    {
        char p = B[m.FR, m.FC];
        char t = char.ToLower(p);
        bool epCapture = t == 'p' && m.FC != m.TC && B[m.TR, m.TC] == '.';
        capture = B[m.TR, m.TC] != '.' || epCapture;

        string note = (t == 'p' ? "" : char.ToUpper(p).ToString()) + Sq(m.FR, m.FC) + (capture ? "x" : "-") + Sq(m.TR, m.TC);
        if (epCapture) B[m.FR, m.TC] = '.';

        if (t == 'k' && Math.Abs(m.TC - m.FC) == 2)
        {
            int row = m.FR;
            if (m.TC == 6) { B[row, 5] = B[row, 7]; B[row, 7] = '.'; note = "O-O"; }
            else { B[row, 3] = B[row, 0]; B[row, 0] = '.'; note = "O-O-O"; }
        }

        if (p == 'K') wK = wQ = false;
        if (p == 'k') bK = bQ = false;
        if ((m.FR == 7 && m.FC == 0) || (m.TR == 7 && m.TC == 0)) wQ = false;
        if ((m.FR == 7 && m.FC == 7) || (m.TR == 7 && m.TC == 7)) wK = false;
        if ((m.FR == 0 && m.FC == 0) || (m.TR == 0 && m.TC == 0)) bQ = false;
        if ((m.FR == 0 && m.FC == 7) || (m.TR == 0 && m.TC == 7)) bK = false;

        B[m.TR, m.TC] = m.Promo != '\0' ? m.Promo : p;
        B[m.FR, m.FC] = '.';
        if (m.Promo != '\0') note += "=Q";

        ep = t == 'p' && Math.Abs(m.TR - m.FR) == 2 ? ((m.FR + m.TR) / 2, m.FC) : null;
        Last = m;
        WhiteToMove = !WhiteToMove;

        var st = State();
        if (st == ChessState.Checkmate) note += "#";
        else if (st == ChessState.Check) note += "+";
        History.Add(note);
        return note;
    }
}
