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

		private bool m_WhatAreLocksHintShown = false;
		private bool m_WhatIsForceLocksHintShown = false;

		private SVNPreferencesManager.PersonalPreferences m_PersonalPrefs => SVNPreferencesManager.Instance.PersonalPrefs;
		private SVNPreferencesManager.ProjectPreferences m_ProjectPrefs => SVNPreferencesManager.Instance.ProjectPrefs;

		private bool m_AllowStealingLocks = false;
		private List<LockEntryData> m_LockEntries = new List<LockEntryData>();
		private Vector2 m_LockEntriesScroll;

		public static void PromptLock(IEnumerable<SVNStatusData> shouldLockEntries, IEnumerable<SVNStatusData> lockedByOtherEntries)
		{
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
			window.minSize = new Vector2(584, 500f);
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

		void OnGUI()
		{
			EditorGUILayout.LabelField("Lock Modified Assets", EditorStyles.boldLabel);

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
				$"To force re-evaluate all of the locks, select the \"{SVNOverlayIcons.InvalidateDatabaseMenuText}\" menu.",
				MessageType.Warning, true);

			const float LockColumnSize = 34;
			const float OwnerSize = 140f;

			bool needsUpdate = false;

			EditorGUILayout.BeginHorizontal();

			GUILayout.Label("Lock", EditorStyles.boldLabel, GUILayout.Width(LockColumnSize));
			GUILayout.Label("Asset", EditorStyles.boldLabel, GUILayout.ExpandWidth(true));
			GUILayout.Label("Owner", EditorStyles.boldLabel, GUILayout.Width(OwnerSize));

			EditorGUILayout.EndHorizontal();

			m_LockEntriesScroll = EditorGUILayout.BeginScrollView(m_LockEntriesScroll);

			foreach (var lockEntry in m_LockEntries) {

				EditorGUILayout.BeginHorizontal();

				bool shouldDisableRow = lockEntry.StatusData.RemoteStatus != VCRemoteFileStatus.None;
				if (!m_AllowStealingLocks) {
					shouldDisableRow = shouldDisableRow || lockEntry.LockedByOther;
				}

				EditorGUI.BeginDisabledGroup(shouldDisableRow);

				const float LockCheckBoxWidth = 14;
				GUILayout.Space(LockColumnSize - LockCheckBoxWidth);
				lockEntry.ShouldLock = EditorGUILayout.Toggle(lockEntry.ShouldLock, GUILayout.Width(LockCheckBoxWidth)) && !shouldDisableRow;

				EditorGUI.BeginDisabledGroup(!lockEntry.ShouldLock);

				if (lockEntry.TargetObject == null || lockEntry.IsMeta) {
					var assetComment = (lockEntry.StatusData.Status == VCFileStatus.Deleted) ? "deleted" : "meta";
					EditorGUILayout.TextField($"({assetComment}) {lockEntry.AssetName}", GUILayout.ExpandWidth(true));
				}

				if (lockEntry.StatusData.Status != VCFileStatus.Deleted) {
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

				if (lockEntry.StatusData.RemoteStatus == VCRemoteFileStatus.None) {
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

			GUILayout.FlexibleSpace();

			var prevBackgroundColor = GUI.backgroundColor;

			GUI.backgroundColor = Color.yellow;
			if (needsUpdate && GUILayout.Button("Update All")) {
				SVNContextMenusManager.UpdateAll();
				SVNLockPromptDatabase.Instance.ClearKnowledge();
				Close();
			}

			GUI.backgroundColor = prevBackgroundColor;
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
