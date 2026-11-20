// Wegenetz aus den Weg-Splines des Spiels (MicroVerse „POI Props & Trails“).
// Die Splines werden in Punkte im Abstand von ~15 m zerlegt; benachbarte Punkte verschiedener
// Wege werden zu Kreuzungen verbunden. Routen per Dijkstra.
// Zusätzlich liefern bekannte Wege benannte Zielpunkte (z. B. Sägewerk, Bergkamm).

using System;
using System.Collections.Generic;
using System.Linq;
using MelonLoader;
using UnityEngine;
using UnityEngine.Splines;

namespace Auftraege
{
    internal struct Route
    {
        public bool Verbunden;
        public float WegM;        // Strecke auf Wegen
        public float GelaendeM;   // Strecke abseits (zum Weg hin / vom Weg weg)
    }

    internal static class Wegenetz
    {
        const float Abstand = 15f;          // Abtastabstand auf den Splines
        const float KreuzungRadius = 25f;   // Punkte verschiedener Wege so nah -> verbunden
        const float MaxZumWegM = 300f;      // weiter weg = nicht an das Wegenetz angebunden
        const float Zelle = 50f;

        // Bekannte Wege -> Name des Zielpunkts am Wegende (Karte 1)
        static readonly Dictionary<string, string> Wegziele = new()
        {
            ["Sawmill_Spline"] = "Sägewerk",
            ["ForestClearingSpline"] = "Waldlichtung",
            ["MountainRidge_Spline"] = "Bergkamm",
            ["DangerousCliffRoad"] = "Klippenstraße",
            ["BackofMountainSpline"] = "Hinter dem Berg",
            ["BoatCabin_Spline"] = "Bootssteg",
            ["TrailThinner"] = "Waldpfad",
        };

        static List<Vector3> _punkte = new();
        static List<List<(int nach, float kosten)>> _kanten = new();
        static Dictionary<(int, int), List<int>> _gitter = new();
        static List<Ort> _wegziele = new();
        public static bool Bereit { get; private set; }

        public static IReadOnlyList<Ort> Wegziele_ => _wegziele;

        public static void Zuruecksetzen()
        {
            _punkte = new(); _kanten = new(); _gitter = new(); _wegziele = new(); Bereit = false;
        }

        public static void Aufbauen(List<Ort> orte)
        {
            Zuruecksetzen();
            var wegIds = new List<List<int>>();
            float gesamt = 0;
            try
            {
                foreach (var c in UnityEngine.Object.FindObjectsOfType<SplineContainer>())
                {
                    if (c == null) continue;
                    string pfad = Pfad(c.transform);
                    // Weg-Splines liegen auf den Kanada-Karten unter „POI Props & Trails“; Hilfsformen ausschließen.
                    // (Algerien-Karten haben keine Weg-Splines – dort gibt es nur Querfeldein-Aufträge.)
                    if (!pfad.Contains("Trails") && !pfad.Contains("Road")) continue;
                    if (pfad.Contains("Flatten") || pfad.Contains("Detail") || pfad.Contains("SplineArea")) continue;

                    int anzahl = 1;
                    try { anzahl = c.Splines.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<Spline>>().Count; } catch { }
                    for (int s = 0; s < anzahl; s++)
                    {
                        float len;
                        try { len = c.CalculateLength(s); } catch { continue; }
                        if (len < 20f) continue;
                        gesamt += len;
                        int n = Mathf.Max(2, Mathf.CeilToInt(len / Abstand) + 1);
                        var ids = new List<int>();
                        for (int i = 0; i < n; i++)
                        {
                            var f = c.EvaluatePosition(s, i / (float)(n - 1));
                            ids.Add(PunktHinzu(new Vector3(f.x, f.y, f.z)));
                        }
                        for (int i = 1; i < ids.Count; i++) Verbinden(ids[i - 1], ids[i]);
                        wegIds.Add(ids);

                        if (s == 0 && Wegziele.TryGetValue(c.name, out var zielName))
                            WegzielHinzu(zielName, _punkte[ids[0]], _punkte[ids[^1]], orte);
                    }
                }
            }
            catch (Exception e) { MelonLogger.Warning("[Wegenetz] Aufbau fehlgeschlagen: " + e.Message); }

            // Kreuzungen: nahe Punkte verschiedener Wege verbinden
            var wegVon = new int[_punkte.Count];
            for (int w = 0; w < wegIds.Count; w++) foreach (int id in wegIds[w]) wegVon[id] = w;
            int kreuzungen = 0;
            for (int a = 0; a < _punkte.Count; a++)
                foreach (int b in Nachbarn(_punkte[a], KreuzungRadius))
                    if (b > a && wegVon[a] != wegVon[b]) { Verbinden(a, b); kreuzungen++; }

            Bereit = _punkte.Count > 0;
            MelonLogger.Msg($"[Wegenetz] {wegIds.Count} Wege, {gesamt / 1000f:F1} km, {_punkte.Count} Punkte, " +
                            $"{kreuzungen} Verbindungen, Zusatzziele: {string.Join(", ", _wegziele.Select(z => z.Name))}");
        }

