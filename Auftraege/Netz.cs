// Netzwerk-Transport über den „Reliable Data“-Kanal von Photon Fusion.
// Senden: NetworkRunner.SendReliableDataToPlayer / SendReliableDataToServer.
// Empfangen: Harmony-Prefix auf NetworkRunner.OnReliableData – Pakete mit unserem
// Schlüssel werden abgefangen und nicht an das Spiel weitergereicht.

using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using Il2CppFusion;
using Il2CppFusion.Sockets;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;

namespace Auftraege
{
    internal static class Netz
    {
        // Kennung unserer Pakete: "OTHA" + Protokollversion
        const int Key0 = 0x4F544841;
        const int Key1 = 1;

        public static Action<int, byte[]> Empfangen; // (Absender-Raw, Daten)

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

        static ReliableKey Schluessel => ReliableKey.FromInts(Key0, Key1, 0, 0);

        // Jede Nachricht bekommt eine zufällige Kennung vorangestellt, damit sie auch dann nur
        // einmal verarbeitet wird, wenn mehrere Empfänger-Objekte sie gemeldet bekommen.
        static readonly System.Random _rnd = new();
        static readonly HashSet<int> _gesehen = new();
        static readonly Queue<int> _gesehenReihe = new();

        static Il2CppSystem.ReadOnlySpan<byte> AlsSpan(byte[] daten)
        {
            var mitKopf = new byte[daten.Length + 4];
            BitConverter.GetBytes(_rnd.Next()).CopyTo(mitKopf, 0);
            daten.CopyTo(mitKopf, 4);
            return new Il2CppSystem.ReadOnlySpan<byte>(new Il2CppStructArray<byte>(mitKopf));
        }

        /// Nachricht an alle anderen Spieler (nur der Leiter sollte das nutzen).
        public static void AnAlle(byte[] daten)
        {
            var r = Runner;
            if (r == null || !EmpfangAktiv) return;
            int lokal = LokalerSpieler;
            foreach (int p in AlleSpieler())
            {
                if (p == lokal) continue;
                try { r.SendReliableDataToPlayer(PlayerRef.FromRaw(p), Schluessel, AlsSpan(daten)); }
                catch (Exception e) { MelonLogger.Warning($"[Aufträge] Senden an {p} fehlgeschlagen: {e.Message}"); }
            }
        }

        /// Nachricht an den Leiter. Ist man selbst Leiter, wird sie direkt verarbeitet.
        public static void AnLeiter(byte[] daten)
        {
            if (IstLeiter) { Empfangen?.Invoke(LokalerSpieler, daten); return; }
            var r = Runner;
            if (r == null || !EmpfangAktiv) return;
            try
            {
                if (r.Topology == Topologies.ClientServer)
                    r.SendReliableDataToServer(Schluessel, AlsSpan(daten));
                else
                    AnAlle(daten); // Shared-Modus: Master-Client filtert selbst
            }
            catch (Exception e) { MelonLogger.Warning("[Aufträge] Senden an Leiter fehlgeschlagen: " + e.Message); }
        }

        // ---------------- Empfang ----------------
        // v0.1.0 hatte NetworkRunner.OnReliableData gepatcht – das hat das Laden der Welt blockiert.
        // Jetzt: Prefix auf den Empfänger VehicleInput (ein Spielobjekt, das den Kanal selbst nicht nutzt),
        // und erst, wenn man in einer Multiplayer-Sitzung ist und die Welt geladen ist.

        public static bool EmpfangAktiv { get; private set; }

        public static void EmpfangAktivieren(HarmonyLib.Harmony harmony)
        {
            if (EmpfangAktiv) return;
            try
            {
                var ziel = AccessTools.Method(typeof(VehicleInput), nameof(VehicleInput.Fusion_INetworkRunnerCallbacks_OnReliableDataReceived));
                var prefix = AccessTools.Method(typeof(EmpfangsPatch), nameof(EmpfangsPatch.Prefix));
                harmony.Patch(ziel, prefix: new HarmonyMethod(prefix));
                EmpfangAktiv = true;
                MelonLogger.Msg("[Aufträge] Multiplayer-Empfang aktiv");
            }
            catch (Exception e) { MelonLogger.Warning("[Aufträge] Multiplayer-Empfang nicht möglich: " + e.Message); }
        }

        internal static class EmpfangsPatch
        {
            internal static bool Prefix(NetworkRunner runner, PlayerRef player, ReliableKey key, Il2CppSystem.ReadOnlySpan<byte> data)
            {
                try
                {
                    key.GetInts(out int k0, out int k1, out _, out _);
                    if (k0 != Key0) return true; // nicht unser Paket -> normal weiter
                    int n = data.Length;
                    if (k1 != Key1 || n < 5) return false;
                    var roh = new byte[n];
                    for (int i = 0; i < n; i++) roh[i] = data[i];
                    int kennung = BitConverter.ToInt32(roh, 0);
                    if (!_gesehen.Add(kennung)) return false;
                    _gesehenReihe.Enqueue(kennung);
                    if (_gesehenReihe.Count > 512) _gesehen.Remove(_gesehenReihe.Dequeue());
                    var daten = new byte[n - 4];
                    Array.Copy(roh, 4, daten, 0, n - 4);
                    Empfangen?.Invoke(player.Raw, daten);
                }
                catch (Exception e) { MelonLogger.Warning("[Aufträge] Empfangsfehler: " + e.Message); }
                return false;
            }
        }
    }
}
