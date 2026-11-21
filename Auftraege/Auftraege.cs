// Transportaufträge: Logik und Protokoll.
//
// Ablauf:
//  1. Frachtauftrag beim Mechaniker annehmen.
//  2. Anreise zur Start-Hütte (fahren oder per Karte schnellreisen).
//  3. An der Hütte anhalten -> Fracht wird geladen, der Auftrag startet (Zeitlimit läuft).
//  4. Zum Ziel fahren (nie eine Hütte). Harte Stöße und Überschläge beschädigen die Fracht,
//     empfindliche Fracht stärker. Schnellreise nach dem Start oder zerstörte Fracht -> ausgeschieden.
//  5. Am Ziel anhalten -> Fracht wird abgeladen, Lohn abhängig vom Zustand der Fracht.
//
// Multiplayer – Konvoi: Ein Spieler nimmt den Auftrag an, alle bekommen ihn angezeigt.
// Mitfahrer ist, wer an der Hütte seine Fracht lädt (auch nachträglich, solange der Auftrag läuft).
// Jeder transportiert seine eigene Ladung mit eigenem Zustand und Lohn; Schnellreise/Aussteigen
// betrifft nur ihn selbst. Gruppenbonus für jeden weiteren Mitfahrer, der abliefert.
// Der Leiter (Host bzw. Einzelspieler) verwaltet Angebote und Auftrag und schickt nach jeder
// Änderung den kompletten Zustand an alle. Jeder Spieler überwacht seine Ladung selbst,
// schreibt sich den Lohn gut und meldet Ereignisse an den Leiter.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MelonLoader;
using UnityEngine;

namespace Auftraege
{
    internal class Fracht
    {
        public string Name;
        public float Empfindlichkeit;   // Schadensfaktor
        public float Lohnfaktor;
        public float Zeitfaktor;        // < 1 = eilig

        public static readonly Fracht[] Katalog =
        {
            new() { Name = "Brennholz",            Empfindlichkeit = 0.4f, Lohnfaktor = 0.8f,  Zeitfaktor = 1.0f },
            new() { Name = "Werkzeugkisten",       Empfindlichkeit = 0.5f, Lohnfaktor = 0.9f,  Zeitfaktor = 1.0f },
            new() { Name = "Lebensmittelvorräte",  Empfindlichkeit = 0.8f, Lohnfaktor = 1.0f,  Zeitfaktor = 1.0f },
            new() { Name = "Post und Pakete",      Empfindlichkeit = 0.8f, Lohnfaktor = 1.0f,  Zeitfaktor = 0.85f },
            new() { Name = "Angelausrüstung",      Empfindlichkeit = 0.8f, Lohnfaktor = 1.0f,  Zeitfaktor = 1.0f },
            new() { Name = "Ersatzteile",          Empfindlichkeit = 0.7f, Lohnfaktor = 1.1f,  Zeitfaktor = 0.9f },
            new() { Name = "Honiggläser",          Empfindlichkeit = 1.4f, Lohnfaktor = 1.25f, Zeitfaktor = 1.0f },
            new() { Name = "Kameraausrüstung",     Empfindlichkeit = 1.5f, Lohnfaktor = 1.3f,  Zeitfaktor = 1.0f },
            new() { Name = "Medikamente",          Empfindlichkeit = 1.2f, Lohnfaktor = 1.35f, Zeitfaktor = 0.75f },
            new() { Name = "Einmachgläser",        Empfindlichkeit = 1.8f, Lohnfaktor = 1.4f,  Zeitfaktor = 1.0f },
        };

        public static Fracht Nach(string name) => Katalog.FirstOrDefault(f => f.Name == name) ?? Katalog[2];

        public string Beschreibung => Empfindlichkeit switch
        {
            >= 1.4f => "sehr empfindlich",
            >= 1.0f => "empfindlich",
            >= 0.7f => "normal",
            _ => "robust",
        };
    }

    internal class Angebot
    {
        public int Id;
        public string Fracht;
        public string Start;
        public Vector3 StartPos;
        public string Ziel;
        public Vector3 ZielPos;
        public float DistanzM;   // Luftlinie Hütte -> Ziel
        public bool Weg;         // über das Wegenetz verbunden
        public float StreckeM;   // Weglänge (bei Weg) bzw. Luftlinie (querfeldein)
        public int Lohn;         // bei 100 % Zustand
        public int ZeitS;
        public bool Eilig;
    }

    internal enum Phase : byte { Anreise = 0, Laeuft = 1, Ende = 2 }

