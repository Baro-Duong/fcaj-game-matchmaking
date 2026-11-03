using System.Globalization;
using UnityEngine;

namespace MM.Core
{
    // Parses the flags the AWS side passes when launching the server, for example:
    //   ./Server.x86_64 -port 7777 -matchSeconds 15 -maxPps 20 -logFile -
    // Unknown flags (Unity's own -batchmode, -logFile...) are ignored.
    public static class CommandLineArgs
    {
        public static void ApplyTo(GameConfig config, string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                string flag = args[i].ToLowerInvariant();
                string value = i + 1 < args.Length ? args[i + 1] : null;

                switch (flag)
                {
                    case "-server":
                        config.ForceServer = true;
                        break;

                    case "-port":
                        if (ushort.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort port) && port > 0)
                        {
                            config.Port = port;
                            i++;
                        }
                        else Warn(flag, value);
                        break;

                    case "-ip":
                        if (!string.IsNullOrWhiteSpace(value) && !value.StartsWith("-"))
                        {
                            config.ServerIp = value;
                            i++;
                        }
                        else Warn(flag, value);
                        break;

                    case "-matchseconds":
                        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float seconds) && seconds > 0f)
                        {
                            config.MatchSeconds = seconds;
                            i++;
                        }
                        else Warn(flag, value);
                        break;

                    case "-maxpps":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int pps) && pps > 0)
                        {
                            config.MaxPressesPerSecond = pps;
                            i++;
                        }
                        else Warn(flag, value);
                        break;
                }
            }
        }

        static void Warn(string flag, string value)
        {
            Debug.LogWarning($"[MM] Ignoring {flag} with invalid value '{value}', keeping default");
        }
    }
}
