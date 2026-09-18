# CheddaBoards for Unity

Free online leaderboards and achievements for Unity games. One C# file, no packages, no server to run.

SDK version: 2.2.7 · Supports Unity 2022.3 LTS and newer.

## Quick start

1. Get a free API key: register your game at https://cheddaboards.com/developers (takes a minute, no card).
2. The SDK is a single script — `CheddaBoards.cs`. It's already in this package; nothing else to install.
3. Wire it up:

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

info@cheddaboards.com · https://cheddaboards.com