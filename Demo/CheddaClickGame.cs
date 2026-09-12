using System.Collections.Generic;
using CheddaTech;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// CheddaClick — a 30-second cheese-clicking demo for the CheddaBoards SDK.
///
/// Click the cheese. Fast clicks build a combo; each click scores the current
/// combo value. When the timer ends, the run is submitted (score + best combo
/// as streak) and the all-time leaderboard is shown.
///
/// Demonstrates: anonymous login, play sessions (anti-cheat), score submit,
/// delta achievement sync, leaderboard fetch + render. One script, no
/// dependencies beyond the SDK.
///
/// Requires SDK v2.2.7+ — earlier versions always send a nickname on submit,
/// which overwrites the player's saved name (see SDK changelog).
///
/// Guest flow: players stay unnamed ("Guest") until they set a name
/// themselves. Nothing auto-assigns one.
/// </summary>
public class CheddaClickGame : MonoBehaviour
{
    [Header("Set these in the CheddaBoards component instead if you prefer")]
    [SerializeField] private string apiKey = "cb_your-game_xxxxxxxxx";
    [SerializeField] private string gameId = "your-game";

    [Header("UI — wire these in the Inspector")]
    [SerializeField] private Button cheeseButton;
    [SerializeField] private Button playButton;          // "Play" / "Play again"
    [SerializeField] private TMP_Text scoreText;
    [SerializeField] private TMP_Text comboText;
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private TMP_Text statusText;        // login / submit status line
    [SerializeField] private TMP_Text leaderboardText;   // multiline board render
    [SerializeField] private TMP_InputField nameInput;   // optional: change nickname
    [SerializeField] private Button changeNameButton;    // optional: change nickname
    [SerializeField] private TMP_Text achievementToast;  // optional: unlock pop-up (transient)
    [SerializeField] private TMP_Text achievementList;   // optional: standing list from the profile
    [SerializeField] private TMP_Text playCountText;     // optional: total plays from the profile

    [Header("Tuning")]
    [SerializeField] private float runSeconds = 30f;
    [SerializeField] private float comboWindow = 1.0f;   // max gap between clicks to keep combo

    private int score;
    private int combo;
    private int maxCombo;
    private float timeLeft;
    private float lastClickTime = -999f;
    private bool runActive;
    private bool loggedIn;

    // ------------------------------------------------------------------
    // Achievement definitions — ids are what the backend stores; names and
    // descriptions are client-side (yours), same as the Godot template.
    // Keep ids in sync with CheckAchievements below.
    // ------------------------------------------------------------------
    private static readonly Dictionary<string, (string name, string desc)> AchievementDefs =
        new Dictionary<string, (string, string)>
    {
        { "first_click",  ("First Nibble",  "Click the cheese once") },
        { "100_club",     ("100 Club",      "Score 100 in one run") },
        { "combo_master", ("Combo Master",  "Hit a x50 combo") },
    };

    // ------------------------------------------------------------------
    // Achievement state — delta sync
    // ------------------------------------------------------------------
    // unlockedKnown = everything the backend already has (seeded from the
    // profile, extended by unlock confirmations). earnedThisRun = new this
    // run only. Only the difference is flushed, so an achievement syncs to
    // the backend exactly once instead of re-sending on every run.
    //
    // Flush order matters for an anonymous player: they don't exist on the
    // backend until their first submit, so the batch goes AFTER the score
    // lands. The profile re-fetch then waits for the batch to finish
    // (OnAchievementsLoaded) — fetching in parallel races the write and
    // renders a stale list that's missing the fresh unlocks.
    private readonly HashSet<string> earnedThisRun = new HashSet<string>();
    private readonly HashSet<string> unlockedKnown = new HashSet<string>();
    private bool profileExists;   // true once a profile has been loaded from the server
    private bool nameEverSet;     // true once we know the player has a real stored name

    private CheddaBoards CB => CheddaBoards.Instance;

