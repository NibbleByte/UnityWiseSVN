using UnityEditor;
using UnityEngine;

namespace DevLocker.VersionControl.SVN
{
	internal class SVNSimpleIntegrationProjectPreferencesWindow : EditorWindow
	{
		public const string PROJECT_PREFERENCES_MENU = "Assets/SVN/SVN Preferences";
		[MenuItem(PROJECT_PREFERENCES_MENU, false, 200)]
		private static void ShowProjectPreferences()
		{
			var window = GetWindow<SVNSimpleIntegrationProjectPreferencesWindow>(true, "SVN Project Preferences");
			window.m_PersonalPrefs = SVNPreferencesManager.Instance.PersonalPrefs.Clone();
			window.m_ProjectPrefs = SVNPreferencesManager.Instance.ProjectPrefs.Clone();
			window.ShowUtility();
			window.position = new Rect(600f, 400f, 400f, 450f);
		}

		// So SerializedObject() can work with it.
		[SerializeField] private SVNPreferencesManager.PersonalPreferences m_PersonalPrefs;
		[SerializeField] private SVNPreferencesManager.ProjectPreferences m_ProjectPrefs;

		private Vector2 m_ProjectPreferencesScroll;

		private void OnGUI()
		{
			string downloadRepositoryChangesHint = "Work online - will ask the repository if there are any changes on the server.\nEnabling this will show locks and out of date additional icons.\nRefreshes might be slower due to the network communication, but shouldn't slow down your editor.";
			const float labelWidthAdd = 40;
			EditorGUIUtility.labelWidth += labelWidthAdd;

			EditorGUILayout.HelpBox("Hint: check the the tooltips.", MessageType.Info);

			EditorGUILayout.LabelField("Personal Preferences:", EditorStyles.boldLabel);
			{
				m_PersonalPrefs.EnableCoreIntegration = EditorGUILayout.Toggle("Enable SVN integration", m_PersonalPrefs.EnableCoreIntegration);

				EditorGUI.BeginDisabledGroup(!m_PersonalPrefs.EnableCoreIntegration);

				m_PersonalPrefs.PopulateStatusesDatabase = EditorGUILayout.Toggle(new GUIContent("Enable overlay icons", "Enables overlay icons in the project windows.\nPopulates internal cache with statuses of changed entities.\nFile changes may trigger repopulation of the cache."), m_PersonalPrefs.PopulateStatusesDatabase);
				EditorGUI.BeginDisabledGroup(!m_PersonalPrefs.PopulateStatusesDatabase);

				m_PersonalPrefs.AutoRefreshDatabaseInterval = EditorGUILayout.IntField(new GUIContent("Overlay icons refresh interval", "How much seconds to wait for the next overlay icons refresh.\nNOTE: -1 will deactivate it - only file changes will trigger refresh."), m_PersonalPrefs.AutoRefreshDatabaseInterval);

				m_PersonalPrefs.DownloadRepositoryChanges =
					(SVNPreferencesManager.BoolPreference) EditorGUILayout.EnumPopup(
						new GUIContent("Check for repository changes", downloadRepositoryChangesHint + "\n\nNOTE: this will override the project setting. Coordinate this with your team.")
						, m_PersonalPrefs.DownloadRepositoryChanges);

				EditorGUI.EndDisabledGroup();

				m_PersonalPrefs.TraceLogs = (SVNTraceLogs) EditorGUILayout.EnumFlagsField(new GUIContent("Trace Logs", "Logs for nerds and debugging."), m_PersonalPrefs.TraceLogs);

				EditorGUI.EndDisabledGroup();
			}

			EditorGUILayout.Space();
			EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

			EditorGUILayout.LabelField("Project Preferences:", EditorStyles.boldLabel);
			{
				EditorGUILayout.HelpBox("These settings will be saved in the ProjectSettings folder.\nFeel free to add them to your version control system.\nCoordinate any changes here with your team.", MessageType.Warning);

				m_ProjectPreferencesScroll = EditorGUILayout.BeginScrollView(m_ProjectPreferencesScroll);

				var so = new SerializedObject(this);
				var sp = so.FindProperty("m_ProjectPrefs");

				m_ProjectPrefs.DownloadRepositoryChanges = EditorGUILayout.Toggle(new GUIContent("Check for repository changes", downloadRepositoryChangesHint), m_ProjectPrefs.DownloadRepositoryChanges);

				m_ProjectPrefs.SvnCLIPath = EditorGUILayout.TextField(new GUIContent("SVN CLI Path", "If you desire to use specific SVN CLI (svn.exe) located in the project, write down its path relative to the root folder."), m_ProjectPrefs.SvnCLIPath);

				EditorGUILayout.PropertyField(sp.FindPropertyRelative("Exclude"), new GUIContent("Exclude Paths", "Asset paths that will be ignored by the SVN integrations. Use with caution."), true);

				so.ApplyModifiedProperties();

				EditorGUILayout.EndScrollView();
			}

			if (GUILayout.Button("Save & Close")) {
				m_ProjectPrefs.SvnCLIPath = m_ProjectPrefs.SvnCLIPath.Trim();
				m_ProjectPrefs.Exclude.RemoveAll(p => string.IsNullOrWhiteSpace(p));

				SVNPreferencesManager.Instance.SavePreferences(m_PersonalPrefs, m_ProjectPrefs);

				GUI.FocusControl("");

				Close();
			}

			EditorGUIUtility.labelWidth -= labelWidthAdd;
		}
	}
}
