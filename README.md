# Smart-ÖPNV Shared

Gemeinsame Bibliotheken für **Smart-ÖPNV Planer** und **Smart-ÖPNV Leitstelle**.

| Projekt | Inhalt |
|---------|--------|
| `SmartOepnv.Core` | Dropbox, Route-Paket (JSON), Geschäftslogik |
| `SmartOepnv.AppShared` | Gemeinsame WPF-Oberfläche (Shell, Views, ViewModels) |

Die beiden Desktop-Programme sind **eigenständige Projekte** in eigenen Ordnern und verweisen hierher:

- `..\SmartOepnv.Shared\src\...` (relativ von Planer/Leitstelle)

## Build

```powershell
cd C:\Users\hkx18\AndroidStudioProjects\SmartOepnv.Shared
dotnet build SmartOepnv.Shared.sln
```
