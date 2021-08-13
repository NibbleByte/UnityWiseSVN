using DevLocker.VersionControl.WiseSVN.AutoLocking;
using DevLocker.VersionControl.WiseSVN.Branches;
using DevLocker.VersionControl.WiseSVN.ContextMenus;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DevLocker.VersionControl.WiseSVN.Preferences
{
	internal class SVNPreferencesWindow : EditorWindow
	{
		public enum PreferencesTab
		{
			Personal = 0,
			Project = 1,
			About = 2,
		}

		private static int m_RandomVideoIndex = -1;
		private static Dictionary<string, string> m_RandomVideos = new Dictionary<string, string>() {
			{ "Fire-Arrows!", "https://www.youtube.com/watch?v=zTd_0FRAwOQ" },
			{ "Climate Change Is An Absolute Nightmare", "https://www.youtube.com/watch?v=uqwvf6R1_QY" },
			{ "HL Cat", "https://www.youtube.com/watch?v=dGUYJn9kjJ4" },
			{ "YMS: Kimba the White Lion", "https://www.youtube.com/watch?v=G5B1mIfQuo4" },
			{ "Wildebeest", "https://www.youtube.com/watch?v=JMJXvsCLu6s" },
			{ "Carwash", "https://www.youtube.com/watch?v=-sAjrL0fJzw" },
			{ "How to be a Pirate: Captain Edition", "https://www.youtube.com/watch?v=3YFeE1eDlD0" },
			{ "A Brief Look at Texting and the Internet in Film", "https://www.youtube.com/watch?v=uFfq2zblGXw" },
			{ "The Problems with First Past the Post Voting Explained", "https://www.youtube.com/watch?v=s7tWHJfhiyo" },
			{ "C&C Stupid Zero Hour Facts! [09]: Combat Chinook.", "https://www.youtube.com/watch?v=_hlq8ZJ4tqo" },
			{ "Broken Kill Counts in Classic Serious Sam (Part 1)", "https://www.youtube.com/watch?v=BF0UFuZsHvo" },
			{ "The Patient Gamer", "https://www.youtube.com/watch?v=wiMyCzezfTg" },
			{ "Friendly Shadow | Dystopian Animated Short Film ", "https://www.youtube.com/watch?v=D0sCsXFAdjY" },
			{ "Soviet Car Gas Cap Lock Decoded ", "https://www.youtube.com/watch?v=NhVR7gOSXPo" },
			{ "Using Glitches and Tricks to Beat Half-Life 2", "https://www.youtube.com/watch?v=gm9lE97sIJo" },
			{ "OK Go - Upside Down & Inside Out", "https://www.youtube.com/watch?v=LWGJA9i18Co" },
			{ "The Pythagorean Siphon Inside Your Washing Machine", "https://www.youtube.com/watch?v=Cg8KQfaT9xY" },
		};

		public const string PROJECT_PREFERENCES_MENU = "Assets/SVN/SVN Preferences";
		[MenuItem(PROJECT_PREFERENCES_MENU, false, 200)]
		public static void ShowProjectPreferences()
		{
			ShowProjectPreferences(PreferencesTab.Personal);
		}

		public static void ShowProjectPreferences(PreferencesTab tab)
		{
			var window = GetWindow<SVNPreferencesWindow>(true, "Wise SVN Preferences");
			window.m_PersonalPrefs = SVNPreferencesManager.Instance.PersonalPrefs.Clone();
			window.m_ProjectPrefs = SVNPreferencesManager.Instance.ProjectPrefs.Clone();
			window.ShowUtility();
			window.position = new Rect(500f, 250f, 520f, 400f);
			window.minSize = new Vector2(520f, 400f);
			window.m_SelectedTab = tab;
		}

		// So SerializedObject() can work with it.
		[SerializeField] private SVNPreferencesManager.PersonalPreferences m_PersonalPrefs;
		[SerializeField] private SVNPreferencesManager.ProjectPreferences m_ProjectPrefs;

		private PreferencesTab m_SelectedTab = PreferencesTab.Personal;
		private static readonly string[] m_PreferencesTabsNames = Enum.GetNames(typeof(PreferencesTab));

		private Vector2 m_ProjectPreferencesScroll;
		private const string m_DownloadRepositoryChangesHint = "Work online - will ask the repository if there are any changes on the server.\nEnabling this will show locks and out of date additional icons.\nRefreshes might be slower due to the network communication, but shouldn't slow down your editor.";

		private bool m_FoldAutoLockHint = true;
		private bool m_FoldBranchesDatabaseHint = true;

		private SerializedObject m_SerializedObject;

		private void OnEnable()
		{
			m_SerializedObject = new SerializedObject(this);
		}

		private void OnDisable()
		{
			if (m_SerializedObject != null) {
				m_SerializedObject.Dispose();
			}
		}

		private void OnGUI()
		{
			m_SerializedObject.Update();

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

					SanitizeBeforeSave();
					SVNPreferencesManager.Instance.SavePreferences(m_PersonalPrefs, m_ProjectPrefs);

					// When turning on the integration do instant refresh.
					// Works when editor started with disabled integration. Doing it here to avoid circle dependency.
					if (m_PersonalPrefs.EnableCoreIntegration) {
						SVNStatusesDatabase.Instance.InvalidateDatabase();
						SVNBranchesDatabase.Instance.InvalidateDatabase();
						SVNAutoLockingDatabaseStarter.TryStartIfNeeded();
					}
				}
				GUI.backgroundColor = prevColor;
			}
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.Space();

			m_SelectedTab = (PreferencesTab)GUILayout.Toolbar((int)m_SelectedTab, m_PreferencesTabsNames);

			m_ProjectPreferencesScroll = EditorGUILayout.BeginScrollView(m_ProjectPreferencesScroll);

			switch (m_SelectedTab) {
				case PreferencesTab.Personal:
					DrawPersonalPreferences();
					break;
				case PreferencesTab.Project:
					DrawProjectPreferences();
					break;
				case PreferencesTab.About:
					DrawHelpAbout();
					break;
			}

			EditorGUILayout.EndScrollView();

			EditorGUILayout.Space();


			EditorGUIUtility.labelWidth -= labelWidthAdd;

			m_SerializedObject.ApplyModifiedProperties();
		}

		private void SanitizeBeforeSave()
		{
			m_ProjectPrefs.SvnCLIPath = SVNPreferencesManager.SanitizeUnityPath(m_ProjectPrefs.SvnCLIPath);
			m_ProjectPrefs.SvnCLIPathMacOS = SVNPreferencesManager.SanitizeUnityPath(m_ProjectPrefs.SvnCLIPathMacOS);
			m_ProjectPrefs.Exclude = SanitizePathsList(m_ProjectPrefs.Exclude);

			if (m_ProjectPrefs.EnableAutoLocking) {

				if (m_ProjectPrefs.AutoLockingParameters.Count == 0) {
					EditorUtility.DisplayDialog("Auto-Locking", "In order to use auto-locking, you must provide at least one auto-locking parameters element.\n\nAuto-Locking will be disabled.", "Ok");
					m_ProjectPrefs.EnableAutoLocking = false;
				}

				m_ProjectPrefs.AutoLockingParameters = m_ProjectPrefs.AutoLockingParameters
					.Select(sp => sp.Sanitized())
					.ToList();

				if (m_ProjectPrefs.AutoLockingParameters.Any(sp => !sp.IsValid)) {
					EditorUtility.DisplayDialog("Auto-Locking", "Some of the auto-locking parameters have invalid data. Please fix it.\n\nAuto-Locking will be disabled.", "Ok");
					m_ProjectPrefs.EnableAutoLocking = false;
				}
			}

			if (m_ProjectPrefs.EnableBranchesDatabase) {

				if (m_ProjectPrefs.BranchesDatabaseScanParameters.Count == 0) {
					EditorUtility.DisplayDialog("Branches Database", "In order to use Branches Database, you must provide at least one scan parameters element.\n\nBranches Database will be disabled.", "Ok");
					m_ProjectPrefs.EnableBranchesDatabase = false;
				}

				m_ProjectPrefs.BranchesDatabaseScanParameters = m_ProjectPrefs.BranchesDatabaseScanParameters
					.Select(sp => sp.Sanitized())
					.ToList();

				m_ProjectPrefs.PinnedBranches = SanitizeStringsList(m_ProjectPrefs.PinnedBranches);

				if (m_ProjectPrefs.BranchesDatabaseScanParameters.Any(sp => !sp.IsValid)) {
					EditorUtility.DisplayDialog("Branches Database", "Some of the branches scan parameters have invalid data. Please fix it.\n\nBranches Database will be disabled.", "Ok");
					m_ProjectPrefs.EnableBranchesDatabase = false;
				}
			}
		}

		private static List<string> SanitizeStringsList(IEnumerable<string> list)
		{
			return list
				.Select(str => str.Trim())
				.Where(str => !string.IsNullOrEmpty(str))
				.ToList();
		}

		private static List<string> SanitizePathsList(IEnumerable<string> list)
		{
			return list
				.Select(SVNPreferencesManager.SanitizeUnityPath)
				.Where(str => !string.IsNullOrEmpty(str))
				.ToList();
		}

		private void DrawPersonalPreferences()
		{
			EditorGUILayout.HelpBox("These are personal preferences stored in the registry.\nHint: check the the tooltips.", MessageType.Info);

			m_PersonalPrefs.EnableCoreIntegration = EditorGUILayout.Toggle("Enable SVN integration", m_PersonalPrefs.EnableCoreIntegration);

			EditorGUI.BeginDisabledGroup(!m_PersonalPrefs.EnableCoreIntegration);

			m_PersonalPrefs.PopulateStatusesDatabase = EditorGUILayout.Toggle(new GUIContent("Enable overlay icons", "Enables overlay icons in the project windows.\nPopulates internal cache with statuses of changed entities.\nFile changes may trigger repopulation of the cache."), m_PersonalPrefs.PopulateStatusesDatabase);
			EditorGUI.BeginDisabledGroup(!m_PersonalPrefs.PopulateStatusesDatabase);

			m_PersonalPrefs.ShowNormalStatusOverlayIcon = EditorGUILayout.Toggle(new GUIContent("Show Normal Status Green Icon", "Normal status is versioned asset that doesn't have any changes."), m_PersonalPrefs.ShowNormalStatusOverlayIcon);
			m_PersonalPrefs.AutoRefreshDatabaseInterval = EditorGUILayout.IntField(new GUIContent("Overlay icons refresh interval", "How much seconds to wait for the next overlay icons refresh.\nNOTE: -1 will deactivate it - only file changes will trigger refresh."), m_PersonalPrefs.AutoRefreshDatabaseInterval);

			m_PersonalPrefs.DownloadRepositoryChanges =
				(SVNPreferencesManager.BoolPreference)EditorGUILayout.EnumPopup(
					new GUIContent("Check for repository changes", m_DownloadRepositoryChangesHint + "\n\nNOTE: this will override the project setting. Coordinate this with your team.")
					, m_PersonalPrefs.DownloadRepositoryChanges);

			EditorGUI.EndDisabledGroup();

			m_PersonalPrefs.ContextMenusClient = (ContextMenusClient)EditorGUILayout.EnumPopup(new GUIContent("Context menus client", "Select what client should be used with the context menus."), m_PersonalPrefs.ContextMenusClient);
			if (GUI.changed) {
				var errorMsg = SVNContextMenusManager.IsCurrentlySupported(m_PersonalPrefs.ContextMenusClient);
				if (!string.IsNullOrEmpty(errorMsg)) {
					EditorUtility.DisplayDialog("Context Menus Client Issue", errorMsg, "Ok");
				}
			}

			m_PersonalPrefs.TraceLogs = (SVNTraceLogs)EditorGUILayout.EnumFlagsField(new GUIContent("Trace logs", "Logs for nerds and debugging."), m_PersonalPrefs.TraceLogs);

			EditorGUI.EndDisabledGroup();
		}

		private void DrawProjectPreferences()
		{
			EditorGUILayout.HelpBox("These settings will be saved in the ProjectSettings folder.\nFeel free to add them to your version control system.\nCoordinate any changes here with your team.", MessageType.Warning);

			var sp = m_SerializedObject.FindProperty("m_ProjectPrefs");

			m_ProjectPrefs.DownloadRepositoryChanges = EditorGUILayout.Toggle(new GUIContent("Check for repository changes", m_DownloadRepositoryChangesHint), m_ProjectPrefs.DownloadRepositoryChanges);

			m_ProjectPrefs.SvnCLIPath = EditorGUILayout.TextField(new GUIContent("SVN CLI Path", "If you desire to use specific SVN CLI (svn.exe) located in the project, write down its path relative to the project folder."), m_ProjectPrefs.SvnCLIPath);
			m_ProjectPrefs.SvnCLIPathMacOS = EditorGUILayout.TextField(new GUIContent("SVN CLI Path MacOS", "Same as above, but for MacOS."), m_ProjectPrefs.SvnCLIPathMacOS);

			m_ProjectPrefs.MoveBehaviour = (SVNMoveBehaviour)EditorGUILayout.EnumPopup(new GUIContent("Assets move behaviour", "Depending on your SVN repository, sometimes you may need to execute move commands as simple add and remove operations, loosing their history. Use with caution.\n(I'm looking at you github that emulates svn)."), m_ProjectPrefs.MoveBehaviour);

			if (!m_PersonalPrefs.PopulateStatusesDatabase) {
				EditorGUILayout.HelpBox("Auto-locking requires enabled overlay icons support from the Personal preferences!", MessageType.Warning);
			}
			EditorGUI.BeginDisabledGroup(!m_PersonalPrefs.PopulateStatusesDatabase);

			m_ProjectPrefs.EnableAutoLocking = EditorGUILayout.Toggle(new GUIContent("Enable Auto-Locking", "Automatically svn lock asset when it or its meta file gets modified."), m_ProjectPrefs.EnableAutoLocking);
			if (m_ProjectPrefs.EnableAutoLocking) {
				EditorGUI.indentLevel++;

				m_FoldAutoLockHint = EditorGUILayout.Foldout(m_FoldAutoLockHint, "Auto-Locking Hint:");
				var autolockHint = "Every time the user modifies any asset or its meta, svn lock will be executed (unless already locked).\n" +
								   "If asset is already locked by others and was NOT modified locally before, warning will be shown to the user.\n" +
								   "SVN lock will be executed on all matching modified assets on start up as well.\n\n" +
								   "Describe below what asset folders and asset types should be monitored for auto-locking.\n" +
								   "To monitor the whole project, type in \"Assets\" for TargetFolder\n" +
								   "Coordinate this with your team.\n" +
								   "Must have at least one entry to work properly."

					;
				if (m_FoldAutoLockHint) {
					EditorGUILayout.BeginHorizontal();
					GUILayout.Space((EditorGUI.indentLevel + 1) * 16f);
					GUILayout.Label(autolockHint, EditorStyles.helpBox);
					EditorGUILayout.EndHorizontal();
				}

				EditorGUILayout.PropertyField(sp.FindPropertyRelative("AutoLockMessage"), new GUIContent("Lock Message", SVNPreferencesManager.ProjectPreferences.LockMessageHint));

				//EditorGUILayout.PropertyField(sp.FindPropertyRelative("AutoLockingParameters"));

				// HACK: PropertyDrawers are not drawn in EditorWindow! Draw everything manually to have custom stuff!
				var alProperty = sp.FindPropertyRelative("AutoLockingParameters").Copy();
				var alPropertyEnd = alProperty.GetEndProperty();

				var prevIndentLevel = EditorGUI.indentLevel;

				EditorGUILayout.PropertyField(alProperty, false);   // Draw the AutoLockingParameters itself always.

				while (alProperty.NextVisible(alProperty.isExpanded) && !SerializedProperty.EqualContents(alProperty, alPropertyEnd)) {
					EditorGUI.indentLevel = prevIndentLevel + alProperty.depth - 1;

					var label = new GUIContent(alProperty.displayName);
					label.tooltip = GetSerializedPropertyTooltip<AutoLockingParameters>(alProperty, false);

					if (alProperty.type == "Enum") {
						// HACK: If it is enum, it is probably AssetType. No real way to know (unless by field name)!
						alProperty.intValue = (int) (AssetType)EditorGUILayout.EnumFlagsField(label, (AssetType) alProperty.intValue);
					} else {
						EditorGUILayout.PropertyField(alProperty, label, false);
					}
				}
				EditorGUI.indentLevel = prevIndentLevel;

				EditorGUI.indentLevel--;
				EditorGUILayout.Space();
			}

			EditorGUI.EndDisabledGroup();

			const string branchesEnableHint = "Scans the SVN repository for Unity projects in branches and keeps them in a simple database.\n\nSingle scan may take up to a few minutes, depending on your network connection and the complexity of your repository.";
			m_ProjectPrefs.EnableBranchesDatabase = EditorGUILayout.Toggle(new GUIContent("Enable Branches Database", branchesEnableHint), m_ProjectPrefs.EnableBranchesDatabase);
			if (m_ProjectPrefs.EnableBranchesDatabase) {

				EditorGUI.indentLevel++;
				m_FoldBranchesDatabaseHint = EditorGUILayout.Foldout(m_FoldBranchesDatabaseHint, "Branches Database Hint:");
				var branchesHint = "Provide at least one branches scan parameter below.\n\n" +
				                   "\"Entry Point URL\" serves as a starting location where scan will commence.\n\n" +
				                   "\"Branch Signature Root Entries\" should contain names of files or folders that mark the beginning of a branch. This will be shown as a branch's name.\n\n" +
				                   "Example setup:\n" +
				                   "  /branches/VariantA/Server\n" +
				                   "  /branches/VariantA/UnityClient\n" +
				                   "  /branches/VariantB/Server\n" +
				                   "  /branches/VariantB/UnityClient\n\n" +
				                   "Set \"Entry Point URL\" to \"https://companyname.com/branches\" as a starting point.\n" +
				                   "\"VariantA\" and \"VariantB\" should be considered as branch roots.\n" +
				                   "UnityClient folder is not a branch root.\n" +
				                   "Set \"Branch Signature Root Entries\" to { \"Server\", \"UnityClient\" } to match the roots ...\n\n" +
				                   "Only one Unity project per branch is supported!" +
				                   ""
					;
				if (m_FoldBranchesDatabaseHint) {
					EditorGUILayout.BeginHorizontal();
					GUILayout.Space((EditorGUI.indentLevel + 1) * 16f);
					GUILayout.Label(branchesHint, EditorStyles.helpBox);
					EditorGUILayout.EndHorizontal();
				}

				EditorGUILayout.PropertyField(sp.FindPropertyRelative("BranchesDatabaseScanParameters"), new GUIContent("Branches Scan Parameters", "Must have at least one entry to work properly."), true);

				EditorGUILayout.PropertyField(sp.FindPropertyRelative("PinnedBranches"), new GUIContent("Pinned Branches", "Pin these branches at the top."), true);

				EditorGUI.indentLevel--;
			}

			EditorGUILayout.PropertyField(sp.FindPropertyRelative("Exclude"), new GUIContent("Exclude Paths", "Relative path (contains '/') or asset name to be ignored by the SVN integrations. Use with caution.\n\nExample: \"Assets/Scenes/Baked\" or \"_deprecated\""), true);
		}

		public static void DrawHelpAbout()
		{
			EditorGUILayout.LabelField("Help:", EditorStyles.boldLabel);

			if (GUILayout.Button("Documentation", GUILayout.MaxWidth(EditorGUIUtility.labelWidth))) {
				var assets = AssetDatabase.FindAssets("WiseSVN-Documentation");
				if (assets.Length == 0) {
					EditorUtility.DisplayDialog("Documentation missing!", "The documentation you requested is missing.", "Ok");
				} else {
					Application.OpenURL(Environment.CurrentDirectory + "/" + AssetDatabase.GUIDToAssetPath(assets[0]));
				}
			}

			EditorGUILayout.LabelField("About:", EditorStyles.boldLabel);
			{
				var urlStyle = new GUIStyle(EditorStyles.label);
				urlStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(1.00f, 0.65f, 0.00f) : Color.blue;
				urlStyle.active.textColor = Color.red;

				const string mail = "NibbleByte3@gmail.com";

				GUILayout.Label("Created by Filip Slavov", GUILayout.ExpandWidth(false));
				if (GUILayout.Button(mail, urlStyle, GUILayout.ExpandWidth(false))) {
					Application.OpenURL("mailto:"+mail);
				}

				EditorGUILayout.LabelField("In collaboration with Snapshot Games");



				EditorGUILayout.BeginHorizontal();

				if (GUILayout.Button("Asset Store", urlStyle, GUILayout.ExpandWidth(false))) {
					var assetStoreURL = "https://assetstore.unity.com/packages/tools/version-control/wise-svn-162636";
					Application.OpenURL(assetStoreURL);
				}

				GUILayout.Label("|", GUILayout.ExpandWidth(false));

				if (GUILayout.Button("GitHub", urlStyle, GUILayout.ExpandWidth(false))) {
					var githubURL = "https://github.com/NibbleByte/UnityWiseSVN";
					Application.OpenURL(githubURL);
				}

				GUILayout.Label("|", GUILayout.ExpandWidth(false));

				if (GUILayout.Button("Unity Forum", urlStyle, GUILayout.ExpandWidth(false))) {
					var unityForumURL = "https://forum.unity.com/threads/wise-svn-powerful-tortoisesvn-snailsvn-integration.844168";
					Application.OpenURL(unityForumURL);
				}

				GUILayout.Label("|", GUILayout.ExpandWidth(false));

				if (GUILayout.Button("Reddit", urlStyle, GUILayout.ExpandWidth(false))) {
					var redditURL = "https://www.reddit.com/r/Unity3D/comments/fgjovk/finally_a_fully_working_tortoisesvn_snailsvn";
					Application.OpenURL(redditURL);
				}

				EditorGUILayout.EndHorizontal();

				EditorGUILayout.Space();

				if (GUILayout.Button("Icons taken from TortoiseSVN (created by Lьbbe Onken)", urlStyle, GUILayout.ExpandWidth(true))) {
					var assetStoreURL = "https://tortoisesvn.net/";
					Application.OpenURL(assetStoreURL);
				}

				GUILayout.FlexibleSpace();

				EditorGUILayout.LabelField("Random Video:", EditorStyles.boldLabel);
				GUILayout.Label("This plugin took a lot of time to make.\nHere are some random videos worth spreading that distracted me along the way. :D");

				if (m_RandomVideoIndex == -1) {
					m_RandomVideoIndex = UnityEngine.Random.Range(0, m_RandomVideos.Count);
				}

				if (GUILayout.Button(m_RandomVideos.Keys.ElementAt(m_RandomVideoIndex), urlStyle, GUILayout.ExpandWidth(false))) {
					Application.OpenURL(m_RandomVideos.Values.ElementAt(m_RandomVideoIndex));
				}
				if (GUILayout.Button("Next Video", GUILayout.ExpandWidth(false))) {
					m_RandomVideoIndex = (m_RandomVideoIndex + 1) % m_RandomVideos.Count;
				}
			}
		}

		private static string GetSerializedPropertyTooltip<Type>(SerializedProperty serializedProperty, bool inherit)
		{
			if (null == serializedProperty) {
				return string.Empty;
			}

			System.Reflection.FieldInfo field = typeof(Type).GetField(serializedProperty.name);
			if (null == field) {
				return string.Empty;
			}

			TooltipAttribute[] attributes = (TooltipAttribute[]) field.GetCustomAttributes(typeof(TooltipAttribute), inherit);

			return attributes.Length > 0 ? attributes[0].tooltip : string.Empty;
		}

	}
}