    // ------------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------------

    private void Start()
    {
        cheeseButton.onClick.AddListener(OnCheeseClicked);
        playButton.onClick.AddListener(StartRun);
        if (changeNameButton != null) changeNameButton.onClick.AddListener(OnChangeName);
        cheeseButton.interactable = false;
        playButton.interactable = false;
        SetNameUiActive(false);   // enabled after the first submit creates a profile

        statusText.text = "Connecting…";
        leaderboardText.text = "";
        if (achievementToast != null) achievementToast.text = "";
        if (playCountText != null) playCountText.text = "";
        RenderAchievements();   // shows the full locked list until the profile seeds it
        UpdateHud();

        // SDK events
        CB.OnLoginSuccess     += HandleLoginSuccess;
        CB.OnLoginFailed      += HandleLoginFailed;
        CB.OnScoreSubmitted   += HandleScoreSubmitted;
        CB.OnScoreError       += HandleScoreError;
        CB.OnLeaderboardLoaded += HandleLeaderboardLoaded;
        CB.OnPlaySessionError += HandlePlaySessionError;
        CB.OnNicknameChanged  += HandleNicknameChanged;
        CB.OnNicknameError    += HandleNicknameError;
        CB.OnAchievementUnlocked += HandleAchievementUnlocked;
        CB.OnAchievementsLoaded  += HandleAchievementsLoaded;
        CB.OnProfileLoaded       += HandleProfileLoaded;
        CB.OnNoProfile           += HandleNoProfile;

        // Credentials (or set them on the CheddaBoards component / in CheddaBoards.cs)
        if (!string.IsNullOrEmpty(apiKey))  CB.SetApiKey(apiKey);
        if (!string.IsNullOrEmpty(gameId))  CB.SetGameId(gameId);

        if (CB.IsReady()) LogIn();
        else CB.OnSdkReady += LogIn;
        CB.OnAchievementsLoaded += l => Debug.Log($"[TEST] GetAchievements returned {l.Count}");
    }

    private void OnDestroy()
    {
        if (CheddaBoards.Instance == null) return;
        CB.OnSdkReady          -= LogIn;
        CB.OnLoginSuccess      -= HandleLoginSuccess;
        CB.OnLoginFailed       -= HandleLoginFailed;
        CB.OnScoreSubmitted    -= HandleScoreSubmitted;
        CB.OnScoreError        -= HandleScoreError;
        CB.OnLeaderboardLoaded -= HandleLeaderboardLoaded;
        CB.OnPlaySessionError  -= HandlePlaySessionError;
        CB.OnNicknameChanged   -= HandleNicknameChanged;
        CB.OnNicknameError     -= HandleNicknameError;
        CB.OnAchievementUnlocked -= HandleAchievementUnlocked;
        CB.OnAchievementsLoaded  -= HandleAchievementsLoaded;
        CB.OnProfileLoaded       -= HandleProfileLoaded;
        CB.OnNoProfile           -= HandleNoProfile;
    }

    private void Update()
    {
        if (!runActive) return;

        timeLeft -= Time.deltaTime;

        // Combo decays if you stop clicking
        if (combo > 1 && Time.time - lastClickTime > comboWindow)
        {
            combo = 1;
        }

        if (timeLeft <= 0f)
        {
            timeLeft = 0f;
            EndRun();
        }
        UpdateHud();
    }

    // ------------------------------------------------------------------
    // Game flow
    // ------------------------------------------------------------------

    private void LogIn()
    {
        statusText.text = "Signing in…";
        // Log in WITHOUT a name. A player stays "Guest" until they set a
        // name themselves; a returning player's real name comes back via
        // the profile fetch (see HandleProfileLoaded).
        CB.LoginAnonymous();
    }

