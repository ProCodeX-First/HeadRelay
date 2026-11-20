# Weitere Geräteadapter

Diese Vorschau enthält zwei feste Adapter (JBL 910 und 950). Zusätzliche *.json-Dateien in diesem Ordner werden beim App-Start validiert und geladen. Es wird daraus kein Code ausgeführt.

Die aktuelle deklarative Form unterstützt vollständige USB-HID-Berichte mit vier signed-int16-Little-Endian-Quaternion-Werten in bekannter Skalierung. Für Bluetooth-GATT, Float32-Pakete, reine Gyroskopdaten oder ein unbekanntes Aktivierungsprotokoll ist ein Codeadapter erforderlich.

Adapterdaten müssen anhand realer Gerätedaten bestimmt werden. Eine Kalibrierung kann ein unbekanntes Paketformat nicht erraten. Geräte erst nach tatsächlicher Signal- und Bewegungsprüfung als unterstützt bezeichnen.

Das Beispiel endet absichtlich auf .json.example und wird nicht geladen. Nach Anpassung der tatsächlichen VID/PID, Schnittstelle, Collection, Report-Länge, Kennung, Header und Quaternion-Offsets kann es unter einem eigenen Namen mit der Endung .json gespeichert werden. Eindeutige Id verwenden und die App neu starten.

Die vier Offsets bezeichnen x, y, z und w ab Byte 0 inklusive HID-Report-ID. Es werden nur Quaternionen mit Norm 0,97 bis 1,03 akzeptiert. Das Auslesen erfolgt ohne Schreibbefehle an die Geräte. Manche Headsets benötigen zuvor die Aktivierung des Headtrackings in der Herstellersoftware.
