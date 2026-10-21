# PEAKMicFix

A **BepInEx 5** mod for **PEAK** that fixes two microphone bugs:

1. **Mic inserted after launch is unusable.** The game only assigns the local recorder's input
   device once, from a saved settings id captured at startup. If **no microphone was connected when
   the game booted** (or when the audio settings were loaded), that id is empty, so the recorder is
   never pointed at any device — and any mic plugged in later is silently ignored.
2. **Mics suddenly lose sound mid-game.** The character keeps transmitting from a stale device handle
   after a hot-unplug, a USB power-cycle, or a Windows re-enumeration. The recorder is not restarted
   onto the (re-)connected device.

## What it does

- Polls Unity's `Microphone.devices` every **2 s** (configurable) while you are in a character.
- When it finds a local `Recorder` with no usable device, it **assigns the current mic** (your saved
  choice if still present, otherwise the system default / first listed device). Assigning
  `Recorder.MicrophoneDevice` makes Photon Voice restart recording onto that device.
- When the applied device is **removed** (unplugged / power-cycled), it forgets the applied state;
  the next poll re-acquires and restarts once a device is available again.
- Optionally (default on) **writes the chosen device back** into the in-game `MicrophoneSetting` so
  the audio-settings UI and the saved value match what's actually active.

## Installation

The compiled plugin is deployed to:

```
<PEAK>\BepInEx\plugins\PEAKMicFix\
    PEAKMicFix.dll
```

A single DLL — the shared `MicrophoneDeviceWatcher` source is compiled directly into each mod that
needs it, so every mod ships standalone. Place the DLL there (or run `common\tools\deploy.ps1` after
`build.ps1`). Start the game via your mod manager / normal launch. To see log output, set
`Logging.Console.Enabled = true` in `BepInEx\config\BepInEx.cfg`.

## Configuration

Created as `BepInEx\config\numbaa.PeakMicFix.cfg` on first run:

| Key | Default | Meaning |
| --- | --- | --- |
| `PollIntervalSeconds` | `2.0` | How often to re-enumerate devices while in a game. |
| `ApplyToSettings` | `true` | Also update the in-game microphone setting to the active device. |
| `VerboseLogging` | `false` | Log every poll + every state-decision detail (~per frame, noisy). ON only when debugging device selection. |

## Logging (for diagnosis)

By default the mod is **quiet** — it logs only meaningful events:

- `Poll#tick — devices[N] — i:name, ...` — every 2 s, the current device list.
- `CHANGE#N — devices[...] added=[...] removed=[...]` + `Device event: ADDED/REMOVED → [...]` —
  each plug/unplug detected.
- `[APPLY] ...` + `[APPLY RESULT] Recorder.MicrophoneDevice now: ...` — every time the recorder is
  (re)assigned to a device.
- `[HANDLE REMOVE] ...` — when the applied device disappears and re-acquisition is queued.
- `[RECORDER CHANGE] ...` — when a new local recorder appears (spawn/respawn) and is re-bound.

To watch live, set `Logging.Console.Enabled = true` in `BepInEx\config\BepInEx.cfg`; otherwise the
same lines land in `<PEAK>\BepInEx\LogOutput.log`.

To diagnose a report: have the player reproduce the problem (boot game with no mic, plug one in;
or unplug/plug mid-game), then send back the `LogOutput.log`. The `[APPLY]` / `CHANGE#` /
`[HANDLE REMOVE]` lines tell the whole story.

## How it's built

- The game-agnostic `MicrophoneDeviceWatcher` (device-polling + change events) lives as a shared
  source file in `common/` and is **compiled directly into this plugin** so the mod ships as one DLL.
  Any future mod that wants it includes the same source file (compiled into its own DLL).
- References the game's `Assembly-CSharp.dll`, `PhotonVoice.dll`, `PhotonVoice.API.dll`,
  `PhotonUnityNetworking.dll`, and `Zorro.Settings.Runtime.dll` from `PEAK_Data\Managed`.

## Debug hint

If a player's mic "works in the lobby but dies in a match", check the `Device event: REMOVED` /
`[HANDLE REMOVE]` / `[APPLY RESULT]` log lines — they show the device list changing and the mod
re-acquiring onto a valid device.