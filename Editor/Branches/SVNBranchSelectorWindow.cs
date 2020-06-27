using DevLocker.VersionControl.WiseSVN.ContextMenus;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DevLocker.VersionControl.WiseSVN
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

		private const string WindowSizePrefsKey = "SVNBranchSelectorWindow-Size";

		private SVNBranchesDatabase Database => SVNBranchesDatabase.Instance;

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
			Missing,
		}

		[System.Serializable]
		private struct ConflictsScanResult
		{
			public string UnityURL;
			public ConflictState State;
		}

		private ConflictsScanState m_ConflictsScanState = ConflictsScanState.None;

		private ConflictsScanResult[] m_ConflictsScanResults = new ConflictsScanResult[0];
		private System.Threading.Thread m_ConflictsScanThread;

		#endregion


		private GUIStyle BorderStyle;

		private GUIContent RefreshBranchesContent;


		private GUIStyle WindowTitleStyle;
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
		private GUIContent ConflictsMissingContent;

		private GUIStyle MiniIconButtonlessStyle;

		private GUIStyle BranchLabelStyle;

		private readonly string[] LoadingDots = new[] { ".  ", ".. ", "..." };

		[MenuItem("Assets/SVN/Branch Selector", false, -490)]
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

			WindowTitleStyle = new GUIStyle(EditorStyles.toolbarButton);
			WindowTitleStyle.font = EditorStyles.boldFont;
			WindowTitleStyle.normal.background = null;


			ToolbarLabelStyle = new GUIStyle(EditorStyles.toolbarButton);
			ToolbarLabelStyle.normal.background = null;
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
			RepoBrowserContent = new GUIContent(Resources.Load<Texture2D>("Editor/BranchesIcons/SVN-RepoBrowser"), "Repo-Browser in this branch at the target asset.");
			ShowLogContent = new GUIContent(Resources.Load<Texture2D>("Editor/BranchesIcons/SVN-ShowLog"), showLogTooltip);
			SwitchBranchContent = new GUIContent(Resources.Load<Texture2D>("Editor/BranchesIcons/SVN-Switch"), "Switch working copy to another branch.\nOpens TortoiseSVN dialog.");
			ScanForConflictsContent = new GUIContent(Resources.Load<Texture2D>("Editor/BranchesIcons/SVN-ScanForConflicts"), "Scan all branches for potential conflicts.\nThis will look for any changes made to the target asset in the branches.");

			ConflictsPendingContent = new GUIContent(Resources.Load<Texture2D>("Editor/BranchesIcons/SVN-ConflictsScan-Pending"), "Pending - waiting to be scanned for conflicts.\n\n" + showLogTooltip);
			ConflictsFoundContent = new GUIContent(Resources.Load<Texture2D>("Editor/BranchesIcons/SVN-Conflicts-Found"), "Conflicts found by the scan.\n\n" + showLogTooltip);
			ConflictsNormalContent = new GUIContent(Resources.Load<Texture2D>("Editor/BranchesIcons/SVN-ConflictsScan-Normal"), "No conflicts were found by the scan.\n\n" + showLogTooltip);
			ConflictsMissingContent = new GUIContent(Resources.Load<Texture2D>("Editor/BranchesIcons/SVN-ConflictsScan-Missing"), "Asset was missing. It may have been deleted or never existed in this branch.\n\n" + showLogTooltip);

			if (RepoBrowserContent.image == null) RepoBrowserContent.text = "R";
			if (ShowLogContent.image == null) ShowLogContent.text = "L";
			if (SwitchBranchContent.image == null) SwitchBranchContent.text = "S";
			if (ScanForConflictsContent.image == null) ScanForConflictsContent.text = "C";

			if (ConflictsPendingContent.image == null) ScanForConflictsContent.text = "P";
			if (ConflictsFoundContent.image == null) ScanForConflictsContent.text = "C";
			if (ConflictsNormalContent.image == null) ScanForConflictsContent.text = "N";
			if (ConflictsMissingContent.image == null) ScanForConflictsContent.text = "M";

			MiniIconButtonlessStyle = new GUIStyle(GUI.skin.button);
			MiniIconButtonlessStyle.hover.background = MiniIconButtonlessStyle.normal.background;
			MiniIconButtonlessStyle.normal.background = null;
			MiniIconButtonlessStyle.padding = new RectOffset();
			MiniIconButtonlessStyle.margin = new RectOffset();

			BranchLabelStyle = new GUIStyle(GUI.skin.label);
			BranchLabelStyle.alignment = TextAnchor.MiddleLeft;
			var margin = BranchLabelStyle.margin;
			margin.top += 2;
			BranchLabelStyle.margin = margin;
		}

		// This is initialized on first OnGUI rather upon creation because it gets overriden.
		private void InitializePositionAndSize()
		{
			Vector2 size = new Vector2(350f, 300);
			minSize = size;

			var sizeData = EditorPrefs.GetString(WindowSizePrefsKey);
			if (!string.IsNullOrEmpty(sizeData)) {
				var sizeArr = sizeData.Split(';');
				size.x = float.Parse(sizeArr[0]);
				size.y = float.Parse(sizeArr[1]);
			}

			// TODO: How will this behave with two monitors?
			var center = new Vector2(Screen.currentResolution.width, Screen.currentResolution.height) / 2f;
			Rect popupRect = new Rect(center - size / 2, size);

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

			EditorPrefs.SetString(WindowSizePrefsKey, $"{position.width};{position.height}");

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

				GUILayout.Label("Asset:", ToolbarTitleStyle, GUILayout.Width(60f));

				var prevColor = GUI.backgroundColor;
				GUI.backgroundColor = m_TargetAsset == null ? new Color(0.93f, 0.40f, 0.40f) : prevColor;

				var targetAsset = EditorGUILayout.ObjectField(m_TargetAsset, m_TargetAsset ? m_TargetAsset.GetType() : typeof(Object), false, GUILayout.ExpandWidth(true));
				if (targetAsset != m_TargetAsset) {
					InvaldateConflictsScan();
					m_TargetAsset = targetAsset;
				}

				GUI.backgroundColor = prevColor;

				GUILayout.Space(24f);

				if (GUILayout.Button(ScanForConflictsContent, EditorStyles.toolbarButton, GUILayout.ExpandWidth(false))) {
					StartConflictsScan();
				}
			}

			using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar)) {

				GUILayout.Label("Search:", ToolbarTitleStyle, GUILayout.Width(60f));

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

				if (!Database.IsReady) {
					EditorGUILayout.LabelField("Scanning branches for Unity projects...", GUILayout.ExpandHeight(true));
				} else if (m_TargetAsset == null) {
					EditorGUILayout.LabelField("Please select target asset....", GUILayout.ExpandHeight(true));
				} else {
					DrawBranchesList();
				}
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

					using (new EditorGUILayout.HorizontalScope(/*BranchRowStyle*/)) {

						float buttonSize = 24f;
						bool repoBrowser = GUILayout.Button(RepoBrowserContent, MiniIconButtonlessStyle, GUILayout.Height(buttonSize), GUILayout.Width(buttonSize));
						bool showLog = GUILayout.Button(SelectShowLogContent(branchProject), MiniIconButtonlessStyle, GUILayout.Height(buttonSize), GUILayout.Width(buttonSize));
						bool switchBranch = GUILayout.Button(SwitchBranchContent, MiniIconButtonlessStyle, GUILayout.Height(buttonSize), GUILayout.Width(buttonSize));

						GUILayout.Label(new GUIContent(branchProject.BranchRelativePath, branchProject.BranchURL), BranchLabelStyle);

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
								var localPath = WiseSVNIntegration.WorkingCopyRootPath();
								var targetUrl = branchProject.BranchURL;

								if (branchProject.BranchURL != branchProject.UnityProjectURL) {
									bool useBranchRoot = EditorUtility.DisplayDialog("Switch what?",
										"What do you want to switch?\n" +
										"- Working copy root (the whole checkout)\n" +
										"- Unity project folder",
										"Working copy root", "Unity project");
									if (!useBranchRoot) {
										localPath = WiseSVNIntegration.ProjectRoot;
										targetUrl = branchProject.UnityProjectURL;
									}
								}

								SVNContextMenusManager.Switch(localPath, targetUrl);
								EditorApplication.Exit(0);
							}
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

				case ConflictState.Missing:
					return ConflictsMissingContent;

				default:
					throw new System.NotSupportedException($"Conflict for {branchProject.UnityProjectURL} - {conflictResult.State}");
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

			// Thread will update the array by ref.
			var jobData = new KeyValuePair<string, ConflictsScanResult[]>(AssetDatabase.GetAssetPath(m_TargetAsset), m_ConflictsScanResults);

			m_ConflictsScanThread = new System.Threading.Thread(GatherConflicts);
			m_ConflictsScanThread.Start(jobData);
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
			var jobData = (KeyValuePair<string, ConflictsScanResult[]>)param;
			var targetAssetPath = jobData.Key;
			var results = jobData.Value;

			for(int i = 0; i < results.Length; ++i) {
				var result = results[i];

				System.Threading.Thread.Sleep(5000);
				result.State = i == 1 ? ConflictState.Normal : ConflictState.Conflicted;
				if (i == 2) result.State = ConflictState.Missing;
				results[i] = result;
			}
		}
	}
}
