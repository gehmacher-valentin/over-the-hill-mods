// Transportaufträge – Over the Hill (MelonLoader 0.7.x, IL2CPP)
//
// Frachtaufträge zum Pebbles-Verdienen, auch im Multiplayer (siehe Auftraege.cs).
//  - Auftragsbrett: erscheint, wenn man beim Mechaniker steht. Annehmen mit 1/2/3, neue Angebote mit 0.
//  - F6: Stand ein-/ausblenden (Fracht, Zustand, Ziel, Richtung, Restzeit, Mitspieler, Kontostand).
//    Bei eingeblendetem Stand bricht Entf den Auftrag ab.
// Im Multiplayer brauchen alle Spieler die Mod; der Host verwaltet die Aufträge.

using System;
using System.Collections.Generic;
using System.Linq;
using Il2Cpp;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

[assembly: MelonInfo(typeof(Auftraege.Mod), "Auftraege", Auftraege.Mod.Version, "Valentin Gehmacher")]
[assembly: MelonGame("Funselektor", "over the hill")]

namespace Auftraege
{
    public class Mod : MelonMod
    {
        public const string Version = "0.5.6";

        const float MechanikerRadiusM = 30f;
        const float StillstandMps = 1.5f;
        const float KreisGroesse = 1.0f;     // Bleistiftkreis so groß, dass er das Ortssymbol umrahmt

        Auftragsverwaltung _v;
        readonly Anzeige _anzeige = new();
        bool _beendet, _standOffen, _amMechaniker;
        string _meldung; float _meldungBis;

        // Markierungen (Lichtstrahl + Kartenmarker) für Hütte bzw. Ziel
        GameObject _strahl, _kartenMarker;
        string _markiert = "";
        float _naechsteIconPruefung;

        // Angebots-Punkte auf der Karte (beim Mechaniker): Sprite -> (Nummer, Ziel?, Versatz)
        readonly List<GameObject> _angebotsMarker = new();
        readonly Dictionary<IntPtr, (int nr, bool ziel, int versatz)> _punktInfo = new();
        string _angebotsSchluessel = "";
        MelonPreferences_Entry<bool> _multiplayer;

        public override void OnInitializeMelon()
        {
            _v = new Auftragsverwaltung();
            _v.Meldung = m => { _meldung = m; _meldungBis = Time.realtimeSinceStartup + 6f; LoggerInstance.Msg(m); };
            Netz.Warnung = m => { _meldung = m; _meldungBis = Time.realtimeSinceStartup + 15f; LoggerInstance.Warning(m); };
            var kat = MelonPreferences.CreateCategory("Auftraege");
            _multiplayer = kat.CreateEntry("Multiplayer", true, description: "Aufträge mit Mitspielern teilen (alle brauchen die Mod)");
            LoggerInstance.Msg("Geladen – Auftragsbrett beim Mechaniker, F6 = Stand");
        }

        public override void OnApplicationQuit() => Herunterfahren();
        public override void OnDeinitializeMelon() => Herunterfahren();

        void Herunterfahren()
        {
            if (_beendet) return;
            _beendet = true;
            Netz.Empfangen = null;
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            if (_beendet) return;
            _v.SitzungZurueckgesetzt();
            MarkierungEntfernen();
        }

        public override void OnUpdate()
        {
            if (_beendet) return;
            try
            {
                if (_multiplayer.Value && !Netz.IstEinzelspieler && Spiel.AlleOrte().Count > 0)
                {
                    Netz.Aktivieren();
                    Netz.Tick();
                }
                MechanikerPruefen();
                TastenPruefen();
                _v.Tick();
                MarkierungAktualisieren();
                AngebotsMarkerAktualisieren();
                KartenIconAnpassen();
                AnzeigeAktualisieren();
            }
            catch (Exception e) { LoggerInstance.Warning("Fehler: " + e.Message); }
        }

        void MechanikerPruefen()
        {
            _amMechaniker = false;
            if (_v.Tempo > StillstandMps || !Spiel.EigenePosition(out var p, out _)) return;
            foreach (var m in Spiel.Mechaniker())
            {
                var d = m.Position - p; d.y = 0;
                if (d.magnitude < MechanikerRadiusM) { _amMechaniker = true; return; }
            }
        }

