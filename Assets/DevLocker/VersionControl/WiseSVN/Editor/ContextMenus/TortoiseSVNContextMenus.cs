using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DevLocker.VersionControl.WiseSVN.ContextMenus.Implementation
{
#if UNITY_EDITOR_WIN
	// TortoiseSVN Commands: https://tortoisesvn.net/docs/release/TortoiseSVN_en/tsvn-automation.html
	internal class TortoiseSVNContextMenus : SVNContextMenusBase
	{
		private const string ClientCommand = "TortoiseProc.exe";

		protected override string FileArgumentsSeparator => "*";
		protected override bool FileArgumentsSurroundQuotes => false;

		public override void CheckChangesAll()
		{
			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:repostatus /path:\"{WiseSVNIntegration.ProjectRoot}\"", false);
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
			}
		}

		public override void CheckChanges()
		{
			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:repostatus /path:\"{GuidsToContextPaths(Selection.assetGUIDs, true)}\"", false);
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
			}
		}

		public override void Update(string filePath)
		{
			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:update /path:\"{WiseSVNIntegration.ProjectRoot + filePath}\"", true);
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
			}
		}

		public override void UpdateAll()
		{
			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:update /path:\"{WiseSVNIntegration.ProjectRoot}\"", true);
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
			}
		}

		public override void CommitAll()
		{
			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:commit /path:\"{WiseSVNIntegration.ProjectRoot}\"", false);
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
			}
		}

		public override void CommitSelected()
		{
			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:commit /path:\"{GuidsToContextPaths(Selection.assetGUIDs, true)}\"", false);
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
			}
		}

		public override void AddSelected()
		{
			var paths = Selection.assetGUIDs
				.Select(AssetDatabase.GUIDToAssetPath)
				.Where(path => !string.IsNullOrEmpty(path))
				.ToList()
				;

			foreach (var path in paths) {
				if (!WiseSVNIntegration.CheckAndAddParentFolderIfNeeded(path))
					return;
			}

			// Don't give versioned metas, as tortoiseSVN doesn't like it.
			var metas = paths
				.Select(path => path + ".meta")
				.Where(path => WiseSVNIntegration.GetStatus(path).Status == VCFileStatus.Unversioned)
				;

			var pathsArg = AssetPathsToContextPaths(paths.Concat(metas), false);

			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:add /path:\"{pathsArg}\"", false);
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
			}
		}

		public override void RevertAll()
		{
			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:revert /path:\"{WiseSVNIntegration.ProjectRoot}\"", false);
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
			}
		}

		public override void RevertSelected()
		{
			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:revert /path:\"{GuidsToContextPaths(Selection.assetGUIDs, true)}\"", false);
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
			}
		}

		public override void ResolveAll()
		{
			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:resolve /path:\"{WiseSVNIntegration.ProjectRoot}\"", false);
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
			}
		}


		public override void GetLocks()
		{
			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:lock /path:\"{GuidsToContextPaths(Selection.assetGUIDs, true)}\"", false);
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
			}
		}

		public override void ReleaseLocks()
		{
			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:unlock /path:\"{GuidsToContextPaths(Selection.assetGUIDs, true)}\"", false);
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
			}
		}

		public override void ShowLogAll()
		{
			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:log /path:\"{WiseSVNIntegration.ProjectRoot}\"", false);
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
			}
		}

		public override void ShowLog()
		{
			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:log /path:\"{GuidToContextPaths(Selection.assetGUIDs.FirstOrDefault(), false)}\"", false);
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
			}
		}

		public override void Blame()
		{
			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:blame /path:\"{GuidToContextPaths(Selection.assetGUIDs.FirstOrDefault(), true)}\"", false);
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
			}
		}

		public override void Cleanup()
		{
			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:cleanup /path:\"{WiseSVNIntegration.ProjectRoot}\"", true);
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
			}
		}
	}
#endif
}
