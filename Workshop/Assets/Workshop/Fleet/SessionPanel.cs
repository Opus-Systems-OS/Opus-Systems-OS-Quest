using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OpusSystems.Api;
using UnityEngine;
using UnityEngine.UI;

namespace OpusSystems.Workshop
{
    /// <summary>
    /// One floating panel bound to one fleet session. The reply streams in
    /// as deltas (spoken by the orb later); the authoritative event replaces
    /// the preview; status and cost follow the session. Grabbable body,
    /// world-space canvas.
    /// </summary>
    public sealed class SessionPanel : MonoBehaviour
    {
        public Text title;
        public Text body;
        public Text status;

        public string SessionId { get; private set; } = "";
        public bool Busy { get; private set; }
        /// <summary>When set, streamed replies are spoken as they arrive.</summary>
        public FleetVoicePlayer voice;

        private OpusClient _api;
        private SessionSocket _ws;
        private readonly StringBuilder _transcript = new StringBuilder();
        private readonly StringBuilder _partial = new StringBuilder();
        private const int MaxChars = 1400;

        public void Set(string titleText, string statusText, string bodyText)
        {
            if (title) title.text = titleText;
            if (status) status.text = statusText;
            if (body) body.text = bodyText;
        }

        /// <summary>
        /// Runs a session-local custom tool the agent asked for and returns
        /// the result text; throw to answer with an error result. Set it
        /// before <see cref="StartAsync"/> / <see cref="BindAsync"/>.
        /// </summary>
        public Func<string, JObject, Task<string>> ToolHandler;

        /// <summary>
        /// Tools declared on this session that another device answers (the
        /// Mac runs the Music app). Calls to these are not run here; if no
        /// result arrives within <see cref="RemoteTimeoutSeconds"/> the
        /// panel answers with an error so the session never hangs.
        /// </summary>
        public HashSet<string> RemoteTools = new HashSet<string>();
        public float RemoteTimeoutSeconds = 25f;
        public string RemoteOwner = "the Mac";
        private readonly HashSet<string> _answeredRemote = new HashSet<string>();

        /// <summary>Start a conversation with an agent; the first message opens the session.</summary>
        public async Task StartAsync(OpusClient api, string agentSlug, string firstMessage, string systemSuffix, List<CustomTool> tools = null)
        {
            _api = api;
            Busy = true;
            Set(agentSlug, "starting…", "");
            try
            {
                var created = await api.CreateSessionAsync(new CreateSessionRequest
                {
                    AgentSlug = agentSlug,
                    Task = firstMessage,
                    SystemSuffix = systemSuffix,
                    Tools = tools,
                    Client = "quest",
                });
                SessionId = created.SessionId;
                Line("You", firstMessage);
                await AttachAsync(history: false);
            }
            catch (Exception e)
            {
                MainThread.Run(() => { Fail(e); });
            }
        }

        /// <summary>Bind to an existing session (history first, then live).</summary>
        public async Task BindAsync(OpusClient api, string sessionId, string agentSlug)
        {
            _api = api;
            SessionId = sessionId;
            Set(agentSlug, "loading…", "");
            try { await AttachAsync(history: true); }
            catch (Exception e) { MainThread.Run(() => Fail(e)); }
        }

        public async Task SendAsync(string text)
        {
            if (_ws == null || string.IsNullOrWhiteSpace(text)) return;
            Busy = true;
            MainThread.Run(() => { Line("You", text); SetStatus("thinking…"); });
            try { await _ws.SendAsync(text); }
            catch (Exception e) { MainThread.Run(() => Fail(e)); }
        }

        public async Task InterruptAsync()
        {
            voice?.Stop();
            if (_ws == null) return;
            try { await _ws.InterruptAsync(); } catch (Exception e) { MainThread.Run(() => Fail(e)); }
        }

        private async Task AttachAsync(bool history)
        {
            _ws = await _api.OpenSessionSocketAsync(SessionId, history: history, deltas: true);
            _ws.OnDelta += (_, fragment) => MainThread.Run(() =>
            {
                if (_partial.Length == 0) SetStatus("speaking…");
                _partial.Append(fragment);
                voice?.Feed(fragment);
                Render();
            });
            _ws.OnEvent += ev => MainThread.Run(() => OnEvent(ev));
            _ws.OnError += (t, m) => MainThread.Run(() => { SetStatus($"error: {t}"); Line("!", m); Busy = false; });
            _ws.OnClosed += reason => MainThread.Run(() => { SetStatus($"closed ({reason})"); _ws = null; Busy = false; });
            MainThread.Run(() => SetStatus(history ? "idle" : "thinking…"));
            _ = RefreshCostAsync();
        }