        void TastenPruefen()
        {
            Keyboard kb;
            try { kb = Keyboard.current; } catch { return; }
            if (kb == null) return;

            if (kb[Key.F6].wasPressedThisFrame) _standOffen = !_standOffen;

            if (_amMechaniker && _v.Aktiv == null)
            {
                if (kb[Key.Digit1].wasPressedThisFrame || kb[Key.Numpad1].wasPressedThisFrame) Annehmen(0);
                if (kb[Key.Digit2].wasPressedThisFrame || kb[Key.Numpad2].wasPressedThisFrame) Annehmen(1);
                if (kb[Key.Digit3].wasPressedThisFrame || kb[Key.Numpad3].wasPressedThisFrame) Annehmen(2);
                if (kb[Key.Digit0].wasPressedThisFrame || kb[Key.Numpad0].wasPressedThisFrame) _v.AngeboteErneuern();
            }
            if (_standOffen && _v.Aktiv != null && _v.Phase != Phase.Ende && kb[Key.Delete].wasPressedThisFrame)
                _v.Abbrechen();
        }

        void Annehmen(int i)
        {
            if (i < _v.Angebote.Count) _v.Annehmen(_v.Angebote[i].Id);
        }

        // ================= Markierungen =================

        void MarkierungAktualisieren()
        {
            var a = _v.Aktiv;
            string soll = "";
            Vector3 pos = default;
            // Hütte markieren, solange man noch nicht geladen hat; danach das Ziel
            bool laden = a != null && a.Id != 0 && (_v.Phase == Phase.Anreise || (_v.Phase == Phase.Laeuft && !_v.FahreMit));
            if (laden) { soll = "S" + a.Id; pos = a.StartPos; }
            else if (a != null && _v.Phase == Phase.Laeuft && !_v.BinDraussen(Netz.LokalerSpieler)) { soll = "Z" + a.Id; pos = a.ZielPos; }

            if (soll == _markiert) return;
            MarkierungEntfernen();
            _markiert = soll;
            if (soll == "") return;

            _strahl = Bodenring(pos, soll.StartsWith("S") ? _v.StartRadiusM : _v.ZielRadiusM);

            try
            {
                _kartenMarker = new GameObject("Auftraege_Kartenmarker");
                _kartenMarker.transform.position = pos;
                var m = _kartenMarker.AddComponent<MapTrackedTransform>();
                m.SetIcon(Bleistift.Kreis);
                m.SetUseBackground(false);
                m.VisibleOnMap = true;
            }
            catch (Exception e) { LoggerInstance.Warning("Kartenmarker nicht möglich: " + e.Message); }
        }

        /// Unser Kartensymbol in einen großen Rotstiftkreis verwandeln. Die Karte setzt Größe und Farbe
        /// ihrer Symbole selbst (Zoom, Stilfarben) – deshalb zeichnen wir den Kreis als eigenes Kind-Bild,
        /// das die Karte nicht anfasst, und blenden das eigentliche Symbol aus. Außerdem über die anderen
        /// Symbole legen, damit der Kreis nicht vom Orts-Symbol verdeckt wird.
        void KartenIconAnpassen()
        {
            if ((_kartenMarker == null && _angebotsMarker.Count == 0) || Time.realtimeSinceStartup < _naechsteIconPruefung) return;
            _naechsteIconPruefung = Time.realtimeSinceStartup + 0.5f;
            try
            {
                var kreis = Bleistift.Kreis;
                foreach (var icon in UnityEngine.Object.FindObjectsOfType<MapIcon>())
                {
                    if (icon == null || icon.iconImage == null || icon.iconImage.sprite == null) continue;
                    if (_punktInfo.TryGetValue(icon.iconImage.sprite.Pointer, out var info)) { NummernPunktAnpassen(icon, info); continue; }
                    if (icon.iconImage.sprite != kreis) continue;
                    icon.SetBackgroundVisibility(false);
                    icon.iconImage.enabled = false;
                    icon.iconImage.raycastTarget = false;
                    var rt = icon.rectTransform;
                    rt.SetAsLastSibling();
                    if (rt.Find("Auftraege_Kreis") != null) continue;

                    var go = new GameObject("Auftraege_Kreis");
                    var krt = go.AddComponent<RectTransform>();
                    krt.SetParent(rt, false);
                    krt.anchorMin = krt.anchorMax = krt.pivot = new Vector2(0.5f, 0.5f);
                    krt.anchoredPosition = Vector2.zero;
                    float basis = Mathf.Max(rt.rect.width, rt.rect.height, 40f);
                    krt.sizeDelta = Vector2.one * basis * KreisGroesse;
                    var img = go.AddComponent<Image>();
                    img.sprite = kreis;
                    img.color = Color.white;
                    img.raycastTarget = false;
                    LoggerInstance.Msg($"[Karte] Kreis angelegt ({basis:F0} px × {KreisGroesse})");
                }
            }
            catch (Exception e) { LoggerInstance.Warning("Kartensymbol: " + e.Message); _naechsteIconPruefung += 30f; }
        }