    internal enum Ergebnis : byte { Offen = 0, Geschafft = 1, Abgelaufen = 2, Abgebrochen = 3, Gescheitert = 4 }

    internal enum Nachricht : byte
    {
        Zustand = 1, Hallo = 2, Annehmen = 3, AmStart = 4, Abgeliefert = 5, Schnellreise = 6,
        Abbrechen = 7, NeueAngebote = 8, Zerstoert = 9
    }

    internal class Auftragsverwaltung
    {
        // --- Einstellungen ---
        public float MinDistanzM = 800f, MaxDistanzM = 5000f;
        // Entspricht der Größe der Bodenringe (Mod.cs)
        public float StartRadiusM = 25f, ZielRadiusM = 20f;
        public float LadezeitS = 3f;            // Stillstand an Hütte/Ziel zum Laden/Abladen
        public float StillstandMps = 1.0f;
        public int Grundbetrag = 3;
        public float PebblesProWegKm = 3.5f;    // pro km Strecke über Wege
        public float PebblesProLuftKm = 7f;     // pro km Luftlinie querfeldein (anstrengender)
        // Abrechnung des letzten Auftrags (für die Anzeige)
        public string AbrechnungTitel;
        public readonly List<(string text, int betrag)> AbrechnungPosten = new();
        public float AbrechnungSeit = -1f;
        public string AbrechnungFusszeile;

        public float Gruppenbonus = 0.15f;      // pro zusätzlichem Spieler, der abliefert
        public float MindestLohnAnteil = 0.2f;  // auch bei fast kaputter Fracht
        const float AnzeigeNachEndeS = 8f;
        const float SchonfristNachStartS = 4f;

        // Schaden: Fracht leidet mit dem Auto. 10 % Autoschaden = 15 % Frachtschaden × Empfindlichkeit
        const float FrachtJeAutoschaden = 1.5f;
        const float UeberschlagSchaden = 30f;   // % (zusätzlich, × Empfindlichkeit)
        const float SchnellreiseMps = 150f;     // schneller „fährt“ niemand

        // --- Gemeinsamer Zustand (beim Leiter maßgeblich, sonst Kopie) ---
        public List<Angebot> Angebote = new();
        public Angebot Aktiv;
        public Phase Phase;
        public Ergebnis Ergebnis;
        public float AblaufZeit;
        public HashSet<int> Teilnehmer = new();  // haben Fracht geladen
        public HashSet<int> Abgeliefert = new();
        public HashSet<int> Raus = new();

        // --- Lokal ---
        public float Ladung = 100f;             // Zustand der eigenen Fracht in %
        public float LadeFortschritt;           // 0..1 beim Laden/Abladen
        public float Tempo;                     // aktuelle Geschwindigkeit m/s
        readonly HashSet<int> _bezahlt = new(), _bonusBezahlt = new();
        int _startGemeldetFuer;
        float _lokalerStart = -1f, _endeSeit = -1f, _stillSeit = -1f, _kopfueberSeit = -1f;
        int _halloFuer = -1;
        bool _keineZieleGemeldet, _preiseGeloggt, _kopfueberGezaehlt;
        float _naechsterAngebotsVersuch;
        Vector3 _letztePos; float _letzteZeit = -1f;
        float _letzterAutoschaden = -1f, _schadenSammler;
        float _gefahrenM;                       // Statistik für die Zeitabstimmung
        bool _diagnoseGemacht;
        bool _warMitfahrer;
        readonly System.Random _rnd = new();

        public Action<string> Meldung;

        public float RestS => Aktiv == null || Phase != Phase.Laeuft ? 0 : Mathf.Max(0, AblaufZeit - Time.realtimeSinceStartup);
        public bool BinDraussen(int p) => Abgeliefert.Contains(p) || Raus.Contains(p);
        public bool FahreMit => Aktiv != null && Teilnehmer.Contains(Netz.LokalerSpieler);
        public Fracht AktiveFracht => Aktiv == null ? null : Fracht.Nach(Aktiv.Fracht);

        public Auftragsverwaltung() { Netz.Empfangen = Verarbeiten; }

        public void SitzungZurueckgesetzt()
        {
            Angebote.Clear(); Aktiv = null; Teilnehmer.Clear(); Abgeliefert.Clear(); Raus.Clear();
            Phase = Phase.Anreise; Ergebnis = Ergebnis.Offen;
            _endeSeit = -1f; _halloFuer = -1; _lokalerStart = -1f; _startGemeldetFuer = 0; _letzteZeit = -1f;
            _naechsterAngebotsVersuch = Time.realtimeSinceStartup + 5f; _keineZieleGemeldet = false;
            Spiel.CacheLeeren();
        }

