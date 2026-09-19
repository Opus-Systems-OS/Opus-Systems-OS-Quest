using Meta.WitAi;
using Meta.WitAi.Configuration;
using Meta.WitAi.Data.Configuration;
using Oculus.Voice.Dictation;
using Oculus.Voice.Dictation.Configuration;
using UnityEngine;

namespace OpusSystems.Workshop
{
    /// <summary>
    /// Your voice in. Meta Voice SDK dictation (Wit.ai) with a configuration
    /// built at runtime from the headset's Wit token — nothing baked into an
    /// asset. Push-to-talk: pinch and hold with the LEFT hand (index+thumb)
    /// to listen, release to send the transcript to the jarvis panel; the
    /// right hand stays free for grabbing panels. Partial transcripts show
    /// on the panel while you talk.
    /// </summary>
    public sealed class JarvisTalk : MonoBehaviour
    {
        public SessionPanel panel;
        public string witClientToken;

        private AppDictationExperience _dictation;
        private OVRHand _leftHand;
        private bool _listening;
        private string _partial = "";
        private string _lastFull = "";
        private float _pinchDownAt;

        private void Start()
        {
            if (string.IsNullOrEmpty(witClientToken))
            {
                Debug.LogWarning("JarvisTalk: no Wit token; voice input disabled");
                enabled = false;
                return;
            }
            var config = ScriptableObject.CreateInstance<WitConfiguration>();
            config.SetClientAccessToken(witClientToken);
            var runtime = new WitDictationRuntimeConfiguration { witConfiguration = config, dictationConfiguration = new DictationConfiguration() };

            // The experience initialises its service in OnEnable, so the
            // configuration has to be in place before the object goes live.
            var go = new GameObject("Dictation");
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            _dictation = go.AddComponent<AppDictationExperience>();
            _dictation.RuntimeDictationConfiguration = runtime;
            go.SetActive(true);

            // The left hand is the microphone hand: its pinch must not also
            // ray-grab or distance-grab panels, so its interactors go dark.
            var leftInteractors = GameObject.Find("HandInteractorsLeft");
            if (leftInteractors) leftInteractors.SetActive(false);

            _dictation.DictationEvents.OnPartialTranscription.AddListener(t => { _partial = t; ShowPartial(); });
            _dictation.DictationEvents.OnFullTranscription.AddListener(t => { _lastFull = t; _partial = ""; });
            _dictation.DictationEvents.OnError.AddListener((e, m) => { Debug.LogWarning($"dictation: {e} {m}"); panel?.ShowDraft(""); });
            _dictation.DictationEvents.OnStartListening.AddListener(() => Debug.Log("dictation: listening"));
            _dictation.DictationEvents.OnDictationSessionStopped.AddListener(_ => Send());

            foreach (var h in FindObjectsByType<OVRHand>(FindObjectsSortMode.None))
                if (h.GetHand() == OVRPlugin.Hand.HandLeft) _leftHand = h;
            if (_leftHand == null) Debug.LogWarning("JarvisTalk: no left OVRHand in the scene");

#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
#endif
        }

        private void Update()
        {
            if (_dictation == null || _leftHand == null || panel == null) return;
            var pinching = _leftHand.IsTracked && _leftHand.GetFingerIsPinching(OVRHand.HandFinger.Index);
            if (pinching && !_listening && !panel.Busy)
            {
                _listening = true;
                _pinchDownAt = Time.unscaledTime;
                _partial = "";
                _lastFull = "";
                panel.voice?.Stop();
                panel.SetListening(true);
                _dictation.Activate();
            }
            else if (!pinching && _listening && Time.unscaledTime - _pinchDownAt > 0.3f)
            {
                _listening = false;
                panel.SetListening(false);
                _dictation.Deactivate(); // the final transcript arrives, then the session-stopped event sends it
            }
        }

        private void ShowPartial()
        {
            if (!string.IsNullOrEmpty(_partial)) panel.ShowDraft(_partial);
        }

        private void Send()
        {
            var text = string.IsNullOrWhiteSpace(_lastFull) ? _partial : _lastFull;
            _partial = "";
            _lastFull = "";
            panel.ShowDraft("");
            if (string.IsNullOrWhiteSpace(text)) return;
            _ = panel.SendAsync(text.Trim());
        }
    }
}
