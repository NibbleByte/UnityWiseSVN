using DevLocker.VersionControl.WiseSVN.Shell;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace DevLocker.VersionControl.WiseSVN.ContextMenus.Implementation
{
	// SnailSVN: https://langui.net/snailsvn
	// Use the "/Applications/SnailSVN.app/Contents/Resources/snailsvn.sh" executable as much as possible.
	// usage: /Applications/SnailSVNLite.app/Contents/Resources/snailsvn.sh <subcommand> [args]
	// Available subcommands:
	// add, checkout (co), cleanup, commit (ci), delete (del, remove, rm), diff (di), export,
	// help (?, h), ignore, import, info, lock, log, merge, relocate, repo-browser (rb),
	// revert, switch, unlock, update (up)
	internal class SnailSVNContextMenus : SVNContextMenusBase
	{
		private const string ClientCommand = "/Applications/SnailSVNLite.app/Contents/Resources/snailsvn.sh";

		protected override string FileArgumentsSeparator => " ";
		protected override bool FileArgumentsSurroundQuotes => true;

		private ShellUtils.ShellResult ExecuteCommand(string command, string workingFolder, bool waitForOutput)
		{
			return ExecuteCommand(command, string.Empty, workingFolder, waitForOutput);
		}

		private ShellUtils.ShellResult ExecuteCommand(string command, string fileArgument, string workingFolder, bool waitForOutput)
		{
			return ShellUtils.ExecuteCommand(new ShellUtils.ShellArgs() {
				Command = ClientCommand,
				Args = string.IsNullOrEmpty(fileArgument) ? command : $"{command} {fileArgument}",
				WorkingDirectory = workingFolder,
				WaitForOutput = waitForOutput,
				WaitTimeout = -1,
			});
		}

		public override void CheckChanges(IEnumerable<string> assetPaths, bool includeMeta, bool wait = false)
		{
			if (!assetPaths.Any())
				return;

			var path = GetWorkingPath(assetPaths);
			if (string.IsNullOrEmpty(path))
				return;

			// The snailsvn.sh currently doesn't accept "check-for-modifications" argument, but with some reverse engineering, managed to make it work like this.
			// open "snailsvnfree://check-for-modifications/SomeFolderHere/UnityProject/Assets"
			Application.OpenURL($"snailsvnfree://check-for-modifications{WiseSVNIntegration.ProjectRootNative}/{path}");
		}

		public override void DiffChanges(string assetPath, bool wait = false)
		{
			CheckChanges(new string[] { assetPath }, false, wait);
		}

		public override void Update(IEnumerable<string> assetPaths, bool includeMeta, bool wait = false)
		{
			if (!assetPaths.Any())
				return;

			var result = ExecuteCommand("update", GetWorkingPath(assetPaths), wait);
			if (!string.IsNullOrEmpty(result.Error)) {
				Debug.LogError($"SVN Error: {result.Error}");
			}
		}

		public override void Commit(IEnumerable<string> assetPaths, bool includeMeta, bool wait = false)
		{
			if (!assetPaths.Any())
				return;

			var result = ExecuteCommand("commit", GetWorkingPath(assetPaths), wait);
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

			var pathsArg = AssetPathsToContextPaths(includeMeta ? assetPaths.Concat(metas) : assetPaths, false);

			var result = ExecuteCommand("add", pathsArg, WiseSVNIntegration.ProjectRootNative, wait);
			if (!string.IsNullOrEmpty(result.Error)) {
				Debug.LogError($"SVN Error: {result.Error}");
			}
		}

		public override void Revert(IEnumerable<string> assetPaths, bool includeMeta, bool wait = false)
		{
			if (!assetPaths.Any())
				return;

			var result = ExecuteCommand("revert", GetWorkingPath(assetPaths), wait);
			if (!string.IsNullOrEmpty(result.Error)) {
				Debug.LogError($"SVN Error: {result.Error}");
			}
		}

		public override void ResolveAll(bool wait = false)
		{
			// Doesn't support resolve command (doesn't seem to have such a window?)
			UnityEditor.EditorUtility.DisplayDialog("Not supported", "Sorry, resolve all functionality is currently not supported by SnailSVN.", "Sad");
		}

		public override void Resolve(string assetPath, bool wait = false)
		{
			// Doesn't support resolve command (doesn't seem to have such a window?)
			UnityEditor.EditorUtility.DisplayDialog("Not supported", "Sorry, resolve functionality is currently not supported by SnailSVN.", "Sad");
		}


		public override void GetLocks(IEnumerable<string> assetPaths, bool includeMeta, bool wait = false)
		{
			if (!assetPaths.Any())
				return;

			var result = ExecuteCommand("lock", AssetPathsToContextPaths(assetPaths, includeMeta), WiseSVNIntegration.ProjectRootNative, wait);
			if (!string.IsNullOrEmpty(result.Error)) {
				Debug.LogError($"SVN Error: {result.Error}");
			}
		}

		public override void ReleaseLocks(IEnumerable<string> assetPaths, bool includeMeta, bool wait = false)
		{
			if (!assetPaths.Any())
				return;

			var result = ExecuteCommand("unlock", AssetPathsToContextPaths(assetPaths, includeMeta), WiseSVNIntegration.ProjectRootNative, wait);
			if (!string.IsNullOrEmpty(result.Error)) {
				Debug.LogError($"SVN Error: {result.Error}");
			}
		}

		public override void ShowLog(string assetPath, bool wait = false)
		{
			if (string.IsNullOrEmpty(assetPath))
				return;

			var pathArg = Directory.Exists(assetPath) ? assetPath : Path.GetDirectoryName(assetPath);

			var result = ExecuteCommand("log", pathArg, wait);
			if (!string.IsNullOrEmpty(result.Error)) {
				Debug.LogError($"SVN Error: {result.Error}");
			}
		}

		public override void Blame(string assetPath, bool wait = false)
		{
			if (string.IsNullOrEmpty(assetPath))
				return;

			// Support only one file.

			// The snailsvn.sh currently doesn't accept "blame" argument, but with some reverse engineering, managed to make it work like this.
			// open "snailsvnfree://svn-blame/SomeFolderHere/UnityProject/Assets/foo.txt"
			Application.OpenURL($"snailsvnfree://svn-blame{WiseSVNIntegration.ProjectRootNative}/{assetPath}");
		}

		public override void Cleanup(bool wait = false)
		{
			// NOTE: SnailSVN doesn't pop up dialog for clean up. It just does some shady stuff in the background and a notification is shown some time later.
			var result = ExecuteCommand("cleanup", WiseSVNIntegration.ProjectRootNative, wait);
			if (!string.IsNullOrEmpty(result.Error)) {
				Debug.LogError($"SVN Error: {result.Error}");
			}
		}

		public override void RepoBrowser(string url, bool wait = false)
		{
			// SnailSVN Repo-Browser doesn't accept URLs, only working copy paths which is no good for us.
			Debug.LogError($"SnailSVN doesn't support Repo-Browser very well. Opening Repo-Browser for the current working copy.");

			var result = ExecuteCommand("repo-browser", string.Empty, WiseSVNIntegration.ProjectRootNative, wait);
			if (!string.IsNullOrEmpty(result.Error)) {
				Debug.LogError($"SVN Error: {result.Error}");
			}
		}

		public override void Switch(string localPath, string url, bool wait = false)
		{
			var result = ExecuteCommand("switch", string.Empty, WiseSVNIntegration.ProjectRootNative, wait);
			if (!string.IsNullOrEmpty(result.Error)) {
				Debug.LogError($"SVN Error: {result.Error}");
			}
		}
	}
}
