using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace OpusSystems.Workshop
{
    /// <summary>
    /// The Music app's now-playing, on the speaker panel: artwork, title,
    /// artist, album, year · genre, a progress bar with elapsed / total /
    /// remaining, and previous / play-pause / next buttons you point at and
    /// pinch. Everything comes straight from the Mac's Jarvis app
    /// (`http://host:48101/now`, `/artwork`, `/control`), polled once a
    /// second and interpolated between polls, with no turn through the fleet.
    /// Built onto a SessionPanel's canvas at runtime.
    /// </summary>
    public sealed class MediaPanel : MonoBehaviour
    {
        public string host = "";
        public int port = 48101;

        private SessionPanel _panel;
        private RawImage _art;
        private Text _title, _artist, _album, _meta, _time, _playLabel;
        private Image _progress;
        private Button _prev, _play, _next;

        private string _state = "";
        private float _position, _duration;
        private float _lastPoll;
        private string _artworkId = "";
        private Texture2D _artTexture;
        private bool _busy;

        private string Base => $"http://{host}:{port}";

        public static MediaPanel Attach(SessionPanel panel, string host)
        {
            var m = panel.gameObject.AddComponent<MediaPanel>();
            m.host = host;
            m._panel = panel;
            m.Build();
            return m;
        }

        private void Build()
        {
            var canvas = _panel.GetComponentInChildren<Canvas>().transform;
            if (_panel.body) _panel.body.gameObject.SetActive(false);
            _panel.Set("music", "…", "");
            var font = _panel.title ? _panel.title.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // Artwork, left.
            var artGo = new GameObject("Artwork", typeof(RawImage));
            artGo.transform.SetParent(canvas, false);
            Place(artGo.GetComponent<RectTransform>(), 20, -66, 200, 200);
            _art = artGo.GetComponent<RawImage>();
            _art.color = new Color(0.2f, 0.28f, 0.36f);

            // Text column, right of the artwork.
            _title = Label(canvas, font, 28, FontStyle.Bold, 240, -66, 340, 40, new Color(0.95f, 0.97f, 1f));
            _artist = Label(canvas, font, 22, FontStyle.Normal, 240, -108, 340, 32, new Color(0.7f, 0.9f, 1f));
            _album = Label(canvas, font, 20, FontStyle.Italic, 240, -142, 340, 30, new Color(0.75f, 0.8f, 0.9f));
            _meta = Label(canvas, font, 18, FontStyle.Normal, 240, -174, 340, 28, new Color(0.6f, 0.65f, 0.75f));

            // Progress bar with elapsed / total / remaining.
            var track = new GameObject("Track", typeof(Image));
            track.transform.SetParent(canvas, false);
            Place(track.GetComponent<RectTransform>(), 20, -286, 560, 10);
            track.GetComponent<Image>().color = new Color(1, 1, 1, 0.15f);
            var fill = new GameObject("Fill", typeof(Image));
            fill.transform.SetParent(track.transform, false);
            var frt = fill.GetComponent<RectTransform>();
            frt.anchorMin = Vector2.zero; frt.anchorMax = new Vector2(0, 1); frt.pivot = new Vector2(0, 0.5f);
            frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero; frt.sizeDelta = new Vector2(0, 0);
            _progress = fill.GetComponent<Image>();
            _progress.color = new Color(0.55f, 0.85f, 1f);
            _time = Label(canvas, font, 18, FontStyle.Normal, 20, -300, 560, 26, new Color(0.7f, 0.75f, 0.85f));

            // Transport.
            _prev = MakeButton(canvas, font, "|<", 170, -330, 80, 54, () => Control("previous"));
            _play = MakeButton(canvas, font, ">", 262, -330, 96, 54, () => Control("toggle"));
            _playLabel = _play.GetComponentInChildren<Text>();
            _next = MakeButton(canvas, font, ">|", 370, -330, 80, 54, () => Control("next"));

            StartCoroutine(Poll());
        }

        private void Update()
        {
            if (_state == "playing" && _duration > 0)
                _position = Mathf.Min(_duration, _position + Time.unscaledDeltaTime);
            RenderTime();
        }

        private void SetPlayerVisible(bool on)
        {
            if (_prev) _prev.gameObject.SetActive(on);
            if (_play) _play.gameObject.SetActive(on);
            if (_next) _next.gameObject.SetActive(on);
            if (_progress) _progress.transform.parent.gameObject.SetActive(on);
            if (_time) _time.gameObject.SetActive(on);
        }

        private void RenderTime()
        {
            if (!_time) return;
            if (_duration <= 0) { _time.text = ""; SetFill(0); return; }
            var left = Mathf.Max(0, _duration - _position);
            _time.text = $"{Clock(_position)} / {Clock(_duration)}     −{Clock(left)}";
            SetFill(_position / _duration);
        }

        private void SetFill(float f)
        {
            var rt = _progress.rectTransform;
            rt.anchorMax = new Vector2(Mathf.Clamp01(f), 1);
        }

        private static string Clock(float s) => $"{(int)(s / 60)}:{(int)(s % 60):00}";

        private IEnumerator Poll()
        {
            while (true)
            {
                using (var req = UnityWebRequest.Get(Base + "/now"))
                {
                    req.timeout = 3;
                    yield return req.SendWebRequest();
                    if (req.result == UnityWebRequest.Result.Success) { Apply(req.downloadHandler.text); SetPlayerVisible(true); }
                    else
                    {
                        _duration = 0;
                        Show(null, "Mac not reachable", "open the Jarvis app on the Mac", "", "");
                        _panel.Set("music", "offline", "");
                        SetPlayerVisible(false);
                    }
                }
                yield return new WaitForSecondsRealtime(_state == "" ? 3f : 1f);
            }
        }

        private void Apply(string json)
        {
            var d = Newtonsoft.Json.Linq.JObject.Parse(json);
            _state = d.Value<string>("state") ?? "";
            if (_state == "stopped" || _state == "unavailable")
            {
                _duration = 0;
                Show(null, _state == "stopped" ? "nothing playing" : "Music unavailable", "", "", "");
                _panel.Set("music", _state, "");
                return;
            }
            _position = d.Value<float>("position");
            _duration = d.Value<float>("duration");
            var year = d.Value<int>("year");
            var genre = d.Value<string>("genre") ?? "";
            var track = d.Value<int>("track");
            var of = d.Value<int>("of");
            var meta = string.Join(" · ", new[]
            {
                year > 0 ? year.ToString() : "",
                genre,
                track > 0 ? (of > 0 ? $"track {track}/{of}" : $"track {track}") : "",
            }.Where(s => s.Length > 0));
            Show(d.Value<string>("artwork_id"), d.Value<string>("title"), d.Value<string>("artist"), d.Value<string>("album"), meta);
            _panel.Set("music", _state + (d.Value<bool>("shuffle") ? " · shuffle" : ""), "");
            if (_playLabel) _playLabel.text = _state == "playing" ? "||" : ">";
        }

        private void Show(string artworkId, string title, string artist, string album, string meta)
        {
            _title.text = title ?? "";
            _artist.text = artist ?? "";
            _album.text = album ?? "";
            _meta.text = meta ?? "";
            if (artworkId != _artworkId)
            {
                _artworkId = artworkId ?? "";
                if (string.IsNullOrEmpty(_artworkId)) { _art.texture = null; _art.color = new Color(0.2f, 0.28f, 0.36f); }
                else StartCoroutine(FetchArt(_artworkId));
            }
        }

        private IEnumerator FetchArt(string id)
        {
            using var req = UnityWebRequestTexture.GetTexture(Base + "/artwork?id=" + id);
            req.timeout = 10;
            yield return req.SendWebRequest();
            if (id != _artworkId) yield break; // the track moved on
            if (req.result != UnityWebRequest.Result.Success) { _art.texture = null; yield break; }
            if (_artTexture) Destroy(_artTexture);
            _artTexture = DownloadHandlerTexture.GetContent(req);
            _art.texture = _artTexture;
            _art.color = Color.white;
        }

        private void Control(string action)
        {
            if (_busy) return;
            StartCoroutine(Post(action));
        }

        private IEnumerator Post(string action)
        {
            _busy = true;
            using (var req = UnityWebRequest.PostWwwForm(Base + "/control?action=" + action, ""))
            {
                req.timeout = 5;
                yield return req.SendWebRequest();
            }
            _busy = false;
            _lastPoll = 0;
            // Reflect the change right away rather than waiting for the next poll.
            using (var req = UnityWebRequest.Get(Base + "/now"))
            {
                req.timeout = 3;
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success) Apply(req.downloadHandler.text);
            }
        }

        // ---- UI helpers (canvas units: 1 = 1 mm at scale 1) --------------------

        private static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
        }

        private static Text Label(Transform parent, Font font, int size, FontStyle style, float x, float y, float w, float h, Color color)
        {
            var go = new GameObject("Label", typeof(Text));
            go.transform.SetParent(parent, false);
            Place(go.GetComponent<RectTransform>(), x, y, w, h);
            var t = go.GetComponent<Text>();
            t.font = font; t.fontSize = size; t.fontStyle = style; t.color = color;
            t.alignment = TextAnchor.UpperLeft;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
        }

        private static Button MakeButton(Transform parent, Font font, string label, float x, float y, float w, float h, Action onClick)
        {
            var go = new GameObject("Button " + label, typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            Place(go.GetComponent<RectTransform>(), x, y, w, h);
            var img = go.GetComponent<Image>();
            img.color = new Color(0.16f, 0.32f, 0.45f, 0.95f);
            var b = go.GetComponent<Button>();
            var colors = b.colors;
            colors.highlightedColor = new Color(0.3f, 0.55f, 0.75f);
            colors.pressedColor = new Color(0.55f, 0.85f, 1f);
            b.colors = colors;
            b.onClick.AddListener(() => onClick());
            var tgo = new GameObject("Text", typeof(Text));
            tgo.transform.SetParent(go.transform, false);
            var trt = tgo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
            var t = tgo.GetComponent<Text>();
            t.font = font; t.fontSize = 30; t.fontStyle = FontStyle.Bold; t.alignment = TextAnchor.MiddleCenter;
            t.color = Color.white; t.text = label;
            return b;
        }
    }
}
