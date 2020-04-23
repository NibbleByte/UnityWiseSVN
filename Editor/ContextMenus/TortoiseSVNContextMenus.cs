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

		public override void CheckChanges(IEnumerable<string> assetPaths, bool includeMeta, bool wait = false)
		{
			if (!assetPaths.Any())
				return;

			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:repostatus /path:\"{AssetPathsToContextPaths(assetPaths, includeMeta)}\"", wait);
			if (!string.IsNullOrEmpty(result.Error)) {
				Debug.LogError($"SVN Error: {result.Error}");
			}
		}

		public override void Update(IEnumerable<string> assetPaths, bool includeMeta, bool wait = false)
		{
			if (!assetPaths.Any())
				return;

			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:update /path:\"{AssetPathsToContextPaths(assetPaths, includeMeta)}\"", wait);
			if (!string.IsNullOrEmpty(result.Error)) {
				Debug.LogError($"SVN Error: {result.Error}");
			}
		}

		public override void Commit(IEnumerable<string> assetPaths, bool includeMeta, bool wait = false)
		{
			if (!assetPaths.Any())
				return;

			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:commit /path:\"{AssetPathsToContextPaths(assetPaths, includeMeta)}\"", wait);
			if (!string.IsNullOrEmpty(result.Error)) {
				Debug.LogError($"SVN Error: {result.Error}");
			}
		}



		public override void Add(IEnumerable<string> assetPaths, bool includeMeta, bool wait = false)
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

			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:add /path:\"{pathsArg}\"", wait);
			if (!string.IsNullOrEmpty(result.Error)) {
				Debug.LogError($"SVN Error: {result.Error}");
			}
		}

		public override void Revert(IEnumerable<string> assetPaths, bool includeMeta, bool wait = false)
		{
			if (!assetPaths.Any())
				return;

			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:revert /path:\"{AssetPathsToContextPaths(assetPaths, includeMeta)}\"", wait);
			if (!string.IsNullOrEmpty(result.Error)) {
				Debug.LogError($"SVN Error: {result.Error}");
			}
		}



		public override void ResolveAll(bool wait = false)
		{
			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:resolve /path:\"{WiseSVNIntegration.ProjectRoot}\"", wait);
			if (!string.IsNullOrEmpty(result.Error)) {
				Debug.LogError($"SVN Error: {result.Error}");
			}
		}



		public override void GetLocks(IEnumerable<string> assetPaths, bool includeMeta, bool wait = false)
		{
			if (!assetPaths.Any())
				return;

			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:lock /path:\"{AssetPathsToContextPaths(assetPaths, includeMeta)}\"", wait);
			if (!string.IsNullOrEmpty(result.Error)) {
				Debug.LogError($"SVN Error: {result.Error}");
			}
		}

		public override void ReleaseLocks(IEnumerable<string> assetPaths, bool includeMeta, bool wait = false)
		{
			if (!assetPaths.Any())
				return;

			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:unlock /path:\"{AssetPathsToContextPaths(assetPaths, includeMeta)}\"", wait);
			if (!string.IsNullOrEmpty(result.Error)) {
				Debug.LogError($"SVN Error: {result.Error}");
			}
		}

		public override void ShowLog(string assetPath, bool wait = false)
		{
			if (string.IsNullOrEmpty(assetPath))
				return;

			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:log /path:\"{AssetPathToContextPaths(assetPath, false)}\"", wait);
			if (!string.IsNullOrEmpty(result.Error)) {
				Debug.LogError($"SVN Error: {result.Error}");
			}
		}



		public override void Blame(string assetPath, bool wait = false)
		{
			if (string.IsNullOrEmpty(assetPath))
				return;

			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:blame /path:\"{AssetPathToContextPaths(assetPath, false)}\"", wait);
			if (!string.IsNullOrEmpty(result.Error)) {
				Debug.LogError($"SVN Error: {result.Error}");
			}
		}



		public override void Cleanup(bool wait = false)
		{
			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:cleanup /path:\"{WiseSVNIntegration.ProjectRoot}\"", wait);
			if (!string.IsNullOrEmpty(result.Error)) {
				Debug.LogError($"SVN Error: {result.Error}");
			}
		}
	}
#endif
}