        // ================= Aktionen =================

        public void Annehmen(int id) => Netz.AnLeiter(Paket(Nachricht.Annehmen, w => w.Write(id)));
        public void Abbrechen() => Netz.AnLeiter(Paket(Nachricht.Abbrechen, w => w.Write(Aktiv?.Id ?? 0)));
        public void AngeboteErneuern() => Netz.AnLeiter(Paket(Nachricht.NeueAngebote, _ => { }));

        // ================= Ablauf pro Frame =================

        public void Tick()
        {
            // Hallo an den Host, sobald (wieder) eine Verbindung zu ihm besteht -> er schickt den Stand
            if (!Netz.IstLeiter && Netz.LeiterVerbindungen > 0 && _halloFuer != Netz.LeiterVerbindungen)
            {
                _halloFuer = Netz.LeiterVerbindungen;
                Netz.AnLeiter(Paket(Nachricht.Hallo, _ => { }));
            }

            Sensor();
            EigenePositionPruefen();

            if (!Netz.IstLeiter) return;

            if (Aktiv == null)
            {
                if (Angebote.Count == 0 && Time.realtimeSinceStartup >= _naechsterAngebotsVersuch)
                {
                    _naechsterAngebotsVersuch = Time.realtimeSinceStartup + 15f;
                    AngeboteErzeugen(true);
                }
                return;
            }

            if (Phase == Phase.Laeuft)
            {
                // Nur Mitfahrer zählen, die noch in der Sitzung sind
                var da = Netz.AlleSpieler();
                var aktiveMitfahrer = Teilnehmer.Where(da.Contains).ToList();
                if (aktiveMitfahrer.All(BinDraussen))
                    Beenden(Abgeliefert.Count > 0 ? Ergebnis.Geschafft : Ergebnis.Gescheitert);
                else if (RestS <= 0)
                    Beenden(Abgeliefert.Count > 0 ? Ergebnis.Geschafft : Ergebnis.Abgelaufen);
            }
            else if (Phase == Phase.Ende && _endeSeit >= 0 && Time.realtimeSinceStartup - _endeSeit > AnzeigeNachEndeS)
            {
                Aktiv = null; Teilnehmer.Clear(); Abgeliefert.Clear(); Raus.Clear(); Phase = Phase.Anreise; Ergebnis = Ergebnis.Offen; _endeSeit = -1f;
                AngeboteErzeugen(true);
            }
        }

        /// Geschwindigkeit, Stöße, Überschläge und Schnellreisen erkennen (alle 0,1 s).
        void Sensor()
        {
            if (!Spiel.EigenePosition(out var pos, out var rot)) return;
            float t = Time.realtimeSinceStartup;
            if (_letzteZeit < 0 || t - _letzteZeit > 2f) { _letztePos = pos; _letzteZeit = t; return; }
            float dt = t - _letzteZeit;
            if (dt < 0.1f) return;

            var v = (pos - _letztePos) / dt;
            _letztePos = pos; _letzteZeit = t;

            if (v.magnitude > SchnellreiseMps)
            {
                // Positionssprung: Schnellreise (oder Laden einer neuen Position)
                Tempo = 0;
                _letzterAutoschaden = -1f;
                Spiel.FahrzeugVergessen();
                SchnellreiseErkannt((v * dt).magnitude);
                return;
            }
            Tempo = v.magnitude;

            // Autoschaden verfolgen (Reparaturen/Heilung senken ihn – das zählt nicht)
            if (Spiel.Fahrzeugschaden(out float schaden, out float max))
            {
                if (_letzterAutoschaden >= 0 && schaden > _letzterAutoschaden && FrachtUnterwegs())
                    _schadenSammler += (schaden - _letzterAutoschaden) / max * 100f * FrachtJeAutoschaden;
                _letzterAutoschaden = schaden;
            }

            if (!FrachtUnterwegs()) { _schadenSammler = 0; return; }
            _gefahrenM += v.magnitude * dt;

            if (_schadenSammler * AktiveFracht.Empfindlichkeit >= 1f)
            {
                Beschaedigen(_schadenSammler, "Aufprall");
                _schadenSammler = 0;
            }

            // Überschlag: Dach zeigt länger als 1,5 s nach unten
            bool kopfueber = Vector3.Dot(rot * Vector3.up, Vector3.up) < 0.1f;
            if (!kopfueber) { _kopfueberSeit = -1f; _kopfueberGezaehlt = false; }
            else if (_kopfueberSeit < 0) _kopfueberSeit = t;
            else if (!_kopfueberGezaehlt && t - _kopfueberSeit > 1.5f)
            {
                _kopfueberGezaehlt = true;
                Beschaedigen(UeberschlagSchaden, "Überschlag");
            }
        }

