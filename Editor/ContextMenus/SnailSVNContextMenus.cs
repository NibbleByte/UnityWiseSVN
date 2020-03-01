using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DevLocker.VersionControl.WiseSVN.ContextMenus.Implementation
{

#if !UNITY_EDITOR_WIN
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

		public override void CheckChangesAll()
		{
			// The snailsvn.sh currently doesn't accept "check-for-modifications" argument, but with some reverse engineering, managed to make it work like this.
			// open "snailsvnfree://check-for-modifications/SomeFolderHere/UnityProject/Assets"
			Application.OpenURL($"snailsvnfree://check-for-modifications{WiseSVNIntegration.ProjectRoot}");
		}

		public override void CheckChanges()
		{
			var path = GetWorkingPath(Selection.assetGUIDs);
			if (string.IsNullOrEmpty(path))
				return;

			// The snailsvn.sh currently doesn't accept "check-for-modifications" argument, but with some reverse engineering, managed to make it work like this.
			// open "snailsvnfree://check-for-modifications/SomeFolderHere/UnityProject/Assets"
			Application.OpenURL($"snailsvnfree://check-for-modifications{WiseSVNIntegration.ProjectRoot}/{path}");
		}

		public override void Update(string filePath)
		{
			var result = ExecuteCommand("update", Path.GetDirectoryName(filePath), false);
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
			}
		}

		public override void UpdateAll()
		{
			var result = ExecuteCommand("update", WiseSVNIntegration.ProjectRoot, true);
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
			}
		}

		public override void CommitAll()
		{
			var result = ExecuteCommand("commit", WiseSVNIntegration.ProjectRoot, false);
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
			}
		}

		public override void CommitSelected()
		{
			var result = ExecuteCommand("commit", GetWorkingPath(Selection.assetGUIDs), false);
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

			var result = ExecuteCommand("add", pathsArg, WiseSVNIntegration.ProjectRoot, false);
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
			}
		}

		public override void RevertAll()
		{
			var result = ExecuteCommand("revert", WiseSVNIntegration.ProjectRoot, false);
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
			}
		}

		public override void RevertSelected()
		{
			var result = ExecuteCommand("revert", GetWorkingPath(Selection.assetGUIDs), false);
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
			}
		}

		public override void ResolveAll()
		{
			// Doesn't support resolve command (doesn't seem to have such a window?)
			EditorUtility.DisplayDialog("Not supported", "Sorry, resolve all functionality is currently not supported by SnailSVN.", "Sad");
		}


		public override void GetLocks()
		{
			var result = ExecuteCommand("lock", GuidsToContextPaths(Selection.assetGUIDs, true), WiseSVNIntegration.ProjectRoot, false);
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
			}
		}

		public override void ReleaseLocks()
		{
			var result = ExecuteCommand("unlock", GuidsToContextPaths(Selection.assetGUIDs, true), WiseSVNIntegration.ProjectRoot, false);
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
			}
		}

		public override void ShowLogAll()
		{
			var result = ExecuteCommand("log", WiseSVNIntegration.ProjectRoot, false);
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
			}
		}

		public override void ShowLog()
		{
			var result = ExecuteCommand("log", GetWorkingPath(Selection.assetGUIDs), false);
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
			}
		}

		public override void Blame()
		{
			// Support only one file.
			var path = AssetDatabase.GUIDToAssetPath(Selection.assetGUIDs.FirstOrDefault());

			if (string.IsNullOrEmpty(path))
				return;

			// The snailsvn.sh currently doesn't accept "blame" argument, but with some reverse engineering, managed to make it work like this.
			// open "snailsvnfree://svn-blame/SomeFolderHere/UnityProject/Assets/foo.txt"
			Application.OpenURL($"snailsvnfree://svn-blame{WiseSVNIntegration.ProjectRoot}/{path}");
		}

		public override void Cleanup()
		{
			// NOTE: SnailSVN doesn't pop up dialog for clean up. It just does some shady stuff in the background and a notification is shown some time later.
			var result = ExecuteCommand("cleanup", WiseSVNIntegration.ProjectRoot, false);
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
			}
		}
	}
#endif
}
