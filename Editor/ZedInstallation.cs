using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using IOPath = System.IO.Path;

namespace Zed.Unity.Editor {
    internal class ZedInstallation : EditorInstallation {
        private static readonly IGenerator _generator = GeneratorFactory.GetInstance(GeneratorStyle.SDK);

        public override bool SupportsAnalyzers => true;

        public override Version LatestLanguageVersionSupported => new Version(13, 0);

        public override string[] GetAnalyzers() {
            // Zed uses rust-analyzer for Rust and tree-sitter for syntax
            // For C#, it relies on OmniSharp or similar language servers
            return Array.Empty<string>();
        }

        public override IGenerator ProjectGenerator => _generator;

        private static bool IsCandidateForDiscovery(string path) {
            if (string.IsNullOrEmpty(path))
                return false;

#if UNITY_EDITOR_OSX
			return Directory.Exists(path) && Regex.IsMatch(path, ".*Zed.*.app$", RegexOptions.IgnoreCase);
#elif UNITY_EDITOR_WIN
            return File.Exists(path) && Regex.IsMatch(path, ".*[Zz]ed.*.exe$", RegexOptions.IgnoreCase);
#else
            // Linux
            return File.Exists(path) && (
                path.EndsWith("zed", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith("zedit", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith("zeditor", StringComparison.OrdinalIgnoreCase)
            );
#endif
        }

        public static bool TryDiscoverInstallation(string editorPath, out IEditorInstallation installation) {
            installation = null;

            if (string.IsNullOrEmpty(editorPath))
                return false;

            if (!IsCandidateForDiscovery(editorPath))
                return false;

            Version version = null;
            var isPreview = false;

            try {
                // Try to get version from Zed
                version = GetZedVersion(editorPath);
                isPreview = editorPath.ToLower().Contains("preview") || editorPath.ToLower().Contains("nightly");
            }
            catch (Exception) {
                // do not fail if we are not able to retrieve the exact version number
            }

            installation = new ZedInstallation() {
                IsPrerelease = isPreview,
                Name = "Zed" + (isPreview ? " - Preview" : string.Empty) + (version != null ? $" [{version.ToString(3)}]" : string.Empty),
                Path = editorPath,
                Version = version ?? new Version()
            };

            return true;
        }

        private static Version GetZedVersion(string editorPath) {
            try {
                var startInfo = new ProcessStartInfo {
                    FileName = editorPath,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(startInfo)) {
                    if (process == null)
                        return null;

                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(3000);

                    // Parse version from output like "Zed 0.165.5"
                    var match = Regex.Match(output, @"[Zz]ed\s+(\d+\.\d+\.\d+)");
                    if (match.Success && Version.TryParse(match.Groups[1].Value, out var ver))
                        return ver;
                }
            }
            catch {
                // Ignore errors
            }

            return null;
        }

        public static IEnumerable<IEditorInstallation> GetZedInstallations() {
            var candidates = new List<string>();

#if UNITY_EDITOR_WIN
            // Windows installation paths
            var localAppPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            // Standard Zed installation locations on Windows
            candidates.Add(IOPath.Combine(localAppPath, "Programs", "Zed", "zed.exe"));
            candidates.Add(IOPath.Combine(localAppPath, "Zed", "zed.exe"));
            candidates.Add(IOPath.Combine(programFiles, "Zed", "zed.exe"));
            candidates.Add(IOPath.Combine(userProfile, ".local", "bin", "zed.exe"));

            // Also check for Zed Preview/Nightly
            candidates.Add(IOPath.Combine(localAppPath, "Programs", "Zed Preview", "zed.exe"));
            candidates.Add(IOPath.Combine(localAppPath, "Programs", "Zed Nightly", "zed.exe"));

#elif UNITY_EDITOR_OSX
			// macOS installation paths
			var appPath = "/Applications";
			candidates.Add(IOPath.Combine(appPath, "Zed.app"));
			candidates.Add(IOPath.Combine(appPath, "Zed Preview.app"));
			
			// Also check user Applications folder
			var userAppPath = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications");
			if (Directory.Exists(userAppPath))
			{
				candidates.Add(IOPath.Combine(userAppPath, "Zed.app"));
				candidates.Add(IOPath.Combine(userAppPath, "Zed Preview.app"));
			}

			// Check for Homebrew installation
			candidates.Add("/opt/homebrew/bin/zed");
			candidates.Add("/usr/local/bin/zed");

#elif UNITY_EDITOR_LINUX
			// Linux installation paths
			candidates.Add("/usr/bin/zed");
			candidates.Add("/usr/local/bin/zed");
			candidates.Add("/bin/zed");
			
			var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			candidates.Add(IOPath.Combine(home, ".local", "bin", "zed"));
			
			// Flatpak
			candidates.Add("/var/lib/flatpak/exports/bin/dev.zed.Zed");
			candidates.Add(IOPath.Combine(home, ".local", "share", "flatpak", "exports", "bin", "dev.zed.Zed"));

			// Snap
			candidates.Add("/snap/bin/zed");

			// AppImage in common locations
			var appImageLocations = new[] { home, IOPath.Combine(home, "Applications"), "/opt" };
			foreach (var loc in appImageLocations)
			{
				if (Directory.Exists(loc))
				{
					try
					{
						candidates.AddRange(Directory.GetFiles(loc, "Zed*.AppImage", SearchOption.TopDirectoryOnly));
					}
					catch { }
				}
			}
#endif

            foreach (var candidate in candidates.Distinct()) {
                if (TryDiscoverInstallation(candidate, out var installation))
                    yield return installation;
            }
        }

        public override void CreateExtraFiles(string projectDirectory) {
            // Zed doesn't require extra configuration files like VS Code
            // It automatically detects .csproj and uses OmniSharp or similar LSPs
            // However, we can create a .zed directory with settings if needed in the future
        }

        public override bool Open(string path, int line, int column, string solution) {
            var application = Path;

            line = Math.Max(1, line);
            column = Math.Max(1, column);

            var directory = IOPath.GetDirectoryName(solution);

#if UNITY_EDITOR_OSX
			// On macOS, we need to use 'open' command for .app bundles
			var arguments = string.IsNullOrEmpty(path)
				? $"-a \"{application}\" \"{directory}\""
				: $"-a \"{application}\" \"{path}:{line}:{column}\"";

			UnityEngine.Debug.Log($"[Zed] Running command: open {arguments}");
			ProcessRunner.Start(ProcessRunner.ProcessStartInfoFor("open", arguments, redirect: false, shell: true));
#else
            // On Windows and Linux, use "zed" CLI from PATH (not the full app path)
            // The CLI is a separate binary that properly handles path:line:column syntax
            var arguments = string.IsNullOrEmpty(path)
                ? $"\"{directory}\""
                : $"\"{directory}\" \"{path}:{line}:{column}\"";

            UnityEngine.Debug.Log($"[Zed] Running command: zed {arguments}");
            ProcessRunner.Start(ProcessRunner.ProcessStartInfoFor("zed", arguments, redirect: false));
#endif

            return true;
        }

        public static void Initialize() {
            // No special initialization needed for Zed
        }
    }
}