    private void StartRun()
    {
        score = 0;
        combo = 1;
        maxCombo = 1;
        earnedThisRun.Clear();
        timeLeft = runSeconds;
        runActive = true;

        cheeseButton.interactable = true;
        playButton.interactable = false;
        leaderboardText.text = "";
        statusText.text = "Click the cheese!";

        // Anti-cheat: open a play session for this run.
        // The SDK stores the token and attaches it to the submit automatically.
        CB.StartPlaySession();

        UpdateHud();
    }

    private void OnCheeseClicked()
    {
        if (!runActive) return;

        if (Time.time - lastClickTime <= comboWindow) combo++;
        else combo = 1;
        lastClickTime = Time.time;

        if (combo > maxCombo) maxCombo = combo;
        score += combo;

        CheckAchievements();
        UpdateHud();
    }

    private void CheckAchievements()
    {
        // Queue only — flushed to the backend after the score submits.
        // QueueIfNew skips anything the backend already has, so nothing
        // re-syncs (and the toast never re-fires for old unlocks).
        QueueIfNew("first_click");
        if (score >= 100) QueueIfNew("100_club");
        if (combo >= 50)  QueueIfNew("combo_master");
    }

    private void QueueIfNew(string id)
    {
        if (unlockedKnown.Contains(id) || earnedThisRun.Contains(id)) return;
        earnedThisRun.Add(id);

        // Instant feedback: toast + list update the moment it's earned.
        // The backend sync happens later (post-submit); the profile reload
        // after that confirms, but the player shouldn't wait for it.
        ShowToast(id);
        RenderAchievements();
    }

    private void EndRun()
    {
        runActive = false;
        cheeseButton.interactable = false;

        if (loggedIn)
        {
            statusText.text = "Submitting score…";
            CB.SubmitScore(score, maxCombo);   // best combo = streak
        }
        else
        {
            statusText.text = "Offline run — score not submitted.";
            playButton.interactable = true;
        }
    }

    // ------------------------------------------------------------------
    // SDK event handlers
    // ------------------------------------------------------------------

    private void HandleLoginSuccess(string nickname)
    {
        loggedIn = true;
        // Load the profile before any run: it seeds unlockedKnown with the
        // achievements the backend already has, and restores the player's
        // real name. Play stays locked until this resolves.
        statusText.text = "Loading profile…";
        playButton.interactable = false;
        CB.GetPlayerProfile();
    }

    private void HandleLoginFailed(string reason)
    {
        statusText.text = $"Sign-in failed: {reason}";
        playButton.interactable = true;   // allow offline play
    }

    private void HandleScoreSubmitted(int submittedScore, int submittedStreak)
    {
        statusText.text = $"Submitted: {submittedScore} (best combo x{submittedStreak})";
        CB.EndPlaySession();

        // The player now exists on the backend. Flush only what's new —
        // ExceptWith guards against anything that snuck into the queue
        // before the profile seeded unlockedKnown.
        earnedThisRun.ExceptWith(unlockedKnown);

        if (earnedThisRun.Count > 0)
        {
            // Profile re-fetch happens in HandleAchievementsLoaded, once the
            // batch has actually landed. Fetching here would race the write.
            CB.UnlockAchievementsBatch(new List<string>(earnedThisRun));
        }
        else
        {
            // Nothing to sync — fetch the profile straight away (updates the
            // cached profile so ChangeNickname takes the server branch).
            CB.GetPlayerProfile();
        }

        CB.GetLeaderboard("score", 10);   // fires OnLeaderboardLoaded
        playButton.interactable = true;
    }

    // Fires when a batch sync finishes. NOTE: until SDK bug #1 (batch response
    // parser) is fixed, the entries list may be empty even on success — treat
    // this as a completion signal only; the profile is the source of truth.
    private void HandleAchievementsLoaded(List<object> entries)
    {
        // Optimistically mark the flushed set as known so a same-session
        // re-check can't re-queue them; the profile fetch below confirms.
        unlockedKnown.UnionWith(earnedThisRun);
        earnedThisRun.Clear();

        CB.GetPlayerProfile();
        CB.GetAchievements();
    }

