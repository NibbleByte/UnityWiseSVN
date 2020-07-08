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

		[Serializable]
		internal class PersonalPreferences
		{
			public bool EnableCoreIntegration = true;		// Sync file operations with SVN
			public bool PopulateStatusesDatabase = true;    // For overlay icons etc.

			// When populating the database, should it check for server changes as well (locks & modified files).
			public BoolPreference DownloadRepositoryChanges = BoolPreference.SameAsProjectPreference;

			public int AutoRefreshDatabaseInterval = 60;    // seconds; Less than 0 will disable it.
			public ContextMenusClient ContextMenusClient = ContextMenusClient.TortoiseSVN;
			public SVNTraceLogs TraceLogs = SVNTraceLogs.SVNOperations;

			public PersonalPreferences Clone()
			{
				return (PersonalPreferences) MemberwiseClone();
			}
		}

		[Serializable]
		internal class ProjectPreferences
		{
			public bool DownloadRepositoryChanges = false;

			// Use PlatformSvnCLIPath instead as it is platform independent.
			public string SvnCLIPath = string.Empty;
			public string SvnCLIPathMacOS = string.Empty;

#if UNITY_EDITOR_WIN
			public string PlatformSvnCLIPath => SvnCLIPath;
#else
			public string PlatformSvnCLIPath => SvnCLIPathMacOS;
#endif

			// Enable svn branches database.
			public bool EnableBranchesDatabase;

			// SVN parameters used for scanning branches in the SVN repo.
			public List<BranchScanParameters> BranchesDatabaseScanParameters = new List<BranchScanParameters>();

			// Show these branches on top.
			public List<string> PinnedBranches = new List<string>();

			public List<string> Exclude = new List<string>();

			public ProjectPreferences Clone()
			{
				var clone = (ProjectPreferences) MemberwiseClone();

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

			if (freshlyCreated) {

				LoadTextures();

				// If WiseSVN was just added to the project, Unity won't manage to load the textures the first time. Try again next frame.
				if (FileStatusIcons[(int)VCFileStatus.Added].image == null) {

					EditorApplication.CallbackFunction reloadTextures = null;
					reloadTextures = () => {
						EditorApplication.update -= reloadTextures;
						LoadTextures();
					};

					EditorApplication.update += reloadTextures;
				}

				Debug.Log($"Loaded WiseSVN Preferences. WiseSVN is turned {(PersonalPrefs.EnableCoreIntegration ? "on" : "off")}.");
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
			}
		}

		private void LoadTextures()
		{
			FileStatusIcons = new GUIContent[Enum.GetValues(typeof(VCFileStatus)).Length];
			FileStatusIcons[(int)VCFileStatus.Added] = new GUIContent(Resources.Load<Texture2D>("Editor/SVNOverlayIcons/SVNAddedIcon"));
			FileStatusIcons[(int)VCFileStatus.Modified] = new GUIContent(Resources.Load<Texture2D>("Editor/SVNOverlayIcons/SVNModifiedIcon"));
			FileStatusIcons[(int)VCFileStatus.Deleted] = new GUIContent(Resources.Load<Texture2D>("Editor/SVNOverlayIcons/SVNDeletedIcon"));
			FileStatusIcons[(int)VCFileStatus.Conflicted] = new GUIContent(Resources.Load<Texture2D>("Editor/SVNOverlayIcons/SVNConflictIcon"));
			FileStatusIcons[(int)VCFileStatus.Unversioned] = new GUIContent(Resources.Load<Texture2D>("Editor/SVNOverlayIcons/SVNUnversionedIcon"));

			LockStatusIcons = new GUIContent[Enum.GetValues(typeof(VCLockStatus)).Length];
			LockStatusIcons[(int)VCLockStatus.LockedHere] = new GUIContent(Resources.Load<Texture2D>("Editor/SVNOverlayIcons/Locks/SVNLockedHereIcon"), "You have locked this file.");
			LockStatusIcons[(int)VCLockStatus.BrokenLock] = new GUIContent(Resources.Load<Texture2D>("Editor/SVNOverlayIcons/Locks/SVNLockedHereIcon"), "You have a lock that is no longer valid (someone else stole it and released it).");
			LockStatusIcons[(int)VCLockStatus.LockedOther] = new GUIContent(Resources.Load<Texture2D>("Editor/SVNOverlayIcons/Locks/SVNLockedOtherIcon"), "Someone else locked this file.");
			LockStatusIcons[(int)VCLockStatus.LockedButStolen] = new GUIContent(Resources.Load<Texture2D>("Editor/SVNOverlayIcons/Locks/SVNLockedOtherIcon"), "Your lock was stolen by someone else.");

			RemoteStatusIcons = new GUIContent(Resources.Load<Texture2D>("Editor/SVNOverlayIcons/Others/SVNRemoteChangesIcon"));
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
	}
}
