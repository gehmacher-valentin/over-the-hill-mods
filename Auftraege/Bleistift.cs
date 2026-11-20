// Handgezeichneter Rotstiftkreis als Sprite (für den Kartenmarker).
// Ein etwas unregelmäßiger Graphitstrich, der sich am Ende überlappt, mit Körnung
// und schwächerem Druck an Anfang und Ende. Innen durchsichtig.

using UnityEngine;

namespace Auftraege
{
    internal static class Bleistift
    {
        static Sprite _kreis;

        public static Sprite Kreis
        {
            get
            {
                if (_kreis != null) return _kreis;
                const int G = 256;
                var alpha = new float[G * G];
                var rnd = new System.Random(7);
                float R = G * 0.38f, mitte = G / 2f;
                float a1 = 0.7f, a2 = 2.1f, start = -0.9f;
                const float umlauf = Mathf.PI * 2f * 1.12f;   // leicht überlappend
                const int schritte = 2600;
                for (int i = 0; i <= schritte; i++)
                {
                    float u = i / (float)schritte;
                    float th = start + u * umlauf;
                    // Unregelmäßiger Radius + leichte Spirale, damit sich die Enden nicht genau treffen
                    float r = R * (1f + 0.035f * Mathf.Sin(2f * th + a1) + 0.018f * Mathf.Sin(5f * th + a2) + 0.05f * (u - 0.5f));
                    float x = mitte + Mathf.Cos(th) * r, y = mitte + Mathf.Sin(th) * r;
                    // Druck: an den Enden schwächer und dünner
                    float druck = Mathf.Clamp01(Mathf.Min(u, 1f - u) * 9f);
                    float breite = 5.0f + 5.0f * druck + 1.5f * Mathf.Sin(th * 3f + 1.3f);
                    Stempel(alpha, G, x, y, breite, 0.55f + 0.45f * druck, rnd);
                }
                var tex = new Texture2D(G, G, TextureFormat.RGBA32, true) { name = "Auftraege_Bleistiftkreis", wrapMode = TextureWrapMode.Clamp };
                var farbe = new Color(0.78f, 0.12f, 0.10f); // Rotstift
                var px = new Color32[G * G];
                for (int i = 0; i < px.Length; i++)
                {
                    float a = Mathf.Clamp01(alpha[i]);
                    px[i] = new Color(farbe.r, farbe.g, farbe.b, a * 0.95f);
                }
                tex.SetPixels32(px);
                tex.Apply(true, true);
                _kreis = Sprite.Create(tex, new Rect(0, 0, G, G), new Vector2(0.5f, 0.5f), 100f);
                _kreis.name = "Auftraege_Bleistiftkreis";
                return _kreis;
            }
        }

        static void Stempel(float[] alpha, int g, float cx, float cy, float radius, float staerke, System.Random rnd)
        {
            int x0 = Mathf.FloorToInt(cx - radius - 1), x1 = Mathf.CeilToInt(cx + radius + 1);
            int y0 = Mathf.FloorToInt(cy - radius - 1), y1 = Mathf.CeilToInt(cy + radius + 1);
            for (int y = Mathf.Max(0, y0); y <= Mathf.Min(g - 1, y1); y++)
                for (int x = Mathf.Max(0, x0); x <= Mathf.Min(g - 1, x1); x++)
                {
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    if (d > radius) continue;
                    // Weiche Kante + Papierkörnung
                    float kante = Mathf.Clamp01((radius - d) / 1.2f);
                    float korn = 0.55f + 0.45f * (float)rnd.NextDouble();
                    float a = kante * korn * staerke * 0.35f;
                    int idx = y * g + x;
                    alpha[idx] = alpha[idx] + a * (1f - alpha[idx]); // Striche überlagern sich wie Graphit
                }
        }
    }
}
