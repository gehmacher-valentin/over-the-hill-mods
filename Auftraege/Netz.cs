// Netzwerk-Transport für den Konvoi.
//
// v0.5.0 hatte den Fusion-Empfang (OnReliableDataReceived) per Harmony gepatcht – das ließ das
// Spiel beim Hosten abstürzen (Il2Cpp-Trampolin mit Struct-Parametern). Jetzt komplett ohne Patch:
// Die Mods reden über Steam-P2P (ISteamNetworkingMessages, eigener Kanal) direkt über die Flat-API
// der steam_api64.dll, die das Spiel ohnehin geladen hat. Empfangen wird per Abfrage im Update.
//
// Wer gehört zur Sitzung? Kandidaten sind Steam-Freunde, die gerade Over the Hill spielen, und die
// Fusion-UserIds der Mitspieler (falls das Steam-IDs sind). Jeder Kandidat bekommt regelmäßig ein
// „Ping“ mit dem Sitzungsnamen; wer denselben Sitzungsnamen meldet, ist Partner.
// Fusion wird nur noch gelesen (Spielerliste, Namen, wer Host ist).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Il2Cpp;
using Il2CppFusion;
using MelonLoader;
using UnityEngine;

namespace Auftraege
{
    internal static class Netz
    {
        const int Magie = 0x4F544841;      // "OTHA"
        const byte Version = 2;
        const int Kanal = 0x4F54;          // eigener Steam-Kanal
        const uint AppId = 2929250;
        const byte TypPing = 0, TypDaten = 1;
        const float PingAbstandS = 8f, PartnerTimeoutS = 30f;

        public static Action<int, byte[]> Empfangen; // (Absender-Raw, Daten)

        /// Zählt hoch, sobald (wieder) ein Leiter gefunden wurde – dann schickt der Client sein Hallo.
        public static int LeiterVerbindungen { get; private set; }

        // ---------------- Fusion (nur lesend) ----------------

        public static NetworkRunner Runner
        {
            get
            {
                try
                {
                    var o = OverlandNetworkRunner.Instance;
                    if (o == null) return null;
                    var r = o.NetworkRunner;
                    return r != null && r.IsRunning ? r : null;
                }
                catch { return null; }
            }
        }

        public static bool IstEinzelspieler
        {
            get
            {
                try
                {
                    var o = OverlandNetworkRunner.Instance;
                    return o == null || o.IsSinglePlayer || Runner == null;
                }
                catch { return true; }
            }
        }

        /// Der „Leiter“ verwaltet die Aufträge: Host (Client/Server) bzw. Master-Client (Shared).
        public static bool IstLeiter
        {
            get
            {
                if (IstEinzelspieler) return true;
                var r = Runner;
                try { return r.IsServer || r.IsSharedModeMasterClient; } catch { return true; }
            }
        }

        public static int LokalerSpieler
        {
            get { try { return Runner?.LocalPlayer.Raw ?? 0; } catch { return 0; } }
        }

        /// Alle Spieler der Sitzung (Raw-IDs), inklusive des lokalen.
        public static List<int> AlleSpieler()
        {
            var liste = new List<int>();
            try
            {
                var o = OverlandNetworkRunner.Instance;
                if (o?.SpawnedCharacters != null)
                    foreach (var kv in o.SpawnedCharacters)
                        liste.Add(kv.Key.Raw);
            }
            catch { }
            int lokal = LokalerSpieler;
            if (!liste.Contains(lokal)) liste.Add(lokal);
            return liste;
        }

        public static string SpielerName(int raw)
        {
            try
            {
                var o = OverlandNetworkRunner.Instance;
                if (o?.spawnedCharactersInfo != null &&
                    o.spawnedCharactersInfo.TryGetValue(PlayerRef.FromRaw(raw), out var info) && info != null &&
                    !string.IsNullOrEmpty(info.PlayerName))
                    return info.PlayerName;
            }
            catch { }
            return raw == LokalerSpieler ? "Du" : "Spieler " + raw;
        }

        static int SitzungsKennung()
        {
            try
            {
                var name = Runner?.SessionInfo?.Name;
                if (string.IsNullOrEmpty(name)) return 0;
                unchecked
                {
                    int h = (int)2166136261;
                    foreach (char c in name) h = (h ^ c) * 16777619;
                    return h == 0 ? 1 : h;
                }
            }
            catch { return 0; }
        }