    private void HandleProfileLoaded(string nickname, int pScore, int pStreak,
                                     List<object> achievements, int playCount)
    {
        // Profile is now cached — a name change will persist server-side.
        profileExists = true;
        SetNameUiActive(true);

        if (!string.IsNullOrEmpty(nickname))
        {
            nameEverSet = true;
            statusText.text = $"Welcome back, {nickname}! Press Play.";
        }
        else
        {
            statusText.text = "Playing as Guest — press Play!";
        }
        playButton.interactable = true;

        // The profile carries the player's real, server-stored achievements
        // (gameProfile.achievements) — the source of truth. Seed the known
        // set from it so runs only sync genuinely new unlocks.
        SeedKnownAchievements(achievements);
        RenderAchievements();

        // playCount is server-side truth: it counts submits, not local runs,
        // so an offline/unsubmitted run correctly doesn't bump it. Updates
        // after every run via the post-submit profile re-fetch.
        if (playCountText != null)
            playCountText.text = playCount == 1 ? "1 play" : $"{playCount} plays";
    }

    // Brand-new player: no profile exists yet. They stay a Guest — no name is
    // auto-assigned. Requires SDK v2.2.7+, where a submit with no nickname set
    // omits the field instead of generating one. The first submit creates the
    // profile unnamed; the name UI unlocks once that profile loads.
    private void HandleNoProfile()
    {
        statusText.text = "Playing as Guest — press Play!";
        playButton.interactable = true;
    }

    // ------------------------------------------------------------------
    // Achievements — helpers
    // ------------------------------------------------------------------

    // Profile achievement entries can be plain id strings or dictionaries
    // (id/name/unlockedAt…) depending on the endpoint. Normalise to the id.
    private static string AchievementId(object entry)
    {
        if (entry == null) return null;
        if (entry is string s) return s;
        if (entry is Dictionary<string, object> d)
        {
            if (d.TryGetValue("id", out object id) && id != null) return id.ToString();
            if (d.TryGetValue("achievementId", out object aid) && aid != null) return aid.ToString();
            if (d.TryGetValue("name", out object n) && n != null) return n.ToString();
            return null;
        }
        return entry.ToString();
    }

    private void SeedKnownAchievements(List<object> achievements)
    {
        if (achievements == null) return;
        foreach (object a in achievements)
        {
            string id = AchievementId(a);
            if (!string.IsNullOrEmpty(id)) unlockedKnown.Add(id);
        }
        // Anything the backend already has must not sit in the pending queue.
        earnedThisRun.ExceptWith(unlockedKnown);
    }

    // Renders every defined achievement, ✓ or 🔒, from LOCAL state
    // (unlockedKnown ∪ earnedThisRun). The server-loaded profile seeds
    // unlockedKnown, so this stays correct across sessions — but it never
    // blocks on a network round-trip, so it updates the instant an
    // achievement is earned mid-run.
    private void RenderAchievements()
    {
        if (achievementList == null) return;

        var lines = new List<string> { "— ACHIEVEMENTS —" };
        int unlocked = 0;
        foreach (var kv in AchievementDefs)
        {
            bool has = unlockedKnown.Contains(kv.Key) || earnedThisRun.Contains(kv.Key);
            if (has) unlocked++;
            lines.Add(has
                ? $"[X] {kv.Value.name} — {kv.Value.desc}"
                : "[ ] ???");
        }
        // Anything the server knows that we have no definition for (e.g. ids
        // from an older build) still counts — show it by id so it isn't lost.
        foreach (string id in unlockedKnown)
            if (!AchievementDefs.ContainsKey(id))
            {
                unlocked++;
                lines.Add($"[X] {id}");
            }

        lines[0] = $"— ACHIEVEMENTS {unlocked}/{AchievementDefs.Count} —";
        achievementList.text = string.Join("\n", lines);
    }