        /// Beim Mechaniker: Start (roter Punkt) und Ziel (weißer Punkt) jedes Angebots mit seiner Nummer
        /// auf der Karte zeigen. Verschwinden, sobald man wegfährt oder einen Auftrag annimmt.
        void AngebotsMarkerAktualisieren()
        {
            bool zeigen = _amMechaniker && _v.Aktiv == null && _v.Angebote.Count > 0;
            string schluessel = zeigen ? string.Join(",", _v.Angebote.Select(a => a.Id)) : "";
            if (schluessel == _angebotsSchluessel) return;
            _angebotsSchluessel = schluessel;

            foreach (var go in _angebotsMarker) if (go != null) UnityEngine.Object.Destroy(go);
            _angebotsMarker.Clear();
            _punktInfo.Clear();
            if (!zeigen) return;

            try
            {
                var belegt = new List<Vector3>(); // gleiche Orte (z. B. zwei Angebote ab derselben Hütte) nebeneinander
                for (int i = 0; i < _v.Angebote.Count && i < 9; i++)
                {
                    var a = _v.Angebote[i];
                    foreach (var (pos, ziel) in new[] { (a.StartPos, false), (a.ZielPos, true) })
                    {
                        int versatz = belegt.Count(b => (b - pos).sqrMagnitude < 40f * 40f);
                        belegt.Add(pos);
                        var sp = Bleistift.Punkt(ziel);
                        _punktInfo[sp.Pointer] = (i + 1, ziel, versatz);
                        var go = new GameObject($"Auftraege_Angebot{i + 1}{(ziel ? "Ziel" : "Start")}");
                        go.transform.position = pos;
                        var m = go.AddComponent<MapTrackedTransform>();
                        m.SetIcon(sp);
                        m.SetUseBackground(false);
                        m.VisibleOnMap = true;
                        _angebotsMarker.Add(go);
                    }
                }
                _naechsteIconPruefung = 0;
            }
            catch (Exception e) { LoggerInstance.Warning("Angebots-Marker nicht möglich: " + e.Message); }
        }

        /// Nummernpunkt als eigenes Bild mit Zahl, als „Abzeichen“ oben rechts neben dem Ortssymbol,
        /// damit das Symbol selbst sichtbar bleibt.
        void NummernPunktAnpassen(MapIcon icon, (int nr, bool ziel, int versatz) info)
        {
            icon.SetBackgroundVisibility(false);
            icon.iconImage.enabled = false;
            icon.iconImage.raycastTarget = false;
            var rt = icon.rectTransform;
            rt.SetAsLastSibling();
            if (rt.Find("Auftraege_Nummer") != null) return;

            float basis = Mathf.Max(rt.rect.width, rt.rect.height, 40f);
            float groesse = basis * 0.62f;
            var go = new GameObject("Auftraege_Nummer");
            var krt = go.AddComponent<RectTransform>();
            krt.SetParent(rt, false);
            krt.anchorMin = krt.anchorMax = krt.pivot = new Vector2(0.5f, 0.5f);
            krt.anchoredPosition = new Vector2(basis * 0.42f + info.versatz * groesse * 0.95f, basis * 0.42f);
            krt.sizeDelta = Vector2.one * groesse;
            var img = go.AddComponent<Image>();
            img.sprite = icon.iconImage.sprite;
            img.raycastTarget = false;

            var tgo = new GameObject("Zahl");
            var trt = tgo.AddComponent<RectTransform>();
            trt.SetParent(krt, false);
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            var t = tgo.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
            if (_anzeige.Schrift != null) t.font = _anzeige.Schrift;
            t.text = info.nr.ToString();
            t.fontSize = groesse * 0.68f;
            t.fontStyle = Il2CppTMPro.FontStyles.Bold;
            t.alignment = Il2CppTMPro.TextAlignmentOptions.Center;
            t.color = info.ziel ? Bleistift.Rot : Color.white;
            t.raycastTarget = false;
        }

