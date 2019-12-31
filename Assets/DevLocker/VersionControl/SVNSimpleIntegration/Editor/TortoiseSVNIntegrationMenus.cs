using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DevLocker.VersionControl.SVN
{
#if UNITY_EDITOR_WIN
	// TortoiseSVN Commands: https://tortoisesvn.net/docs/release/TortoiseSVN_en/tsvn-automation.html
	public static class TortoiseSVNIntegrationMenus
	{
		[MenuItem("Assets/SVN/Check Changes All", false, -1000)]
		internal static void CheckChangesAll()
		{
			var result = ShellUtils.ExecuteCommand("TortoiseProc.exe", $"/command:repostatus /path:\"{SVNSimpleIntegration.ProjectRoot}\"", false);
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
			}
		}

		[MenuItem("Assets/SVN/Check Changes", false, -1000)]
		private static void CheckChanges()
		{
			var result = ShellUtils.ExecuteCommand("TortoiseProc.exe", $"/command:repostatus /path:\"{GetContextPaths(Selection.assetGUIDs, true)}\"", false);
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
			}
		}
		
		public static void Update(string filePath)
		{
			var result = ShellUtils.ExecuteCommand("TortoiseProc.exe", $"/command:update /path:\"{SVNSimpleIntegration.ProjectRoot + filePath}\"", true);
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
			}
		}

		[MenuItem("Assets/SVN/Update All", false, -950)]
		private static void UpdateAll()
		{
			var result = ShellUtils.ExecuteCommand("TortoiseProc.exe", $"/command:update /path:\"{SVNSimpleIntegration.ProjectRoot}\"", true);
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
			}
		}

		[MenuItem("Assets/SVN/Commit All", false, -900)]
		private static void CommitAll()
		{
			var result = ShellUtils.ExecuteCommand("TortoiseProc.exe", $"/command:commit /path:\"{SVNSimpleIntegration.ProjectRoot}\"", false);
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
			}
		}

		[MenuItem("Assets/SVN/Commit", false, -900)]
		private static void CommitSelected()
		{
			var result = ShellUtils.ExecuteCommand("TortoiseProc.exe", $"/command:commit /path:\"{GetContextPaths(Selection.assetGUIDs, true)}\"", false);
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
			}
		}

		[MenuItem("Assets/SVN/Add", false, -900)]
		private static void AddSelected()
		{
			var paths = Selection.assetGUIDs
				.Select(AssetDatabase.GUIDToAssetPath)
				.Where(path => !string.IsNullOrEmpty(path))
				.ToList()
				;

			foreach (var path in paths) {
				if (!SVNSimpleIntegration.CheckAndAddParentFolderIfNeeded(path))
					return;
			}

			// Don't give versioned metas, as tortoiseSVN doesn't like it.
			var metas = paths
				.Select(path => path + ".meta")
				.Where(path => SVNSimpleIntegration.GetStatus(path).Status == VCFileStatus.Unversioned)
				;

			var pathsArg = paths.Concat(metas).Aggregate((p, n) => $"{p}*{n}");


			var result = ShellUtils.ExecuteCommand("TortoiseProc.exe", $"/command:add /path:\"{pathsArg}\"", false);
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
			}

			// Files are added directly without user interface. Folders will pop out user interface.
			SVNOverlayIcons.InvalidateDatabase();
		}

		[MenuItem("Assets/SVN/Revert All", false, -800)]
		private static void RevertAll()
		{
			var result = ShellUtils.ExecuteCommand("TortoiseProc.exe", $"/command:revert /path:\"{SVNSimpleIntegration.ProjectRoot}\"", false);
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
			}
		}

		[MenuItem("Assets/SVN/Revert", false, -800)]
		private static void RevertSelected()
		{
			var result = ShellUtils.ExecuteCommand("TortoiseProc.exe", $"/command:revert /path:\"{GetContextPaths(Selection.assetGUIDs, true)}\"", false);
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
			}
		}

		[MenuItem("Assets/SVN/Resolve All", false, -800)]
		private static void ResolveAll()
		{
			var result = ShellUtils.ExecuteCommand("TortoiseProc.exe", $"/command:resolve /path:\"{SVNSimpleIntegration.ProjectRoot}\"", false);
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
			}
		}

		[MenuItem("Assets/SVN/Show Log All", false, -500)]
		private static void ShowLogAll()
		{
			var result = ShellUtils.ExecuteCommand("TortoiseProc.exe", $"/command:log /path:\"{SVNSimpleIntegration.ProjectRoot}\"", false);
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
			}
		}

		[MenuItem("Assets/SVN/Show Log", false, -500)]
		private static void ShowLog()
		{
			var result = ShellUtils.ExecuteCommand("TortoiseProc.exe", $"/command:log /path:\"{GetContextPaths(Selection.assetGUIDs.FirstOrDefault(), false)}\"", false);
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
			}
		}

		[MenuItem("Assets/SVN/Blame", false, -500)]
		private static void Blame()
		{
			var result = ShellUtils.ExecuteCommand("TortoiseProc.exe", $"/command:blame /path:\"{GetContextPaths(Selection.assetGUIDs.FirstOrDefault(), true)}\"", false);
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
			}
		}

		[MenuItem("Assets/SVN/Cleanup", false, -500)]
		private static void Cleanup()
		{
			var result = ShellUtils.ExecuteCommand("TortoiseProc.exe", $"/command:cleanup /path:\"{SVNSimpleIntegration.ProjectRoot}\"", true);
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
			}
		}

		private static string GetContextPaths(string[] guids, bool includeMeta)
		{
			if (guids.Length == 0)
				return string.Empty;

			if (guids.Length > 1) {
				return guids.Select(obj => GetContextPaths(obj, includeMeta)).Aggregate((p, n) => $"{p}*{n}");
			} else {
				return GetContextPaths(guids[0], includeMeta);
			}
		}

		private static string GetContextPaths(string guid, bool includeMeta)
		{
			var path = AssetDatabase.GUIDToAssetPath(guid);
			if (string.IsNullOrEmpty(path))
				return Path.GetDirectoryName(Application.dataPath);

			if (includeMeta) {
				path += $"*{path}.meta";
			}

			return path;
		}
	}
#endif
}