        // ---------------- Partner ----------------

        class Partner
        {
            public ulong SteamId;
            public int Raw;
            public bool Leiter;
            public float ZuletztGehoert;
        }

        static readonly Dictionary<ulong, Partner> _partner = new();

        // ---------------- Senden ----------------

        /// Nachricht an alle anderen Spieler (nur der Leiter sollte das nutzen).
        public static void AnAlle(byte[] daten)
        {
            if (!EmpfangAktiv) return;
            foreach (var p in _partner.Values.ToList()) Senden(p.SteamId, TypDaten, daten);
        }

        /// Nachricht an den Leiter. Ist man selbst Leiter, wird sie direkt verarbeitet.
        public static void AnLeiter(byte[] daten)
        {
            if (IstLeiter) { Empfangen?.Invoke(LokalerSpieler, daten); return; }
            if (!EmpfangAktiv) return;
            var l = _partner.Values.FirstOrDefault(p => p.Leiter);
            if (l == null) { MelonLogger.Warning("[Aufträge] Kein Host mit der Mod gefunden – Nachricht verworfen."); return; }
            Senden(l.SteamId, TypDaten, daten);
        }

        static int _sendeFehler;

        static void Senden(ulong an, byte typ, byte[] daten)
        {
            int sitzung = SitzungsKennung();
            if (sitzung == 0) return;
            var paket = new byte[14 + (daten?.Length ?? 0)];
            BitConverter.GetBytes(Magie).CopyTo(paket, 0);
            paket[4] = Version;
            paket[5] = typ;
            BitConverter.GetBytes(sitzung).CopyTo(paket, 6);
            BitConverter.GetBytes(LokalerSpieler).CopyTo(paket, 10);
            // Byte 14ff.: Daten; beim Ping stattdessen 1 Byte „bin Leiter“
            if (typ == TypPing) { Array.Resize(ref paket, 15); paket[14] = (byte)(IstLeiter ? 1 : 0); }
            else daten?.CopyTo(paket, 14);

            try
            {
                Marshal.Copy(new byte[IdentitaetGroesse], 0, _identitaet, IdentitaetGroesse);
                Steam.SteamAPI_SteamNetworkingIdentity_SetSteamID64(_identitaet, an);
                var h = GCHandle.Alloc(paket, GCHandleType.Pinned);
                int ergebnis;
                try
                {
                    ergebnis = Steam.SteamAPI_ISteamNetworkingMessages_SendMessageToUser(
                        _nachrichten, _identitaet, h.AddrOfPinnedObject(), (uint)paket.Length,
                        Steam.SendReliable | Steam.SendAutoRestart, Kanal);
                }
                finally { h.Free(); }
                if (ergebnis != 1 && _sendeFehler++ < 10)
                    MelonLogger.Warning($"[Aufträge] Steam-Senden fehlgeschlagen (EResult {ergebnis})");
            }
            catch (Exception e) { if (_sendeFehler++ < 10) MelonLogger.Warning("[Aufträge] Senden fehlgeschlagen: " + e.Message); }
        }

        // ---------------- Start / Abfrage ----------------

        const int IdentitaetGroesse = 136; // sizeof(SteamNetworkingIdentity)
        static IntPtr _nachrichten, _freunde, _identitaet, _puffer, _spielInfo;
        static ulong _eigeneId;
        static bool _fehlgeschlagen;
        static float _naechsterPing;
        static int _letzteSitzung;

        public static bool EmpfangAktiv { get; private set; }

