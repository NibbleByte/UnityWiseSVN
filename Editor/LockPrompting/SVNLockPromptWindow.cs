// MIT License Copyright(c) 2022 Filip Slavov, https://github.com/NibbleByte/UnityWiseSVN

using DevLocker.VersionControl.WiseSVN.ContextMenus;
using DevLocker.VersionControl.WiseSVN.Preferences;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DevLocker.VersionControl.WiseSVN.LockPrompting
{
	/// <summary>
	/// Popup that prompts user should it force-lock changed assets.
	/// </summary>
	public class SVNLockPromptWindow : EditorWindow
	{
		[Serializable]
		private class LockEntryData
		{
			public SVNStatusData StatusData;
			public bool IsMeta = false;

#pragma warning disable CA2235 // Field is a member of Serializable but is not of such type. Unity will handle this.
			public UnityEngine.Object TargetObject;
#pragma warning restore CA2235

			public bool ShouldLock = true;

			public string AssetName => System.IO.Path.GetFileName(StatusData.Path);
			public VCLockStatus LockStatus => StatusData.LockStatus;
			public string Owner => StatusData.LockDetails.Owner;

			public bool LockedByOther => LockStatus == VCLockStatus.LockedOther
			                             || LockStatus == VCLockStatus.LockedButStolen;

			public LockEntryData() { }

			public LockEntryData(SVNStatusData statusData)
			{
				var assetPath = statusData.Path;
				if (statusData.Path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) {
					assetPath = statusData.Path.Substring(0, statusData.Path.LastIndexOf(".meta"));
					IsMeta = true;
				}

				StatusData = statusData;
				TargetObject = AssetDatabase.LoadMainAssetAtPath(assetPath);

				// Can't lock if there is newer version at the repository or locked by others.
				ShouldLock = statusData.RemoteStatus == VCRemoteFileStatus.None && !LockedByOther;
			}
		}

		private bool m_Initialized = false;

		private bool m_WhatAreLocksHintShown = false;
		private bool m_WhatIsForceLocksHintShown = false;

		private SVNPreferencesManager.PersonalPreferences m_PersonalPrefs => SVNPreferencesManager.Instance.PersonalPrefs;
		private SVNPreferencesManager.ProjectPreferences m_ProjectPrefs => SVNPreferencesManager.Instance.ProjectPrefs;

		private bool m_AllowStealingLocks = false;
		private List<LockEntryData> m_LockEntries = new List<LockEntryData>();
		private Vector2 m_LockEntriesScroll;

		private GUIContent m_RevertContent;
		private GUIContent m_DiffContent;
		private GUIStyle MiniIconButtonlessStyle;

		public static void PromptLock(IEnumerable<SVNStatusData> shouldLockEntries, IEnumerable<SVNStatusData> lockedByOtherEntries)
		{
			if (SVNPreferencesManager.Instance.TemporarySilenceLockPrompts)
				return;

			if (SVNPreferencesManager.Instance.PersonalPrefs.AutoLockOnModified) {
				SVNLockPromptDatabase.Instance.LockEntries(shouldLockEntries, false);

				string notificationMessage = $"Auto-Locking {shouldLockEntries.Count()} Assets in SVN";

				if (focusedWindow && !(focusedWindow is SceneView)) {
					focusedWindow.ShowNotification(new GUIContent(notificationMessage));
				}

				foreach(SceneView sceneView in SceneView.sceneViews) {
					sceneView.ShowNotification(new GUIContent(notificationMessage));
				}

				shouldLockEntries = new List<SVNStatusData>();

				if (!lockedByOtherEntries.Any()) {
					return;
				}
			}

			var window = GetWindow<SVNLockPromptWindow>(true, "SVN Lock Modified Assets");
			window.minSize = new Vector2(600f, 500f);
			var center = new Vector2(Screen.currentResolution.width, Screen.currentResolution.height) / 2f;
			window.position = new Rect(center - window.position.size / 2, window.position.size);
			window.AppendEntriesToLock(lockedByOtherEntries);
			window.AppendEntriesToLock(shouldLockEntries);

		}

		private void AppendEntriesToLock(IEnumerable<SVNStatusData> entries)
		{
			var toAdd = entries
				.Where(sd => m_LockEntries.All(e => e.StatusData.Path != sd.Path))
				.Select(sd => new LockEntryData(sd))
				;

			m_LockEntries.AddRange(toAdd);
		}

		void OnEnable()
		{
			// Resets on assembly reload.
			wantsMouseMove = true;  // Needed for the hover effects.
		}

		private void InitializeStyles()
		{
			m_RevertContent = SVNPreferencesManager.LoadTexture("Editor/BranchesIcons/SVN-Revert", "Revert asset");
			m_DiffContent = SVNPreferencesManager.LoadTexture("Editor/BranchesIcons/SVN-ConflictsScan-Pending", "Check changes");

			// Copied from SVNBranchSelectorWindow.
			MiniIconButtonlessStyle = new GUIStyle(GUI.skin.button);
			MiniIconButtonlessStyle.hover.background = MiniIconButtonlessStyle.normal.background;
			MiniIconButtonlessStyle.hover.scaledBackgrounds = MiniIconButtonlessStyle.normal.scaledBackgrounds;
			MiniIconButtonlessStyle.hover.textColor = GUI.skin.label.hover.textColor;
			MiniIconButtonlessStyle.normal.background = null;
			MiniIconButtonlessStyle.normal.scaledBackgrounds = null;
			MiniIconButtonlessStyle.padding = new RectOffset();
			MiniIconButtonlessStyle.margin = new RectOffset();

			SVNPreferencesWindow.MigrateButtonStyleToUIElementsIfNeeded(MiniIconButtonlessStyle);
		}

		void OnGUI()
		{
			if (!m_Initialized) {
				InitializeStyles();

				m_Initialized = true;
			}

			// For hover effects to work.
			if (Event.current.type == EventType.MouseMove) {
				Repaint();
			}

			EditorGUILayout.BeginHorizontal();

			EditorGUILayout.LabelField("Lock Modified Assets", EditorStyles.boldLabel);

			GUILayout.FlexibleSpace();

			var silenceContent = new GUIContent("Silence!", $"Suppress all lock prompts and auto-lock actions until Unity is restarted or by selecting \"{SVNOverlayIcons.InvalidateDatabaseMenuText.Replace("&&", "&")}\"");
			if (GUILayout.Button(silenceContent, EditorStyles.toolbarButton)) {
				if (EditorUtility.DisplayDialog("Silence Lock Prompts?", $"{silenceContent.tooltip}\n\nUseful if you want to test stuff locally without committing later.\n\nAre you sure?", "Yes", "No")) {
					SVNPreferencesManager.Instance.TemporarySilenceLockPrompts = true;
					SVNLockPromptDatabase.Instance.ClearKnowledge();
					Close();
				}
			}

			EditorGUILayout.EndHorizontal();

			m_WhatAreLocksHintShown = EditorGUILayout.Foldout(m_WhatAreLocksHintShown, "What are locks?");
			if (m_WhatAreLocksHintShown) {
				EditorGUILayout.HelpBox("In a large team two or more people may be working on the same files simultaneously, unaware of each other.\n" +
					"First one to commit their changes to the repository server has their work \"saved\".\n" +
					"Anyone else trying to commit later on will have to deal with any resulting conflicts.\n" +
					"This may lead to lost work. To avoid this, when starting work on a file, one can \"lock\" that file on the server.\n" +
					"This indicates to everyone else that someone is working on that file.",
					MessageType.Info, true);
				EditorGUILayout.Space();
			}

			m_AllowStealingLocks = EditorGUILayout.Toggle("Steal locks by force", m_AllowStealingLocks);

			if (m_AllowStealingLocks) {
				m_WhatIsForceLocksHintShown = EditorGUILayout.Foldout(m_WhatIsForceLocksHintShown, "What is \"Steal locks by force\"?");
				if (m_WhatIsForceLocksHintShown) {
					EditorGUILayout.HelpBox("These assets have local changes and are all locked by someone else. You can steal their lock by force.\n" +
					                        "They are probably working on these asset and by committing your changes you risk others having conflicts in the future.\n\n" +
					                        "Coordinate with the locks' owner and select which assets you want to lock by force.",
						MessageType.Info, true);
				}
			}

			bool autoLock = EditorGUILayout.Toggle(new GUIContent("Auto lock when possible", SVNPreferencesManager.PersonalPreferences.AutoLockOnModifiedHint + "\n\nCan be changed in the SVN Preferences window."), m_PersonalPrefs.AutoLockOnModified);
			if (m_PersonalPrefs.AutoLockOnModified != autoLock) {
				m_PersonalPrefs.AutoLockOnModified = autoLock;
				SVNPreferencesManager.Instance.SavePreferences(m_PersonalPrefs, m_ProjectPrefs);
			}

			EditorGUILayout.HelpBox(
				"If you skip locking assets, you won't be prompted again unless the assets status change or Unity restarts.\n" +
				$"To force re-evaluate all of the locks, select the \"{SVNOverlayIcons.InvalidateDatabaseMenuText.Replace("&&", "&")}\" menu.",
				MessageType.Warning, true);

			const float LockColumnSize = 34;
			const float OwnerSize = 140f;

			#if UNITY_2019_4_OR_NEWER
			const float RevertSize = 20f;
			#else
			const float RevertSize = 18f;
			#endif

			bool needsUpdate = false;

			EditorGUILayout.BeginHorizontal();

			GUILayout.Label("Lock", EditorStyles.boldLabel, GUILayout.Width(LockColumnSize));
			GUILayout.Label("Asset", EditorStyles.boldLabel, GUILayout.ExpandWidth(true));
			GUILayout.Label("Revert", EditorStyles.boldLabel, GUILayout.Width(RevertSize * 2 + 12f));
			GUILayout.Label("Owner", EditorStyles.boldLabel, GUILayout.Width(OwnerSize));

			EditorGUILayout.EndHorizontal();

			if (m_LockEntries.Count == 0) {
				GUILayout.Label("Scanning for changes...");
			}

			m_LockEntriesScroll = EditorGUILayout.BeginScrollView(m_LockEntriesScroll);

			foreach (var lockEntry in m_LockEntries) {

				SVNStatusData statusData = lockEntry.StatusData;

				EditorGUILayout.BeginHorizontal();

				bool shouldDisableRow = statusData.RemoteStatus != VCRemoteFileStatus.None;
				if (!m_AllowStealingLocks) {
					shouldDisableRow = shouldDisableRow || lockEntry.LockedByOther;
				}

				// NOTE: This is copy-pasted below.
				EditorGUI.BeginDisabledGroup(shouldDisableRow);

				const float LockCheckBoxWidth = 14;
				GUILayout.Space(LockColumnSize - LockCheckBoxWidth);
				lockEntry.ShouldLock = EditorGUILayout.Toggle(lockEntry.ShouldLock, GUILayout.Width(LockCheckBoxWidth)) && !shouldDisableRow;

				// NOTE: This is copy-pasted below.
				EditorGUI.BeginDisabledGroup(!lockEntry.ShouldLock);

				if (lockEntry.TargetObject == null || lockEntry.IsMeta) {
					var assetComment = (statusData.Status == VCFileStatus.Deleted) ? "deleted" : "meta";
					EditorGUILayout.BeginHorizontal();
					EditorGUILayout.TextField($"({assetComment}) {lockEntry.AssetName}", GUILayout.ExpandWidth(true));

					if (statusData.IsMovedFile) {
						UnityEngine.Object movedToObject = AssetDatabase.LoadMainAssetAtPath(statusData.MovedTo);

						GUILayout.Label(new GUIContent("=>", "Moved to..."), GUILayout.ExpandWidth(false));
						if (movedToObject) {
							EditorGUILayout.ObjectField(movedToObject, movedToObject.GetType(), false, GUILayout.MaxWidth(100f));
						} else {
							EditorGUILayout.TextField(statusData.MovedTo, GUILayout.MaxWidth(100f));
						}
					}
					EditorGUILayout.EndHorizontal();
				}

				// Marked for deletion file can still exist on disk. In that case - show it.
				if (statusData.Status != VCFileStatus.Deleted || lockEntry.TargetObject) {
					if (lockEntry.IsMeta) {
						EditorGUILayout.ObjectField(lockEntry.TargetObject,
							lockEntry.TargetObject ? lockEntry.TargetObject.GetType() : typeof(UnityEngine.Object),
							false, GUILayout.MaxWidth(100f));
					} else {
						EditorGUILayout.ObjectField(lockEntry.TargetObject,
							lockEntry.TargetObject ? lockEntry.TargetObject.GetType() : typeof(UnityEngine.Object),
							false, GUILayout.ExpandWidth(true));
					}
				}

				EditorGUI.EndDisabledGroup();

				EditorGUI.EndDisabledGroup();

				if (GUILayout.Button(m_RevertContent, MiniIconButtonlessStyle, GUILayout.Width(RevertSize), GUILayout.Height(RevertSize))) {
					if (statusData.Path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) && (statusData.Status == VCFileStatus.Added || statusData.Status == VCFileStatus.Deleted)) {
						if (!EditorUtility.DisplayDialog("Revert meta", "Reverting meta files directly for Added or Deleted assets is usually a bad idea. Are you sure?", "Revert .meta", "Cancel")) {
							GUIUtility.ExitGUI();
						}
					}

					using (var reporter = WiseSVNIntegration.CreateReporter()) {

						if (statusData.Status == VCFileStatus.Deleted
							&& !string.IsNullOrEmpty(statusData.MovedTo)
							&& !statusData.Path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) {

							int choice = EditorUtility.DisplayDialogComplex(
								"Revert Moved Asset",
								$"This asset was moved to\n\"{statusData.MovedTo}\"\n\nDo you want to move it back instead?",
								"Move it back", "Cancel", "Revert deleted"
								);

							if (choice == 0) {
								string error = AssetDatabase.ValidateMoveAsset(statusData.MovedTo, statusData.Path);
								if (!string.IsNullOrEmpty(error)) {
									EditorUtility.DisplayDialog("Revert Error", $"Couldn't move asset back:\n\"{error}\"", "Ok");
									GUIUtility.ExitGUI();
								}
								AssetDatabase.MoveAsset(statusData.MovedTo, statusData.Path);

								m_LockEntries.Remove(lockEntry);
								m_LockEntries.RemoveAll(e => e.StatusData.Path == statusData.Path + ".meta");

								GUIUtility.ExitGUI();
							}

							if (choice == 1) {
								GUIUtility.ExitGUI();
							}
						}
						WiseSVNIntegration.Revert(new string[] { statusData.Path }, false, true, false, "", -1, reporter);
					}

					AssetDatabase.Refresh();
					//SVNStatusesDatabase.Instance.InvalidateDatabase();	// Change will trigger this automatically.

					m_LockEntries.Remove(lockEntry);

					if (m_LockEntries.Count == 0) {
						Close();
					}

					GUIUtility.ExitGUI();
				}

				GUILayout.Space(4f);

				MiniIconButtonlessStyle.contentOffset = new Vector2(0f, -2f);
				if (GUILayout.Button(m_DiffContent, MiniIconButtonlessStyle, GUILayout.Width(RevertSize), GUILayout.Height(RevertSize))) {
					if (!string.IsNullOrEmpty(statusData.MovedTo)) {
						SVNContextMenusManager.DiffAsset(statusData.MovedTo);
					} else {
						SVNContextMenusManager.DiffAsset(statusData.Path);
					}
				}
				MiniIconButtonlessStyle.contentOffset = new Vector2(0f, 0f);

				GUILayout.Space(4f);


				EditorGUI.BeginDisabledGroup(shouldDisableRow);

				EditorGUI.BeginDisabledGroup(!lockEntry.ShouldLock);

				if (statusData.RemoteStatus == VCRemoteFileStatus.None) {
					if (lockEntry.LockedByOther) {
						EditorGUILayout.TextField(lockEntry.Owner, GUILayout.Width(OwnerSize));
					} else {
						EditorGUILayout.LabelField("", GUILayout.Width(OwnerSize));
					}
				} else {
					Color prevColor = GUI.color;
					GUI.color = Color.yellow;

					EditorGUILayout.LabelField(new GUIContent("Out of date!", "Can't lock because the server repository has newer changes. You need to update."), GUILayout.Width(OwnerSize));
					needsUpdate = true;

					GUI.color = prevColor;
				}

				EditorGUI.EndDisabledGroup();

				EditorGUI.EndDisabledGroup();

				EditorGUILayout.EndHorizontal();
			}

			EditorGUILayout.EndScrollView();



			EditorGUILayout.BeginHorizontal();

			if (GUILayout.Button("Toggle Selected")) {
				foreach(var lockEntry in m_LockEntries) {

					bool lockConflict = lockEntry.StatusData.RemoteStatus != VCRemoteFileStatus.None;
					if (!m_AllowStealingLocks) {
						lockConflict = lockConflict || lockEntry.LockedByOther;
					}

					lockEntry.ShouldLock = !lockEntry.ShouldLock && !lockConflict;
				}
			}

			if (GUILayout.Button("Refresh All")) {
				SVNStatusesDatabase.Instance.InvalidateDatabase();
				SVNLockPromptDatabase.Instance.ClearKnowledge();
				m_LockEntries.Clear();
			}

			GUILayout.FlexibleSpace();

			var prevBackgroundColor = GUI.backgroundColor;

			GUI.backgroundColor = Color.yellow;
			if (needsUpdate && GUILayout.Button("Update All")) {
				SVNContextMenusManager.UpdateAll();
				SVNLockPromptDatabase.Instance.ClearKnowledge();
				Close();
			}

			GUI.backgroundColor = prevBackgroundColor;

			if (GUILayout.Button("Revert All Window")) {
				SVNContextMenusManager.RevertAll();
				AssetDatabase.Refresh();
				//SVNStatusesDatabase.Instance.InvalidateDatabase();	// Change will trigger this automatically.
				SVNLockPromptDatabase.Instance.ClearKnowledge();
				Close();
			}

			if (GUILayout.Button("Skip All")) {
				Close();
			}

			GUI.backgroundColor = m_AllowStealingLocks ? Color.red : Color.green;
			var lockSelectedButtonText = m_AllowStealingLocks ? "Lock OR STEAL Selected" : "Lock Selected";

			if (GUILayout.Button(lockSelectedButtonText)) {
				var selectedStatusData = m_LockEntries
					.Where(e => e.ShouldLock)
					.Select(e => e.StatusData)
					.ToList();

				if (selectedStatusData.Any()) {
					SVNLockPromptDatabase.Instance.LockEntries(selectedStatusData, m_AllowStealingLocks);
				}
				Close();
			}
			GUI.backgroundColor = prevBackgroundColor;

			EditorGUILayout.EndHorizontal();
		}

	}
}
