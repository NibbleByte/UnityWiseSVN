using System.Collections.Generic;
using System.Linq;
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

		public override void CheckChanges(IEnumerable<string> assetPaths, bool includeMeta)
		{
			if (!assetPaths.Any())
				return;

			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:repostatus /path:\"{AssetPathsToContextPaths(assetPaths, includeMeta)}\"", false);
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
			}
		}

		public override void Update(IEnumerable<string> assetPaths, bool includeMeta)
		{
			if (!assetPaths.Any())
				return;

			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:update /path:\"{AssetPathsToContextPaths(assetPaths, includeMeta)}\"", true);
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
			}
		}

		public override void Commit(IEnumerable<string> assetPaths, bool includeMeta)
		{
			if (!assetPaths.Any())
				return;

			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:commit /path:\"{AssetPathsToContextPaths(assetPaths, includeMeta)}\"", false);
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
			}
		}



		public override void Add(IEnumerable<string> assetPaths, bool includeMeta)
		{
			if (!assetPaths.Any())
				return;

			foreach (var path in assetPaths) {
				if (!WiseSVNIntegration.CheckAndAddParentFolderIfNeeded(path))
					return;
			}

			// Don't give versioned metas, as tortoiseSVN doesn't like it.
			var metas = assetPaths
				.Select(path => path + ".meta")
				.Where(path => WiseSVNIntegration.GetStatus(path).Status == VCFileStatus.Unversioned)
				;

			var pathsArg = AssetPathsToContextPaths(includeMeta ? assetPaths.Concat(metas) : assetPaths, false);

			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:add /path:\"{pathsArg}\"", false);
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
			}
		}

		public override void Revert(IEnumerable<string> assetPaths, bool includeMeta)
		{
			if (!assetPaths.Any())
				return;

			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:revert /path:\"{AssetPathsToContextPaths(assetPaths, includeMeta)}\"", false);
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



		public override void GetLocks(IEnumerable<string> assetPaths, bool includeMeta)
		{
			if (!assetPaths.Any())
				return;

			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:lock /path:\"{AssetPathsToContextPaths(assetPaths, includeMeta)}\"", false);
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
			}
		}

		public override void ReleaseLocks(IEnumerable<string> assetPaths, bool includeMeta)
		{
			if (!assetPaths.Any())
				return;

			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:unlock /path:\"{AssetPathsToContextPaths(assetPaths, includeMeta)}\"", false);
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
			}
		}

		public override void ShowLog(string assetPath)
		{
			if (string.IsNullOrEmpty(assetPath))
				return;

			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:log /path:\"{AssetPathToContextPaths(assetPath, false)}\"", false);
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
			}
		}



		public override void Blame(string assetPath)
		{
			if (string.IsNullOrEmpty(assetPath))
				return;

			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:blame /path:\"{AssetPathToContextPaths(assetPath, false)}\"", false);
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
