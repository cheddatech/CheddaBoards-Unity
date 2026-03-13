# CheddaBoards Unity SDK 🧀

> **⚠️ BETA** — This SDK is under active development and testing. The API may change. Feedback welcome via [Issues](https://github.com/cheddatech/CheddaBoards-Unity/issues) or [@cheddatech](https://x.com/cheddatech).

**Leaderboards, achievements, and auth for Unity. Any platform. 3-minute setup.**

Drop-in C# SDK for [CheddaBoards](https://cheddaboards.com) — permanent, serverless gaming infrastructure powered by the Internet Computer.

[![Website](https://img.shields.io/badge/website-cheddaboards.com-blue)](https://cheddaboards.com)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)
[![Version](https://img.shields.io/badge/version-2.0.0--beta-orange)]()

---

## Features

- **Leaderboards** — All Time, Weekly, Daily, Monthly with automatic archiving
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

```csharp
var cb = CheddaBoards.Instance; // Auto-creates singleton GameObject
cb.SetApiKey("your-api-key");
cb.SetGameId("your-game-id");
```

Get your API key and game ID from the [CheddaBoards Dashboard](https://cheddaboards.com).

### 3. Login and submit scores

```csharp
void Start()
{
    var cb = CheddaBoards.Instance;
    cb.SetApiKey("your-api-key");
    cb.SetGameId("your-game-id");

    cb.OnLoginSuccess += (nickname) => Debug.Log($"Welcome {nickname}!");
    cb.OnScoreSubmitted += (score, streak) => Debug.Log($"Score saved: {score}");

    cb.LoginAnonymous("PlayerName");
}

void OnGameOver(int score, int streak)
{
    CheddaBoards.Instance.SubmitScore(score, streak);
}
```

That's it. Leaderboard data is permanently stored on-chain.

---

## Authentication

### Anonymous Login

Instant login with a persistent device ID. No account creation needed.

```csharp
cb.OnLoginSuccess += (nickname) => Debug.Log("Logged in: " + nickname);
cb.OnLoginFailed += (error) => Debug.Log("Failed: " + error);

cb.LoginAnonymous("PlayerName");
```

### Social Login (Google / Apple / Internet Identity)

Uses the Device Code Auth flow (RFC 8628) — works on every platform including consoles, VR, and native builds. No browser pop-ups needed in-game.

```csharp
// Player gets a code to enter at cheddaboards.com/link
cb.OnDeviceCodeReceived += (code, url) =>
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

### Account Migration

Upgrade an anonymous player to a verified account without losing scores or achievements:

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

// Or by scoreboard ID
cb.GetScoreboard("weekly", 100);
```

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

---

## Achievements

```csharp
cb.OnAchievementUnlocked += (id) => Debug.Log($"Unlocked: {id}");

// Single
cb.UnlockAchievement("first_win");

// Batch
cb.UnlockAchievementsBatch(new List<string> { "first_win", "speed_run" });

// Load player's achievements
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
cb.SubmitScore(score, streak);

// End when player quits or pauses
cb.EndPlaySession();
```

---

## Events Reference

| Event | Parameters | Description |
|-------|-----------|-------------|
| `OnSdkReady` | — | SDK initialised |
| `OnLoginSuccess` | `nickname` | Login completed |
| `OnLoginFailed` | `error` | Login failed |
| `OnLogoutSuccess` | — | Logged out |
| `OnScoreSubmitted` | `score, streak` | Score saved |
| `OnScoreError` | `error` | Score submission failed |
| `OnScoreboardLoaded` | `id, config, entries` | Scoreboard data received |
| `OnScoreboardRankLoaded` | `id, rank, score, streak, total` | Player rank received |
| `OnAchievementUnlocked` | `achievementId` | Achievement unlocked |
| `OnAchievementsLoaded` | `achievements` | Achievement list received |
| `OnPlaySessionStarted` | `token` | Play session active |
| `OnDeviceCodeReceived` | `code, url` | Device code ready to display |
| `OnDeviceCodeApproved` | `nickname` | Social login completed |
| `OnDeviceCodeExpired` | — | Code timed out |
| `OnAccountUpgraded` | `oldProfile, newProfile` | Migration completed |
| `OnProfileLoaded` | `nickname, score, streak, achievements` | Profile data received |
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
cb.GetNickname()         // current player nickname
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
| `debugLogging` | `true` | Enable verbose console logging |

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

- **Website**: [cheddaboards.com](https://cheddaboards.com)
- **Godot SDK**: [CheddaBoards-Godot](https://github.com/cheddatech/CheddaBoards-Godot)
- **Company**: [cheddatech.com](https://cheddatech.com)
- **X**: [@cheddatech](https://x.com/cheddatech)

---

## License

MIT — see [LICENSE](LICENSE)

---

**Built by [CheddaTech Ltd](https://cheddatech.com) on the Internet Computer.**