        bool FrachtUnterwegs() => FahreMit && Phase == Phase.Laeuft && !BinDraussen(Netz.LokalerSpieler)
                                  && Time.realtimeSinceStartup - _lokalerStart > SchonfristNachStartS;

        void Beschaedigen(float prozent, string grund)
        {
            float schaden = prozent * AktiveFracht.Empfindlichkeit;
            if (schaden < 1f) return;
            Ladung = Mathf.Max(0, Ladung - schaden);
            MelonLogger.Msg($"[Aufträge] {grund}: Fracht -{schaden:F0} % -> {Ladung:F0} %");
            if (Ladung <= 0)
            {
                Meldung?.Invoke($"{grund} – die Fracht ist zerstört!");
                Raus.Add(Netz.LokalerSpieler);
                Netz.AnLeiter(Paket(Nachricht.Zerstoert, w => w.Write(Aktiv.Id)));
            }
            else Meldung?.Invoke($"{grund} – Fracht beschädigt ({Ladung:F0} %)");
        }

        void SchnellreiseErkannt(float sprungM)
        {
            if (!FrachtUnterwegs()) return;
            MelonLogger.Msg($"[Aufträge] Schnellreise erkannt ({sprungM:F0} m Sprung)");
            Meldung?.Invoke("Schnellreise – du bist aus dem Auftrag ausgeschieden");
            Raus.Add(Netz.LokalerSpieler);
            Netz.AnLeiter(Paket(Nachricht.Schnellreise, w => w.Write(Aktiv.Id)));
        }

        void EigenePositionPruefen()
        {
            LadeFortschritt = 0;
            if (Aktiv == null || !Spiel.EigenePosition(out var pos, out _)) return;
            float t = Time.realtimeSinceStartup;
            bool still = Tempo < StillstandMps;

            int lokal = Netz.LokalerSpieler;
            bool amStart = (Phase == Phase.Anreise || Phase == Phase.Laeuft) && !FahreMit && _startGemeldetFuer != Aktiv.Id
                           && Flach(pos - Aktiv.StartPos) <= StartRadiusM;
            bool amZiel = Phase == Phase.Laeuft && FahreMit && !BinDraussen(lokal) && !_bezahlt.Contains(Aktiv.Id)
                          && Flach(pos - Aktiv.ZielPos) <= ZielRadiusM;

            if (!(amStart || amZiel) || !still) { _stillSeit = -1f; return; }
            if (_stillSeit < 0) _stillSeit = t;
            LadeFortschritt = Mathf.Clamp01((t - _stillSeit) / LadezeitS);
            if (LadeFortschritt < 1f) return;
            _stillSeit = -1f;

            if (amStart)
            {
                _startGemeldetFuer = Aktiv.Id;
                Netz.AnLeiter(Paket(Nachricht.AmStart, w => w.Write(Aktiv.Id)));
                return;
            }

            _bezahlt.Add(Aktiv.Id);
            Abgeliefert.Add(lokal);
            int lohn = Mathf.Max(1, Mathf.RoundToInt(Aktiv.Lohn * Mathf.Max(MindestLohnAnteil, Ladung / 100f)));
            float fahrzeit = Time.realtimeSinceStartup - _lokalerStart;
            MelonLogger.Msg($"[Aufträge] Statistik ({(Aktiv.Weg ? "Weg" : "querfeldein")}): Luftlinie {Aktiv.DistanzM:F0} m, Route {Aktiv.StreckeM:F0} m, gefahren {_gefahrenM:F0} m, " +
                            $"Fahrzeit {fahrzeit:F0} s von {Aktiv.ZeitS} s, Schnitt {_gefahrenM / Mathf.Max(1, fahrzeit) * 3.6f:F1} km/h");
            if (Spiel.PebblesGutschreiben(lohn))
            {
                Meldung?.Invoke($"{Aktiv.Fracht} abgeliefert ({Ladung:F0} %) – +{lohn} Pebbles");
                AbrechnungErstellen(lohn, fahrzeit);
            }
            Netz.AnLeiter(Paket(Nachricht.Abgeliefert, w => w.Write(Aktiv.Id)));
        }

