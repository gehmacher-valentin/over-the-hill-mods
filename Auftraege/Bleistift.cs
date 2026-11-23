// Handgezeichnete Buntstiftkreise als Sprites (rot/blau/grün) für die Karte.
// Die Striche (Skizze aus mehreren dünnen Runden mit Papierkörnung) sind vorab mit
// werkzeuge/stift.py gezeichnet und als Deckkraft eingebettet: 3 × 256×256 Bytes, gzip.

using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace Auftraege
{
    internal static class Bleistift
    {
        // Drei Stiftfarben – eine pro Angebot. Die Farbe bleibt nach dem Annehmen erhalten
        // (Kartenkreis und Leuchtring am Boden).
        public static readonly Color[] Stifte =
        {
            new(0.80f, 0.13f, 0.10f),   // Rot
            new(0.13f, 0.38f, 0.85f),   // Blau
            new(0.10f, 0.58f, 0.22f),   // Grün
        };
        public static readonly string[] StiftHex = { "#E0473D", "#4C86F0", "#3DBA5C" };
        public static readonly Color[] Leuchten =
        {
            new(1f, 0.28f, 0.22f, 1f),
            new(0.30f, 0.58f, 1f, 1f),
            new(0.32f, 0.92f, 0.40f, 1f),
        };

        const int G = 256;
        static readonly Sprite[] _kreise = new Sprite[3];
        static byte[] _deckkraft;

        public static bool IstKreis(Sprite sp)
        {
            if (sp == null) return false;
            foreach (var k in _kreise) if (k != null && k.Pointer == sp.Pointer) return true;
            return false;
        }

        public static Sprite Kreis(int farbe)
        {
            farbe = ((farbe % 3) + 3) % 3;
            if (_kreise[farbe] != null) return _kreise[farbe];
            _deckkraft ??= Laden();

            var tex = new Texture2D(G, G, TextureFormat.RGBA32, true) { name = "Auftraege_Bleistiftkreis", wrapMode = TextureWrapMode.Clamp };
            Color32 stift = Stifte[farbe];
            var px = new Color32[G * G];
            for (int i = 0; i < px.Length; i++)
                px[i] = new Color32(stift.r, stift.g, stift.b, _deckkraft[farbe * G * G + i]);
            tex.SetPixels32(px);
            tex.Apply(true, true);
            var sp = Sprite.Create(tex, new Rect(0, 0, G, G), new Vector2(0.5f, 0.5f), 100f);
            sp.name = "Auftraege_Bleistiftkreis" + farbe;
            return _kreise[farbe] = sp;
        }

        static byte[] Laden()
        {
            using var roh = typeof(Bleistift).Assembly.GetManifestResourceStream("kreise.bin.gz");
            using var gz = new GZipStream(roh, CompressionMode.Decompress);
            using var ms = new MemoryStream();
            gz.CopyTo(ms);
            return ms.ToArray();
        }
    }
}
