# peak-mods

Mods for the game **PEAK** (LandCrab), loaded through **BepInEx 5** (`BepInExPack_PEAK`)
([Harmony](https://github.com/BepInEx/BepInEx) available for runtime patching where a patch is needed).

Game: `C:\Users\ntu\game\Steam\steamapps\common\PEAK` · Unity **6000.3.15f1** · BepInEx 5.4.23.3 · Managed at `PEAK_Data\Managed`.

## Layout

```
peak-mods/
├── README.md
├── common/
│   ├── PeakModding.Common/     # shared source files (audio watcher etc.) — compiled INTO each mod's DLL
│   ├── decompiled/             # decompiled game assemblies (dev reference only — local, git-ignored, not shipped)
│   └── tools/
│       ├── build.ps1           # builds every mod into common/artifacts
│       └── deploy.ps1          # copies built plugins into <game>\BepInEx\plugins
├── micfix/                     # ★ this mod: microphone hot-plug fix
│   ├── PEAKMicFix/             # BepInEx plugin project (single plugin DLL: PEAKMicFix.dll)
│   └── README.md
└── <future mods go in their own subfolders>
```

Each mod lives in its own top-level subfolder with its own plugin project and README. Anything shared
(applies to several mods) belongs under `common/` as **source files that get compiled into each mod's
own DLL** — so every mod ships standalone as a single DLL. Add mods as siblings of `micfix/`.

## Building

Prereqs: .NET SDK 8+ (the repo targets `netstandard2.1`, BepInEx 5 runs on the game's Unity/Mono runtime).

Env vars used by the build (already the real paths for this machine; override if the game moves):

- `PEAK_GAME` dir containing `PEAK.exe`
- `PEAK_MANAGED` → `<PEAK_GAME>\PEAK_Data\Managed`
- `PEAK_BEPINEX_CORE` → `<PEAK_GAME>\BepInEx\core`

Build + deploy everything:

```powershell
powershell -ExecutionPolicy Bypass -File common\tools\build.ps1
powershell -ExecutionPolicy Bypass -File common\tools\deploy.ps1
```

Then start the game via your mod manager / normal launch. Set `Logging.Console.Enabled = true` in
`BepInEx\config\BepInEx.cfg` to see log output.