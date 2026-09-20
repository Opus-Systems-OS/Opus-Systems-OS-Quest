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

## Release builds

`WorkshopBuild.Apk` signs with a release keystore when the environment
names one, so updates install over each other and the app carries its
icon and a version (the commit count):

```sh
set -a; . ~/.config/opus-systems/workshop-keystore.env; set +a   # OPUS_KEYSTORE, _PASS, _ALIAS, OPUS_KEY_PASS
"$U" -batchmode -nographics -projectPath "$PWD" -buildTarget Android -executeMethod OpusSystems.Workshop.Editor.WorkshopBuild.Apk -logFile /tmp/apk.log -quit
```

The keystore lives in `~/.config/opus-systems/` and is never committed.
To make one: `keytool -genkeypair -keystore ~/.config/opus-systems/workshop.keystore
-alias workshop -keyalg RSA -keysize 2048 -validity 10000` and write the four
variables to `workshop-keystore.env` (`chmod 600` both). A lost keystore
means the next install needs `adb uninstall dev.opustower.workshop` first
(the headset forgets its pairing; pair again from the Mac). Without the
environment, the build is debug-signed as before.

## First launch

The headset needs no setup by hand: it shows a six-digit code; on the Mac,
Jarvis → Settings → Workshop → *Approve headset*. That mints its key and
sends the Wit token and the Mac's address for music. *Unpair* on the Opus
panel forgets them.