        private async Task RunToolAsync(string id, string name, JObject input)
        {
            if (RemoteTools.Contains(name))
            {
                _ = WaitForRemoteAsync(id, name);
                return;
            }
            string result;
            var isError = false;
            try
            {
                if (ToolHandler == null) throw new InvalidOperationException($"this client has no tool named {name}");
                result = await ToolHandler(name, input);
            }
            catch (Exception e) { result = e.Message; isError = true; }
            Line("tool", $"{name} → {result}");
            try { if (_ws != null) await _ws.SendToolResultAsync(id, result, isError); }
            catch (Exception e) { MainThread.Run(() => Fail(e)); }
        }

        /// <summary>A remote tool call: show who we're waiting on; give up after the timeout.</summary>
        private async Task WaitForRemoteAsync(string id, string name)
        {
            SetStatus($"{name} · waiting on {RemoteOwner}…");
            var deadline = Time.unscaledTime + RemoteTimeoutSeconds;
            while (Time.unscaledTime < deadline)
            {
                await Task.Delay(500);
                if (_answeredRemote.Contains(id) || _ws == null) return;
            }
            if (_answeredRemote.Contains(id)) return;
            _answeredRemote.Add(id);
            Line("tool", $"{name} → {RemoteOwner} didn't answer");
            try { await _ws.SendToolResultAsync(id, $"{RemoteOwner} isn't available right now — is the Jarvis app open there?", isError: true); }
            catch (Exception e) { MainThread.Run(() => Fail(e)); }
        }

        private void OnEvent(JObject ev)
        {
            switch (Events.Type(ev))
            {
                case "user.custom_tool_result":
                    // Another device answered one of this session's tools.
                    var answeredId = ev.Value<string>("custom_tool_use_id") ?? "";
                    if (_answeredRemote.Add(answeredId)) Line("tool", $"answered by {RemoteOwner}");
                    break;
                case "user.message":
                    // Already echoed locally for our own sends; history replays come here.
                    if (_transcript.Length == 0 || !_transcript.ToString().EndsWith(Events.Text(ev) + "\n"))
                        Line("You", Events.Text(ev));
                    break;
                case "agent.message":
                    _partial.Clear();
                    voice?.Flush();
                    Line(title ? title.text : "agent", Events.Text(ev));
                    break;
                case "agent.custom_tool_use":
                    SetStatus($"tool: {Events.ToolName(ev)}");
                    _ = RunToolAsync(Events.Id(ev), Events.ToolName(ev) ?? "", Events.ToolInput(ev));
                    break;
                case "session.status_running":
                    SetStatus("thinking…");
                    break;
                case "session.status_idle":
                    if (Events.RequiresAction(ev)) { SetStatus("waiting on a tool"); break; }
                    var reason = Events.StopReason(ev);
                    SetStatus(reason == "end_turn" ? "idle" : $"idle · {reason}");
                    Busy = false;
                    _ = RefreshCostAsync();
                    break;
                case "session.error":
                    SetStatus("error");
                    Line("!", Events.Error(ev) ?? "session error");
                    Busy = false;
                    break;
            }
        }

        private async Task RefreshCostAsync()
        {
            if (_api == null || string.IsNullOrEmpty(SessionId)) return;
            try
            {
                var s = await _api.GetSessionAsync(SessionId);
                var spent = s["usage"]?["list_cost"]?.Value<string>("amount") ?? "0";
                var cap = s["budget"]?["max_list_cost"]?.Value<string>("amount");
                MainThread.Run(() => _cost = cap != null ? $"{spent}¢ / {cap}¢" : $"{spent}¢");
                MainThread.Run(Render);
            }
            catch { /* cosmetic */ }
        }

        private string _cost = "";
        private string _status = "idle";
        private string _draft = "";
        private bool _listening;

        /// <summary>Live dictation, shown as "You: …" until sent.</summary>
        public void ShowDraft(string text) { _draft = text; Render(); }
        public void SetListening(bool on) { _listening = on; Render(); }

        private void SetStatus(string s) { _status = s; Render(); }

        private void Line(string who, string text)
        {
            _transcript.Append(who).Append(": ").Append(text).Append('\n');
            if (_transcript.Length > MaxChars) _transcript.Remove(0, _transcript.Length - MaxChars);
            Render();
        }

        private void Fail(Exception e)
        {
            Busy = false;
            var msg = e is OpusApiException oe ? $"{(int)oe.Status} {oe.Type}: {oe.Message}" : e.Message;
            SetStatus("error");
            Line("!", msg);
        }

        private void Render()
        {
            var st = _listening ? "listening…" : _status;
            if (status) status.text = string.IsNullOrEmpty(_cost) ? st : $"{st}  ·  {_cost}";
            if (!body) return;
            var shown = _transcript.ToString();
            if (_partial.Length > 0) shown += (title ? title.text : "agent") + ": " + _partial + " ▍";
            if (!string.IsNullOrEmpty(_draft)) shown += "You: " + _draft + " …";
            body.text = shown;
        }

        private void OnDestroy()
        {
            _ws?.Dispose();
        }
    }
}
