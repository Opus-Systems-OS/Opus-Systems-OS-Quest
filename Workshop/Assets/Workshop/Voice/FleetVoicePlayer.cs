using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace OpusSystems.Workshop
{
    /// <summary>
    /// Jarvis's voice on the headset: each sentence is fetched from
    /// `POST /v1/voice/speak` as WAV (Unity decodes WAV natively; MP3 would
    /// need a decoder) and played in order on this AudioSource. The next
    /// sentence is requested while the current one plays. Sentences are cut
    /// from the streamed deltas the same way the Mac app does it.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class FleetVoicePlayer : MonoBehaviour
    {
        public string baseUrl;
        public string apiKey;
        /// <summary>0…1 while speaking, for the orb.</summary>
        public float Level { get; private set; }
        public bool IsSpeaking => _playing || _queue.Count > 0;

        private AudioSource _source;
        private readonly Queue<string> _queue = new Queue<string>();
        private readonly StringBuilder _pending = new StringBuilder();
        private bool _playing;
        private Coroutine _pump;
        private readonly float[] _samples = new float[256];

        private void Awake()
        {
            _source = GetComponent<AudioSource>();
            _source.spatialBlend = 1f;      // it comes from the panel/orb, not your head
            _source.playOnAwake = false;
        }

        /// <summary>Streamed text in; complete sentences are spoken as they close.</summary>
        public void Feed(string fragment)
        {
            _pending.Append(fragment);
            var text = _pending.ToString();
            var cut = LastSentenceBoundary(text);
            if (cut < 0) return;
            var sentence = text.Substring(0, cut).Trim();
            _pending.Clear();
            _pending.Append(text.Substring(cut));
            Say(sentence);
        }

        /// <summary>Whatever is left once the reply ended.</summary>
        public void Flush()
        {
            var rest = _pending.ToString().Trim();
            _pending.Clear();
            Say(rest);
        }

        public void Say(string sentence)
        {
            if (string.IsNullOrWhiteSpace(sentence)) return;
            _queue.Enqueue(Speakable(sentence));
            if (_pump == null) _pump = StartCoroutine(Pump());
        }

        public void Stop()
        {
            _queue.Clear();
            _pending.Clear();
            if (_pump != null) { StopCoroutine(_pump); _pump = null; }
            _source.Stop();
            _playing = false;
            Level = 0;
        }

        private IEnumerator Pump()
        {
            while (_queue.Count > 0)
            {
                var sentence = _queue.Dequeue();
                AudioClip clip = null;
                yield return Fetch(sentence, c => clip = c);
                if (clip == null) continue; // logged; skip the sentence rather than stall
                _playing = true;
                _source.clip = clip;
                _source.Play();
                while (_source.isPlaying)
                {
                    _source.GetOutputData(_samples, 0);
                    float sum = 0;
                    foreach (var s in _samples) sum += s * s;
                    Level = Mathf.Clamp01(Mathf.Sqrt(sum / _samples.Length) * 4f);
                    yield return null;
                }
                _playing = false;
                Level = 0;
            }
            _pump = null;
        }

        private IEnumerator Fetch(string text, Action<AudioClip> done)
        {
            var body = Encoding.UTF8.GetBytes("{\"text\":" + Quote(text) + ",\"format\":\"wav\",\"latency\":\"low\"}");
            using var req = UnityWebRequestMultimedia.GetAudioClip(baseUrl.TrimEnd('/') + "/voice/speak", AudioType.WAV);
            req.method = "POST";
            req.uploadHandler = new UploadHandlerRaw(body) { contentType = "application/json" };
            req.SetRequestHeader("Authorization", "Bearer " + apiKey);
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = 30;
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"voice: {req.responseCode} {req.error} {req.downloadHandler?.text}");
                done(null);
                yield break;
            }
            done(DownloadHandlerAudioClip.GetContent(req));
        }

        private static string Quote(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': break;
                    case '\t': sb.Append(' '); break;
                    default: if (c < 0x20) sb.Append(' '); else sb.Append(c); break;
                }
            }
            return sb.Append('"').ToString();
        }

        private static string Speakable(string text)
        {
            text = System.Text.RegularExpressions.Regex.Replace(text, "```[\\s\\S]*?```", " ");
            text = System.Text.RegularExpressions.Regex.Replace(text, "[*_#`>]", "");
            return text.Trim();
        }

        private static int LastSentenceBoundary(string text)
        {
            var boundary = -1;
            for (var i = 0; i + 1 < text.Length; i++)
                if (".!?:\n".IndexOf(text[i]) >= 0 && char.IsWhiteSpace(text[i + 1])) boundary = i + 1;
            return boundary;
        }
    }
}
