using System;

namespace MM.Core
{
    // Match tuning values. Defaults live here; the dedicated server overrides them from the
    // command line (see CommandLineArgs) so the AWS side can tweak a match without a rebuild.
    public sealed class GameConfig
    {
        public const int MaxPlayers = 2;

        public float MatchSeconds { get; internal set; } = 15f;
        public float CountdownSeconds { get; internal set; } = 3f;
        public int MaxPressesPerSecond { get; internal set; } = 20;
        public float PostMatchSeconds { get; internal set; } = 5f;
        // Used from week 5, when a server waits for its matched players before cancelling the match.
        public float JoinTimeoutSeconds { get; internal set; } = 30f;

        public string ServerIp { get; internal set; } = "127.0.0.1";
        public ushort Port { get; internal set; } = 7777;
        public bool ForceServer { get; internal set; }

        static GameConfig s_Current;

        // Read once per process: command line args cannot change after launch.
        public static GameConfig Current => s_Current ??= FromCommandLine(Environment.GetCommandLineArgs());

        public static GameConfig FromCommandLine(string[] args)
        {
            var config = new GameConfig();
            CommandLineArgs.ApplyTo(config, args);
            return config;
        }
    }
}
