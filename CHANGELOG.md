# Changelog

All notable changes to the CheddaBoards Unity SDK are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
