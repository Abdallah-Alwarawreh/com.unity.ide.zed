using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;
using Unity.CodeEditor;

[assembly: InternalsVisibleTo("Unity.Zed.EditorTests")]
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]

namespace Zed.Unity.Editor {
    [InitializeOnLoad]
    public class ZedEditor : IExternalCodeEditor {
        CodeEditor.Installation[] IExternalCodeEditor.Installations => _discoverInstallations
            .Result
            .Values
            .Select(v => v.ToCodeEditorInstallation())
            .ToArray();

        private static readonly AsyncOperation<Dictionary<string, IEditorInstallation>> _discoverInstallations;

        static ZedEditor() {
            if (!UnityInstallation.IsMainUnityEditorProcess)
                return;

            Discovery.Initialize();
            CodeEditor.Register(new ZedEditor());

            _discoverInstallations = AsyncOperation<Dictionary<string, IEditorInstallation>>.Run(DiscoverInstallations);
        }

        private static Dictionary<string, IEditorInstallation> DiscoverInstallations() {
            try {
                return Discovery
                    .GetZedInstallations()
                    .ToDictionary(i => FileUtility.GetAbsolutePath(i.Path), i => i);
            }
            catch (Exception ex) {
                Debug.LogError($"Error detecting Zed installations: {ex}");
                return new Dictionary<string, IEditorInstallation>();
            }
        }

        internal static bool IsEnabled => CodeEditor.CurrentEditor is ZedEditor && UnityInstallation.IsMainUnityEditorProcess;

        public void CreateIfDoesntExist() {
            if (!TryGetZedInstallationForPath(CodeEditor.CurrentEditorInstallation, true, out var installation))
                return;

            var generator = installation.ProjectGenerator;
            if (!generator.HasSolutionBeenGenerated())
                generator.Sync();
        }

        public void Initialize(string editorInstallationPath) {
        }

        internal virtual bool TryGetZedInstallationForPath(string editorPath, bool lookupDiscoveredInstallations, out IEditorInstallation installation) {
            editorPath = FileUtility.GetAbsolutePath(editorPath);

            // lookup for well known installations
            if (lookupDiscoveredInstallations && _discoverInstallations.Result.TryGetValue(editorPath, out installation))
                return true;

            return Discovery.TryDiscoverInstallation(editorPath, out installation);
        }

        public virtual bool TryGetInstallationForPath(string editorPath, out CodeEditor.Installation installation) {
            var result = TryGetZedInstallationForPath(editorPath, lookupDiscoveredInstallations: false, out var zedInstallation);
            installation = zedInstallation?.ToCodeEditorInstallation() ?? default;
            return result;
        }

        public void OnGUI() {
            if (!TryGetZedInstallationForPath(CodeEditor.CurrentEditorInstallation, true, out var installation))
                return;

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(GetType().Assembly);

            var style = new GUIStyle {
                richText = true,
                margin = new RectOffset(0, 4, 0, 0)
            };

            var versionText = package != null
                ? $"{package.displayName} v{package.version}"
                : "Zed Editor";
            GUILayout.Label($"<size=10><color=grey>{versionText} enabled</color></size>", style);
            GUILayout.EndHorizontal();

            EditorGUILayout.LabelField("Generate .csproj files for:");
            EditorGUI.indentLevel++;
            SettingsButton(ProjectGenerationFlag.Embedded, "Embedded packages", "", installation);
            SettingsButton(ProjectGenerationFlag.Local, "Local packages", "", installation);
            SettingsButton(ProjectGenerationFlag.Registry, "Registry packages", "", installation);
            SettingsButton(ProjectGenerationFlag.Git, "Git packages", "", installation);
            SettingsButton(ProjectGenerationFlag.BuiltIn, "Built-in packages", "", installation);
            SettingsButton(ProjectGenerationFlag.LocalTarBall, "Local tarball", "", installation);
            SettingsButton(ProjectGenerationFlag.Unknown, "Packages from unknown sources", "", installation);
            SettingsButton(ProjectGenerationFlag.PlayerAssemblies, "Player projects", "For each player project generate an additional csproj with the name 'project-player.csproj'", installation);
            RegenerateProjectFiles(installation);
            EditorGUI.indentLevel--;
        }

        private static void RegenerateProjectFiles(IEditorInstallation installation) {
            var rect = EditorGUI.IndentedRect(EditorGUILayout.GetControlRect());
            rect.width = 252;
            if (GUI.Button(rect, "Regenerate project files")) {
                installation.ProjectGenerator.Sync();
            }
        }

        private static void SettingsButton(ProjectGenerationFlag preference, string guiMessage, string toolTip, IEditorInstallation installation) {
            var generator = installation.ProjectGenerator;
            var prevValue = generator.AssemblyNameProvider.ProjectGenerationFlag.HasFlag(preference);

            var newValue = EditorGUILayout.Toggle(new GUIContent(guiMessage, toolTip), prevValue);
            if (newValue != prevValue)
                generator.AssemblyNameProvider.ToggleProjectGeneration(preference);
        }