        void AbrechnungErstellen(int lohn, float fahrzeit)
        {
            var a = Aktiv;
            var f = Fracht.Nach(a.Fracht);
            int basis = Mathf.RoundToInt(a.Weg ? Grundbetrag + a.StreckeM / 1000f * PebblesProWegKm
                                               : Grundbetrag + a.DistanzM / 1000f * PebblesProLuftKm);
            basis = Mathf.Clamp(basis, 1, a.Lohn);
            AbrechnungPosten.Clear();
            AbrechnungTitel = $"Abrechnung: {a.Fracht}";
            AbrechnungPosten.Add((a.Weg ? $"Transport {a.StreckeM / 1000f:F1} km über Wege" : $"Transport {a.DistanzM / 1000f:F1} km querfeldein", basis));
            if (a.Lohn - basis != 0)
                AbrechnungPosten.Add(($"Frachtzuschlag ({(f.Empfindlichkeit >= 1.2f ? "empfindlich" : a.Eilig ? "eilig" : "Fracht")}, ×{f.Lohnfaktor:0.##})", a.Lohn - basis));
            if (lohn - a.Lohn != 0)
                AbrechnungPosten.Add((Ladung / 100f < MindestLohnAnteil ? $"Frachtschaden ({Ladung:F0} % heil, Mindestlohn)" : $"Frachtschaden ({Ladung:F0} % heil)", lohn - a.Lohn));
            AbrechnungFusszeile = $"Fahrzeit {Uhr(fahrzeit)} von {Uhr(a.ZeitS)} · {_gefahrenM / 1000f:F1} km gefahren";
            AbrechnungSeit = Time.realtimeSinceStartup;
        }

        static string Uhr(float s) { int t = Mathf.Max(0, Mathf.RoundToInt(s)); return $"{t / 60}:{t % 60:00}"; }

        static float Flach(Vector3 d) { d.y = 0; return d.magnitude; }

        // ================= Leiter-Logik =================

