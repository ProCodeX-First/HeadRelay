# HeadRelay 0.2.0

HeadRelay überträgt Kopfbewegungen eines JBL Quantum 910 oder 950 auf die Spielkamera. Die Windows-App liest den Headset-Sensor über USB-HID, kalibriert Drehen und Nicken und gibt die Bewegungen über eine eigene 32-/64-Bit-Spielbrücke oder per UDP aus.

## Systemvoraussetzungen

- Windows x64 und [.NET Desktop Runtime 10 für Windows x64](https://dotnet.microsoft.com/download/dotnet/10.0).
- Zum Spielen ein unterstütztes Headset mit verbundenem USB-Dongle oder Basisstation und aktiviertem Headtracking. Die automatischen Tests laufen ohne Headset.

## Starten

Die Windows-ZIP unter [Releases](../../releases) vollständig in einen beschreibbaren Ordner entpacken und `HeadRelay.exe` starten.

1. Headset verbinden.
2. Unter Kalibrierung geradeaus, nach rechts und nach oben aufnehmen. Anschließend wieder geradeaus schauen und zentrieren.
3. Unter Spielanbindung die eigene HeadRelay-Spielbrücke einmal einrichten. Dafür werden nur Benutzer-Registrywerte geändert; vorhandene Pfade werden gesichert.
4. Tracking starten, danach das Spiel starten oder neu starten. Als Quelle TrackIR auswählen, falls das Spiel diese Schnittstelle verwendet.
5. F9 zentriert. F10 pausiert oder setzt die Ausgabe fort. Empfindlichkeit, Kurve, Ruhezone und Glättung stehen unter Bewegung.

Vorherige Anbindung wiederherstellen setzt gespeicherte Registrywerte nur zurück, wenn der aktuelle Wert noch zur eigenen HeadRelay-Brücke gehört. App-Ordner nach der Einrichtung nicht verschieben; andernfalls erneut einrichten. Andere Tracking-Sender vor direkter Ausgabe schließen.

## Aus dem Quellcode bauen

Auf Windows x64 das [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) und [Zig 0.13.0 für Windows x64](https://ziglang.org/download/0.13.0/) installieren. Das Verzeichnis mit `zig.exe` zum PATH hinzufügen. In PowerShell im Projektstamm ausführen.

```powershell
.\tools\Build.ps1
```

Über `-ZigPath` lässt sich alternativ der Pfad zur entpackten `zig.exe` angeben. Das Skript baut beide nativen Brücken und die WPF-App. Es entpackt die neu erzeugte ZIP, führt den App-Selbsttest und einen separaten nativen x86-Test aus. Das geprüfte Paket liegt anschließend unter `artifacts/HeadRelay-0.2.0-Windows.zip`. Für einen erneuten Selbsttest in der entpackten ZIP `.\HeadRelay.exe --test` ausführen. Das Ergebnis steht in `Tests/TestResults.txt`.

## Funktionsstand und Grenzen

- Sensoradapter für JBL Quantum 910 und 950; persönliche Achsenkalibrierung, Vorschau, Bewegungsfilter, Hotkeys und Diagnoseexport.
- Eigene 32- und 64-Bit-Spielbrücke. Es werden keine kopierten OpenTrack- oder NaturalPoint-Binaries mitgeliefert.
- Neu geschriebener Hilfsprozess TrackIR.exe dient der Prozess-Erkennung einzelner Spiele und endet mit HeadRelay. Er ist keine NaturalPoint-Anwendung.
- Das eingebaute Star-Citizen-Profil und die Paketübertragung werden mit Protokolltests geprüft. Ein Test dieser neuen Brücke im laufenden Star Citizen steht noch aus.
- Hersteller-Signaturdaten werden nicht mitgeliefert. Spiele, die diese Signaturen prüfen, sind nicht als kompatibel bestätigt.
- Nur Drehen/Nicken; keine Positionsmessung und keine Roll-Ausgabe.
- Gemessen am 30.10.2026 über ca. 20 Sekunden im vorhandenen Funk-/USB-Aufbau: 910 etwa 99,6 Rotationspakete/s, 950 etwa 23,5. Dies sind Messwerte dieses Aufbaus, keine allgemeine Sensorgrenze.
- Weitere Headsets und Spiele sind nicht pauschal unterstützt. Numerische Spieleprofile können in Profiles/games.json ergänzt werden. Format: JSON-Liste mit Id, Name und KeyHex (16 Hex-Zeichen; 0000000000000000 für unverschlüsselten Modus).

## Optionale UDP-Ausgabe

UDP / FreePIE überträgt an 127.0.0.1:5555. Dafür ist ein passender Empfänger erforderlich. Diese optionale Ausgabe ist unabhängig von der direkten HeadRelay-Brücke.

## Einstellungen und Diagnose

Data/settings.json enthält Einstellungen und persönliche Kalibrierung. Data/integration-backup.json sichert vorherige Registrypfade. Diagnoseberichte entstehen erst beim Export unter Diagnose. Diese Dateien sind nicht Teil des Downloads und sollten vor öffentlichem Teilen geprüft werden.

## Lizenz

HeadRelay steht unter der [MIT-Lizenz](LICENSE). Wer Teile in VerseKit oder ein anderes GPL-3.0-Projekt übernimmt, muss die zugehörigen Hinweise beibehalten. Sie stehen in [THIRD_PARTY.md](THIRD_PARTY.md) und im Ordner [Licenses](Licenses). JBL, TrackIR und die genannten Spiele sind Marken ihrer jeweiligen Inhaber. HeadRelay ist ein unabhängiges Projekt.
