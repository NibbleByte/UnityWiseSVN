using DevLocker.VersionControl.WiseSVN.Shell;
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

			string pathsArg = AssetPathsToContextPaths(assetPaths, includeMeta);
			if (string.IsNullOrEmpty(pathsArg))
				return;

			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:repostatus /path:\"{pathsArg}\"", wait);
			if (!string.IsNullOrEmpty(result.Error)) {
				Debug.LogError($"SVN Error: {result.Error}");
			}
		}

		public override void DiffChanges(string assetPath, bool wait = false)
		{
			string pathsArg = AssetPathToContextPaths(assetPath, false);
			if (string.IsNullOrEmpty(pathsArg))
				return;
			
			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:diff /path:\"{pathsArg}\"", wait);
			if (!string.IsNullOrEmpty(result.Error)) {
				Debug.LogError($"SVN Error: {result.Error}");
			}
		}

		public override void Update(IEnumerable<string> assetPaths, bool includeMeta, bool wait = false)
		{
			if (!assetPaths.Any())
				return;
			
			string pathsArg = AssetPathsToContextPaths(assetPaths, includeMeta);
			if (string.IsNullOrEmpty(pathsArg))
				return;

			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:update /path:\"{pathsArg}\"", wait);
			if (!string.IsNullOrEmpty(result.Error)) {
				Debug.LogError($"SVN Error: {result.Error}");
			}
		}

		public override void Commit(IEnumerable<string> assetPaths, bool includeMeta, bool wait = false)
		{
			if (!assetPaths.Any())
				return;
			
			string pathsArg = AssetPathsToContextPaths(assetPaths, includeMeta);
			if (string.IsNullOrEmpty(pathsArg))
				return;

			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:commit /path:\"{pathsArg}\"", wait);
			if (!string.IsNullOrEmpty(result.Error)) {
				Debug.LogError($"SVN Error: {result.Error}");
			}
		}



		public override void Add(IEnumerable<string> assetPaths, bool includeMeta, bool wait = false)
		{
			if (!assetPaths.Any())
				return;

			foreach (var path in assetPaths) {
				if (!WiseSVNIntegration.CheckAndAddParentFolderIfNeeded(path, true))
					return;
			}

			// Don't give versioned metas, as tortoiseSVN doesn't like it.
			var metas = assetPaths
				.Select(path => path + ".meta")
				.Where(path => WiseSVNIntegration.GetStatus(path).Status == VCFileStatus.Unversioned)
				;

			string pathsArg = AssetPathsToContextPaths(includeMeta ? assetPaths.Concat(metas) : assetPaths, false);
			if (string.IsNullOrEmpty(pathsArg))
				return;

			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:add /path:\"{pathsArg}\"", wait);
			if (!string.IsNullOrEmpty(result.Error)) {
				Debug.LogError($"SVN Error: {result.Error}");
			}
		}

		public override void Revert(IEnumerable<string> assetPaths, bool includeMeta, bool wait = false)
		{
			if (!assetPaths.Any())
				return;
			
			string pathsArg = AssetPathsToContextPaths(assetPaths, includeMeta);
			if (string.IsNullOrEmpty(pathsArg))
				return;

			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:revert /path:\"{pathsArg}\"", wait);
			if (!string.IsNullOrEmpty(result.Error)) {
				Debug.LogError($"SVN Error: {result.Error}");
			}
		}



		public override void ResolveAll(bool wait = false)
		{
			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:resolve /path:\"{WiseSVNIntegration.ProjectRootNative}\"", wait);
			if (!string.IsNullOrEmpty(result.Error)) {
				Debug.LogError($"SVN Error: {result.Error}");
			}
		}

		public override void Resolve(string assetPath, bool wait = false)
		{
			if (System.IO.Directory.Exists(assetPath)) {
				var resolveResult = ShellUtils.ExecuteCommand(ClientCommand, $"/command:resolve /path:\"{AssetPathToContextPaths(assetPath, false)}\"", wait);
				if (!string.IsNullOrEmpty(resolveResult.Error)) {
					Debug.LogError($"SVN Error: {resolveResult.Error}");
				}

				return;
			}

			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:conflicteditor /path:\"{AssetPathToContextPaths(assetPath, false)}\"", wait);
			if (!string.IsNullOrEmpty(result.Error)) {
				Debug.LogError($"SVN Error: {result.Error}");
			}
		}



		public override void GetLocks(IEnumerable<string> assetPaths, bool includeMeta, bool wait = false)
		{
			if (!assetPaths.Any())
				return;
			
			string pathsArg = AssetPathsToContextPaths(assetPaths, includeMeta);
			if (string.IsNullOrEmpty(pathsArg))
				return;

			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:lock /path:\"{pathsArg}\"", wait);
			if (!string.IsNullOrEmpty(result.Error)) {
				Debug.LogError($"SVN Error: {result.Error}");
			}
		}

		public override void ReleaseLocks(IEnumerable<string> assetPaths, bool includeMeta, bool wait = false)
		{
			if (!assetPaths.Any())
				return;
			
			string pathsArg = AssetPathsToContextPaths(assetPaths, includeMeta);
			if (string.IsNullOrEmpty(pathsArg))
				return;

			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:unlock /path:\"{pathsArg}\"", wait);
			if (!string.IsNullOrEmpty(result.Error)) {
				Debug.LogError($"SVN Error: {result.Error}");
			}
		}

		public override void ShowLog(string assetPath, bool wait = false)
		{
			if (string.IsNullOrEmpty(assetPath))
				return;
			
			string pathsArg = AssetPathToContextPaths(assetPath, false);
			if (string.IsNullOrEmpty(pathsArg))
				return;

			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:log /path:\"{pathsArg}\"", wait);
			if (!string.IsNullOrEmpty(result.Error)) {
				Debug.LogError($"SVN Error: {result.Error}");
			}
		}



		public override void Blame(string assetPath, bool wait = false)
		{
			if (string.IsNullOrEmpty(assetPath))
				return;
			
			string pathsArg = AssetPathToContextPaths(assetPath, false);
			if (string.IsNullOrEmpty(pathsArg))
				return;

			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:blame /path:\"{pathsArg}\"", wait);
			if (!string.IsNullOrEmpty(result.Error)) {
				Debug.LogError($"SVN Error: {result.Error}");
			}
		}



		public override void Cleanup(bool wait = false)
		{
			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:cleanup /path:\"{WiseSVNIntegration.ProjectRootNative}\"", wait);
			if (!string.IsNullOrEmpty(result.Error)) {
				Debug.LogError($"SVN Error: {result.Error}");
			}
		}


		public override void RepoBrowser(string url, bool wait = false)
		{
			if (string.IsNullOrEmpty(url))
				return;

			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:repobrowser /path:\"{url}\"", wait);
			if (!string.IsNullOrEmpty(result.Error)) {
				Debug.LogError($"SVN Error: {result.Error}");
			}
		}

		public override void Switch(string localPath, string url, bool wait = false)
		{
			if (string.IsNullOrEmpty(localPath) || string.IsNullOrEmpty(url))
				return;

			var result = ShellUtils.ExecuteCommand(ClientCommand, $"/command:switch /path:\"{localPath}\" /url:\"{url}\"", wait);
			if (!string.IsNullOrEmpty(result.Error)) {
				Debug.LogError($"SVN Error: {result.Error}");
			}
		}
	}
#endif
}