        void AngeboteErzeugen(bool senden)
        {
            if (!_preiseGeloggt) { _preiseGeloggt = Spiel.PreiseLoggen(); }
            if (!_diagnoseGemacht && Spiel.AlleOrte().Count > 0) { _diagnoseGemacht = true; Spiel.Diagnose(); }
            if (!Wegenetz.Bereit && Spiel.AlleOrte().Count > 0) Wegenetz.Aufbauen(Spiel.AlleOrte());

            Angebote.Clear();
            var huetten = Spiel.Huetten().ToList();
            var erreicht = huetten.Where(h => h.Erreicht).ToList();
            if (erreicht.Count > 0) huetten = erreicht; // nur Hütten, zu denen man schnellreisen kann
            var ziele = Spiel.Ziele().ToList();

            var kandidaten = new List<(Ort h, Ort z, float d, Route r)>();
            foreach (var h in huetten)
                foreach (var z in ziele)
                {
                    float d = Flach(z.Position - h.Position);
                    if (d >= MinDistanzM && d <= MaxDistanzM) kandidaten.Add((h, z, d, Wegenetz.Berechnen(h.Position, z.Position)));
                }
            kandidaten = kandidaten.OrderBy(_ => _rnd.Next()).ToList();
            var mitWeg = kandidaten.Where(k => k.r.Verbunden).ToList();
            var ohneWeg = kandidaten.Where(k => !k.r.Verbunden).ToList();

            // Bevorzugt zwei Aufträge über Wege (kurz/lang) und einen querfeldein
            var auswahl = new List<(Ort h, Ort z, float d, Route r)>();
            // Kein Ort darf in den Angeboten doppelt vorkommen – weder als Start noch als Ziel
            static bool GleicherOrt(Ort a, Ort b) => a.Name == b.Name || Flach(a.Position - b.Position) < 60f;
            bool Frei((Ort h, Ort z, float d, Route r) x) =>
                auswahl.All(s => !GleicherOrt(s.h, x.h) && !GleicherOrt(s.h, x.z) && !GleicherOrt(s.z, x.h) && !GleicherOrt(s.z, x.z));
            void Nimm(IEnumerable<(Ort h, Ort z, float d, Route r)> quelle)
            {
                var k = quelle.Where(Frei).FirstOrDefault();
                if (k.z.Name != null) auswahl.Add(k);
            }
            float mitte = (MinDistanzM + MaxDistanzM) / 2f;
            Nimm(mitWeg.Where(x => x.d < mitte));
            Nimm(mitWeg.Where(x => x.d >= mitte));
            Nimm(ohneWeg);
            foreach (var k in mitWeg.Concat(ohneWeg))
            {
                if (auswahl.Count >= 3) break;
                if (Frei(k)) auswahl.Add(k);
            }

            var frachten = Fracht.Katalog.OrderBy(_ => _rnd.Next()).ToList();
            int fi = 0;
            foreach (var (h, z, d, r) in auswahl.OrderBy(x => x.d))
            {
                var f = frachten[fi++ % frachten.Count];
                float zeit, lohn, strecke;
                if (r.Verbunden)
                {
                    // Gemessen: ~19 km/h im Schnitt. Wege mit 16 km/h, Gelände zum Weg mit 6 km/h, +30 % Puffer
                    strecke = r.WegM + r.GelaendeM;
                    zeit = (180f + (r.WegM / 16000f + r.GelaendeM / 6000f) * 3600f) * 1.3f;
                    lohn = Grundbetrag + strecke / 1000f * PebblesProWegKm;
                }
                else
                {
                    // Querfeldein: effektiv nur ~4 km/h Luftlinie
                    strecke = d;
                    zeit = 180f + d / 1000f * 900f;
                    lohn = Grundbetrag + d / 1000f * PebblesProLuftKm;
                }
                Angebote.Add(new Angebot
                {
                    Id = _rnd.Next(1, int.MaxValue),
                    Fracht = f.Name,
                    Start = h.Name, StartPos = h.Position,
                    Ziel = z.Name, ZielPos = z.Position,
                    DistanzM = d, Weg = r.Verbunden, StreckeM = strecke,
                    Lohn = Mathf.Max(2, Mathf.RoundToInt(lohn * f.Lohnfaktor)),
                    ZeitS = Mathf.RoundToInt(zeit * f.Zeitfaktor / 30f) * 30,
                    Eilig = f.Zeitfaktor < 0.95f,
                });
            }

            if (Angebote.Count == 0)
            {
                if (!_keineZieleGemeldet)
                    MelonLogger.Msg($"[Aufträge] Keine Angebote möglich ({huetten.Count} Hütten, {ziele.Count} Ziele).");
                _keineZieleGemeldet = true;
            }
            else
            {
                _keineZieleGemeldet = false;
                MelonLogger.Msg("[Aufträge] Angebote: " + string.Join(" | ",
                    Angebote.Select(x => $"{x.Fracht}: {x.Start} -> {x.Ziel} {(x.Weg ? "Weg" : "querfeldein")} {x.StreckeM:F0} m (Luftlinie {x.DistanzM:F0}) {x.ZeitS} s {x.Lohn} P")));
            }
            if (senden) ZustandSenden();
        }

        void Beenden(Ergebnis e)
        {
            Phase = Phase.Ende;
            Ergebnis = e;
            _endeSeit = Time.realtimeSinceStartup;
            ZustandSenden();
            ErgebnisLokalAuswerten();
        }

        void ZustandSenden() => Netz.AnAlle(ZustandPaket());

        byte[] ZustandPaket() => Paket(Nachricht.Zustand, w =>
        {
            w.Write((byte)Angebote.Count);
            foreach (var a in Angebote) AngebotSchreiben(w, a);
            w.Write(Aktiv != null);
            if (Aktiv == null) return;
            AngebotSchreiben(w, Aktiv);
            w.Write((byte)Phase); w.Write((byte)Ergebnis); w.Write(RestS);
            w.Write((byte)Teilnehmer.Count); foreach (int p in Teilnehmer) w.Write(p);
            w.Write((byte)Abgeliefert.Count); foreach (int p in Abgeliefert) w.Write(p);
            w.Write((byte)Raus.Count); foreach (int p in Raus) w.Write(p);
        });

        // ================= Nachrichten =================

