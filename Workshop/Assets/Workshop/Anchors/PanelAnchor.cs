using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace OpusSystems.Workshop
{
    /// <summary>
    /// A panel that stays where you put it. While it rests, an
    /// OVRSpatialAnchor pins it to the real room; when you grab it, the
    /// anchor is dropped so the hand can move it, and on release a new one is
    /// created and saved on the headset. The anchor's UUID is remembered per
    /// key ("jarvis", "fleet") in PlayerPrefs, and <see cref="RestoreAsync"/>
    /// brings the panel back to that spot on the next launch. If nothing was
    /// saved, or the room isn't recognised, the caller's default layout
    /// stands.
    /// </summary>
    public sealed class PanelAnchor : MonoBehaviour
    {
        public string key = "";

        private OVRSpatialAnchor _anchor;
        private PanelMover _mover;
        private bool _saving;

        private static string Pref(string key) => "opus.anchor." + key;

        private void Start()
        {
            _mover = GetComponent<PanelMover>();
            if (_mover != null)
            {
                _mover.Grabbed += Release;
                _mover.Released += OnReleased;
            }
        }

        private void OnDestroy()
        {
            if (_mover != null)
            {
                _mover.Grabbed -= Release;
                _mover.Released -= OnReleased;
            }
        }

        private void OnReleased() => _ = PinAfterSettleAsync();

        /// <summary>The mover eases onto its final pose; pin once it has stopped.</summary>
        private async Task PinAfterSettleAsync()
        {
            await Task.Delay(350);
            if (!this || (_mover != null && _mover.IsGrabbed)) return;
            await PinAsync();
        }

        /// <summary>Drop the saved spot (a room reset).</summary>
        public static void Forget(string key)
        {
            PlayerPrefs.DeleteKey(Pref(key));
        }

        /// <summary>Let go of the room so the hand can move the panel.</summary>
        private void Release()
        {
            if (_anchor) Destroy(_anchor);
            _anchor = null;
        }

        /// <summary>Pin the panel where it is now, and remember it.</summary>
        public async Task PinAsync()
        {
            if (_saving || !this) return;
            _saving = true;
            try
            {
                Release();
                await Task.Yield(); // let Destroy finish before adding another
                if (!this) return;
                // The runtime refuses anchors until tracking has settled after
                // a resume, so creation is retried for a few seconds.
                OVRSpatialAnchor anchor = null;
                for (var attempt = 0; attempt < 8 && this; attempt++)
                {
                    anchor = gameObject.AddComponent<OVRSpatialAnchor>();
                    if (await anchor.WhenCreatedAsync()) break;
                    if (anchor) Destroy(anchor);
                    anchor = null;
                    await Task.Delay(1000);
                }
                if (anchor == null || !this)
                {
                    Debug.LogWarning($"anchor {key}: create failed after retries");
                    return;
                }
                var saved = await anchor.SaveAnchorAsync();
                if (!saved.Success)
                {
                    Debug.LogWarning($"anchor {key}: save failed: {saved.Status}");
                    Destroy(anchor);
                    return;
                }
                _anchor = anchor;
                Debug.Log($"anchor {key}: pinned {anchor.Uuid}");
                var old = PlayerPrefs.GetString(Pref(key), "");
                PlayerPrefs.SetString(Pref(key), anchor.Uuid.ToString());
                PlayerPrefs.Save();
                if (Guid.TryParse(old, out var stale) && stale != anchor.Uuid)
                    _ = OVRAnchor.EraseAsync(null, new[] { stale }); // tidy; failure is harmless
            }
            catch (Exception e) { Debug.LogWarning($"anchor {key}: {e.Message}"); }
            finally { _saving = false; }
        }

        /// <summary>
        /// Move the panel to its remembered spot. False when nothing was
        /// saved or the anchor could not be found in this room.
        /// </summary>
        public async Task<bool> RestoreAsync()
        {
            if (!Guid.TryParse(PlayerPrefs.GetString(Pref(key), ""), out var uuid)) return false;
            try
            {
                // Like creation, discovery comes back empty until the anchor
                // service has settled after a resume: ask a few times.
                var unbound = new List<OVRSpatialAnchor.UnboundAnchor>();
                for (var attempt = 0; attempt < 8 && this; attempt++)
                {
                    unbound.Clear();
                    var result = await OVRSpatialAnchor.LoadUnboundAnchorsAsync(new[] { uuid }, unbound);
                    if (result.Success && unbound.Count > 0) break;
                    Debug.Log($"anchor {key}: not found yet ({result.Status}), attempt {attempt + 1}");
                    await Task.Delay(1000);
                }
                if (unbound.Count == 0) return false;
                var found = unbound[0];
                if (!await found.LocalizeAsync())
                {
                    Debug.Log($"anchor {key}: could not localize");
                    return false;
                }
                if (!this) return false;
                Release();
                await Task.Yield();
                if (!this) return false;
                var anchor = gameObject.AddComponent<OVRSpatialAnchor>();
                found.BindTo(anchor);
                _anchor = anchor;
                Debug.Log($"anchor {key}: restored {uuid}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"anchor {key}: {e.Message}");
                return false;
            }
        }
    }
}
