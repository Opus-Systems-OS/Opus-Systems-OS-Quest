using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OpusSystems.Api;
using UnityEngine;
using UnityEngine.UI;

namespace OpusSystems.Workshop
{
    /// <summary>
    /// One layer of the stack, in detail: `GET /v1/ops/{service}` rendered as
    /// lines the way you'd want to read them standing in the room — GitHub's
    /// repos with their open PRs and CI, UptimeRobot's monitors, the droplet's
    /// load, Docker's containers, Tailscale's devices, Cloudflare's records.
    /// Refreshes every 30 s; closes with its own × button.
    /// </summary>
    public sealed class OpsPanel : MonoBehaviour
    {
        public string service = "";
        public Action<string> OnClose;

        private SessionPanel _panel;
        private OpusClient _api;
        private float _nextRefresh;

        public static OpsPanel Attach(SessionPanel panel, OpusClient api, string service)
        {
            var p = panel.gameObject.AddComponent<OpsPanel>();
            p._panel = panel;
            p._api = api;
            p.service = service;
            p.Build();
            return p;
        }

        private void Build()
        {
            _panel.Set(service, "loading…", "");
            if (_panel.body) _panel.body.fontSize = 19;
            _panel.closeMode = SessionPanel.CloseMode.Destroy;
            _panel.OnClosed = _ => OnClose?.Invoke(service);
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 30f;
            _ = RefreshAsync();
        }

        public async Task RefreshAsync()
        {
            try
            {
                var s = await _api.OpsAsync(service);
                MainThread.Run(() => Apply(s));
            }
            catch (Exception e)
            {
                MainThread.Run(() => _panel.Set(service, "error", e.Message));
            }
        }

        private void Apply(JObject s)
        {
            var state = s.Value<string>("state") ?? "unknown";
            var detail = s["detail"] as JObject ?? new JObject();
            _panel.Set(s.Value<string>("name") ?? service, $"{state} · {s.Value<string>("headline")}", Render(detail));
            if (_panel.status) _panel.status.color = Ui.StateColor(state);
        }

        private string Render(JObject d)
        {
            if (d["error"] != null) return d.Value<string>("error");
            var sb = new StringBuilder();
            switch (service)
            {
                case "github":
                    foreach (var r in d["repos"] as JArray ?? new JArray())
                    {
                        var run = r["last_run"] as JObject;
                        var ci = run == null ? "" : run.Value<string>("conclusion") ?? run.Value<string>("status") ?? "";
                        var prs = r["open_prs"] as JArray;
                        sb.AppendLine($"{r.Value<string>("name")}  ·  {(prs == null ? "" : prs.Count + " PR" + (prs.Count == 1 ? "" : "s"))}  ·  CI {ci}  ·  {Ago(r.Value<string>("pushed_at"))}");
                        if (prs != null)
                            foreach (var p in prs.Take(3))
                                sb.AppendLine($"    #{p.Value<int>("number")} {Trim(p.Value<string>("title"), 44)}  ({p.Value<string>("author")})");
                    }
                    var notes = d["notifications"] as JArray ?? new JArray();
                    if (notes.Count > 0)
                    {
                        sb.AppendLine($"— {notes.Count} notifications —");
                        foreach (var n in notes.Take(4))
                            sb.AppendLine($"{Trim(n.Value<string>("title"), 40)}  ·  {n.Value<string>("reason")}");
                    }
                    break;
                case "uptimerobot":
                    foreach (var m in d["monitors"] as JArray ?? new JArray())
                        sb.AppendLine($"{Pad(m.Value<string>("status"), 10)} {Trim(m.Value<string>("name"), 26)}  {m.Value<string>("uptime_24h")}% 24h · {m.Value<string>("uptime_30d")}% 30d · {m["response_ms"]} ms");
                    break;
                case "droplet":
                    foreach (var x in d["droplets"] as JArray ?? new JArray())
                    {
                        sb.AppendLine($"{x.Value<string>("name")}  ·  {x.Value<string>("region")}  ·  {x.Value<string>("status")}");
                        sb.AppendLine($"    {string.Join(", ", (x["ipv4"] as JArray ?? new JArray()).Select(v => v.ToString()))}");
                        sb.AppendLine($"    {x.Value<int>("vcpus")} vCPU · {x.Value<int>("memory_mb")} MB · {x.Value<int>("disk_gb")} GB disk");
                        if (x["load_1"]?.Type == JTokenType.Float || x["load_1"]?.Type == JTokenType.Integer)
                            sb.AppendLine($"    load {x.Value<double>("load_1"):0.00}   mem {x["memory_used_pct"]}%");
                        sb.AppendLine($"    since {x.Value<string>("created_at")?.Substring(0, 10)}");
                    }
                    break;
                case "docker":
                    foreach (var c in d["containers"] as JArray ?? new JArray())
                        sb.AppendLine($"{Pad(c.Value<string>("state"), 8)} {Trim(c.Value<string>("name"), 24)}  {c.Value<string>("status")}");
                    break;
                case "tailscale":
                    foreach (var v in d["devices"] as JArray ?? new JArray())
                        sb.AppendLine($"{(v.Value<bool>("online") ? "online " : "offline")} {Pad(v.Value<string>("name"), 12)} {Pad(v.Value<string>("os"), 8)} {(v["addresses"] as JArray)?.FirstOrDefault()}  {(v.Value<bool>("online") ? "" : Ago(v.Value<string>("last_seen")))}");
                    break;
                case "cloudflare":
                    foreach (var z in d["zones"] as JArray ?? new JArray())
                    {
                        sb.AppendLine($"{z.Value<string>("name")}  ·  {z.Value<string>("status")}");
                        foreach (var r in z["records"] as JArray ?? new JArray())
                            sb.AppendLine($"    {Pad(r.Value<string>("type"), 5)} {Pad(r.Value<string>("name"), 26)} {Trim(r.Value<string>("content"), 22)} {(r.Value<bool>("proxied") ? "☁" : "")}");
                    }
                    break;
                default:
                    sb.Append(d.ToString(Newtonsoft.Json.Formatting.Indented));
                    break;
            }
            return sb.ToString();
        }

        private static string Pad(string s, int n) => (s ?? "").PadRight(n);
        private static string Trim(string s, int n) => string.IsNullOrEmpty(s) ? "" : (s.Length > n ? s.Substring(0, n - 1) + "…" : s);

        private static string Ago(string rfc3339)
        {
            if (!DateTimeOffset.TryParse(rfc3339, out var t)) return "";
            var d = DateTimeOffset.UtcNow - t;
            if (d.TotalMinutes < 1) return "just now";
            if (d.TotalHours < 1) return $"{(int)d.TotalMinutes}m ago";
            if (d.TotalDays < 1) return $"{(int)d.TotalHours}h ago";
            return $"{(int)d.TotalDays}d ago";
        }
    }
}
