using System;
using MM.Core;
using Unity.Netcode;
using UnityEngine;

namespace MM.Gameplay
{
    // In-scene NetworkObject that runs the match: WaitingForPlayers -> Countdown -> Playing -> Ended -> WaitingForPlayers.
    // Every decision (timing, accepting presses, winner) happens on the server; clients only read
    // the NetworkVariables to draw the HUD.
    public sealed class MatchManager : NetworkBehaviour
    {
        public static MatchManager Instance { get; private set; }

        readonly NetworkVariable<MatchState> m_State = new NetworkVariable<MatchState>(MatchState.WaitingForPlayers);
        // Server time (NetworkManager.ServerTime) at which the current countdown / match / result screen ends.
        // Clients derive the remaining time from it instead of receiving a clock value every frame.
        readonly NetworkVariable<double> m_PhaseEndTime = new NetworkVariable<double>();
        readonly NetworkVariable<int> m_WinnerSlot = new NetworkVariable<int>(MatchResult.Draw);
        readonly NetworkVariable<MatchEndReason> m_EndReason = new NetworkVariable<MatchEndReason>(MatchEndReason.None);

        // Server-only bookkeeping, indexed by slot (0 = P1, 1 = P2).
        readonly PlayerPresser[] m_Slots = new PlayerPresser[GameConfig.MaxPlayers];
        // Scores live here too so the final result survives a player object despawning mid-match.
        readonly int[] m_Scores = new int[GameConfig.MaxPlayers];
        readonly int[] m_DroppedPresses = new int[GameConfig.MaxPlayers];
        readonly double[] m_LastRateWarning = new double[GameConfig.MaxPlayers];
        PressRateLimiter[] m_Limiters;
        GameConfig m_Config;

        public MatchState State => m_State.Value;
        public int WinnerSlot => m_WinnerSlot.Value;
        public MatchEndReason EndReason => m_EndReason.Value;

        // Raised on the server when a match finishes; week 5 hooks POST /server/result here.
        public event Action<MatchResult> ServerMatchEnded;

        public double SecondsRemaining =>
            IsSpawned ? Math.Max(0.0, m_PhaseEndTime.Value - NetworkManager.ServerTime.Time) : 0.0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        void Awake()
        {
            Instance = this;
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;

            m_Config = GameConfig.Current;
            m_Limiters = new PressRateLimiter[GameConfig.MaxPlayers];
            for (int i = 0; i < m_Limiters.Length; i++)
                m_Limiters[i] = new PressRateLimiter(m_Config.MaxPressesPerSecond);

            // In-scene objects are reused across Shutdown/Start, so clear what the previous session left behind
            // (ServerTime also restarts from 0, which would make an old phase end time wait far too long).
            Array.Clear(m_Slots, 0, m_Slots.Length);
            Array.Clear(m_Scores, 0, m_Scores.Length);
            m_State.Value = MatchState.WaitingForPlayers;
            m_PhaseEndTime.Value = 0.0;
            m_WinnerSlot.Value = MatchResult.Draw;
            m_EndReason.Value = MatchEndReason.None;

            // The host's player object can spawn before this in-scene object, so pick up anyone already here.
            foreach (var player in PlayerPresser.All)
                ServerRegisterPlayer(player);

            Debug.Log($"[MM] Match config: {m_Config.MatchSeconds}s, max {m_Config.MaxPressesPerSecond} presses/s");
        }

        void Update()
        {
            if (!IsServer || !IsSpawned) return;

            double now = NetworkManager.ServerTime.Time;
            switch (m_State.Value)
            {
                case MatchState.WaitingForPlayers:
                    if (PlayerCount == GameConfig.MaxPlayers) ServerStartCountdown(now);
                    break;

                case MatchState.Countdown:
                    if (now >= m_PhaseEndTime.Value) ServerStartPlaying(now);
                    break;

                case MatchState.Playing:
                    if (now >= m_PhaseEndTime.Value) ServerEndMatch(MatchEndReason.TimeUp, WinnerByScore());
                    break;

                case MatchState.Ended:
                    if (now >= m_PhaseEndTime.Value) ServerResetToWaiting();
                    break;
            }
        }

        internal void ServerRegisterPlayer(PlayerPresser player)
        {
            // Not spawned yet: OnNetworkSpawn will register this player.
            if (!IsServer || !IsSpawned) return;
            if (Array.IndexOf(m_Slots, player) >= 0) return;

            int slot = Array.IndexOf(m_Slots, null);
            if (slot < 0)
            {
                Debug.LogWarning($"[MM] No free slot for client {player.OwnerClientId}");
                return;
            }

            m_Slots[slot] = player;
            player.ServerAssignSlot(slot);
            Debug.Log($"[MM] Client {player.OwnerClientId} joined as P{slot + 1}");
        }

