#if UNITY_2020_2_OR_NEWER || UNITY_2019_4_OR_NEWER || (UNITY_2018_4_OR_NEWER && !UNITY_2018_4_19 && !UNITY_2018_4_18 && !UNITY_2018_4_17 && !UNITY_2018_4_16 && !UNITY_2018_4_15)
#define CAN_DISABLE_REFRESH
#endif

using DevLocker.VersionControl.WiseSVN.Preferences;
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
	/// <summary>
	/// The core implementation of the SVN integration.
	/// Hooks up to file operations (create, move, delete) and executes corresponding SVN operations.
	/// Takes care of the meta files as well.
	/// Also provides API to integrate SVN with your tools - wraps SVN commands for your convenience.
	/// SVN console commands: https://tortoisesvn.net/docs/nightly/TortoiseSVN_en/tsvn-cli-main.html
	/// </summary>
	[InitializeOnLoad]
	public class WiseSVNIntegration : UnityEditor.AssetModificationProcessor
	{
		#region SVN CLI Definitions

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

		private static readonly Dictionary<char, VCSwitchedExternal> m_SwitchedExternalStatusMap = new Dictionary<char, VCSwitchedExternal>
		{
			{' ', VCSwitchedExternal.Normal},
			{'S', VCSwitchedExternal.Switched},
			{'X', VCSwitchedExternal.External},
		};

		private static readonly Dictionary<char, VCLockStatus> m_LockStatusMap = new Dictionary<char, VCLockStatus>
		{
			{' ', VCLockStatus.NoLock},
			{'K', VCLockStatus.LockedHere},
			{'O', VCLockStatus.LockedOther},
			{'T', VCLockStatus.LockedButStolen},
			{'B', VCLockStatus.BrokenLock},
		};

		private static readonly Dictionary<char, VCPropertiesStatus> m_PropertyStatusMap = new Dictionary<char, VCPropertiesStatus>
		{
			{' ', VCPropertiesStatus.Normal},
			{'C', VCPropertiesStatus.Conflicted},
			{'M', VCPropertiesStatus.Modified},
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

		private static readonly Dictionary<char, LogPathChange> m_LogPathChangesMap = new Dictionary<char, LogPathChange>
		{
			{'A', LogPathChange.Added},
			{'D', LogPathChange.Deleted},
			{'R', LogPathChange.Replaced},
			{'M', LogPathChange.Modified},
		};

		#endregion

		public static readonly string ProjectRootNative;
		public static readonly string ProjectRootUnity;

		public static event Action ShowChangesUI;
		public static event Action RunUpdateUI;

		/// <summary>
		/// Is the integration enabled.
		/// If you want to temporarily disable it by code use RequestTemporaryDisable().
		/// </summary>
		public static bool Enabled => m_PersonalPrefs.EnableCoreIntegration;

		/// <summary>
		/// Temporarily disable the integration (by code).
		/// File operations like create, move and delete won't be monitored. You'll have to do the SVN operations yourself.
		/// </summary>
		public static bool TemporaryDisabled => m_TemporaryDisabledCount > 0;

		/// <summary>
		/// Do not show dialogs. To use call RequestSilence();
		/// </summary>
		public static bool Silent => m_SilenceCount > 0;

		/// <summary>
		/// Should the SVN Integration log operations.
		/// </summary>
		public static SVNTraceLogs TraceLogs => m_PersonalPrefs.TraceLogs;

		private static int m_SilenceCount = 0;
		private static int m_TemporaryDisabledCount = 0;

		private static SVNPreferencesManager.PersonalPreferences m_PersonalPrefs => SVNPreferencesManager.Instance.PersonalPrefs;
		private static SVNPreferencesManager.ProjectPreferences m_ProjectPrefs => SVNPreferencesManager.Instance.ProjectPrefs;



		private static string SVN_Command => string.IsNullOrEmpty(m_ProjectPrefs.PlatformSvnCLIPath)
			? "svn"
			: Path.Combine(ProjectRootNative, m_ProjectPrefs.PlatformSvnCLIPath);

		internal const int COMMAND_TIMEOUT = 20000;	// Milliseconds
		internal const int ONLINE_COMMAND_TIMEOUT = 45000;  // Milliseconds

		// Used to avoid spam (specially when importing the whole project and errors start popping up, interrupting the process).
		[NonSerialized]
		private static string m_LastDisplayedError = string.Empty;

		private static HashSet<string> m_PendingErrorMessages = new HashSet<string>();

		#region Logging

		// Used to track the shell commands output for errors and log them on Dispose().
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

			// Because using AppendOutputLine() will output all the SVN operation spam that we parse.
			public void AppendTraceLine(string line)
			{
				m_CombinedOutput.Enqueue(line);
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

			public void ResetErrorFlag()
			{
				m_HasErrors = false;
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
							DisplayError("SVN error happened while processing the assets. Check the logs.");
						}
					} else if (m_LogOutput && m_HasCommand) {
						Debug.Log(output);
					}

					m_HasErrors = false;
					m_HasCommand = false;
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
			ProjectRootNative = Path.GetDirectoryName(Application.dataPath);
			ProjectRootUnity = ProjectRootNative.Replace('\\', '/');
		}

		/// <summary>
		/// Temporarily don't show any dialogs.
		/// This increments an integer, so don't forget to decrement it by calling ClearSilence().
		/// </summary>
		public static void RequestSilence()
		{
			m_SilenceCount++;
		}

		/// <summary>
		/// Allow dialogs to be shown again.
		/// </summary>
		public static void ClearSilence()
		{
			if (m_SilenceCount == 0) {
				Debug.LogError("WiseSVN: trying to clear silence more times than it was requested.");
				return;
			}

			m_SilenceCount--;
		}

		/// <summary>
		/// Temporarily disable the integration (by code).
		/// File operations like create, move and delete won't be monitored. You'll have to do the SVN operations yourself.
		/// This increments an integer, so don't forget to decrement it by calling ClearTemporaryDisable().
		/// </summary>
		public static void RequestTemporaryDisable()
		{
			m_TemporaryDisabledCount++;
		}

		/// <summary>
		/// Allow SVN integration again.
		/// </summary>
		public static void ClearTemporaryDisable()
		{
			if (m_TemporaryDisabledCount == 0) {
				Debug.LogError("WiseSVN: trying to clear temporary disable more times than it was requested.");
				return;
			}

			m_TemporaryDisabledCount--;
		}


		/// <summary>
		/// Get statuses of files based on the options you provide.
		/// NOTE: data is returned ONLY for folders / files that has something to show (has changes, locks or remote changes).
		///		 If used with non-recursive option it will return single data with normal status (if non).
		/// NOTE: This is synchronous operation. Better use the Async method version to avoid editor slow down.
		/// </summary>
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
					DisplayError(displayMessage);
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

		/// <summary>
		/// Get statuses of files based on the options you provide.
		/// NOTE: data is returned ONLY for folders / files that has something to show (has changes, locks or remote changes).
		///		 If used with non-recursive option it will return single data with normal status (if non).
		/// </summary>
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

		/// <summary>
		/// Get statuses of files based on the options you provide.
		/// NOTE: data is returned ONLY for folders / files that has something to show (has changes, locks or remote changes).
		///		 If used with non-recursive option it will return single data with normal status (if non).
		/// </summary>
		public static SVNAsyncOperation<IEnumerable<SVNStatusData>> GetStatusesAsync(string path, SVNStatusDataOptions options)
		{
			// Do ToList() to enforce enumerate and fetch lock statuses as well.
			return SVNAsyncOperation<IEnumerable<SVNStatusData>>.Start(op => GetStatuses(path, options, op).ToList());
		}


		/// <summary>
		/// Get offline status for a single file (non recursive). This won't make requests to the repository (so it shouldn't be that slow).
		/// Will return valid status even if the file has nothing to show (has no changes).
		/// If error happened, invalid status data will be returned (check statusData.IsValid).
		/// </summary>
		public static SVNStatusData GetStatus(string path, IShellMonitor shellMonitor = null)
		{
			// Optimization: empty depth will return nothing if status is normal.
			// If path is modified, added, deleted, unversioned, it will return proper value.
			var statusOptions = new SVNStatusDataOptions() {
				Depth = SVNStatusDataOptions.SearchDepth.Empty,
				RaiseError = true,
				Timeout = COMMAND_TIMEOUT,
				Offline = true,
				FetchLockOwner = false,
			};

			var statusData = GetStatuses(path, statusOptions, shellMonitor).FirstOrDefault();

			// If no path was found, error happened.
			if (!statusData.IsValid) {
				// Fallback to unversioned as we don't touch them.
				statusData.Status = VCFileStatus.Unversioned;
			}

			return statusData;
		}

		/// <summary>
		/// Get status for a single file (non recursive).
		/// Will return valid status even if the file has nothing to show (has no changes).
		/// If error happened, invalid status data will be returned (check statusData.IsValid).
		/// </summary>
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


		/// <summary>
		/// Ask the repository server for lock details of the specified file.
		/// NOTE: This is synchronous operation. Better use the Async method version to avoid editor slow down.
		/// </summary>
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
						DisplayError(displayMessage);
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
						DisplayError(displayMessage);
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

		/// <summary>
		/// Ask the repository server for lock details of the specified file.
		/// NOTE: If assembly reload happens, request will be lost, complete handler won't be called.
		/// </summary>
		public static SVNAsyncOperation<LockDetails> FetchLockDetailsAsync(string path, int timeout = -1)
		{
			return SVNAsyncOperation<LockDetails>.Start(op => FetchLockDetails(path, timeout, false, op));
		}


		/// <summary>
		/// Lock a file on the repository server.
		/// Use force to steal a lock from another user or working copy.
		/// NOTE: This is synchronous operation. Better use the Async method version to avoid editor slow down.
		/// </summary>
		public static LockOperationResult LockFile(string path, bool force, string message = "", string encoding = "", string targetsFileToUse = "", int timeout = ONLINE_COMMAND_TIMEOUT, IShellMonitor shellMonitor = null)
		{
			return LockFiles(Enumerate(path), force, message, encoding, targetsFileToUse, timeout, shellMonitor);
		}

		/// <summary>
		/// Lock a file on the repository server.
		/// Use force to steal a lock from another user or working copy.
		/// NOTE: If assembly reload happens, task will be lost, complete handler won't be called.
		/// </summary>
		public static SVNAsyncOperation<LockOperationResult> LockFileAsync(string path, bool force, string message = "", string encoding = "", int timeout = -1)
		{
			var targetsFileToUse = FileUtil.GetUniqueTempPathInProject();   // Not thread safe - call in main thread only.
			return SVNAsyncOperation<LockOperationResult>.Start(op => LockFile(path, force, message, encoding, targetsFileToUse, timeout, op));
		}

		/// <summary>
		/// Lock files on the repository server.
		/// Use force to steal a lock from another user or working copy.
		/// NOTE: This is synchronous operation. Better use the Async method version to avoid editor slow down.
		/// </summary>
		public static LockOperationResult LockFiles(IEnumerable<string> paths, bool force, string message = "", string encoding = "", string targetsFileToUse = "", int timeout = ONLINE_COMMAND_TIMEOUT, IShellMonitor shellMonitor = null)
		{
			var messageArg = string.IsNullOrEmpty(message) ? string.Empty : $"--message \"{message}\"";
			var encodingArg = string.IsNullOrEmpty(encoding) ? string.Empty : $"--encoding \"{encoding}\"";
			var forceArg = force ? "--force" : string.Empty;

			targetsFileToUse = string.IsNullOrEmpty(targetsFileToUse) ? FileUtil.GetUniqueTempPathInProject() : targetsFileToUse;
			File.WriteAllLines(targetsFileToUse, paths.Select(SVNFormatPath));

			var result = ShellUtils.ExecuteCommand(SVN_Command, $"lock {forceArg} {messageArg} {encodingArg} --targets \"{targetsFileToUse}\"", timeout, shellMonitor);

			// svn: warning: W160035: Path '...' is already locked by user '...'
			// File is already locked by another working copy (can be the same user). Use force to re-lock it.
			// This happens even if this working copy got the lock.
			if (result.Error.Contains("W160035"))
				return LockOperationResult.LockedByOther;

			if (!string.IsNullOrEmpty(result.Error)) {

				// User needs to log in using normal SVN client and save their authentication.
				// svn: E170013: Unable to connect to a repository at URL '...'
				// svn: E230001: Server SSL certificate verification failed: issuer is not trusted
				// svn: E215004: No more credentials or we tried too many times.
				// Authentication failed
				if (result.Error.Contains("E230001") || result.Error.Contains("E215004"))
					return LockOperationResult.AuthenticationFailed;

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

		/// <summary>
		/// Lock a file on the repository server.
		/// Use force to steal a lock from another user or working copy.
		/// NOTE: If assembly reload happens, task will be lost, complete handler won't be called.
		/// </summary>
		public static SVNAsyncOperation<LockOperationResult> LockFilesAsync(IEnumerable<string> paths, bool force, string message = "", string encoding = "", int timeout = -1)
		{
			var targetsFileToUse = FileUtil.GetUniqueTempPathInProject();   // Not thread safe - call in main thread only.
			return SVNAsyncOperation<LockOperationResult>.Start(op => LockFiles(paths, force, message, encoding, targetsFileToUse, timeout, op));
		}

		/// <summary>
		/// Unlock a file on the repository server.
		/// Use force to break a lock held by another user or working copy.
		/// NOTE: This is synchronous operation. Better use the Async method version to avoid editor slow down.
		/// </summary>
		public static LockOperationResult UnlockFile(string path, bool force, string targetsFileToUse = "", int timeout = ONLINE_COMMAND_TIMEOUT, IShellMonitor shellMonitor = null)
		{
			return UnlockFiles(Enumerate(path), force, targetsFileToUse, timeout, shellMonitor);
		}

		/// <summary>
		/// Unlock a file on the repository server.
		/// Use force to break a lock held by another user or working copy.
		/// NOTE: If assembly reload happens, task will be lost, complete handler won't be called.
		/// </summary>
		public static SVNAsyncOperation<LockOperationResult> UnlockFileAsync(string path, bool force, int timeout = -1)
		{
			var targetsFileToUse = FileUtil.GetUniqueTempPathInProject();   // Not thread safe - call in main thread only.
			return SVNAsyncOperation<LockOperationResult>.Start(op => UnlockFile(path, force, targetsFileToUse, timeout, op));
		}

		/// <summary>
		/// Unlock a file on the repository server.
		/// Use force to break a lock held by another user or working copy.
		/// NOTE: This is synchronous operation. Better use the Async method version to avoid editor slow down.
		/// </summary>
		public static LockOperationResult UnlockFiles(IEnumerable<string> paths, bool force, string targetsFileToUse = "", int timeout = ONLINE_COMMAND_TIMEOUT, IShellMonitor shellMonitor = null)
		{
			var forceArg = force ? "--force" : string.Empty;

			targetsFileToUse = string.IsNullOrEmpty(targetsFileToUse) ? FileUtil.GetUniqueTempPathInProject() : targetsFileToUse;
			File.WriteAllLines(targetsFileToUse, paths.Select(SVNFormatPath));

			var result = ShellUtils.ExecuteCommand(SVN_Command, $"unlock {forceArg} --targets \"{targetsFileToUse}\"", timeout, shellMonitor);

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

				// User needs to log in using normal SVN client and save their authentication.
				// svn: E170013: Unable to connect to a repository at URL '...'
				// svn: E230001: Server SSL certificate verification failed: issuer is not trusted
				// svn: E215004: No more credentials or we tried too many times.
				// Authentication failed
				if (result.Error.Contains("E230001") || result.Error.Contains("E215004"))
					return LockOperationResult.AuthenticationFailed;

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
				Debug.LogError($"Failed to lock \"{string.Join(",", paths)}\".\n\n{result.Output} ");
			}

			return LockOperationResult.UnknownError;
		}

		/// <summary>
		/// Unlock a file on the repository server.
		/// Use force to break a lock held by another user or working copy.
		/// NOTE: If assembly reload happens, task will be lost, complete handler won't be called.
		/// </summary>
		public static SVNAsyncOperation<LockOperationResult> UnlockFilesAsync(IEnumerable<string> paths, bool force, int timeout = -1)
		{
			var targetsFileToUse = FileUtil.GetUniqueTempPathInProject();   // Not thread safe - call in main thread only.
			return SVNAsyncOperation<LockOperationResult>.Start(op => UnlockFiles(paths, force, targetsFileToUse, timeout, op));
		}

		/// <summary>
		/// Update file or folder in SVN directly (without GUI).
		/// The force param will auto-resolve tree conflicts occurring on incoming new files (add) over existing unversioned files in the working copy.
		/// NOTE: This is synchronous operation. Better use the Async method version to avoid editor slow down.
		/// </summary>
		public static UpdateOperationResult Update(
			string path,
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

#if CAN_DISABLE_REFRESH
			AssetDatabase.DisallowAutoRefresh();
#endif
			ShellUtils.ShellResult result;
			try {
				result = ShellUtils.ExecuteCommand(SVN_Command, $"update --depth {depth} {acceptArg} {forceArg} {revisionArg} \"{SVNFormatPath(path)}\"", timeout, shellMonitor);
			}
			finally {

#if CAN_DISABLE_REFRESH
			AssetDatabase.AllowAutoRefresh();
#endif
			}

			if (result.HasErrors) {

				// Tree conflicts limit the auto-resolve capabilities. In that case "Summary of conflicts" is not shown.
				// svn: E155027: Tree conflict can only be resolved to 'working' state; '...' not resolved
				if (result.Error.Contains("E155027"))
					return UpdateOperationResult.SuccessWithConflicts;

				// User needs to log in using normal SVN client and save their authentication.
				// svn: E170013: Unable to connect to a repository at URL '...'
				// svn: E230001: Server SSL certificate verification failed: issuer is not trusted
				// svn: E215004: No more credentials or we tried too many times.
				// Authentication failed
				if (result.Error.Contains("E230001") || result.Error.Contains("E215004"))
					return UpdateOperationResult.AuthenticationFailed;

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

#if CAN_DISABLE_REFRESH
		/// <summary>
		/// Update file or folder in SVN directly (without GUI).
		/// The force param will auto-resolve tree conflicts occurring on incoming new files (add) over existing unversioned files in the working copy.
		/// DANGER: SVN updating while editor is crunching assets IS DANGEROUS! This Update method will disable unity auto-refresh feature until it has finished.
		/// </summary>
		public static SVNAsyncOperation<UpdateOperationResult> UpdateAsync(
			string path,
			UpdateResolveConflicts resolveConflicts = UpdateResolveConflicts.Postpone,
			bool force = false,
			int revision = -1,
			int timeout = -1
			)
		{
			return SVNAsyncOperation<UpdateOperationResult>.Start(op => Update(path, resolveConflicts, force, revision, timeout, op));
		}
#else
		/// <summary>
		/// Update file or folder in SVN directly (without GUI).
		/// The force param will auto-resolve tree conflicts occurring on incoming new files (add) over existing unversioned files in the working copy.
		/// DANGER: SVN updating while editor is crunching assets IS DANGEROUS! It WILL corrupt your asset guids. Use with caution!!!
		/// </summary>
		public static SVNAsyncOperation<UpdateOperationResult> UpdateAsyncDANGER(
			string path,
			UpdateResolveConflicts resolveConflicts = UpdateResolveConflicts.Postpone,
			bool force = false,
			int revision = -1,
			int timeout = -1
			)
		{
			return SVNAsyncOperation<UpdateOperationResult>.Start(op => Update(path, resolveConflicts, force, revision, timeout, op));
		}
#endif

		/// <summary>
		/// Commit files to SVN directly (without GUI).
		/// On commit all included locks will be unlocked unless specified not to by the keepLocks param.
		/// NOTE: This is synchronous operation. Better use the Async method version to avoid editor slow down.
		/// </summary>
		public static CommitOperationResult Commit(
			IEnumerable<string> assetPaths,
			bool includeMeta,
			bool recursive,
			string message,
			string encoding = "",
			bool keepLocks = false,
			string targetsFileToUse = "",
			int timeout = -1,
			IShellMonitor shellMonitor = null
			)
		{
			targetsFileToUse = string.IsNullOrEmpty(targetsFileToUse) ? FileUtil.GetUniqueTempPathInProject() : targetsFileToUse;
			if (includeMeta) {
				assetPaths = assetPaths.Select(path => path + ".meta").Concat(assetPaths);
			}
			File.WriteAllLines(targetsFileToUse, assetPaths.Select(SVNFormatPath));


			var depth = recursive ? "infinity" : "empty";
			var encodingArg = string.IsNullOrEmpty(encoding) ? "" : $"--encoding {encoding}";
			var keepLocksArg = keepLocks ? "--no-unlock" : "";

			var result = ShellUtils.ExecuteCommand(SVN_Command, $"commit --targets \"{targetsFileToUse}\" --depth {depth} --message \"{message}\" {encodingArg} {keepLocksArg}", timeout, shellMonitor);
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

				// User needs to log in using normal SVN client and save their authentication.
				// svn: E170013: Unable to connect to a repository at URL '...'
				// svn: E230001: Server SSL certificate verification failed: issuer is not trusted
				// svn: E215004: No more credentials or we tried too many times.
				// Authentication failed
				if (result.Error.Contains("E230001") || result.Error.Contains("E215004"))
					return CommitOperationResult.AuthenticationFailed;

				// Unable to connect to repository indicating some network or server problems.
				// svn: E170013: Unable to connect to a repository at URL '...'
				// svn: E731001: No such host is known.
				if (result.Error.Contains("E170013") || result.Error.Contains("E731001"))
					return CommitOperationResult.UnableToConnectError;

				return CommitOperationResult.UnknownError;
			}

			return CommitOperationResult.Success;
		}

		/// <summary>
		/// Commit files to SVN directly (without GUI).
		/// On commit all included locks will be unlocked unless specified not to by the keepLocks param.
		/// </summary>
		public static SVNAsyncOperation<CommitOperationResult> CommitAsync(
			IEnumerable<string> assetPaths,
			bool includeMeta, bool recursive,
			string message,
			string encoding = "",
			bool keepLocks = false,
			int timeout = -1
			)
		{
			var targetsFileToUse = FileUtil.GetUniqueTempPathInProject();	// Not thread safe - call in main thread only.
			return SVNAsyncOperation<CommitOperationResult>.Start(op => Commit(assetPaths, includeMeta, recursive, message, encoding, keepLocks, targetsFileToUse, timeout, op));
		}

		/// <summary>
		/// Revert files to SVN directly (without GUI).
		/// This is an offline operation, but it still can take time, since it will copy from the original files.
		/// RemoveAdded will remove added files from disk.
		/// Recursive won't restore deleted files. You have to specify them manually. Weird.
		/// NOTE: This is synchronous operation. Better use the Async method version to avoid editor slow down.
		/// </summary>
		public static RevertOperationResult Revert(
			IEnumerable<string> assetPaths,
			bool includeMeta,
			bool recursive,
			bool removeAdded,
			string targetsFileToUse = "",
			int timeout = -1,
			IShellMonitor shellMonitor = null
			)
		{
			targetsFileToUse = string.IsNullOrEmpty(targetsFileToUse) ? FileUtil.GetUniqueTempPathInProject() : targetsFileToUse;
			if (includeMeta) {
				assetPaths = assetPaths.Select(path => path + ".meta").Concat(assetPaths);
			}
			File.WriteAllLines(targetsFileToUse, assetPaths.Select(SVNFormatPath));


			var depth = recursive ? "infinity" : "empty";
			var removeAddedArg = removeAdded ? "--remove-added" : "";

			var result = ShellUtils.ExecuteCommand(SVN_Command, $"revert --targets \"{targetsFileToUse}\" --depth {depth} {removeAddedArg}", timeout, shellMonitor);
			if (result.HasErrors) {
				return RevertOperationResult.UnknownError;
			}

			return RevertOperationResult.Success;
		}

		/// <summary>
		/// Revert files to SVN directly (without GUI).
		/// This is an offline operation, but it still can take time, since it will copy from the original files.
		/// RemoveAdded will remove added files from disk.
		/// Recursive won't restore deleted files. You have to specify them manually. Weird.
		/// </summary>
		public static SVNAsyncOperation<RevertOperationResult> RevertAsync(
			IEnumerable<string> assetPaths,
			bool includeMeta, bool recursive,
			bool removeAdded,
			int timeout = -1
			)
		{
			var targetsFileToUse = FileUtil.GetUniqueTempPathInProject();   // Not thread safe - call in main thread only.
			return SVNAsyncOperation<RevertOperationResult>.Start(op => Revert(assetPaths, includeMeta, recursive, removeAdded, targetsFileToUse, timeout, op));
		}


		/// <summary>
		/// Add files to SVN directly (without GUI).
		/// </summary>
		public static bool Add(string path, bool includeMeta, bool recursive, IShellMonitor shellMonitor = null)
		{
			if (string.IsNullOrEmpty(path))
				return true;

			// Will add parent folders and their metas.
			var success = CheckAndAddParentFolderIfNeeded(path, false, shellMonitor);
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
		}

		/// <summary>
		/// Adds all parent unversioned folders AND THEIR META FILES!
		/// If this is needed it will ask the user for permission if promptUser is true.
		/// </summary>
		public static bool CheckAndAddParentFolderIfNeeded(string path, bool promptUser)
		{
			using (var reporter = CreateReporter()) {
				return CheckAndAddParentFolderIfNeeded(path, promptUser, reporter);
			}
		}

		/// <summary>
		/// Adds all parent unversioned folders AND THEIR META FILES!
		/// If this is needed it will ask the user for permission if promptUser is true.
		/// </summary>
		public static bool CheckAndAddParentFolderIfNeeded(string path, bool promptUser, IShellMonitor shellMonitor = null)
		{
			var directory = Path.GetDirectoryName(path);

			// Special case - Root folders like Assets, ProjectSettings, etc...
			if (string.IsNullOrEmpty(directory)) {
				directory = ".";
			}

			var newDirectoryStatusData = GetStatus(directory);
			if (newDirectoryStatusData.IsConflicted) {
				if (!Silent && promptUser && EditorUtility.DisplayDialog(
					"Conflicted files",
					$"Failed to move the files to \n\"{directory}\"\nbecause it has conflicts. Resolve them first!",
					"Check changes",
					"Cancel"
					)) {
					ShowChangesUI?.Invoke();
				}

				return false;
			}

			// Moving to unversioned folder -> add it to svn.
			if (newDirectoryStatusData.Status == VCFileStatus.Unversioned) {

				if (!Silent && promptUser && !EditorUtility.DisplayDialog(
					"Unversioned directory",
					$"The target directory:\n\"{directory}\"\nis not under SVN control. Should it be added?",
					"Add it!",
					"Cancel"
#if UNITY_2019_4_OR_NEWER
					, DialogOptOutDecisionType.ForThisSession, "WiseSVN.AddUnversionedFolder"
				))
#else
				))
#endif
					return false;

				if (!AddParentFolders(directory, shellMonitor))
					return false;

			}

			return true;
		}

		/// <summary>
		/// Adds all parent unversioned folders AND THEIR META FILES!
		/// </summary>
		public static bool AddParentFolders(string newDirectory, IShellMonitor shellMonitor = null)
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

		/// <summary>
		/// Check if file or folder has conflicts.
		/// </summary>
		public static bool HasAnyConflicts(string path, int timeout = COMMAND_TIMEOUT * 4, IShellMonitor shellMonitor = null)
		{
			var result = ShellUtils.ExecuteCommand(SVN_Command, $"status --depth=infinity \"{SVNFormatPath(path)}\"", timeout, shellMonitor);

			if (!string.IsNullOrEmpty(result.Error)) {

				string displayMessage;
				bool isCritical = IsCriticalError(result.Error, out displayMessage);

				if (!string.IsNullOrEmpty(displayMessage) && !Silent) {
					DisplayError(displayMessage);
				}

				if (isCritical) {
					throw new IOException($"Trying to get status for file {path} caused error:\n{result.Error}!");
				} else {
					return false;
				}
			}

			return result.Output.Contains("Summary of conflicts:");
		}

		/// <summary>
		/// List files and folders in specified url directory.
		/// Results will be appended to resultPaths. Paths will be relative to the url.
		/// If entry is a folder, it will end with a '/' character.
		/// NOTE: This is synchronous operation. Better use the Async method version to avoid editor slow down.
		/// </summary>
		public static ListOperationResult ListURL(string url, bool recursive, List<string> resultPaths, int timeout = ONLINE_COMMAND_TIMEOUT, IShellMonitor shellMonitor = null)
		{
			var depth = recursive ? "infinity" : "immediates";

			var result = ShellUtils.ExecuteCommand(SVN_Command, $"list --depth {depth} \"{SVNFormatPath(url)}\"", timeout, shellMonitor);

			if (!string.IsNullOrEmpty(result.Error)) {

				// User needs to log in using normal SVN client and save their authentication.
				// svn: E170013: Unable to connect to a repository at URL '...'
				// svn: E230001: Server SSL certificate verification failed: issuer is not trusted
				// svn: E215004: No more credentials or we tried too many times.
				// Authentication failed
				if (result.Error.Contains("E230001") || result.Error.Contains("E215004"))
					return ListOperationResult.AuthenticationFailed;

				// Unable to connect to repository indicating some network or server problems.
				// svn: E170013: Unable to connect to a repository at URL '...'
				// svn: E731001: No such host is known.
				if (result.Error.Contains("E170013") || result.Error.Contains("E731001"))
					return ListOperationResult.UnableToConnectError;

				// URL or local path not found (or invalid working copy path).
				// svn: warning: W155010: The node '...' was not found.
				// svn: E155007: '...' is not a working copy
				// svn: warning: W160013: URL 'https://...' non-existent in revision 59280
				// svn: E200009: Could not list all targets because some targets don't exist
				if (result.Error.Contains("W155010") || result.Error.Contains("E155007") || result.Error.Contains("W160013") || result.Error.Contains("E200009"))
					return ListOperationResult.NotFound;

				return ListOperationResult.UnknownError;
			}

			var output = result.Output.Replace("\r", "");
			resultPaths.AddRange(output.Split('\n'));

			return ListOperationResult.Success;
		}

		/// <summary>
		/// List files and folders in specified url directory.
		/// Results will be appended to resultPaths. Paths will be relative to the url.
		/// If entry is a folder, it will end with a '/' character.
		/// NOTE: If assembly reload happens, task will be lost, complete handler won't be called.
		/// </summary>
		public static SVNAsyncOperation<ListOperationResult> ListURLAsync(string url, bool recursive, List<string> resultPaths, int timeout = -1)
		{
			var threadResults = new List<string>();
			var operation = SVNAsyncOperation<ListOperationResult>.Start(op => ListURL(url, recursive, threadResults, timeout, op));
			operation.Completed += (op) => {
				resultPaths.AddRange(threadResults);
			};

			return operation;
		}

		/// <summary>
		/// Performs show log operation based on the provided parameters. The less data it needs to fetch the faster it will go.
		/// Returned paths are absolute: /trunk/YourProject/Foo.cs
		/// "StopOnCopy = false" may result in entries that do not match requested path (since they were moved but are part of its history).
		/// In that case you may end up with empty AffectedPaths array.
		/// Search query may have additional "--search" or "--search-and" options. Check the SVN documentation.
		/// NOTE: This is synchronous operation. Better use the Async method version to avoid editor slow down.
		/// </summary>
		public static LogOperationResult Log(string assetPathOrUrl, LogParams logParams, List<LogEntry> resultEntries, int timeout = ONLINE_COMMAND_TIMEOUT, IShellMonitor shellMonitor = null)
		{
			var fetchAffectedPathsStr = logParams.FetchAffectedPaths ? "-v" : "";
			var fetchCommitMessagesStr = logParams.FetchCommitMessages ? "" : "-q";
			var stopOnCopyStr = logParams.StopOnCopy ? "--stop-on-copy" : "";
			var limitStr = logParams.Limit > 0 ? "-l " + logParams.Limit : "";
			var searchStr = string.IsNullOrEmpty(logParams.SearchQuery) ? "" : "--search " + logParams.SearchQuery;

			var rangeStr = logParams.RangeEnd;
			bool hasRangeStart = !string.IsNullOrWhiteSpace(logParams.RangeStart);
			bool hasRangeEnd = !string.IsNullOrWhiteSpace(logParams.RangeEnd);
			if (hasRangeStart) {
				rangeStr = logParams.RangeStart + (hasRangeEnd ? $":{logParams.RangeEnd}" : "");
			}

			if (!string.IsNullOrWhiteSpace(rangeStr)) {
				rangeStr = "-r " + rangeStr;
			}

			var args = $"{fetchAffectedPathsStr} {fetchCommitMessagesStr} {stopOnCopyStr} {limitStr} {searchStr} {rangeStr}";

			var result = ShellUtils.ExecuteCommand(SVN_Command, $"log {args} \"{SVNFormatPath(assetPathOrUrl)}\"", timeout, shellMonitor);

			var relativeURL = AssetPathToRelativeURL(assetPathOrUrl);

			if (!string.IsNullOrEmpty(result.Error)) {

				// User needs to log in using normal SVN client and save their authentication.
				// svn: E170013: Unable to connect to a repository at URL '...'
				// svn: E230001: Server SSL certificate verification failed: issuer is not trusted
				// svn: E215004: No more credentials or we tried too many times.
				// Authentication failed
				if (result.Error.Contains("E230001") || result.Error.Contains("E215004"))
					return LogOperationResult.AuthenticationFailed;

				// Unable to connect to repository indicating some network or server problems.
				// svn: E170013: Unable to connect to a repository at URL '...'
				// svn: E731001: No such host is known.
				if (result.Error.Contains("E170013") || result.Error.Contains("E731001"))
					return LogOperationResult.UnableToConnectError;

				// URL is local path that is not a proper SVN working copy.
				// svn: E155010: The node '...' was not found.
				// svn: E155007: '...' is not a working copy
				// svn: E160013: '...' path not found
				// svn: E200009: Could not list all targets because some targets don't exist
				if (result.Error.Contains("E155010") || result.Error.Contains("E155007") || result.Error.Contains("E160013") || result.Error.Contains("E200009"))
					return LogOperationResult.NotFound;

				return LogOperationResult.UnknownError;
			}

			try {

				// Each entry is separated by that many dashes.
				const string entriesSeparator = "------------------------------------------------------------------------";
				var outputEntries = result.Output
					.Replace("\r", "")
					.Split(new string[] { entriesSeparator }, StringSplitOptions.RemoveEmptyEntries)
					.Select(line => line.Trim())
					;

				foreach (var outputEntry in outputEntries) {

					// Last line is empty.
					if (string.IsNullOrEmpty(outputEntry))
						break;

					var lines = outputEntry.Split('\n');    // Empty lines are valid
					int lineIndex = 0;

					var logEntry = new LogEntry();

					// First line always exists and contains basic info (last one is the commit message number of lines, optional):
					// r59162 | nikola | 2020-06-24 14:21:35 +0300 (ср, 24 юни 2020)
					// r59162 | nikola | 2020-06-24 14:21:35 +0300 (ср, 24 юни 2020) | 2 lines
					var basicInfos = lines[lineIndex].Split('|');

					logEntry.Revision = int.Parse(basicInfos[0].TrimStart('r'));
					logEntry.Author = basicInfos[1].Trim();
					logEntry.Date = basicInfos[2].Trim();
					lineIndex++;

					if (logParams.FetchAffectedPaths) {
						// Skip this line.
						Debug.Assert(lines[lineIndex] == "Changed paths:", "Invalid log format!");
						lineIndex++;

						var logPaths = new List<LogPath>(lines.Length);

						// Affected paths are printed ending with an empty line (or run out of lines).
						for(; lineIndex < lines.Length && !string.IsNullOrWhiteSpace(lines[lineIndex]); ++lineIndex) {
							var line = lines[lineIndex];
							var logPath = new LogPath();

							// Example paths:
							//    M /branches/YourBranch/SomeFolder
							//    A /branches/YourBranch/SomeFile.cs (from /branches/YourBranch/OldFileName.cs:58540)

							const string changeOpening = "   A ";

							logPath.Change = m_LogPathChangesMap[line[changeOpening.Length - 2]];

							bool wasMoved = logPath.Added && line[line.Length - 1] == ')';

							if (wasMoved) { // Maybe was moved

								const string copyFromOpening = "(from ";
								var openBracketIndex = line.IndexOf(copyFromOpening);

								if (openBracketIndex != -1) {
									var from = line.Substring(openBracketIndex + copyFromOpening.Length, line.Length - 1 - openBracketIndex - copyFromOpening.Length);

									var revisionColon = from.LastIndexOf(':');

									if (revisionColon != -1) {
										logPath.CopiedFrom = from.Substring(0, revisionColon);
										bool success = int.TryParse(from.Substring(revisionColon + 1), out logPath.CopiedFromRevision);

										if (success) {
											logPath.Path = line.Substring(changeOpening.Length, openBracketIndex - changeOpening.Length - 1); // -1 is for the space before the bracket
										} else {
											wasMoved = false;	// Oh well...
										}
									} else {
										// Probably folder that contains special elements: "Memories (from my childhood)".
										wasMoved = false;
									}

								} else {
									// Probably a folder ending with ')'
									wasMoved = false;
								}
							}

							if (!wasMoved) {
								logPath.CopiedFrom = string.Empty;
								logPath.Path = line.Substring(changeOpening.Length);
							}

							logPaths.Add(logPath);
						}

						logEntry.AllPaths = logPaths.ToArray();
						logEntry.AffectedPaths = logPaths
							.Where(lp => lp.Path.Contains(relativeURL) || lp.CopiedFrom.Contains(relativeURL))
							.ToArray()
							;

					} else {
						logEntry.AffectedPaths = new LogPath[0];
						logEntry.AllPaths = new LogPath[0];
					}


					logEntry.Message = logParams.FetchCommitMessages
						? string.Join("\n", lines.Skip(lineIndex)).Trim()
						: ""
						;

					resultEntries.Add(logEntry);
				}

			} catch (System.Threading.ThreadAbortException) {
				// Thread was aborted.
				resultEntries.Clear();
				return LogOperationResult.UnknownError;
			} catch (Exception ex) {
				// Parsing failed... unsupported format?
				Debug.LogException(ex);
				resultEntries.Clear();
				return LogOperationResult.UnknownError;
			}

			return LogOperationResult.Success;
		}

		/// <summary>
		/// Performs show log operation based on the provided parameters. The less data it needs to fetch the faster it will go.
		/// Returned paths are absolute: /trunk/YourProject/Foo.cs
		/// "StopOnCopy = false" may result in entries that do not match requested path (since they were moved but are part of its history).
		/// In that case you may end up with empty AffectedPaths array.
		/// Search query may have additional "--search" or "--search-and" options. Check the SVN documentation.
		/// NOTE: If assembly reload happens, task will be lost, complete handler won't be called.
		/// </summary>
		public static SVNAsyncOperation<LogOperationResult> LogAsync(string assetPathOrUrl, LogParams logParams, List<LogEntry> resultEntries, int timeout = -1)
		{
			var threadResults = new List<LogEntry>();
			var operation = SVNAsyncOperation<LogOperationResult>.Start(op => Log(assetPathOrUrl, logParams, resultEntries, timeout, op));
			operation.Completed += (op) => {
				resultEntries.AddRange(threadResults);
			};

			return operation;
		}



		/// <summary>
		/// Convert Unity asset path to svn URL. Works with files and folders.
		/// Example: https://yourcompany.com/svn/trunk/YourProject/Assets/Foo.cs
		/// </summary>
		public static string AssetPathToURL(string assetPath)
		{
			var result = ShellUtils.ExecuteCommand(SVN_Command, $"info \"{SVNFormatPath(assetPath)}\"", COMMAND_TIMEOUT);

			if (result.HasErrors)
				return string.Empty;

			return ExtractLineValue("URL:", result.Output);
		}

		/// <summary>
		/// Convert Unity asset path to relative repo URL. Works with files and folders.
		/// Example: ^/trunk/YourProject/Assets/Foo.cs
		/// </summary>
		public static string AssetPathToRelativeURL(string assetPathOrUrl)
		{
			var result = ShellUtils.ExecuteCommand(SVN_Command, $"info \"{SVNFormatPath(assetPathOrUrl)}\"", COMMAND_TIMEOUT);

			if (result.HasErrors)
				return string.Empty;

			return ExtractLineValue("Relative URL:", result.Output).TrimStart('^');
		}

		/// <summary>
		/// Get working copy root path on disk (the root of your checkout). Working copy root can be different from the Unity project folder.
		/// </summary>
		public static string WorkingCopyRootPath()
		{
			var result = ShellUtils.ExecuteCommand(SVN_Command, $"info \"{SVNFormatPath(ProjectRootNative)}\"", COMMAND_TIMEOUT);

			if (result.HasErrors)
				return string.Empty;

			return ExtractLineValue("Working Copy Root Path:", result.Output);
		}

		/// <summary>
		/// Get working copy root URL at your svn repository. Working copy root can be different from the Unity project folder.
		/// </summary>
		public static string WorkingCopyRootURL()
		{
			var result = ShellUtils.ExecuteCommand(SVN_Command, $"info \"{SVNFormatPath(WorkingCopyRootPath())}\"", COMMAND_TIMEOUT);

			if (result.HasErrors)
				return string.Empty;

			return ExtractLineValue("URL:", result.Output);
		}

		/// <summary>
		/// Get the revision number of the last change at specified location.
		/// Returns -1 if failed.
		/// </summary>
		public static int LastChangedRevision(string assetPathOrUrl)
		{
			var result = ShellUtils.ExecuteCommand(SVN_Command, $"info \"{SVNFormatPath(assetPathOrUrl)}\"", COMMAND_TIMEOUT);

			if (result.HasErrors)
				return -1;

			var revisionStr = ExtractLineValue("Last Changed Rev:", result.Output);
			if (string.IsNullOrEmpty(revisionStr))
				return -1;

			return int.Parse(revisionStr);
		}

		/// <summary>
		/// Checks if SVN CLI is setup and working properly.
		/// Returns string containing the SVN errors if any.
		/// </summary>
		public static string CheckForSVNErrors()
		{
			var result = ShellUtils.ExecuteCommand(SVN_Command, $"status --depth=empty \"{SVNFormatPath(ProjectRootNative)}\"", COMMAND_TIMEOUT);

			return result.Error;
		}

		/// <summary>
		/// Search for hidden files and folders starting with .
		/// Basically search for any "/." or "\."
		/// </summary>
		public static bool IsHiddenPath(string path)
		{
			for (int i = 0, len = path.Length; i < len - 1; ++i) {
				if (path[i + 1] == '.' && (path[i] == '/' || path[i] == '\\'))
					return true;
			}

			return false;
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
						$"The desired location\n\"{path}\"\nis marked as deleted in SVN. The file will be replaced in SVN with the new one.\n\nIf this is an automated change, consider adding this file to the exclusion list in the project preferences:\n\"{SVNPreferencesWindow.PROJECT_PREFERENCES_MENU}\"\n...or change your tool to silence the integration.",
						"Replace"
#if UNITY_2019_4_OR_NEWER
						, DialogOptOutDecisionType.ForThisSession, "WiseSVN.ReplaceFile"
					);
#else
					);
