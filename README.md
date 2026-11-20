# Over the Hill Mods

Kleine Mods für [Over the Hill](https://store.steampowered.com/app/2929250/) (Funselektor) auf Basis von **MelonLoader**.

| Mod | Beschreibung |
|---|---|
| [Symmetrische Schäkel](SymmetrischeSchaekel/) | Schäkel vorne und hinten spiegelsymmetrisch, die Verschraubung zeigt immer zur Fahrzeugmitte. Die rote Schraube wird grau. |

Fertige DLLs gibt es unter [Releases](../../releases).

## Installation

### 1. MelonLoader installieren (einmalig)

Die Mods brauchen **MelonLoader 0.7.3**. Andere Versionen und BepInEx funktionieren nicht.

**Windows:**
1. Lade `MelonLoader.Installer.exe` von [MelonLoader v0.7.3](https://github.com/LavaGang/MelonLoader/releases/tag/v0.7.3) herunter.
2. Starte den Installer, wähl `over the hill.exe` aus, setz die Version auf **v0.7.3** und klick auf *Install*.

**Linux / Steam Deck (über Proton):**
1. Stell in Steam unter *Eigenschaften → Kompatibilität* Proton ein (zum Beispiel Proton Experimental).
2. Lade `MelonLoader.x64.zip` von [MelonLoader v0.7.3](https://github.com/LavaGang/MelonLoader/releases/tag/v0.7.3) herunter und entpack die Datei in den Spielordner. Danach liegen dort `version.dll` und der Ordner `MelonLoader/`.
3. Trag in Steam unter *Eigenschaften → Allgemein → Startoptionen* ein:
   ```
   WINEDLLOVERRIDES="version=n,b" %command%
   ```

Der erste Start mit MelonLoader dauert ein bis zwei Minuten länger, weil Dateien erzeugt werden. Ab dem zweiten Start geht's wieder normal schnell.

### 2. Mod installieren

Lade die `.dll` der Mod aus den [Releases](../../releases) herunter und leg sie in den Ordner `Mods` im Spielverzeichnis:

```
…/steamapps/common/over the hill/Mods/
```

Den Spielordner findest du in Steam per Rechtsklick auf das Spiel → *Verwalten* → *Lokale Dateien durchsuchen*.

### Deinstallieren

Lösch die `.dll` aus dem Ordner `Mods`.

## Selbst bauen

Du brauchst das [.NET SDK](https://dotnet.microsoft.com/) (Version 6 oder neuer). Außerdem musst du das Spiel mindestens einmal mit MelonLoader gestartet haben, damit `MelonLoader/Il2CppAssemblies/` existiert.

```sh
cd SymmetrischeSchaekel
dotnet build -c Release -p:SpielOrdner="/pfad/zu/over the hill"
```

Die fertige DLL liegt danach in `bin/Release/net6.0/`.
