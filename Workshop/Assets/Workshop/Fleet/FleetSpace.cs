using System.Threading.Tasks;
using OpusSystems.Api;
using UnityEngine;

namespace OpusSystems.Workshop
{
    /// <summary>
    /// The room's brain, stage 2: finds the panel and starts a jarvis
    /// conversation on it. Without a keyboard or voice yet, turns are driven
    /// from the Mac during development through Android intent extras:
    ///   adb shell am start -n dev.opustower.workshop/com.unity3d.player.UnityPlayerGameActivity \
    ///     -e opus.key osk_…            (once; stored in PlayerPrefs)
    ///     -e opus.say "what is 17 times 23"
    /// Stage 3 lists every session; stage 4 replaces this with the orb + voice.
    /// </summary>
    public sealed class FleetSpace : MonoBehaviour
    {
        public SessionPanel panel;
        [TextArea] public string greeting = "I'm in the workshop with the headset on. Say hello in one short sentence.";

        private OpusClient _api;
        private string _lastSay = "";

        private void Start()
        {
            MainThread.Ensure();
            if (!panel) panel = FindFirstObjectByType<SessionPanel>();
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
                "You are speaking through a Meta Quest 3 headset app called the Workshop, where your replies appear on a floating panel in the user's real room. Plain sentences, no markdown, one to three sentences.");
            _lastSay = say ?? "";
            try { if (System.IO.File.Exists(SayFile)) _lastSay = System.IO.File.ReadAllText(SayFile).Trim(); } catch { }
        }

        /// <summary>
        /// Development input until voice (stage 4): the Mac writes a line to
        /// /data/local/tmp/opus-say.txt (`adb push` / `adb shell "echo … >"`),
        /// which apps can read; each new content is sent as a message. A
        /// re-sent `am start` intent would not do — Unity's activity keeps
        /// its original intent.
        /// </summary>
        private const string SayFile = "/data/local/tmp/opus-say.txt";
        private float _nextPoll;

        private void Update()
        {
            if (Time.unscaledTime < _nextPoll || panel == null || _api == null || panel.Busy) return;
            _nextPoll = Time.unscaledTime + 0.5f;
            try
            {
                if (!System.IO.File.Exists(SayFile)) return;
                var text = System.IO.File.ReadAllText(SayFile).Trim();
                if (string.IsNullOrEmpty(text) || text == _lastSay) return;
                _lastSay = text;
                _ = panel.SendAsync(text);
            }
            catch { /* not readable on this device; voice replaces this anyway */ }
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