        public void SyncIfNeeded(string[] addedFiles, string[] deletedFiles, string[] movedFiles, string[] movedFromFiles, string[] importedFiles) {
            if (TryGetZedInstallationForPath(CodeEditor.CurrentEditorInstallation, true, out var installation)) {
                installation.ProjectGenerator.SyncIfNeeded(addedFiles.Union(deletedFiles).Union(movedFiles).Union(movedFromFiles), importedFiles);
            }

            foreach (var file in importedFiles.Where(a => Path.GetExtension(a) == ".pdb")) {
                var pdbFile = FileUtility.GetAssetFullPath(file);

                // skip Unity packages like com.unity.ext.nunit
                if (pdbFile.IndexOf($"{Path.DirectorySeparatorChar}com.unity.", StringComparison.OrdinalIgnoreCase) > 0)
                    continue;

                var asmFile = Path.ChangeExtension(pdbFile, ".dll");
                if (!File.Exists(asmFile) || !Image.IsAssembly(asmFile))
                    continue;

                if (Symbols.IsPortableSymbolFile(pdbFile))
                    continue;

                Debug.LogWarning($"Unity is only able to load mdb or portable-pdb symbols. {file} is using a legacy pdb format.");
            }
        }

        public void SyncAll() {
            if (TryGetZedInstallationForPath(CodeEditor.CurrentEditorInstallation, true, out var installation)) {
                installation.ProjectGenerator.Sync();
            }
        }

        private static bool IsSupportedPath(string path, IGenerator generator) {
            // Path is empty with "Open C# Project", as we only want to open the solution without specific files
            if (string.IsNullOrEmpty(path))
                return true;

            // cs, uxml, uss, shader, compute, cginc, hlsl, glslinc, template are part of Unity builtin extensions
            // txt, xml, fnt, cd are -often- part of Unity user extensions
            // asmdef is mandatory included
            return generator.IsSupportedFile(path);
        }

        public bool OpenProject(string path, int line, int column) {
            var editorPath = CodeEditor.CurrentEditorInstallation;

            if (!Discovery.TryDiscoverInstallation(editorPath, out var installation)) {
                Debug.LogWarning($"Zed executable {editorPath} is not found. Please change your settings in Edit > Preferences > External Tools.");
                return false;
            }

            var generator = installation.ProjectGenerator;
            if (!IsSupportedPath(path, generator))
                return false;

            if (!IsProjectGeneratedFor(path, generator, out var missingFlag))
                Debug.LogWarning($"You are trying to open {path} outside a generated project. This might cause problems with IntelliSense and debugging. To avoid this, you can change your .csproj preferences in Edit > Preferences > External Tools and enable {GetProjectGenerationFlagDescription(missingFlag)} generation.");

            var solution = GetOrGenerateSolutionFile(generator);
            return installation.Open(path, line, column, solution);
        }

        private static string GetProjectGenerationFlagDescription(ProjectGenerationFlag flag) {
            switch (flag) {
                case ProjectGenerationFlag.BuiltIn:
                    return "Built-in packages";
                case ProjectGenerationFlag.Embedded:
                    return "Embedded packages";
                case ProjectGenerationFlag.Git:
                    return "Git packages";
                case ProjectGenerationFlag.Local:
                    return "Local packages";
                case ProjectGenerationFlag.LocalTarBall:
                    return "Local tarball";
                case ProjectGenerationFlag.PlayerAssemblies:
                    return "Player projects";
                case ProjectGenerationFlag.Registry:
                    return "Registry packages";
                case ProjectGenerationFlag.Unknown:
                    return "Packages from unknown sources";
                default:
                    return string.Empty;
            }
        }

        private static bool IsProjectGeneratedFor(string path, IGenerator generator, out ProjectGenerationFlag missingFlag) {
            missingFlag = ProjectGenerationFlag.None;

            // No need to check when opening the whole solution
            if (string.IsNullOrEmpty(path))
                return true;

            // We only want to check for cs scripts
            if (ProjectGeneration.ScriptingLanguageForFile(path) != ScriptingLanguage.CSharp)
                return true;

            // Even on windows, the package manager requires relative path + unix style separators for queries
            var basePath = generator.ProjectDirectory;
            var relativePath = path
                .NormalizeWindowsToUnix()
                .Replace(basePath, string.Empty)
                .Trim(FileUtility.UnixSeparator);

            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(relativePath);
            if (packageInfo == null)
                return true;

            var source = packageInfo.source;
            if (!Enum.TryParse<ProjectGenerationFlag>(source.ToString(), out var flag))
                return true;

            if (generator.AssemblyNameProvider.ProjectGenerationFlag.HasFlag(flag))
                return true;

            // Return false if we found a source not flagged for generation
            missingFlag = flag;
            return false;
        }

        private static string GetOrGenerateSolutionFile(IGenerator generator) {
            generator.Sync();
            return generator.SolutionFile();
        }
    }
}

