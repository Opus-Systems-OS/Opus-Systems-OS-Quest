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

## Stage 4 — next

Voice: Meta Voice SDK dictation (Wit.ai app token in a gitignored config)
→ the jarvis panel; replies played from `/v1/voice/speak` (wav) in the
Fish voice. The polled say-file goes away.
