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
        public int maxSessionPanels = 3;
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
        private Pose _origin = new Pose(new Vector3(0, 1.4f, 0), Quaternion.identity);
        private bool _laidOut;

        private void Start()
        {
            MainThread.Ensure();
            if (!panel) panel = FindFirstObjectByType<SessionPanel>();
            _head = Camera.main ? Camera.main.transform : transform;
            var key = IntentExtra("opus.key");
            if (!string.IsNullOrEmpty(key)) FleetConfig.ApiKey = key;
            if (!FleetConfig.HasKey)
            {
                // First launch: pair from the Mac, then boot for real.
                SetupPanel.Begin(panel, Boot);
                return;
            }
            Boot();
        }

        private void Boot()
        {
            _api = new OpusClient(FleetConfig.BaseUrl, FleetConfig.ApiKey);
            // Jarvis's voice, spatialised at the jarvis panel.
            var voiceGo = new GameObject("JarvisVoice");
            voiceGo.transform.SetParent(panel.transform, false);
            var voice = voiceGo.AddComponent<FleetVoicePlayer>();
            voice.baseUrl = FleetConfig.BaseUrl;
            voice.apiKey = FleetConfig.ApiKey;
            panel.voice = voice;
            // Your voice in: Wit token delivered once (am start -e opus.wit …),
            // kept in PlayerPrefs like the API key.
            var wit = IntentExtra("opus.wit");
            if (!string.IsNullOrEmpty(wit)) FleetConfig.WitToken = wit;
            var talk = new GameObject("JarvisTalk").AddComponent<JarvisTalk>();
            talk.panel = panel;
            talk.witClientToken = FleetConfig.WitToken;
            // The Mac's music in the room: address handed over once (am start -e opus.speaker host:port).
            var speaker = IntentExtra("opus.speaker");
            if (!string.IsNullOrEmpty(speaker)) FleetConfig.Speaker = speaker;
            var say = IntentExtra("opus.say");
            panel.ToolHandler = RunToolAsync;
            panel.RemoteTools = MacTools;
            // If the conversation is gone for good, start a fresh one in place.
            panel.OnSessionEnded = _ => { PlayerPrefs.DeleteKey(SessionPref); var restart = BootJarvisAsync(null); };
            _ = BootJarvisAsync(say);
            _lastSay = say ?? "";
            try { if (System.IO.File.Exists(SayFile)) _lastSay = System.IO.File.ReadAllText(SayFile).Trim(); } catch { }

            _header = Spawn("fleet", Slot(0, up: true));
            _header.Set("fleet", "…", "");
            _nextRefresh = Time.unscaledTime + 2f;
        }

        private void Update()
        {
            if (_api == null) return;
            TryLayout();
            UpdateSpeakerUi();
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
                    if (_laidOut) SyncPanels(sessions.Data);
                });
            }
            catch (System.Exception e)
            {
                MainThread.Run(() => _header?.Set("fleet", "error", e.Message));
            }
        }

        // ---- jarvis: one continuing conversation, with this room's tools -----

        private const string SessionPref = "opus.jarvis.session";
        private const string ToolsPref = "opus.jarvis.tools";
        /// <summary>Bump when the tool set changes: tools are fixed at session create.</summary>
        private const string ToolsVersion = "desk-1";
        private const int ReuseCostLimitCents = 30;

        /// <summary>Tools the Mac answers (its Music app), declared here so Jarvis has them in the room.</summary>
        public static readonly HashSet<string> MacTools = new HashSet<string> { "play_music", "music_control", "list_playlists", "queue_music" };

        private static List<CustomTool> JarvisTools => new List<CustomTool>
        {
            new CustomTool
            {
                Name = "play_music",
                Description = "Play something from the user's Apple Music library on the Mac in the room: a song title, an artist or album (plays everything matching), or one of the user's playlists by name. Matching is case-insensitive and partial. Reports what started playing, or that nothing matched.",
                InputSchema = JObject.Parse(@"{""type"":""object"",""properties"":{""title"":{""type"":""string"",""description"":""Song title, for one specific song""},""artist"":{""type"":""string""},""album"":{""type"":""string""},""playlist"":{""type"":""string"",""description"":""One of the user's playlist names""},""shuffle"":{""type"":""boolean""}},""additionalProperties"":false}"),
            },
            new CustomTool
            {
                Name = "music_control",
                Description = "Control the Music app on the Mac: pause, resume, skip forward or back, report what's playing, set the volume, or toggle shuffle.",
                InputSchema = JObject.Parse(@"{""type"":""object"",""properties"":{""action"":{""type"":""string"",""enum"":[""pause"",""resume"",""next"",""previous"",""now_playing"",""set_volume"",""shuffle_on"",""shuffle_off""]},""volume"":{""type"":""integer"",""minimum"":0,""maximum"":100}},""required"":[""action""],""additionalProperties"":false}"),
            },
            new CustomTool
            {
                Name = "calculate",
                Description = "Work out an arithmetic expression exactly and show it on the calculator panel in the room. Supports + - * / ^, parentheses, sqrt(), and a postfix percent (17% of 340 → \"17% * 340\"). Returns \"expression = result\".",
                InputSchema = JObject.Parse(@"{""type"":""object"",""properties"":{""expression"":{""type"":""string""}},""required"":[""expression""]}"),
            },
            new CustomTool
            {
                Name = "open_panel",
                Description = "Open a panel in the room. A layer of the Opus stack — github, uptimerobot, droplet, docker, tailscale, cloudflare — opens its live status panel and returns its state and headline. A desk panel — jarvis, fleet, calculator, music, opus — is brought back if it was closed.",
                InputSchema = JObject.Parse(@"{""type"":""object"",""properties"":{""service"":{""type"":""string"",""enum"":[""github"",""uptimerobot"",""droplet"",""docker"",""tailscale"",""cloudflare"",""jarvis"",""fleet"",""calculator"",""music"",""opus""]}},""required"":[""service""]}"),
            },
            new CustomTool
            {
                Name = "reset_room",
                Description = "Put every panel back in its default place around the user, forget saved positions, and close the transient ones.",
            },
            new CustomTool
            {
                Name = "close_panel",
                Description = "Close an open status panel: one service name, or \"all\".",
                InputSchema = JObject.Parse(@"{""type"":""object"",""properties"":{""service"":{""type"":""string""}},""required"":[""service""]}"),
            },
            new CustomTool
            {
                Name = "list_playlists",
                Description = "List the user's Apple Music playlists by name with their track counts.",
            },
            new CustomTool
            {
                Name = "queue_music",
                Description = "Add music to the end of the queue without interrupting what's playing: a song title, an artist, an album, or a playlist name.",
                InputSchema = JObject.Parse(@"{""type"":""object"",""properties"":{""title"":{""type"":""string""},""artist"":{""type"":""string""},""album"":{""type"":""string""},""playlist"":{""type"":""string""}},""additionalProperties"":false}"),
            },
            new CustomTool
            {
                Name = "list_models",
                Description = "List the 3D-print files (STL/GLB) stored on this headset, by file name.",
            },
            new CustomTool
            {
                Name = "show_model",
                Description = "Show a 3D print on the stand in the room, turning slowly, scaled to fit the hand. Give a file name from list_models (loose matches are fine) or an http(s) URL to an .stl/.glb file. Returns the model's size in millimetres and triangle count.",
                InputSchema = JObject.Parse(@"{""type"":""object"",""properties"":{""name"":{""type"":""string"",""description"":""file name on the headset""},""url"":{""type"":""string"",""description"":""http(s) URL of an .stl/.glb""}}}"),
            },
        };

        /// <summary>
        /// Pick up the last conversation if it is still usable — same tool
        /// set, idle, not much spent — otherwise start a new one. Either way
        /// Jarvis says something, so you know he's there.
        /// </summary>
        private async Task BootJarvisAsync(string say)
        {
            var suffix = "You are speaking through a Meta Quest 3 headset app called the Workshop: your replies appear on a floating panel in the user's real room, and other panels around it show the fleet's other sessions. You can show 3D prints on a stand with show_model, open a status panel for any layer of the Opus stack with open_panel (github, uptimerobot, droplet, docker, tailscale, cloudflare) or bring back a closed desk panel (jarvis, fleet, calculator, music, opus), close panels with close_panel, put the room back in order with reset_room, work sums out on the calculator panel with calculate (use it for any arithmetic rather than doing it in your head), and play the user's Apple Music through the Mac in the room with play_music, queue_music, list_playlists and music_control. Search the web when an answer needs current facts, and say when you did. Plain sentences, no markdown, one to three sentences.";
            var saved = PlayerPrefs.GetString(SessionPref, "");
            if (!string.IsNullOrEmpty(saved) && PlayerPrefs.GetString(ToolsPref, "") == ToolsVersion && string.IsNullOrEmpty(say))
            {
                try
                {
                    var s = await _api.GetSessionAsync(saved);
                    var status = s.Value<string>("status") ?? "";
                    int.TryParse(s["usage"]?["list_cost"]?.Value<string>("amount") ?? "0", out var spent);
                    if (status == "idle" && spent < ReuseCostLimitCents)
                    {
                        await panel.BindAsync(_api, saved, "jarvis");
                        await panel.SendAsync("I'm back in the workshop with the headset on. One short sentence.");
                        return;
                    }
                    Debug.Log($"jarvis session {saved} not reused: {status}, {spent}¢");
                }
                catch (System.Exception e) { Debug.Log($"jarvis session {saved} not reused: {e.Message}"); }
            }
            await panel.StartAsync(_api, "jarvis", string.IsNullOrEmpty(say) ? greeting : say, suffix, JarvisTools);
            if (!string.IsNullOrEmpty(panel.SessionId))
            {
                PlayerPrefs.SetString(SessionPref, panel.SessionId);
                PlayerPrefs.SetString(ToolsPref, ToolsVersion);
                PlayerPrefs.Save();
            }
        }

        private PrintPreview _print;

        private async Task<string> RunToolAsync(string name, JObject input)
        {
            switch (name)
            {
                case "list_models":
                    var files = ModelLibrary.List();
                    return files.Length == 0
                        ? $"No models on the headset. Push .stl/.glb files to {ModelLibrary.Folder}."
                        : string.Join("\n", files);
                case "open_panel":
                    var what = (input.Value<string>("service") ?? "").Trim().ToLowerInvariant();
                    var deskKey = what switch { "jarvis" => "jarvis", "fleet" => "fleet", "calculator" => "calc", "calc" => "calc", "music" => "speaker", "opus" => "hub", "hub" => "hub", _ => null };
                    if (deskKey != null) return ShowCore(deskKey) ? $"{what} panel is back" : $"there is no {what} panel";
                    return await OpenOpsAsync(what);
                case "reset_room":
                    ResetRoom();
                    return "the room is reset";
                case "close_panel":
                    return CloseOps(input.Value<string>("service") ?? "");
                case "calculate":
                    EnsureCalculator();
                    return _calculator.Evaluate(input.Value<string>("expression") ?? "");
                case "show_model":
                    var url = input.Value<string>("url");
                    var source = !string.IsNullOrWhiteSpace(url) ? url : ModelLibrary.Resolve(input.Value<string>("name") ?? "");
                    if (source == null) throw new System.Exception($"no model matching '{input.Value<string>("name")}'; list_models has the names");
                    if (!_print)
                    {
                        var stand = Spawn("print", PrintSlot());
                        stand.transform.localScale *= 0.5f;
                        _print = stand.gameObject.AddComponent<PrintPreview>();
                        _print.stand = stand;
                        stand.OnClosed = _ => _print = null;
                    }
                    await _print.LoadAsync(source);
                    return "Showing " + _print.Summary;
                default:
                    throw new System.Exception($"unknown tool {name}");
            }
        }

        /// <summary>The print stand: forward-left of the jarvis panel, lower, where a hand reaches.</summary>
        private Pose PrintSlot()
        {
            var pos = _origin.position + _origin.rotation * new Vector3(-0.55f, -0.45f, 1.0f);
            var look = Quaternion.LookRotation(Vector3.ProjectOnPlane(pos - _origin.position, Vector3.up), Vector3.up);
            return new Pose(pos, look);
        }

        /// <summary>
        /// A panel for each session that is actually working — running, or
        /// stopped waiting on a tool result — up to a small limit. Idle and
        /// finished sessions are lines in the fleet header, not screens; and
        /// other jarvis sessions (past conversations, the Mac app's) never
        /// get one — the jarvis panel in front of you is the only jarvis.
        /// </summary>
        private void SyncPanels(IEnumerable<JObject> sessions)
        {
            var wanted = sessions
                .Where(s => s.Value<string>("id") != panel.SessionId)
                .Where(s => s["metadata"]?.Value<string>("iron_fleet_agent") is string agent && agent != "jarvis")
                .Where(s => s.Value<string>("status") == "running" || _panels.ContainsKey(s.Value<string>("id")))
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
        /// Positions on an arc around where the user stood when the room
        /// was laid out: slot 0 is straight ahead (the jarvis panel's place);
        /// odd slots go right, even go left, alternating outward on a wider,
        /// slightly lower ring so they frame the jarvis panel instead of
        /// crowding it. `up` raises the header above slot 0.
        /// </summary>
        private Pose Slot(int slot, bool up)
        {
            var origin = new Vector3(_origin.position.x, _origin.position.y - 0.15f, _origin.position.z);
            var side = slot == 0 ? 0 : (slot % 2 == 1 ? 1 : -1);
            var ring = (slot + 1) / 2;
            var angle = side * ring * arcDegrees;
            var radius = slot == 0 ? arcRadius : arcRadius + 0.4f;
            var dir = _origin.rotation * Quaternion.Euler(0, angle, 0) * Vector3.forward;
            var pos = origin + dir * radius + Vector3.up * (up ? 0.45f : (slot == 0 ? 0f : -0.1f));
            var rot = Quaternion.LookRotation(pos - origin, Vector3.up);
            return new Pose(pos, rot);
        }

        /// <summary>
        /// Where you were and which way you faced when tracking first
        /// reported a real head pose. Everything is placed relative to it,
        /// once — panels do not follow you around the room.
        /// </summary>
        private bool TryLayout()
        {
            if (_laidOut) return true;
            if (_head == transform || _head.position.y < 0.3f) return false; // no tracking yet
            var fwd = Vector3.ProjectOnPlane(_head.forward, Vector3.up);
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            _origin = new Pose(_head.position, Quaternion.LookRotation(fwd.normalized, Vector3.up));
            _laidOut = true;
            LayoutDesk(restore: true);
            return true;
        }

        /// <summary>
        /// Every core panel to its default spot around the origin. With
        /// `restore`, a remembered anchor wins; without, the defaults are
        /// pinned fresh (a room reset).
        /// </summary>
        private void LayoutDesk(bool restore)
        {
            if (panel) Core(panel, "jarvis", Slot(0, up: false), restore);
            if (_header) Core(_header, "fleet", Slot(0, up: true), restore);
            SpawnSpeaker();
            EnsureCalculator();
            SpawnHub();
            if (_speakerPanel) Core(_speakerPanel, "speaker", DeskPose(0.75f, -0.35f, 1.1f), restore);
            if (_calculator) Core(_calculator.GetComponent<SessionPanel>(), "calc", DeskPose(-0.95f, -0.15f, 1.05f), restore);
            if (_hub) Core(_hub.GetComponent<SessionPanel>(), "hub", DeskPose(-0.75f, 0.45f, 1.25f), restore);
        }

        /// <summary>A desk spot relative to the origin, turned to face it.</summary>
        private Pose DeskPose(float right, float up, float forward)
        {
            var pos = _origin.position + _origin.rotation * new Vector3(right, up, forward);
            return new Pose(pos, Quaternion.LookRotation(_origin.rotation * new Vector3(right, 0, forward), Vector3.up));
        }

        private readonly Dictionary<string, SessionPanel> _core = new Dictionary<string, SessionPanel>();

        /// <summary>Register a core panel: hides on ×, comes back from the hub, remembers its place.</summary>
        private void Core(SessionPanel p, string key, Pose pose, bool restore)
        {
            _core[key] = p;
            p.closeMode = SessionPanel.CloseMode.Hide;
            p.gameObject.SetActive(true);
            p.transform.SetPositionAndRotation(pose.position, pose.rotation);
            var a = p.GetComponent<PanelAnchor>();
            if (a == null) { a = p.gameObject.AddComponent<PanelAnchor>(); a.key = key; }
            _ = restore ? RestoreOrPin(a) : a.PinAsync();
        }

        private static async Task RestoreOrPin(PanelAnchor a)
        {
            if (!await a.RestoreAsync()) await a.PinAsync();
        }

        /// <summary>Bring a hidden core panel back (the hub's Desk row, or "open the calculator").</summary>
        public bool ShowCore(string key)
        {
            if (!_core.TryGetValue(key, out var p) || !p) return false;
            if (p.gameObject.activeSelf) return true; // already out; leave it where it is
            p.gameObject.SetActive(true);
            // It was closed: put it back in front of you rather than wherever it was.
            var mover = p.GetComponent<PanelMover>();
            var pose = DeskPose(0, -0.1f, 1.1f);
            p.transform.SetPositionAndRotation(pose.position, mover ? mover.Facing(pose.position) : pose.rotation);
            var a = p.GetComponent<PanelAnchor>();
            if (a) _ = a.PinAsync();
            return true;
        }

        /// <summary>Drop the key and extras; the next launch pairs again.</summary>
        private void ForgetHeadset()
        {
            FleetConfig.Clear();
            if (_hub) _hub.GetComponent<SessionPanel>().Set("Opus Systems", "forgotten", "This headset's key is gone.\nQuit the app and open it again to pair.");
        }

        /// <summary>Everything back to its default spot, anchors forgotten, transient panels gone.</summary>
        public void ResetRoom()
        {
            foreach (var key in new[] { "jarvis", "fleet", "speaker", "calc", "hub" }) PanelAnchor.Forget(key);
            foreach (var kv in _opsPanels.ToList()) { PanelAnchor.Forget("ops-" + kv.Key); if (kv.Value) Destroy(kv.Value.gameObject); }
            _opsPanels.Clear();
            if (_print) { Destroy(_print.gameObject); _print = null; }
            var fwd = Vector3.ProjectOnPlane(_head.forward, Vector3.up);
            if (fwd.sqrMagnitude < 0.01f) fwd = _origin.rotation * Vector3.forward;
            _origin = new Pose(_head.position, Quaternion.LookRotation(fwd.normalized, Vector3.up));
            LayoutDesk(restore: false);
        }

        // ---- the Opus launcher and its service panels ------------------------

        private OpsHubPanel _hub;
        private readonly Dictionary<string, SessionPanel> _opsPanels = new Dictionary<string, SessionPanel>();

        private void SpawnHub()
        {
            if (_hub) return;
            // Above the ring, left of the fleet header.
            var pose = new Pose(
                _origin.position + _origin.rotation * new Vector3(-0.75f, 0.45f, 1.25f),
                Quaternion.LookRotation(_origin.rotation * new Vector3(-0.75f, 0, 1.25f), Vector3.up));
            var stand = Spawn("Opus Systems", pose);
            _hub = OpsHubPanel.Attach(stand, _api);
            _hub.OnOpen = id => _ = OpenOpsAsync(id);
            _hub.OnDesk = key => ShowCore(key);
            _hub.OnReset = ResetRoom;
            _hub.OnForget = ForgetHeadset;
        }

        private static readonly string[] OpsIds = { "github", "uptimerobot", "droplet", "docker", "tailscale", "cloudflare" };

        private async Task<string> OpenOpsAsync(string service)
        {
            service = service.Trim().ToLowerInvariant();
            if (System.Array.IndexOf(OpsIds, service) < 0) throw new System.Exception($"no layer called '{service}'; one of {string.Join(", ", OpsIds)}");
            if (!_opsPanels.TryGetValue(service, out var p) || !p)
            {
                // Open panels line up on a second ring above the sessions, outward from the hub.
                var n = _opsPanels.Count(kv => kv.Value);
                var slot = 1 + n;
                var pose = Slot(slot, up: true);
                p = Spawn(service, pose);
                _opsPanels[service] = p;
                var ops = OpsPanel.Attach(p, _api, service);
                ops.OnClose = id => CloseOps(id);
                _ = Anchor(p, "ops-" + service);
                p.OnClosed = _ => { PanelAnchor.Forget("ops-" + service); _opsPanels.Remove(service); };
            }
            var s = await _api.OpsAsync(service);
            return $"{s.Value<string>("name")}: {s.Value<string>("state")} — {s.Value<string>("headline")}";
        }

        private string CloseOps(string service)
        {
            service = service.Trim().ToLowerInvariant();
            var ids = service == "all" ? _opsPanels.Keys.ToList() : new List<string> { service };
            var closed = 0;
            foreach (var id in ids)
            {
                if (_opsPanels.TryGetValue(id, out var p) && p) { Destroy(p.gameObject); closed++; }
                _opsPanels.Remove(id);
            }
            return closed == 0 ? $"no {service} panel is open" : $"closed {closed}";
        }

        // ---- the calculator, a desk tool that is always there ----------------

        private CalculatorPanel _calculator;

        private void EnsureCalculator()
        {
            if (_calculator) return;
            var pose = new Pose(
                _origin.position + _origin.rotation * new Vector3(-0.95f, -0.15f, 1.05f),
                Quaternion.LookRotation(_origin.rotation * new Vector3(-0.95f, 0, 1.05f), Vector3.up));
            var stand = Spawn("calculator", pose);
            stand.transform.localScale *= 0.8f;
            _calculator = CalculatorPanel.Attach(stand);
        }

        // ---- the Mac's music, from a speaker you can move ------------------

        private SessionPanel _speakerPanel;
        private MacSpeaker _speaker;

        private void SpawnSpeaker()
        {
            var addr = FleetConfig.Speaker;
            if (string.IsNullOrEmpty(addr) || _speakerPanel) return;
            var parts = addr.Split(':');
            var pose = new Pose(
                _origin.position + _origin.rotation * new Vector3(0.75f, -0.35f, 1.1f),
                Quaternion.LookRotation(_origin.rotation * new Vector3(0.75f, 0, 1.1f), Vector3.up));
            _speakerPanel = Spawn("music", pose);
            _speakerPanel.transform.localScale *= 0.7f;
            _speakerPanel.Set("music", "connecting…", "The Mac's Music app, heard here.");
            _speaker = _speakerPanel.gameObject.AddComponent<MacSpeaker>();
            _speaker.host = parts[0];
            if (parts.Length > 1 && int.TryParse(parts[1], out var port)) _speaker.port = port;
            // The media player on the same panel: artwork, track, transport.
            MediaPanel.Attach(_speakerPanel, parts[0]);
        }

        private float _nextSpeakerUi;

        private void UpdateSpeakerUi()
        {
            if (!_speaker || Time.unscaledTime < _nextSpeakerUi) return;
            _nextSpeakerUi = Time.unscaledTime + 0.2f;
            if (!_speaker.Connected && _speakerPanel.status) _speakerPanel.status.text = _speaker.State;
        }

        /// <summary>
        /// The two fixed panels remember their place in the room: restore the
        /// saved anchor, or pin the default spot so next launch finds it.
        /// </summary>
        private static async Task Anchor(SessionPanel p, string key)
        {
            var a = p.gameObject.AddComponent<PanelAnchor>();
            a.key = key;
            if (!await a.RestoreAsync()) await a.PinAsync();
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
