# HeadRelay 0.2.1

HeadRelay steuert die Spielkamera mit den Kopfbewegungen eines JBL Quantum 910 oder 950. Die Windows-App liest den Sensor am USB-Dongle oder an der Basisstation. Für Star Citizen sendet HeadRelay die Bewegung an die TrackIR-Schnittstelle aus der lokalen OpenTrack-Installation.

## Voraussetzungen und Download

- Windows x64 mit der [.NET Desktop Runtime 10 für Windows x64](https://dotnet.microsoft.com/download/dotnet/10.0).
- JBL Quantum 910 oder 950 mit verbundenem Dongle bzw. verbundener Basisstation und aktiviertem Headtracking.
- Für die direkte Spielanbindung OpenTrack für Windows installieren. HeadRelay benötigt die Dateien `modules/NPClient.dll` und `modules/NPClient64.dll` aus der Installation. OpenTrack selbst während des Spielens geschlossen lassen. [OpenTrack für Windows herunterladen](https://github.com/opentrack/opentrack/releases)
- `HeadRelay-0.2.1-Windows.zip` vollständig in einen beschreibbaren Ordner entpacken und `HeadRelay.exe` starten. [Windows-ZIP herunterladen](https://github.com/ProCodeX-First/HeadRelay/releases/download/v0.2.1/HeadRelay-0.2.1-Windows.zip)

## In Star Citizen umschauen

1. In HeadRelay das Headset verbinden. Beim ersten Start unter **Kalibrierung** nacheinander geradeaus schauen, nach rechts drehen und nach oben schauen. Wieder geradeaus schauen und zentrieren.
2. Unter **Spielanbindung** die direkte Ausgabe wählen und einmal auf **Spielanbindung einrichten** klicken. HeadRelay findet OpenTrack in den üblichen Installationsordnern. Bei einer Installation in einem eigenen Ordner die dortige `opentrack.exe` auswählen.
3. **Tracking starten**. Dann Star Citizen starten. War das Spiel bei der Einrichtung bereits geöffnet, das Spiel vollständig beenden und neu starten.
4. In Star Citizen unter **Options → Comms, FoIP & Head Tracking** als Quelle **TrackIR** wählen und Headtracking aktivieren. Wenn die Verbindung steht, erscheint in HeadRelay **Star Citizen**. F9 zentriert den Blick. F10 pausiert die Ausgabe oder setzt sie fort.

### Update von 0.2.0

Wer die Spielanbindung der alten Version später vollständig zurücksetzen möchte, kann vor dem Wechsel dort **Vorherige Anbindung wiederherstellen** wählen. Die alte App vor dem Wechsel schließen. Die neue ZIP in einen eigenen Ordner entpacken. Für die bisherige Kalibrierung bei geschlossenen Apps nur `Data/settings.json` in den gleichnamigen Unterordner der neuen Version kopieren. Danach die Spielanbindung in **0.2.1** erneut einrichten und die Startreihenfolge oben beachten.

Wenn sich die Vorschau bewegt, HeadRelay aber weiterhin **Warte auf Spiel** anzeigt, unter **Spielanbindung** die Einrichtung erneut ausführen. Anschließend das Tracking starten und das Spiel neu öffnen. Der frühere Download 0.2.0 enthielt eine eigene TrackIR-Client-DLL, mit der Star Citizen im Praxistest keine Verbindung aufgebaut hat.

## Bauen und testen

Windows x64, das [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) und [Zig 0.13.0 für Windows x64](https://ziglang.org/download/0.13.0/) werden zum Bauen benötigt. Den Ordner mit `zig.exe` zum PATH hinzufügen und im Projektstamm in PowerShell ausführen.

```powershell
.\tools\Build.ps1
```

Alternativ akzeptiert das Skript `-ZigPath` mit dem Pfad zur entpackten `zig.exe`. Es baut die WPF-App, die FreeTrack-DLLs für x86 und x64 und den Hilfsprozess. Es entpackt die erzeugte ZIP, prüft ihren Inhalt und führt die Selbsttests sowie einen separaten nativen x86-Test aus. Das geprüfte Paket liegt unter `artifacts/HeadRelay-0.2.1-Windows.zip`. Die Selbsttests lassen sich mit `.\HeadRelay.exe --test` wiederholen. Das Ergebnis steht in `Tests/TestResults.txt`. Die Tests verwenden eigene temporäre Registrywerte und funktionieren auch ohne Headset.

## Getestete Geräte und Ausgabe

- **JBL Quantum 910 und 950** wurden am Sensor getestet. Im Testaufbau vom 30.10.2026 lieferte das 910 über rund 20 Sekunden etwa 99,6 Rotationspakete pro Sekunde, das 950 etwa 23,5. Andere Modelle sind bisher ungetestet.
- **Star Citizen LIVE und JBL Quantum 950** wurden mit dem OpenTrack-Client im Spiel getestet. Die Kopfbewegung steuerte die Kamera. Für die ZIP von 0.2.1 werden Pfadauswahl, Registrierung, Wiederherstellung und Datenausgabe separat automatisiert geprüft.
- **Bewegung** umfasst Drehen und Nicken. Empfindlichkeit, Ruhezone und Glättung sind einstellbar. Weitere Spieleprofile lassen sich in `Profiles/games.json` ergänzen. [Profilformat](Profiles/README.md)
- **FreeTrack** verwendet die eigenen 32- und 64-Bit-DLLs im Ordner `Bridge`. Der mitgelieferte Hilfsprozess `TrackIR.exe` dient der Spielerkennung und endet mit HeadRelay.
- **UDP / FreePIE** sendet optional an `127.0.0.1:5555`. Dafür wird ein passender Empfänger benötigt. Diese Ausgabe funktioniert unabhängig von der OpenTrack-Installation.

## Einstellungen und Diagnose

Die Einrichtung speichert die bisherigen Spielpfade in `Data/integration-backup.json`. **Vorherige Anbindung wiederherstellen** setzt nur Pfade zurück, die noch auf die eingerichteten Ziele zeigen. Nach dem Verschieben des App-Ordners die Spielanbindung erneut einrichten. Persönliche Kalibrierung und Einstellungen stehen in `Data/settings.json`. Diagnoseberichte entstehen beim Export in der App.

## Lizenz

HeadRelay steht unter der [MIT-Lizenz](LICENSE). Bei einer Übernahme in VerseKit oder ein anderes GPL-3.0-Projekt sind die Urheber- und Lizenzhinweise der übernommenen Teile beizubehalten. Einzelheiten stehen in [THIRD_PARTY.md](THIRD_PARTY.md) und [Licenses](Licenses). JBL, TrackIR und Star Citizen sind Marken ihrer jeweiligen Inhaber.
