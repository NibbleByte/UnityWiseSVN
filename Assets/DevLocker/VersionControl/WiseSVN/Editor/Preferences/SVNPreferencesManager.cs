using DevLocker.VersionControl.WiseSVN.ContextMenus;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DevLocker.VersionControl.WiseSVN.Preferences
{
	internal class SVNPreferencesManager : Utils.EditorPersistentSingleton<SVNPreferencesManager>
	{
		internal enum BoolPreference
		{
			SameAsProjectPreference = 0,
			Enabled = 4,
			Disabled = 8,
		}

		private const string PERSONAL_PREFERENCES_KEY = "WiseSVN";
		private const string PROJECT_PREFERENCES_PATH = "ProjectSettings/WiseSVN.prefs";

		// Icons are stored in the database so we don't reload them every time.
		[SerializeField] private GUIContent[] FileStatusIcons = new GUIContent[0];
		[SerializeField] private GUIContent[] LockStatusIcons = new GUIContent[0];
		[SerializeField] private GUIContent RemoteStatusIcons = null;

		[SerializeField] private bool m_RetryTextures = false;

		[Serializable]
		internal class PersonalPreferences
		{
			public bool EnableCoreIntegration = true;		// Sync file operations with SVN
			public bool PopulateStatusesDatabase = true;    // For overlay icons etc.
			public bool PopulateIgnoresDatabase = true;    // For svn-ignored icons etc.
			public bool ShowNormalStatusOverlayIcon = false;
			public bool ShowExcludedStatusOverlayIcon = true;

			public string SvnCLIPath = string.Empty;

			// When populating the database, should it check for server changes as well (locks & modified files).
			public BoolPreference DownloadRepositoryChanges = BoolPreference.SameAsProjectPreference;
			public bool AutoLockOnModified = false;
			public bool WarnForPotentialConflicts = true;

			public int AutoRefreshDatabaseInterval = 60;    // seconds; Less than 0 will disable it.
			public ContextMenusClient ContextMenusClient = ContextMenusClient.TortoiseSVN;
			public SVNTraceLogs TraceLogs = SVNTraceLogs.SVNOperations;

			public const string AutoLockOnModifiedHint = "Will automatically lock assets if possible when they become modified, instead of prompting the user.\nIf assets have newer version or are locked by someone else, prompt will still be displayed.\n\nNotification will be displayed. Check the logs to know what was locked.";

			public PersonalPreferences Clone()
			{
				return (PersonalPreferences) MemberwiseClone();
			}
		}

		[Serializable]
		internal class ProjectPreferences
		{
			public bool DownloadRepositoryChanges = true;

			// Use PlatformSvnCLIPath instead as it is platform independent.
			public string SvnCLIPath = string.Empty;
			public string SvnCLIPathMacOS = string.Empty;

#if UNITY_EDITOR_WIN
			public string PlatformSvnCLIPath => SvnCLIPath;
#else
			public string PlatformSvnCLIPath => SvnCLIPathMacOS;
#endif
			public SVNMoveBehaviour MoveBehaviour = SVNMoveBehaviour.NormalSVNMove;

			// Enable lock prompts on asset modify.
			public bool EnableLockPrompt = false;

			public const string LockMessageHint = "Message used when locked after prompting the user.";
			[Tooltip(LockMessageHint)]
			public string LockPromptMessage = "Auto-locked.";

			[Tooltip("Automatically unlock if asset becomes unmodified (i.e. you reverted the asset).")]
			public bool AutoUnlockIfUnmodified = false;

#if UNITY_2020_2_OR_NEWER
			// Because we are rendering this list manually.
			[NonReorderable]
#endif
			// Lock prompt parameters for when asset is modified.
			public List<LockPromptParameters> LockPromptParameters = new List<LockPromptParameters>();


			// Enable svn branches database.
			public bool EnableBranchesDatabase;

#if UNITY_2020_2_OR_NEWER
			[NonReorderable]
#endif
			// SVN parameters used for scanning branches in the SVN repo.
			public List<BranchScanParameters> BranchesDatabaseScanParameters = new List<BranchScanParameters>();

#if UNITY_2020_2_OR_NEWER
			[NonReorderable]
#endif
			// Show these branches on top.
			public List<string> PinnedBranches = new List<string>();

#if UNITY_2020_2_OR_NEWER
			[NonReorderable]
#endif
			public List<string> Exclude = new List<string>();

			public ProjectPreferences Clone()
			{
				var clone = (ProjectPreferences) MemberwiseClone();

				clone.LockPromptParameters = new List<LockPromptParameters>(LockPromptParameters);
				clone.BranchesDatabaseScanParameters = new List<BranchScanParameters>(BranchesDatabaseScanParameters);
				clone.PinnedBranches = new List<string>(PinnedBranches);
				clone.Exclude = new List<string>(Exclude);

				return clone;
			}
		}

		public PersonalPreferences PersonalPrefs;
		public ProjectPreferences ProjectPrefs;

		[SerializeField] private long m_ProjectPrefsLastModifiedTime = 0;

		public event Action PreferencesChanged;

		public bool DownloadRepositoryChanges =>
			PersonalPrefs.DownloadRepositoryChanges == BoolPreference.SameAsProjectPreference
			? ProjectPrefs.DownloadRepositoryChanges
			: PersonalPrefs.DownloadRepositoryChanges == BoolPreference.Enabled;


		public override void Initialize(bool freshlyCreated)
		{
			var lastModifiedDate = File.Exists(PROJECT_PREFERENCES_PATH)
				? File.GetLastWriteTime(PROJECT_PREFERENCES_PATH).Ticks
				: 0
				;

			if (freshlyCreated || m_ProjectPrefsLastModifiedTime != lastModifiedDate) {
				LoadPreferences();
			}

			if (freshlyCreated || m_RetryTextures) {

				LoadTextures();

				m_RetryTextures = false;

				// If WiseSVN was just added to the project, Unity won't manage to load the textures the first time. Try again next frame.
				if (FileStatusIcons[(int)VCFileStatus.Modified].image == null) {

					// We're using a flag as assembly reload may happen and update callback will be lost.
					m_RetryTextures = true;

					EditorApplication.CallbackFunction reloadTextures = null;
					reloadTextures = () => {
						LoadTextures();
						m_RetryTextures = false;
						EditorApplication.update -= reloadTextures;

						if (FileStatusIcons[(int)VCFileStatus.Modified].image == null) {
							Debug.LogWarning("SVN overlay icons are missing.");
						}
					};

					EditorApplication.update += reloadTextures;
				}

				Debug.Log($"Loaded WiseSVN Preferences. WiseSVN is turned {(PersonalPrefs.EnableCoreIntegration ? "on" : "off")}.");

				if (PersonalPrefs.EnableCoreIntegration) {
					CheckSVNSupport();
				}
			}

			SVNContextMenusManager.SetupContextType(PersonalPrefs.ContextMenusClient);
		}

		public GUIContent GetFileStatusIconContent(VCFileStatus status)
		{
			// TODO: this is a legacy hack-fix. The enum status got new values and needs to be refreshed on old running clients. Remove someday.
			var index = (int)status;
			if (index >= FileStatusIcons.Length) {
				LoadTextures();
			}

			return FileStatusIcons[(int)status];
		}


		public GUIContent GetLockStatusIconContent(VCLockStatus status)
		{
			return LockStatusIcons[(int)status];
		}

		public GUIContent GetRemoteStatusIconContent(VCRemoteFileStatus status)
		{
			return status == VCRemoteFileStatus.Modified ? RemoteStatusIcons : null;
		}

		private void LoadPreferences()
		{
			var personalPrefsData = EditorPrefs.GetString(PERSONAL_PREFERENCES_KEY, string.Empty);
			if (!string.IsNullOrEmpty(personalPrefsData)) {
				PersonalPrefs = JsonUtility.FromJson<PersonalPreferences>(personalPrefsData);
			} else {
				PersonalPrefs = new PersonalPreferences();
#if UNITY_EDITOR_WIN
				PersonalPrefs.ContextMenusClient = ContextMenusClient.TortoiseSVN;
#else
				PersonalPrefs.ContextMenusClient = ContextMenusClient.SnailSVN;
#endif
			}

			if (File.Exists(PROJECT_PREFERENCES_PATH)) {
				ProjectPrefs = JsonUtility.FromJson<ProjectPreferences>(File.ReadAllText(PROJECT_PREFERENCES_PATH));
				m_ProjectPrefsLastModifiedTime = File.GetLastWriteTime(PROJECT_PREFERENCES_PATH).Ticks;
			} else {
				ProjectPrefs = new ProjectPreferences();
				m_ProjectPrefsLastModifiedTime = 0;
			}
		}

		private void LoadTextures()
		{
			FileStatusIcons = new GUIContent[Enum.GetValues(typeof(VCFileStatus)).Length];
			FileStatusIcons[(int)VCFileStatus.Normal] = LoadTexture("Editor/SVNOverlayIcons/SVNNormalIcon");
			FileStatusIcons[(int)VCFileStatus.Added] = LoadTexture("Editor/SVNOverlayIcons/SVNAddedIcon");
			FileStatusIcons[(int)VCFileStatus.Modified] = LoadTexture("Editor/SVNOverlayIcons/SVNModifiedIcon");
			FileStatusIcons[(int)VCFileStatus.Replaced] = LoadTexture("Editor/SVNOverlayIcons/SVNModifiedIcon");
			FileStatusIcons[(int)VCFileStatus.Deleted] = LoadTexture("Editor/SVNOverlayIcons/SVNDeletedIcon");
			FileStatusIcons[(int)VCFileStatus.Conflicted] = LoadTexture("Editor/SVNOverlayIcons/SVNConflictIcon");
			FileStatusIcons[(int)VCFileStatus.Ignored] = LoadTexture("Editor/SVNOverlayIcons/SVNIgnoredIcon", "This item is in a svn-ignore list. It is not tracked by SVN.");
			FileStatusIcons[(int)VCFileStatus.Unversioned] = LoadTexture("Editor/SVNOverlayIcons/SVNUnversionedIcon");
			FileStatusIcons[(int)VCFileStatus.Excluded] = LoadTexture("Editor/SVNOverlayIcons/SVNReadOnlyIcon", "This item is excluded from monitoring by WiseSVN, but it may still be tracked by SVN. Check the WiseSVN preferences - Excludes setting.");

			LockStatusIcons = new GUIContent[Enum.GetValues(typeof(VCLockStatus)).Length];
			LockStatusIcons[(int)VCLockStatus.LockedHere] = LoadTexture("Editor/SVNOverlayIcons/Locks/SVNLockedHereIcon", "You have locked this file.\nClick for more details.");
			LockStatusIcons[(int)VCLockStatus.BrokenLock] = LoadTexture("Editor/SVNOverlayIcons/Locks/SVNLockedOtherIcon", "You have a lock that is no longer valid (someone else stole it and released it).\nClick for more details.");
			LockStatusIcons[(int)VCLockStatus.LockedOther] = LoadTexture("Editor/SVNOverlayIcons/Locks/SVNLockedOtherIcon", "Someone else locked this file.\nClick for more details.");
			LockStatusIcons[(int)VCLockStatus.LockedButStolen] = LoadTexture("Editor/SVNOverlayIcons/Locks/SVNLockedOtherIcon", "Your lock was stolen by someone else.\nClick for more details.");

			RemoteStatusIcons = LoadTexture("Editor/SVNOverlayIcons/Others/SVNRemoteChangesIcon", "Asset is out of date. Update to avoid conflicts.");
		}

		public static GUIContent LoadTexture(string path, string tooltip = null)
		{
			return new GUIContent(Resources.Load<Texture2D>(path), tooltip);

			//var texture = AssetDatabase.FindAssets(Path.GetFileNameWithoutExtension(path))
			//	.Select(AssetDatabase.GUIDToAssetPath)
			//	.Select(AssetDatabase.LoadAssetAtPath<Texture2D>)
			//	.FirstOrDefault()
			//	;
			//
			//return new GUIContent(texture, tooltip);
		}


		public void SavePreferences(PersonalPreferences personalPrefs, ProjectPreferences projectPrefs)
		{
			PersonalPrefs = personalPrefs.Clone();
			ProjectPrefs = projectPrefs.Clone();

			EditorPrefs.SetString(PERSONAL_PREFERENCES_KEY, JsonUtility.ToJson(PersonalPrefs));

			File.WriteAllText(PROJECT_PREFERENCES_PATH, JsonUtility.ToJson(ProjectPrefs, true));

			SVNContextMenusManager.SetupContextType(PersonalPrefs.ContextMenusClient);

			PreferencesChanged?.Invoke();
		}

		// NOTE: Copy pasted from SearchAssetsFilter.
		public static bool ShouldExclude(IEnumerable<string> excludes, string path)
		{
			foreach(var exclude in excludes) {

				bool isExcludePath = exclude.Contains("/");    // Check if this is a path or just a filename

				if (isExcludePath) {
					if (path.StartsWith(exclude, StringComparison.OrdinalIgnoreCase))
						return true;

				} else {

					var filename = Path.GetFileName(path);
					if (filename.IndexOf(exclude, StringComparison.OrdinalIgnoreCase) != -1)
						return true;
				}
			}

			return false;
		}

		public static string SanitizeUnityPath(string path)
		{
			return path
				.Trim()
				.Trim('\\', '/')
				.Replace('\\', '/')
				;
		}

		private void CheckSVNSupport()
		{
			string svnError;
			try {
				svnError = WiseSVNIntegration.CheckForSVNErrors();

			}
			catch (Exception ex) {
				svnError = ex.ToString();
			}

			if (string.IsNullOrEmpty(svnError))
				return;

			PersonalPrefs.EnableCoreIntegration = false;

			// NOTE: check for SVN binaries first, as it tries to recover and may get other errors!

			// System.ComponentModel.Win32Exception (0x80004005): ApplicationName='...', CommandLine='...', Native error= The system cannot find the file specified.
			// Could not find the command executable. The user hasn't installed their CLI (Command Line Interface) so we're missing an "svn.exe" in the PATH environment.
			// This is allowed only if there isn't ProjectPreference specified CLI path.
			if (svnError.Contains("0x80004005") || svnError.Contains("IOException")) {

#if UNITY_EDITOR_OSX
				// For some reason OSX doesn't have the svn binaries set to the PATH environment by default.
				// If that is the case and we find them at the usual place, just set it as a personal preference.
				if (string.IsNullOrWhiteSpace(PersonalPrefs.SvnCLIPath)) {

					// Just shooting in the dark where SVN could be installed.
					string[] osxDefaultBinariesPaths = new string[] {
						"/usr/local/bin/svn",
						"/usr/bin/svn",
						"/Applications/Xcode.app/Contents/Developer/usr/bin/svn",
						"/opt/subversion/bin/svn",
					};

					foreach(string osxPath in osxDefaultBinariesPaths) {
						if (!File.Exists(osxPath))
							continue;

						PersonalPrefs.SvnCLIPath = osxPath;

						try {
							string secondSvnError = WiseSVNIntegration.CheckForSVNErrors();
							if (!string.IsNullOrEmpty(secondSvnError))
								continue;

							PersonalPrefs.EnableCoreIntegration = true;	// Save this enabled!
							SavePreferences(PersonalPrefs, ProjectPrefs);
							Debug.Log($"SVN binaries missing in PATH environment variable. Found them at \"{osxPath}\". Setting this as personal preference.\n\n{svnError}");
							return;

						} catch(Exception) {
						}
					}

					// Failed to find binaries.
					PersonalPrefs.SvnCLIPath = string.Empty;
				}
#endif

				Debug.LogError($"SVN CLI (Command Line Interface) not found. You need to install it in order for the SVN integration to work properly. Disabling WiseSVN integration. Please fix the error and restart Unity.\n\n{svnError}");
#if UNITY_EDITOR_OSX
				Debug.LogError($"If you installed SVN via Brew or similar, you may need to add \"/usr/local/bin\" (or wherever svn binaries can be found) to your PATH environment variable and restart. Example:\nsudo launchctl config user path /usr/local/bin\nAlternatively, you may add relative SVN CLI path in your WiseSVN preferences at:\n{SVNPreferencesWindow.PROJECT_PREFERENCES_MENU}");
#endif
				return;
			}

			// svn: warning: W155007: '...' is not a working copy!
			// This can be returned when project is not a valid svn checkout. (Probably)
			if (svnError.Contains("W155007")) {
				Debug.LogError($"This project is NOT under version control (not a proper SVN checkout). Disabling WiseSVN integration.\n\n{svnError}");
				return;
			}

			// Any other error.
			if (!string.IsNullOrEmpty(svnError)) {
				Debug.LogError($"Calling SVN CLI (Command Line Interface) caused fatal error!\nDisabling WiseSVN integration. Please fix the error and restart Unity.\n{svnError}\n\n");
			} else {
				// Recovered from error, enable back integration.
				PersonalPrefs.EnableCoreIntegration = true;
			}
		}
	}
}