        /// Leuchtring am Boden im Stil des Spiels: Kopie des CylinderMarkers eines Ortes
        /// (derselbe Ring, den z. B. geschlossene Kisten haben), auf unsere Größe und Gelb gesetzt.
        GameObject Bodenring(Vector3 pos, float radius)
        {
            try
            {
                CylinderMarker vorlage = null;
                foreach (var cm in UnityEngine.Object.FindObjectsOfType<CylinderMarker>(true))
                    if (cm != null && !cm.name.StartsWith("Auftraege")) { vorlage = cm; break; }
                if (vorlage == null) { LoggerInstance.Warning("Kein Bodenring als Vorlage gefunden"); return null; }

                var go = UnityEngine.Object.Instantiate(vorlage.gameObject);
                go.name = "Auftraege_Bodenring";
                go.transform.SetParent(null, false);
                go.transform.position = pos;
                go.transform.rotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                go.SetActive(true);
                var ring = go.GetComponent<CylinderMarker>();
                var gelb = new Color(1f, 0.82f, 0.25f, 1f);
                ring.size = new Vector3(radius * 2f, ring.size.y > 0 ? ring.size.y : 8f, radius * 2f);
                ring.Color = gelb;
                ring.colorOverride = new Il2CppSystem.Nullable<Color>(gelb);
                ring.geometryDirty = true;
                ring.propertiesDirty = true;
                ring.Refresh();
                return go;
            }
            catch (Exception e) { LoggerInstance.Warning("Bodenring nicht möglich: " + e.Message); return null; }
        }

        void MarkierungEntfernen()
        {
            if (_strahl != null) UnityEngine.Object.Destroy(_strahl);
            if (_kartenMarker != null) UnityEngine.Object.Destroy(_kartenMarker);
            foreach (var go in _angebotsMarker) if (go != null) UnityEngine.Object.Destroy(go);
            _angebotsMarker.Clear(); _punktInfo.Clear(); _angebotsSchluessel = "";
            _strahl = null; _kartenMarker = null; _markiert = "";
        }

        // ================= Anzeige =================

        const string Grau = "#9AA0A6", Gold = "#FFB347", Gruen = "#7FD87F", Rot = "#FF6B6B", Blau = "#6FB7FF";

        string _letzterZustand = "";

        /// Liegt gerade ein Menü des Spiels über dem Fahren (Pause, Einstellungen, Mechaniker/Händler,
        /// Karte, Dialoge …)? Dann blenden wir unsere Kästen aus. Gefahren wird im Zustand „StateGame“.
        bool SpielMenuOffen()
        {
            try
            {
                var gsm = Il2CppFunCore.Managers.GameStateManager.Instance;
                var st = gsm?.PeekState();
                if (st == null) return false;
                string name = st.GetIl2CppType().Name;
                if (name != _letzterZustand)
                {
                    _letzterZustand = name;
                    LoggerInstance.Msg("[Zustand] " + name);
                }
                return name != "StateGame";
            }
            catch { return false; }
        }

        void AnzeigeAktualisieren()
        {
            _anzeige.SicherstellenAufgebaut();
            _anzeige.Sichtbar(!SpielMenuOffen());

            if (_meldung != null && Time.realtimeSinceStartup < _meldungBis) _anzeige.Meldung.Zeigen("", _meldung);
            else _anzeige.Meldung.Verstecken();

            if (_amMechaniker && _v.Aktiv == null) _anzeige.Brett.Zeigen("Frachtaufträge", BrettText());
            else _anzeige.Brett.Verstecken();

            // Abrechnung nach dem Abliefern 25 s lang zeigen
            if (_v.AbrechnungSeit >= 0 && Time.realtimeSinceStartup - _v.AbrechnungSeit < 25f && _v.AbrechnungPosten.Count > 0)
                _anzeige.Abrechnung.Zeigen(_v.AbrechnungTitel, AbrechnungText());
            else _anzeige.Abrechnung.Verstecken();

            // Beim Laden/Abladen den Stand automatisch zeigen
            if (_standOffen || _v.LadeFortschritt > 0) _anzeige.Stand.Zeigen(StandTitel(), StandText());
            else _anzeige.Stand.Verstecken();
        }

