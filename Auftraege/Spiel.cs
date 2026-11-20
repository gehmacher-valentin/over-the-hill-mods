// Zugriff auf Spielfunktionen: Position, Orte, Hütten, Mechaniker, Geld (Pebbles).

using System;
using System.Collections.Generic;
using System.Linq;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace Auftraege
{
    internal struct Ort
    {
        public string Name;
        public Vector3 Position;
        public PointOfInterestType Typ;
        public bool Erreicht;   // vom Spieler schon besucht (bei Hütten: Schnellreise möglich)
        public bool Fundstueck; // Kisten u. ä. – kein sinnvolles Lieferziel
        public string InfoKlasse;
    }

    internal static class Spiel
    {
        // Als Ziel ungeeignet: Hütten (sind Startpunkte), Fundstücke, Portale, Rätsel
        const PointOfInterestType KeinZiel = PointOfInterestType.Cabin | PointOfInterestType.ItemRecovery
                                             | PointOfInterestType.Portal | PointOfInterestType.Puzzle;

        // Teure Suchen in der Szene (FindObjectsOfType) nur selten ausführen
        static LocalVehicleCustomizationController _auto;
        static float _autoSucheAb;
        static List<Ort> _orte = new();
        static float _orteGueltigBis;

        public static void CacheLeeren()
        {
            _auto = null; _autoSucheAb = 0; _orte = new List<Ort>(); _orteGueltigBis = 0;
            FahrzeugVergessen();
            Wegenetz.Zuruecksetzen();
        }

        /// Position und Ausrichtung des eigenen Fahrzeugs.
        public static bool EigenePosition(out Vector3 pos, out Quaternion rot)
        {
            pos = default; rot = Quaternion.identity;
            try
            {
                var o = OverlandNetworkRunner.Instance;
                var r = Netz.Runner;
                if (o != null && r != null && o.GetPlayerPositionAndRotation(r.LocalPlayer, out pos, out rot))
                    return true;
            }
            catch { }
            try
            {
                if (_auto == null && Time.realtimeSinceStartup >= _autoSucheAb)
                {
                    _autoSucheAb = Time.realtimeSinceStartup + 5f;
                    _auto = UnityEngine.Object.FindObjectOfType<LocalVehicleCustomizationController>();
                }
                if (_auto != null) { pos = _auto.transform.position; rot = _auto.transform.rotation; return true; }
            }
            catch { _auto = null; }
            return false;
        }

        /// Alle benannten Orte der aktuellen Karte (zwischengespeichert).
        public static List<Ort> AlleOrte()
        {
            if (Time.realtimeSinceStartup < _orteGueltigBis) return _orte;
            _orte = OrteSuchen();
            // Leere Ergebnisse (z. B. während des Ladens) nur kurz merken
            _orteGueltigBis = Time.realtimeSinceStartup + (_orte.Count > 0 ? 30f : 10f);
            return _orte;
        }

        public static IEnumerable<Ort> Ziele() =>
            AlleOrte().Where(o => (o.Typ & KeinZiel) == 0 && !o.Fundstueck).Concat(Wegenetz.Wegziele_);
        public static IEnumerable<Ort> Huetten() => AlleOrte().Where(o => o.Typ == PointOfInterestType.Cabin);
        public static IEnumerable<Ort> Mechaniker() => AlleOrte().Where(o => o.Typ == PointOfInterestType.Merchant);

        static List<Ort> OrteSuchen()
        {
            var liste = new List<Ort>();
            var gesehen = new HashSet<string>();
            foreach (var poi in UnityEngine.Object.FindObjectsOfType<PointOfInterest>())
            {
                if (poi == null) continue;
                string name = OrtName(poi);
                if (string.IsNullOrWhiteSpace(name)) continue;
                var p = poi.transform.position;
                // Doppelte Namen an fast gleicher Stelle zusammenfassen
                string schluessel = name + "|" + Mathf.RoundToInt(p.x / 50f) + "|" + Mathf.RoundToInt(p.z / 50f);
                if (!gesehen.Add(schluessel)) continue;

                PointOfInterestType typ = PointOfInterestType.Generic;
                try { if (poi.Info != null) typ = poi.Info.Type; } catch { }
                bool erreicht = true;
                try
                {
                    var d = poi.GetData<PointOfInterestData>();
                    if (d != null) erreicht = d.CurrentState == PointOfInterestData.State.Reached;
                }
                catch { }
                string klasse = "";
                try { klasse = poi.Info?.GetIl2CppType()?.Name ?? ""; } catch { }
                bool fund = klasse.Contains("Recovery") || name.IndexOf("crate", StringComparison.OrdinalIgnoreCase) >= 0
                            || name.IndexOf("kiste", StringComparison.OrdinalIgnoreCase) >= 0;
                liste.Add(new Ort { Name = name, Position = p, Typ = typ, Erreicht = erreicht, Fundstueck = fund, InfoKlasse = klasse });
            }
            return liste;
        }

        static string OrtName(PointOfInterest poi)
        {
            try
            {
                var info = poi.Info;
                if (info != null && info.Name != null && !info.Name.IsEmpty)
                {
                    string s = info.Name.GetLocalizedString();
                    if (!string.IsNullOrWhiteSpace(s) && !s.StartsWith("No translation")) return s.Trim();
                }
            }
            catch { }
            try
            {
                string d = poi.GetDataName();
                if (!string.IsNullOrWhiteSpace(d)) return d.Replace('_', ' ').Trim();
            }
            catch { }
            return null;
        }

        // ================= Fahrzeugschaden =================

        static CarDamageSystem _schadenSystem;
        static float _schadenSucheAb;

        /// Schaden des eigenen Autos (0..max). false, wenn nicht ermittelbar.
        public static bool Fahrzeugschaden(out float schaden, out float max)
        {
            schaden = 0; max = 1;
            try
            {
                if (_schadenSystem == null && Time.realtimeSinceStartup >= _schadenSucheAb)
                {
                    _schadenSucheAb = Time.realtimeSinceStartup + 5f;
                    if (EigenePosition(out var p, out _))
                    {
                        float beste = 15f;
                        foreach (var s in UnityEngine.Object.FindObjectsOfType<CarDamageSystem>())
                        {
                            if (s == null) continue;
                            float d = (s.transform.position - p).magnitude;
                            if (d < beste) { beste = d; _schadenSystem = s; }
                        }
                    }
                }
                if (_schadenSystem == null) return false;
                schaden = _schadenSystem.DamageAmount;
                max = Mathf.Max(1f, _schadenSystem.MaxDamageAmount);
                return true;
            }
            catch { _schadenSystem = null; return false; }
        }

        public static void FahrzeugVergessen() { _schadenSystem = null; _schadenSucheAb = 0; }

        // ================= Diagnose =================

        /// Einmalig ins Log: alle Orte mit Typ sowie vorhandene Splines (mögliche Wege).
        public static void Diagnose()
        {
            try
            {
                foreach (var o in AlleOrte().OrderBy(o => o.Typ.ToString()).ThenBy(o => o.Name))
                    MelonLogger.Msg($"[Diagnose] Ort: {o.Name} | {o.Typ} | {o.InfoKlasse} | erreicht={o.Erreicht} | {o.Position:F0}");
            }
            catch (Exception e) { MelonLogger.Warning("[Diagnose] Orte: " + e.Message); }

            try
            {
                var container = UnityEngine.Object.FindObjectsOfType<UnityEngine.Splines.SplineContainer>();
                float gesamt = 0; int knoten = 0;
                var zeilen = new List<(float len, string text)>();
                foreach (var c in container)
                {
                    if (c == null) continue;
                    try
                    {
                        var sp = c.Spline;
                        float len = sp != null ? sp.GetLength() : 0;
                        int k = sp != null ? sp.Count : 0;
                        gesamt += len; knoten += k;
                        string pfad = c.name;
                        for (var t = c.transform.parent; t != null; t = t.parent) pfad = t.name + "/" + pfad;
                        zeilen.Add((len, $"{pfad} | {k} Knoten | {len:F0} m | Pos {c.transform.position:F0}"));
                    }
                    catch { }
                }
                MelonLogger.Msg($"[Diagnose] SplineContainer: {container.Length}, zusammen {gesamt / 1000f:F1} km, {knoten} Knoten");
                foreach (var z in zeilen.OrderByDescending(z => z.len).Take(40)) MelonLogger.Msg("[Diagnose] Spline: " + z.text);
            }
            catch (Exception e) { MelonLogger.Warning("[Diagnose] Splines: " + e.Message); }
        }

        /// Pebbles gutschreiben (lokales Inventar des Spielers).
        public static bool PebblesGutschreiben(int menge)
        {
            if (menge <= 0) return false;
            try
            {
                var m = PlayerInventoryManager.Instance;
                if (m == null || m.Inventory == null) return false;
                m.Inventory.AddItem(m.MoneyItemId, menge, false, false);
                return true;
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Aufträge] Gutschrift fehlgeschlagen: " + e.Message);
                return false;
            }
        }

        public static int PebblesKontostand()
        {
            try
            {
                var m = PlayerInventoryManager.Instance;
                return m?.Inventory?.GetItemsCount(m.MoneyItemId) ?? -1;
            }
            catch { return -1; }
        }

        public static Sprite PebbleIcon()
        {
            try
            {
                var m = PlayerInventoryManager.Instance;
                if (m != null && PlayerInventoryManager.ItemScriptableObjectLookup(m.MoneyItemId, out var so) && so != null)
                    return so.Icon;
            }
            catch { }
            return null;
        }

        /// Preise im Shop ins Log schreiben – zum Abstimmen der Belohnungen.
        public static bool PreiseLoggen()
        {
            try
            {
                var kat = PlayerInventoryManager.InventoryItemsCatalog;
                if (kat?.IdToSO == null) return false;
                var preise = new List<int>();
                foreach (var kv in kat.IdToSO)
                    if (kv.Value != null && kv.Value.Price > 0) preise.Add(kv.Value.Price);
                if (preise.Count == 0) return false;
                preise.Sort();
                MelonLogger.Msg($"[Aufträge] Shop-Preise: {preise.Count} Artikel, min {preise.First()}, " +
                                $"Median {preise[preise.Count / 2]}, max {preise.Last()}");

                // Zum Vergleich: Pebbles, die das Spiel selbst für Orte vergibt
                var belohnungen = new List<int>();
                foreach (var poi in UnityEngine.Object.FindObjectsOfType<PointOfInterest>())
                {
                    try
                    {
                        var info = poi?.Info;
                        if (info == null) continue;
                        int a = info.OneTimeReward != null ? info.OneTimeReward.GetPebblesCount() : 0;
                        int b = info.EveryTimeReward != null ? info.EveryTimeReward.GetPebblesCount() : 0;
                        if (a + b > 0) belohnungen.Add(a + b);
                    }
                    catch { }
                }
                if (belohnungen.Count > 0)
                {
                    belohnungen.Sort();
                    MelonLogger.Msg($"[Aufträge] Pebbles für Orte im Spiel: {belohnungen.Count} Orte, min {belohnungen.First()}, " +
                                    $"Median {belohnungen[belohnungen.Count / 2]}, max {belohnungen.Last()}");
                }
                return true;
            }
            catch (Exception e) { MelonLogger.Warning("[Aufträge] Preise nicht lesbar: " + e.Message); return false; }
        }
    }
}
