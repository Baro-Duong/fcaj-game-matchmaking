using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace MM.EditorTools
{
    // Editor only. Menu MM/Build/* for people; the same static methods run headless through
    //   Unity.exe -batchmode -quit -projectPath unity -executeMethod MM.EditorTools.BuildScript.BuildLinuxServer
    // The previous build is parked in Builds/_previous during a build and restored if the build fails,
    // so a failed build never destroys the last good one.
    public static class BuildScript
    {
        // Relative to the Unity project folder (unity/). Fixed names: the AWS User Data script runs Server.x86_64.
        public const string LinuxServerDir = "Builds/LinuxServer";
        public const string LinuxServerExe = "Server.x86_64";
        public const string WindowsClientDir = "Builds/WindowsClient";
        public const string WindowsClientExe = "Client.exe";
        public const string BuildInfoFile = "build-info.txt";
        const string k_PreviousRoot = "Builds/_previous";

        [MenuItem("MM/Build/Linux Server")]
        public static void BuildLinuxServer() =>
            Build("Linux Server", BuildTarget.StandaloneLinux64, StandaloneBuildSubtarget.Server, LinuxServerDir, LinuxServerExe);

        [MenuItem("MM/Build/Windows Client")]
        public static void BuildWindowsClient() =>
            Build("Windows Client", BuildTarget.StandaloneWindows64, StandaloneBuildSubtarget.Player, WindowsClientDir, WindowsClientExe);

        static void Build(string label, BuildTarget target, StandaloneBuildSubtarget subtarget, string outputDir, string exeName)
        {
            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                Fail(label, "no enabled scene in the scene list (File > Build Profiles > Scene List)");
                return;
            }

            // Park the last good build instead of building to a staging folder and moving it afterwards:
            // right after a build, Windows Defender is still scanning the fresh binaries and a move fails
            // with "Access denied". The old build was scanned long ago, so moving it is safe.
            string previousDir = Path.Combine(k_PreviousRoot, Path.GetFileName(outputDir));
            if (!TryIo(() => DeleteDirectory(previousDir), $"clear {previousDir}"))
            {
                Fail(label, $"could not clear {previousDir}");
                return;
            }

            bool hasPrevious = Directory.Exists(outputDir);
            if (hasPrevious)
            {
                Directory.CreateDirectory(k_PreviousRoot);
                if (!TryIo(() => Directory.Move(outputDir, previousDir), $"park {outputDir}"))
                {
                    Fail(label, $"could not move the old build out of {outputDir}. Close any running copy of it and try again.");
                    return;
                }
            }

            BuildTarget previousTarget = EditorUserBuildSettings.activeBuildTarget;
            StandaloneBuildSubtarget previousSubtarget = EditorUserBuildSettings.standaloneBuildSubtarget;

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(outputDir, exeName),
                target = target,
                subtarget = (int)subtarget,   // Server = headless Dedicated Server player, compiled with UNITY_SERVER
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };

            Debug.Log($"[MM] Building {label} from {scenes.Length} scene(s); editor target is {previousTarget} ({previousSubtarget})");
            BuildReport report = BuildPipeline.BuildPlayer(options);
            RestoreActiveTarget(previousTarget, previousSubtarget);

            BuildSummary summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                TryIo(() => DeleteDirectory(outputDir), $"remove the partial build in {outputDir}");
                bool restored = hasPrevious && TryIo(() => Directory.Move(previousDir, outputDir), $"restore {outputDir}");
                string kept = !hasPrevious ? "No previous build to keep."
                    : restored ? $"Previous build restored in {outputDir}."
                    : $"Previous build is in {previousDir}.";
                Fail(label, $"{summary.result} with {summary.totalErrors} error(s), see the log above. {kept}");
                return;
            }

            WriteBuildInfo(outputDir, label);
            // Best effort: a leftover is cleared by the next build anyway.
            TryIo(() => DeleteDirectory(previousDir), $"delete the old build in {previousDir}");

            string exePath = Path.GetFullPath(Path.Combine(outputDir, exeName));
            Debug.Log($"[MM] {label} build OK: {exePath} ({summary.totalSize / (1024 * 1024)} MB, {summary.totalTime.TotalSeconds:F0}s)");
        }

        // Leaving the editor on "Dedicated Server" would define UNITY_SERVER in Play Mode
        // (the menu hides and a server auto-starts), so switch back if building moved the active target.
        // Batch mode too: the active target is saved in Library/ and the next interactive Editor session opens with it.
        static void RestoreActiveTarget(BuildTarget target, StandaloneBuildSubtarget subtarget)
        {
            if (EditorUserBuildSettings.activeBuildTarget == target && EditorUserBuildSettings.standaloneBuildSubtarget == subtarget) return;

            EditorUserBuildSettings.standaloneBuildSubtarget = subtarget;
            NamedBuildTarget named = subtarget == StandaloneBuildSubtarget.Server
                ? NamedBuildTarget.Server
                : NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(target));
            EditorUserBuildSettings.SwitchActiveBuildTarget(named, target);
            Debug.Log($"[MM] Switched the editor back to {target} ({subtarget})");
        }

        // key=value lines so shell scripts can read it (tools/build-server.ps1 takes the version from here).
        static void WriteBuildInfo(string dir, string label)
        {
            string commit = RunGit("rev-parse --short HEAD") ?? "unknown";
            string status = RunGit("--no-optional-locks status --porcelain");
            string dirty = status == null ? "unknown" : status.Length > 0 ? "true" : "false";

            string[] lines =
            {
                $"product={PlayerSettings.productName}",
                $"version={PlayerSettings.bundleVersion}",
                $"target={label}",
                $"commit={commit}",
                $"dirty={dirty}",
                $"unity={Application.unityVersion}",
                $"built_at_utc={DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}",
            };
            // LF endings: the file is read on Linux too.
            File.WriteAllText(Path.Combine(dir, BuildInfoFile), string.Join("\n", lines) + "\n");
        }

        // Returns trimmed stdout, or null when git is missing or fails; the build itself must not depend on git.
        static string RunGit(string arguments)
        {
            try
            {
                var startInfo = new ProcessStartInfo("git", arguments)
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Directory.GetCurrentDirectory(),
                };
                using var process = Process.Start(startInfo);
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(10000);
                return process.ExitCode == 0 ? output.Trim() : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        static void DeleteDirectory(string dir)
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }

        // File operations on Windows fail briefly while an antivirus or indexer holds a handle; retry ~2 s.
        static bool TryIo(Action action, string what)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    action();
                    return true;
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    if (attempt == 5)
                    {
                        Debug.LogWarning($"[MM] Could not {what}: {e.Message}");
                        return false;
                    }
                    Thread.Sleep(500);
                }
            }
        }

        static void Fail(string label, string message)
        {
            Debug.LogError($"[MM] {label} build FAILED: {message}");
            // Non-zero exit code so build-server.ps1 / CI stops instead of zipping a stale build.
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }
}
