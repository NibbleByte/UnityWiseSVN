using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DevLocker.VersionControl.SVN
{
	// Stolen from UVC plugin.
	public enum VCFileStatus
	{
		Normal,
		Added,
		Conflicted,
		Deleted,
		Ignored,
		Modified,
		Replaced,
		Unversioned,
		Missing,
		//External,
		//Incomplete,
		//Merged,
		Obstructed,
		None,	// File not found or something worse....
	}

	// Stolen from UVC plugin.
	public enum VCProperty
	{
		None,
		Normal,
		Conflicted,
		Modified,
	}

	// Stolen from UVC plugin.
	public enum VCTreeConflictStatus
	{
		Normal,
		TreeConflict
	}

	// SVN console commands: https://tortoisesvn.net/docs/nightly/TortoiseSVN_en/tsvn-cli-main.html
	[InitializeOnLoad]
	public class SVNSimpleIntegration : UnityEditor.AssetModificationProcessor
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
			{'~', VCFileStatus.Obstructed},
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



		public struct StatusData
		{
			public VCFileStatus Status;
			public VCProperty PropertyStatus;
			public VCTreeConflictStatus TreeConflictStatus;

			public string Path;

			public bool IsConflicted => 
				Status == VCFileStatus.Conflicted || 
				PropertyStatus == VCProperty.Conflicted ||
				TreeConflictStatus == VCTreeConflictStatus.TreeConflict;
		}

		public static readonly string ProjectRoot;
		public static readonly string ProjectDataPath;

		public static bool Enabled { get; private set; }
		public static bool TemporaryDisabled => m_TemporaryDisabledCount > 0;	// Temporarily disable the integration (by code).
		public static bool Silent => m_SilenceCount > 0;	// Do not show dialogs

		private static int m_SilenceCount = 0;
		private static int m_TemporaryDisabledCount = 0;

		[Serializable]
		internal struct ProjectPreferences
		{
			[Tooltip("If you desire to use specific SVN CLI (svn.exe) located in the project, write down its path relative to the root folder.")]
			public string SvnCLIPath;

			[Tooltip("Asset paths that will be ignored by the SVN integrations. Use with caution.")]
			public List<string> Exclude;
		}

		private const string PROJECT_PREFERENCES_PATH = "ProjectSettings/SVNSimpleIntegration.prefs";
		private static ProjectPreferences m_ProjectPreferences = new ProjectPreferences() { SvnCLIPath = string.Empty, Exclude = new List<string>() };

		private static string SVN_Command => string.IsNullOrEmpty(m_ProjectPreferences.SvnCLIPath) 
			? "svn" 
			: Path.Combine(Path.GetDirectoryName(Application.dataPath), m_ProjectPreferences.SvnCLIPath);

		private const int COMMAND_TIMEOUT = 35000;	// Milliseconds

		#region Logging

		private class ResultReporter : IDisposable
		{
			public StringBuilder Builder = new StringBuilder();

			public ShellUtils.ShellResult Result;

			public void Append(string str)
			{
				Builder.Append(str);
			}

			public void AppendLine()
			{
				Builder.AppendLine();
			}

			public void AppendLine(string line)
			{
				Builder.AppendLine(line);
			}

			public void Dispose()
			{
				if (Builder.Length > 0) {
					if (Result.HasErrors) {
						Debug.LogError(Builder);
						if (!Silent) {
							EditorUtility.DisplayDialog("SVN Error",
								"SVN error happened while processing the assets. Check the logs.", "I will!");
						}
					} else {
						Debug.Log(Builder);
					}
				}
			}


			public static implicit operator StringBuilder(ResultReporter logger)
			{
				return logger.Builder;
			}
		}

		private static ResultReporter CreateLogger()
		{
			var logger = new ResultReporter();
			logger.AppendLine("SVN Operations:");

			return logger;
		}

		#endregion

		static SVNSimpleIntegration()
		{
			ProjectDataPath = Application.dataPath;
			ProjectRoot = Path.GetDirectoryName(Application.dataPath);

			Enabled = EditorPrefs.GetBool("SVNIntegration", true);

			if (File.Exists(PROJECT_PREFERENCES_PATH)) {
				m_ProjectPreferences = JsonUtility.FromJson<ProjectPreferences>(File.ReadAllText(PROJECT_PREFERENCES_PATH));
			}
		}

		internal static void SaveProjectPreferences(ProjectPreferences preferences)
		{
			m_ProjectPreferences = preferences;
			m_ProjectPreferences.Exclude.RemoveAll(p => string.IsNullOrWhiteSpace(p));

			File.WriteAllText(PROJECT_PREFERENCES_PATH, JsonUtility.ToJson(m_ProjectPreferences, true));
		}

		// NOTE: This is called separately for the file and its meta.
		public static void OnWillCreateAsset(string path)
		{
			if (!Enabled || TemporaryDisabled)
				return;

			var pathStatusData = GetStatus(path);
			if (pathStatusData.Status == VCFileStatus.Deleted) {

				var isMeta = path.EndsWith(".meta");

				if (!isMeta && !Silent) {
					EditorUtility.DisplayDialog(
						"Deleted file",
						$"The desired location\n\"{path}\"\nis marked as deleted in SVN. The file will be replaced in SVN with the new one.\n\nIf this is an automated change, consider adding this file to the exclusion list in the project preferences:\n\"{PROJECT_PREFERENCES_MENU}\"\n...or change your tool to silence the integration.",
						"Replace");
				}

				using (var reporter = CreateLogger()) {
					// File isn't still created, so we need to improvise.
					reporter.Result = ShellUtils.ExecuteCommand(SVN_Command, $"revert \"{SVNFormatPath(path)}\"", COMMAND_TIMEOUT, reporter);
					File.Delete(path);
				}
			}
		}

		public static AssetDeleteResult OnWillDeleteAsset(string path, RemoveAssetOptions option)
		{
			if (!Enabled || TemporaryDisabled || m_ProjectPreferences.Exclude.Any(path.StartsWith))
				return AssetDeleteResult.DidNotDelete;

			var oldStatus = GetStatus(path).Status;

			if (oldStatus == VCFileStatus.Unversioned)
				return AssetDeleteResult.DidNotDelete;

			using (var reporter = CreateLogger()) {

				reporter.Result = ShellUtils.ExecuteCommand(SVN_Command, $"delete --force \"{SVNFormatPath(path)}\"", COMMAND_TIMEOUT, reporter);
				if (reporter.Result.HasErrors)
					return AssetDeleteResult.FailedDelete;

				reporter.Result = ShellUtils.ExecuteCommand(SVN_Command, $"delete --force \"{SVNFormatPath(path + ".meta")}\"", COMMAND_TIMEOUT, reporter);
				if (reporter.Result.HasErrors)
					return AssetDeleteResult.FailedDelete;

				return AssetDeleteResult.DidDelete;
			}
		}

		public static AssetMoveResult OnWillMoveAsset(string oldPath, string newPath)
		{
			if (!Enabled || TemporaryDisabled || m_ProjectPreferences.Exclude.Any(oldPath.StartsWith))
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

						using (var reporter = CreateLogger()) {
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
					TortoiseSVNIntegrationMenus.CheckChangesAll();
				}

				return AssetMoveResult.FailedMove;
			}

			using (var reporter = CreateLogger()) {

				if (!CheckAndAddParentFolderIfNeeded(newPath, reporter))
					return AssetMoveResult.FailedMove;

				reporter.Result = ShellUtils.ExecuteCommand(SVN_Command, $"move \"{SVNFormatPath(oldPath)}\" \"{newPath}\"", COMMAND_TIMEOUT, reporter);
				if (reporter.Result.HasErrors)
					return AssetMoveResult.FailedMove;

				reporter.Result = ShellUtils.ExecuteCommand(SVN_Command, $"move \"{SVNFormatPath(oldPath + ".meta")}\" \"{newPath}.meta\"", COMMAND_TIMEOUT, reporter);
				if (reporter.Result.HasErrors)
					return AssetMoveResult.FailedMove;

				return AssetMoveResult.DidMove;
			}
		}

		public static bool CheckAndAddParentFolderIfNeeded(string path)
		{
			using (var reporter = CreateLogger()) {
				return CheckAndAddParentFolderIfNeeded(path, reporter);
			}
		}

		private static bool CheckAndAddParentFolderIfNeeded(string path, ResultReporter reporter)
		{
			var directory = Path.GetDirectoryName(path);

			// Special case
			if (string.IsNullOrEmpty(directory) && path == "Assets") {
				directory = path;
			}

			var newDirectoryStatusData = GetStatus(directory);
			if (newDirectoryStatusData.IsConflicted) {
				if (Silent || EditorUtility.DisplayDialog(
					"Conflicted files",
					$"Failed to move the files to \n\"{directory}\"\nbecause it has conflicts. Resolve them first!",
					"Check changes",
					"Cancel")) {
					TortoiseSVNIntegrationMenus.CheckChangesAll();
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

				if (!SVNAddDirectory(directory, reporter))
					return false;

			}

			return true;
		}

		private static bool SVNAddDirectory(string newDirectory, ResultReporter reporter)
		{
			// --parents will add all unversioned parent directories as well.
			reporter.Result = ShellUtils.ExecuteCommand(SVN_Command, $"add --parents --depth empty \"{SVNFormatPath(newDirectory)}\"", COMMAND_TIMEOUT, reporter);
			if (reporter.Result.HasErrors)
				return false;

			// Now add all folder metas upwards
			var directoryMeta = newDirectory + ".meta";
			var directoryMetaStatus = GetStatus(directoryMeta).Status; // Will be unversioned.
			while (directoryMetaStatus == VCFileStatus.Unversioned) {

				reporter.Result = ShellUtils.ExecuteCommand(SVN_Command, $"add \"{SVNFormatPath(directoryMeta)}\"", COMMAND_TIMEOUT, reporter);
				if (reporter.Result.HasErrors)
					return false;

				directoryMeta = Path.GetDirectoryName(directoryMeta) + ".meta";
				directoryMetaStatus = GetStatus(directoryMeta).Status;
			}

			return true;
		}

		private static bool SVNReplaceFile(string oldPath, string newPath, ResultReporter reporter)
		{
			File.Move(oldPath, newPath);
			File.Move(oldPath + ".meta", newPath + ".meta");

			reporter.Result = ShellUtils.ExecuteCommand(SVN_Command, $"add \"{SVNFormatPath(newPath)}\"", COMMAND_TIMEOUT, reporter);
			if (reporter.Result.HasErrors)
				return false;

			reporter.Result = ShellUtils.ExecuteCommand(SVN_Command, $"add \"{SVNFormatPath(newPath + ".meta")}\"", COMMAND_TIMEOUT, reporter);
			if (reporter.Result.HasErrors)
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
			if (error.Contains("0x80004005") && string.IsNullOrEmpty(m_ProjectPreferences.SvnCLIPath)) {
				displayMessage = $"SVN CLI (Command Line Interface) not found. " +
					$"Please install it or specify path to a valid svn.exe in the svn project preferences at:\n{PROJECT_PREFERENCES_MENU}\n\n" +
					$"You can disable the SVN integration from:\n{TURN_OFF_MENU}";

				return false;
			}

			// Same as above but the specified svn.exe in the project preferences is missing.
			if (error.Contains("0x80004005") && !string.IsNullOrEmpty(m_ProjectPreferences.SvnCLIPath)) {
				displayMessage = $"Cannot find the specified in the svn project preferences svn.exe:\n{m_ProjectPreferences.SvnCLIPath}\n\n" +
					$"You can reconfigure the svn project preferences at:\n{PROJECT_PREFERENCES_MENU}\n\n" +
					$"You can disable the SVN integration from:\n{TURN_OFF_MENU}";

				return false;
			}

			displayMessage = "SVN error happened while processing the assets. Check the logs.";
			return true;
		}

		static StatusData fff;
		private static IEnumerable<StatusData> ExtractStatuses(string output)
		{
			using (var sr = new StringReader(output)) {
				string line;
				while ((line = sr.ReadLine()) != null) {

					// Last status was deleted / added+, so this is telling us where it moved to / from. Skip it.
					if (line.Length > 8 && line[8] == '>')
						continue;

					// Rules are described in "svn help status".
					var statusData = new StatusData();
					statusData.Status = m_FileStatusMap[line[0]];
					statusData.PropertyStatus = m_PropertyStatusMap[line[1]];
					statusData.TreeConflictStatus = m_ConflictStatusMap[line[6]];

					// 7 columns plus space; Length+1 to skip '/'
					statusData.Path = line.Substring(8).Remove(0, ProjectRoot.Length + 1);

					yield return statusData;
				}
			}

		}

		public static IEnumerable<StatusData> GetStatuses(string path, string depth = "infinity")
		{
			// File can be missing, if it was deleted by svn.
			//if (!File.Exists(path) && !Directory.Exists(path)) {
			//	if (!Silent) {
			//		EditorUtility.DisplayDialog("SVN Error", "SVN error happened while processing the assets. Check the logs.", "I will!");
			//	}
			//	throw new IOException($"Trying to get status for file {path} that does not exist!");
			//}

			var result = ShellUtils.ExecuteCommand(SVN_Command, $"status --depth={depth} \"{SVNFormatPath(path)}\"", COMMAND_TIMEOUT * 4);

			if (!string.IsNullOrEmpty(result.error)) {
				string displayMessage;
				bool isCritical = IsCriticalError(result.error, out displayMessage);

				if (!string.IsNullOrEmpty(displayMessage) && !Silent) {
					EditorUtility.DisplayDialog("SVN Error", displayMessage, "I will!");
				}

				if (isCritical) {
					throw new IOException($"Trying to get status for file {path} caused error:\n{result.error}!");
				} else {
					return Enumerable.Empty<StatusData>();
				}
			}

			// If no info is returned for path, the status is normal.
			if (string.IsNullOrWhiteSpace(result.output) && depth == "empty") {
				return Enumerable.Repeat(new StatusData() { Status = VCFileStatus.Normal, Path = path }, 1);
			}

			return ExtractStatuses(result.output);
		}

		public static StatusData GetStatus(string path)
		{
			// Optimization: empty depth will return nothing if status is normal.
			// If path is modified, added, deleted, unversioned, it will return proper value.
			var statusData = GetStatuses(path, "empty").FirstOrDefault();

			// If no path was found, error happened.
			if (string.IsNullOrEmpty(statusData.Path)) {
				// Fallback to unversioned as we don't touch them.
				statusData.Status = VCFileStatus.Unversioned;
			}

			return statusData;
		}

		public static string SVNFormatPath(string path)
		{
			// NOTE: @ is added at the end of path, to avoid problems when file name contains @, and SVN mistakes that as "At revision" syntax".
			//		https://stackoverflow.com/questions/757435/how-to-escape-characters-in-subversion-managed-file-names
			return path + "@";
		}

		public static bool HasConflictsAny(string path)
		{
			var result = ShellUtils.ExecuteCommand(SVN_Command, $"status --depth=infinity \"{SVNFormatPath(path)}\"", COMMAND_TIMEOUT * 4); ;

			if (!string.IsNullOrEmpty(result.error)) {

				string displayMessage;
				bool isCritical = IsCriticalError(result.error, out displayMessage);

				if (!string.IsNullOrEmpty(displayMessage) && !Silent) {
					EditorUtility.DisplayDialog("SVN Error", displayMessage, "I will!");
				}

				if (isCritical) {
					throw new IOException($"Trying to get status for file {path} caused error:\n{result.error}!");
				} else {
					return false;
				}
			}

			return result.output.Contains("Summary of conflicts:");
		}

		public static void RequestSilence()
		{
			m_SilenceCount++;
		}

		public static void ClearSilence()
		{
			if (m_SilenceCount == 0) {
				Debug.LogError("SVNSimpleIntegration: trying to clear silence more times than it was requested.");
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
				Debug.LogError("SVNSimpleIntegration: trying to clear temporary disable more times than it was requested.");
				return;
			}

			m_TemporaryDisabledCount--;
		}


		[MenuItem("Assets/SVN/Selected Status", false, 200)]
		private static void StatusSelected()
		{
			if (Selection.assetGUIDs.Length == 0)
				return;

			var path = AssetDatabase.GUIDToAssetPath(Selection.assetGUIDs.FirstOrDefault());

			var result = ShellUtils.ExecuteCommand(SVN_Command, $"status \"{SVNFormatPath(path)}\"");
			if (!string.IsNullOrEmpty(result.error)) {
				Debug.LogError($"SVN Error: {result.error}");
				return;
			}

			Debug.Log($"Status for {path}\n{(string.IsNullOrEmpty(result.output) ? "No Changes" : result.output)}", Selection.activeObject);
		}

		private const string PROJECT_PREFERENCES_MENU = "Assets/SVN/Project Preferences";
		[MenuItem(PROJECT_PREFERENCES_MENU, false, 200)]
		private static void ShowProjectPreferences()
		{
			var window = EditorWindow.GetWindow<SVNSimpleIntegrationProjectPreferencesWindow>(true, "SVN Project Preferences");
			window.ProjectPreferences = m_ProjectPreferences;
			window.ShowUtility();
			window.position = new Rect(600f, 400f, 400f, 250f);
		}

		[MenuItem("Assets/SVN/Turn On", true, 200)]
		private static bool SVNEnableValidate() => !Enabled;

		[MenuItem("Assets/SVN/Turn On", false, 200)]
		private static void SVNEnable()
		{
			Enabled = true;
			EditorPrefs.SetBool("SVNIntegration", Enabled);
		}

		private const string TURN_OFF_MENU = "Assets/SVN/Turn Off";
		[MenuItem(TURN_OFF_MENU, true, 200)]
		private static bool SVNDisableValidate() =>	Enabled;

		[MenuItem(TURN_OFF_MENU, false, 200)]
		private static void SVNDisable()
		{
			Enabled = false;
			EditorPrefs.SetBool("SVNIntegration", Enabled);

			EditorUtility.DisplayDialog("SVN Integration", $"You have turned off the SVN integration.", "Ok");
		}
	}

	internal class SVNSimpleIntegrationProjectPreferencesWindow : EditorWindow
	{
		public SVNSimpleIntegration.ProjectPreferences ProjectPreferences;

		private Vector2 m_Scroll;

		private void OnGUI()
		{
			EditorGUILayout.LabelField("Project Preferences:", EditorStyles.boldLabel);

			EditorGUILayout.HelpBox("These settings will be saved in the ProjectSettings folder. Feel free to add them to your version control system.", MessageType.Info);

			m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);

			var so = new SerializedObject(this);
			var sp = so.FindProperty("ProjectPreferences");

			EditorGUILayout.PropertyField(sp, true);

			so.ApplyModifiedProperties();

			EditorGUILayout.EndScrollView();

			if (GUILayout.Button("Save")) {
				ProjectPreferences.SvnCLIPath = ProjectPreferences.SvnCLIPath.Trim();
				SVNSimpleIntegration.SaveProjectPreferences(ProjectPreferences);
				Close();
			}
		}
	}
}
