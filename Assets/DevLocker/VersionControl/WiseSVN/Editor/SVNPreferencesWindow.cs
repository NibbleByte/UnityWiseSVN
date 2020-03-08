using DevLocker.VersionControl.WiseSVN.ContextMenus;
using UnityEditor;
using UnityEngine;

namespace DevLocker.VersionControl.WiseSVN
{
	internal class WiseSVNProjectPreferencesWindow : EditorWindow
	{
		public const string PROJECT_PREFERENCES_MENU = "Assets/SVN/SVN Preferences";
		[MenuItem(PROJECT_PREFERENCES_MENU, false, 200)]
		private static void ShowProjectPreferences()
		{
			var window = GetWindow<WiseSVNProjectPreferencesWindow>(true, "Wise SVN Preferences");
			window.m_PersonalPrefs = SVNPreferencesManager.Instance.PersonalPrefs.Clone();
			window.m_ProjectPrefs = SVNPreferencesManager.Instance.ProjectPrefs.Clone();
			window.ShowUtility();
			window.position = new Rect(500f, 250f, 450f, 560f);
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

			EditorGUILayout.Space();

			EditorGUILayout.BeginHorizontal();
			{
				EditorGUILayout.LabelField("Save changes:", EditorStyles.boldLabel);

				GUILayout.FlexibleSpace();

				if (GUILayout.Button("Close", GUILayout.MaxWidth(60f))) {
					GUI.FocusControl("");
					Close();
					EditorGUIUtility.ExitGUI();
				}

				var prevColor = GUI.backgroundColor;
				GUI.backgroundColor = Color.green / 1.2f;
				if (GUILayout.Button("Save All", GUILayout.MaxWidth(150f))) {
					m_ProjectPrefs.SvnCLIPath = m_ProjectPrefs.SvnCLIPath.Trim();
					m_ProjectPrefs.SvnCLIPathMacOS = m_ProjectPrefs.SvnCLIPathMacOS.Trim();
					m_ProjectPrefs.Exclude.RemoveAll(p => string.IsNullOrWhiteSpace(p));

					SVNPreferencesManager.Instance.SavePreferences(m_PersonalPrefs, m_ProjectPrefs);
				}
				GUI.backgroundColor = prevColor;
			}
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.Space();

			EditorGUILayout.LabelField("Personal Preferences:", EditorStyles.boldLabel);
			{
				EditorGUILayout.HelpBox("Hint: check the the tooltips.", MessageType.Info);

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

				m_PersonalPrefs.ContextMenusClient = (ContextMenusClient) EditorGUILayout.EnumPopup(new GUIContent("Context menus client", "Select what client should be used with the context menus."), m_PersonalPrefs.ContextMenusClient);
				if (GUI.changed) {
					var errorMsg = SVNContextMenusManager.IsCurrentlySupported(m_PersonalPrefs.ContextMenusClient);
					if (!string.IsNullOrEmpty(errorMsg)) {
						EditorUtility.DisplayDialog("Context Menus Client Issue", errorMsg, "Ok");
					}
				}

				m_PersonalPrefs.TraceLogs = (SVNTraceLogs) EditorGUILayout.EnumFlagsField(new GUIContent("Trace logs", "Logs for nerds and debugging."), m_PersonalPrefs.TraceLogs);

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
				m_ProjectPrefs.SvnCLIPathMacOS = EditorGUILayout.TextField(new GUIContent("SVN CLI Path MacOS", "Same as above, but for MacOS."), m_ProjectPrefs.SvnCLIPathMacOS);

				EditorGUILayout.PropertyField(sp.FindPropertyRelative("Exclude"), new GUIContent("Exclude Paths", "Asset paths that will be ignored by the SVN integrations. Use with caution."), true);

				so.ApplyModifiedProperties();

				EditorGUILayout.EndScrollView();
			}

			EditorGUILayout.Space();
			EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

			EditorGUILayout.LabelField("About:", EditorStyles.boldLabel);
			{
				EditorGUILayout.LabelField("Created by Filip Slavov (NibbleByte)");
				EditorGUILayout.LabelField("In collaboration with Snapshot Games");

				var style = new GUIStyle(EditorStyles.label);
				style.normal.textColor = Color.blue;
				style.active.textColor = Color.red;
				if (GUILayout.Button("Icons taken from TortoiseSVN (created by Lübbe Onken)", style, GUILayout.ExpandWidth(true))) {
					var assetStoreURL = "https://tortoisesvn.net/";
					Application.OpenURL(assetStoreURL);
				}


				if (GUILayout.Button("Source at GitHub", GUILayout.MaxWidth(EditorGUIUtility.labelWidth))) {
					var githubURL = "https://github.com/NibbleByte/UnityWiseSVN";
					Application.OpenURL(githubURL);
				}

				if (GUILayout.Button("Plugin at Asset Store",GUILayout.MaxWidth(EditorGUIUtility.labelWidth))) {
					var assetStoreURL = "https://assetstore.unity.com/packages/tools/version-control/wise-svn-162636";
					Application.OpenURL(assetStoreURL);
				}
			}

			//EditorGUILayout.Space();
			//EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);



			EditorGUILayout.Space();


			EditorGUIUtility.labelWidth -= labelWidthAdd;
		}
	}
}
