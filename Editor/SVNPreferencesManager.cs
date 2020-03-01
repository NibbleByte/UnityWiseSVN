using DevLocker.VersionControl.WiseSVN.ContextMenus;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DevLocker.VersionControl.WiseSVN
{
	internal class SVNPreferencesManager : ScriptableObject
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

			public List<string> Exclude = new List<string>();

			public ProjectPreferences Clone()
			{
				var clone = (ProjectPreferences) MemberwiseClone();

				clone.Exclude = new List<string>(this.Exclude);

				return clone;
			}
		}

		public PersonalPreferences PersonalPrefs;
		public ProjectPreferences ProjectPrefs;

		public event Action PreferencesChanged;

		public bool DownloadRepositoryChanges =>
			PersonalPrefs.DownloadRepositoryChanges == BoolPreference.SameAsProjectPreference
			? ProjectPrefs.DownloadRepositoryChanges
			: PersonalPrefs.DownloadRepositoryChanges == BoolPreference.Enabled;


		private static SVNPreferencesManager m_Instance;
		public static SVNPreferencesManager Instance {
			get {
				if (m_Instance == null) {
					m_Instance = Resources.FindObjectsOfTypeAll<SVNPreferencesManager>().FirstOrDefault();

					if (m_Instance == null) {

						m_Instance = ScriptableObject.CreateInstance<SVNPreferencesManager>();
						m_Instance.name = "SVNPreferencesManager";

						// Setting this flag will tell Unity NOT to destroy this object on assembly reload (as no scene references this object).
						// We're essentially leaking this object. But we can still find it with Resources.FindObjectsOfTypeAll() after reload.
						// More info on this: https://blogs.unity3d.com/2012/10/25/unity-serialization/
						m_Instance.hideFlags = HideFlags.HideAndDontSave;

						m_Instance.LoadPreferences();

						Debug.Log($"Loaded WiseSVN Preferences. WiseSVN is turned {(m_Instance.PersonalPrefs.EnableCoreIntegration ? "on" : "off")}.");

					} else {
						// Data is already deserialized by Unity onto the scriptable object.
						// Even though OnEnable is not yet called, data is there after assembly reload.
						// It is deserialized even before static constructors [InitializeOnLoad] are called. I tested it! :D

						// The idea here is to save some time on assembly reload from deserializing json as the reload is already slow enough for big projects.
					}

					//
					// More stuff to refresh on reload.
					//
					SVNContextMenusManager.SetupContextType(m_Instance.PersonalPrefs.ContextMenusClient);
				}

				return m_Instance;
			}
		}

		public GUIContent GetFileStatusIconContent(VCFileStatus status)
		{
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
			} else {
				ProjectPrefs = new ProjectPreferences();
			}

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
