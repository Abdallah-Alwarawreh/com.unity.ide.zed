using System;
using System.Linq;
using Unity.CodeEditor;

namespace Zed.Unity.Editor {
	internal static class Cli {
		internal static void Log(string message) {
			// Use writeline here, instead of UnityEngine.Debug.Log to not include the stacktrace in the editor.log
			Console.WriteLine($"[Zed.Editor.{nameof(Cli)}] {message}");
		}

		internal static string GetInstallationDetails(IEditorInstallation installation) {
			return $"{installation.ToCodeEditorInstallation().Name} Path:{installation.Path}, LanguageVersionSupport:{installation.LatestLanguageVersionSupported} AnalyzersSupport:{installation.SupportsAnalyzers}";
		}

		internal static void GenerateSolutionWith(ZedEditor editor, string installationPath) {
			if (editor != null && editor.TryGetZedInstallationForPath(installationPath, lookupDiscoveredInstallations: true, out var installation)) {
				Log($"Using {GetInstallationDetails(installation)}");
				editor.SyncAll();
			}
			else {
				Log($"No Zed installation found in ${installationPath}!");
			}
		}

		internal static void GenerateSolution() {
			if (CodeEditor.CurrentEditor is ZedEditor editor) {
				Log($"Using default editor settings for Zed installation");
				GenerateSolutionWith(editor, CodeEditor.CurrentEditorInstallation);
			}
			else {
				Log($"Zed is not set as your default editor, looking for installations");
				try {
					var installations = Discovery
						.GetZedInstallations()
						.Cast<EditorInstallation>()
						.OrderByDescending(i => !i.IsPrerelease)
						.ThenBy(i => i.Version)
						.ToArray();

					foreach (var installation in installations) {
						Log($"Detected {GetInstallationDetails(installation)}");
					}

					var selectedInstallation = installations.FirstOrDefault();

					if (selectedInstallation != null) {
						var current = CodeEditor.CurrentEditorInstallation;
						try {
							CodeEditor.SetExternalScriptEditor(selectedInstallation.Path);
							GenerateSolutionWith(CodeEditor.CurrentEditor as ZedEditor, selectedInstallation.Path);
						}
						finally {
							CodeEditor.SetExternalScriptEditor(current);
						}
					}
					else {
						Log($"No Zed installation found!");
					}
				}
				catch (Exception ex) {
					Log($"Error detecting Zed installations: {ex}");
				}
			}
		}
	}
}
