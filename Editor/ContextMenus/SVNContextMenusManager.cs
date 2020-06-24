using DevLocker.VersionControl.WiseSVN.ContextMenus.Implementation;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace DevLocker.VersionControl.WiseSVN.ContextMenus
{
	public enum ContextMenusClient
	{
		None,
		TortoiseSVN,	// Good for Windows
		SnailSVN,		// Good for MacOS
	}

	/// <summary>
	/// This class is responsible for the "Assets/SVN/..." context menus that pop up SVN client windows.
	/// You can do this from your code as well. For the most methods you have to provide list of asset paths,
	/// should the method add meta files as well and should it wait for the SVN client window to close.
	/// *** It is recommended to wait for update operations to finish! Check the Update method for more info. ***
	/// </summary>
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

		private static IEnumerable<string> GetRootAssetPath()
		{
			yield return ".";	// The root folder of the project (not the Assets folder).
		}

		private static IEnumerable<string> GetSelectedAssetPaths()
		{
			return Selection.assetGUIDs.Select(AssetDatabase.GUIDToAssetPath);
		}

		[MenuItem("Assets/SVN/Check Changes All", false, -1000)]
		public static void CheckChangesAll()
		{
			m_Integration?.CheckChanges(GetRootAssetPath(), false);
		}

		[MenuItem("Assets/SVN/Check Changes", false, -1000)]
		public static void CheckChangesSelected()
		{
			m_Integration?.CheckChanges(GetSelectedAssetPaths(), true);
		}

		public static void CheckChanges(IEnumerable<string> assetPaths, bool includeMeta, bool wait = false)
		{
			m_Integration?.CheckChanges(assetPaths, includeMeta, wait);
		}



		[MenuItem("Assets/SVN/Update All", false, -950)]
		public static void UpdateAll()
		{
			// It is recommended to freeze Unity while updating.
			// If SVN downloads files while Unity is crunching assets, GUID database may get corrupted.
			m_Integration?.Update(GetRootAssetPath(), false, true);
		}

		// It is recommended to freeze Unity while updating.
		// DANGER: SVN updating while editor is crunching assets IS DANGEROUS! It WILL corrupt your asset guids. Use with caution!!!
		//		   This is the reason why this method freezes your editor and waits for the update to finish.
		public static void Update(IEnumerable<string> assetPaths, bool includeMeta)
		{
			m_Integration?.Update(assetPaths, includeMeta, true);
		}

		// It is recommended to freeze Unity while updating.
		// DANGER: SVN updating while editor is crunching assets IS DANGEROUS! It WILL corrupt your asset guids. Use with caution!!!
		public static void UpdateAndDontWaitDANGER(IEnumerable<string> assetPaths, bool includeMeta)
		{
			m_Integration?.Update(assetPaths, includeMeta, false);
		}



		[MenuItem("Assets/SVN/Commit All", false, -900)]
		public static void CommitAll()
		{
			m_Integration?.Commit(GetRootAssetPath(), false);
		}

		[MenuItem("Assets/SVN/Commit", false, -900)]
		public static void CommitSelected()
		{
			m_Integration?.Commit(GetSelectedAssetPaths(), true);
		}

		public static void Commit(IEnumerable<string> assetPaths, bool includeMeta, bool wait = false)
		{
			m_Integration?.Commit(assetPaths, includeMeta, wait);
		}



		[MenuItem("Assets/SVN/Add", false, -900)]
		public static void AddSelected()
		{
			m_Integration?.Add(GetSelectedAssetPaths(), true);
		}

		public static void Add(IEnumerable<string> assetPaths, bool includeMeta, bool wait = false)
		{
			m_Integration?.Add(assetPaths, includeMeta, wait);
		}



		[MenuItem("Assets/SVN/Revert All", false, -800)]
		public static void RevertAll()
		{
			m_Integration?.Revert(GetRootAssetPath(), false);
		}

		[MenuItem("Assets/SVN/Revert", false, -800)]
		public static void RevertSelected()
		{
			m_Integration?.Revert(GetSelectedAssetPaths(), true);
		}

		public static void Revert(IEnumerable<string> assetPaths, bool includeMeta, bool wait = false)
		{
			m_Integration?.Revert(assetPaths, includeMeta, wait);
		}



		[MenuItem("Assets/SVN/Resolve All", false, -800)]
		private static void ResolveAllMenu()
		{
			m_Integration?.ResolveAll(false);
		}

		public static void ResolveAll(bool wait = false)
		{
			m_Integration?.ResolveAll(wait);
		}




		[MenuItem("Assets/SVN/Get Locks", false, -700)]
		public static void GetLocksSelected()
		{
			m_Integration?.GetLocks(GetSelectedAssetPaths(), true);
		}

		public static void GetLocks(IEnumerable<string> assetPaths, bool includeMeta, bool wait = false)
		{
			m_Integration?.GetLocks(assetPaths, includeMeta, wait);
		}



		[MenuItem("Assets/SVN/Release Locks", false, -700)]
		public static void ReleaseLocksSelected()
		{
			m_Integration?.ReleaseLocks(GetSelectedAssetPaths(), true);
		}

		public static void ReleaseLocks(IEnumerable<string> assetPaths, bool includeMeta, bool wait = false)
		{
			m_Integration?.ReleaseLocks(assetPaths, includeMeta, wait);
		}



		[MenuItem("Assets/SVN/Show Log All", false, -500)]
		public static void ShowLogAll()
		{
			m_Integration?.ShowLog(GetRootAssetPath().First());
		}

		[MenuItem("Assets/SVN/Show Log", false, -500)]
		public static void ShowLogSelected()
		{
			m_Integration?.ShowLog(GetSelectedAssetPaths().FirstOrDefault());
		}

		public static void ShowLog(string assetPath, bool wait = false)
		{
			m_Integration?.ShowLog(assetPath, wait);
		}



		[MenuItem("Assets/SVN/Blame", false, -500)]
		public static void BlameSelected()
		{
			m_Integration?.Blame(GetSelectedAssetPaths().FirstOrDefault());
		}

		public static void Blame(string assetPath, bool wait = false)
		{
			m_Integration?.Blame(assetPath, wait);
		}



		[MenuItem("Assets/SVN/Cleanup", false, -500)]
		public static void Cleanup()
		{
			m_Integration?.Cleanup(true);
		}

		// It is recommended to freeze Unity while Cleanup is working.
		public static void CleanupAndDontWait()
		{
			m_Integration?.Cleanup(false);
		}

		/// <summary>
		/// Open Repo-Browser at url location. You can get url from local working copy by using WiseSVNIntegration.AssetPathToURL(path);
		/// </summary>
		public static void RepoBrowser(string url, bool wait = false)
		{
			m_Integration?.RepoBrowser(url, wait);
		}

		/// <summary>
		/// Open Switch dialog. localPath specifies the target directory and url the URL to switch to.
		/// Most likely you want the root of the working copy (checkout), not just the Unity project. To get it use WiseSVNIntegration.WorkingCopyRootPath();
		/// </summary>
		public static void Switch(string localPath, string url, bool wait = false)
		{
			m_Integration?.Switch(localPath, url, wait);
		}
	}
}
