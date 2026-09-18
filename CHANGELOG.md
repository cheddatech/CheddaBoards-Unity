# Changelog

All notable changes to the CheddaBoards Unity SDK are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.2.7]

Two fixes and one repaired method on the anonymous-player paths, matching the Godot SDK v2.2.7 release. No API changes — drop-in for existing games.

### Fixed
- **`GetAchievements()` works again.** It called `GET /players/{id}/achievements`, a route the API doesn't have — the server answered `"Unknown endpoint"` and `OnAchievementsLoaded` never fired with data. Achievements are only exposed on the profile, so the method now fetches the profile and surfaces `gameProfile.achievements` via `OnAchievementsLoaded`. `OnProfileLoaded` does **not** fire for this call, so existing profile handlers aren't double-triggered — and reading achievements from `OnProfileLoaded` directly still works exactly as before.
- **Submits no longer rename the player.** All three submit paths (`SubmitScore`, `SubmitScoreWithAchievements`, `SubmitScoreToBoard`) always sent a nickname, filling in a generated `Player_XXXXXX` when none was set — so every submit by a returning anonymous player whose profile hadn't loaded yet silently overwrote their saved name with a fresh generated one. The `nickname` field is now omitted from the submit body unless the caller actually set one; the server keeps the existing profile name. The profile parser also no longer backfills a generated name into the cached nickname when a profile arrives unnamed — that read-path leak would have written a generated name back on the very next submit. Games that force-loaded the profile at startup to dodge this still work unchanged; the workaround is just no longer needed.
- **Batch achievement sync no longer reports "0 synced" on success.** The response parser read a `synced` count key the server doesn't send (the real key is `unlocked`) and accepted only one exact `results` shape, so a successful batch logged `Batch achievement sync complete: 0 synced` and `OnAchievementsLoaded` could fire empty — a false failure on a write that actually persisted. The reported count is now the number of ids actually parsed; the parser tolerates alternate array keys (`unlocked` / `syncedIds` / `achievements`), alternate id keys (`id` / `achievement`), plain id-string arrays, and non-bool success flags; and if an HTTP 200 body still isn't recognised, the **requested** ids are reported as synced (raw body logged for diagnosis) — a 200 means the server stored them. Verified against live API v1.8.0: `data.results[]` of `{achievementId, success, message}`, where re-sends of already-unlocked ids also return `success: true`.

### Added
- `CheddaBoards.VERSION` constant; the init banner now reads from it (it had been reporting a stale version).
- JSON parse failures now log the HTTP status, byte count, first character codes, and raw body — enough to diagnose an empty body, an HTML error page, or a BOM from a single log line.

### Changed
- **Unnamed anonymous players stay unnamed.** With the submit fix above, a player who never sets a name keeps an empty nickname server-side instead of accumulating generated ones. Render these as `"Guest"` in your UI — `GetNickname()` already returns `""` for this case (since 2.2.0).

## [2.2.6]

### Fixed
- `GetAlltimeLeaderboard()` queried the board ID `all-time-new` and `GetWeeklyLeaderboard()` queried `weekly-scoreboard` — both wrong. They now query `all-time` and `weekly`, the standard boards every game is created with, so these helpers return data instead of nothing.

### Changed
- `GetLeaderboard()` default limit is now **100** (was 1000), matching `GetScoreboard()` and every other getter. Pass a limit explicitly for deeper results; use `GetScoreboardRank()` to find a specific player's position.

## [2.2.5]

### Added
- **Direct canister reads.** `GetScoreboard()` — and the `GetWeeklyLeaderboard()` / `GetDailyLeaderboard()` / `GetAlltimeLeaderboard()` / `GetMonthlyLeaderboard()` helpers built on it — now read straight from the CheddaBoards canister over the IC HTTP gateway instead of routing through the API proxy. Faster board loads, no cold-start lag, and keyless / header-free so web exports stay CORS-simple. Same JSON, same events, no code changes needed.
- **Automatic proxy fallback.** If a direct read can't get through (a network that filters `raw.icp0.io`, a gateway hiccup, a non-JSON error page), the SDK silently retries the identical request via the proxy. After three consecutive direct failures it stops trying direct for the rest of the session; one success resets the count. A genuine "not found" from the canister is treated as the real answer, not retried.

### Unchanged
- Writes, ranks, archive readers, and all authenticated calls stay on the proxy.

## [2.2.3]

### Added
- **Session persistence.** The session token from device-code auth is saved to `PlayerPrefs` and restored on startup, so signed-in players stay signed in across app restarts instead of repeating device-code auth.
- **`OnSessionExpired` event.** Fired when the server rejects a stored token (401/403). The saved session is cleared and `OnLogoutSuccess` also fires, so existing menus fall back to their login screen with no changes.

### Changed
- `Logout()` now clears the saved session.
- `ChangeNickname()` enforces the canonical nickname rule client-side (3–16 characters, letters/numbers/underscores), matching proxy and canister validation.

## [2.2.1]

### Added
- **Category scoreboards.** `SubmitScoreToBoard(scoreboardId, score, streak)` and the `OnScoreSubmittedToBoard` event for targeted per-level / per-mode / per-category boards. Writes to one board only; does not fan out or touch the player's profile total. The board must be configured as targeted in the dashboard.

## [2.2.0]

Brings the Unity SDK to parity with the v2.2.0 Godot release.

### Breaking
- `OnProfileLoaded` now passes `playCount` as a 5th argument. Four-argument handlers must add a trailing `int playCount` parameter.
- `OnDeviceCodeReceived` now passes `qrDataUrl` as a 3rd argument (base64 PNG data URL, or `""` if the API returns none). Two-argument handlers must add a trailing `string` parameter.

### Changed
- `debugLogging` now defaults to `false`. Set `CheddaBoards.Instance.debugLogging = true` while developing.
- Device codes and emails are redacted in log output.
- Device-code polling fires an immediate poll on app focus/resume, and uses real-time waits so it keeps polling while the game is paused (`Time.timeScale = 0`).
- Anonymous players with no nickname keep an empty nickname, so UIs can show "Guest" instead of an auto-generated placeholder. `GetNickname()` filters `Player_dev_*` / `Player_p_*` and returns `""` when unnamed.
- `RefreshProfile()` always allows the first call; the cooldown applies from the second call onward.
- A 404 on scoreboard lookups is treated as non-fatal.
- `ChangeNickname()` can be called with no argument.

## [2.1.0]

### Added
- `qrDataUrl` support on `OnDeviceCodeReceived` (base64 PNG QR code).

## [2.0.0]

### Changed
- **HTTP-only SDK.** Removed the JavaScript bridge / web SDK dependency. All platforms use the same REST API paths. Social login via Device Code Auth (works everywhere).

## [1.9.0]

### Added
- Device Code Auth — cross-platform social login via the REST API.

---

Full version history for older releases lives in the header comment of `CheddaBoards.cs`.