        string AbrechnungText()
        {
            var z = new List<string>();
            foreach (var (text, betrag) in _v.AbrechnungPosten)
            {
                string farbe = betrag < 0 ? Rot : text.StartsWith("Gruppenbonus") ? Gruen : "#FFFFFF";
                z.Add($"{text}<pos=82%><color={farbe}>{(betrag > 0 ? "+" : "−")}{Mathf.Abs(betrag)}</color>");
            }
            int summe = _v.AbrechnungPosten.Sum(p => p.betrag);
            z.Add($"<color={Grau}>────────────────────────────</color>");
            z.Add($"<b>Verdient</b><pos=82%><color={Gold}><b>{summe} Pebbles</b></color>");
            if (!string.IsNullOrEmpty(_v.AbrechnungFusszeile)) z.Add($"<size=80%><color={Grau}>{_v.AbrechnungFusszeile}</color></size>");
            return string.Join("\n", z);
        }

        string BrettText()
        {
            if (_v.Angebote.Count == 0)
                return Netz.IstLeiter ? $"<color={Grau}>Gerade keine Aufträge verfügbar.</color>"
                                      : $"<color={Grau}>Warte auf Aufträge vom Host …</color>";
            var zeilen = new List<string>();
            for (int i = 0; i < _v.Angebote.Count; i++)
            {
                var a = _v.Angebote[i];
                var f = Fracht.Nach(a.Fracht);
                string eilig = a.Eilig ? $"  <color={Rot}>EILIG</color>" : "";
                zeilen.Add($"<color={Gold}><b>[{i + 1}]</b></color>  <b>{a.Fracht}</b>  <size=80%><color={Grau}>({f.Beschreibung})</color></size>{eilig}\n" +
                           $"<size=85%>{a.Start}  →  {a.Ziel}\n" +
                           $"<color={Grau}>{(a.Weg ? $"{a.StreckeM / 1000f:F1} km über Wege" : $"{a.DistanzM / 1000f:F1} km querfeldein")} · {Zeit(a.ZeitS)}</color> · <color={Gold}>{a.Lohn} Pebbles</color></size>");
            }
            zeilen.Add($"<size=80%><color={Grau}>1–3 annehmen · 0 neue Aufträge</color></size>");
            return string.Join("\n\n", zeilen);
        }

        string StandTitel() => _v.Aktiv == null ? "Kein Auftrag" : _v.Aktiv.Fracht;

