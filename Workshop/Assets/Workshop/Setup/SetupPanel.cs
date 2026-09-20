using System;
using System.Threading.Tasks;
using OpusSystems.Api;
using UnityEngine;
using UnityEngine.UI;

namespace OpusSystems.Workshop
{
    /// <summary>
    /// First launch, or after "forget this headset": the Jarvis panel shows a
    /// six-digit pairing code. On the Mac, Jarvis → Settings → Approve headset
    /// with that code mints this headset's key and passes along the Wit token
    /// and the Mac's music address; the panel collects them and boots.
    /// Nothing is ever typed on the headset.
    /// </summary>
    public sealed class SetupPanel : MonoBehaviour
    {
        private SessionPanel _panel;
        private Text _code;
        private Text _hint;
        private Action _done;
        private bool _stop;

        public static SetupPanel Begin(SessionPanel panel, Action onPaired)
        {
            var s = panel.gameObject.AddComponent<SetupPanel>();
            s._panel = panel;
            s._done = onPaired;
            s.Build();
            _ = s.RunAsync();
            return s;
        }

        private void Build()
        {
            var canvas = _panel.GetComponentInChildren<Canvas>().transform;
            var font = _panel.title ? _panel.title.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_panel.body) _panel.body.gameObject.SetActive(false);
            _panel.Set("Pair this headset", "", "");
            _code = Ui.Label(canvas, font, 96, FontStyle.Bold, 20, -70, 560, 120, new Color(0.7f, 0.9f, 1f));
            _code.alignment = TextAnchor.MiddleCenter;
            _code.horizontalOverflow = HorizontalWrapMode.Overflow;
            _hint = Ui.Label(canvas, font, 22, FontStyle.Normal, 30, -210, 540, 170, new Color(0.9f, 0.93f, 1f));
            _hint.alignment = TextAnchor.UpperCenter;
            _code.text = "· · · · · ·";
            _hint.text = "Getting a code…";
        }

        private void OnDestroy() => _stop = true;

        private async Task RunAsync()
        {
            OpusClient api;
            try { api = new OpusClient(FleetConfig.BaseUrl, ""); }
            catch (Exception e) { MainThread.Run(() => _hint.text = "Setup failed: " + e.Message); return; }
            while (!_stop)
            {
                string code, token;
                try
                {
                    var started = await api.PairStartAsync();
                    code = started.Value<string>("code");
                    token = started.Value<string>("token");
                }
                catch (Exception e)
                {
                    MainThread.Run(() => { _hint.text = $"Can't reach the API: {e.Message}\nRetrying…"; });
                    await Task.Delay(5000);
                    continue;
                }
                MainThread.Run(() =>
                {
                    _code.text = $"{code.Substring(0, 3)} {code.Substring(3)}";
                    _hint.text = "On the Mac: open Jarvis → Settings (⌘,) → Workshop,\ntype this code and press Approve headset.\n\nThe code is good for ten minutes.";
                    if (_panel.status) _panel.status.text = "waiting…";
                });
                var deadline = DateTime.UtcNow.AddMinutes(9.5);
                while (!_stop && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(2000);
                    Newtonsoft.Json.Linq.JObject bundle;
                    try { bundle = await api.PairPollAsync(code, token); }
                    catch (OpusApiException e) when ((int)e.Status == 404) { break; } // expired: start over
                    catch (Exception) { continue; }
                    if (bundle == null) continue;
                    var key = bundle.Value<string>("api_key") ?? "";
                    var wit = bundle.Value<string>("wit_token");
                    var speaker = bundle.Value<string>("speaker");
                    MainThread.Run(() =>
                    {
                        FleetConfig.ApiKey = key;
                        if (!string.IsNullOrEmpty(wit)) FleetConfig.WitToken = wit;
                        if (!string.IsNullOrEmpty(speaker)) FleetConfig.Speaker = speaker;
                        _stop = true;
                        _code.text = "paired";
                        _hint.text = "";
                        Destroy(_code.gameObject);
                        Destroy(_hint.gameObject);
                        if (_panel.body) _panel.body.gameObject.SetActive(true);
                        _done?.Invoke();
                        Destroy(this);
                    });
                    return;
                }
            }
        }
    }
}
