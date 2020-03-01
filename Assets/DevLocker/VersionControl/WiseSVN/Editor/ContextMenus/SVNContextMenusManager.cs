using DevLocker.VersionControl.WiseSVN.ContextMenus.Implementation;
using System;
using UnityEditor;

namespace DevLocker.VersionControl.WiseSVN.ContextMenus
{
	public enum ContextMenusClient
	{
		None,
		TortoiseSVN,	// Good for Windows
		SnailSVN,		// Good for MacOS
	}

	public static class SVNContextMenusManager
	{
		private static SVNContextMenusBase m_Integration;

		internal static void SetupContextType(ContextMenusClient client)
		{
			string errorMsg;
			m_Integration = TryCreateContextMenusIntegration(client, out errorMsg);

			if (!string.IsNullOrEmpty(errorMsg)) {
				UnityEngine.Debug.LogError($"WiseSVN: Unsupported context menus client: {client}. Reason: {errorMsg}");
			}

			WiseSVNIntegration.ShowChangesUI -= CheckChangesAll;
			WiseSVNIntegration.ShowChangesUI += CheckChangesAll;
		}

		private static SVNContextMenusBase TryCreateContextMenusIntegration(ContextMenusClient client, out string errorMsg)
		{
			if (client == ContextMenusClient.None) {
				errorMsg = string.Empty;
				return null;
			}

#if UNITY_EDITOR_WIN
			switch (client) {

				case ContextMenusClient.TortoiseSVN:
					errorMsg = string.Empty;
					return new TortoiseSVNContextMenus();

				case ContextMenusClient.SnailSVN:
					errorMsg = "SnailSVN is not supported on windows.";
					return null;

				default:
					throw new NotImplementedException(client + " not implemented yet for this platform.");
			}
#else
			switch (client)
			{

				case ContextMenusClient.TortoiseSVN:
					errorMsg = "TortoiseSVN is not supported on MacOS";
					return null;

				case ContextMenusClient.SnailSVN:
					errorMsg = string.Empty;
					return new SnailSVNContextMenus();

				default:
					throw new NotImplementedException(client + " not implemented yet for this platform.");
			}
#endif
		}

		public static string IsCurrentlySupported(ContextMenusClient client)
		{
			string errorMsg = null;

			TryCreateContextMenusIntegration(client, out errorMsg);

			return errorMsg;
		}

		[MenuItem("Assets/SVN/Check Changes All", false, -1000)]
		static void CheckChangesAll()
		{
			m_Integration?.CheckChangesAll();
		}

		[MenuItem("Assets/SVN/Check Changes", false, -1000)]
		private static void CheckChanges()
		{
			m_Integration?.CheckChanges();
		}

		public static void Update(string filePath)
		{
			m_Integration?.Update(filePath);
		}

		[MenuItem("Assets/SVN/Update All", false, -950)]
		private static void UpdateAll()
		{
			m_Integration?.UpdateAll();
		}

		[MenuItem("Assets/SVN/Commit All", false, -900)]
		private static void CommitAll()
		{
			m_Integration?.CommitAll();
		}

		[MenuItem("Assets/SVN/Commit", false, -900)]
		private static void CommitSelected()
		{
			m_Integration?.CommitSelected();
		}

		[MenuItem("Assets/SVN/Add", false, -900)]
		private static void AddSelected()
		{
			m_Integration?.AddSelected();
		}

		[MenuItem("Assets/SVN/Revert All", false, -800)]
		private static void RevertAll()
		{
			m_Integration?.RevertAll();
		}

		[MenuItem("Assets/SVN/Revert", false, -800)]
		private static void RevertSelected()
		{
			m_Integration?.RevertSelected();
		}

		[MenuItem("Assets/SVN/Resolve All", false, -800)]
		private static void ResolveAll()
		{
			m_Integration?.ResolveAll();
		}


		[MenuItem("Assets/SVN/Get Locks", false, -700)]
		private static void GetLocks()
		{
			m_Integration?.GetLocks();
		}

		[MenuItem("Assets/SVN/Release Locks", false, -700)]
		private static void ReleaseLocks()
		{
			m_Integration?.ReleaseLocks();
		}

		[MenuItem("Assets/SVN/Show Log All", false, -500)]
		private static void ShowLogAll()
		{
			m_Integration?.ShowLogAll();
		}

		[MenuItem("Assets/SVN/Show Log", false, -500)]
		private static void ShowLog()
		{
			m_Integration?.ShowLog();
		}

		[MenuItem("Assets/SVN/Blame", false, -500)]
		private static void Blame()
		{
			m_Integration?.Blame();
		}

		[MenuItem("Assets/SVN/Cleanup", false, -500)]
		private static void Cleanup()
		{
			m_Integration?.Cleanup();
		}
	}
}
