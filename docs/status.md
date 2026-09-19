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

## Stage 2 — next

Bind `SessionPanel` to a jarvis session via `com.opussystems.api`
(WebSocket, deltas), keyboard input from the editor / a virtual keyboard;
reply streams onto the panel; status and cost update; interrupt works.
