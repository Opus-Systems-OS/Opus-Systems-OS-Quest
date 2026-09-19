# Status — resume here

Repo created 2026-09-19. Unity 6000.6.2f1 + Android Build Support (installed
via `unity install-modules`), Meta XR Core/Interaction.OVR/MRUK 205.0.0,
OpenXR 1.18.0, xr.management 4.6.1, `com.opussystems.api` by git URL.
Gotchas found on the way: `com.unity.modules.vr` no longer exists in 6000.6
(xr.management ≥ 4.6 stops requiring it); OpenXR ≤ 1.17 fails to compile
on 6000.6 (obsolete API); Meta's XR Simulator package (81.0.1) is
incompatible with Core 205 and is not used; Core 205's editor code needs
the Android module present to compile at all.

## Stage 1 — in progress

`WorkshopSceneBuilder.Build` applies the Project Setup fixes for Android
and installs the Camera Rig, Passthrough, Hand Tracking, Real Hands blocks
and a Grabbable block on our `SessionPanel` (a 60×40 cm world-space card).
