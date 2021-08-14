using DevLocker.VersionControl.WiseSVN.ContextMenus;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DevLocker.VersionControl.WiseSVN.Documentation
{
	/// <summary>
	/// This an example window showing how you can integrate your tools with the WiseSVN plugin.
	/// When your tool needs to run some SVN operation it is best to run Async method
	/// and subscribe for the task events to avoid editor freezing.
	/// Those events are guaranteed to run on the Unity thread.
	/// </summary>
	public class ExampleStatusWindow : EditorWindow
	{
		private string m_CombinedOutput = "";
		private string m_StateLabel = "Idle";
		private Vector2 m_OutputScroll;

		private SVNAsyncOperation<IEnumerable<SVNStatusData>> m_SVNOperation;

		//[MenuItem("Assets/SVN/Example Status Window")]
		private static void Init()
		{
			var window = (ExampleStatusWindow)GetWindow(typeof(ExampleStatusWindow), false, "Example SVN Window");

			window.position = new Rect(window.position.xMin + 100f, window.position.yMin + 100f, 450f, 600f);
			window.minSize = new Vector2(450f, 200f);
		}

		private void OnGUI()
		{
			var outputStyle = new GUIStyle(EditorStyles.textArea);
			outputStyle.wordWrap = false;

			var textSize = outputStyle.CalcSize(new GUIContent(m_CombinedOutput));

			m_OutputScroll = EditorGUILayout.BeginScrollView(m_OutputScroll);
			EditorGUILayout.LabelField(m_CombinedOutput, outputStyle, GUILayout.ExpandHeight(true), GUILayout.ExpandWidth(true), GUILayout.MinWidth(textSize.x), GUILayout.MinHeight(textSize.y));
			EditorGUILayout.EndScrollView();

			EditorGUILayout.BeginHorizontal();
			{
				bool isWorking = m_SVNOperation == null || m_SVNOperation.HasFinished;

				EditorGUI.BeginDisabledGroup(isWorking);

				if (GUILayout.Button("Abort")) {
					m_SVNOperation.Abort(false);
					m_CombinedOutput += "Aborting...\n";
					m_StateLabel = "Aborting...";
				}

				if (GUILayout.Button("Kill")) {
					m_SVNOperation.Abort(true);
					m_CombinedOutput += "Killing...\n";
					m_StateLabel = "Killing...";
				}

				EditorGUI.EndDisabledGroup();

				GUILayout.FlexibleSpace();

				EditorGUILayout.LabelField(m_StateLabel);

				GUILayout.FlexibleSpace();

				if (GUILayout.Button("Clear")) {
					m_CombinedOutput = "";
				}

				EditorGUI.BeginDisabledGroup(!isWorking);

				if (GUILayout.Button("Get Status")) {
					m_SVNOperation = WiseSVNIntegration.GetStatusesAsync(".", recursive: true, offline: false);

					m_SVNOperation.AnyOutput += (line) => { m_CombinedOutput += line + "\n"; };

					m_SVNOperation.Completed += (op) => {
						m_StateLabel = op.AbortRequested ? "Aborted!" : "Completed!";
						m_CombinedOutput += m_StateLabel + "\n\n";
						m_SVNOperation = null;
					};

					m_StateLabel = "Working...";
				}

				EditorGUI.EndDisabledGroup();
			}
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.Space();

			EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
			{
				GUILayout.FlexibleSpace();

				GUILayout.Label("External SVN Client:");

				if (GUILayout.Button("Commit", GUILayout.ExpandWidth(false))) {
					SVNContextMenusManager.CommitAll();
				}

				if (GUILayout.Button("Update", GUILayout.ExpandWidth(false))) {
					SVNContextMenusManager.UpdateAll();
				}
			}
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.Space();
		}
	}
}
