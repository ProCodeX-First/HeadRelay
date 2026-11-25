# Fremdanteile in HeadRelay 0.2.1

Die MIT-Lizenz im Projektstamm gilt für die eigenen Beiträge von ProCodeX. Beim Weitergeben der genannten Bestandteile bleiben die folgenden Hinweise erhalten.

| Bestandteil | Einsatz und Weitergabe | Lizenz |
|---|---|---|
| linuxtrack | Angepasste Routinen für Paketprüfsumme und Codierung in `native/AxisBridge.c`. Im Quellcode und in den gebauten Client-DLLs enthalten. | MIT, vollständiger Text in [Licenses/LINUXTRACK-MIT.txt](Licenses/LINUXTRACK-MIT.txt). [Originalquelle](https://github.com/uglyDwarf/linuxtrack/blob/master/src/wine_bridge/client/NPClient_main.c) |
| opentrack | FreeTrack-Protokollreferenz für die Spielausgabe in `src/Core.cs`. Der Originalvermerk folgt unten. | ISC, [Originalquelle](https://github.com/opentrack/opentrack/blob/master/proto-ft/ftnoir_protocol_ft.cpp) |
| opentrack für Windows (separat installiert) | Für die direkte TrackIR-Ausgabe registriert HeadRelay den lokalen Ordner mit `NPClient.dll` und `NPClient64.dll`. Beide Dateien bezieht der Nutzer mit der OpenTrack-Distribution. | Es gelten die jeweiligen Lizenzhinweise der separat bezogenen Distribution. Die HeadRelay-MIT-Lizenz räumt keine Rechte an diesen Binärdateien ein. [Offizielle Windows-Downloads](https://github.com/opentrack/opentrack/releases) |
| .NET und WPF | C#-Compiler, Basisklassen und Windows-Desktop-Framework aus dem .NET 10 SDK. Der Windows-Programmstarter (Apphost) steckt in `HeadRelay.exe`. Die Desktop Runtime wird separat installiert. Das Projekt hat keine zusätzlichen NuGet-Paketreferenzen. | Apphost und Open-Source-Code MIT, [Lizenztext](Licenses/DOTNET-MIT.txt). Für bestimmte separat installierte Windows-Runtime-Binärdateien gelten Microsoft-Lizenzen. [Zuordnung von Microsoft](https://github.com/dotnet/core/blob/main/license-information-windows.md) |
| Zig 0.13.0 mit Clang, dlltool und LLD | Externe Werkzeuge zum Übersetzen und Linken der C-Brücke. | Zig MIT, LLVM-Werkzeuge Apache-2.0 WITH LLVM-exception. [Zig](https://github.com/ziglang/zig/blob/0.13.0/LICENSE) · [LLVM](https://llvm.org/LICENSE.txt) |
| mingw-w64 | Windows-Header des Zig-Compilers. Die Brücke importiert Funktionen aus Windows-System-DLLs und bindet keine MinGW-CRT-Startdateien ein. | ZPL-2.1 und dateispezifische Hinweise, darunter als Public Domain gekennzeichnete Header. [Mitgelieferter Hinweis](Licenses/MINGW-RUNTIME.txt) |
| Windows-APIs | HID, SetupAPI, Kernel32, User32, DWM, Shell32, MSVCRT und Registry-Funktionen der Windows-Installation. | Windows-Systemkomponenten unter Microsofts Bedingungen, separat vorhanden. |

## Originalvermerk zur opentrack-Protokollreferenz

```text
Copyright (c) 2013-2015, 2017 Stanislaw Halik <sthalik@misaki.pl>
Copyright (c) 2015 Wim Vriend

Permission to use, copy, modify, and/or distribute this software for any purpose with or without fee is hereby granted, provided that the above copyright notice and this permission notice appear in all copies.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Übernahme in GPL-3.0-Projekte

VerseKit kann die unter MIT und ISC weitergegebenen Quellteile in eine GPL-3.0-Fassung einbauen. Dabei sind die Urheber-, Erlaubnis- und Gewährleistungshinweise der übernommenen Teile beizubehalten. Wer das kombinierte Werk verbreitet, erfüllt zusätzlich die für dieses Werk geltenden GPL-3.0-Pflichten. Der Windows-Download ist frameworkabhängig. Beim Bündeln einer eigenen Desktop Runtime gelten deren Bedingungen gesondert. [GNU-Lizenzliste](https://www.gnu.org/licenses/license-list.html)
