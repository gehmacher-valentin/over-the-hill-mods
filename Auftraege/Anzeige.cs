// Bildschirmanzeige mit Unity-UI (uGUI + TextMeshPro, Schrift des Spiels).
// Eine Spalte am linken Bildrand – überlagert den Kompass oben in der Mitte nicht.
// Enthält drei Kästen: Auftragsbrett, Stand, Kurzmeldung.

using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Auftraege
{
    internal class Kasten
    {
        public GameObject Wurzel;
        public TextMeshProUGUI Titel, Text;

        public void Zeigen(string titel, string text)
        {
            if (Wurzel == null) return;
            if (Titel.text != titel) Titel.text = titel;
            if (Text.text != text) Text.text = text;
            if (!Wurzel.activeSelf) Wurzel.SetActive(true);
        }

        public void Verstecken()
        {
            if (Wurzel != null && Wurzel.activeSelf) Wurzel.SetActive(false);
        }
    }

    internal class Anzeige
    {
        public static readonly Color Akzent = new(1f, 0.62f, 0.2f);
        static readonly Color Hintergrund = new(0.06f, 0.065f, 0.075f, 0.82f);

        GameObject _canvas;
        TMP_FontAsset _schrift;
        float _naechsteSchriftSuche;
        public Kasten Brett, Stand, Meldung, Abrechnung;

        public bool Bereit => _canvas != null;

        public void SicherstellenAufgebaut()
        {
            if (_canvas != null) { SchriftUebernehmen(); return; }

            _canvas = new GameObject("Auftraege_Anzeige");
            Object.DontDestroyOnLoad(_canvas);
            var c = _canvas.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = 500;
            var s = _canvas.AddComponent<CanvasScaler>();
            s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            s.referenceResolution = new Vector2(1920, 1080);
            s.matchWidthOrHeight = 0.5f;

            // Spalte links, etwas unterhalb des oberen Rands
            var spalte = new GameObject("Spalte");
            spalte.transform.SetParent(_canvas.transform, false);
            var rt = spalte.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(28, -190);
            rt.sizeDelta = new Vector2(430, 0);
            var vl = spalte.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 10;
            vl.childControlHeight = true; vl.childControlWidth = true;
            vl.childForceExpandHeight = false; vl.childForceExpandWidth = true;
            var fit = spalte.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            Meldung = KastenBauen(spalte.transform, "Meldung", 19, false);
            Brett = KastenBauen(spalte.transform, "Brett", 17, true);
            Stand = KastenBauen(spalte.transform, "Stand", 17, true);
            Abrechnung = KastenBauen(spalte.transform, "Abrechnung", 17, true);
            SchriftUebernehmen();
        }

        public void Abbauen()
        {
            if (_canvas != null) Object.Destroy(_canvas);
            _canvas = null; Brett = Stand = Meldung = Abrechnung = null;
        }

        Kasten KastenBauen(Transform eltern, string name, float textGroesse, bool mitTitel)
        {
            var k = new Kasten();
            k.Wurzel = new GameObject(name);
            k.Wurzel.transform.SetParent(eltern, false);
            k.Wurzel.AddComponent<RectTransform>();
            k.Wurzel.AddComponent<Image>().color = Hintergrund;
            var vl = k.Wurzel.AddComponent<VerticalLayoutGroup>();
            vl.padding = new RectOffset(22, 16, 12, 14);
            vl.spacing = 4;
            vl.childControlHeight = true; vl.childControlWidth = true;
            vl.childForceExpandHeight = false; vl.childForceExpandWidth = true;

            // Akzentstreifen links
            var streifen = new GameObject("Akzent");
            streifen.transform.SetParent(k.Wurzel.transform, false);
            var srt = streifen.AddComponent<RectTransform>();
            srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(0, 1);
            srt.pivot = new Vector2(0, 0.5f);
            srt.sizeDelta = new Vector2(5, 0);
            srt.anchoredPosition = Vector2.zero;
            streifen.AddComponent<Image>().color = Akzent;
            streifen.AddComponent<LayoutElement>().ignoreLayout = true;

            k.Titel = TextBauen(k.Wurzel.transform, "Titel", 21, FontStyles.Bold, Akzent);
            k.Text = TextBauen(k.Wurzel.transform, "Text", textGroesse, FontStyles.Normal, Color.white);
            if (!mitTitel) k.Titel.gameObject.SetActive(false);
            k.Wurzel.SetActive(false);
            return k;
        }

        TextMeshProUGUI TextBauen(Transform eltern, string name, float groesse, FontStyles stil, Color farbe)
        {
            var go = new GameObject(name);
            go.transform.SetParent(eltern, false);
            go.AddComponent<RectTransform>();
            var t = go.AddComponent<TextMeshProUGUI>();
            t.fontSize = groesse;
            t.fontStyle = stil;
            t.color = farbe;
            t.richText = true;
            t.enableWordWrapping = true;
            t.lineSpacing = 4;
            return t;
        }

        /// Schrift des Spiels übernehmen, sobald irgendein Spieltext existiert.
        void SchriftUebernehmen()
        {
            if (_schrift != null || Time.realtimeSinceStartup < _naechsteSchriftSuche) return;
            _naechsteSchriftSuche = Time.realtimeSinceStartup + 5f;
            foreach (var t in Object.FindObjectsOfType<TextMeshProUGUI>())
            {
                if (t == null || t.font == null || t.transform.IsChildOf(_canvas.transform)) continue;
                _schrift = t.font;
                break;
            }
            if (_schrift == null) return;
            foreach (var k in new[] { Brett, Stand, Meldung })
            {
                k.Titel.font = _schrift;
                k.Text.font = _schrift;
            }
        }
    }
}
