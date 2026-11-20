# Transportaufträge (Testversion)

Frachtaufträge zum Pebbles-Verdienen, allein oder im **Konvoi mit Freunden**.

> ⚠️ **Testversion:** Der Einzelspieler ist getestet, der Multiplayer noch nicht. Rückmeldungen sind willkommen.

## So funktioniert's

1. **Beim Mechaniker (Crazy Günther) anhalten.** Links erscheint das Brett „Frachtaufträge“ mit 3 Angeboten.
   **1 / 2 / 3** nimmt einen Auftrag an, **0** holt neue Angebote.
2. **Zur Start-Hütte**, entweder hinfahren oder über die Karte schnellreisen. Die Hütte ist mit einem gelben Ring am Boden und einem Bleistiftkreis auf der Karte markiert.
3. **Im Ring kurz stehen bleiben.** Die Fracht wird geladen, und die Zeit läuft.
4. **Zum Ziel fahren.** Nach dem Laden ist **keine Schnellreise** mehr erlaubt, sonst scheidet man aus.
   Wenn das Auto Schaden nimmt, leidet auch die Fracht. Glas und Kameras leiden mehr als Brennholz, und ein Überschlag kostet zusätzlich.
5. **Im Ziel-Ring stehen bleiben.** Die Fracht wird abgeladen, und der Lohn richtet sich nach ihrem Zustand.

**F6** blendet den Stand ein oder aus: Fracht, Zustand, Ziel, Entfernung, Richtung, Restzeit, Konvoi und Kontostand.
**Entf** (bei eingeblendetem Stand) bricht den Auftrag ab bzw. steigt aus dem Konvoi aus.

### Aufträge
- **Über Wege:** Die Route läuft über die Wege der Karte, das Zeitlimit richtet sich nach der Weglänge.
- **Querfeldein:** Hütte und Ziel sind nicht über Wege verbunden, dafür gibt es deutlich mehr Zeit und etwas mehr Lohn.
  In Algerien gibt es keine Wege, dort sind alle Aufträge querfeldein.
- 10 Frachtarten von *robust* (Brennholz) bis *sehr empfindlich* (Einmachgläser), manche davon *eilig*.

## Multiplayer (Konvoi)

**Alle Spieler brauchen die Mod (dieselbe Version)** und spielen über **Steam**. Mitspieler müssen auf Steam **befreundet** sein, darüber finden sich die Mods gegenseitig. Der Host verwaltet die Aufträge.

- Alle sehen dieselben Angebote, und jeder kann sie beim Mechaniker annehmen.
- **Mitfahrer ist, wer an der Hütte Fracht lädt.** Das geht auch noch, nachdem der Konvoi losgefahren ist, solange die Zeit läuft.
- Jeder transportiert seine eigene Ladung mit eigenem Zustand und Lohn. Wer schnellreist oder aussteigt, scheidet nur selbst aus.
- **Gruppenbonus:** +15 % Lohn pro weiterem Mitfahrer, der abliefert.
- Verlässt der Host die Sitzung, geht der laufende Auftrag verloren.

Falls der Multiplayer Probleme macht, kannst du ihn in `UserData/MelonPreferences.cfg` abschalten:
```
[Auftraege]
Multiplayer = false
```

## Installation

MelonLoader 0.7.3 installieren (siehe [Haupt-README](../README.md)), dann `Auftraege.dll` in den Ordner `Mods` legen.

## Bekannte Grenzen

- Die Ortsnamen sind englisch, so wie im Spiel. Auf Kanada-Karte 1 kommen ein paar selbst benannte Ziele an Wegenden dazu (Sägewerk, Bergkamm …).
- Während ein Auftrag läuft, gibt es keine weiteren Angebote, es läuft immer nur ein Auftrag pro Sitzung.