        /// Route zwischen zwei Orten über das Wegenetz.
        public static Route Berechnen(Vector3 von, Vector3 nach)
        {
            var r = new Route();
            if (!Bereit) return r;
            int a = Naechster(von, out float da), b = Naechster(nach, out float db);
            if (a < 0 || b < 0 || da > MaxZumWegM || db > MaxZumWegM) return r;

            float weg = Dijkstra(a, b);
            if (float.IsInfinity(weg)) return r;
            r.Verbunden = true; r.WegM = weg; r.GelaendeM = da + db;
            return r;
        }

        // ---------------- intern ----------------

        static void WegzielHinzu(string name, Vector3 anfang, Vector3 ende, List<Ort> orte)
        {
            // Das Wegende, das weiter von bekannten Orten entfernt ist, ist das „Ziel“ des Weges
            float Abst(Vector3 p) => orte.Count == 0 ? 1e9f : orte.Min(o => Flach(o.Position - p));
            var p = Abst(ende) >= Abst(anfang) ? ende : anfang;
            if (Abst(p) < 150f) return; // liegt schon an einem bekannten Ort
            _wegziele.Add(new Ort { Name = name, Position = p, Typ = Il2Cpp.PointOfInterestType.Generic, Erreicht = true, InfoKlasse = "Weg" });
        }

        static int PunktHinzu(Vector3 p)
        {
            int id = _punkte.Count;
            _punkte.Add(p);
            _kanten.Add(new List<(int, float)>());
            var z = ZelleVon(p);
            if (!_gitter.TryGetValue(z, out var l)) _gitter[z] = l = new List<int>();
            l.Add(id);
            return id;
        }

        static void Verbinden(int a, int b)
        {
            float d = (_punkte[a] - _punkte[b]).magnitude;
            _kanten[a].Add((b, d));
            _kanten[b].Add((a, d));
        }

        static (int, int) ZelleVon(Vector3 p) => (Mathf.FloorToInt(p.x / Zelle), Mathf.FloorToInt(p.z / Zelle));

        static IEnumerable<int> Nachbarn(Vector3 p, float radius)
        {
            var (zx, zz) = ZelleVon(p);
            int r = Mathf.CeilToInt(radius / Zelle);
            for (int x = zx - r; x <= zx + r; x++)
                for (int z = zz - r; z <= zz + r; z++)
                    if (_gitter.TryGetValue((x, z), out var l))
                        foreach (int id in l)
                            if (Flach(_punkte[id] - p) <= radius) yield return id;
        }

        static int Naechster(Vector3 p, out float abstand)
        {
            int beste = -1; abstand = float.MaxValue;
            for (int i = 0; i < _punkte.Count; i++)
            {
                float d = Flach(_punkte[i] - p);
                if (d < abstand) { abstand = d; beste = i; }
            }
            return beste;
        }

        static float Dijkstra(int start, int ziel)
        {
            var dist = new float[_punkte.Count];
            for (int i = 0; i < dist.Length; i++) dist[i] = float.PositiveInfinity;
            dist[start] = 0;
            var offen = new PriorityQueue<int, float>();
            offen.Enqueue(start, 0);
            while (offen.TryDequeue(out int u, out float du))
            {
                if (u == ziel) return du;
                if (du > dist[u]) continue;
                foreach (var (v, k) in _kanten[u])
                {
                    float nd = du + k;
                    if (nd < dist[v]) { dist[v] = nd; offen.Enqueue(v, nd); }
                }
            }
            return float.PositiveInfinity;
        }

        static float Flach(Vector3 d) { d.y = 0; return d.magnitude; }

        static string Pfad(Transform t)
        {
            string p = t.name;
            for (var x = t.parent; x != null; x = x.parent) p = x.name + "/" + p;
            return p;
        }
    }
}