        /// Einmalig, sobald man in einer Multiplayer-Sitzung ist. Kein Patch – nur Steam-Aufrufe.
        public static void Aktivieren()
        {
            if (EmpfangAktiv || _fehlgeschlagen) return;
            try
            {
                var user = Steam.SteamAPI_SteamUser_v023();
                _nachrichten = Steam.SteamAPI_SteamNetworkingMessages_SteamAPI_v002();
                _freunde = Steam.SteamAPI_SteamFriends_v018();
                if (user == IntPtr.Zero || _nachrichten == IntPtr.Zero)
                {
                    _fehlgeschlagen = true;
                    MelonLogger.Warning("[Aufträge] Steam nicht verfügbar – Konvoi-Modus aus (nur Steam-Version wird unterstützt).");
                    return;
                }
                _eigeneId = Steam.SteamAPI_ISteamUser_GetSteamID(user);
                try
                {
                    var utils = Steam.SteamAPI_SteamNetworkingUtils_SteamAPI_v004();
                    if (utils != IntPtr.Zero) Steam.SteamAPI_ISteamNetworkingUtils_InitRelayNetworkAccess(utils);
                }
                catch { }
                _identitaet = Marshal.AllocHGlobal(IdentitaetGroesse);
                _puffer = Marshal.AllocHGlobal(IntPtr.Size * 32);
                _spielInfo = Marshal.AllocHGlobal(64);
                EmpfangAktiv = true;
                MelonLogger.Msg("[Aufträge] Konvoi-Netz aktiv (Steam-P2P)");
            }
            catch (Exception e)
            {
                _fehlgeschlagen = true;
                MelonLogger.Warning("[Aufträge] Konvoi-Netz nicht möglich: " + e.Message);
            }
        }

        /// Jedes Frame: eingehende Nachrichten abholen, regelmäßig Partner suchen.
        public static void Tick()
        {
            if (!EmpfangAktiv) return;
            int sitzung = SitzungsKennung();
            if (sitzung != _letzteSitzung) { _partner.Clear(); _letzteSitzung = sitzung; _naechsterPing = 0; }
            if (sitzung == 0) return;

            Abholen(sitzung);

            float jetzt = Time.realtimeSinceStartup;
            foreach (var p in _partner.Values.Where(p => jetzt - p.ZuletztGehoert > PartnerTimeoutS).ToList())
            {
                _partner.Remove(p.SteamId);
                MelonLogger.Msg($"[Aufträge] Konvoi-Partner weg: {SpielerName(p.Raw)}");
            }

            if (jetzt >= _naechsterPing)
            {
                _naechsterPing = jetzt + PingAbstandS;
                foreach (ulong id in Kandidaten()) Senden(id, TypPing, null);
            }
        }

        static void Abholen(int sitzung)
        {
            for (int runde = 0; runde < 8; runde++)
            {
                int n;
                try { n = Steam.SteamAPI_ISteamNetworkingMessages_ReceiveMessagesOnChannel(_nachrichten, Kanal, _puffer, 32); }
                catch (Exception e) { MelonLogger.Warning("[Aufträge] Empfang fehlgeschlagen: " + e.Message); EmpfangAktiv = false; return; }
                if (n <= 0) return;
                for (int i = 0; i < n; i++)
                {
                    var msg = Marshal.ReadIntPtr(_puffer, i * IntPtr.Size);
                    if (msg == IntPtr.Zero) continue;
                    try
                    {
                        // SteamNetworkingMessage_t: m_pData @0, m_cbSize @8, m_identityPeer @16
                        var daten = Marshal.ReadIntPtr(msg, 0);
                        int groesse = Marshal.ReadInt32(msg, 8);
                        ulong von = Steam.SteamAPI_SteamNetworkingIdentity_GetSteamID64(msg + 16);
                        if (groesse >= 14 && groesse < 1 << 20 && daten != IntPtr.Zero)
                        {
                            var roh = new byte[groesse];
                            Marshal.Copy(daten, roh, 0, groesse);
                            Verarbeiten(von, roh, sitzung);
                        }
                    }
                    catch (Exception e) { MelonLogger.Warning("[Aufträge] Empfangsfehler: " + e.Message); }
                    finally { try { Steam.SteamAPI_SteamNetworkingMessage_t_Release(msg); } catch { } }
                }
                if (n < 32) return;
            }
        }

