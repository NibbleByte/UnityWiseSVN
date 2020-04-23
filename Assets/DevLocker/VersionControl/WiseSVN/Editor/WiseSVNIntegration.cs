using DevLocker.VersionControl.WiseSVN.Shell;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DevLocker.VersionControl.WiseSVN
{
	// SVN console commands: https://tortoisesvn.net/docs/nightly/TortoiseSVN_en/tsvn-cli-main.html
	[InitializeOnLoad]
	public class WiseSVNIntegration : UnityEditor.AssetModificationProcessor
	{
		private static readonly Dictionary<char, VCFileStatus> m_FileStatusMap = new Dictionary<char, VCFileStatus>
		{
			{' ', VCFileStatus.Normal},
			{'A', VCFileStatus.Added},
			{'C', VCFileStatus.Conflicted},
			{'D', VCFileStatus.Deleted},
			{'I', VCFileStatus.Ignored},
			{'M', VCFileStatus.Modified},
			{'R', VCFileStatus.Replaced},
			{'?', VCFileStatus.Unversioned},
			{'!', VCFileStatus.Missing},
			{'X', VCFileStatus.External},
			{'~', VCFileStatus.Obstructed},
		};

		private static readonly Dictionary<char, VCLockStatus> m_LockStatusMap = new Dictionary<char, VCLockStatus>
		{
			{' ', VCLockStatus.NoLock},
			{'K', VCLockStatus.LockedHere},
			{'O', VCLockStatus.LockedOther},
			{'T', VCLockStatus.LockedButStolen},
			{'B', VCLockStatus.BrokenLock},
		};

		private static readonly Dictionary<char, VCProperty> m_PropertyStatusMap = new Dictionary<char, VCProperty>
		{
			{' ', VCProperty.Normal},
			{'C', VCProperty.Conflicted},
			{'M', VCProperty.Modified},
		};

		private static readonly Dictionary<char, VCTreeConflictStatus> m_ConflictStatusMap = new Dictionary<char, VCTreeConflictStatus>
		{
			{' ', VCTreeConflictStatus.Normal},
			{'C', VCTreeConflictStatus.TreeConflict},
		};

		private static readonly Dictionary<char, VCRemoteFileStatus> m_RemoteStatusMap = new Dictionary<char, VCRemoteFileStatus>
		{
			{' ', VCRemoteFileStatus.None},
			{'*', VCRemoteFileStatus.Modified},
		};


		private static readonly Dictionary<UpdateResolveConflicts, string> m_UpdateResolveConflictsMap = new Dictionary<UpdateResolveConflicts, string>
		{
			{UpdateResolveConflicts.Postpone, "postpone"},
			{UpdateResolveConflicts.Working, "working"},
			{UpdateResolveConflicts.Base, "base"},
			{UpdateResolveConflicts.MineConflict, "mine-conflict"},
			{UpdateResolveConflicts.TheirsConflict, "theirs-conflict"},
			{UpdateResolveConflicts.MineFull, "mine-full"},
			{UpdateResolveConflicts.TheirsFull, "theirs-full"},
			{UpdateResolveConflicts.Edit, "edit"},
			{UpdateResolveConflicts.Launch, "launch"},
		};

		public static readonly string ProjectRoot;

		public static event Action ShowChangesUI;

		public static bool Enabled => m_PersonalPrefs.EnableCoreIntegration;
		public static bool TemporaryDisabled => m_TemporaryDisabledCount > 0;	// Temporarily disable the integration (by code).
		public static bool Silent => m_SilenceCount > 0;	// Do not show dialogs

		public static SVNTraceLogs TraceLogs => m_PersonalPrefs.TraceLogs;

		private static int m_SilenceCount = 0;
		private static int m_TemporaryDisabledCount = 0;

		private static SVNPreferencesManager.PersonalPreferences m_PersonalPrefs => SVNPreferencesManager.Instance.PersonalPrefs;
		private static SVNPreferencesManager.ProjectPreferences m_ProjectPrefs => SVNPreferencesManager.Instance.ProjectPrefs;



		private static string SVN_Command => string.IsNullOrEmpty(m_ProjectPrefs.PlatformSvnCLIPath)
			? "svn"
			: Path.Combine(ProjectRoot, m_ProjectPrefs.PlatformSvnCLIPath);

		internal const int COMMAND_TIMEOUT = 15000;	// Milliseconds
		internal const int ONLINE_COMMAND_TIMEOUT = 45000;	// Milliseconds

		#region Logging

		private class ResultReporter : IShellMonitor, IDisposable
		{
			private readonly ConcurrentQueue<string> m_CombinedOutput = new ConcurrentQueue<string>();

			private bool m_HasErrors = false;
			private bool m_HasCommand = false;
			private bool m_LogOutput;
			private bool m_Silent;


			public ResultReporter(bool logOutput, bool silent, string initialText = "")
			{
				m_LogOutput = logOutput;
				m_Silent = silent;

				if (!string.IsNullOrEmpty(initialText)) {
					m_CombinedOutput.Enqueue(initialText);
				}
			}

			public bool AbortRequested { get; private set; }
			public event ShellRequestAbortEventHandler RequestAbort;

			public void AppendCommand(string command, string args)
			{
				m_CombinedOutput.Enqueue(command + " " + args);
				m_HasCommand = true;
			}

			public void AppendOutputLine(string line)
			{
				// Not used for now...
			}

			public void AppendErrorLine(string line)
			{
				m_CombinedOutput.Enqueue(line);
				m_HasErrors = true;
			}

			public void Abort(bool kill)
			{
				AbortRequested = true;
				RequestAbort?.Invoke(kill);
			}

			public void Dispose()
			{
				if (!m_CombinedOutput.IsEmpty) {
					StringBuilder output = new StringBuilder();
					string line;
					while(m_CombinedOutput.TryDequeue(out line)) {
						output.AppendLine(line);
					}

					if (m_HasErrors) {
						Debug.LogError(output);
						if (!m_Silent) {
							EditorUtility.DisplayDialog("SVN Error",
								"SVN error happened while processing the assets. Check the logs.", "I will!");
						}
					} else if (m_LogOutput && m_HasCommand) {
						Debug.Log(output);
					}
				}
			}
		}

		private static ResultReporter CreateReporter()
		{
			var logger = new ResultReporter((TraceLogs & SVNTraceLogs.SVNOperations) != 0, Silent, "SVN Operations:");

			return logger;
		}

		#endregion

		static WiseSVNIntegration()
		{
			ProjectRoot = Path.GetDirectoryName(Application.dataPath);
		}

		// NOTE: This is called separately for the file and its meta.
		private static void OnWillCreateAsset(string path)
		{
			if (!Enabled || TemporaryDisabled)
				return;

			var pathStatusData = GetStatus(path);
			if (pathStatusData.Status == VCFileStatus.Deleted) {

				var isMeta = path.EndsWith(".meta");

				if (!isMeta && !Silent) {
					EditorUtility.DisplayDialog(
						"Deleted file",
						$"The desired location\n\"{path}\"\nis marked as deleted in SVN. The file will be replaced in SVN with the new one.\n\nIf this is an automated change, consider adding this file to the exclusion list in the project preferences:\n\"{WiseSVNProjectPreferencesWindow.PROJECT_PREFERENCES_MENU}\"\n...or change your tool to silence the integration.",
						"Replace");
				}

				using (var reporter = CreateReporter()) {
					// File isn't still created, so we need to improvise.
					var result = ShellUtils.ExecuteCommand(SVN_Command, $"revert \"{SVNFormatPath(path)}\"", COMMAND_TIMEOUT, reporter);
					File.Delete(path);
				}
			}
		}

		private static AssetDeleteResult OnWillDeleteAsset(string path, RemoveAssetOptions option)
		{
			if (!Enabled || TemporaryDisabled || m_ProjectPrefs.Exclude.Any(path.StartsWith))
				return AssetDeleteResult.DidNotDelete;

			var oldStatus = GetStatus(path).Status;

			if (oldStatus == VCFileStatus.Unversioned)
				return AssetDeleteResult.DidNotDelete;

			using (var reporter = CreateReporter()) {

				var result = ShellUtils.ExecuteCommand(SVN_Command, $"delete --force \"{SVNFormatPath(path)}\"", COMMAND_TIMEOUT, reporter);
				if (result.HasErrors)
					return AssetDeleteResult.FailedDelete;

				result = ShellUtils.ExecuteCommand(SVN_Command, $"delete --force \"{SVNFormatPath(path + ".meta")}\"", COMMAND_TIMEOUT, reporter);
				if (result.HasErrors)
					return AssetDeleteResult.FailedDelete;

				return AssetDeleteResult.DidDelete;
			}
		}

		private static AssetMoveResult OnWillMoveAsset(string oldPath, string newPath)
		{
			if (!Enabled || TemporaryDisabled || m_ProjectPrefs.Exclude.Any(oldPath.StartsWith))
				return AssetMoveResult.DidNotMove;

			var oldStatusData = GetStatus(oldPath);

			if (oldStatusData.Status == VCFileStatus.Unversioned) {

				var newStatusData = GetStatus(newPath);
				if (newStatusData.Status == VCFileStatus.Deleted) {
					if (Silent || EditorUtility.DisplayDialog(
						"Deleted file",
						$"The desired location\n\"{newPath}\"\nis marked as deleted in SVN. Are you trying to replace it with a new one?",
						"Replace",
						"Cancel")) {

						using (var reporter = CreateReporter()) {
							if (SVNReplaceFile(oldPath, newPath, reporter)) {
								return AssetMoveResult.DidMove;
							}
						}

					}

					return AssetMoveResult.FailedMove;
				}

				return AssetMoveResult.DidNotMove;
			}

			if (oldStatusData.IsConflicted || (Directory.Exists(oldPath) && HasConflictsAny(oldPath))) {
				if (Silent || EditorUtility.DisplayDialog(
					"Conflicted files",
					$"Failed to move the files\n\"{oldPath}\"\nbecause it has conflicts. Resolve them first!",
					"Check changes",
					"Cancel")) {
					ShowChangesUI?.Invoke();
				}

				return AssetMoveResult.FailedMove;
			}

			using (var reporter = CreateReporter()) {

				if (!CheckAndAddParentFolderIfNeeded(newPath, reporter))
					return AssetMoveResult.FailedMove;

				var result = ShellUtils.ExecuteCommand(SVN_Command, $"move \"{SVNFormatPath(oldPath)}\" \"{newPath}\"", COMMAND_TIMEOUT, reporter);
				if (result.HasErrors)
					return AssetMoveResult.FailedMove;

				result = ShellUtils.ExecuteCommand(SVN_Command, $"move \"{SVNFormatPath(oldPath + ".meta")}\" \"{newPath}.meta\"", COMMAND_TIMEOUT, reporter);
				if (result.HasErrors)
					return AssetMoveResult.FailedMove;

				return AssetMoveResult.DidMove;
			}
		}

		public static bool CheckAndAddParentFolderIfNeeded(string path)
		{
			using (var reporter = CreateReporter()) {
				return CheckAndAddParentFolderIfNeeded(path, reporter);
			}
		}

		private static bool CheckAndAddParentFolderIfNeeded(string path, IShellMonitor shellMonitor = null)
		{
			var directory = Path.GetDirectoryName(path);

			// Special case - Root folders like Assets, ProjectSettings, etc...
			if (string.IsNullOrEmpty(directory)) {
				directory = ".";
			}

			var newDirectoryStatusData = GetStatus(directory);
			if (newDirectoryStatusData.IsConflicted) {
				if (Silent || EditorUtility.DisplayDialog(
					"Conflicted files",
					$"Failed to move the files to \n\"{directory}\"\nbecause it has conflicts. Resolve them first!",
					"Check changes",
					"Cancel")) {
					ShowChangesUI?.Invoke();
				}

				return false;
			}

			// Moving to unversioned folder -> add it to svn.
			if (newDirectoryStatusData.Status == VCFileStatus.Unversioned) {

				if (!Silent && !EditorUtility.DisplayDialog(
					"Unversioned directory",
					$"The target directory:\n\"{directory}\"\nis not under SVN control. Should it be added?",
					"Add it!",
					"Cancel"
				))
					return false;

				if (!SVNAddDirectory(directory, shellMonitor))
					return false;

			}

			return true;
		}

		// Adds all parent unversioned folders AND THEIR META FILES!
		private static bool SVNAddDirectory(string newDirectory, IShellMonitor shellMonitor = null)
		{
			// --parents will add all unversioned parent directories as well.
			var result = ShellUtils.ExecuteCommand(SVN_Command, $"add --parents --depth empty \"{SVNFormatPath(newDirectory)}\"", COMMAND_TIMEOUT, shellMonitor);
			if (result.HasErrors)
				return false;

			// If working outside Assets folder, don't consider metas.
			if (!newDirectory.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
				return true;

			// Now add all folder metas upwards
			var directoryMeta = newDirectory + ".meta";
			var directoryMetaStatus = GetStatus(directoryMeta).Status; // Will be unversioned.
			while (directoryMetaStatus == VCFileStatus.Unversioned) {

				result = ShellUtils.ExecuteCommand(SVN_Command, $"add \"{SVNFormatPath(directoryMeta)}\"", COMMAND_TIMEOUT, shellMonitor);
				if (result.HasErrors)
					return false;

				directoryMeta = Path.GetDirectoryName(directoryMeta) + ".meta";
				directoryMetaStatus = GetStatus(directoryMeta).Status;
			}

			return true;
		}

		private static bool SVNReplaceFile(string oldPath, string newPath, IShellMonitor shellMonitor = null)
		{
			File.Move(oldPath, newPath);
			File.Move(oldPath + ".meta", newPath + ".meta");

			var result = ShellUtils.ExecuteCommand(SVN_Command, $"add \"{SVNFormatPath(newPath)}\"", COMMAND_TIMEOUT, shellMonitor);
			if (result.HasErrors)
				return false;

			result = ShellUtils.ExecuteCommand(SVN_Command, $"add \"{SVNFormatPath(newPath + ".meta")}\"", COMMAND_TIMEOUT, shellMonitor);
			if (result.HasErrors)
				return false;

			return false;
		}


		private static bool IsCriticalError(string error, out string displayMessage)
		{
			// svn: warning: W155010: The node '...' was not found.
			// This can be returned when path is under unversioned directory. In that case we consider it is unversioned as well.
			if (error.Contains("W155010")) {
				displayMessage = string.Empty;
				return false;
			}

			// svn: warning: W155007: '...' is not a working copy!
			// This can be returned when project is not a valid svn checkout. (Probably)
			if (error.Contains("W155007")) {
				displayMessage = string.Empty;
				return false;
			}

			// System.ComponentModel.Win32Exception (0x80004005): ApplicationName='...', CommandLine='...', Native error= The system cannot find the file specified.
			// Could not find the command executable. The user hasn't installed their CLI (Command Line Interface) so we're missing an "svn.exe" in the PATH environment.
			// This is allowed only if there isn't ProjectPreference specified CLI path.
			if (error.Contains("0x80004005") && string.IsNullOrEmpty(m_ProjectPrefs.PlatformSvnCLIPath)) {
				displayMessage = $"SVN CLI (Command Line Interface) not found. " +
					$"Please install it or specify path to a valid svn.exe in the svn preferences at:\n{WiseSVNProjectPreferencesWindow.PROJECT_PREFERENCES_MENU}\n\n" +
					$"You can also disable the SVN integration.";

				return false;
			}

			// Same as above but the specified svn.exe in the project preferences is missing.
			if (error.Contains("0x80004005") && !string.IsNullOrEmpty(m_ProjectPrefs.PlatformSvnCLIPath)) {
				displayMessage = $"Cannot find the specified in the svn project preferences svn.exe:\n{m_ProjectPrefs.PlatformSvnCLIPath}\n\n" +
					$"You can reconfigure the svn preferences at:\n{WiseSVNProjectPreferencesWindow.PROJECT_PREFERENCES_MENU}\n\n" +
					$"You can also disable the SVN integration.";

				return false;
			}

			displayMessage = "SVN error happened while processing the assets. Check the logs.";
			return true;
		}

		private static IEnumerable<SVNStatusData> ExtractStatuses(string output, SVNStatusDataOptions options, IShellMonitor shellMonitor = null)
		{
			using (var sr = new StringReader(output)) {
				string line;
				while ((line = sr.ReadLine()) != null) {

					var lineLen = line.Length;

					// Last status was deleted / added+, so this is telling us where it moved to / from. Skip it.
					if (lineLen > 8 && line[8] == '>')
						continue;

					// Tree conflict "local dir edit, incoming dir delete or move upon switch / update" or similar.
					if (lineLen > 6 && line[6] == '>')
						continue;

					// If there are any conflicts, the report will have two additional lines like this:
					// Summary of conflicts:
					// Text conflicts: 1
					if (line.StartsWith("Summary", StringComparison.Ordinal))
						break;

					// If -u is used, additional line is added at the end:
					// Status against revision:     14
					if (line.StartsWith("Status", StringComparison.Ordinal))
						break;

					// All externals append separate sections with their statuses:
					// Performing status on external item at '...':
					if (line.StartsWith("Performing status", StringComparison.Ordinal))
						continue;

					// If user has files in the "ignore-on-commit" list, this is added at the end plus empty line:
					// ---Changelist 'ignore-on-commit': ...
					if (string.IsNullOrEmpty(line))
						continue;
					if (line.StartsWith("---", StringComparison.Ordinal))
						break;

					// Rules are described in "svn help status".
					var statusData = new SVNStatusData();
					statusData.Status = m_FileStatusMap[line[0]];
					statusData.PropertyStatus = m_PropertyStatusMap[line[1]];
					statusData.LockStatus = m_LockStatusMap[line[5]];
					statusData.TreeConflictStatus = m_ConflictStatusMap[line[6]];
					statusData.LockDetails = LockDetails.Empty;

					// 7 columns statuses + space;
					int pathStart = 7 + 1;

					if (!options.Offline) {
						// + remote status + revision
						pathStart += 13;
						statusData.RemoteStatus = m_RemoteStatusMap[line[8]];
					}

					statusData.Path = line.Substring(pathStart);

					// NOTE: If you pass absolute path to svn, the output will be with absolute path -> always pass relative path and we'll be good.
					// If path is not relative, make it.
					//if (!statusData.Path.StartsWith("Assets", StringComparison.Ordinal)) {
					//	// Length+1 to skip '/'
					//	statusData.Path = statusData.Path.Remove(0, ProjectRoot.Length + 1);
					//}

					if (IsHiddenPath(statusData.Path))
						continue;


					if (!options.Offline && options.FetchLockOwner) {
						if (statusData.LockStatus != VCLockStatus.NoLock && statusData.LockStatus != VCLockStatus.BrokenLock) {
							statusData.LockDetails = FetchLockDetails(statusData.Path, options.Timeout, options.RaiseError, shellMonitor);
						}
					}

					yield return statusData;
				}
			}
		}

		// Ask the repository server for lock details of the specified file.
		// NOTE: This is synchronous operation. Better use the Async method version to avoid editor slow down.
		public static LockDetails FetchLockDetails(string path, int timeout = ONLINE_COMMAND_TIMEOUT, bool raiseError = false, IShellMonitor shellMonitor = null)
		{
			string url;
			LockDetails lockDetails = LockDetails.Empty;

			//
			// Find the repository url of the path.
			// We need to call "svn info [repo-url]" in order to get up to date repository information.
			// NOTE: Project url can be cached and prepended to path, but externals may have different base url.
			//
			{
				var result = ShellUtils.ExecuteCommand(SVN_Command, $"info \"{SVNFormatPath(path)}\"", timeout, shellMonitor);

				url = ExtractLineValue("URL:", result.Output);

				if (!string.IsNullOrEmpty(result.Error) || string.IsNullOrEmpty(url)) {

					if (!raiseError || Silent)
						return lockDetails;

					var displayMessage = $"Failed to get info for \"{path}\".\n{result.Output}\n{result.Error}";
					if (m_LastDisplayedError != displayMessage) {
						Debug.LogError($"{displayMessage}\n\n{result.Error}");
						m_LastDisplayedError = displayMessage;
						EditorUtility.DisplayDialog("SVN Error", displayMessage, "I will!");
					}


					return lockDetails;
				}
			}

			//
			// Get the actual owner from the repository (using the url).
			//
			{
				var result = ShellUtils.ExecuteCommand(SVN_Command, $"info \"{SVNFormatPath(url)}\"", timeout, shellMonitor);

				lockDetails.Owner = ExtractLineValue("Lock Owner:", result.Output);

				if (!string.IsNullOrEmpty(result.Error) || string.IsNullOrEmpty(lockDetails.Owner)) {

					// Owner will be missing if there is no lock. If true, just find something familiar to confirm it was not an error.
					if (result.Output.IndexOf("URL:", StringComparison.OrdinalIgnoreCase) != -1) {
						lockDetails.Path = path;	// LockDetails is still valid, just no lock.
						return lockDetails;
					}

					if (!raiseError || Silent)
						return lockDetails;

					var displayMessage = $"Failed to get lock details for \"{path}\".\n{result.Output}\n{result.Error}";
					if (m_LastDisplayedError != displayMessage) {
						Debug.LogError($"{displayMessage}\n\n{result.Error}");
						m_LastDisplayedError = displayMessage;
						EditorUtility.DisplayDialog("SVN Error", displayMessage, "I will!");
					}

					return lockDetails;
				}

				lockDetails.Path = path;
				lockDetails.Date = ExtractLineValue("Lock Created:", result.Output);

				// Locked message looks like this:
				// Lock Comment (4 lines):
				// Foo
				// Bar
				// ...
				// The number of lines is arbitrary. If there is no comment, this section is omitted.
				var lockMessageLineIndex = result.Output.IndexOf("Lock Comment", StringComparison.OrdinalIgnoreCase);
				if (lockMessageLineIndex != -1) {
					var lockMessageStart = result.Output.IndexOf("\n", lockMessageLineIndex, StringComparison.OrdinalIgnoreCase) + 1;
					lockDetails.Message = result.Output.Substring(lockMessageStart).Replace("\r", "");
					// Fuck '\r'
				}
			}

			return lockDetails;
		}

		// Ask the repository server for lock details of the specified file.
		// NOTE: If assembly reload happens, request will be lost, complete handler won't be called.
		public static SVNAsyncOperation<LockDetails> FetchLockDetailsAsync(string path, int timeout = -1)
		{
			return SVNAsyncOperation<LockDetails>.Start(op => FetchLockDetails(path, timeout, false, op));
		}

		// Lock a file on the repository server.
		// NOTE: This is synchronous operation. Better use the Async method version to avoid editor slow down.
		public static LockOperationResult LockFile(string path, bool force, string message = "", string encoding = "", int timeout = ONLINE_COMMAND_TIMEOUT, IShellMonitor shellMonitor = null)
		{
			var messageArg = string.IsNullOrEmpty(message) ? string.Empty : $"--message \"{message}\"";
			var encodingArg = string.IsNullOrEmpty(encoding) ? string.Empty : $"--encoding \"{encoding}\"";
			var forceArg = force ? "--force" : string.Empty;

			var result = ShellUtils.ExecuteCommand(SVN_Command, $"lock {forceArg} {messageArg} {encodingArg} \"{SVNFormatPath(path)}\"", timeout, shellMonitor);

			// svn: warning: W160035: Path '...' is already locked by user '...'
			// File is already locked by another working copy (can be the same user). Use force to re-lock it.
			// This happens even if this working copy got the lock.
			if (result.Error.Contains("W160035"))
				return LockOperationResult.LockedByOther;

			if (!string.IsNullOrEmpty(result.Error)) {

				// Unable to connect to repository indicating some network or server problems.
				// svn: E170013: Unable to connect to a repository at URL '...'
				// svn: E731001: No such host is known.
				if (result.Error.Contains("E170013") || result.Error.Contains("E731001"))
					return LockOperationResult.UnableToConnectError;

				return LockOperationResult.UnknownError;
			}

			// '... some file ...' locked by user '...'.
			if (result.Output.Contains("locked by user"))
				return LockOperationResult.Success;

			return LockOperationResult.UnknownError;
		}

		// Lock a file on the repository server.
		// NOTE: If assembly reload happens, task will be lost, complete handler won't be called.
		public static SVNAsyncOperation<LockOperationResult> LockFileAsync(string path, bool force, string message = "", string encoding = "", int timeout = -1)
		{
			return SVNAsyncOperation<LockOperationResult>.Start(op => LockFile(path, force, message, encoding, timeout, op));
		}

		// Unlock a file on the repository server.
		// NOTE: This is synchronous operation. Better use the Async method version to avoid editor slow down.
		public static LockOperationResult UnlockFile(string path, bool force, int timeout = ONLINE_COMMAND_TIMEOUT, IShellMonitor shellMonitor = null)
		{
			var forceArg = force ? "--force" : string.Empty;

			var result = ShellUtils.ExecuteCommand(SVN_Command, $"unlock {forceArg} \"{SVNFormatPath(path)}\"", timeout, shellMonitor);

			// svn: E195013: '...' is not locked in this working copy
			// This working copy doesn't own a lock to this file (when used without force flag, offline check).
			if (result.Error.Contains("E195013"))
				return LockOperationResult.Success;

			// svn: warning: W170007: '...' is not locked in the repository
			// File is already unlocked (when used with force flag).
			if (result.Error.Contains("W170007"))
				return LockOperationResult.Success;

			// svn: warning: W160040: No lock on path '...' (400 Bad Request)
			// This working copy owned the lock, but it got stolen or broken (when used without force flag).
			// After this operation, this working copy will destroy its lock, so this message will show up only once.
			if (result.Error.Contains("W160040"))
				return LockOperationResult.LockedByOther;

			if (!string.IsNullOrEmpty(result.Error)) {

				// Unable to connect to repository indicating some network or server problems.
				// svn: E170013: Unable to connect to a repository at URL '...'
				// svn: E731001: No such host is known.
				if (result.Error.Contains("E170013") || result.Error.Contains("E731001"))
					return LockOperationResult.UnableToConnectError;

				return LockOperationResult.UnknownError;
			}

			// '...' unlocked.
			if (result.Output.Contains("unlocked"))
				return LockOperationResult.Success;

			if (!Silent) {
				Debug.LogError($"Failed to lock \"{path}\".\n\n{result.Output} ");
			}

			return LockOperationResult.UnknownError;
		}

		// Unlock a file on the repository server.
		// NOTE: If assembly reload happens, task will be lost, complete handler won't be called.
		public static SVNAsyncOperation<LockOperationResult> UnlockFileAsync(string path, bool force, int timeout = -1)
		{
			return SVNAsyncOperation<LockOperationResult>.Start(op => UnlockFile(path, force, timeout, op));
		}

		// Add files to SVN directly (without GUI).
		public static bool Add(string path, bool includeMeta, bool recursive, IShellMonitor shellMonitor = null)
		{
			if (string.IsNullOrEmpty(path))
				return true;

			try {
				RequestSilence();

				// Will add parent folders and their metas.
				var success = CheckAndAddParentFolderIfNeeded(path, shellMonitor);
				if (success == false)
					return false;

				var depth = recursive ? "infinity" : "empty";
				var result = ShellUtils.ExecuteCommand(SVN_Command, $"add --depth {depth} --force \"{SVNFormatPath(path)}\"", COMMAND_TIMEOUT, shellMonitor);
				if (result.HasErrors)
					return false;

				if (includeMeta) {
					result = ShellUtils.ExecuteCommand(SVN_Command, $"add --depth {depth} --force \"{SVNFormatPath(path + ".meta")}\"", COMMAND_TIMEOUT, shellMonitor);
					if (result.HasErrors)
						return false;
				}

				return true;

			} finally {
				ClearSilence();
			}
		}

		// Update file or folder in SVN directly (without GUI).
		// The force param will auto-resolve tree conflicts occurring on incoming new files (add) over existing unversioned files in the working copy.
		// NOTE: This is synchronous operation. Better use the Async method version to avoid editor slow down.
		public static UpdateOperationResult Update(string path,
			UpdateResolveConflicts resolveConflicts = UpdateResolveConflicts.Postpone,
			bool force = false,
			int revision = -1,
			int timeout = -1,
			IShellMonitor shellMonitor = null
			)
		{

			var depth = "infinity"; // Recursive whether it is a file or a folder. Keep it simple for now.
			string acceptArg = $"--accept {m_UpdateResolveConflictsMap[resolveConflicts]}";
			var forceArg = force ? $"--force" : "";
			var revisionArg = revision > 0 ? $"--revision {revision}" : "";

			var result = ShellUtils.ExecuteCommand(SVN_Command, $"update --depth {depth} {acceptArg} {forceArg} {revisionArg} \"{SVNFormatPath(path)}\"", timeout, shellMonitor);
			if (result.HasErrors) {

				// Tree conflicts limit the auto-resolve capabilities. In that case "Summary of conflicts" is not shown.
				// svn: E155027: Tree conflict can only be resolved to 'working' state; '...' not resolved
				if (result.Error.Contains("E155027"))
					return UpdateOperationResult.SuccessWithConflicts;

				// Unable to connect to repository indicating some network or server problems.
				// svn: E170013: Unable to connect to a repository at URL '...'
				// svn: E731001: No such host is known.
				if (result.Error.Contains("E170013") || result.Error.Contains("E731001"))
					return UpdateOperationResult.UnableToConnectError;

				return UpdateOperationResult.UnknownError;
			}


			// Update was successful, but some folders/files have conflicts. Some of them might get auto-resolved (depending on the resolveConflicts param)
			// Summary of conflicts:
			//  Text conflicts: 1
			//  Tree conflicts: 2
			// -- OR --
			//  Text conflicts: 0 remaining (and 1 already resolved)
			//  Tree conflicts: 0 remaining (and 1 already resolved)
			if (result.Output.Contains("Summary of conflicts:")) {
				// Depending on the resolveConflicts param, conflicts may auto-resolve. Check if they did.
				var TEXT_CONFLICTS = "Text conflicts: ";    // Space at the end is important.
				var TREE_CONFLICTS = "Tree conflicts: ";
				var textConflictsIndex = result.Output.IndexOf(TEXT_CONFLICTS);
				var treeConflictsIndex = result.Output.IndexOf(TREE_CONFLICTS);
				var noTextConflicts = textConflictsIndex == -1 || result.Output[textConflictsIndex + 1] == '0';
				var noTreeConflicts = treeConflictsIndex == -1 || result.Output[treeConflictsIndex + 1] == '0';

				return noTextConflicts && noTreeConflicts ? UpdateOperationResult.Success : UpdateOperationResult.SuccessWithConflicts;
			}

			return UpdateOperationResult.Success;
		}

		// Update file or folder in SVN directly (without GUI).
		// The force param will auto-resolve tree conflicts occurring on incoming new files (add) over existing unversioned files in the working copy.
		// DANGER: SVN updating while editor is crunching assets IS DANGEROUS! It WILL corrupt your asset guids. Use with caution!!!
		public static SVNAsyncOperation<UpdateOperationResult> UpdateAsyncDANGER(string path,
			UpdateResolveConflicts resolveConflicts = UpdateResolveConflicts.Postpone,
			bool force = false,
			int revision = -1,
			int timeout = -1
			)
		{
			return SVNAsyncOperation<UpdateOperationResult>.Start(op => Update(path, resolveConflicts, force, revision, timeout, op));
		}

		// Commit files to SVN directly (without GUI).
		// On commit all included locks will be unlocked unless specified not to by the keepLocks param.
		// NOTE: This is synchronous operation. Better use the Async method version to avoid editor slow down.
		public static CommitOperationResult Commit(IEnumerable<string> assetPaths,
			bool includeMeta,
			bool recursive,
			string message,
			string encoding = "",
			bool keepLocks = false,
			int timeout = -1,
			IShellMonitor shellMonitor = null
			)
		{
			var targetsFile = FileUtil.GetUniqueTempPathInProject();
			if (includeMeta) {
				assetPaths = assetPaths.Select(path => path + ".meta").Concat(assetPaths);
			}
			File.WriteAllLines(targetsFile, assetPaths.Select(SVNFormatPath));


			var depth = recursive ? "infinity" : "empty";
			var encodingArg = string.IsNullOrEmpty(encoding) ? "" : $"--encoding {encoding}";
			var keepLocksArg = keepLocks ? "--no-unlock" : "";

			var result = ShellUtils.ExecuteCommand(SVN_Command, $"commit --targets \"{targetsFile}\" --depth {depth} --message \"{message}\" {encodingArg} {keepLocksArg}", timeout, shellMonitor);
			if (result.HasErrors) {

				// Some folders/files have pending changes in the repository. Update them before trying to commit.
				// svn: E155011: File '...' is out of date
				// svn: E160024: resource out of date; try updating
				if (result.Error.Contains("E160024"))
					return CommitOperationResult.OutOfDateError;

				// Some folders/files have conflicts. Clear them before trying to commit.
				// svn: E155015: Aborting commit: '...' remains in conflict
				if (result.Error.Contains("E155015"))
					return CommitOperationResult.ConflictsError;

				// Can't commit unversioned files directly. Add them before trying to commit. Recursive skips unversioned files.
				// svn: E200009: '...' is not under version control
				if (result.Error.Contains("E200009"))
					return CommitOperationResult.UnversionedError;

				// Precommit hook denied the commit on the server side. Talk with your administrator about your commit company policies. Example: always commit with a valid message.
				// svn: E165001: Commit blocked by pre-commit hook (exit code 1) with output: ...
				if (result.Error.Contains("E165001"))
					return CommitOperationResult.PrecommitHookError;

				// Unable to connect to repository indicating some network or server problems.
				// svn: E170013: Unable to connect to a repository at URL '...'
				// svn: E731001: No such host is known.
				if (result.Error.Contains("E170013") || result.Error.Contains("E731001"))
					return CommitOperationResult.UnableToConnectError;

				return CommitOperationResult.UnknownError;
			}

			return CommitOperationResult.Success;
		}

		// Commit files to SVN directly (without GUI).
		// On commit all included locks will be unlocked unless specified not to by the keepLocks param.
		public static SVNAsyncOperation<CommitOperationResult> CommitAsync(IEnumerable<string> assetPaths, bool includeMeta, bool recursive, string message, string encoding = "", bool keepLocks = false, int timeout = -1)
		{
			return SVNAsyncOperation<CommitOperationResult>.Start(op => Commit(assetPaths, includeMeta, recursive, message, encoding, keepLocks, timeout, op));
		}

		// Used to avoid spam (specially when importing the whole project and errors start popping up, interrupting the process).
		[NonSerialized]
		private static string m_LastDisplayedError = string.Empty;


		// Get statuses of files based on the options you provide.
		// NOTE: data is returned ONLY for folders / files that has something to show (has changes, locks or remote changes).
		//		 If used with non-recursive option it will return single data with normal status (if non).
		// NOTE2: this is a synchronous operation.
		//		 If you use it in online mode it might freeze your code for a long time.
		//		 To avoid this, use the Async version!
		public static IEnumerable<SVNStatusData> GetStatuses(string path, SVNStatusDataOptions options, IShellMonitor shellMonitor = null)
		{
			// File can be missing, if it was deleted by svn.
			//if (!File.Exists(path) && !Directory.Exists(path)) {
			//	if (!Silent) {
			//		EditorUtility.DisplayDialog("SVN Error", "SVN error happened while processing the assets. Check the logs.", "I will!");
			//	}
			//	throw new IOException($"Trying to get status for file {path} that does not exist!");
			//}
			var depth = options.Depth == SVNStatusDataOptions.SearchDepth.Empty ? "empty" : "infinity";
			var offline = options.Offline ? string.Empty : "-u";

			var result = ShellUtils.ExecuteCommand(SVN_Command, $"status --depth={depth} {offline} \"{SVNFormatPath(path)}\"", options.Timeout, shellMonitor);

			if (!string.IsNullOrEmpty(result.Error)) {

				if (!options.RaiseError)
					return Enumerable.Empty<SVNStatusData>();

				string displayMessage;
				bool isCritical = IsCriticalError(result.Error, out displayMessage);

				if (!string.IsNullOrEmpty(displayMessage) && !Silent && m_LastDisplayedError != displayMessage) {
					Debug.LogError($"{displayMessage}\n\n{result.Error}");
					m_LastDisplayedError = displayMessage;
					EditorUtility.DisplayDialog("SVN Error", displayMessage, "I will!");
				}

				if (isCritical) {
					throw new IOException($"Trying to get status for file {path} caused error:\n{result.Error}");
				} else {
					return Enumerable.Empty<SVNStatusData>();
				}
			}

			// If no info is returned for path, the status is normal. Reflect this when searching for Empty depth.
			if (options.Depth == SVNStatusDataOptions.SearchDepth.Empty) {

				if (options.Offline && string.IsNullOrWhiteSpace(result.Output)) {
					return Enumerable.Repeat(new SVNStatusData() { Status = VCFileStatus.Normal, Path = path, LockDetails = LockDetails.Empty }, 1);
				}

				// If -u is used, additional line is added at the end:
				// Status against revision:     14
				if (!options.Offline && result.Output.StartsWith("Status", StringComparison.Ordinal)) {
					return Enumerable.Repeat(new SVNStatusData() { Status = VCFileStatus.Normal, Path = path, LockDetails = LockDetails.Empty }, 1);
				}
			}

			return ExtractStatuses(result.Output, options, shellMonitor);
		}

		// Get statuses of files based on the options you provide.
		// NOTE: data is returned ONLY for folders / files that has something to show (has changes, locks or remote changes).
		//		 If used with non-recursive option it will return single data with normal status (if non).
		public static SVNAsyncOperation<IEnumerable<SVNStatusData>> GetStatusesAsync(string path, bool recursive, bool offline, bool fetchLockDetails = true, int timeout = -1)
		{
			var options = new SVNStatusDataOptions() {
				Depth = recursive ? SVNStatusDataOptions.SearchDepth.Infinity : SVNStatusDataOptions.SearchDepth.Empty,
				Timeout = timeout,
				RaiseError = false,
				Offline = offline,
				FetchLockOwner = fetchLockDetails,  // If offline, this is ignored.
			};

			return GetStatusesAsync(path, options);
		}

		// Get statuses of files based on the options you provide.
		// NOTE: data is returned ONLY for folders / files that has something to show (has changes, locks or remote changes).
		//		 If used with non-recursive option it will return single data with normal status (if non).
		public static SVNAsyncOperation<IEnumerable<SVNStatusData>> GetStatusesAsync(string path, SVNStatusDataOptions options)
		{
			// Do ToList() to enforce enumerate and fetch lock statuses as well.
			return SVNAsyncOperation<IEnumerable<SVNStatusData>>.Start(op => GetStatuses(path, options, op).ToList());
		}


		// Get offline status for a single file (non recursive). This won't make requests to the repository.
		// Will return valid status even if the file has nothing to show (has no changes).
		// If error happened, invalid status data will be returned (check statusData.IsValid).
		public static SVNStatusData GetStatus(string path, IShellMonitor shellMonitor = null)
		{
			// Optimization: empty depth will return nothing if status is normal.
			// If path is modified, added, deleted, unversioned, it will return proper value.
			var statusOptions = new SVNStatusDataOptions(SVNStatusDataOptions.SearchDepth.Empty);
			var statusData = GetStatuses(path, statusOptions, shellMonitor).FirstOrDefault();

			// If no path was found, error happened.
			if (!statusData.IsValid) {
				// Fallback to unversioned as we don't touch them.
				statusData.Status = VCFileStatus.Unversioned;
			}

			return statusData;
		}

		// Get status for a single file (non recursive).
		// Will return valid status even if the file has nothing to show (has no changes).
		// If error happened, invalid status data will be returned (check statusData.IsValid).
		public static SVNAsyncOperation<SVNStatusData> GetStatusAsync(string path, bool offline, bool fetchLockDetails = true, int timeout = -1)
		{
			var options = new SVNStatusDataOptions() {
				Depth = SVNStatusDataOptions.SearchDepth.Empty,
				Timeout = timeout,
				RaiseError = false,
				Offline = offline,
				FetchLockOwner = fetchLockDetails,  // If offline, this is ignored.
			};

			return SVNAsyncOperation<SVNStatusData>.Start(op => {

				var statusData = GetStatuses(path, options, op).FirstOrDefault();

				// If no path was found, error happened.
				if (!statusData.IsValid) {
					// Fallback to unversioned as we don't touch them.
					statusData.Status = VCFileStatus.Unversioned;
				}

				return statusData;
			});
		}

		public static bool HasConflictsAny(string path, int timeout = COMMAND_TIMEOUT * 4, IShellMonitor shellMonitor = null)
		{
			var result = ShellUtils.ExecuteCommand(SVN_Command, $"status --depth=infinity \"{SVNFormatPath(path)}\"", timeout, shellMonitor);

			if (!string.IsNullOrEmpty(result.Error)) {

				string displayMessage;
				bool isCritical = IsCriticalError(result.Error, out displayMessage);

				if (!string.IsNullOrEmpty(displayMessage) && !Silent) {
					EditorUtility.DisplayDialog("SVN Error", displayMessage, "I will!");
				}

				if (isCritical) {
					throw new IOException($"Trying to get status for file {path} caused error:\n{result.Error}!");
				} else {
					return false;
				}
			}

			return result.Output.Contains("Summary of conflicts:");
		}

		// Search for hidden files and folders starting with .
		// Basically search for any "/." or "\."
		public static bool IsHiddenPath(string path)
		{
			for (int i = 0, len = path.Length; i < len - 1; ++i) {
				if (path[i + 1] == '.' && (path[i] == '/' || path[i] == '\\'))
					return true;
			}

			return false;
		}


		private static string ExtractLineValue(string pattern, string str)
		{
			var lineIndex = str.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
			if (lineIndex == -1)
				return string.Empty;

			var valueStartIndex = lineIndex + pattern.Length + 1;
			var lineEndIndex = str.IndexOf("\n", valueStartIndex, StringComparison.OrdinalIgnoreCase);
			if (lineEndIndex == -1) {
				lineEndIndex = str.Length - 1;
			}

			// F@!$!#@!#!
			if (str[lineEndIndex - 1] == '\r') {
				lineEndIndex--;
			}

			return str.Substring(valueStartIndex, lineEndIndex - valueStartIndex);
		}

		private static string SVNFormatPath(string path)
		{
			// NOTE: @ is added at the end of path, to avoid problems when file name contains @, and SVN mistakes that as "At revision" syntax".
			//		https://stackoverflow.com/questions/757435/how-to-escape-characters-in-subversion-managed-file-names
			return path + "@";
		}

		public static void RequestSilence()
		{
			m_SilenceCount++;
		}

		public static void ClearSilence()
		{
			if (m_SilenceCount == 0) {
				Debug.LogError("WiseSVN: trying to clear silence more times than it was requested.");
				return;
			}

			m_SilenceCount--;
		}


		public static void RequestTemporaryDisable()
		{
			m_TemporaryDisabledCount++;
		}

		public static void ClearTemporaryDisable()
		{
			if (m_TemporaryDisabledCount == 0) {
				Debug.LogError("WiseSVN: trying to clear temporary disable more times than it was requested.");
				return;
			}

			m_TemporaryDisabledCount--;
		}

		// Use for debug.
		//[MenuItem("Assets/SVN/Selected Status", false, 200)]
		private static void StatusSelected()
		{
			if (Selection.assetGUIDs.Length == 0)
				return;

			var path = AssetDatabase.GUIDToAssetPath(Selection.assetGUIDs.FirstOrDefault());

			var result = ShellUtils.ExecuteCommand(SVN_Command, $"status \"{SVNFormatPath(path)}\"");
			if (!string.IsNullOrEmpty(result.Error)) {
				Debug.LogError($"SVN Error: {result.Error}");
				return;
			}

			Debug.Log($"Status for {path}\n{(string.IsNullOrEmpty(result.Output) ? "No Changes" : result.Output)}", Selection.activeObject);
		}
	}
}
