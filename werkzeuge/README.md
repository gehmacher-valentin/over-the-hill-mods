# Werkzeuge

Python-Skripte (mit [UnityPy](https://github.com/K0lb3/UnityPy)), um Kartendaten direkt aus den Spieldateien zu lesen. Damit lassen sich Mods auch für Karten abstimmen, die man selbst noch nicht freigespielt hat.

```sh
python -m venv venv && venv/bin/pip install UnityPy
B=".../over the hill_Data/StreamingAssets/aa/StandaloneWindows64"
venv/bin/python texte.py "$B/localization-string-tables-english(en)_assets_all.bundle" texte_en.json
venv/bin/python karten.py "$B/r1_canada_scenes_all_<hash>.bundle" texte_en.json r1.json
```

`karten.py` schreibt pro Karte (Szene) eine Liste mit allen Orten (Typ, Name, Position) und allen Splines (Pfad, Länge, Endpunkte).