        internal void ServerUnregisterPlayer(PlayerPresser player)
        {
            int slot = Array.IndexOf(m_Slots, player);
            if (slot < 0) return;

            m_Slots[slot] = null;
            Debug.Log($"[MM] Client {player.OwnerClientId} left slot P{slot + 1}");

            // During shutdown everything despawns; there is no match left to update.
            if (!IsSpawned || NetworkManager == null || NetworkManager.ShutdownInProgress) return;

            switch (m_State.Value)
            {
                case MatchState.Countdown:
                    ServerResetToWaiting();
                    break;

                case MatchState.Playing:
                    int other = 1 - slot;
                    ServerEndMatch(MatchEndReason.OpponentLeft, m_Slots[other] != null ? other : MatchResult.Draw);
                    break;
            }
        }

        internal void ServerTryRegisterPress(PlayerPresser player)
        {
            if (m_State.Value != MatchState.Playing) return;
            // A press that arrives after the buzzer but before Update ends the match does not count.
            if (NetworkManager.ServerTime.Time >= m_PhaseEndTime.Value) return;

            int slot = Array.IndexOf(m_Slots, player);
            if (slot < 0) return;

            double now = Time.realtimeSinceStartupAsDouble;
            if (!m_Limiters[slot].TryAccept(now))
            {
                ReportDroppedPress(slot, player, now);
                return;
            }

            m_Scores[slot]++;
            player.ServerSetPresses(m_Scores[slot]);
        }

        int PlayerCount
        {
            get
            {
                int count = 0;
                foreach (var player in m_Slots)
                    if (player != null) count++;
                return count;
            }
        }

        int WinnerByScore()
        {
            if (m_Scores[0] > m_Scores[1]) return 0;
            if (m_Scores[1] > m_Scores[0]) return 1;
            return MatchResult.Draw;
        }

        void ServerStartCountdown(double now)
        {
            for (int slot = 0; slot < m_Slots.Length; slot++)
            {
                m_Scores[slot] = 0;
                m_DroppedPresses[slot] = 0;
                m_LastRateWarning[slot] = double.NegativeInfinity;
                m_Limiters[slot].Reset();
                m_Slots[slot].ServerSetPresses(0);
            }

            m_WinnerSlot.Value = MatchResult.Draw;
            m_EndReason.Value = MatchEndReason.None;
            m_PhaseEndTime.Value = now + m_Config.CountdownSeconds;
            m_State.Value = MatchState.Countdown;
            Debug.Log("[MM] Countdown started");
        }

        void ServerStartPlaying(double now)
        {
            m_PhaseEndTime.Value = now + m_Config.MatchSeconds;
            m_State.Value = MatchState.Playing;
            Debug.Log($"[MM] Match started ({m_Config.MatchSeconds}s)");
        }

        void ServerEndMatch(MatchEndReason reason, int winnerSlot)
        {
            var result = new MatchResult(m_Scores[0], m_Scores[1], winnerSlot, reason);

            m_WinnerSlot.Value = winnerSlot;
            m_EndReason.Value = reason;
            m_PhaseEndTime.Value = NetworkManager.ServerTime.Time + m_Config.PostMatchSeconds;
            m_State.Value = MatchState.Ended;

            Debug.Log($"[MM] Match ended winner={result.WinnerLabel} reason={reason} " +
                      $"scores=p1:{result.P1Score} p2:{result.P2Score} " +
                      $"dropped=p1:{m_DroppedPresses[0]} p2:{m_DroppedPresses[1]}");
            ServerMatchEnded?.Invoke(result);
        }

        void ServerResetToWaiting()
        {
            m_State.Value = MatchState.WaitingForPlayers;
            Debug.Log($"[MM] Waiting for players ({PlayerCount}/{GameConfig.MaxPlayers})");
        }

        void ReportDroppedPress(int slot, PlayerPresser player, double now)
        {
            m_DroppedPresses[slot]++;

            // At most one warning per second per player, so a macro cannot flood the server log.
            if (now - m_LastRateWarning[slot] < 1.0) return;
            m_LastRateWarning[slot] = now;
            Debug.LogWarning($"[MM] Rate limit: client {player.OwnerClientId} (P{slot + 1}) is over " +
                             $"{m_Config.MaxPressesPerSecond} presses/s, dropped {m_DroppedPresses[slot]} so far");
        }
    }
}
