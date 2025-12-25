using System;

namespace Zed.Unity.Editor.Testing {
	[Serializable]
	internal enum TestStatusAdaptor {
		Passed,
		Skipped,
		Inconclusive,
		Failed,
	}
}
