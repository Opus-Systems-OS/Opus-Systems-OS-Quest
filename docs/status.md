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

## Stage 5 — done 2026-09-19 (on device)

Panels stay where you put them. `PanelAnchor` on the jarvis panel and the
fleet header: at rest each is pinned to the room with an `OVRSpatialAnchor`;
a grab drops the anchor so the hand can move it, and the release creates,
saves and remembers a new one (UUID per key in PlayerPrefs, the previous
anchor erased). On launch the saved anchor is loaded and localised; if the
room is not recognised, or nothing was saved, the default layout applies
and is pinned. Session panels (transient) are not anchored.

Verified: both anchors `restored` on relaunch after a move.

Noted: right after a resume the runtime refuses anchor creation and
discovery comes back empty with `Success`; both are retried for up to 8 s.
`OVRSpatialAnchor`'s own logging is compiled out of release builds, so
`PanelAnchor` logs `pinned` / `restored` / `not found yet` itself. The
Grabbable Item building block leaves a demo cube at the origin; `Finish()`
removes it.

## Stage 6 — done 2026-09-19 (on device)

3D-print preview, and one continuing conversation.

- **Tools on the jarvis session** (session-local, declared at create,
  answered over the WebSocket): `list_models` — the `.stl/.glb/.gltf` files
  in the app's external files dir (`adb push x.stl
  /sdcard/Android/data/dev.opustower.workshop/files/models/`); `show_model
  {name | url}` — loose name match or an http(s) URL. "What models do I
  have?" → "bring up the duck" works by voice.
- **`PrintPreview`**: a half-size SessionPanel as the stand (grab it to move
  or turn the print), the model on a world-scale-1 mount at its top edge,
  fitted to 22 cm, turning slowly; the stand shows w×d×h in mm and the
  triangle count, which the tool result also carries back to Jarvis.
  `StlLoader` parses binary and ASCII STL (mm → m, Z-up → Y-up, 32-bit
  indices); GLB/glTF through glTFast 6.20, meshes only, every material
  replaced by `Resources/PrintMaterial` (a print is one colour, and
  glTFast's shaders are not in the build).
- **Session reuse**: the jarvis session id and a tool-set version are kept
  in PlayerPrefs; on launch the session is picked up if it is idle, under
  30 ¢ and was created with the current tools ("I'm back in the workshop"
  as the opener), else a new one starts. Verified: the duck conversation
  survived a relaunch (`sesn_014bsD…`, 8 ¢ → 18 ¢, no new session).

Verified: "i saw both, they are blue (perfect)" — torus.stl (generated,
54 mm) and Khronos Duck.glb.

Noted: glTFast instantiates a file's camera nodes as real Cameras — a
second Camera in a VR scene draws the room twice ("two ducks"); instantiate
with `ComponentType.Mesh` only. Concurrent loads are serialised (latest
wins). Tool results need `Events.Id(ev)` (the custom_tool_use event id).

## Workshop upgrade (2026-09-20)

Plan: Apple Music, web search, a calculator, and a launcher for every layer
of the stack (Iron-Fleet `docs/centralization-plan.md` "Resume here" has
the cross-repo view).

- **Web search** — already on the `jarvis` agent (`agent_toolset_20260401`
  includes it; proved with `usage.web_search_requests: 1`); the system
  suffix now tells Jarvis to use it for current facts.
- **Apple Music from the headset — done.** The session declares the four
  music tools (`play_music`, `music_control`, `list_playlists`,
  `queue_music`) and is created with `client: "quest"`; the Mac Jarvis
  app's `WorkshopRelay` finds that session by metadata and answers them
  through the Music app. `SessionPanel.RemoteTools`: those calls are not
  run here — the panel shows "waiting on the Mac…" and answers with an
  error after 25 s if no `user.custom_tool_result` arrives. Verified:
  playlists listed, a playlist played, skip. Audio comes out of the Mac.
- Ops (`/v1/ops`) and the `ops:read` key are live on the API side; the
  hub panel is next.

- **The Mac's music in the room — done.** `MacSpeaker` streams the Jarvis
  app's PCM (`OPUSPCM1 48000 2` then 16-bit LE stereo over TCP 48100) into
  a spatialised speaker panel; the Mac mutes its own output while the
  headset listens. Address handed over once (`-e opus.speaker host:port`).
- **Buttons in the room — done.** `WorkshopSceneBuilder.AddRayCanvas`
  runs the Interaction SDK's Ray Canvas wizard on the panel's Canvas child
  (`ISDK_RayCanvasInteraction`: PointableCanvas + RayInteractable + clipped
  plane) and adds an EventSystem with `PointableCanvasModule`; every
  spawned panel inherits it through the prefab. Unity UI `Button`s work
  with the right hand's ray + pinch; the distance grab did not fight it.
- **Media player — done.** `MediaPanel` on the speaker panel: artwork,
  title / artist / album / year · genre · track, progress bar with
  elapsed / total / remaining (interpolated between 1 s polls), previous /
  play-pause / next. Reads the Mac's `http://host:48101/now`, `/artwork`,
  `/control` directly — no fleet turn. Verified: "perfect!"

