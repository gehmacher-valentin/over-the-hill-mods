# Symmetrische Schäkel

Im Spiel haben alle vier Schäkel (vorne und hinten, links und rechts) dasselbe Modell. Deshalb zeigt die Verschraubung überall zur selben Seite.

Die Mod spiegelt je einen Schäkel vorne und hinten, sodass die Verschraubung **immer zur Fahrzeugmitte** zeigt. Außerdem wird die rote Schraube **grau** gefärbt.

- Rein optisch und nur lokal: Mitspieler ohne die Mod sehen die Originale, online gibt es keine Probleme.
- Wirkt auf alle Fahrzeuge mit Schäkeln, auch auf die der Mitspieler.
- Keine Tasten, keine Einstellungen.

## Installation

Du brauchst MelonLoader 0.7.3, die Anleitung steht im [Haupt-README](../README.md). Danach legst du `SymmetrischeSchaekel.dll` in den Ordner `Mods` im Spielverzeichnis.

## Technik

- Die Schäkel sind die Objekte `shackleFL`, `shackleFR`, `shackleRL` und `shackleRR` in den Cosmetic-Teilen `Shackles_Front` und `Shackles_Rear`.
- Gespiegelt werden `shackleFL` und `shackleRR`, und zwar über eine negative Skalierung entlang der Fahrzeug-Querachse. Hinten ist das Modell um 180° gedreht eingebaut, deshalb wird dort die andere Seite gespiegelt.
- Für die Farbe werden kräftig rote Materialfarben oder Texturpixel der Schäkel in Grau umgerechnet. Das geschieht in eigenen Material-Instanzen, die Original-Assets bleiben unverändert.

Die Farbe lässt sich im Quellcode ändern: `Schraubenfarbe` in `Mod.cs` kann `grau`, `dunkelrot` oder `original` sein.
