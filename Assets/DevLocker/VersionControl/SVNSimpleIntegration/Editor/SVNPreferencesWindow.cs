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
			window.m_ProjectPreferences = SVNSimpleIntegration.m_ProjectPreferences;
			window.m_EnableIntegration = SVNSimpleIntegration.Enabled;
			window.m_TraceLogs = SVNSimpleIntegration.TraceLogs;
			window.m_EnableOverlayIcons = SVNOverlayIcons.Enabled;
			window.m_AutoRefreshInterval = (int) SVNOverlayIcons.AutoRefreshInterval;
			window.ShowUtility();
			window.position = new Rect(600f, 400f, 400f, 350f);
		}

		private bool m_EnableIntegration;
		private bool m_EnableOverlayIcons;
		private int m_AutoRefreshInterval;
		private SVNTraceLogs m_TraceLogs;

		// So SerializedObject() can work with it.
		[SerializeField] private SVNSimpleIntegration.ProjectPreferences m_ProjectPreferences;

		private Vector2 m_ProjectPreferencesScroll;

		private void OnGUI()
		{
			EditorGUILayout.LabelField("Personal Preferences:", EditorStyles.boldLabel);
			{
				m_EnableIntegration = EditorGUILayout.Toggle("Enable SVN Integration", m_EnableIntegration);

				EditorGUI.BeginDisabledGroup(!m_EnableIntegration);

				m_EnableOverlayIcons = EditorGUILayout.Toggle("Enable Overlay Icons", m_EnableOverlayIcons);
				EditorGUI.BeginDisabledGroup(!m_EnableOverlayIcons);
				m_AutoRefreshInterval = EditorGUILayout.IntField(new GUIContent("Overlay icons refresh interval", "How much seconds to wait for the next overlay icons refresh.\nNOTE: -1 will deactivate it."), m_AutoRefreshInterval);
				EditorGUI.EndDisabledGroup();

				m_TraceLogs = (SVNTraceLogs) EditorGUILayout.EnumFlagsField("Trace Logs", m_TraceLogs);

				EditorGUI.EndDisabledGroup();
			}

			EditorGUILayout.Space();

			EditorGUILayout.LabelField("Project Preferences:", EditorStyles.boldLabel);
			{
				EditorGUILayout.HelpBox("These settings will be saved in the ProjectSettings folder. Feel free to add them to your version control system.", MessageType.Info);

				m_ProjectPreferencesScroll = EditorGUILayout.BeginScrollView(m_ProjectPreferencesScroll);

				var so = new SerializedObject(this);
				var sp = so.FindProperty("m_ProjectPreferences");

				EditorGUILayout.PropertyField(sp, true);

				so.ApplyModifiedProperties();

				EditorGUILayout.EndScrollView();
			}

			if (GUILayout.Button("Save")) {
				m_ProjectPreferences.SvnCLIPath = m_ProjectPreferences.SvnCLIPath.Trim();

				// NOTE: Order is important.
				SVNSimpleIntegration.SavePreferences(m_EnableIntegration, m_TraceLogs, m_ProjectPreferences);
				SVNOverlayIcons.SavePreferences(m_EnableOverlayIcons, m_AutoRefreshInterval);

				Close();
			}
		}
	}
}
