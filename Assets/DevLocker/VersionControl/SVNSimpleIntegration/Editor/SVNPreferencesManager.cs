using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DevLocker.VersionControl.SVN
{
	[InitializeOnLoad]
	internal class SVNPreferencesManager : ScriptableObject
	{
		private const string PERSONAL_PREFERENCES_KEY = "SVNSimpleIntegration";
		private const string PROJECT_PREFERENCES_PATH = "ProjectSettings/SVNSimpleIntegration.prefs";

		[Serializable]
		internal class PersonalPreferences
		{
			public bool EnabledCoreIntegration = true;
			public bool EnabledOverlayIcons = true;
			public bool EnabledCheckLocks = false;
			public int AutoRefreshInterval = 60; // // seconds; Less than 0 will disable it.
			public SVNTraceLogs TraceLogs = SVNTraceLogs.SVNOperations;

			public PersonalPreferences Clone()
			{
				return (PersonalPreferences) MemberwiseClone();
			}
		}

		[Serializable]
		internal class ProjectPreferences
		{
			public string SvnCLIPath = string.Empty;

			public List<string> Exclude = new List<string>();

			public ProjectPreferences Clone()
			{
				return new ProjectPreferences() {
					SvnCLIPath = this.SvnCLIPath,
					Exclude = new List<string>(this.Exclude),
				};
			}
		}

		public PersonalPreferences PersonalPrefs;
		public ProjectPreferences ProjectPrefs;

		public event Action PreferencesChanged;


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

						Debug.Log($"Loaded SVN Simple Integration Preferences. The integration is turned {(m_Instance.PersonalPrefs.EnabledCoreIntegration ? "on" : "off")}.");

					} else {
						// Preferences are already deserialized by Unity onto the scriptable object.
						// Even though OnEnable is not yet called, data is there after assembly reload.
						// It is deserialized even before static constructors [InitializeOnLoad] are called. I tested it! :D

						// The idea here is to save some time on assembly reload from deserializing json as the reload is already slow enough for big projects.
					}
				}

				return m_Instance;
			}
		}

		private void LoadPreferences()
		{
			var personalPrefsData = EditorPrefs.GetString(PERSONAL_PREFERENCES_KEY, string.Empty);
			if (!string.IsNullOrEmpty(personalPrefsData)) {
				PersonalPrefs = JsonUtility.FromJson<PersonalPreferences>(personalPrefsData);
			} else {
				PersonalPrefs = new PersonalPreferences();
			}

			if (File.Exists(PROJECT_PREFERENCES_PATH)) {
				ProjectPrefs = JsonUtility.FromJson<ProjectPreferences>(File.ReadAllText(PROJECT_PREFERENCES_PATH));
			} else {
				ProjectPrefs = new ProjectPreferences();
			}
		}


		public void SavePreferences(PersonalPreferences personalPrefs, ProjectPreferences projectPrefs)
		{
			PersonalPrefs = personalPrefs;
			ProjectPrefs = projectPrefs;

			EditorPrefs.SetString(PERSONAL_PREFERENCES_KEY, JsonUtility.ToJson(PersonalPrefs));

			File.WriteAllText(PROJECT_PREFERENCES_PATH, JsonUtility.ToJson(ProjectPrefs, true));

			PreferencesChanged?.Invoke();
		}
	}
}
