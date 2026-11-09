# LAN Games Arena (Visual Programming Project)

Developed by **Aisha & Sir Muhammad Abdullah** (add team mates after these names in `UI.cs -> AppInfo`).

Games: **Ludo (2-4 players, sounds)**, **Chess (realistic board + chat)**, **Tic-Tac-Toe**. All play over LAN / same Wi-Fi.

## Run
1. Install .NET 8 SDK + Visual Studio 2022 (".NET desktop development").
2. Open `LanGames.csproj` -> press F5  (or `dotnet run` in this folder).
3. Allow the app in Windows Firewall (Private network) the first time.

## How to play
- Type your name -> pick a game -> **Host Game** (Ludo: choose 2/3/4 players first).
- Other PCs: **Join Game** -> choose the host from the list (or type its IP shown on the waiting screen).
- Testing alone: run the app 2-4 times and join with IP `127.0.0.1`.

## Sir's checklist
| # | Requirement | Where |
|---|-------------|-------|
| 1 | Main window can't close while a game is open (popup message) | `Form1.OnFormClosing`, `GameForm.ActiveGames` |
| 2 | Credits line with names | `AppInfo` in `UI.cs` (main screen footer + About) |
| 3 | Ludo sounds + 4 players | `Sfx.cs`, `LudoForm.cs`, `NetworkPeer.cs` (host relays to 3 clients) |
| 4 | Realistic chess UI / 3D-style pieces | `ChessBoard` in `ChessForm.cs` |
| 5 | Chess chat | right side panel in `ChessForm.cs` |
| 6 | Public GitHub repo | see below |

## GitHub (public)
```
git init
git add .
git commit -m "LAN Games Arena - final version"
git branch -M main
git remote add origin https://github.com/<your-username>/<repo>.git
git push -u origin main
```
Then GitHub -> repo -> Settings -> General -> Danger Zone -> **Change visibility -> Public**.
