# CheddaBoards for Unity

Free online leaderboards and achievements for Unity games. One C# file, no packages, no server to run.

SDK version: 2.2.7 · Supports Unity 2022.3 LTS and newer.

## Quick start

<<<<<<< HEAD
1. Get a free API key: register your game at https://cheddaboards.com/developers (takes a minute, no card).
2. The SDK is a single script — `CheddaBoards.cs`. It's already in this package; nothing else to install.
3. Wire it up:
=======
[![Website](https://img.shields.io/badge/website-cheddaboards.com-blue)](https://cheddaboards.com)
[![Docs](https://img.shields.io/badge/docs-docs.cheddaboards.com-blue)](https://docs.cheddaboards.com)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)
[![Version](https://img.shields.io/badge/version-2.2.7-green)]()
[![API uptime](https://img.shields.io/endpoint?url=https%3A%2F%2Fraw.githubusercontent.com%2Fcheddatech%2Fstatus%2FHEAD%2Fapi%2Fapi%2Fuptime.json&label=API%20uptime)](https://status.cheddatech.com)
[![Leaderboards uptime](https://img.shields.io/endpoint?url=https%3A%2F%2Fraw.githubusercontent.com%2Fcheddatech%2Fstatus%2FHEAD%2Fapi%2Fleaderboards-on-chain%2Fuptime.json&label=leaderboards%20uptime)](https://status.cheddatech.com)

---

## What's new

- **2.2.7** — Three fixes on the anonymous-player paths: submits no longer silently overwrite a player's saved nickname with a generated one; batch achievement sync reports the real synced ids instead of a false "0 synced"; and `GetAchievements()` works again (it now reads from the profile — the standalone route it called never existed). Unnamed players stay unnamed — render them as "Guest".
- **2.2.6** — Board reads now come **straight from the CheddaBoards canister** for faster loads, with automatic proxy fallback (see 2.2.5). Fixed the `GetAlltimeLeaderboard()` / `GetWeeklyLeaderboard()` helpers, which queried the wrong board IDs. `GetLeaderboard()` default limit is now 100.
- **2.2.5** — Direct canister reads: `GetScoreboard()` and the Weekly / Daily / Alltime / Monthly helpers read directly from the Internet Computer, keyless and CORS-simple, falling back to the proxy automatically if the direct path can't get through. Same events, no code changes.
- **2.2.3** — Sessions persist across restarts (device-code sign-in is now a one-time flow), plus a new `OnSessionExpired` event. Nickname validation matches the canonical rule (3–16 chars, letters/numbers/underscores).

Full history is in the header comment of `CheddaBoards.cs`.

---

## Features

- **Leaderboards** — All Time, Weekly, Daily, Monthly, and custom-interval, with automatic archiving
- **Category Scoreboards** — Targeted per-level / per-mode boards; submit to one board by ID
- **Achievements** — Unlock tracking with batch sync support
- **Multi-Auth** — Anonymous, Google, Apple, Internet Identity via Device Code flow
- **Play Sessions** — Server-side time validation for anti-cheat
- **Cross-Game Profiles** — Players keep one identity across all CheddaBoards games
- **Account Migration** — Upgrade anonymous accounts to verified without losing data
- **HTTP-Only** — No JavaScript bridges. Works identically on all platforms
- **Zero Dependencies** — Pure UnityWebRequest. No third-party packages required

---

## Quick Start

### 1. Add to your project

Copy `CheddaBoards.cs` into your Unity project (e.g. `Assets/Scripts/CheddaBoards.cs`).

### 2. Configure
>>>>>>> 8a55c79 (update status link on readme)

```csharp
var cb = CheddaBoards.Instance;
cb.SetApiKey("your-api-key");
cb.SetGameId("your-game-id");
cb.OnLoginSuccess += (nick) => canSubmit = true;
cb.LoginAnonymous();          // no player accounts needed

// at game over:
CheddaBoards.Instance.SubmitScore(score, streak);
```

Every game gets all-time, weekly and daily boards automatically, with resets and archiving handled for you.

## Demo

Open `Demo/CheddaClick.unity` — a complete 30-second clicker showing login, score submission, achievements and a live leaderboard with board switching. Replace the placeholder API key and game ID with your own to see your scores appear on your dashboard.

The demo UI uses TextMeshPro. If your project doesn't have it yet, Unity will prompt you to import TMP Essentials (free, built into Unity) when you open the scene. The SDK itself (`CheddaBoards.cs`) has no dependencies and doesn't need TMP.

## Documentation

Full guides, REST reference and troubleshooting: https://docs.cheddaboards.com

- Unity quick start: https://docs.cheddaboards.com/quickstart/unity
- Anti-cheat (optional): https://docs.cheddaboards.com/concepts/anti-cheat
- Category boards: https://docs.cheddaboards.com/concepts/category-boards

## Service & costs

This asset connects to CheddaBoards, a free online leaderboard service. A free account is required for an API key. No fees, no usage costs.

## Support

<<<<<<< HEAD
info@cheddaboards.com · https://cheddaboards.com
=======
A complete working example lives in [`Demo/`](Demo/): **CheddaClick**, a
30-second cheese-clicking game in one script. It shows the full integration —
anonymous login, guest flow, play sessions, score submit, leaderboard render,
delta-synced achievements with a standing unlock panel, play count, and
nickname changes.

To run it: open `Demo/CheddaClick.unity`, put your API key and game ID on the
`CheddaClickGame` component (or in `CheddaBoards.cs`), and press Play.
`CheddaClickGame.cs` is commented as a reference — the event wiring and the
anonymous-player ordering (profile before play, achievements after submit) are
the patterns to copy into your own game.

---

## Authentication

### Anonymous Login

Instant login with a persistent device ID. No account creation needed.

```csharp
cb.OnLoginSuccess += (nickname) => Debug.Log("Logged in: " + nickname);
cb.OnLoginFailed += (error) => Debug.Log("Failed: " + error);

cb.LoginAnonymous();
```

Log in **without** a name unless the player has just chosen one: any name you
pass becomes the current nickname and is written to the server on the next
submit, overwriting whatever the player had saved. Leave it empty and returning
players keep their stored name; brand-new players stay unnamed ("Guest") until
they pick one via `ChangeNickname()`.

> **Nickname rules (server-enforced):** 3–16 characters, letters, numbers, and
> underscores. Anything else is rejected on nickname changes; names supplied at
> login are sanitized server-side. If a name is already taken, the server
> assigns a suffixed variant (e.g. `Player_1`). See
> [Authentication](https://docs.cheddaboards.com/api/authentication).

### Social Login (Google / Apple / Internet Identity)

Uses the Device Code Auth flow (RFC 8628) — works on every platform including consoles, VR, and native builds. No browser pop-ups needed in-game.

```csharp
// code/url let the player authorise at cheddaboards.com/link.
// qrDataUrl is a base64 PNG data URL — display it as a scannable QR, or just show the code.
cb.OnDeviceCodeReceived += (code, url, qrDataUrl) =>
{
    codeLabel.text = $"Go to {url}\nEnter code: {code}";
};

cb.OnDeviceCodeApproved += (nickname) =>
{
    Debug.Log($"Welcome {nickname}!");
    // Player is now authenticated with their social account
};

cb.OnDeviceCodeExpired += () => Debug.Log("Code expired, try again");

cb.LoginWithDeviceCode();
```

Full device-code flow, including QR rendering: [Device code login](https://docs.cheddaboards.com/concepts/device-code).

### Account Migration

Upgrade an anonymous player to a verified account without losing scores or achievements. Scores and streaks merge by maximum, achievements are deduplicated, and play counts are summed — linking the same account from a second device merges cleanly.

```csharp
cb.OnAccountUpgraded += (oldProfile, newProfile) =>
{
    Debug.Log("Account upgraded! Scores preserved.");
};

cb.MigrateAnonymousToCurrent(anonymousDeviceId);
```

---

## Scores & Leaderboards

### Submit a Score

```csharp
cb.OnScoreSubmitted += (score, streak) => Debug.Log($"Saved: {score}");
cb.OnScoreError += (error) => Debug.Log($"Error: {error}");

cb.SubmitScore(1500, 5); // score, streak
```

`SubmitScore` fans the score out to every standard board on your game (all-time, weekly, daily…).

### Submit to a Specific Board (Category Scoreboards)

For per-level, per-mode, or per-category leaderboards, submit to one board by ID. Unlike `SubmitScore`, this writes to that board **only** — it does not fan out to your other boards or touch the player's overall profile total.

```csharp
cb.OnScoreSubmittedToBoard += (boardId, score, streak) => Debug.Log($"Saved to {boardId}: {score}");

cb.SubmitScoreToBoard("level-14", 1500, 5); // boardId, score, streak
```

The board must be configured as **targeted** in the dashboard. You can call it several times in a row for different boards (e.g. a per-level board plus a shared `runs` board). Failures come back on `OnScoreError`. More detail: [Category boards](https://docs.cheddaboards.com/concepts/category-boards).

### Submit Score with Achievements

Achievements sync automatically after the score is confirmed:

```csharp
var achievements = new List<string> { "first_win", "high_scorer", "streak_5" };
cb.SubmitScoreWithAchievements(2000, 10, achievements);
```

### Get Scoreboards

```csharp
// Time-based scoreboards
cb.OnScoreboardLoaded += (id, config, entries) =>
{
    foreach (Dictionary<string, object> entry in entries)
    {
        Debug.Log($"#{entry["rank"]} {entry["nickname"]}: {entry["score"]}");
    }
};

cb.GetWeeklyLeaderboard();
cb.GetDailyLeaderboard();
cb.GetAlltimeLeaderboard();
cb.GetMonthlyLeaderboard();

// Or by scoreboard ID — works for any board, timed or targeted
cb.GetScoreboard("weekly", 100);
cb.GetScoreboard("level-14", 100);
```

Board reads are served directly from the canister for speed, with automatic proxy fallback — see [Scoreboards](https://docs.cheddaboards.com/api/scoreboards).

### Get Player Rank

```csharp
cb.OnScoreboardRankLoaded += (scoreboardId, rank, score, streak, total) =>
{
    Debug.Log($"You are #{rank} out of {total} players");
};

cb.GetScoreboardRank("weekly");
```

### Browse Archives

View previous periods (last week's results, last month, etc.):

```csharp
cb.OnArchivedScoreboardLoaded += (archiveId, config, entries) =>
{
    Debug.Log($"Archive from {config["periodStart"]} to {config["periodEnd"]}");
};

cb.GetLastWeekScoreboard();
cb.GetLastMonthScoreboard();
cb.GetYesterdayScoreboard();
```

Reset schedules and archive retention: [Timed leaderboards](https://docs.cheddaboards.com/concepts/timed-leaderboards).

---

## Achievements

```csharp
cb.OnAchievementUnlocked += (id) => Debug.Log($"Unlocked: {id}");

// Single
cb.UnlockAchievement("first_win");

// Batch
cb.UnlockAchievementsBatch(new List<string> { "first_win", "speed_run" });

// Load player's achievements (reads from the player's profile)
cb.OnAchievementsLoaded += (achievements) => Debug.Log($"Got {achievements.Count} achievements");
cb.GetAchievements();
```

---

## Play Sessions (Anti-Cheat)

Server-side time validation ensures scores match actual play time:

```csharp
cb.OnPlaySessionStarted += (token) => Debug.Log("Session started");

// Start when gameplay begins
cb.StartPlaySession();

// Submit score — play session token is attached automatically
// (applies to both SubmitScore and SubmitScoreToBoard)
cb.SubmitScore(score, streak);

// End when player quits or pauses
cb.EndPlaySession();
```

Caps, time validation, and the suspicion log: [Anti-cheat](https://docs.cheddaboards.com/concepts/anti-cheat).

---

## Events Reference

| Event | Parameters | Description |
|-------|-----------|-------------|
| `OnSdkReady` | — | SDK initialised |
| `OnLoginSuccess` | `nickname` | Login completed |
| `OnLoginFailed` | `error` | Login failed |
| `OnLogoutSuccess` | — | Logged out |
| `OnSessionExpired` | — | Stored session rejected by server (401/403); `OnLogoutSuccess` also fires |
| `OnScoreSubmitted` | `score, streak` | Score saved (fan-out) |
| `OnScoreSubmittedToBoard` | `boardId, score, streak` | Targeted score saved to one board |
| `OnScoreError` | `error` | Score submission failed |
| `OnScoreboardLoaded` | `id, config, entries` | Scoreboard data received |
| `OnScoreboardRankLoaded` | `id, rank, score, streak, total` | Player rank received |
| `OnAchievementUnlocked` | `achievementId` | Achievement unlocked |
| `OnAchievementsLoaded` | `achievements` | Achievement list received |
| `OnPlaySessionStarted` | `token` | Play session active |
| `OnDeviceCodeReceived` | `code, url, qrDataUrl` | Device code ready to display |
| `OnDeviceCodeApproved` | `nickname` | Social login completed |
| `OnDeviceCodeExpired` | — | Code timed out |
| `OnAccountUpgraded` | `oldProfile, newProfile` | Migration completed |
| `OnProfileLoaded` | `nickname, score, streak, achievements, playCount` | Profile data received |
| `OnNicknameChanged` | `nickname` | Nickname updated |
| `OnArchivesListLoaded` | `scoreboardId, archives` | Archive list received |
| `OnArchivedScoreboardLoaded` | `archiveId, config, entries` | Archived scoreboard data |

---

## Utility Methods

```csharp
cb.IsAuthenticated()     // true if logged in
cb.HasAccount()          // true if logged in with a non-anonymous account
cb.IsAnonymous()         // true if using anonymous auth
cb.CanConnect()          // true if API key or session is set
cb.GetNickname()         // current nickname ("" for unnamed anonymous — show "Guest")
cb.GetHighScore()        // cached high score
cb.GetBestStreak()       // cached best streak
cb.GetPlayCount()        // cached play count
cb.GetPlayerId()         // persistent device ID
```

---

## Configuration

| Property | Default | Description |
|----------|---------|-------------|
| `apiKey` | — | Your CheddaBoards API key |
| `gameId` | — | Your game ID |
| `debugLogging` | `false` | Enable verbose console logging (set `true` while developing) |

The SDK auto-creates a singleton `GameObject` with `DontDestroyOnLoad`. No manual scene setup required.

---

## Platform Support

The SDK is HTTP-only — it works identically everywhere Unity runs:

- Windows, Mac, Linux
- iOS, Android
- WebGL
- Consoles
- VR/AR

---

## Links

- **Docs**: [docs.cheddaboards.com](https://docs.cheddaboards.com) — guides and full REST API reference
- **Website**: [cheddaboards.com](https://cheddaboards.com)
- **Service status**: [status.cheddatech.com](https://status.cheddatech.com) — check here first if scores stop submitting
- **Godot SDK**: [CheddaBoards-Godot](https://github.com/cheddatech/CheddaBoards-Godot)
- **Backend (open source)**: [cheddaboards](https://github.com/cheddatech/cheddaboards) — the canister this all runs on
- **Company**: [cheddatech.com](https://cheddatech.com)
- **X**: [@cheddatech](https://x.com/cheddatech)

---

## License

MIT — see [LICENSE](LICENSE)
>>>>>>> 8a55c79 (update status link on readme)
