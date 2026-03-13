// CheddaBoards.cs v2.0.0
// CheddaBoards integration for Unity
// https://github.com/cheddatech/CheddaBoards-Unity
// https://cheddaboards.com
//
// HTTP-ONLY SDK: All platforms use the REST API
// - Anonymous login: API key + persistent device ID
// - Social login (Google, Apple, II): Device Code Auth flow
//   Player authenticates on their phone at cheddaboards.com/link
// - Score submissions, play sessions, achievements: all via HTTP API
//
// v2.0.0: HTTP-only SDK. Removed JavaScript bridge / web SDK dependency.
//          All platforms use the same REST API paths.
//          Social login via Device Code Auth (works everywhere).
// v1.9.0: Device Code Auth - cross-platform social login via REST API.
// v1.8.2: Achievement sync is now non-blocking (async/fire-and-forget).
// v1.8.1: Fixed achievements not syncing - score submitted FIRST (creates player),
//          then achievements sent one-at-a-time.
// v1.7.0: Fixed post-upgrade scores creating ghost anonymous accounts
// v1.6.0: Fixed end_play_session body field (token → playSessionToken)
// v1.5.9: Persistent device IDs - anonymous players keep same identity across sessions
//
// Add to scene as singleton GameObject (DontDestroyOnLoad)

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace CheddaTech
{
    /// <summary>
    /// CheddaBoards SDK for Unity — Leaderboards, Achievements, and Player Profiles.
    /// HTTP-only: works on all platforms without JavaScript bridges.
    /// </summary>
    public class CheddaBoards : MonoBehaviour
    {
        // ============================================================
        // SINGLETON
        // ============================================================

        private static CheddaBoards _instance;
        public static CheddaBoards Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("CheddaBoards");
                    _instance = go.AddComponent<CheddaBoards>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        // ============================================================
        // QUICK START
        // ============================================================
        // 1. Add CheddaBoards.Instance to your scene (auto-creates singleton)
        // 2. Set your API key: CheddaBoards.Instance.SetApiKey("cb_xxx");
        // 3. Set your game ID: CheddaBoards.Instance.SetGameId("your-game");
        //
        // Anonymous login:
        //    void Start() {
        //        var cb = CheddaBoards.Instance;
        //        cb.OnLoginSuccess += (nick) => Debug.Log("Logged in: " + nick);
        //        cb.LoginAnonymous("PlayerName");
        //    }
        //
        // Social login (Google/Apple via device code):
        //    void Start() {
        //        var cb = CheddaBoards.Instance;
        //        cb.OnDeviceCodeReceived += (code, url) =>
        //            codeLabel.text = $"Go to {url}\nEnter code: {code}";
        //        cb.OnDeviceCodeApproved += (nick) => Debug.Log("Welcome " + nick);
        //        cb.LoginWithDeviceCode();
        //    }
        //
        // Score submission:
        //    void OnGameOver(int score, int streak) {
        //        CheddaBoards.Instance.SubmitScore(score, streak);
        //    }

        // ============================================================
        // EVENTS (C# equivalents of Godot signals)
        // ============================================================

        // --- Initialization ---
        public event Action OnSdkReady;
        public event Action<string> OnInitError;

        // --- Authentication ---
        public event Action<string> OnLoginSuccess;
        public event Action<string> OnLoginFailed;
        public event Action OnLogoutSuccess;
        public event Action<string> OnAuthError;

        // --- Profile ---
        public event Action<string, int, int, List<object>> OnProfileLoaded;
        public event Action OnNoProfile;
        public event Action<string> OnNicknameChanged;
        public event Action<string> OnNicknameError;

        // --- Scores & Leaderboards (Legacy) ---
        public event Action<int, int> OnScoreSubmitted;
        public event Action<string> OnScoreError;
        public event Action<List<object>> OnLeaderboardLoaded;
        public event Action<int, int, int, int> OnPlayerRankLoaded;
        public event Action<string> OnRankError;

        // --- Scoreboards (Time-based) ---
        public event Action<List<object>> OnScoreboardsLoaded;
        public event Action<string, Dictionary<string, object>, List<object>> OnScoreboardLoaded;
        public event Action<string, int, int, int, int> OnScoreboardRankLoaded;
        public event Action<string> OnScoreboardError;

        // --- Scoreboard Archives ---
        public event Action<string, List<object>> OnArchivesListLoaded;
        public event Action<string, Dictionary<string, object>, List<object>> OnArchivedScoreboardLoaded;
        public event Action<int, List<object>> OnArchiveStatsLoaded;
        public event Action<string> OnArchiveError;

        // --- Achievements ---
        public event Action<string> OnAchievementUnlocked;
        public event Action<List<object>> OnAchievementsLoaded;

        // --- HTTP API ---
        public event Action<string, string> OnRequestFailed;

        // --- Play Sessions (Time Validation) ---
        public event Action<string> OnPlaySessionStarted;
        public event Action<string> OnPlaySessionError;

        // --- Account Upgrade (Anonymous → Verified) ---
        public event Action<Dictionary<string, object>, Dictionary<string, object>> OnAccountUpgraded;
        public event Action<string> OnAccountUpgradeFailed;

        // --- Device Code Auth (Cross-platform login) ---
        public event Action<string, string> OnDeviceCodeReceived;
        public event Action<string> OnDeviceCodeApproved;
        public event Action OnDeviceCodeExpired;
        public event Action<string> OnDeviceCodeError;

        // ============================================================
        // CONFIGURATION
        // ============================================================

        /// <summary>Set to true to enable verbose logging.</summary>
        public bool debugLogging = true;

        /// <summary>HTTP API base URL.</summary>
        public const string API_BASE_URL = "https://api.cheddaboards.com";

        /// <summary>Your API key (set via SetApiKey()).</summary>
        public string apiKey = "cb_cheddaclick-v2_122222222";

        /// <summary>Your game ID (set via SetGameId()).</summary>
        public string gameId = "cheddaclick-v2";

        private string _playerId = "";
        private string _sessionToken = "";       // For OAuth session-based auth
        private string _playSessionToken = "";    // For time validation

        // ============================================================
        // INTERNAL STATE
        // ============================================================

        private bool _initComplete = false;
        private string _authType = "";
        private Dictionary<string, object> _cachedProfile = new Dictionary<string, object>();
        private bool _nicknameJustChanged = false;
        private string _nickname = "";

        // ============================================================
        // PERFORMANCE OPTIMIZATION
        // ============================================================

        private bool _isRefreshingProfile = false;
        private bool _isSubmittingScore = false;
        private float _lastProfileRefresh = 0f;
        private const float PROFILE_REFRESH_COOLDOWN = 2.0f;

        // ============================================================
        // PENDING SCORE SUBMISSION VALUES
        // ============================================================

        private int _pendingScore = 0;
        private int _pendingStreak = 0;

        // ============================================================
        // DEVICE CODE AUTH STATE
        // ============================================================

        private string _deviceCode = "";
        private string _deviceUserCode = "";
        private Coroutine _deviceCodePollCoroutine = null;
        private float _deviceCodePollInterval = 5.0f;
        private double _deviceCodeExpiresAt = 0;
        private bool _isPollingDeviceCode = false;
        private bool _deviceCodePollInFlight = false;
        private bool _deviceCodeApprovedFlag = false;

        // ============================================================
        // REQUEST QUEUE
        // ============================================================

        private string _currentEndpoint = "";
        private Dictionary<string, object> _currentMeta = new Dictionary<string, object>();
        private bool _httpBusy = false;
        private Queue<RequestData> _requestQueue = new Queue<RequestData>();

        // Deferred achievement tracking (sent after score succeeds)
        private List<string> _deferredAchievementIds = new List<string>();
        private int _deferredAchievementsRemaining = 0;
        private List<string> _deferredAchievementsSynced = new List<string>();

        // ============================================================
        // PERSISTENT DEVICE ID
        // ============================================================

        private const string DEVICE_ID_PREF_KEY = "cheddaboards_device_id";
        private const string DEVICE_ID_CREATED_KEY = "cheddaboards_device_created";

        // ============================================================
        // INTERNAL TYPES
        // ============================================================

        private class RequestData
        {
            public string endpoint;
            public string method;
            public Dictionary<string, object> body;
            public string requestType;
            public Dictionary<string, object> meta;
        }

        // ============================================================
        // INITIALIZATION
        // ============================================================

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            Log("Initializing CheddaBoards v2.0.0 (HTTP API Mode)...");
            _initComplete = true;
            StartCoroutine(EmitSdkReadyDeferred());
        }

        private IEnumerator EmitSdkReadyDeferred()
        {
            yield return null; // Wait one frame (equivalent to call_deferred)
            OnSdkReady?.Invoke();
        }

        // ============================================================
        // HTTP HELPERS
        // ============================================================

        /// <summary>Build headers for an HTTP request based on auth state.</summary>
        private Dictionary<string, string> BuildHeaders(string requestType = "")
        {
            var headers = new Dictionary<string, string>
            {
                { "Content-Type", "application/json" }
            };

            // Session token takes priority over API key (mutually exclusive)
            // EXCEPT: play sessions always use API key (game-level operation, skip_validation)
            bool forceApiKey = (requestType == "start_play_session" || requestType == "end_play_session")
                               && string.IsNullOrEmpty(_sessionToken);
            if (!string.IsNullOrEmpty(_sessionToken) && !forceApiKey)
            {
                headers["X-Session-Token"] = _sessionToken;
            }
            else if (!string.IsNullOrEmpty(apiKey))
            {
                headers["X-API-Key"] = apiKey;
            }

            if (!string.IsNullOrEmpty(gameId))
            {
                headers["X-Game-ID"] = gameId;
            }

            return headers;
        }

        // ============================================================
        // HTTP REQUEST EXECUTION
        // ============================================================

        private void MakeHttpRequest(string endpoint, string method, Dictionary<string, object> body,
                                     string requestType, Dictionary<string, object> meta = null)
        {
            if (string.IsNullOrEmpty(apiKey) && string.IsNullOrEmpty(_sessionToken))
            {
                Log($"No credentials set - skipping HTTP request to {endpoint}");
                switch (requestType)
                {
                    case "submit_score": OnScoreError?.Invoke("No credentials set"); break;
                    case "player_profile": OnNoProfile?.Invoke(); break;
                    case "leaderboard": OnLeaderboardLoaded?.Invoke(new List<object>()); break;
                    case "player_rank": OnRankError?.Invoke("No credentials set"); break;
                    case "list_scoreboards":
                    case "get_scoreboard":
                        OnScoreboardError?.Invoke("No credentials set"); break;
                    case "list_archives":
                    case "get_archive":
                    case "get_last_archive":
                    case "archive_stats":
                        OnArchiveError?.Invoke("No credentials set"); break;
                }
                return;
            }

            var requestData = new RequestData
            {
                endpoint = endpoint,
                method = method,
                body = body ?? new Dictionary<string, object>(),
                requestType = requestType,
                meta = meta ?? new Dictionary<string, object>()
            };

            if (_httpBusy)
            {
                Log($"HTTP busy, queuing request: {requestType}");
                _requestQueue.Enqueue(requestData);
                return;
            }

            ExecuteHttpRequest(requestData);
        }

        private void ExecuteHttpRequest(RequestData requestData)
        {
            _httpBusy = true;
            _currentEndpoint = requestData.requestType;
            _currentMeta = requestData.meta ?? new Dictionary<string, object>();

            StartCoroutine(SendHttpRequest(requestData));
        }

        private IEnumerator SendHttpRequest(RequestData requestData)
        {
            string url = API_BASE_URL + requestData.endpoint;
            var headers = BuildHeaders(_currentEndpoint);

            string methodStr = requestData.method;
            Log($"HTTP {methodStr}: {url}");

            UnityWebRequest request;
            string jsonBody = requestData.body.Count > 0 ? DictToJson(requestData.body) : "";

            switch (requestData.method)
            {
                case "POST":
                    request = new UnityWebRequest(url, "POST");
                    if (!string.IsNullOrEmpty(jsonBody))
                    {
                        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                    }
                    request.downloadHandler = new DownloadHandlerBuffer();
                    break;
                case "PUT":
                    request = new UnityWebRequest(url, "PUT");
                    if (!string.IsNullOrEmpty(jsonBody))
                    {
                        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                    }
                    request.downloadHandler = new DownloadHandlerBuffer();
                    break;
                case "DELETE":
                    request = UnityWebRequest.Delete(url);
                    request.downloadHandler = new DownloadHandlerBuffer();
                    break;
                default: // GET
                    request = UnityWebRequest.Get(url);
                    break;
            }

            foreach (var header in headers)
            {
                request.SetRequestHeader(header.Key, header.Value);
            }

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.ConnectionError ||
                request.result == UnityWebRequest.Result.ProtocolError)
            {
                // Network-level errors
                if (request.result == UnityWebRequest.Result.ConnectionError)
                {
                    Debug.LogError($"[CheddaBoards] Request failed: network error");
                    OnRequestFailed?.Invoke(_currentEndpoint, "Network error");
                    EmitHttpFailure("Network error");
                    request.Dispose();
                    yield break;
                }
            }

            string responseText = request.downloadHandler?.text ?? "";
            long responseCode = request.responseCode;
            request.Dispose();

            var response = ParseJson(responseText) as Dictionary<string, object>;
            if (response == null)
            {
                Debug.LogError("[CheddaBoards] Failed to parse JSON response");
                OnRequestFailed?.Invoke(_currentEndpoint, "Invalid JSON response");
                EmitHttpFailure("Invalid JSON response");
                yield break;
            }

            if (responseCode != 200)
            {
                string errorMsg = GetString(response, "error", "Unknown error");

                // 404 on profile lookup is expected for new players
                if (responseCode == 404 && _currentEndpoint == "player_profile")
                {
                    Log("Player profile not found (new player) - normal for first-time players");
                    _isRefreshingProfile = false;
                    OnNoProfile?.Invoke();
                    _currentMeta = new Dictionary<string, object>();
                    _httpBusy = false;
                    ProcessNextRequest();
                    yield break;
                }

                // 404 on end play session - already consumed or expired
                if (responseCode == 404 && _currentEndpoint == "end_play_session")
                {
                    Log("Play session already ended or expired - normal");
                    _currentMeta = new Dictionary<string, object>();
                    _httpBusy = false;
                    ProcessNextRequest();
                    yield break;
                }

                // Migration errors are non-fatal
                if (_currentEndpoint == "migrate_account")
                {
                    Log($"Migration note: {errorMsg} (non-fatal, continuing)");
                    _currentMeta = new Dictionary<string, object>();
                    _httpBusy = false;
                    ProcessNextRequest();
                    yield break;
                }

                Debug.LogError($"[CheddaBoards] API error ({responseCode}): {errorMsg}");
                OnRequestFailed?.Invoke(_currentEndpoint, errorMsg);
                EmitHttpFailure(errorMsg);
                yield break;
            }

            bool ok = false;
            if (response.ContainsKey("ok"))
            {
                var okVal = response["ok"];
                if (okVal is bool b) ok = b;
                else if (okVal is string s) ok = s.ToLower() == "true";
            }

            if (!ok)
            {
                string errorMsg = GetString(response, "error", "Unknown error");
                OnRequestFailed?.Invoke(_currentEndpoint, errorMsg);
                EmitHttpFailure(errorMsg);
                yield break;
            }

            var data = response.ContainsKey("data") ? response["data"] as Dictionary<string, object> : new Dictionary<string, object>();
            if (data == null) data = new Dictionary<string, object>();

            EmitHttpSuccess(data);
        }

        /// <summary>Fire-and-forget HTTP request (non-blocking, doesn't use queue).
        /// Used for achievements so they don't block leaderboard loading.</summary>
        private void MakeHttpRequestAsync(string endpoint, string method, Dictionary<string, object> body, string requestType)
        {
            if (string.IsNullOrEmpty(apiKey) && string.IsNullOrEmpty(_sessionToken))
            {
                Log($"No credentials - skipping async request to {endpoint}");
                return;
            }

            StartCoroutine(SendHttpRequestAsync(endpoint, method, body, requestType));
        }

        private IEnumerator SendHttpRequestAsync(string endpoint, string method, Dictionary<string, object> body, string requestType)
        {
            var headers = BuildHeaders(requestType);
            string url = API_BASE_URL + endpoint;
            string jsonBody = body != null && body.Count > 0 ? DictToJson(body) : "";

            Log($"HTTP async {requestType}: {endpoint}");

            UnityWebRequest request;
            if (method == "POST")
            {
                request = new UnityWebRequest(url, "POST");
                if (!string.IsNullOrEmpty(jsonBody))
                {
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
                }
                request.downloadHandler = new DownloadHandlerBuffer();
            }
            else
            {
                request = UnityWebRequest.Get(url);
            }

            foreach (var header in headers)
            {
                request.SetRequestHeader(header.Key, header.Value);
            }

            yield return request.SendWebRequest();

            long code = request.responseCode;
            string responseText = request.downloadHandler?.text ?? "";
            request.Dispose();

            if (code >= 200 && code < 300)
            {
                Log($"Async {requestType} complete (HTTP {code})");

                // Handle async achievement responses
                if (requestType == "unlock_achievement")
                {
                    var resp = ParseJson(responseText) as Dictionary<string, object>;
                    if (resp != null && resp.ContainsKey("data"))
                    {
                        var asyncData = resp["data"] as Dictionary<string, object>;
                        if (asyncData != null)
                        {
                            string achId = GetString(asyncData, "achievementId", "");
                            OnAchievementUnlocked?.Invoke(achId);
                            if (_deferredAchievementsRemaining > 0)
                            {
                                _deferredAchievementsSynced.Add(achId);
                                _deferredAchievementsRemaining--;
                                Log($"Achievement synced: {achId} ({_deferredAchievementsRemaining} remaining)");
                                if (_deferredAchievementsRemaining <= 0)
                                {
                                    Log($"All deferred achievements done: {_deferredAchievementsSynced.Count} synced");
                                    OnAchievementsLoaded?.Invoke(new List<object>(_deferredAchievementsSynced));
                                    _deferredAchievementsSynced.Clear();
                                }
                            }
                        }
                    }
                }
                else if (requestType == "unlock_achievement_batch")
                {
                    var resp = ParseJson(responseText) as Dictionary<string, object>;
                    if (resp != null && resp.ContainsKey("data"))
                    {
                        var asyncData = resp["data"] as Dictionary<string, object>;
                        if (asyncData != null)
                        {
                            int synced = SafeInt(asyncData.ContainsKey("synced") ? asyncData["synced"] : 0);
                            var results = asyncData.ContainsKey("results") ? asyncData["results"] as List<object> : new List<object>();
                            Log($"Batch achievement sync complete: {synced} synced");
                            if (results != null)
                            {
                                foreach (var r in results)
                                {
                                    var result = r as Dictionary<string, object>;
                                    if (result != null)
                                    {
                                        bool success = result.ContainsKey("success") && result["success"] is bool b2 && b2;
                                        if (success)
                                        {
                                            string achId = GetString(result, "achievementId", "");
                                            _deferredAchievementsSynced.Add(achId);
                                            OnAchievementUnlocked?.Invoke(achId);
                                        }
                                    }
                                }
                            }
                            OnAchievementsLoaded?.Invoke(new List<object>(_deferredAchievementsSynced));
                            _deferredAchievementsSynced.Clear();
                            _deferredAchievementsRemaining = 0;
                        }
                    }
                }
            }
            else
            {
                Log($"Async {requestType} failed (HTTP {code})");

                if (requestType == "unlock_achievement" && _deferredAchievementsRemaining > 0)
                {
                    _deferredAchievementsRemaining--;
                    Log($"Achievement unlock failed ({_deferredAchievementsRemaining} remaining)");
                    if (_deferredAchievementsRemaining <= 0)
                    {
                        OnAchievementsLoaded?.Invoke(new List<object>(_deferredAchievementsSynced));
                        _deferredAchievementsSynced.Clear();
                    }
                }
                else if (requestType == "unlock_achievement_batch")
                {
                    Log($"Batch achievement sync failed");
                    OnAchievementsLoaded?.Invoke(new List<object>());
                    _deferredAchievementsSynced.Clear();
                    _deferredAchievementsRemaining = 0;
                }
            }
        }

        // ============================================================
        // HTTP RESPONSE HANDLERS
        // ============================================================

        private void EmitHttpSuccess(Dictionary<string, object> data)
        {
            switch (_currentEndpoint)
            {
                case "submit_score":
                    _isSubmittingScore = false;
                    Log($"Score submission successful: {_pendingScore} points, {_pendingStreak} streak");
                    OnScoreSubmitted?.Invoke(_pendingScore, _pendingStreak);
                    FlushDeferredAchievements();
                    break;

                case "leaderboard":
                    var entries = GetList(data, "leaderboard");
                    OnLeaderboardLoaded?.Invoke(entries);
                    break;

                case "player_rank":
                {
                    int rank = SafeInt(data.ContainsKey("rank") ? data["rank"] : 0);
                    int scoreVal = SafeInt(data.ContainsKey("score") ? data["score"] : 0);
                    int streakVal = SafeInt(data.ContainsKey("streak") ? data["streak"] : 0);
                    int total = SafeInt(data.ContainsKey("totalPlayers") ? data["totalPlayers"] : 0);
                    OnPlayerRankLoaded?.Invoke(rank, scoreVal, streakVal, total);
                    break;
                }

                case "player_profile":
                    _isRefreshingProfile = false;
                    if (data != null && data.Count > 0)
                        UpdateCachedProfile(data);
                    else
                        OnNoProfile?.Invoke();
                    break;

                case "change_nickname":
                {
                    string newNick = GetString(data, "nickname", "");
                    if (newNick != "")
                    {
                        _nickname = newNick;
                        _nicknameJustChanged = true;
                        if (_cachedProfile.Count > 0)
                            _cachedProfile["nickname"] = newNick;
                        OnNicknameChanged?.Invoke(newNick);
                        Log($"Nickname changed to: {newNick}");
                    }
                    break;
                }

                case "change_nickname_anonymous":
                {
                    string newNick = GetString(data, "nickname", "");
                    if (newNick != "")
                    {
                        _nickname = newNick;
                        _nicknameJustChanged = true;
                        if (_cachedProfile.Count > 0)
                            _cachedProfile["nickname"] = newNick;
                        OnNicknameChanged?.Invoke(newNick);
                        Log($"Anonymous nickname changed to: {newNick}");
                    }
                    GetPlayerProfile();
                    break;
                }

                case "unlock_achievement":
                {
                    string achId = GetString(data, "achievementId", "");
                    OnAchievementUnlocked?.Invoke(achId);
                    if (_deferredAchievementsRemaining > 0)
                    {
                        _deferredAchievementsSynced.Add(achId);
                        _deferredAchievementsRemaining--;
                        Log($"Achievement synced: {achId} ({_deferredAchievementsRemaining} remaining)");
                        if (_deferredAchievementsRemaining <= 0)
                        {
                            Log($"All deferred achievements done: {_deferredAchievementsSynced.Count} synced");
                            OnAchievementsLoaded?.Invoke(new List<object>(_deferredAchievementsSynced));
                            _deferredAchievementsSynced.Clear();
                        }
                    }
                    break;
                }

                case "unlock_achievement_batch":
                {
                    int synced = SafeInt(data.ContainsKey("synced") ? data["synced"] : 0);
                    var results = GetList(data, "results");
                    Log($"Batch achievement sync complete: {synced} synced");
                    foreach (var r in results)
                    {
                        var result = r as Dictionary<string, object>;
                        if (result != null)
                        {
                            bool success = result.ContainsKey("success") && result["success"] is bool b && b;
                            if (success)
                            {
                                string achId = GetString(result, "achievementId", "");
                                _deferredAchievementsSynced.Add(achId);
                                OnAchievementUnlocked?.Invoke(achId);
                            }
                        }
                    }
                    OnAchievementsLoaded?.Invoke(new List<object>(_deferredAchievementsSynced));
                    _deferredAchievementsSynced.Clear();
                    _deferredAchievementsRemaining = 0;
                    break;
                }

                case "achievements":
                {
                    var achievements = GetList(data, "achievements");
                    OnAchievementsLoaded?.Invoke(achievements);
                    break;
                }

                case "list_scoreboards":
                {
                    var scoreboards = GetList(data, "scoreboards");
                    OnScoreboardsLoaded?.Invoke(scoreboards);
                    Log($"Loaded {scoreboards.Count} scoreboards");
                    break;
                }

                case "get_scoreboard":
                {
                    string sbId = GetMetaString("scoreboard_id");
                    var config = data.ContainsKey("config") ? data["config"] as Dictionary<string, object> : new Dictionary<string, object>();
                    var sbEntries = GetList(data, "entries");
                    OnScoreboardLoaded?.Invoke(sbId, config ?? new Dictionary<string, object>(), sbEntries);
                    Log($"Loaded scoreboard '{sbId}' with {sbEntries.Count} entries");
                    break;
                }

                case "scoreboard_rank":
                {
                    string sbId = GetMetaString("scoreboard_id");
                    bool found = data.ContainsKey("found") && data["found"] is bool fb && fb;
                    if (found)
                    {
                        int rank = SafeInt(data.ContainsKey("rank") ? data["rank"] : 0);
                        int scoreVal = SafeInt(data.ContainsKey("score") ? data["score"] : 0);
                        int streakVal = SafeInt(data.ContainsKey("streak") ? data["streak"] : 0);
                        int total = SafeInt(data.ContainsKey("totalPlayers") ? data["totalPlayers"] : 0);
                        OnScoreboardRankLoaded?.Invoke(sbId, rank, scoreVal, streakVal, total);
                    }
                    else
                    {
                        int total = SafeInt(data.ContainsKey("totalPlayers") ? data["totalPlayers"] : 0);
                        OnScoreboardRankLoaded?.Invoke(sbId, 0, 0, 0, total);
                    }
                    break;
                }

                case "list_archives":
                {
                    string sbId = GetMetaString("scoreboard_id");
                    var archives = GetList(data, "archives");
                    OnArchivesListLoaded?.Invoke(sbId, archives);
                    Log($"Loaded {archives.Count} archives for '{sbId}'");
                    break;
                }

                case "get_archive":
                case "get_last_archive":
                {
                    string archiveId = GetString(data, "archiveId", GetMetaString("archive_id"));
                    var config = data.ContainsKey("config") ? data["config"] as Dictionary<string, object> : new Dictionary<string, object>();
                    var archEntries = GetList(data, "entries");
                    OnArchivedScoreboardLoaded?.Invoke(archiveId, config ?? new Dictionary<string, object>(), archEntries);
                    Log($"Loaded archive '{archiveId}' with {archEntries.Count} entries");
                    break;
                }

                case "archive_stats":
                {
                    int totalArchives = SafeInt(data.ContainsKey("totalArchives") ? data["totalArchives"] : 0);
                    var bySb = GetList(data, "byScoreboard");
                    OnArchiveStatsLoaded?.Invoke(totalArchives, bySb);
                    Log($"Archive stats: {totalArchives} total archives");
                    break;
                }

                case "game_info":
                case "game_stats":
                case "health":
                    Log($"API response: {DictToJson(data)}");
                    break;

                case "start_play_session":
                    if (data.ContainsKey("ok"))
                        _playSessionToken = data["ok"].ToString();
                    else if (data.ContainsKey("token"))
                        _playSessionToken = data["token"].ToString();
                    else
                    {
                        string err = data.ContainsKey("err") ? data["err"].ToString()
                                   : data.ContainsKey("error") ? data["error"].ToString()
                                   : "Unknown error";
                        Log($"Play session error: {err}");
                        OnPlaySessionError?.Invoke(err);
                        _currentMeta = new Dictionary<string, object>();
                        _httpBusy = false;
                        ProcessNextRequest();
                        return;
                    }
                    Log($"Play session started: {_playSessionToken.Substring(0, Math.Min(30, _playSessionToken.Length))}");
                    OnPlaySessionStarted?.Invoke(_playSessionToken);
                    break;

                case "end_play_session":
                    Log("Play session ended on server successfully");
                    break;

                case "migrate_account":
                {
                    int migratedGames = SafeInt(data.ContainsKey("migratedGames") ? data["migratedGames"] : 0);
                    int migratedSb = SafeInt(data.ContainsKey("migratedScoreboards") ? data["migratedScoreboards"] : 0);
                    Log($"Migration complete: {migratedGames} games, {migratedSb} scoreboards migrated");
                    RefreshProfile();
                    OnAccountUpgraded?.Invoke(_cachedProfile, new Dictionary<string, object>
                    {
                        { "migratedGames", migratedGames },
                        { "migratedScoreboards", migratedSb }
                    });
                    break;
                }

                case "device_code_request":
                {
                    string dc = GetString(data, "device_code", "");
                    string uc = GetString(data, "user_code", "");
                    string urlVal = GetString(data, "verification_url", "");
                    string urlComplete = GetString(data, "verification_url_complete", "");
                    int expiresIn = SafeInt(data.ContainsKey("expires_in") ? data["expires_in"] : 300);
                    int interval = SafeInt(data.ContainsKey("interval") ? data["interval"] : 5);

                    _deviceCode = dc;
                    _deviceUserCode = uc;
                    _deviceCodePollInterval = interval;
                    _deviceCodeExpiresAt = GetUnixTime() + expiresIn;

                    Log($"Device code received: {uc} (expires in {expiresIn}s)");
                    OnDeviceCodeReceived?.Invoke(uc, !string.IsNullOrEmpty(urlComplete) ? urlComplete : urlVal);
                    StartDeviceCodePolling();
                    break;
                }

                case "device_code_token":
                    // Handled in custom polling function
                    break;
            }

            _currentMeta = new Dictionary<string, object>();
            _httpBusy = false;
            ProcessNextRequest();
        }

        private void EmitHttpFailure(string error)
        {
            switch (_currentEndpoint)
            {
                case "submit_score":
                    _isSubmittingScore = false;
                    OnScoreError?.Invoke(error);
                    break;
                case "leaderboard":
                    OnLeaderboardLoaded?.Invoke(new List<object>());
                    break;
                case "player_rank":
                    OnRankError?.Invoke(error);
                    break;
                case "player_profile":
                    _isRefreshingProfile = false;
                    OnNoProfile?.Invoke();
                    break;
                case "change_nickname":
                case "change_nickname_anonymous":
                    OnNicknameError?.Invoke(error);
                    break;
                case "unlock_achievement":
                    if (_deferredAchievementsRemaining > 0)
                    {
                        _deferredAchievementsRemaining--;
                        Log($"Achievement unlock failed ({_deferredAchievementsRemaining} remaining)");
                        if (_deferredAchievementsRemaining <= 0)
                        {
                            OnAchievementsLoaded?.Invoke(new List<object>(_deferredAchievementsSynced));
                            _deferredAchievementsSynced.Clear();
                        }
                    }
                    break;
                case "unlock_achievement_batch":
                    Log($"Batch achievement sync failed: {error}");
                    OnAchievementsLoaded?.Invoke(new List<object>());
                    _deferredAchievementsSynced.Clear();
                    _deferredAchievementsRemaining = 0;
                    break;
                case "achievements":
                    OnAchievementsLoaded?.Invoke(new List<object>());
                    break;
                case "list_scoreboards":
                    OnScoreboardsLoaded?.Invoke(new List<object>());
                    OnScoreboardError?.Invoke(error);
                    break;
                case "get_scoreboard":
                {
                    string sbId = GetMetaString("scoreboard_id");
                    OnScoreboardLoaded?.Invoke(sbId, new Dictionary<string, object>(), new List<object>());
                    OnScoreboardError?.Invoke(error);
                    break;
                }
                case "scoreboard_rank":
                {
                    string sbId = GetMetaString("scoreboard_id");
                    OnScoreboardRankLoaded?.Invoke(sbId, 0, 0, 0, 0);
                    OnScoreboardError?.Invoke(error);
                    break;
                }
                case "list_archives":
                {
                    string sbId = GetMetaString("scoreboard_id");
                    OnArchivesListLoaded?.Invoke(sbId, new List<object>());
                    OnArchiveError?.Invoke(error);
                    break;
                }
                case "get_archive":
                case "get_last_archive":
                {
                    string archiveId = GetMetaString("archive_id");
                    OnArchivedScoreboardLoaded?.Invoke(archiveId, new Dictionary<string, object>(), new List<object>());
                    OnArchiveError?.Invoke(error);
                    break;
                }
                case "start_play_session":
                    Log($"Play session error: {error}");
                    OnPlaySessionError?.Invoke(error);
                    break;
                case "end_play_session":
                    Log($"End play session error (ignored): {error}");
                    break;
                case "device_code_request":
                    Log($"Device code request failed: {error}");
                    OnDeviceCodeError?.Invoke(error);
                    break;
                case "archive_stats":
                    OnArchiveStatsLoaded?.Invoke(0, new List<object>());
                    OnArchiveError?.Invoke(error);
                    break;
            }

            _currentMeta = new Dictionary<string, object>();
            _httpBusy = false;
            ProcessNextRequest();
        }

        private void ProcessNextRequest()
        {
            if (_requestQueue.Count == 0) return;

            var next = _requestQueue.Dequeue();
            Log($"Processing queued request: {next.requestType}");
            ExecuteHttpRequest(next);
        }

        // ============================================================
        // PROFILE MANAGEMENT
        // ============================================================

        private void UpdateCachedProfile(Dictionary<string, object> profile)
        {
            if (profile == null || profile.Count == 0) return;

            // Preserve nickname from recent rename - backend may return stale data
            if (_nicknameJustChanged && !string.IsNullOrEmpty(_nickname))
            {
                profile["nickname"] = _nickname;
                Log($"Preserving renamed nickname '{_nickname}' over stale backend data");
                _nicknameJustChanged = false;
            }

            _cachedProfile = new Dictionary<string, object>(profile);

            string nickname = GetString(profile, "nickname",
                              GetString(profile, "username", GetDefaultNickname()));

            // Handle nested gameProfile from API
            var gameProfile = profile.ContainsKey("gameProfile") ? profile["gameProfile"] as Dictionary<string, object> : null;
            int score = 0;
            int streak = 0;
            List<object> achievements = new List<object>();
            int playCount = 0;

            if (gameProfile != null && gameProfile.Count > 0)
            {
                score = SafeInt(gameProfile.ContainsKey("score") ? gameProfile["score"] : 0);
                streak = SafeInt(gameProfile.ContainsKey("streak") ? gameProfile["streak"] : 0);
                achievements = GetList(gameProfile, "achievements");
                playCount = SafeInt(gameProfile.ContainsKey("playCount") ? gameProfile["playCount"] : 0);
                _cachedProfile["score"] = score;
                _cachedProfile["streak"] = streak;
                _cachedProfile["achievements"] = achievements;
                _cachedProfile["playCount"] = playCount;
            }
            else
            {
                score = SafeInt(profile.ContainsKey("score") ? profile["score"]
                      : profile.ContainsKey("highScore") ? profile["highScore"] : 0);
                streak = SafeInt(profile.ContainsKey("streak") ? profile["streak"]
                       : profile.ContainsKey("bestStreak") ? profile["bestStreak"] : 0);
                achievements = GetList(profile, "achievements");
                playCount = SafeInt(profile.ContainsKey("playCount") ? profile["playCount"]
                          : profile.ContainsKey("plays") ? profile["plays"] : 0);
            }

            _nickname = nickname;
            OnProfileLoaded?.Invoke(nickname, score, streak, achievements);
        }

        // ============================================================
        // LOGGING
        // ============================================================

        private void Log(string message)
        {
            if (debugLogging)
                Debug.Log($"[CheddaBoards] {message}");
        }

        // ============================================================
        // PUBLIC API - UTILITIES
        // ============================================================

        public bool IsReady() => _initComplete;

        public bool CanConnect() => _initComplete && (!string.IsNullOrEmpty(apiKey) || !string.IsNullOrEmpty(_sessionToken));

        /// <summary>Safely convert any value to int. Handles null, float, string, etc.</summary>
        public static int SafeInt(object value)
        {
            if (value == null) return 0;
            if (value is int i) return i;
            if (value is long l) return (int)l;
            if (value is float f) return (int)f;
            if (value is double d) return (int)d;
            if (value is string s)
            {
                if (int.TryParse(s, out int parsed)) return parsed;
                if (float.TryParse(s, out float fparsed)) return (int)fparsed;
                return 0;
            }
            return 0;
        }

        private string GetDefaultNickname()
        {
            string pid = GetPlayerId();
            return "Player_" + (pid.Length > 6 ? pid.Substring(0, 6) : pid);
        }

        public string GetNickname()
        {
            if (!string.IsNullOrEmpty(_nickname) && !_nickname.StartsWith("Player_p_"))
                return _nickname;

            if (_cachedProfile.Count > 0)
            {
                string profileNick = GetString(_cachedProfile, "nickname", "");
                if (!string.IsNullOrEmpty(profileNick) && !profileNick.StartsWith("Player_p_"))
                    return profileNick;
            }

            if (!string.IsNullOrEmpty(_nickname))
                return _nickname;

            return GetDefaultNickname();
        }

        public int GetHighScore()
        {
            if (_cachedProfile.Count == 0) return 0;
            if (_cachedProfile.ContainsKey("score"))
                return SafeInt(_cachedProfile["score"]);
            var gp = _cachedProfile.ContainsKey("gameProfile") ? _cachedProfile["gameProfile"] as Dictionary<string, object> : null;
            if (gp != null && gp.Count > 0)
                return SafeInt(gp.ContainsKey("score") ? gp["score"] : 0);
            return 0;
        }

        public int GetBestStreak()
        {
            if (_cachedProfile.Count == 0) return 0;
            if (_cachedProfile.ContainsKey("streak"))
                return SafeInt(_cachedProfile["streak"]);
            var gp = _cachedProfile.ContainsKey("gameProfile") ? _cachedProfile["gameProfile"] as Dictionary<string, object> : null;
            if (gp != null && gp.Count > 0)
                return SafeInt(gp.ContainsKey("streak") ? gp["streak"] : 0);
            return 0;
        }

        public int GetPlayCount()
        {
            if (_cachedProfile.Count == 0) return 0;
            if (_cachedProfile.ContainsKey("playCount"))
                return SafeInt(_cachedProfile["playCount"]);
            var gp = _cachedProfile.ContainsKey("gameProfile") ? _cachedProfile["gameProfile"] as Dictionary<string, object> : null;
            if (gp != null && gp.Count > 0)
                return SafeInt(gp.ContainsKey("playCount") ? gp["playCount"] : 0);
            return 0;
        }

        public Dictionary<string, object> GetCachedProfile() => new Dictionary<string, object>(_cachedProfile);

        public string GetAuthType() => _authType;

        // ============================================================
        // PUBLIC API - CONFIGURATION
        // ============================================================

        public void SetApiKey(string key)
        {
            apiKey = key;
            Log("API key set");
        }

        public void SetGameId(string id)
        {
            gameId = id;
            Log($"Game ID set: {id}");
        }

        public void SetSessionToken(string token)
        {
            _sessionToken = token;
            Log("Session token set");
        }

        public void SetPlayerId(string playerId)
        {
            _playerId = SanitizePlayerId(playerId);
            Log($"Player ID set: {_playerId}");
        }

        public string GetPlayerId()
        {
            if (!string.IsNullOrEmpty(_playerId))
                return _playerId;

            // Try loading saved device ID from PlayerPrefs
            string savedId = LoadDeviceId();
            if (!string.IsNullOrEmpty(savedId))
            {
                _playerId = savedId;
                Log($"Loaded persistent device ID: {_playerId.Substring(0, Math.Min(12, _playerId.Length))}");
                return _playerId;
            }

            // Generate new persistent device ID (first launch only)
            string timestamp = ((long)(GetUnixTime() * 1000)).ToString();
            string randomPart = UnityEngine.Random.Range(0, int.MaxValue).ToString("x8");
            _playerId = "dev_" + timestamp + "_" + randomPart;
            SaveDeviceId(_playerId);
            Log($"Generated new persistent device ID: {_playerId}");
            return _playerId;
        }

        private void SaveDeviceId(string deviceId)
        {
            PlayerPrefs.SetString(DEVICE_ID_PREF_KEY, deviceId);
            PlayerPrefs.SetFloat(DEVICE_ID_CREATED_KEY, (float)GetUnixTime());
            PlayerPrefs.Save();
            Log($"Device ID saved to PlayerPrefs");
        }

        private string LoadDeviceId()
        {
            return PlayerPrefs.GetString(DEVICE_ID_PREF_KEY, "");
        }

        private string SanitizePlayerId(string rawId)
        {
            if (string.IsNullOrEmpty(rawId))
                return GetPlayerId();

            var sb = new StringBuilder();
            foreach (char c in rawId)
            {
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                    (c >= '0' && c <= '9') || c == '_' || c == '-')
                    sb.Append(c);
            }

            string sanitized = sb.ToString();

            if (string.IsNullOrEmpty(sanitized))
                return "p_" + Math.Abs(rawId.GetHashCode()).ToString();

            if (sanitized[0] >= '0' && sanitized[0] <= '9')
                sanitized = "p_" + sanitized;

            if (sanitized.Length > 100)
                sanitized = sanitized.Substring(0, 100);

            return sanitized;
        }

        // ============================================================
        // PUBLIC API - AUTHENTICATION
        // ============================================================

        /// <summary>Anonymous login - uses API key + persistent device ID.</summary>
        public void LoginAnonymous(string nickname = "")
        {
            if (string.IsNullOrEmpty(apiKey))
            {
                OnLoginFailed?.Invoke("API key not set. Call SetApiKey() first.");
                return;
            }

            _nickname = !string.IsNullOrEmpty(nickname) ? nickname : GetDefaultNickname();
            _authType = "anonymous";
            OnLoginSuccess?.Invoke(_nickname);
            Log($"Anonymous login: {_nickname} (player: {GetPlayerId()})");
        }

        /// <summary>Social login (Google, Apple, etc.) via Device Code Auth.</summary>
        public void LoginGoogle()
        {
            Log("Google login → use LoginWithDeviceCode() for cross-platform social login");
            LoginWithDeviceCode();
        }

        /// <summary>Social login (Google, Apple, etc.) via Device Code Auth.</summary>
        public void LoginApple()
        {
            Log("Apple login → use LoginWithDeviceCode() for cross-platform social login");
            LoginWithDeviceCode();
        }

        /// <summary>Internet Identity login via Device Code Auth.</summary>
        public void LoginInternetIdentity(string nickname = "")
        {
            Log("II login → use LoginWithDeviceCode() for cross-platform social login");
            LoginWithDeviceCode();
        }

        /// <summary>Alias for LoginInternetIdentity.</summary>
        public void LoginCheddaId(string nickname = "") => LoginInternetIdentity(nickname);

        public void Logout()
        {
            _cachedProfile.Clear();
            _authType = "";
            _nickname = "";
            _sessionToken = "";
            _playSessionToken = "";
            OnLogoutSuccess?.Invoke();
            Log("Logged out");
        }

        public bool IsAuthenticated()
        {
            if (_authType == "anonymous") return true;
            return !string.IsNullOrEmpty(_sessionToken);
        }

        public bool IsAnonymous() => _authType == "anonymous" || string.IsNullOrEmpty(_sessionToken);

        public bool HasAccount() => IsAuthenticated() && !IsAnonymous();

        public void RefreshProfile()
        {
            if (_isRefreshingProfile) return;

            float currentTime = Time.time;
            if (currentTime - _lastProfileRefresh < PROFILE_REFRESH_COOLDOWN) return;

            _isRefreshingProfile = true;
            _lastProfileRefresh = currentTime;
            GetPlayerProfile();
            Log("Profile refresh requested");
        }

        public void ChangeNickname(string newNickname)
        {
            if (string.IsNullOrEmpty(newNickname) || newNickname.Length < 2)
            {
                OnNicknameError?.Invoke("Nickname must be at least 2 characters");
                return;
            }

            // Anonymous players who haven't submitted a score yet don't exist on backend
            if (IsAnonymous() && _cachedProfile.Count == 0)
            {
                _nickname = newNickname;
                Log($"Nickname set locally (no backend profile yet): {newNickname}");
                OnNicknameChanged?.Invoke(newNickname);
                return;
            }

            if (!string.IsNullOrEmpty(_sessionToken))
            {
                var body = new Dictionary<string, object> { { "nickname", newNickname } };
                MakeHttpRequest("/profile/nickname", "PUT", body, "change_nickname");
                Log($"Nickname change requested (session) -> {newNickname}");
            }
            else if (!string.IsNullOrEmpty(apiKey))
            {
                string pid = GetPlayerId();
                if (string.IsNullOrEmpty(pid))
                {
                    OnNicknameError?.Invoke("No player ID set");
                    return;
                }
                var body = new Dictionary<string, object> { { "nickname", newNickname } };
                string url = $"/players/{Uri.EscapeDataString(pid)}/nickname";
                MakeHttpRequest(url, "PUT", body, "change_nickname_anonymous");
                Log($"Nickname change requested (API) for: {pid} -> {newNickname}");
            }
            else
            {
                OnNicknameError?.Invoke("Not authenticated");
            }
        }

        // ============================================================
        // PUBLIC API - DEVICE CODE AUTH (Cross-platform social login)
        // ============================================================

        /// <summary>Start device code login flow.
        /// Emits OnDeviceCodeReceived with the code to show the player.
        /// Automatically polls for approval and emits OnDeviceCodeApproved on success.</summary>
        public void LoginWithDeviceCode()
        {
            if (!_initComplete)
            {
                OnDeviceCodeError?.Invoke("CheddaBoards not ready");
                return;
            }

            if (string.IsNullOrEmpty(gameId))
            {
                OnDeviceCodeError?.Invoke("Game ID not set. Call SetGameId() first.");
                return;
            }

            StopDeviceCodePolling();

            Log($"Requesting device code for game: {gameId}");
            var body = new Dictionary<string, object> { { "gameId", gameId } };
            MakeHttpRequest("/auth/device/code", "POST", body, "device_code_request");
        }

        /// <summary>Cancel an in-progress device code login.</summary>
        public void CancelDeviceCode()
        {
            StopDeviceCodePolling();
            _deviceCode = "";
            _deviceUserCode = "";
            Log("Device code login cancelled");
        }

        /// <summary>Get the current user code (for display purposes).</summary>
        public string GetDeviceUserCode() => _deviceUserCode;

        /// <summary>Check if a device code login is in progress.</summary>
        public bool IsDeviceCodePending() => _isPollingDeviceCode && !string.IsNullOrEmpty(_deviceCode);

        // ============================================================
        // DEVICE CODE POLLING (Internal)
        // ============================================================

        private void StartDeviceCodePolling()
        {
            StopDeviceCodePolling();
            _isPollingDeviceCode = true;
            _deviceCodePollInFlight = false;
            _deviceCodeApprovedFlag = false;

            _deviceCodePollCoroutine = StartCoroutine(DeviceCodePollLoop());
            Log($"Device code polling started (every {(int)_deviceCodePollInterval}s)");
        }

        private void StopDeviceCodePolling()
        {
            _isPollingDeviceCode = false;
            _deviceCodePollInFlight = false;
            if (_deviceCodePollCoroutine != null)
            {
                StopCoroutine(_deviceCodePollCoroutine);
                _deviceCodePollCoroutine = null;
            }
        }

        private IEnumerator DeviceCodePollLoop()
        {
            while (_isPollingDeviceCode && !string.IsNullOrEmpty(_deviceCode))
            {
                yield return new WaitForSeconds(_deviceCodePollInterval);

                if (!_isPollingDeviceCode || string.IsNullOrEmpty(_deviceCode))
                    yield break;

                if (_deviceCodePollInFlight) continue;

                // Check expiry
                if (GetUnixTime() >= _deviceCodeExpiresAt)
                {
                    Log($"Device code expired: {_deviceUserCode}");
                    StopDeviceCodePolling();
                    _deviceCode = "";
                    _deviceUserCode = "";
                    OnDeviceCodeExpired?.Invoke();
                    yield break;
                }

                _deviceCodePollInFlight = true;
                yield return StartCoroutine(PollDeviceCodeToken());
            }
        }

        private IEnumerator PollDeviceCodeToken()
        {
            var headers = new Dictionary<string, string>
            {
                { "Content-Type", "application/json" }
            };
            if (!string.IsNullOrEmpty(gameId))
                headers["X-Game-ID"] = gameId;

            string body = DictToJson(new Dictionary<string, object> { { "device_code", _deviceCode } });
            string url = API_BASE_URL + "/auth/device/token";

            var request = new UnityWebRequest(url, "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            request.downloadHandler = new DownloadHandlerBuffer();
            foreach (var h in headers)
                request.SetRequestHeader(h.Key, h.Value);

            yield return request.SendWebRequest();

            _deviceCodePollInFlight = false;
            long responseCode = request.responseCode;
            string responseText = request.downloadHandler?.text ?? "";
            bool networkError = request.result == UnityWebRequest.Result.ConnectionError;
            request.Dispose();

            if (networkError)
            {
                Log("Device code poll: network error");
                yield break;
            }

            if (_deviceCodeApprovedFlag)
            {
                Log("Device code poll: ignoring response (already approved)");
                yield break;
            }

            var response = ParseJson(responseText) as Dictionary<string, object>;
            if (response == null)
            {
                Log("Device code poll: invalid JSON");
                yield break;
            }

            // 428 = authorization_pending (keep polling)
            if (responseCode == 428)
                yield break;

            // 410 = expired
            if (responseCode == 410)
            {
                Log("Device code expired (server confirmed)");
                StopDeviceCodePolling();
                _deviceCode = "";
                _deviceUserCode = "";
                OnDeviceCodeExpired?.Invoke();
                yield break;
            }

            // 200 = approved!
            if (responseCode == 200)
            {
                bool ok = response.ContainsKey("ok") && response["ok"] is bool b && b;
                if (ok)
                {
                    _deviceCodeApprovedFlag = true;
                    StopDeviceCodePolling();

                    var data = response.ContainsKey("data") ? response["data"] as Dictionary<string, object> : new Dictionary<string, object>();
                    if (data == null) data = new Dictionary<string, object>();

                    string sessionId = GetString(data, "sessionId", "");
                    string nickname = GetString(data, "nickname", "Player");
                    string email = GetString(data, "email", "");

                    Log($"Device code approved! User: {nickname} ({email})");

                    // Save anonymous player ID BEFORE switching auth — needed for migration
                    string previousAnonymousId = _playerId;
                    bool wasAnonymous = _authType == "anonymous" && !string.IsNullOrEmpty(previousAnonymousId);

                    // Set session state
                    _sessionToken = sessionId;
                    _nickname = nickname;
                    _authType = "google"; // Provider determined by what they chose on the page

                    // Clear stale anonymous play session
                    if (!string.IsNullOrEmpty(_playSessionToken))
                    {
                        Log("Clearing stale anonymous play session after device code auth");
                        _playSessionToken = "";
                    }

                    // Cache profile data
                    var gameProfile = data.ContainsKey("gameProfile") ? data["gameProfile"] as Dictionary<string, object> : null;
                    if (gameProfile != null)
                    {
                        UpdateCachedProfile(new Dictionary<string, object>
                        {
                            { "nickname", nickname },
                            { "gameProfile", gameProfile }
                        });
                    }
                    else
                    {
                        _cachedProfile = new Dictionary<string, object> { { "nickname", nickname } };
                        OnProfileLoaded?.Invoke(nickname, 0, 0, new List<object>());
                    }

                    // Clear device code state
                    _deviceCode = "";
                    _deviceUserCode = "";

                    // Emit both signals so existing login flows work
                    OnDeviceCodeApproved?.Invoke(nickname);
                    OnLoginSuccess?.Invoke(nickname);

                    // Auto-migrate anonymous data → new account
                    if (wasAnonymous)
                        MigrateAnonymousAccount(previousAnonymousId);

                    yield break;
                }
            }

            // 404 = invalid code (or already consumed)
            if (responseCode == 404)
            {
                if (_deviceCodeApprovedFlag || string.IsNullOrEmpty(_deviceCode))
                {
                    Log("Device code poll: 404 after approval (ignoring)");
                    yield break;
                }
                Log("Device code invalid or expired");
                StopDeviceCodePolling();
                _deviceCode = "";
                _deviceUserCode = "";
                OnDeviceCodeError?.Invoke("Invalid or expired code");
                yield break;
            }

            // Other errors - log but keep polling
            string errorMsg = GetString(response, "error", "Unknown error");
            Log($"Device code poll error ({responseCode}): {errorMsg}");
        }

        // ============================================================
        // ACCOUNT MIGRATION (Anonymous → Verified)
        // ============================================================

        private void MigrateAnonymousAccount(string anonymousDeviceId)
        {
            if (string.IsNullOrEmpty(anonymousDeviceId) || string.IsNullOrEmpty(_sessionToken))
            {
                Log("Migration skipped: missing device ID or session token");
                return;
            }

            Log($"Migrating anonymous data: {anonymousDeviceId} → authenticated account");
            var body = new Dictionary<string, object> { { "deviceId", anonymousDeviceId } };
            MakeHttpRequest("/migrate-account", "POST", body, "migrate_account");
        }

        /// <summary>Public method: manually trigger migration if needed.</summary>
        public void MigrateAnonymousToCurrent(string anonymousDeviceId)
        {
            MigrateAnonymousAccount(anonymousDeviceId);
        }

        // ============================================================
        // PUBLIC API - SCORES
        // ============================================================

        public void SubmitScore(int score, int streak = 0)
        {
            if (!IsAuthenticated())
            {
                Log("Not authenticated, cannot submit");
                OnScoreError?.Invoke("Not authenticated");
                return;
            }

            if (_isSubmittingScore)
            {
                Log("Score submission already in progress");
                return;
            }

            _isSubmittingScore = true;
            _pendingScore = score;
            _pendingStreak = streak;

            string nick = !string.IsNullOrEmpty(_nickname) ? _nickname : GetDefaultNickname();
            var body = new Dictionary<string, object>
            {
                { "playerId", GetPlayerId() },
                { "gameId", gameId },
                { "score", score },
                { "streak", streak },
                { "nickname", nick }
            };
            if (!string.IsNullOrEmpty(_playSessionToken))
                body["playSessionToken"] = _playSessionToken;

            Log($"Submitting: score={score}, streak={streak}, nickname={nick}, gameId={gameId}, playerId={body["playerId"]}, session={(_playSessionToken.Length > 20 ? _playSessionToken.Substring(0, 20) : _playSessionToken)}");
            MakeHttpRequest("/scores", "POST", body, "submit_score");
        }

        public void SubmitScoreWithAchievements(int score, int streak, List<string> achievements)
        {
            if (!IsAuthenticated())
            {
                Log("Not authenticated, cannot submit");
                OnScoreError?.Invoke("Not authenticated");
                return;
            }
            if (_isSubmittingScore)
            {
                Log("Score submission already in progress");
                return;
            }
            _isSubmittingScore = true;
            _pendingScore = score;
            _pendingStreak = streak;

            var achIds = new List<string>();
            foreach (var ach in achievements)
            {
                if (!string.IsNullOrEmpty(ach))
                    achIds.Add(ach);
            }

            Log($"Submitting score with {achIds.Count} achievements (HTTP API)");

            // Store achievement IDs - queued AFTER score succeeds
            _deferredAchievementIds = new List<string>(achIds);
            _deferredAchievementsRemaining = 0;
            _deferredAchievementsSynced = new List<string>();

            // Submit score FIRST (creates/updates player profile on backend)
            string nick = !string.IsNullOrEmpty(_nickname) ? _nickname : GetDefaultNickname();
            var scoreBody = new Dictionary<string, object>
            {
                { "playerId", GetPlayerId() },
                { "gameId", gameId },
                { "score", score },
                { "streak", streak },
                { "nickname", nick }
            };
            if (!string.IsNullOrEmpty(_playSessionToken))
                scoreBody["playSessionToken"] = _playSessionToken;

            Log($"Submitting: score={score}, streak={streak}, nickname={nick}, gameId={gameId}, playerId={scoreBody["playerId"]}, session={(_playSessionToken.Length > 20 ? _playSessionToken.Substring(0, 20) : _playSessionToken)}");
            MakeHttpRequest("/scores", "POST", scoreBody, "submit_score");
        }

        // ============================================================
        // PUBLIC API - PLAY SESSIONS (Time Validation)
        // ============================================================

        public void StartPlaySession()
        {
            _playSessionToken = "";
            var body = new Dictionary<string, object>
            {
                { "gameId", gameId },
                { "playerId", GetPlayerId() }
            };
            MakeHttpRequest("/play-sessions/start", "POST", body, "start_play_session");
            Log($"Play session requested for game: {gameId}, player: {GetPlayerId()}");
        }

        public string GetPlaySessionToken() => _playSessionToken;
        public bool HasPlaySession() => !string.IsNullOrEmpty(_playSessionToken);

        public void EndPlaySession()
        {
            if (string.IsNullOrEmpty(_playSessionToken) || _playSessionToken.StartsWith("fallback_"))
            {
                Log("No active server session to end");
                _playSessionToken = "";
                return;
            }

            Log($"Ending play session on server: {_playSessionToken.Substring(0, Math.Min(30, _playSessionToken.Length))}");
            var body = new Dictionary<string, object> { { "playSessionToken", _playSessionToken } };
            MakeHttpRequest("/play-sessions/end", "POST", body, "end_play_session");
            _playSessionToken = "";
        }

        public void ClearPlaySession()
        {
            if (!string.IsNullOrEmpty(_playSessionToken) && !_playSessionToken.StartsWith("fallback_"))
                EndPlaySession();
            else
            {
                _playSessionToken = "";
                Log("Play session cleared (local only)");
            }
        }

        // ============================================================
        // PUBLIC API - LEADERBOARDS
        // ============================================================

        public void GetLeaderboard(string sortBy = "score", int limit = 1000)
        {
            string url = $"/leaderboard?sort={sortBy}&limit={limit}";
            MakeHttpRequest(url, "GET", new Dictionary<string, object>(), "leaderboard");
            Log($"Leaderboard requested (sort: {sortBy}, limit: {limit})");
        }

        public void GetPlayerRank(string sortBy = "score")
        {
            string url = $"/players/{Uri.EscapeDataString(GetPlayerId())}/rank?sort={sortBy}";
            MakeHttpRequest(url, "GET", new Dictionary<string, object>(), "player_rank");
            Log($"Player rank requested (sort: {sortBy})");
        }

        public void GetPlayerProfile(string playerId = "")
        {
            if (!string.IsNullOrEmpty(_sessionToken))
            {
                MakeHttpRequest("/auth/profile", "GET", new Dictionary<string, object>(), "player_profile");
                Log("Player profile requested (session)");
            }
            else
            {
                string pid = !string.IsNullOrEmpty(playerId) ? playerId : GetPlayerId();
                if (string.IsNullOrEmpty(pid))
                {
                    Log("No player ID for profile fetch");
                    OnNoProfile?.Invoke();
                    return;
                }
                string url = $"/players/{Uri.EscapeDataString(pid)}/profile";
                MakeHttpRequest(url, "GET", new Dictionary<string, object>(), "player_profile");
                Log($"Player profile requested for: {pid}");
            }
        }

        // ============================================================
        // PUBLIC API - SCOREBOARDS (Time-based Leaderboards)
        // ============================================================

        public void GetScoreboards(string forGameId = "")
        {
            string gid = !string.IsNullOrEmpty(forGameId) ? forGameId : gameId;
            if (string.IsNullOrEmpty(gid))
            {
                OnScoreboardError?.Invoke("Game ID not set. Call SetGameId() first.");
                return;
            }
            string url = $"/games/{Uri.EscapeDataString(gid)}/scoreboards";
            MakeHttpRequest(url, "GET", new Dictionary<string, object>(), "list_scoreboards");
            Log($"Scoreboards list requested for game: {gid}");
        }

        public void GetScoreboard(string scoreboardId, int limit = 100, string forGameId = "")
        {
            string gid = !string.IsNullOrEmpty(forGameId) ? forGameId : gameId;
            if (string.IsNullOrEmpty(gid))
            {
                OnScoreboardError?.Invoke("Game ID not set. Call SetGameId() first.");
                return;
            }
            string url = $"/games/{Uri.EscapeDataString(gid)}/scoreboards/{Uri.EscapeDataString(scoreboardId)}?limit={limit}";
            MakeHttpRequest(url, "GET", new Dictionary<string, object>(), "get_scoreboard",
                new Dictionary<string, object> { { "scoreboard_id", scoreboardId } });
            Log($"Scoreboard '{scoreboardId}' requested (limit: {limit})");
        }

        public void GetScoreboardRank(string scoreboardId, string forGameId = "")
        {
            string gid = !string.IsNullOrEmpty(forGameId) ? forGameId : gameId;
            if (string.IsNullOrEmpty(gid))
            {
                OnScoreboardError?.Invoke("Game ID not set. Call SetGameId() first.");
                return;
            }
            if (string.IsNullOrEmpty(_sessionToken))
            {
                OnScoreboardError?.Invoke("Session token required for rank lookup");
                return;
            }
            string url = $"/games/{Uri.EscapeDataString(gid)}/scoreboards/{Uri.EscapeDataString(scoreboardId)}/rank";
            MakeHttpRequest(url, "GET", new Dictionary<string, object>(), "scoreboard_rank",
                new Dictionary<string, object> { { "scoreboard_id", scoreboardId } });
            Log($"Scoreboard rank requested for '{scoreboardId}'");
        }

        public void GetWeeklyLeaderboard(int limit = 100, string forGameId = "") =>
            GetScoreboard("weekly-scoreboard", limit, forGameId);

        public void GetDailyLeaderboard(int limit = 100, string forGameId = "") =>
            GetScoreboard("daily", limit, forGameId);

        public void GetAlltimeLeaderboard(int limit = 100, string forGameId = "") =>
            GetScoreboard("all-time-new", limit, forGameId);

        public void GetMonthlyLeaderboard(int limit = 100, string forGameId = "") =>
            GetScoreboard("monthly", limit, forGameId);

        // ============================================================
        // PUBLIC API - SCOREBOARD ARCHIVES
        // ============================================================

        public void GetScoreboardArchives(string scoreboardId, string forGameId = "")
        {
            string gid = !string.IsNullOrEmpty(forGameId) ? forGameId : gameId;
            if (string.IsNullOrEmpty(gid))
            {
                OnArchiveError?.Invoke("Game ID not set. Call SetGameId() first.");
                return;
            }
            string url = $"/games/{Uri.EscapeDataString(gid)}/scoreboards/{Uri.EscapeDataString(scoreboardId)}/archives";
            MakeHttpRequest(url, "GET", new Dictionary<string, object>(), "list_archives",
                new Dictionary<string, object> { { "scoreboard_id", scoreboardId } });
            Log($"Archives list requested for '{scoreboardId}'");
        }

        public void GetLastArchivedScoreboard(string scoreboardId, int limit = 100, string forGameId = "")
        {
            string gid = !string.IsNullOrEmpty(forGameId) ? forGameId : gameId;
            if (string.IsNullOrEmpty(gid))
            {
                OnArchiveError?.Invoke("Game ID not set. Call SetGameId() first.");
                return;
            }
            string url = $"/games/{Uri.EscapeDataString(gid)}/scoreboards/{Uri.EscapeDataString(scoreboardId)}/archives/latest?limit={limit}";
            MakeHttpRequest(url, "GET", new Dictionary<string, object>(), "get_last_archive",
                new Dictionary<string, object> { { "scoreboard_id", scoreboardId } });
            Log($"Last archive requested for '{scoreboardId}'");
        }

        public void GetArchivedScoreboard(string archiveId, int limit = 100)
        {
            string url = $"/archives/{Uri.EscapeDataString(archiveId)}?limit={limit}";
            MakeHttpRequest(url, "GET", new Dictionary<string, object>(), "get_archive",
                new Dictionary<string, object> { { "archive_id", archiveId } });
            Log($"Archive '{archiveId}' requested");
        }

        public void GetArchivesInRange(string scoreboardId, long afterTimestamp, long beforeTimestamp, string forGameId = "")
        {
            string gid = !string.IsNullOrEmpty(forGameId) ? forGameId : gameId;
            if (string.IsNullOrEmpty(gid))
            {
                OnArchiveError?.Invoke("Game ID not set. Call SetGameId() first.");
                return;
            }
            string url = $"/games/{Uri.EscapeDataString(gid)}/scoreboards/{Uri.EscapeDataString(scoreboardId)}/archives?after={afterTimestamp}&before={beforeTimestamp}";
            MakeHttpRequest(url, "GET", new Dictionary<string, object>(), "list_archives",
                new Dictionary<string, object> { { "scoreboard_id", scoreboardId } });
            Log($"Archives in range requested for '{scoreboardId}'");
        }

        public void GetArchiveStats(string forGameId = "")
        {
            string gid = !string.IsNullOrEmpty(forGameId) ? forGameId : gameId;
            if (string.IsNullOrEmpty(gid))
            {
                OnArchiveError?.Invoke("Game ID not set. Call SetGameId() first.");
                return;
            }
            string url = $"/games/{Uri.EscapeDataString(gid)}/archives/stats";
            MakeHttpRequest(url, "GET", new Dictionary<string, object>(), "archive_stats");
            Log($"Archive stats requested for game: {gid}");
        }

        public void GetLastWeekScoreboard(int limit = 100, string forGameId = "") =>
            GetLastArchivedScoreboard("weekly", limit, forGameId);

        public void GetLastMonthScoreboard(int limit = 100, string forGameId = "") =>
            GetLastArchivedScoreboard("monthly", limit, forGameId);

        public void GetYesterdayScoreboard(int limit = 100, string forGameId = "") =>
            GetLastArchivedScoreboard("daily", limit, forGameId);

        // ============================================================
        // PUBLIC API - ACHIEVEMENTS
        // ============================================================

        public void UnlockAchievement(string achievementId, string achievementName = "", string achievementDesc = "")
        {
            var body = new Dictionary<string, object>
            {
                { "playerId", GetPlayerId() },
                { "achievementId", achievementId }
            };
            MakeHttpRequestAsync("/achievements", "POST", body, "unlock_achievement");
            Log($"Achievement unlock (async): {achievementId}");
        }

        /// <summary>Unlock multiple achievements in a single request.</summary>
        public void UnlockAchievementsBatch(List<string> achievementIds)
        {
            if (achievementIds == null || achievementIds.Count == 0) return;

            Log($"Batch unlocking {achievementIds.Count} achievements...");
            _deferredAchievementsRemaining = 1;
            _deferredAchievementsSynced = new List<string>();

            var body = new Dictionary<string, object>
            {
                { "playerId", GetPlayerId() },
                { "achievementIds", achievementIds }
            };
            MakeHttpRequestAsync("/achievements", "POST", body, "unlock_achievement_batch");
        }

        public void GetAchievements(string playerId = "")
        {
            string pid = !string.IsNullOrEmpty(playerId) ? playerId : GetPlayerId();
            string url = $"/players/{Uri.EscapeDataString(pid)}/achievements";
            MakeHttpRequest(url, "GET", new Dictionary<string, object>(), "achievements");
            Log($"Achievements requested for: {pid}");
        }

        /// <summary>Send all deferred achievements in a single batch request.</summary>
        private void FlushDeferredAchievements()
        {
            if (_deferredAchievementIds.Count == 0) return;
            int count = _deferredAchievementIds.Count;
            _deferredAchievementsRemaining = 1;
            _deferredAchievementsSynced = new List<string>();
            Log($"Batch syncing {count} achievements...");

            var body = new Dictionary<string, object>
            {
                { "playerId", GetPlayerId() },
                { "achievementIds", new List<string>(_deferredAchievementIds) }
            };
            MakeHttpRequestAsync("/achievements", "POST", body, "unlock_achievement_batch");
            _deferredAchievementIds.Clear();
        }

        // ============================================================
        // PUBLIC API - ANALYTICS
        // ============================================================

        public void TrackEvent(string eventType, Dictionary<string, object> metadata = null)
        {
            // TODO: POST to analytics endpoint when available
            Log($"Event tracked (local): {eventType} {(metadata != null ? DictToJson(metadata) : "")}");
        }

        // ============================================================
        // PUBLIC API - GAME INFO
        // ============================================================

        public void GetGameInfo() => MakeHttpRequest("/game", "GET", new Dictionary<string, object>(), "game_info");
        public void GetGameStats() => MakeHttpRequest("/game/stats", "GET", new Dictionary<string, object>(), "game_stats");
        public void HealthCheck() => MakeHttpRequest("/health", "GET", new Dictionary<string, object>(), "health");

        // ============================================================
        // DEBUG
        // ============================================================

        public void DebugStatus()
        {
            Debug.Log("");
            Debug.Log("╔══════════════════════════════════════════════╗");
            Debug.Log("║        CheddaBoards Debug Status v2.0.0      ║");
            Debug.Log("╠══════════════════════════════════════════════╣");
            Debug.Log($"║ Configuration                                ║");
            Debug.Log($"║  - Platform:         {Application.platform.ToString().PadRight(24)}║");
            Debug.Log($"║  - Init Complete:    {_initComplete.ToString().PadRight(24)}║");
            Debug.Log($"║  - Game ID:          {(gameId.Length > 20 ? gameId.Substring(0, 20) : gameId).PadRight(24)}║");
            Debug.Log($"║  - API Key Set:      {(!string.IsNullOrEmpty(apiKey)).ToString().PadRight(24)}║");
            Debug.Log($"║  - Session Token:    {(!string.IsNullOrEmpty(_sessionToken)).ToString().PadRight(24)}║");
            Debug.Log("╠══════════════════════════════════════════════╣");
            Debug.Log($"║ Authentication                               ║");
            Debug.Log($"║  - Authenticated:    {IsAuthenticated().ToString().PadRight(24)}║");
            Debug.Log($"║  - Auth Type:        {_authType.PadRight(24)}║");
            Debug.Log($"║  - Player ID:        {(GetPlayerId().Length > 20 ? GetPlayerId().Substring(0, 20) : GetPlayerId()).PadRight(24)}║");
            Debug.Log($"║  - Anonymous:        {IsAnonymous().ToString().PadRight(24)}║");
            Debug.Log("╠══════════════════════════════════════════════╣");
            Debug.Log($"║ Profile                                      ║");
            Debug.Log($"║  - Nickname:         {GetNickname().PadRight(24)}║");
            Debug.Log($"║  - High Score:       {GetHighScore().ToString().PadRight(24)}║");
            Debug.Log($"║  - Best Streak:      {GetBestStreak().ToString().PadRight(24)}║");
            Debug.Log($"║  - Play Count:       {GetPlayCount().ToString().PadRight(24)}║");
            Debug.Log("╠══════════════════════════════════════════════╣");
            Debug.Log($"║ State                                        ║");
            Debug.Log($"║  - Refreshing:       {_isRefreshingProfile.ToString().PadRight(24)}║");
            Debug.Log($"║  - Submitting:       {_isSubmittingScore.ToString().PadRight(24)}║");
            Debug.Log($"║  - HTTP Busy:        {_httpBusy.ToString().PadRight(24)}║");
            Debug.Log($"║  - Queue Size:       {_requestQueue.Count.ToString().PadRight(24)}║");
            Debug.Log($"║  - Play Session:     {HasPlaySession().ToString().PadRight(24)}║");
            Debug.Log($"║  - Device Code:      {(!string.IsNullOrEmpty(_deviceUserCode) ? _deviceUserCode : "none").PadRight(24)}║");
            Debug.Log($"║  - DC Polling:       {_isPollingDeviceCode.ToString().PadRight(24)}║");
            Debug.Log("╚══════════════════════════════════════════════╝");
            Debug.Log("");
        }

        // ============================================================
        // CLEANUP
        // ============================================================

        private void OnDestroy()
        {
            StopDeviceCodePolling();
        }

        // ============================================================
        // JSON HELPERS (Zero-dependency — no Newtonsoft required)
        // ============================================================

        /// <summary>Simple recursive JSON parser. Returns Dictionary, List, string, double, bool, or null.</summary>
        private static object ParseJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            int index = 0;
            return ParseValue(json, ref index);
        }

        private static object ParseValue(string json, ref int index)
        {
            SkipWhitespace(json, ref index);
            if (index >= json.Length) return null;

            char c = json[index];
            if (c == '{') return ParseObject(json, ref index);
            if (c == '[') return ParseArray(json, ref index);
            if (c == '"') return ParseString(json, ref index);
            if (c == 't' || c == 'f') return ParseBool(json, ref index);
            if (c == 'n') return ParseNull(json, ref index);
            return ParseNumber(json, ref index);
        }

        private static Dictionary<string, object> ParseObject(string json, ref int index)
        {
            var dict = new Dictionary<string, object>();
            index++; // skip '{'
            SkipWhitespace(json, ref index);
            if (index < json.Length && json[index] == '}') { index++; return dict; }

            while (index < json.Length)
            {
                SkipWhitespace(json, ref index);
                string key = ParseString(json, ref index);
                SkipWhitespace(json, ref index);
                if (index < json.Length && json[index] == ':') index++;
                object value = ParseValue(json, ref index);
                dict[key] = value;
                SkipWhitespace(json, ref index);
                if (index < json.Length && json[index] == ',') { index++; continue; }
                if (index < json.Length && json[index] == '}') { index++; break; }
                break;
            }
            return dict;
        }

        private static List<object> ParseArray(string json, ref int index)
        {
            var list = new List<object>();
            index++; // skip '['
            SkipWhitespace(json, ref index);
            if (index < json.Length && json[index] == ']') { index++; return list; }

            while (index < json.Length)
            {
                list.Add(ParseValue(json, ref index));
                SkipWhitespace(json, ref index);
                if (index < json.Length && json[index] == ',') { index++; continue; }
                if (index < json.Length && json[index] == ']') { index++; break; }
                break;
            }
            return list;
        }

        private static string ParseString(string json, ref int index)
        {
            if (index >= json.Length || json[index] != '"') return "";
            index++; // skip opening quote
            var sb = new StringBuilder();
            while (index < json.Length)
            {
                char c = json[index];
                if (c == '\\')
                {
                    index++;
                    if (index < json.Length)
                    {
                        char escaped = json[index];
                        switch (escaped)
                        {
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case '/': sb.Append('/'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'u':
                                if (index + 4 < json.Length)
                                {
                                    string hex = json.Substring(index + 1, 4);
                                    if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int code))
                                        sb.Append((char)code);
                                    index += 4;
                                }
                                break;
                            default: sb.Append(escaped); break;
                        }
                    }
                }
                else if (c == '"')
                {
                    index++; // skip closing quote
                    return sb.ToString();
                }
                else
                {
                    sb.Append(c);
                }
                index++;
            }
            return sb.ToString();
        }

        private static object ParseNumber(string json, ref int index)
        {
            int start = index;
            if (index < json.Length && json[index] == '-') index++;
            while (index < json.Length && char.IsDigit(json[index])) index++;
            bool isFloat = false;
            if (index < json.Length && json[index] == '.')
            {
                isFloat = true;
                index++;
                while (index < json.Length && char.IsDigit(json[index])) index++;
            }
            if (index < json.Length && (json[index] == 'e' || json[index] == 'E'))
            {
                isFloat = true;
                index++;
                if (index < json.Length && (json[index] == '+' || json[index] == '-')) index++;
                while (index < json.Length && char.IsDigit(json[index])) index++;
            }
            string numStr = json.Substring(start, index - start);
            if (isFloat)
            {
                if (double.TryParse(numStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double d))
                    return d;
                return 0.0;
            }
            if (long.TryParse(numStr, out long l))
            {
                if (l >= int.MinValue && l <= int.MaxValue) return (int)l;
                return l;
            }
            return 0;
        }

        private static bool ParseBool(string json, ref int index)
        {
            if (json.Substring(index).StartsWith("true")) { index += 4; return true; }
            if (json.Substring(index).StartsWith("false")) { index += 5; return false; }
            return false;
        }

        private static object ParseNull(string json, ref int index)
        {
            if (json.Substring(index).StartsWith("null")) { index += 4; }
            return null;
        }

        private static void SkipWhitespace(string json, ref int index)
        {
            while (index < json.Length && char.IsWhiteSpace(json[index])) index++;
        }

        /// <summary>Serialize a Dictionary to JSON string.</summary>
        private static string DictToJson(Dictionary<string, object> dict)
        {
            if (dict == null || dict.Count == 0) return "{}";
            var sb = new StringBuilder();
            sb.Append("{");
            bool first = true;
            foreach (var kvp in dict)
            {
                if (!first) sb.Append(",");
                first = false;
                sb.Append($"\"{EscapeJsonString(kvp.Key)}\":");
                sb.Append(ValueToJson(kvp.Value));
            }
            sb.Append("}");
            return sb.ToString();
        }

        private static string ValueToJson(object value)
        {
            if (value == null) return "null";
            if (value is bool b) return b ? "true" : "false";
            if (value is int i) return i.ToString();
            if (value is long l) return l.ToString();
            if (value is float f) return f.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (value is double d) return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (value is string s) return $"\"{EscapeJsonString(s)}\"";
            if (value is Dictionary<string, object> dict) return DictToJson(dict);
            if (value is List<string> strList)
            {
                var sb = new StringBuilder("[");
                for (int idx = 0; idx < strList.Count; idx++)
                {
                    if (idx > 0) sb.Append(",");
                    sb.Append($"\"{EscapeJsonString(strList[idx])}\"");
                }
                sb.Append("]");
                return sb.ToString();
            }
            if (value is List<object> objList)
            {
                var sb = new StringBuilder("[");
                for (int idx = 0; idx < objList.Count; idx++)
                {
                    if (idx > 0) sb.Append(",");
                    sb.Append(ValueToJson(objList[idx]));
                }
                sb.Append("]");
                return sb.ToString();
            }
            return $"\"{EscapeJsonString(value.ToString())}\"";
        }

        private static string EscapeJsonString(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        // ============================================================
        // UTILITY HELPERS
        // ============================================================

        private static string GetString(Dictionary<string, object> dict, string key, string defaultVal = "")
        {
            if (dict != null && dict.ContainsKey(key) && dict[key] != null)
                return dict[key].ToString();
            return defaultVal;
        }

        private static List<object> GetList(Dictionary<string, object> dict, string key)
        {
            if (dict != null && dict.ContainsKey(key) && dict[key] is List<object> list)
                return list;
            return new List<object>();
        }

        private string GetMetaString(string key)
        {
            return GetString(_currentMeta, key, "");
        }

        private static double GetUnixTime()
        {
            return (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }
    }
}
