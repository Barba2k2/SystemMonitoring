# SystemMonitoring

Minimal game overlay for Windows: CPU and GPU usage, temperatures and FPS in a thin bar centered at the top of the screen. A lighter take on MSI Afterburner's on-screen display.

```
CPU 45% 62°C  │  GPU 78% 71°C  │  144 FPS
```

## Features

- Click-through, always-on-top overlay that never takes focus
- Centered at the top of the monitor the game runs on
- CPU/GPU load and temperature via [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (NVIDIA GPUs)
- FPS of the foreground process from ETW DXGI Present events, smoothed over a 1s sliding window
- Hotkeys: `Ctrl+Alt+F10` show/hide, `Ctrl+Alt+F11` quit

## Requirements

- Windows 10/11, .NET 8 SDK
- Administrator rights (hardware driver and ETW session)
- Game in **borderless/windowed** mode (the overlay is a regular window, so exclusive fullscreen hides it)
- AMD CPU temperature needs the [PawnIO](https://pawnio.eu/) driver: `winget install namazso.PawnIO`

## Build and run

```
dotnet build src/SystemMonitor -c Release
```

Then start `src/SystemMonitor/bin/Release/net8.0-windows/SystemMonitor.exe` as administrator.

## Known limits

- FPS covers DirectX 9-12 only; Vulkan and OpenGL need the DxgKrnl ETW provider.
- No anti-cheat interaction: nothing is injected into games.
