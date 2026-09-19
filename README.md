# Opus Systems OS — Quest

Jarvis and the fleet as translucent panels floating in your real room, on a
Meta Quest 3 with passthrough. A Unity 6 app on the
[Opus Systems OS API](https://github.com/Opus-Systems-OS/Opus-Systems-OS-API);
the C# SDK arrives as the `com.opussystems.api` package.

See `CLAUDE.md` for the rules and build order, `docs/status.md` for where
things stand. Build and install:

```sh
U=/Applications/Unity/Hub/Editor/6000.6.2f1/Unity.app/Contents/MacOS/Unity
ADB=/Applications/Unity/Hub/Editor/6000.6.2f1/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb
cd Workshop
"$U" -batchmode -nographics -projectPath "$PWD" -buildTarget Android -executeMethod OpusSystems.Workshop.Editor.WorkshopSceneBuilder.Build -logFile /tmp/ws.log
"$U" -batchmode -nographics -projectPath "$PWD" -buildTarget Android -executeMethod OpusSystems.Workshop.Editor.WorkshopBuild.Apk -logFile /tmp/apk.log -quit
"$ADB" install -r Builds/Workshop.apk
```
