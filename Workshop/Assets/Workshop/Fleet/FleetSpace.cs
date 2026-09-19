using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OpusSystems.Api;
using UnityEngine;

namespace OpusSystems.Workshop
{
    /// <summary>
    /// The room's brain. Owns the jarvis panel (the one you talk to), keeps a
    /// panel per other recent fleet session (spawned from the prefab, laid
    /// out on an arc, then left wherever you put them), and a fleet header
    /// with the rig line. Polls `/v1/sessions` and `/v1/rig`; each session
    /// panel streams its own events over its own WebSocket.
    ///
    /// Development input until voice (stage 4): the Mac writes a line to
    /// /data/local/tmp/opus-say.txt; new content is sent to the jarvis panel.
    /// The headset's key arrives once via `am start -e opus.key osk_…`.
    /// </summary>
    public sealed class FleetSpace : MonoBehaviour
    {
        [Tooltip("The jarvis panel placed in the scene.")]
        public SessionPanel panel;
        [Tooltip("Spawned for every other session.")]
        public SessionPanel panelPrefab;
        [TextArea] public string greeting = "I'm in the workshop with the headset on. Say hello in one short sentence.";
        public int maxSessionPanels = 5;
        public float arcRadius = 1.3f;
        public float arcDegrees = 34f;
        public float refreshSeconds = 10f;

        private OpusClient _api;
        private string _lastSay = "";
        private SessionPanel _header;
        private readonly Dictionary<string, SessionPanel> _panels = new Dictionary<string, SessionPanel>();
        private readonly List<SessionPanel> _spawned = new List<SessionPanel>();
        private float _nextRefresh;
        private Transform _head;

        private const string SayFile = "/data/local/tmp/opus-say.txt";
        private float _nextPoll;

        private void Start()
        {
            MainThread.Ensure();
            if (!panel) panel = FindFirstObjectByType<SessionPanel>();
            _head = Camera.main ? Camera.main.transform : transform;
            var key = IntentExtra("opus.key");
            if (!string.IsNullOrEmpty(key)) FleetConfig.ApiKey = key;
            if (!FleetConfig.HasKey)
            {
                panel?.Set("jarvis", "no key", "No API key on this headset.\n\nadb shell am start … -e opus.key osk_…");
                return;
            }
            _api = new OpusClient(FleetConfig.BaseUrl, FleetConfig.ApiKey);
            var say = IntentExtra("opus.say");
            _ = panel.StartAsync(_api, "jarvis", string.IsNullOrEmpty(say) ? greeting : say,
                "You are speaking through a Meta Quest 3 headset app called the Workshop: your replies appear on a floating panel in the user's real room, and other panels around it show the fleet's other sessions. Plain sentences, no markdown, one to three sentences.");
            _lastSay = say ?? "";
            try { if (System.IO.File.Exists(SayFile)) _lastSay = System.IO.File.ReadAllText(SayFile).Trim(); } catch { }

            _header = Spawn("fleet", Slot(0, up: true));
            _header.Set("fleet", "…", "");
            _nextRefresh = Time.unscaledTime + 2f;
        }

        private void Update()
        {
            if (_api == null) return;
            PollSayFile();
            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + refreshSeconds;
                _ = RefreshAsync();
            }
        }

        // ---- the fleet ---------------------------------------------------

        private async Task RefreshAsync()
        {
            try
            {
                var rigTask = _api.RigAsync();
                var sessions = await _api.ListSessionsAsync(limit: 12);
                var rig = await rigTask;
                MainThread.Run(() =>
                {
                    var rigLine = rig.Configured
                        ? (rig.Online ? $"rig · online · {string.Join(", ", rig.Models)}" : "rig · offline")
                        : "rig · not configured";
                    var lines = new System.Text.StringBuilder();
                    lines.AppendLine(rigLine);
                    lines.AppendLine();
                    foreach (var s in sessions.Data)
                    {
                        var id = s.Value<string>("id") ?? "";
                        var agent = s["metadata"]?.Value<string>("iron_fleet_agent") ?? "?";
                        var status = s.Value<string>("status") ?? "?";
                        var spent = s["usage"]?["list_cost"]?.Value<string>("amount") ?? "0";
                        lines.AppendLine($"{agent,-15} {status,-8} {spent,4}¢   {Short(s.Value<string>("title"))}");
                    }
                    _header.Set("fleet", $"{sessions.Data.Count} sessions", lines.ToString());
                    SyncPanels(sessions.Data);
                });
            }
            catch (System.Exception e)
            {
                MainThread.Run(() => _header?.Set("fleet", "error", e.Message));
            }
        }

