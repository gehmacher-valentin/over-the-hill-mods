# Over the Hill Mods

Kleine Mods für [Over the Hill](https://store.steampowered.com/app/2929250/) (Funselektor) auf Basis von **MelonLoader**.

| Mod | Beschreibung | Download |
|---|---|---|
| [Symmetrische Schäkel](SymmetrischeSchaekel/) | Die Schäkel sind vorne und hinten spiegelsymmetrisch, die Verschraubung zeigt immer zur Fahrzeugmitte. Die rote Schraube wird grau. | [v1.0.0](../../releases/tag/schaekel-v1.0.0) |
| [Transportaufträge](Auftraege/) *(Testversion)* | Frachtaufträge zum Pebbles-Verdienen: beim Mechaniker annehmen, an der Hütte laden, heil abliefern. Im Multiplayer als Konvoi. | [v0.5.1](../../releases/tag/auftraege-v0.5.1) |

Die **Symmetrischen Schäkel** ändern nur, was du selbst siehst. Mitspieler müssen sie nicht installieren.
Bei den **Transportaufträgen** brauchen im Multiplayer **alle Spieler** die Mod.

## Installation

> **Du nutzt schon andere Mods für Over the Hill (mit MelonLoader)?**
> Dann lad einfach die `.dll` herunter und leg sie in den `Mods`-Ordner im Spielordner. Fertig.

### Windows

**Schritt 1: MelonLoader installieren** (nur beim ersten Mal)

1. Lade `MelonLoader.Installer.exe` von dieser Seite herunter: https://github.com/LavaGang/MelonLoader/releases/tag/v0.7.3
2. Starte den Installer.
3. Klick auf *Add* bzw. *Select* und wähl `over the hill.exe` im Spielordner aus.
   Den Spielordner findest du in Steam so: Rechtsklick auf das Spiel → *Verwalten* → *Lokale Dateien durchsuchen*.
4. Stell als Version **v0.7.3** ein (nicht die neueste!) und klick auf *Install*.
5. Starte das Spiel einmal. Der erste Start dauert ein bis zwei Minuten länger, dabei wird auch der Ordner `Mods` angelegt.

**Schritt 2: Mod installieren**

1. Lad die `.dll` der Mod herunter (Link in der Tabelle oben).
2. Leg sie in den Ordner `Mods` im Spielordner.
3. Starte das Spiel.

**Läuft die Mod?** Beim Start mit MelonLoader geht ein Konsolenfenster auf. Steht dort der Name der Mod (zum Beispiel „Symmetrische Schaekel v1.0.0“), ist sie geladen.

Windows oder dein Virenscanner warnen bei Mod-DLLs manchmal, weil die Dateien unbekannt sind. Das ist bei Mods normal.

### Linux / Steam Deck

Hier läuft das Spiel über Proton.

1. In Steam: Rechtsklick auf das Spiel → *Eigenschaften* → *Kompatibilität* → Proton erzwingen (zum Beispiel *Proton Experimental*).
2. Lad `MelonLoader.x64.zip` von https://github.com/LavaGang/MelonLoader/releases/tag/v0.7.3 herunter und entpack die Datei in den Spielordner. Danach liegen dort `version.dll` und der Ordner `MelonLoader`.
3. In Steam unter *Eigenschaften* → *Allgemein* → *Startoptionen* eintragen:
   ```
   WINEDLLOVERRIDES="version=n,b" %command%
   ```
4. Starte das Spiel einmal, damit MelonLoader seine Dateien anlegt. Das dauert ein bis zwei Minuten länger als sonst.
5. Leg die `.dll` der Mod in den Ordner `Mods` im Spielordner. Falls es den Ordner noch nicht gibt, leg ihn selbst an.

### Deinstallieren

Lösch die `.dll` aus dem Ordner `Mods`. Wenn du MelonLoader ganz loswerden willst, lösch zusätzlich `version.dll` und den Ordner `MelonLoader` aus dem Spielordner.

## Nach Spiel-Updates

Nach einem großen Update kann es sein, dass eine Mod nicht mehr funktioniert, bis sie angepasst wurde. Wenn das Spiel deswegen Probleme macht, nimm die `.dll` einfach vorübergehend aus dem `Mods`-Ordner.

## Selbst bauen

Du brauchst das [.NET SDK](https://dotnet.microsoft.com/) (Version 6 oder neuer). Außerdem musst du das Spiel mindestens einmal mit MelonLoader gestartet haben, damit `MelonLoader/Il2CppAssemblies/` existiert.

```sh
cd SymmetrischeSchaekel
dotnet build -c Release -p:SpielOrdner="/pfad/zu/over the hill"
```

Die fertige DLL liegt danach in `bin/Release/net6.0/`.