    private static string AchievementDisplayName(string id) =>
        AchievementDefs.TryGetValue(id, out var def) ? def.name : id;

    private void HandleAchievementUnlocked(string achievementId)
    {
        // Server confirmed this unlock — it's known now. No toast here: the
        // player already saw one the moment they earned it (QueueIfNew).
        // Toasting again on sync would double-pop every achievement.
        unlockedKnown.Add(achievementId);
        earnedThisRun.Remove(achievementId);
        RenderAchievements();
    }

    private void ShowToast(string achievementId)
    {
        if (achievementToast == null) return;
        achievementToast.text = $"Achievement unlocked: {AchievementDisplayName(achievementId)}!";
        CancelInvoke(nameof(ClearToast));
        Invoke(nameof(ClearToast), 2.5f);
    }

    private void ClearToast()
    {
        if (achievementToast != null) achievementToast.text = "";
    }

    // ------------------------------------------------------------------
    // Score / leaderboard handlers
    // ------------------------------------------------------------------

    private void HandleScoreError(string reason)
    {
        statusText.text = $"Submit failed: {reason}";
        CB.EndPlaySession();
        playButton.interactable = true;
    }

    private void HandlePlaySessionError(string reason)
    {
        // Non-fatal for the demo: the run still plays; the submit may be
        // rejected if the game has time validation enabled.
        Debug.LogWarning($"Play session error: {reason}");
    }

    private void HandleLeaderboardLoaded(List<object> entries)
    {
        if (entries == null || entries.Count == 0)
        {
            leaderboardText.text = "No scores yet — you're first!";
            return;
        }

        var lines = new List<string> { "— TOP CHEESERS —" };
        foreach (object e in entries)
        {
            if (e is Dictionary<string, object> d)
            {
                int rank     = CheddaBoards.SafeInt(d.ContainsKey("rank")   ? d["rank"]   : 0);
                int s        = CheddaBoards.SafeInt(d.ContainsKey("score")  ? d["score"]  : 0);
                int streak   = CheddaBoards.SafeInt(d.ContainsKey("streak") ? d["streak"] : 0);
                string nick  = d.ContainsKey("nickname") && d["nickname"] != null
                             ? d["nickname"].ToString() : "";
                if (string.IsNullOrEmpty(nick)) nick = "Guest";   // unnamed players render as Guest
                lines.Add($"{rank}. {nick} — {s} (x{streak})");
            }
        }
        leaderboardText.text = string.Join("\n", lines);
    }

    // ------------------------------------------------------------------
    // Nickname
    // ------------------------------------------------------------------

    private void OnChangeName()
    {
        if (nameInput == null) return;
        if (!profileExists)
        {
            statusText.text = "Play one run first — then you can set your name.";
            return;
        }
        string wanted = nameInput.text.Trim();
        if (string.IsNullOrEmpty(wanted)) return;
        CB.ChangeNickname(wanted);   // takes the server branch now a profile is cached
    }

    private void SetNameUiActive(bool on)
    {
        if (nameInput != null)        nameInput.interactable = on;
        if (changeNameButton != null) changeNameButton.interactable = on;
    }

    private void HandleNicknameChanged(string newName)
    {
        nameEverSet = true;
        statusText.text = $"Name set to {newName}";
        if (nameInput != null) nameInput.text = "";
        CB.GetLeaderboard("score", 10);   // refresh so the new name shows on the board
    }

    private void HandleNicknameError(string reason)
    {
        // Shows the validation-error path working — rejected values never reach the backend.
        statusText.text = $"Name rejected: {reason}";
    }

    // ------------------------------------------------------------------
    // HUD
    // ------------------------------------------------------------------

    private void UpdateHud()
    {
        scoreText.text = $"Score: {score}";
        comboText.text = combo > 1 ? $"Combo x{combo}" : "";
        timerText.text = runActive ? $"{timeLeft:0.0}s" : $"{runSeconds:0}s";
    }
}