        /// <summary>One panel per recent session other than jarvis's own, up to a limit.</summary>
        private void SyncPanels(IEnumerable<JObject> sessions)
        {
            var wanted = sessions
                .Where(s => s.Value<string>("id") != panel.SessionId)
                .Where(s => s["metadata"]?.Value<string>("iron_fleet_agent") != null)
                .Take(maxSessionPanels)
                .ToList();
            var keep = new HashSet<string>(wanted.Select(s => s.Value<string>("id")));
            foreach (var id in _panels.Keys.Where(k => !keep.Contains(k)).ToList())
            {
                Destroy(_panels[id].gameObject);
                _panels.Remove(id);
            }
            var slot = 1;
            foreach (var s in wanted)
            {
                var id = s.Value<string>("id");
                var agent = s["metadata"]?.Value<string>("iron_fleet_agent") ?? "agent";
                if (!_panels.ContainsKey(id))
                {
                    var p = Spawn(agent, Slot(slot, up: false));
                    _panels[id] = p;
                    _ = p.BindAsync(_api, id, agent);
                }
                slot++;
            }
        }

        private SessionPanel Spawn(string title, Pose pose)
        {
            var p = Instantiate(panelPrefab, pose.position, pose.rotation);
            p.name = $"Panel:{title}";
            p.Set(title, "", "");
            _spawned.Add(p);
            return p;
        }

        /// <summary>
        /// Positions on an arc around where the user stood at launch: slot 0
        /// is straight ahead (the jarvis panel's place); odd slots go right,
        /// even go left, alternating outward. `up` raises the header above.
        /// </summary>
        private Pose Slot(int slot, bool up)
        {
            var origin = panel ? panel.transform.position - new Vector3(0, 0, arcRadius) : _head.position;
            origin = new Vector3(_head.position.x, panel ? panel.transform.position.y : 1.4f, _head.position.z);
            var side = slot == 0 ? 0 : (slot % 2 == 1 ? 1 : -1);
            var ring = (slot + 1) / 2;
            var angle = side * ring * arcDegrees;
            var dir = Quaternion.Euler(0, angle, 0) * Vector3.forward;
            var pos = origin + dir * arcRadius + (up ? Vector3.up * 0.5f : Vector3.zero);
            var rot = Quaternion.LookRotation(pos - origin, Vector3.up);
            return new Pose(pos, rot);
        }

        private static string Short(string s) => string.IsNullOrEmpty(s) ? "" : (s.Length > 28 ? s.Substring(0, 27) + "…" : s);

        // ---- dev input --------------------------------------------------

        private void PollSayFile()
        {
            if (Time.unscaledTime < _nextPoll || panel == null || panel.Busy) return;
            _nextPoll = Time.unscaledTime + 0.5f;
            try
            {
                if (!System.IO.File.Exists(SayFile)) return;
                var text = System.IO.File.ReadAllText(SayFile).Trim();
                if (string.IsNullOrEmpty(text) || text == _lastSay) return;
                _lastSay = text;
                _ = panel.SendAsync(text);
            }
            catch { }
        }

        private static string IntentExtra(string name)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var intent = activity.Call<AndroidJavaObject>("getIntent");
                return intent.Call<string>("getStringExtra", name);
            }
            catch { return null; }
#else
            return System.Environment.GetEnvironmentVariable(name.Replace('.', '_').ToUpperInvariant());
#endif
        }

        private void OnDestroy() => _api?.Dispose();
    }
}
