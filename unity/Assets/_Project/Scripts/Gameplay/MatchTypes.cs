namespace MM.Gameplay
{
    // Synced through NetworkVariable, so keep it a byte enum (1 byte on the wire).
    public enum MatchState : byte
    {
        WaitingForPlayers,
        Countdown,
        Playing,
        Ended
    }

    public enum MatchEndReason : byte
    {
        None,
        TimeUp,
        OpponentLeft
    }

    // Snapshot of a finished match. Slot 0 = "p1", slot 1 = "p2", matching POST /server/result.
    public readonly struct MatchResult
    {
        public const int Draw = -1;

        public readonly int P1Score;
        public readonly int P2Score;
        public readonly int WinnerSlot;
        public readonly MatchEndReason Reason;

        public MatchResult(int p1Score, int p2Score, int winnerSlot, MatchEndReason reason)
        {
            P1Score = p1Score;
            P2Score = p2Score;
            WinnerSlot = winnerSlot;
            Reason = reason;
        }

        public string WinnerLabel => WinnerSlot == 0 ? "p1" : WinnerSlot == 1 ? "p2" : "draw";
    }
}
