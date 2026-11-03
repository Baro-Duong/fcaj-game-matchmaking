using System;
using MM.Gameplay;
using MM.Net;
using TMPro;
using UnityEngine;

namespace MM.UI
{
    // Client-side HUD: both scores, the timer and the match message. It only reads synced state;
    // texts are rebuilt only when a displayed value changes, so there is no per-frame string garbage.
    public sealed class MatchHud : MonoBehaviour
    {
        [SerializeField] GameObject m_Root;
        [SerializeField] TMP_Text m_LeftScoreText;
        [SerializeField] TMP_Text m_RightScoreText;
        [SerializeField] TMP_Text m_TimerText;
        [SerializeField] TMP_Text m_MessageText;

        // Everything the texts depend on; compared each frame to skip redundant rebuilds.
        struct View : IEquatable<View>
        {
            public MatchState State;
            public int Seconds;
            public int LocalSlot;
            public bool LeftPresent, RightPresent;
            public int LeftScore, RightScore;
            public int WinnerSlot;
            public MatchEndReason Reason;

            public bool Equals(View o) =>
                State == o.State && Seconds == o.Seconds && LocalSlot == o.LocalSlot &&
                LeftPresent == o.LeftPresent && RightPresent == o.RightPresent &&
                LeftScore == o.LeftScore && RightScore == o.RightScore &&
                WinnerSlot == o.WinnerSlot && Reason == o.Reason;
        }

        View m_LastView;
        bool m_HasView;

        void Awake()
        {
            if (NetworkBootstrap.IsDedicatedServerBuild)
            {
                gameObject.SetActive(false);
                return;
            }

            m_LeftScoreText.color = PlayerPresser.ColorOf(0);
            m_RightScoreText.color = PlayerPresser.ColorOf(1);
            m_Root.SetActive(false);
        }

        void Update()
        {
            var match = MatchManager.Instance;
            bool visible = match != null && match.IsSpawned;
            if (m_Root.activeSelf != visible) m_Root.SetActive(visible);
            if (!visible)
            {
                m_HasView = false;
                return;
            }

            View view = BuildView(match);
            if (m_HasView && view.Equals(m_LastView)) return;

            m_LastView = view;
            m_HasView = true;
            Render(view);
        }

        static View BuildView(MatchManager match)
        {
            var view = new View
            {
                State = match.State,
                Seconds = (int)Math.Ceiling(match.SecondsRemaining),
                LocalSlot = PlayerPresser.NoSlot,
                WinnerSlot = match.WinnerSlot,
                Reason = match.EndReason
            };

            foreach (var player in PlayerPresser.All)
            {
                if (player.IsOwner) view.LocalSlot = player.Slot;
                if (player.Slot == 0)
                {
                    view.LeftPresent = true;
                    view.LeftScore = player.Presses;
                }
                else if (player.Slot == 1)
                {
                    view.RightPresent = true;
                    view.RightScore = player.Presses;
                }
            }

            return view;
        }

        void Render(View v)
        {
            m_LeftScoreText.text = ScoreText(0, v.LeftPresent, v.LeftScore, v.LocalSlot);
            m_RightScoreText.text = ScoreText(1, v.RightPresent, v.RightScore, v.LocalSlot);
            m_TimerText.text = v.State == MatchState.Playing ? v.Seconds.ToString() : "";
            m_MessageText.text = Message(v);
        }

        static string ScoreText(int slot, bool present, int score, int localSlot)
        {
            if (!present) return "<size=60%>waiting...</size>";
            return $"<size=60%>{SlotLabel(slot, localSlot)}</size>\n{score}";
        }

        // "YOU" / "OPPONENT" for players; "P1" / "P2" when watching as host-less server.
        static string SlotLabel(int slot, int localSlot)
        {
            if (localSlot == PlayerPresser.NoSlot) return $"P{slot + 1}";
            return slot == localSlot ? "YOU" : "OPPONENT";
        }

        static string Message(View v)
        {
            switch (v.State)
            {
                case MatchState.WaitingForPlayers:
                    return v.LocalSlot == PlayerPresser.NoSlot ? "Waiting for players..." : "Waiting for opponent...";

                case MatchState.Countdown:
                    return Math.Max(1, v.Seconds).ToString();

                case MatchState.Playing:
                    return "PRESS SPACE!";

                case MatchState.Ended:
                    return $"{ResultText(v)}\n<size=50%>Next match in {v.Seconds}s</size>";

                default:
                    return "";
            }
        }

        static string ResultText(View v)
        {
            if (v.WinnerSlot == MatchResult.Draw) return "DRAW";

            string winner = v.LocalSlot == PlayerPresser.NoSlot ? $"P{v.WinnerSlot + 1} WINS"
                : v.WinnerSlot == v.LocalSlot ? "YOU WIN!"
                : "YOU LOSE";

            return v.Reason == MatchEndReason.OpponentLeft ? $"{winner}\n<size=50%>(opponent left)</size>" : winner;
        }
    }
}
