using DevLocker.VersionControl.WiseSVN.ContextMenus;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DevLocker.VersionControl.WiseSVN.Branches
{
	/// <summary>
	/// Popup that shows found branches. User can open "Repo Browser" or "Show log" for the selected file.
	/// </summary>
	public class SVNBranchSelectorWindow : EditorWindow
	{
		private bool m_Initialized = false;

		private Object m_TargetAsset;
		private string m_BranchFilter;
		private Vector2 m_BranchesScroll;

		private SVNBranchesDatabase Database => SVNBranchesDatabase.Instance;

		private enum BranchContextMenu
		{
			CopyBranchName = 1,
			CopyBranchURL = 2,
			CopyBranchRelativeURL = 3,

			CopyTargetAssetBranchURL = 11,
			CopyTargetAssetBranchRelativeURL = 12,

			Cancel = 101,
		}

		#region Conflicts Scans

		private enum ConflictsScanState
		{
			None,
			WaitingForBranchesDatabase,
			Scanning,
			Scanned,
			Error,
		}

		private enum ConflictState
		{
			None,
			Pending,
			Normal,
			Conflicted,
			Added,
			Missing,
			Error,
		}

		private enum ConflictsScanLimitType
		{
			Unlimited,
			Revisions,
			Days,
			Weeks,
			Months,
		}

		[System.Serializable]
		private struct ConflictsScanJobData
		{
			public string TargetAssetPath;
			public ConflictsScanLimitType LimitType;
			public int LimitParam;
			public ConflictsScanResult[] Reults;
		}

		[System.Serializable]
		private struct ConflictsScanResult
		{
			public string UnityURL;
			public ConflictState State;
		}

		private bool m_ShowConflictsMenu = false;
		private ConflictsScanLimitType m_ConflictsScanLimitType;
		private int m_ConflictsScanLimitParam = 1;

		private bool m_ConflictsShowNormal = true;

		private ConflictsScanState m_ConflictsScanState = ConflictsScanState.None;

		private ConflictsScanResult[] m_ConflictsScanResults = new ConflictsScanResult[0];
		private System.Threading.Thread m_ConflictsScanThread;

		#endregion


		private GUIStyle BorderStyle;

		private GUIContent RefreshBranchesContent;


		private GUIStyle ToolbarTitleStyle;
		private GUIStyle ToolbarLabelStyle;

		private GUIStyle SearchFieldStyle;
		private GUIStyle SearchFieldCancelStyle;
		private GUIStyle SearchFieldCancelEmptyStyle;

		// Branch buttons
		private GUIContent RepoBrowserContent;
		private GUIContent ShowLogContent;
		private GUIContent SwitchBranchContent;

		private GUIContent ScanForConflictsContent;

		private GUIContent ConflictsPendingContent;
		private GUIContent ConflictsFoundContent;
		private GUIContent ConflictsNormalContent;
		private GUIContent ConflictsAddedContent;
		private GUIContent ConflictsMissingContent;
		private GUIContent ConflictsErrorContent;

		private GUIStyle MiniIconButtonlessStyle;

		private GUIStyle BranchLabelStyle;

		private GUIContent RevisionsHintContent;

		private readonly string[] LoadingDots = new[] { ".  ", ".. ", "..." };

		private const float ToolbarsTitleWidth = 70f;

		[MenuItem("Assets/SVN/Branch Selector", false, 190)]
		private static void OpenBranchesSelector()
		{
			var window = CreateInstance<SVNBranchSelectorWindow>();
			window.titleContent = new GUIContent("Branch Selector");

			var assetPath = Selection.assetGUIDs.Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault();
			window.m_TargetAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);

			window.ShowUtility();
		}

		private void InitializeStyles()
		{
			BorderStyle = new GUIStyle(GUI.skin.box);
			BorderStyle.padding = new RectOffset(1, 1, 1, 1);
			BorderStyle.margin = new RectOffset();

			const string refreshBranchesHint = "Refresh branches cache database.\n\nNOTE: Single scan may take up to a few minutes, depending on your network connection and the complexity of your repository.";
			RefreshBranchesContent = new GUIContent(EditorGUIUtility.FindTexture("Refresh"), refreshBranchesHint);

			ToolbarLabelStyle = new GUIStyle(EditorStyles.toolbarButton);
			ToolbarLabelStyle.normal.background = null;
			ToolbarLabelStyle.normal.scaledBackgrounds = null;
			ToolbarLabelStyle.alignment = TextAnchor.MiddleLeft;
			var padding = ToolbarLabelStyle.padding;
			padding.bottom = 2;
			ToolbarLabelStyle.padding = padding;

			ToolbarTitleStyle = new GUIStyle(ToolbarLabelStyle);
			ToolbarTitleStyle.font = EditorStyles.boldFont;

			SearchFieldStyle = GUI.skin.GetStyle("ToolbarSeachTextField");
			SearchFieldCancelStyle = GUI.skin.GetStyle("ToolbarSeachCancelButton");
			SearchFieldCancelEmptyStyle = GUI.skin.GetStyle("ToolbarSeachCancelButtonEmpty");

			const string showLogTooltip = "Show Log in this branch at the target asset.";
			RepoBrowserContent = Preferences.SVNPreferencesManager.LoadTexture("Editor/BranchesIcons/SVN-RepoBrowser", "Repo-Browser in this branch at the target asset.");
			ShowLogContent = Preferences.SVNPreferencesManager.LoadTexture("Editor/BranchesIcons/SVN-ShowLog", showLogTooltip);
			SwitchBranchContent = Preferences.SVNPreferencesManager.LoadTexture("Editor/BranchesIcons/SVN-Switch", "Switch working copy to another branch.\nOpens TortoiseSVN dialog.");

			ScanForConflictsContent = Preferences.SVNPreferencesManager.LoadTexture("Editor/BranchesIcons/SVN-ScanForConflicts", "Scan all branches for potential conflicts.\nThis will look for any changes made to the target asset in the branches.");

			ConflictsPendingContent = Preferences.SVNPreferencesManager.LoadTexture("Editor/BranchesIcons/SVN-ConflictsScan-Pending", "Pending - waiting to be scanned for conflicts.\n\n" + showLogTooltip);
			ConflictsFoundContent = Preferences.SVNPreferencesManager.LoadTexture("Editor/BranchesIcons/SVN-Conflicts-Found", "The target asset was modified in this branch - potential conflicts.\n\n" + showLogTooltip);
			ConflictsNormalContent = Preferences.SVNPreferencesManager.LoadTexture("Editor/BranchesIcons/SVN-ConflictsScan-Normal", "No conflicts were found by the scan.\n\n" + showLogTooltip);
			ConflictsAddedContent = Preferences.SVNPreferencesManager.LoadTexture("Editor/BranchesIcons/SVN-ConflictsScan-Added", "Asset was added in this branch.\n\n" + showLogTooltip);
			ConflictsMissingContent = Preferences.SVNPreferencesManager.LoadTexture("Editor/BranchesIcons/SVN-ConflictsScan-Missing", "Asset was missing. It may have been deleted or never existed in this branch.\n\n" + showLogTooltip);
			ConflictsErrorContent = new GUIContent(EditorGUIUtility.FindTexture("console.erroricon.sml"), "Error while scanning this branch. Check the console logs for more info.\n\n" + showLogTooltip);

			if (RepoBrowserContent.image == null) RepoBrowserContent.text = "R";
			if (ShowLogContent.image == null) ShowLogContent.text = "L";
			if (SwitchBranchContent.image == null) SwitchBranchContent.text = "S";

			if (ScanForConflictsContent.image == null) ScanForConflictsContent.text = "C";

			if (ConflictsPendingContent.image == null) ConflictsPendingContent.text = "P";
			if (ConflictsFoundContent.image == null) ConflictsFoundContent.text = "C";
			if (ConflictsNormalContent.image == null) ConflictsNormalContent.text = "N";
			if (ConflictsAddedContent.image == null) ConflictsAddedContent.text = "A";
			if (ConflictsMissingContent.image == null) ConflictsMissingContent.text = "M";
			if (ConflictsErrorContent.image == null) ConflictsErrorContent.text = "E";

			MiniIconButtonlessStyle = new GUIStyle(GUI.skin.button);
			MiniIconButtonlessStyle.hover.background = MiniIconButtonlessStyle.normal.background;
			MiniIconButtonlessStyle.hover.scaledBackgrounds = MiniIconButtonlessStyle.normal.scaledBackgrounds;
			MiniIconButtonlessStyle.hover.textColor = GUI.skin.label.hover.textColor;
			MiniIconButtonlessStyle.normal.background = null;
			MiniIconButtonlessStyle.normal.scaledBackgrounds = null;
			MiniIconButtonlessStyle.padding = new RectOffset();
			MiniIconButtonlessStyle.margin = new RectOffset();

			// Do it before BranchLabelStyle copies the style.
			MigrateToUIElementsIfNeeded();

			BranchLabelStyle = new GUIStyle(MiniIconButtonlessStyle);
			BranchLabelStyle.alignment = TextAnchor.MiddleLeft;
			BranchLabelStyle.margin = new RectOffset(2, 4, 2, 0);
			BranchLabelStyle.padding = new RectOffset(4, 4, 3, 3);


			RevisionsHintContent = new GUIContent(EditorGUIUtility.FindTexture("console.infoicon.sml"), "Scan number of revisions back from the last changed one in the checked branch.");

		}

		#region UIElements Background HACKS!

		private void MigrateToUIElementsIfNeeded()
		{
			// As 2019 & 2020 incorporates the UIElements framework, background textures are now null / empty.
			// Because this was written in the old IMGUI style using 2018, this quick and dirty hack was created.
			// Manually create background textures imitating the real buttons ones.

			MiniIconButtonlessStyle.name = "";	// UIElements matches button styles by name and overrides everything.

			if (MiniIconButtonlessStyle.hover.background == null) {
				var hoverColor = EditorGUIUtility.isProSkin ? new Color(0.404f, 0.404f, 0.404f, 1.0f) : new Color(0.925f, 0.925f, 0.925f, 1.0f);
				MiniIconButtonlessStyle.hover.background = MakeButtonBackgroundTexture(hoverColor);
			}
			if (MiniIconButtonlessStyle.active.background == null) {
				var activeColor = EditorGUIUtility.isProSkin ? new Color(0.455f, 0.455f, 0.455f, 1.0f) : new Color(0.694f, 0.694f, 0.694f, 1.0f);
				MiniIconButtonlessStyle.active.background = MakeButtonBackgroundTexture(activeColor);
			}


			BorderStyle.name = "";
			if (BorderStyle.normal.background == null) {
				var normalColor = EditorGUIUtility.isProSkin ? new Color(0.290f, 0.290f, 0.290f, 1.0f) : new Color(0.740f, 0.740f, 0.740f, 1.0f);
				BorderStyle.normal.background = MakeBoxBackgroundTexture(normalColor);
			}
		}

		private static Texture2D MakeButtonBackgroundTexture(Color color)
		{
			const int width = 16;
			const int height = 16;

			var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);

			var pixels = new Color[width * height];
			for (int y = 0; y < height; ++y) {
				for (int x = 0; x < width; ++x) {
					var index = x + y * width;
					pixels[index] = color;

					if (y == 0) {
						pixels[index] *= 0.7f;
						pixels[index].a = 1f;
					}

					if (y == height - 1) {
						pixels[index] *= 1.02f;
						pixels[index].a = 1f;
					}

					if (x == 0 || x == width - 1) {
						pixels[index] *= 0.95f;
						pixels[index].a = 1f;
					}
				}
			}

			texture.SetPixels(pixels);

			texture.SetPixel(0, 0, new Color());
			texture.SetPixel(1, 0, new Color());
			texture.SetPixel(0, 1, new Color());


			texture.SetPixel(width - 1, 0, new Color());
			texture.SetPixel(width - 2, 0, new Color());
			texture.SetPixel(width - 1, 1, new Color());

			texture.SetPixel(0, height - 1, new Color());
			texture.SetPixel(0, height - 2, new Color());
			texture.SetPixel(1, height - 1, new Color());

			texture.SetPixel(width - 1, height - 1, new Color());
			texture.SetPixel(width - 2, height - 1, new Color());
			texture.SetPixel(width - 1, height - 2, new Color());

			texture.Apply();

			return texture;
		}

		private static Texture2D MakeBoxBackgroundTexture(Color color)
		{
			const int width = 16;
			const int height = 16;

			var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);

			var pixels = new Color[width * height];
			for (int y = 0; y < height; ++y) {
				for (int x = 0; x < width; ++x) {
					var index = x + y * width;
					pixels[index] = color;

					if (y == 0 || y == height - 1 || x == 0 || x == width - 1) {
						pixels[index] *= 0.5f;
						pixels[index].a = 1f;
					}
				}
			}

			texture.SetPixels(pixels);
			texture.Apply();

			return texture;
		}
		#endregion

		// This is initialized on first OnGUI rather upon creation because it gets overridden.
		private void InitializePositionAndSize()
		{
			Vector2 size = new Vector2(550f, 400);
			minSize = size;

			var center = new Vector2(Screen.currentResolution.width, Screen.currentResolution.height) / 2f;
			Rect popupRect = new Rect(center - position.size / 2, position.size);

			position = popupRect;
		}

		void OnEnable()
		{
			wantsMouseMove = true;  // Needed for the hover effects.

			Database.DatabaseChanged -= OnDatabaseChanged;
			Database.DatabaseChanged += OnDatabaseChanged;
			AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
		}

		private void OnDisable()
		{
			Database.DatabaseChanged -= OnDatabaseChanged;
			AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;

			if (m_ConflictsScanState != ConflictsScanState.Scanned) {
				InvaldateConflictsScan();
			}
		}

		void OnGUI()
		{
			if (!m_Initialized) {
				InitializeStyles();
				InitializePositionAndSize();

				m_Initialized = true;
			}

			EditorGUILayout.BeginVertical(BorderStyle);

			DrawContent();

			GUILayout.FlexibleSpace();

			using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar, GUILayout.ExpandWidth(true))) {
				DrawStatusBar();
			}

			EditorGUILayout.EndVertical();

			if (m_ConflictsScanState == ConflictsScanState.Scanning && Event.current.type == EventType.Repaint) {

				if (!m_ConflictsScanThread.IsAlive) {
					m_ConflictsScanState = ConflictsScanState.Scanned;
				}

				Repaint();
			}
		}

		private void DrawContent()
		{
			using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar)) {

				GUILayout.Label("Asset:", ToolbarTitleStyle, GUILayout.Width(ToolbarsTitleWidth));

				var prevColor = GUI.backgroundColor;
				GUI.backgroundColor = m_TargetAsset == null ? new Color(0.93f, 0.40f, 0.40f) : prevColor;

				var targetAsset = EditorGUILayout.ObjectField(m_TargetAsset, typeof(Object), false, GUILayout.ExpandWidth(true));
				if (targetAsset != m_TargetAsset) {
					InvaldateConflictsScan();
					m_TargetAsset = targetAsset;
				}

				GUI.backgroundColor = prevColor;

				GUILayout.Space(24f);

				m_ShowConflictsMenu = GUILayout.Toggle(m_ShowConflictsMenu, ScanForConflictsContent, EditorStyles.toolbarButton, GUILayout.ExpandWidth(false));
			}

			if (m_ShowConflictsMenu) {
				DrawConflictsMenu();
			}

			using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar)) {

				GUILayout.Label("Search:", ToolbarTitleStyle, GUILayout.Width(ToolbarsTitleWidth));

				m_BranchFilter = EditorGUILayout.TextField(m_BranchFilter, SearchFieldStyle);

				if (GUILayout.Button(" ", string.IsNullOrEmpty(m_BranchFilter) ? SearchFieldCancelEmptyStyle : SearchFieldCancelStyle)) {
					m_BranchFilter = "";
					GUI.FocusControl("");
					Repaint();
				}

				if (GUILayout.Button(RefreshBranchesContent, EditorStyles.toolbarButton, GUILayout.ExpandWidth(false))) {
					Database.InvalidateDatabase();
					InvaldateConflictsScan();
				}
			}


			using (new EditorGUILayout.VerticalScope()) {
				if (!Database.IsActive) {
					EditorGUILayout.LabelField("To use Branch Selector, you must enable and setup\n\"Branches Database\" in the Project preferences...", GUILayout.Height(40f));
					if (GUILayout.Button("Open Project Preferences")) {
						Preferences.SVNPreferencesWindow.ShowProjectPreferences(Preferences.SVNPreferencesWindow.PreferencesTab.Project);
					}

				} else if (!Database.IsReady) {
					EditorGUILayout.LabelField("Scanning branches for Unity projects...");
				} else if (m_TargetAsset == null) {
					EditorGUILayout.LabelField("Please select target asset....");
				} else {
					DrawBranchesList();
				}
			}
		}

		private void DrawConflictsMenu()
		{
			using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar)) {

				using (new EditorGUI.DisabledGroupScope(m_ConflictsScanState == ConflictsScanState.Scanning)) {
					GUILayout.Label("Conflicts:", ToolbarTitleStyle, GUILayout.Width(ToolbarsTitleWidth));

					GUILayout.Label("Show Non-Conflicts:");
					m_ConflictsShowNormal = EditorGUILayout.Toggle(m_ConflictsShowNormal, GUILayout.Width(28f));

					var prevWidth = EditorGUIUtility.labelWidth;
					EditorGUIUtility.labelWidth = 55f;
					m_ConflictsScanLimitType = (ConflictsScanLimitType)EditorGUILayout.EnumPopup("Limit by: ", m_ConflictsScanLimitType, GUILayout.Width(140f));
					EditorGUIUtility.labelWidth = prevWidth;

					if (m_ConflictsScanLimitType != ConflictsScanLimitType.Unlimited) {
						m_ConflictsScanLimitParam = Mathf.Max(1, EditorGUILayout.IntField(m_ConflictsScanLimitParam, GUILayout.Width(50f)));
					}

					if (m_ConflictsScanLimitType == ConflictsScanLimitType.Revisions) {
						GUILayout.Label(RevisionsHintContent);
					}
				}

				GUILayout.FlexibleSpace();

				var showStartScan = m_ConflictsScanState == ConflictsScanState.None || m_ConflictsScanState == ConflictsScanState.Scanned;
				var scanButtonContent = showStartScan ? "Start Scan" : "Stop Scan";
				var prevColor = GUI.backgroundColor;
				GUI.backgroundColor = showStartScan ? Color.green : Color.red;
				if (GUILayout.Button( scanButtonContent, EditorStyles.toolbarButton, GUILayout.ExpandWidth(false))) {
					if (showStartScan) {
						StartConflictsScan();
					} else {
						InvaldateConflictsScan();
					}
				}

				GUI.backgroundColor = prevColor;
			}
		}

		private void DrawBranchesList()
		{
			using (var scrollView = new EditorGUILayout.ScrollViewScope(m_BranchesScroll)) {
				m_BranchesScroll = scrollView.scrollPosition;

				// For hover effects to work.
				if (Event.current.type == EventType.MouseMove) {
					Repaint();
				}

				// TODO: Sort list by folder depths: compare by lastIndexOf('/'). If equal, by string.

				foreach (var branchProject in Database.BranchProjects) {
					if (!string.IsNullOrEmpty(m_BranchFilter) && branchProject.BranchName.IndexOf(m_BranchFilter, System.StringComparison.OrdinalIgnoreCase) == -1)
						continue;

					// Apply setting only in Scanned mode since the thread will still be working otherwise and
					// cause inconsistent GUILayout structure.
					if (m_ConflictsScanState == ConflictsScanState.Scanned && !m_ConflictsShowNormal) {
						var conflictResult = m_ConflictsScanResults.First(r => r.UnityURL == branchProject.UnityProjectURL);
						if (conflictResult.State == ConflictState.Normal)
							continue;
					}

					using (new EditorGUILayout.HorizontalScope(/*BranchRowStyle*/)) {

						float buttonSize = 24f;
						bool repoBrowser = GUILayout.Button(RepoBrowserContent, MiniIconButtonlessStyle, GUILayout.Height(buttonSize), GUILayout.Width(buttonSize));
						bool showLog = GUILayout.Button(SelectShowLogContent(branchProject), MiniIconButtonlessStyle, GUILayout.Height(buttonSize), GUILayout.Width(buttonSize));
						bool switchBranch = GUILayout.Button(SwitchBranchContent, MiniIconButtonlessStyle, GUILayout.Height(buttonSize), GUILayout.Width(buttonSize));

						bool branchSelected = GUILayout.Button(new GUIContent(branchProject.BranchRelativePath, branchProject.BranchURL), BranchLabelStyle);

						if (repoBrowser) {
							SVNContextMenusManager.RepoBrowser(branchProject.UnityProjectURL + "/" + AssetDatabase.GetAssetPath(m_TargetAsset));
						}

						if (showLog) {
							SVNContextMenusManager.ShowLog(branchProject.UnityProjectURL + "/" + AssetDatabase.GetAssetPath(m_TargetAsset));
						}

						if (switchBranch) {
							bool confirm = EditorUtility.DisplayDialog("Switch Operation",
								"Unity needs to be closed while switching. Do you want to close it?\n\n" +
								"Reason: if Unity starts crunching assets while SVN is downloading files, the Library may get corrupted.",
								"Yes!", "No"
								);
							if (confirm && UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) {
								var localPathNative = WiseSVNIntegration.WorkingCopyRootPath();
								var targetUrl = branchProject.BranchURL;

								if (branchProject.BranchURL != branchProject.UnityProjectURL) {
									bool useBranchRoot = EditorUtility.DisplayDialog("Switch what?",
										"What do you want to switch?\n" +
										"- Working copy root (the whole checkout)\n" +
										"- Unity project folder",
										"Working copy root", "Unity project");
									if (!useBranchRoot) {
										localPathNative = WiseSVNIntegration.ProjectRootNative;
										targetUrl = branchProject.UnityProjectURL;
									}
								}

								SVNContextMenusManager.Switch(localPathNative, targetUrl);
								EditorApplication.Exit(0);
							}
						}

						if (branchSelected) {
							var menu = new GenericMenu();

							var prevValue = BranchContextMenu.CopyBranchName;
							foreach (var value in System.Enum.GetValues(typeof(BranchContextMenu)).OfType<BranchContextMenu>()) {
								if ((int)value / 10 != (int)prevValue / 10) {
									menu.AddSeparator("");
								}

								menu.AddItem(new GUIContent(ObjectNames.NicifyVariableName(value.ToString())), false, OnSelectBranchOption, new KeyValuePair<BranchContextMenu, BranchProject>(value, branchProject));
								prevValue = value;
							}

							menu.ShowAsContext();
						}
					}

					var rect = EditorGUILayout.GetControlRect(GUILayout.ExpandWidth(true), GUILayout.Height(1f));
					EditorGUI.DrawRect(rect, Color.black);
				}

			}
		}

		private GUIContent SelectShowLogContent(BranchProject branchProject)
		{
			if (m_ConflictsScanState != ConflictsScanState.Scanning && m_ConflictsScanState != ConflictsScanState.Scanned)
				return ShowLogContent;

			var conflictResult = m_ConflictsScanResults.First(r => r.UnityURL == branchProject.UnityProjectURL);
			switch(conflictResult.State) {

				case ConflictState.Pending:
					return ConflictsPendingContent;

				case ConflictState.Normal:
					return ConflictsNormalContent;

				case ConflictState.Conflicted:
					return ConflictsFoundContent;

				case ConflictState.Added:
					return ConflictsAddedContent;

				case ConflictState.Missing:
					return ConflictsMissingContent;

				case ConflictState.Error:
					return ConflictsErrorContent;

				default:
					throw new System.NotSupportedException($"Conflict for {branchProject.UnityProjectURL} - {conflictResult.State}");
			}
		}

		private void OnSelectBranchOption(object data)
		{
			var pair = (KeyValuePair<BranchContextMenu, BranchProject>)data;
			var branchProject = pair.Value;

			if (pair.Key == BranchContextMenu.Cancel)
				return;


			string copyText = null;
			switch(pair.Key) {

				case BranchContextMenu.CopyBranchName:
					copyText = branchProject.BranchName;
					break;

				case BranchContextMenu.CopyBranchURL:
					copyText = branchProject.BranchURL;
					break;

				case BranchContextMenu.CopyBranchRelativeURL:
					copyText = branchProject.BranchRelativePath;
					break;

				case BranchContextMenu.CopyTargetAssetBranchURL:
					copyText = branchProject.UnityProjectURL + "/" + AssetDatabase.GetAssetPath(m_TargetAsset);
					break;

				case BranchContextMenu.CopyTargetAssetBranchRelativeURL:
					copyText = branchProject.UnityProjectRelativePath + "/" + AssetDatabase.GetAssetPath(m_TargetAsset);
					break;

				default:
					throw new System.NotSupportedException(pair.Key.ToString());
			}

			if (!string.IsNullOrEmpty(copyText)) {
				var textEditor = new TextEditor();
				textEditor.text = copyText;
				textEditor.SelectAll();
				textEditor.Copy();
			}
		}


			private void DrawStatusBar()
		{
			if (Database.LastError != ListOperationResult.Success) {
				GUILayout.Label($"Error scanning branches: {ObjectNames.NicifyVariableName(Database.LastError.ToString())}", ToolbarLabelStyle, GUILayout.ExpandWidth(false));
				return;
			}

			if (Database.IsUpdating) {
				int dots = ((int)EditorApplication.timeSinceStartup) % 3;
				GUILayout.Label($"Scanning{LoadingDots[dots]}", ToolbarLabelStyle, GUILayout.ExpandWidth(false));

				GUILayout.FlexibleSpace();

				GUILayout.Label(Database.LastProcessedEntry, ToolbarLabelStyle, GUILayout.ExpandWidth(false));
				Repaint();
				return;
			}


			GUILayout.Label($"Branches: {Database.BranchProjects.Count}", ToolbarLabelStyle, GUILayout.ExpandWidth(false));

			if (m_ConflictsScanState != ConflictsScanState.None) {
				GUILayout.FlexibleSpace();

				GUILayout.Label($"Conflicts: {ObjectNames.NicifyVariableName(m_ConflictsScanState.ToString())}", ToolbarLabelStyle, GUILayout.ExpandWidth(false));
			}
		}

		private void OnDatabaseChanged()
		{
			if (m_ConflictsScanState != ConflictsScanState.None) {
				StartConflictsScan();
			}

			Repaint();
		}

		private void StartConflictsScan()
		{
			if (Database.IsUpdating || !Database.IsReady) {
				m_ConflictsScanState = ConflictsScanState.WaitingForBranchesDatabase;
				return;
			}

			if (Database.LastError != ListOperationResult.Success) {
				m_ConflictsScanState = ConflictsScanState.Error;
				return;
			}

			if (m_TargetAsset == null || Database.BranchProjects.Count == 0) {
				m_ConflictsScanState = ConflictsScanState.Error;
				return;
			}

			if (m_ConflictsScanState == ConflictsScanState.Scanning)
				return;

			if (m_ConflictsScanThread != null && m_ConflictsScanThread.IsAlive) {
				throw new System.Exception("Starting conflicts scan while another one is pending?");
			}

			m_ConflictsScanState = ConflictsScanState.Scanning;

			m_ConflictsScanResults = Database.BranchProjects
				.Select(bp => new ConflictsScanResult() { UnityURL = bp.UnityProjectURL, State = ConflictState.Pending })
				.ToArray();

			m_ConflictsScanThread = new System.Threading.Thread(GatherConflicts);

			// Thread will update the array by ref.
			m_ConflictsScanThread.Start(new ConflictsScanJobData() {

				TargetAssetPath = AssetDatabase.GetAssetPath(m_TargetAsset),
				LimitType = m_ConflictsScanLimitType,
				LimitParam = m_ConflictsScanLimitParam,
				Reults = m_ConflictsScanResults,

			});
		}

		private void InvaldateConflictsScan()
		{
			m_ConflictsScanState = ConflictsScanState.None;

			m_ConflictsScanResults = new ConflictsScanResult[0];

			if (m_ConflictsScanThread != null && m_ConflictsScanThread.IsAlive) {
				m_ConflictsScanThread.Abort();
				m_ConflictsScanThread = null;
			}
		}

		private void OnBeforeAssemblyReload()
		{
			// Do it before Unity does it. Cause Unity aborts the thread badly sometimes :(
			if (m_ConflictsScanState != ConflictsScanState.Scanned) {
				InvaldateConflictsScan();
			}
		}

		private static void GatherConflicts(object param)
		{
			var jobData = (ConflictsScanJobData)param;
			var results = jobData.Reults;

			var logParams = new LogParams() {
				FetchAffectedPaths = true,
				FetchCommitMessages = false,
				StopOnCopy = true,
				Limit = 10,
			};

			const string svnDateFormat = "yyyy-MM-dd";
			switch (jobData.LimitType) {
				case ConflictsScanLimitType.Days:
					logParams.RangeStart = "{" + System.DateTime.Now.AddDays(-1 * jobData.LimitParam).ToString(svnDateFormat) + "}";
					logParams.RangeEnd = "HEAD";
					break;

				case ConflictsScanLimitType.Weeks:
					logParams.RangeStart = "{" + System.DateTime.Now.AddDays(-1 * 7 * jobData.LimitParam).ToString(svnDateFormat) + "}";
					logParams.RangeEnd = "HEAD";
					break;

				case ConflictsScanLimitType.Months:
					logParams.RangeStart = "{" + System.DateTime.Now.AddMonths(-1 * jobData.LimitParam).ToString(svnDateFormat) + "}";
					logParams.RangeEnd = "HEAD";
					break;

				case ConflictsScanLimitType.Revisions:
					// Revisions are calculated per branch. Do nothing here.
					break;

				case ConflictsScanLimitType.Unlimited:
					logParams.RangeStart = "";
					break;

				default:
					Debug.LogError($"Unsupported ConflictsScanLimitType {jobData.LimitType} with param {jobData.LimitParam}");
					break;
			}

			List<LogEntry> logEntries = new List<LogEntry>();

			for (int i = 0; i < results.Length; ++i) {
				var result = results[i];

				logEntries.Clear();

				var targetURL = result.UnityURL + "/" + jobData.TargetAssetPath;
				var targetRelativeURL = WiseSVNIntegration.AssetPathToRelativeURL(targetURL);

				// Either it doesn't exist in this branch or it was moved / deleted. Can't know for sure without some deep digging.
				if (string.IsNullOrEmpty(targetRelativeURL)) {
					result.State = ConflictState.Missing;
					results[i] = result;
					continue;
				}

				if (jobData.LimitType == ConflictsScanLimitType.Revisions) {
					var lastChangedRevision = WiseSVNIntegration.LastChangedRevision(targetURL);
					if (lastChangedRevision < 0) {
						// Probably doesn't exist in this branch.
						logParams.RangeStart = "";
						logParams.RangeEnd = "";
					} else {
						logParams.RangeStart = (lastChangedRevision - jobData.LimitParam).ToString();
						logParams.RangeEnd = lastChangedRevision.ToString();
					}
				}

				var opResult = WiseSVNIntegration.Log(targetURL, logParams, logEntries, 60000 * 5);

				// Either it doesn't exist in this branch or it was moved / deleted. Can't know for sure without some deep digging.
				if (opResult == LogOperationResult.NotFound) {
					result.State = ConflictState.Missing;
					results[i] = result;
					continue;
				}

				if (opResult != LogOperationResult.Success) {
					result.State = ConflictState.Error;
					results[i] = result;
					continue;
				}

				result.State = ConflictState.Normal;

				foreach(var logEntry in logEntries) {
					var logPath = logEntry.AffectedPaths.FirstOrDefault(ap => ap.Path.StartsWith(targetRelativeURL));

					// If not found in the affected paths -> this is the log entry of the branch copy.
					if (string.IsNullOrEmpty(logPath.Path))
						continue;

					result.State = ConflictState.Conflicted;

					// Don't consider folder children for "Added" and "Deleted". Folders are just modified by their children.
					if (logPath.Path != targetRelativeURL)
						continue;

					if (logPath.Added || logPath.Replaced) {
						result.State = ConflictState.Added;
						break;
					}

					if (logPath.Deleted) {
						result.State = ConflictState.Missing;
						break;
					}
				}


				results[i] = result;
			}
		}
	}
}
