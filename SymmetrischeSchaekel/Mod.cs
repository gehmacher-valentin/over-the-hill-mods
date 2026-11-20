// Symmetrische Schäkel – Over the Hill (MelonLoader 0.7.x, IL2CPP)
//
// Das Spiel verwendet für alle vier Schäkel (shackleFL/FR/RL/RR) dasselbe Modell,
// daher zeigt die Verschraubung überall zur selben Seite. Diese Mod spiegelt je einen
// Schäkel vorne und hinten entlang der Fahrzeug-Querachse, sodass die Verschraubung
// immer zur Fahrzeugmitte zeigt. Vorne ist das Modell andersherum eingebaut als hinten
// (hinten um 180° gedreht), daher wird vorne links und hinten rechts gespiegelt
// (im Spiel geprüft am 2026-11-20).
// Zusätzlich wird die rote Schraube grau gefärbt (siehe Umfaerben.cs).
// Rein optisch und nur lokal – Mitspieler sehen weiterhin das Original.

using System;
using System.Collections.Generic;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(SymmetrischeSchaekel.Mod), "Symmetrische Schaekel", "1.0.0", "Valentin Gehmacher")]
[assembly: MelonGame("Funselektor", "over the hill")]

namespace SymmetrischeSchaekel
{
    public class Mod : MelonMod
    {
        // Diese Schäkel werden gespiegelt
        static readonly HashSet<string> ZuSpiegeln = new() { "FL", "RR" };

        // Schraubenfarbe: "grau", "dunkelrot" oder "original" (siehe Umfaerber.Modi)
        const string Schraubenfarbe = "grau";
        Umfaerber _umfaerber;

        // Gespiegelte Transforms: InstanceID -> (Transform, Originalskalierung)
        readonly Dictionary<int, (Transform t, Vector3 orig)> _gespiegelt = new();
        float _naechsterScan;

        public override void OnInitializeMelon()
        {
            _umfaerber = new Umfaerber(LoggerInstance);
            LoggerInstance.Msg("Geladen – Schraubenfarbe " + Schraubenfarbe);
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            _gespiegelt.Clear();
            _umfaerber.SzeneGewechselt();
            _naechsterScan = 0f;
        }

        public override void OnUpdate()
        {
            if (Time.unscaledTime < _naechsterScan) return;
            _naechsterScan = Time.unscaledTime + 1.5f;
            try { Scannen(); }
            catch (Exception e) { LoggerInstance.Warning("Scan-Fehler: " + e.Message); }
        }

        void Scannen()
        {
            // Lokale und vernetzte Fahrzeuge (eigenes Auto + Mitspieler)
            var wurzeln = new List<Transform>();
            foreach (var c in UnityEngine.Object.FindObjectsOfType<LocalVehicleCustomizationController>())
                wurzeln.Add(c.transform);
            foreach (var c in UnityEngine.Object.FindObjectsOfType<VehicleCustomizationController>())
                wurzeln.Add(c.transform);

            var gesehen = new HashSet<int>();
            foreach (var wurzel in wurzeln)
                if (wurzel != null && gesehen.Add(wurzel.GetInstanceID()))
                    FahrzeugBearbeiten(wurzel);
        }

        void FahrzeugBearbeiten(Transform wurzel)
        {
            // Schäkel nach Position sammeln
            var schaekel = new Dictionary<string, List<Transform>>();
            foreach (var t in wurzel.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                if (n.Length != 9 || !n.StartsWith("shackle", StringComparison.OrdinalIgnoreCase)) continue;
                string pos = n.Substring(7).ToUpperInvariant(); // FL, FR, RL, RR
                if (pos != "FL" && pos != "FR" && pos != "RL" && pos != "RR") continue;
                if (!schaekel.TryGetValue(pos, out var liste)) schaekel[pos] = liste = new List<Transform>();
                liste.Add(t);
            }
            if (schaekel.Count == 0) return;

            foreach (var kv in schaekel)
                foreach (var s in kv.Value)
                    try { _umfaerber.Bearbeiten(s, Schraubenfarbe); }
                    catch (Exception e) { LoggerInstance.Warning("Umfärben fehlgeschlagen: " + e.Message); }

            // Querachse des Fahrzeugs: links -> rechts
            Vector3 quer = QuerachseBestimmen(schaekel, "F") ?? QuerachseBestimmen(schaekel, "R") ?? wurzel.right;

            foreach (var kv in schaekel)
                if (ZuSpiegeln.Contains(kv.Key))
                    foreach (var s in kv.Value)
                        Spiegeln(s, quer);
        }

        static Vector3? QuerachseBestimmen(Dictionary<string, List<Transform>> s, string vorneHinten)
        {
            if (s.TryGetValue(vorneHinten + "L", out var l) && s.TryGetValue(vorneHinten + "R", out var r))
            {
                Vector3 d = r[0].position - l[0].position;
                if (d.sqrMagnitude > 1e-4f) return d.normalized;
            }
            return null;
        }

        void Spiegeln(Transform t, Vector3 quer)
        {
            int id = t.GetInstanceID();
            if (_gespiegelt.TryGetValue(id, out var eintrag))
            {
                // Falls das Spiel die Skalierung zurückgesetzt hat: erneut spiegeln
                if (t.localScale != eintrag.orig) return;
            }
            else
            {
                eintrag = (t, t.localScale);
                _gespiegelt[id] = eintrag;
            }

            // Lokale Achse finden, die am besten zur Fahrzeug-Querachse passt
            float dx = Mathf.Abs(Vector3.Dot(t.right, quer));
            float dy = Mathf.Abs(Vector3.Dot(t.up, quer));
            float dz = Mathf.Abs(Vector3.Dot(t.forward, quer));
            Vector3 sk = eintrag.orig;
            if (dx >= dy && dx >= dz) sk.x = -sk.x;
            else if (dy >= dz) sk.y = -sk.y;
            else sk.z = -sk.z;
            t.localScale = sk;
        }
    }
}
