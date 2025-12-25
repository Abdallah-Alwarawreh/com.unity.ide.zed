using System.Collections.Generic;
using System.IO;

namespace Zed.Unity.Editor {
	internal static class Discovery {
		public static IEnumerable<IEditorInstallation> GetZedInstallations() {
			foreach (var installation in ZedInstallation.GetZedInstallations())
				yield return installation;
		}

		public static bool TryDiscoverInstallation(string editorPath, out IEditorInstallation installation) {
			try {
				if (ZedInstallation.TryDiscoverInstallation(editorPath, out installation))
					return true;
			}
			catch (IOException) {
				installation = null;
			}

			return false;
		}

		public static void Initialize() {
			ZedInstallation.Initialize();
		}
	}
}
