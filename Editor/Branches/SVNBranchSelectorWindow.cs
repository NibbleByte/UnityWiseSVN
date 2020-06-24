using DevLocker.VersionControl.WiseSVN.ContextMenus;
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
		[MenuItem("Assets/SVN/Branch Selector", false, -490)]
		private static void OpenBranchesSelector()
		{
			//var centerPos = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);
			var centerPos = new Vector2(Screen.currentResolution.width, Screen.currentResolution.height) / 2f;
			OpenBranchesSelector(Selection.assetGUIDs.Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(), centerPos);
		}

		public static void OpenBranchesSelector(string assetPath, Vector2 center)
		{
			var window = CreateInstance<SVNBranchSelectorWindow>();

			window.m_TargetAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);

			Vector2 size = new Vector2(350f, 200);
			Rect popupRect = new Rect(center - size / 2, size);

			window.minSize = size;
			window.position = popupRect;
			window.ShowPopup();
		}

		private bool m_Initialized = false;

		private Object m_TargetAsset;
		private string m_BranchFilter;
		private Vector2 m_BranchesScroll;

		private GUIStyle BorderStyle;

		private GUIContent RefreshBranchesContent;


		private GUIStyle WindowTitleStyle;
		private GUIStyle ToolbarTitleStyle;
		private GUIStyle ToolbarLabelStyle;

		private GUIStyle SearchFieldStyle;
		private GUIStyle SearchFieldCancelStyle;
		private GUIStyle SearchFieldCancelEmptyStyle;

		private GUIContent RepoBrowserContent;
		private GUIContent ShowLogContent;
		private GUIContent SwitchBranchContent;

		private GUIStyle MiniIconButtonlessStyle;

		private GUIStyle BranchLabelStyle;

		private readonly string[] LoadingDots = new[] { ".  ", ".. ", "..." };

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

			RepoBrowserContent = new GUIContent(Resources.Load<Texture2D>("Editor/BranchesIcons/SVN-RepoBrowser"), "Repo-Browser in this branch at the target asset.");
			ShowLogContent = new GUIContent(Resources.Load<Texture2D>("Editor/BranchesIcons/SVN-ShowLog"), "Show Log in this branch at the target asset.");
			SwitchBranchContent = new GUIContent(Resources.Load<Texture2D>("Editor/BranchesIcons/SVN-Switch"), "Switch working copy to another branch.\nOpens TortoiseSVN dialog.");

			if (RepoBrowserContent.image == null) RepoBrowserContent.text = "R";
			if (ShowLogContent.image == null) ShowLogContent.text = "L";
			if (SwitchBranchContent.image == null) SwitchBranchContent.text = "S";

			MiniIconButtonlessStyle = new GUIStyle(GUI.skin.button);
			MiniIconButtonlessStyle.hover.background = MiniIconButtonlessStyle.normal.background;
			MiniIconButtonlessStyle.normal.background = null;
			MiniIconButtonlessStyle.padding = new RectOffset();
			MiniIconButtonlessStyle.margin = new RectOffset();
			wantsMouseMove = true;	// Needed for the hover effects.

			BranchLabelStyle = new GUIStyle(GUI.skin.label);
			BranchLabelStyle.alignment = TextAnchor.MiddleLeft;
			var margin = BranchLabelStyle.margin;
			margin.top += 2;
			BranchLabelStyle.margin = margin;
		}

		void OnEnable()
		{
			SVNBranchesDatabase.Instance.DatabaseChanged -= Repaint;
			SVNBranchesDatabase.Instance.DatabaseChanged += Repaint;
		}

		private void OnDisable()
		{
			SVNBranchesDatabase.Instance.DatabaseChanged -= Repaint;
		}

		void OnGUI()
		{
			if (!m_Initialized) {
				InitializeStyles();
				m_Initialized = true;
			}

			// In case someone deletes the object in the mean time.
			if (m_TargetAsset == null) {
				Close();
				return;
			}

			EditorGUILayout.BeginVertical(BorderStyle);

			using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar)) {
				GUILayout.Label("Branch Selector", WindowTitleStyle, GUILayout.ExpandWidth(true));
			}

			DrawContent();

			EditorGUILayout.EndVertical();
		}

		private void DrawContent()
		{
			using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar)) {

				GUILayout.Label("Asset:", ToolbarTitleStyle, GUILayout.Width(60f));

				m_TargetAsset = EditorGUILayout.ObjectField(m_TargetAsset, m_TargetAsset.GetType(), false, GUILayout.ExpandWidth(true));

				if (SVNBranchesDatabase.Instance.IsUpdating) {
					int dots = ((int)EditorApplication.timeSinceStartup) % 3;
					GUILayout.Label($"Scanning{LoadingDots[dots]}", ToolbarLabelStyle, GUILayout.ExpandWidth(false));
					Repaint();
				} else {
					GUILayout.Label($"Branches: {SVNBranchesDatabase.Instance.BranchProjects.Count}", ToolbarLabelStyle, GUILayout.ExpandWidth(false));
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
					SVNBranchesDatabase.Instance.InvalidateDatabase();
				}
			}


			using (new EditorGUILayout.VerticalScope()) {

				if (SVNBranchesDatabase.Instance.IsUpdating) {
					EditorGUILayout.LabelField("Scanning branches for Unity projects...");
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

				foreach (var branchProject in SVNBranchesDatabase.Instance.BranchProjects) {
					if (!string.IsNullOrEmpty(m_BranchFilter) && branchProject.BranchName.IndexOf(m_BranchFilter, System.StringComparison.OrdinalIgnoreCase) == -1)
						continue;

					using (new EditorGUILayout.HorizontalScope(/*BranchRowStyle*/)) {

						float buttonSize = 24f;
						bool repoBrowser = GUILayout.Button(RepoBrowserContent, MiniIconButtonlessStyle, GUILayout.Height(buttonSize), GUILayout.Width(buttonSize));
						bool showLog = GUILayout.Button(ShowLogContent, MiniIconButtonlessStyle, GUILayout.Height(buttonSize), GUILayout.Width(buttonSize));
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
								SVNContextMenusManager.Switch(WiseSVNIntegration.WorkingCopyRootPath(), branchProject.BranchURL);
								EditorApplication.Exit(0);
							}
						}
					}

					var rect = EditorGUILayout.GetControlRect(GUILayout.ExpandWidth(true), GUILayout.Height(1f));
					EditorGUI.DrawRect(rect, Color.black);
				}

			}
		}

		private void OnLostFocus()
		{
			Close();
		}
	}
}
