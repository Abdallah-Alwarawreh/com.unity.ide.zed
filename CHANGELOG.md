# Changelog

## [1.0.0] - 2025-01-01

### Added
- Initial release of Zed Editor integration for Unity
- Auto-discovery of Zed installations on Windows, macOS, and Linux
- Support for opening files at specific line and column positions
- C# project (csproj) and solution (sln/slnx) generation for IntelliSense
- Integration with Unity Test Runner

### Changed
- Forked from com.unity.ide.visualstudio 2.0.26
- Replaced Visual Studio/VS Code detection with Zed editor detection
- Updated namespace from Microsoft.Unity.VisualStudio to Zed.Unity.Editor

### Removed
- Visual Studio for Windows specific features (COM integration, VSWhere)
- VS Code specific configuration file generation (.vscode folder)
