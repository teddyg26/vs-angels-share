# Dev Build Commands:
- Quick run:
```PowerShell
powershell -ExecutionPolicy Bypass -File .\tools\deploy.ps1
```
- Explicit target:
```PowerShell
powershell -ExecutionPolicy Bypass -File .\tools\deploy.ps1 -ModDir "$env:APPDATA\VintagestoryData\Mods\AngelsShare_X.X.X"
```

NOTE: Make sure you have set environment variables in your terminal that match what the .csproj file expects. These should point to your game files and game data. A .zsh script
is also in the ./tools folder for development on MacOS and Linux, however at current instability with Rosetta 2 causes crashes on MacOS.