        static void Verarbeiten(ulong von, byte[] roh, int sitzung)
        {
            if (BitConverter.ToInt32(roh, 0) != Magie) return;
            if (roh[4] != Version)
            {
                MelonLogger.Warning("[Aufträge] Mitspieler hat eine andere Mod-Version – bitte beide dieselbe Auftraege.dll nutzen.");
                return;
            }
            if (BitConverter.ToInt32(roh, 6) != sitzung || von == _eigeneId) return;
            byte typ = roh[5];
            int raw = BitConverter.ToInt32(roh, 10);

            bool neu = !_partner.TryGetValue(von, out var p);
            if (neu) p = _partner[von] = new Partner { SteamId = von };
            p.Raw = raw;
            p.ZuletztGehoert = Time.realtimeSinceStartup;

            if (typ == TypPing)
            {
                bool warLeiter = p.Leiter;
                p.Leiter = roh.Length > 14 && roh[14] == 1;
                if (neu)
                {
                    MelonLogger.Msg($"[Aufträge] Konvoi-Partner gefunden: {SpielerName(raw)}{(p.Leiter ? " (Host)" : "")}");
                    Senden(von, TypPing, null); // sofort antworten, damit die Gegenseite uns auch kennt
                }
                if (p.Leiter && (!warLeiter || neu)) LeiterVerbindungen++;
                return;
            }

            if (typ == TypDaten)
            {
                var daten = new byte[roh.Length - 14];
                Array.Copy(roh, 14, daten, 0, daten.Length);
                Empfangen?.Invoke(raw, daten);
            }
        }

        /// Steam-IDs, die zur Sitzung gehören könnten.
        static IEnumerable<ulong> Kandidaten()
        {
            var ids = new HashSet<ulong>(_partner.Keys);
            // 1) Fusion-UserIds der Mitspieler (falls Steam-IDs)
            try
            {
                var r = Runner;
                if (r != null)
                    foreach (var kv in OverlandNetworkRunner.Instance.SpawnedCharacters)
                    {
                        string uid = null;
                        try { uid = r.GetPlayerUserId(kv.Key); } catch { }
                        if (ulong.TryParse(uid, out ulong s) && s > 76561197960265728UL) ids.Add(s);
                    }
            }
            catch { }
            // 2) Steam-Freunde, die gerade Over the Hill spielen
            try
            {
                if (_freunde != IntPtr.Zero)
                {
                    int n = Steam.SteamAPI_ISteamFriends_GetFriendCount(_freunde, Steam.FreundeDirekt);
                    for (int i = 0; i < n && i < 2000; i++)
                    {
                        ulong f = Steam.SteamAPI_ISteamFriends_GetFriendByIndex(_freunde, i, Steam.FreundeDirekt);
                        if (f == 0) continue;
                        Marshal.Copy(new byte[64], 0, _spielInfo, 64);
                        if (Steam.SteamAPI_ISteamFriends_GetFriendGamePlayed(_freunde, f, _spielInfo) &&
                            ((ulong)Marshal.ReadInt64(_spielInfo, 0) & 0xFFFFFF) == AppId)
                            ids.Add(f);
                    }
                }
            }
            catch { }
            ids.Remove(_eigeneId);
            return ids;
        }

        // ---------------- Steam-Flat-API ----------------

        static class Steam
        {
            const string Dll = "steam_api64";
            public const int SendReliable = 8, SendAutoRestart = 32;
            public const int FreundeDirekt = 4; // k_EFriendFlagImmediate

            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SteamAPI_SteamUser_v023();
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern ulong SteamAPI_ISteamUser_GetSteamID(IntPtr self);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SteamAPI_SteamFriends_v018();
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int SteamAPI_ISteamFriends_GetFriendCount(IntPtr self, int flags);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern ulong SteamAPI_ISteamFriends_GetFriendByIndex(IntPtr self, int index, int flags);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.U1)]
            public static extern bool SteamAPI_ISteamFriends_GetFriendGamePlayed(IntPtr self, ulong freund, IntPtr info);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SteamAPI_SteamNetworkingMessages_SteamAPI_v002();
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SteamAPI_SteamNetworkingUtils_SteamAPI_v004();
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void SteamAPI_ISteamNetworkingUtils_InitRelayNetworkAccess(IntPtr self);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void SteamAPI_SteamNetworkingIdentity_SetSteamID64(IntPtr identitaet, ulong id);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern ulong SteamAPI_SteamNetworkingIdentity_GetSteamID64(IntPtr identitaet);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern int SteamAPI_ISteamNetworkingMessages_SendMessageToUser(IntPtr self, IntPtr identitaet, IntPtr daten, uint groesse, int flags, int kanal);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern int SteamAPI_ISteamNetworkingMessages_ReceiveMessagesOnChannel(IntPtr self, int kanal, IntPtr ausgabe, int max);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void SteamAPI_SteamNetworkingMessage_t_Release(IntPtr msg);
        }
    }
}