#endif
				}

				using (var reporter = CreateReporter()) {
					// File isn't still created, so we need to improvise.
					var result = ShellUtils.ExecuteCommand(SVN_Command, $"revert \"{SVNFormatPath(path)}\"", COMMAND_TIMEOUT, reporter);
					Debug.Assert(!result.HasErrors, "Revert of deleted file failed.");
					File.Delete(path);
				}
			}
		}

		private static AssetDeleteResult OnWillDeleteAsset(string path, RemoveAssetOptions option)
		{
			if (!Enabled || TemporaryDisabled || SVNPreferencesManager.ShouldExclude(m_ProjectPrefs.Exclude, path))
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
			if (!Enabled || TemporaryDisabled || SVNPreferencesManager.ShouldExclude(m_ProjectPrefs.Exclude, oldPath))
				return AssetMoveResult.DidNotMove;

			var oldStatusData = GetStatus(oldPath);

			if (oldStatusData.Status == VCFileStatus.Unversioned) {

				var newStatusData = GetStatus(newPath);
				if (newStatusData.Status == VCFileStatus.Deleted) {
					if (Silent || EditorUtility.DisplayDialog(
						"Deleted file",
						$"The desired location\n\"{newPath}\"\nis marked as deleted in SVN. Are you trying to replace it with a new one?",
						"Replace",
						"Cancel"
#if UNITY_2019_4_OR_NEWER
						, DialogOptOutDecisionType.ForThisSession, "WiseSVN.ReplaceFile"
					)) {
#else
					)) {
#endif

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

			if (oldStatusData.IsConflicted || (Directory.Exists(oldPath) && HasAnyConflicts(oldPath))) {
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

				if (!CheckAndAddParentFolderIfNeeded(newPath, true, reporter))
					return AssetMoveResult.FailedMove;


				if (m_ProjectPrefs.MoveBehaviour == SVNMoveBehaviour.UseAddAndDeleteForAllAssets ||
					m_ProjectPrefs.MoveBehaviour == SVNMoveBehaviour.UseAddAndDeleteForFolders && Directory.Exists(oldPath)
					) {

					return MoveAssetByAddDeleteOperations(oldPath, newPath, reporter)
						? AssetMoveResult.DidMove
						: AssetMoveResult.FailedMove
						;
				}


				var result = ShellUtils.ExecuteCommand(SVN_Command, $"move \"{SVNFormatPath(oldPath)}\" \"{newPath}\"", COMMAND_TIMEOUT, reporter);
				if (result.HasErrors) {

					// Moving files from one repository to another is not allowed (nested checkouts or externals).
					//svn: E155023: Cannot copy to '...', as it is not from repository '...'; it is from '...'
					if (result.Error.Contains("E155023")) {

						if (Silent || EditorUtility.DisplayDialog(
							"Error moving asset",
							$"Failed to move file as destination is in another external repository:\n{oldPath}\n\nWould you like to force move the file anyway?\nWARNING: You'll loose the SVN history of the file.\n\nTarget path:\n{newPath}",
							"Yes, ignore SVN",
							"Cancel"
							)) {

							return MoveAssetByAddDeleteOperations(oldPath, newPath, reporter)
								? AssetMoveResult.DidMove
								: AssetMoveResult.FailedMove
								;

						} else {
							reporter.ResetErrorFlag();
							return AssetMoveResult.FailedMove;
						}


						// Moving files / folder into unversioned folder, sometimes results in this strange error. Handle it gracefully.
						//svn: E155040: Cannot move mixed-revision subtree '...' [52:53]; try updating it first
					} else if (result.Error.Contains("try updating it first") && !Silent) {
						var formattedError = result
							.Error
							.Replace(ProjectRootNative + Path.DirectorySeparatorChar, "")
							.Replace(" '", "\n'")
							.Replace("; ", ";\n\n");

						reporter.ResetErrorFlag();

						if (EditorUtility.DisplayDialog(
							"Update Needed",
							$"Failed to move / rename files with error: \n{formattedError}",
							"Run Update",
							"Cancel"
							)) {

							reporter.AppendTraceLine("Running Update via GUI...");
							RunUpdateUI?.Invoke();
							reporter.AppendTraceLine("Update Finished.");

							if (EditorUtility.DisplayDialog(
								"Retry Move / Rename?",
								$"Update finished.\nDo you wish to retry moving the asset?\nAsset: {oldPath}",
								"Retry Move / Rename",
								"Cancel"
								)) {
								// Try moving it again with all the checks because the situation may have changed (conflicts & stuff).
								reporter.Dispose();
								return OnWillMoveAsset(oldPath, newPath);
							} else {
								return AssetMoveResult.FailedMove;
							}

						} else {
							return AssetMoveResult.FailedMove;
						}
					} else {
						return AssetMoveResult.FailedMove;
					}
				}

				// HACK: Really don't want to copy-paste the "try update first" error handle from above a second time. Hope this case never happens here.
				result = ShellUtils.ExecuteCommand(SVN_Command, $"move \"{SVNFormatPath(oldPath + ".meta")}\" \"{newPath}.meta\"", COMMAND_TIMEOUT, reporter);
				if (result.HasErrors)
					return AssetMoveResult.FailedMove;

				return AssetMoveResult.DidMove;
			}
		}

		private static bool MoveAssetByAddDeleteOperations(string oldPath, string newPath, ResultReporter reporter)
		{
			reporter.AppendTraceLine($"Moving file \"{oldPath}\" to \"{newPath}\" without SVN history...");

			if (Directory.Exists(oldPath)) {
				Directory.Move(oldPath, newPath);
				Directory.Move(oldPath + ".meta", newPath + ".meta");
			} else {
				File.Move(oldPath, newPath);
				File.Move(oldPath + ".meta", newPath + ".meta");
			}

			// Reset after the danger is gone (manual file operations)
			reporter.ResetErrorFlag();

			var result = ShellUtils.ExecuteCommand(SVN_Command, $"delete --force \"{SVNFormatPath(oldPath)}\"", COMMAND_TIMEOUT, reporter);
			if (result.HasErrors)
				return false;

			result = ShellUtils.ExecuteCommand(SVN_Command, $"delete --force \"{SVNFormatPath(oldPath + ".meta")}\"", COMMAND_TIMEOUT, reporter);
			if (result.HasErrors)
				return false;

			result = ShellUtils.ExecuteCommand(SVN_Command, $"add \"{SVNFormatPath(newPath)}\"", COMMAND_TIMEOUT, reporter);
			if (result.HasErrors)
				return false;

			result = ShellUtils.ExecuteCommand(SVN_Command, $"add \"{SVNFormatPath(newPath + ".meta")}\"", COMMAND_TIMEOUT, reporter);
			if (result.HasErrors)
				return false;

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
					$"Please install it or specify path to a valid svn.exe in the svn preferences at:\n{SVNPreferencesWindow.PROJECT_PREFERENCES_MENU}\n\n" +
					$"You can also disable the SVN integration.";

				return false;
			}

			// Same as above but the specified svn.exe in the project preferences is missing.
			if (error.Contains("0x80004005") && !string.IsNullOrEmpty(m_ProjectPrefs.PlatformSvnCLIPath)) {
				displayMessage = $"Cannot find the specified in the svn project preferences svn.exe:\n{m_ProjectPrefs.PlatformSvnCLIPath}\n\n" +
					$"You can reconfigure the svn preferences at:\n{SVNPreferencesWindow.PROJECT_PREFERENCES_MENU}\n\n" +
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
						continue;

					// All externals append separate sections with their statuses:
					// Performing status on external item at '...':
					if (line.StartsWith("Performing status", StringComparison.Ordinal))
						continue;

					// If user has files in the "ignore-on-commit" list, this is added at the end plus empty line:
					// ---Changelist 'ignore-on-commit': ...
					if (string.IsNullOrWhiteSpace(line))
						continue;
					if (line.StartsWith("---", StringComparison.Ordinal))
						break;

					// Rules are described in "svn help status".
					var statusData = new SVNStatusData();
					statusData.Status = m_FileStatusMap[line[0]];
					statusData.PropertiesStatus = m_PropertyStatusMap[line[1]];
					statusData.SwitchedExternalStatus = m_SwitchedExternalStatusMap[line[4]];
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

		private static IEnumerable<string> Enumerate(string str)
		{
			yield return str;
		}

		private static void DisplayError(string message)
		{
			EditorApplication.update -= DisplayPendingMessages;
			EditorApplication.update += DisplayPendingMessages;

			m_PendingErrorMessages.Add(message);
		}

		private static void DisplayPendingMessages()
		{
			EditorApplication.update -= DisplayPendingMessages;

			var message = string.Join("\n-----\n", m_PendingErrorMessages);

			if (message.Length > 1500) {
				message = message.Substring(0, 1490) + "...";
			}

#if UNITY_2019_4_OR_NEWER
			EditorUtility.DisplayDialog("SVN Error", message, "I will!", DialogOptOutDecisionType.ForThisSession, "WiseSVN.ErrorMessages");
#else
			EditorUtility.DisplayDialog("SVN Error", message, "I will!");
#endif
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
