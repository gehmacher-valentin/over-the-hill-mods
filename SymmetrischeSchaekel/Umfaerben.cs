// Umfärben der roten Schäkel-Schraube (dunkelrot / grau / original).
// Rot kann entweder als Materialfarbe oder in der Textur stecken – beides wird behandelt:
//  - Materialfarben (Color-Properties), die deutlich rot sind, werden ersetzt.
//  - In Farb-Texturen werden kräftig rote Pixel umgefärbt (Schattierung bleibt erhalten).
// Es werden nur Material-Instanzen der Schäkel verändert, nie die geteilten Originale.

using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace SymmetrischeSchaekel
{
    internal class Umfaerber
    {
        public static readonly string[] Modi = { "dunkelrot", "grau", "original" };

        readonly MelonLogger.Instance _log;
        // Renderer-ID -> (Renderer, Original-Materialien)
        readonly Dictionary<int, (Renderer r, Material[] orig)> _bearbeitet = new();
        // (Textur-ID, Modus) -> umgefärbte Textur
        readonly Dictionary<string, Texture2D> _texCache = new();
        readonly HashSet<string> _geloggteMaterialien = new();

        public Umfaerber(MelonLogger.Instance log) { _log = log; }

        public void AllesZuruecksetzen()
        {
            foreach (var (r, orig) in _bearbeitet.Values)
                if (r != null) r.sharedMaterials = orig;
            _bearbeitet.Clear();
        }

        public void SzeneGewechselt() => _bearbeitet.Clear();

        public void Bearbeiten(Transform schaekel, string modus)
        {
            foreach (var r in schaekel.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                MaterialienLoggen(r);
                if (modus == "original") continue;
                int id = r.GetInstanceID();
                if (_bearbeitet.ContainsKey(id)) continue;

                var orig = r.sharedMaterials;
                var neu = new Material[orig.Length];
                for (int i = 0; i < orig.Length; i++)
                    neu[i] = orig[i] == null ? null : MaterialUmfaerben(orig[i], modus);
                r.sharedMaterials = neu;
                _bearbeitet[id] = (r, orig);
            }
        }

        Material MaterialUmfaerben(Material quelle, string modus)
        {
            var m = new Material(quelle) { name = quelle.name + "_Umgefaerbt" };
            var shader = m.shader;
            int n = shader.GetPropertyCount();
            for (int i = 0; i < n; i++)
            {
                string name = shader.GetPropertyName(i);
                var typ = shader.GetPropertyType(i);
                if (typ == UnityEngine.Rendering.ShaderPropertyType.Color)
                {
                    if (IstFarbProperty(name))
                    {
                        Color c = m.GetColor(name);
                        if (IstRot(c.r, c.g, c.b)) m.SetColor(name, Ersetzen(c, modus));
                    }
                }
                else if (typ == UnityEngine.Rendering.ShaderPropertyType.Texture && IstFarbTextur(name))
                {
                    var tex = m.GetTexture(name);
                    if (tex == null) continue;
                    var neu = TexturUmfaerben(tex, modus);
                    if (neu != null) m.SetTexture(name, neu);
                }
            }
            return m;
        }

        static bool IstFarbProperty(string n)
        {
            n = n.ToLowerInvariant();
            return n.Contains("color") && !n.Contains("emission") && !n.Contains("spec");
        }

        static bool IstFarbTextur(string n)
        {
            n = n.ToLowerInvariant();
            if (n.Contains("normal") || n.Contains("bump") || n.Contains("mask") || n.Contains("metal")
                || n.Contains("occlusion") || n.Contains("height") || n.Contains("detail") || n.Contains("emission"))
                return false;
            return n.Contains("base") || n.Contains("main") || n.Contains("albedo") || n.Contains("diffuse") || n.Contains("color");
        }

        // Kräftiges Rot: Rot dominiert deutlich gegenüber Grün und Blau
        static bool IstRot(float r, float g, float b) => r > 0.25f && r > g * 1.8f && r > b * 1.8f;

        static Color Ersetzen(Color c, string modus)
        {
            if (modus == "grau")
            {
                // Helligkeit erhalten, aber als dunkles Metallgrau
                float v = Mathf.Clamp01((0.30f * c.r + 0.59f * c.g + 0.11f * c.b) * 0.9f + 0.12f);
                return new Color(v, v, v, c.a);
            }
            // dunkelrot: Farbton behalten, deutlich dunkler und etwas entsättigt
            return new Color(c.r * 0.42f, c.g * 0.30f + 0.02f, c.b * 0.30f + 0.02f, c.a);
        }

        Texture2D TexturUmfaerben(Texture quelle, string modus)
        {
            string key = quelle.GetInstanceID() + "|" + modus;
            if (_texCache.TryGetValue(key, out var cached) && cached != null) return cached;

            try
            {
                int w = quelle.width, h = quelle.height;
                var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var vorher = RenderTexture.active;
                Graphics.Blit(quelle, rt);
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, true, false) { name = quelle.name + "_Umgefaerbt" };
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                RenderTexture.active = vorher;
                RenderTexture.ReleaseTemporary(rt);

                var px = tex.GetPixels32();
                int anzahl = 0;
                for (int i = 0; i < px.Length; i++)
                {
                    Color32 p = px[i];
                    if (!IstRot(p.r / 255f, p.g / 255f, p.b / 255f)) continue;
                    Color neu = Ersetzen(new Color(p.r / 255f, p.g / 255f, p.b / 255f, p.a / 255f), modus);
                    px[i] = neu;
                    anzahl++;
                }
                tex.SetPixels32(px);
                tex.Apply(true, false);
                tex.wrapMode = quelle.wrapMode;
                tex.filterMode = quelle.filterMode;
                tex.anisoLevel = quelle.anisoLevel;
                _log.Msg($"Textur '{quelle.name}' ({w}x{h}): {anzahl} rote Pixel -> {modus}");
                _texCache[key] = tex;
                return tex;
            }
            catch (Exception e)
            {
                _log.Warning($"Textur '{quelle.name}' konnte nicht umgefärbt werden: {e.Message}");
                return null;
            }
        }

        void MaterialienLoggen(Renderer r)
        {
            foreach (var m in r.sharedMaterials)
            {
                if (m == null || m.name.EndsWith("_Umgefaerbt") || !_geloggteMaterialien.Add(m.name)) continue;
                var shader = m.shader;
                var teile = new List<string>();
                int n = shader.GetPropertyCount();
                for (int i = 0; i < n; i++)
                {
                    string name = shader.GetPropertyName(i);
                    var typ = shader.GetPropertyType(i);
                    if (typ == UnityEngine.Rendering.ShaderPropertyType.Color)
                        teile.Add($"{name}={m.GetColor(name)}");
                    else if (typ == UnityEngine.Rendering.ShaderPropertyType.Texture)
                    {
                        var t = m.GetTexture(name);
                        if (t != null) teile.Add($"{name}=Tex'{t.name}'");
                    }
                }
                _log.Msg($"Material '{m.name}' Shader '{shader.name}': {string.Join("; ", teile)}");
            }
        }
    }
}