Noted: Unity's Android "Allow downloads over HTTP" defaults to *not
allowed*, which silently refuses every UnityWebRequest to a plain-HTTP LAN
address (the raw-TCP audio worked, the JSON and artwork didn't);
`WorkshopBuild` sets `InsecureHttpOption.AlwaysAllowed`.

- **Calculator — done.** `Calc` (shunting-yard over `decimal`: + − × ÷ ^,
  parentheses, unary minus, postfix %, √; spoken forms like "15 percent
  of 80" normalised) and `CalculatorPanel` (display, eight-line tape,
  5×5 keypad of ray-pinch buttons) on a panel to the left, anchored
  (`calc`). Jarvis's `calculate` tool lands on the same tape, and the
  system suffix tells him to use it for any arithmetic. Verified on
  device by keypad and by voice.

- **The Opus launcher — done.** `OpsHubPanel` (up-left of the fleet
  header, anchored `hub`): one row per layer from `GET /v1/ops` — state
  dot, name, headline — refreshed every 30 s; a row is a button that
  opens the layer's `OpsPanel` on the upper ring (`GET /v1/ops/{service}`
  rendered per service: repos + open PRs + CI + notifications; monitors;
  droplet load and memory; containers; tailnet devices; DNS records),
  each with its own ×. Tools `open_panel` / `close_panel` do the same by
  voice. Verified on device: "it all works".

## Window management (2026-09-20) — done

Force-pull is gone. Every panel gets a frame at runtime (`PanelChrome`,
from `SessionPanel.Awake`): a title bar whose background is its own
`RayInteractable` (PlaneSurface clipped to the bar, 5 mm proud of the
canvas so the ray prefers it over content), a ⌖ face-me button and a ×.
`PanelMover` reads the selecting `RayInteractor` (matched by
`PointerEvent.Identifier`) and moves the panel Quest-home style: it rides
the ray; hand travel along the ray changes the distance ×4 (0.3–5 m); the
panel yaws to face the head; smoothed. `PanelAnchor` pins on the mover's
release. × hides a desk panel (Jarvis, fleet, calculator, music, Opus —
the hub's Desk row or `open_panel` brings it back in front of you) and
destroys a transient one (service, print, session). "Reset room" (hub
button / `reset_room`) forgets anchors and re-lays the desk. The prefab
lost `ISDK_DistanceHandGrabInteraction` and its `Grabbable` (`Finish()`
strips them); canvas `dynamicPixelsPerUnit` 4. Verified: "the overhaul
went perfectly".

Noted: the Interaction SDK's pointer `Pose` on a ray is the hit point, not
the ray, hence reading the interactor; the hands carry no direct
`HandGrabInteractor`, so movement is ray-only (works at any distance).

## Next

Standalone: pairing from the Mac (no adb), a signed release build with an
icon, Mac-off behaviour. The workshop is the desk now: Jarvis, music with a real
player, a calculator, the whole stack's status, and prints on a stand.
Ideas: a "start a session" agent menu on the fleet header; the
passthrough camera when Meta exposes it; a fine-grained GitHub token in
place of the classic one; Tailscale on the Mac so the speaker address
survives a DHCP change. Ideas, in rough order of value: more room tools (start a
fleet session by voice with an agent menu; "look at this" with the
passthrough camera when Meta exposes it); the Duck-style dimensions check
against a real print bed; a "print this" hand-off to the slicer on the
Mac; multi-user later.