        void Verarbeiten(int absender, byte[] daten)
        {
            try
            {
                using var r = new BinaryReader(new MemoryStream(daten));
                var typ = (Nachricht)r.ReadByte();

                if (typ == Nachricht.Zustand)
                {
                    if (!Netz.IstLeiter) ZustandLesen(r);
                    return;
                }
                if (!Netz.IstLeiter) return;

                int id = typ is Nachricht.Hallo or Nachricht.NeueAngebote ? 0 : r.ReadInt32();
                bool passt = Aktiv != null && Aktiv.Id == id;
                bool fremd = absender != Netz.LokalerSpieler;
                string wer = Netz.SpielerName(absender);

                switch (typ)
                {
                    case Nachricht.Annehmen:
                    {
                        if (Aktiv != null) break;
                        var a = Angebote.FirstOrDefault(x => x.Id == id);
                        if (a == null) break;
                        Aktiv = a; Angebote.Clear(); Teilnehmer.Clear(); Abgeliefert.Clear(); Raus.Clear();
                        Phase = Phase.Anreise; Ergebnis = Ergebnis.Offen; _endeSeit = -1f;
                        Meldung?.Invoke(fremd ? $"{wer} hat einen Konvoi-Auftrag angenommen: {a.Fracht}, abholen bei {a.Start}"
                                              : $"Auftrag angenommen: {a.Fracht} – abholen bei {a.Start}");
                        MelonLogger.Msg($"[Aufträge] {wer} hat angenommen: {a.Fracht}, {a.Start} -> {a.Ziel} ({a.DistanzM:F0} m, {a.Lohn} P)");
                        break;
                    }
                    case Nachricht.AmStart:
                        if (!passt || Phase == Phase.Ende || !Teilnehmer.Add(absender)) break;
                        if (Phase == Phase.Anreise)
                        {
                            Phase = Phase.Laeuft;
                            AblaufZeit = Time.realtimeSinceStartup + Aktiv.ZeitS;
                        }
                        if (fremd) Meldung?.Invoke($"{wer} hat {Aktiv.Fracht} geladen" + (FahreMit ? "" : $" – du kannst bei {Aktiv.Start} noch zuladen"));
                        else LokalGestartet();
                        MelonLogger.Msg($"[Aufträge] Fracht geladen durch {wer} ({Teilnehmer.Count} im Konvoi)");
                        break;
                    case Nachricht.Abgeliefert:
                        if (!passt || Phase != Phase.Laeuft) break;
                        if (Abgeliefert.Add(absender) && fremd) Meldung?.Invoke($"{wer} hat abgeliefert");
                        break;
                    case Nachricht.Schnellreise:
                        if (!passt || Phase != Phase.Laeuft) break;
                        if (Raus.Add(absender) && fremd) Meldung?.Invoke($"{wer} ist schnellgereist und ausgeschieden");
                        break;
                    case Nachricht.Zerstoert:
                        if (!passt || Phase != Phase.Laeuft) break;
                        if (Raus.Add(absender) && fremd) Meldung?.Invoke($"{wer} hat die Fracht zerstört");
                        break;
                    case Nachricht.Abbrechen:
                        if (!passt || Phase == Phase.Ende) break;
                        if (Phase == Phase.Laeuft && Teilnehmer.Count > 0)
                        {
                            // Während der Fahrt: nur der Spieler selbst steigt aus
                            if (Teilnehmer.Contains(absender)) Raus.Add(absender);
                            if (fremd) Meldung?.Invoke($"{wer} ist aus dem Konvoi ausgestiegen");
                            break;
                        }
                        if (fremd) Meldung?.Invoke($"{wer} hat den Auftrag abgebrochen");
                        Beenden(Ergebnis.Abgebrochen);
                        return;
                    case Nachricht.NeueAngebote:
                        if (Aktiv == null) AngeboteErzeugen(false);
                        break;
                }
                ZustandSenden();
            }
            catch (Exception e) { MelonLogger.Warning("[Aufträge] Ungültige Nachricht: " + e.Message); }
        }

        void LokalGestartet()
        {
            _lokalerStart = Time.realtimeSinceStartup;
            Ladung = 100f; _gefahrenM = 0; _schadenSammler = 0;
            Meldung?.Invoke($"{Aktiv.Fracht} geladen – ab nach {Aktiv.Ziel}!");
        }

