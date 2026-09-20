# Opus Systems OS — Quest

The Stark desk: Jarvis and the fleet as translucent panels floating in your
real room, on a Meta Quest 3 with passthrough. A Unity 6 app on the Opus
Systems OS API. Everything it does — sessions, streaming, voice, device
tools — is the API's; this app is glass and hands.

```
Quest 3 ──(osk_ key: sessions:*, fleet:read, voice)──> api.opustower.dev/v1
   panels ⇄ /v1/sessions/{id}/ws (deltas)      orb ⇄ jarvis session + /v1/voice/speak
```

## Rules

- **No fleet logic here.** The app never talks to Anthropic, the control
  plane, or the rig directly; only the API, with a per-headset key that
  cannot mint keys or touch budgets. Device tools the headset runs itself
  are declared per session (`CreateSessionRequest.Tools`) and answered with
  `SessionSocket.SendToolResultAsync` — the Mac app's Music pattern.
- **The SDK is a package.** `com.opussystems.api` by git URL from the API
  repo; never copy its sources here.
- **Scenes and settings are built by script.** `WorkshopSceneBuilder.Build`
  assembles the scene from Meta Building Blocks + our prefabs and applies
  the Project Setup Tool's fixes, so nothing depends on an editor session.
  Rebuild it rather than hand-editing the scene.
- **Main thread only.** SDK callbacks arrive on background threads; queue
  them and apply in `Update()` (`MainThread.cs`).
- **Nothing secret in the repo.** The key lives in PlayerPrefs (prototype)
  or the Keystore; the Wit.ai token is handed over once (`-e opus.wit`) and
  kept in PlayerPrefs — never in an asset or the repo.
- Editor 6000.6.2f1 with Android Build Support. Meta XR Core/Interaction/
  MRUK 205 from `npm.developer.oculus.com`. Meta's XR Simulator package
  (81) predates Core 205 and is not used; the dev loop is build → `adb
  install` → headset, plus EditMode tests for what needs no device.

## Layout

```
Workshop/                          the Unity project
  Assets/Workshop/Editor/WorkshopSceneBuilder.cs   scene + project setup from the CLI
  Assets/Workshop/Fleet/           SessionPanel, FleetSpace (stage 3)
  Assets/Workshop/Jarvis/          the orb (stage 2/4)
  Assets/Workshop/Config/          FleetConfig (URL, key)
  Assets/Workshop/Scenes/Workshop.unity            generated
  Assets/Tests/                    EditMode / PlayMode
docs/design.md                     what the panels are and how they bind to sessions
docs/status.md                     resume here
```

## Build order (hard stage boundaries)

Stages 1–6 are done on the device (2026-09-19), and the 2026-09-20 upgrade
(music with a player, web search, calculator, the Opus launcher) too;
progress and the gotchas each one hit are in `docs/status.md`.

1. **Room + one panel** — passthrough, hands, one grabbable panel; APK on
   the headset. Exit: grab and place the panel in your real room.
2. **Live panel** — the panel bound to a jarvis session (WebSocket deltas),
   keyboard input from the editor / a virtual keyboard. Exit: a real turn
   on the panel.
3. **Fleet space** — a panel per running session, agent menu to start one,
   cost/status badges, rig line. Exit: two agents' sessions side by side.
4. **Voice** — Meta Voice SDK dictation → orb; replies in the Fish voice via
   `/v1/voice/speak`. Exit: talk to the orb, hear Jarvis.
5. **Anchors + polish** — panels remember their places; ergonomics.
6. **Print preview** — STL/GLB on a stand via jarvis tools (`list_models`,
   `show_model`); the jarvis session continues across launches.

## Commands

```sh
U=/Applications/Unity/Hub/Editor/6000.6.2f1/Unity.app/Contents/MacOS/Unity
cd Workshop
"$U" -batchmode -nographics -projectPath "$PWD" -buildTarget Android -executeMethod OpusSystems.Workshop.Editor.WorkshopSceneBuilder.Build -logFile /tmp/ws.log
"$U" -batchmode -nographics -projectPath "$PWD" -buildTarget Android -executeMethod OpusSystems.Workshop.Editor.WorkshopBuild.Apk -logFile /tmp/apk.log
adb install -r Builds/Workshop.apk
```
