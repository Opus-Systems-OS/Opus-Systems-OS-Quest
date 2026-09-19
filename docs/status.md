# Status — resume here

Repo created 2026-09-19. Unity 6000.6.2f1 + Android Build Support (installed
via `unity install-modules`), Meta XR Core/Interaction.OVR/MRUK 205.0.0,
OpenXR 1.18.0, xr.management 4.6.1, `com.opussystems.api` by git URL.
Gotchas found on the way: `com.unity.modules.vr` no longer exists in 6000.6
(xr.management ≥ 4.6 stops requiring it); OpenXR ≤ 1.17 fails to compile
on 6000.6 (obsolete API); Meta's XR Simulator package (81.0.1) is
incompatible with Core 205 and is not used; Core 205's editor code needs
the Android module present to compile at all.

## Stage 1 — done 2026-09-19 ~11:40 PDT

On the Quest 3: passthrough of the real room, a 60×40 cm translucent
`SessionPanel` 1.2 m ahead, grabbed and placed with hands (distance grab
by pinch, plus hand-ray). Built entirely from the CLI:
`WorkshopSceneBuilder.Build` (Project Setup fixes + Camera Rig,
Passthrough, Hand Tracking, Real Hands, Interactions Rig, Hand
Interactions, Hand Ray Interactor, Grabbable Item) then
`WorkshopSceneBuilder.AddDistanceGrab` (a second launch), then
`WorkshopBuild.Apk` → `adb install`.

Gotchas that cost the afternoon:
- Building Blocks' installer is async and completes on the editor's
  main-thread sync context: **never block on it** from `-executeMethod`;
  return and let the editor loop run, exit from the end of the chain
  (`Run`), and launch **without `-quit`**.
- "Interactions Rig" is a singleton dependency of Real Hands; installing
  it again throws `InstallationCancelledException` (internal) — matched by
  type name and skipped.
- The **Distance Grab block never settles in batch mode** (its install
  triggers an import the async chain doesn't survive). It is a thin
  wrapper around the Interaction SDK's `DistanceGrabWizard` quick action,
  which is synchronous — called by reflection instead, in its own launch
  on the saved scene. `[InitializeOnLoadMethod]` + `SessionState` resume
  is in place in case a reload ever interrupts it.
- Two editors on one project: a hung batch editor holds
  `Temp/UnityLockfile`; `pkill` it before relaunching.
- On the device, "can't interact" with a `Grabbable` meant **no
  interactors on the hands** — Grabbable Item alone is not enough.

## Stage 2 — done 2026-09-19 ~12:20 PDT

`SessionPanel` binds to a jarvis session through `com.opussystems.api`:
`FleetSpace` creates the session on start (headset key `quest-3`,
`sessions:*,fleet:read,voice`, delivered once via `am start -e opus.key`
and kept in PlayerPrefs; a session-local `system_suffix` says where the
user is), opens the WebSocket with deltas, streams the reply onto the
panel with status (thinking/speaking/idle) and a cost badge from
`GET /sessions/{id}`. `MainThread` marshals SDK callbacks. Until voice,
turns are driven from the Mac by writing `/data/local/tmp/opus-say.txt`
(apps can read it; a re-sent `am start` intent is *not* seen — Unity's
activity keeps its original intent).

Exit, on the Quest 3: `sesn_01GHjBQ8hAbaM9x5ibSMDmCM` — greeting, then a
pushed follow-up ("seventeen times twenty-three… what should the second
panel show?") streamed in: "Three hundred ninety-one. The second panel
should show your running fleet sessions and their status…"

## Stage 3 — done 2026-09-19 ~12:45 PDT

`FleetSpace` polls `/v1/sessions` and `/v1/rig` every 10 s: a fleet header
panel (rig line + session list with agent/status/cost/title) above the
jarvis panel, and a panel per other recent session spawned from the
`SessionPanel` prefab (saved by `Finish` with its grab components), laid
out on an arc and then left wherever the user puts them; each binds to
its session over its own WebSocket (history, then live). Exit on the Quest
3: a `blueweb-ops` session started from the Mac appeared beside jarvis and
streamed its answer; the header listed it.

## Stage 4 — done 2026-09-19 (on device)

Voice both ways, on the jarvis panel.

- **Out:** `FleetVoicePlayer` splits the streamed reply into sentences and
  fetches each from `POST /v1/voice/speak` (`format: wav`, `latency: low`)
  — the Fish voice the API holds, the same one the Mac and Tauri apps use.
  Clips are queued and played from the panel's position (spatial).
- **In:** Meta Voice SDK 85 dictation (`JarvisTalk`). Push-to-talk: pinch
  and hold with the **left** hand, talk, release; the partial transcript
  shows on the panel as "You: …", the final one is sent as the next user
  message. The right hand stays free for grabbing.
- The Wit.ai client token never enters the repo or an asset: it is handed
  to the app once via an intent extra and kept in PlayerPrefs, and the
  `WitConfiguration` is built at runtime from it.

```sh
adb shell am force-stop dev.opustower.workshop
adb shell am start -n dev.opustower.workshop/com.unity3d.player.UnityPlayerGameActivity \
  -e opus.wit "$(cat ~/.config/opus-systems/wit-token)"
```

The polled say-file (`/data/local/tmp/opus-say.txt`) stays as a dev
fallback for typed input from the Mac.

Verified on device: "I talked, I heard Jarvis."

Ergonomics fixed the same day, from use: the room is laid out once from
the first tracked head pose (jarvis straight ahead, header above, other
panels on a wider, lower ring) instead of on world-forward around wherever
you stood at the first refresh; the left hand's interactors are off so the
talk pinch cannot distance-grab a panel into your face; and a panel is
spawned only for a session that is actually working (running / waiting on a
tool, max 3, never another jarvis) — idle and finished sessions are lines
in the fleet header, not screens.

Noted: `com.meta.xr.sdk.voice` 85 bundles Dictation; the separate
`…voice.dictation` package (64) conflicts with it and is not installed.
Microphone permission is requested at first launch.

## Stage 5 — next

Headset ergonomics: spatial anchors so panels stay where you left them
across launches, distance/size defaults, passthrough tint.