        void ZustandLesen(BinaryReader r)
        {
            int vorherId = Aktiv?.Id ?? 0;
            var vorherPhase = Phase;

            Angebote.Clear();
            int n = r.ReadByte();
            for (int i = 0; i < n; i++) Angebote.Add(AngebotLesen(r));
            if (r.ReadBoolean())
            {
                Aktiv = AngebotLesen(r);
                Phase = (Phase)r.ReadByte(); Ergebnis = (Ergebnis)r.ReadByte();
                AblaufZeit = Time.realtimeSinceStartup + r.ReadSingle();
                Teilnehmer.Clear(); int m = r.ReadByte(); for (int i = 0; i < m; i++) Teilnehmer.Add(r.ReadInt32());
                Abgeliefert.Clear(); m = r.ReadByte(); for (int i = 0; i < m; i++) Abgeliefert.Add(r.ReadInt32());
                Raus.Clear(); m = r.ReadByte(); for (int i = 0; i < m; i++) Raus.Add(r.ReadInt32());
            }
            else { Aktiv = null; Teilnehmer.Clear(); Abgeliefert.Clear(); Raus.Clear(); Phase = Phase.Anreise; Ergebnis = Ergebnis.Offen; }

            if (Aktiv == null) { _warMitfahrer = false; return; }
            bool neu = Aktiv.Id != vorherId;
            if (neu) _warMitfahrer = false;
            if (neu && Phase == Phase.Anreise) Meldung?.Invoke($"Konvoi-Auftrag: {Aktiv.Fracht} – abholen bei {Aktiv.Start}");
            if (FahreMit && !_warMitfahrer) { _warMitfahrer = true; LokalGestartet(); }
            if (Phase == Phase.Ende && (neu || vorherPhase != Phase.Ende)) ErgebnisLokalAuswerten();
        }

        void ErgebnisLokalAuswerten()
        {
            if (Aktiv == null) return;
            int lokal = Netz.LokalerSpieler;
            if (!Teilnehmer.Contains(lokal) && Ergebnis != Ergebnis.Abgebrochen)
            {
                Meldung?.Invoke(Ergebnis == Ergebnis.Geschafft ? $"Konvoi erledigt ({Abgeliefert.Count} abgeliefert)" : "Konvoi-Auftrag beendet");
                return;
            }
            switch (Ergebnis)
            {
                case Ergebnis.Geschafft:
                    int weitere = Abgeliefert.Count - 1;
                    if (weitere > 0 && Abgeliefert.Contains(lokal) && _bonusBezahlt.Add(Aktiv.Id))
                    {
                        int bonus = Mathf.Max(1, Mathf.RoundToInt(Aktiv.Lohn * Gruppenbonus * weitere));
                        if (Spiel.PebblesGutschreiben(bonus))
                        {
                            Meldung?.Invoke($"Auftrag erledigt! Gruppenbonus +{bonus} Pebbles");
                            AbrechnungPosten.Add(($"Gruppenbonus ({weitere} {(weitere == 1 ? "Mitfahrer" : "Mitfahrer")} × {Gruppenbonus * 100:F0} %)", bonus));
                            AbrechnungSeit = Time.realtimeSinceStartup;
                        }
                    }
                    else if (!Abgeliefert.Contains(lokal)) Meldung?.Invoke("Auftrag beendet – deine Fracht kam nicht an");
                    break;
                case Ergebnis.Abgelaufen: Meldung?.Invoke("Zeit abgelaufen – Auftrag gescheitert"); break;
                case Ergebnis.Gescheitert: Meldung?.Invoke("Auftrag gescheitert – keine Fracht ist angekommen"); break;
                case Ergebnis.Abgebrochen: Meldung?.Invoke("Auftrag abgebrochen"); break;
            }
        }

        // ================= Serialisierung =================

        static byte[] Paket(Nachricht typ, Action<BinaryWriter> inhalt)
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms)) { w.Write((byte)typ); inhalt(w); }
            return ms.ToArray();
        }

        static void V3(BinaryWriter w, Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
        static Vector3 V3(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        static void AngebotSchreiben(BinaryWriter w, Angebot a)
        {
            w.Write(a.Id); w.Write(a.Fracht ?? "");
            w.Write(a.Start ?? ""); V3(w, a.StartPos);
            w.Write(a.Ziel ?? ""); V3(w, a.ZielPos);
            w.Write(a.DistanzM); w.Write(a.Weg); w.Write(a.StreckeM); w.Write(a.Lohn); w.Write(a.ZeitS); w.Write(a.Eilig);
        }

        static Angebot AngebotLesen(BinaryReader r) => new()
        {
            Id = r.ReadInt32(), Fracht = r.ReadString(),
            Start = r.ReadString(), StartPos = V3(r),
            Ziel = r.ReadString(), ZielPos = V3(r),
            DistanzM = r.ReadSingle(), Weg = r.ReadBoolean(), StreckeM = r.ReadSingle(),
            Lohn = r.ReadInt32(), ZeitS = r.ReadInt32(), Eilig = r.ReadBoolean(),
        };
    }
}
