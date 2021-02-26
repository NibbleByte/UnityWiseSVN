using System;
using UnityEditor;
using UnityEngine;

namespace DevLocker.VersionControl.WiseSVN.Preferences
{
#if !UNITY_2017
	// Provide Unity Preferences / Project Settings entry for the WiseSVN.
	// It is really a big button that redirects to the original preferences window.
	// Tried to draw the full window, but failed, because I need a SerializedObject() to use and didn't care too much about it.
	[Serializable]
	internal class WiseSVNProjectPreferencesSettingsProvider : SettingsProvider
	{
		private const string m_SettingsProviderName = "WiseSVN";
		private readonly static string[] m_Keywords = new[] { "Wise", "SVN", "TortoiseSVM", "SnailSVN" };

		private WiseSVNProjectPreferencesSettingsProvider(string path, SettingsScope scope)
			: base(path, scope)
		{

		}

		[SettingsProvider]
		public static SettingsProvider CreateUserInstance()
		{
			var provider = new WiseSVNProjectPreferencesSettingsProvider("Preferences/" + m_SettingsProviderName, SettingsScope.User);
			provider.keywords = m_Keywords;

			return provider;
		}

		[SettingsProvider]
		public static SettingsProvider CreateProjectInstance()
		{
			var provider = new WiseSVNProjectPreferencesSettingsProvider("Project/" + m_SettingsProviderName, SettingsScope.Project);
			provider.keywords = m_Keywords;

			return provider;
		}

		public override void OnGUI(string searchContext)
		{
			base.OnGUI(searchContext);

			if (GUILayout.Button("Open WiseSVN preferences", GUILayout.ExpandWidth(false), GUILayout.Height(30f))) {
				SVNPreferencesWindow.ShowProjectPreferences();
			}

			EditorGUILayout.Space();
			EditorGUILayout.Space();
			EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

			SVNPreferencesWindow.DrawHelpAbout();
		}
	}
#endif

}