        string StandText()
        {
            var a = _v.Aktiv;
            var z = new List<string>();
            if (a == null)
            {
                z.Add($"<color={Grau}>Beim Mechaniker gibt es Frachtaufträge.</color>");
            }
            else
            {
                Spiel.EigenePosition(out var p, out var rot);
                int lokal = Netz.LokalerSpieler;
                switch (_v.Phase)
                {
                    case Phase.Anreise:
                        z.Add($"Abholen bei <color={Blau}><b>{a.Start}</b></color>");
                        z.Add($"<size=85%><color={Grau}>Hinfahren oder per Karte schnellreisen</color></size>");
                        z.Add(Entfernung(a.StartPos - p, rot));
                        if (_v.LadeFortschritt > 0) z.Add($"<color={Gold}>Fracht wird geladen … {_v.LadeFortschritt * 100:F0} %</color>");
                        else z.Add($"<size=85%>Danach: {a.Ziel} · <color={Gold}>{a.Lohn} Pebbles</color> · {Zeit(a.ZeitS)}</size>");
                        break;
                    case Phase.Laeuft when !_v.FahreMit:
                        z.Add($"Konvoi unterwegs nach <color={Gold}><b>{a.Ziel}</b></color>");
                        z.Add($"Zuladen bei <color={Blau}><b>{a.Start}</b></color>");
                        z.Add(Entfernung(a.StartPos - p, rot));
                        z.Add($"Restzeit: {Zeit(Mathf.CeilToInt(_v.RestS))}");
                        if (_v.LadeFortschritt > 0) z.Add($"<color={Gold}>Fracht wird geladen … {_v.LadeFortschritt * 100:F0} %</color>");
                        break;
                    case Phase.Laeuft:
                        z.Add($"Liefern nach <color={Gold}><b>{a.Ziel}</b></color>");
                        if (!_v.BinDraussen(lokal))
                        {
                            z.Add(Entfernung(a.ZielPos - p, rot));
                            string fz = _v.Ladung > 70 ? Gruen : _v.Ladung > 35 ? Gold : Rot;
                            z.Add($"Zustand der Fracht: <color={fz}><b>{_v.Ladung:F0} %</b></color>");
                            float rest = _v.RestS;
                            z.Add($"Restzeit: <color={(rest < 60 ? Rot : "#FFFFFF")}>{Zeit(Mathf.CeilToInt(rest))}</color>");
                            if (_v.LadeFortschritt > 0) z.Add($"<color={Gold}>Fracht wird abgeladen … {_v.LadeFortschritt * 100:F0} %</color>");
                            else z.Add($"<size=85%><color={Grau}>Lohn bei voller Fracht: {a.Lohn} Pebbles · keine Schnellreise!</color></size>");
                        }
                        else z.Add(_v.Abgeliefert.Contains(lokal) ? $"<color={Gruen}>Abgeliefert</color> – warte auf die anderen"
                                                                  : $"<color={Rot}>Ausgeschieden</color>");
                        break;
                    case Phase.Ende:
                        z.Add(_v.Ergebnis switch
                        {
                            Ergebnis.Geschafft => $"<color={Gruen}>Auftrag erledigt</color>",
                            Ergebnis.Abgelaufen => $"<color={Rot}>Zeit abgelaufen</color>",
                            Ergebnis.Gescheitert => $"<color={Rot}>Gescheitert</color>",
                            _ => $"<color={Rot}>Abgebrochen</color>",
                        });
                        break;
                }

                var spieler = Netz.AlleSpieler();
                if (spieler.Count > 1 && _v.Phase != Phase.Anreise)
                    z.Add("<size=85%>" + string.Join("  ", spieler.Select(s =>
                        _v.Abgeliefert.Contains(s) ? $"<color={Gruen}>{Netz.SpielerName(s)}</color>" :
                        _v.Raus.Contains(s) ? $"<color={Rot}><s>{Netz.SpielerName(s)}</s></color>" :
                        $"<color={Grau}>{Netz.SpielerName(s)}</color>")) + "</size>");
                if (_v.Phase != Phase.Ende)
                    z.Add($"<size=80%><color={Grau}>Entf = {(_v.Phase == Phase.Laeuft && _v.FahreMit && Netz.AlleSpieler().Count > 1 ? "aussteigen" : "Auftrag abbrechen")} · F6 ausblenden</color></size>");
            }
            int stand = Spiel.PebblesKontostand();
            if (stand >= 0) z.Add($"<size=85%>Kontostand: <color={Gold}>{stand} Pebbles</color></size>");
            return string.Join("\n", z);
        }

        static string Entfernung(Vector3 d, Quaternion rot)
        {
            d.y = 0;
            return $"Entfernung: <b>{d.magnitude / 1000f:F2} km</b>  <color={Grau}>{Richtung(d, rot)}</color>";
        }

        static string Zeit(int s) => $"{s / 60}:{s % 60:00} min";

        /// Richtung als Uhrzeit relativ zur Fahrtrichtung (12 Uhr = geradeaus).
        static string Richtung(Vector3 ziel, Quaternion rot)
        {
            var vor = rot * Vector3.forward; vor.y = 0;
            if (vor.sqrMagnitude < 1e-4f || ziel.sqrMagnitude < 1e-4f) return "";
            float w = Vector3.SignedAngle(vor, ziel, Vector3.up);
            int uhr = Mathf.RoundToInt((w < 0 ? w + 360 : w) / 30f) % 12;
            return $"auf {(uhr == 0 ? 12 : uhr)} Uhr";
        }
    }
}
