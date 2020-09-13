using DevLocker.VersionControl.WiseSVN.ContextMenus;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DevLocker.VersionControl.WiseSVN.AutoLocking
{
	/// <summary>
	/// Popup that prompts user should it force-lock changed assets.
	/// </summary>
	public class SVNAutoLockingForceWindow : EditorWindow
	{
		[Serializable]
		private class LockEntryData
		{
			public SVNStatusData StatusData;
			public bool IsMeta = false;
			public UnityEngine.Object TargetObject;
			public bool ShouldLock = true;

			public string AssetName => System.IO.Path.GetFileName(StatusData.Path);
			public VCLockStatus LockStatus => StatusData.LockStatus;
			public string Owner => StatusData.LockDetails.Owner;

			public LockEntryData(SVNStatusData statusData)
			{
				var assetPath = statusData.Path;
				if (statusData.Path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) {
					assetPath = statusData.Path.Substring(0, statusData.Path.LastIndexOf(".meta"));
					IsMeta = true;
				}
				assetPath = assetPath.Replace("\\", "/");

				StatusData = statusData;
				TargetObject = AssetDatabase.LoadMainAssetAtPath(assetPath);
			}
		}

		private bool m_WhatAreLocksHintShown = false;
		private bool m_WhatIsForceLocksHintShown = false;
		private List<LockEntryData> m_LockEntries = new List<LockEntryData>();
		private Vector2 m_LockEntriesScroll;

		public static void PromptForceLock(IEnumerable<SVNStatusData> lockedByOtherEntries)
		{
			var window = GetWindow<SVNAutoLockingForceWindow>(true, "SVN Auto-Lock by Force");
			window.minSize = new Vector2(584, 500f);
			var center = new Vector2(Screen.currentResolution.width, Screen.currentResolution.height) / 2f;
			window.position = new Rect(center - window.position.size / 2, window.position.size);
			window.AppendEntriesToLock(lockedByOtherEntries);
		}

		private void AppendEntriesToLock(IEnumerable<SVNStatusData> lockedByOtherEntries)
		{
			var toAdd = lockedByOtherEntries
				.Where(sd => m_LockEntries.All(e => e.StatusData.Path != sd.Path))
				.Select(sd => new LockEntryData(sd))
				;

			m_LockEntries.AddRange(toAdd);
		}

		void OnGUI()
		{
			EditorGUILayout.LabelField("Lock Assets by Force", EditorStyles.boldLabel);

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

			m_WhatIsForceLocksHintShown = EditorGUILayout.Foldout(m_WhatIsForceLocksHintShown, "What is \"lock by force \"?");
			if (m_WhatIsForceLocksHintShown) {
				EditorGUILayout.HelpBox("These assets have local changes and are all locked by someone else. You can steal their lock by force.\n" +
					"They are probably working on these asset and by committing your changes you risk others having conflicts in the future.\n\n" +
					"Coordinate with the locks' owner and select which assets you want to lock by force.",
					MessageType.Info, true);
			}

			EditorGUILayout.HelpBox(
				"If you skip locking assets, you won't be prompted again unless the assets status change or Unity restarts.\n" +
				$"To force re-evaluate all of the locks, select the \"{SVNOverlayIcons.InvalidateDatabaseMenuText}\" menu.",
				MessageType.Warning, true);

			const float LockColumnSize = 34;
			const float OwnerSize = 140f;

			EditorGUILayout.BeginHorizontal();

			GUILayout.Label("Lock", EditorStyles.boldLabel, GUILayout.Width(LockColumnSize));
			GUILayout.Label("Asset", EditorStyles.boldLabel, GUILayout.ExpandWidth(true));
			GUILayout.Label("Owner", EditorStyles.boldLabel, GUILayout.Width(OwnerSize));

			EditorGUILayout.EndHorizontal();

			m_LockEntriesScroll = EditorGUILayout.BeginScrollView(m_LockEntriesScroll);

			foreach (var lockEntry in m_LockEntries) {

				EditorGUILayout.BeginHorizontal();

				const float LockCheckBoxWidth = 14;
				GUILayout.Space(LockColumnSize - LockCheckBoxWidth);
				lockEntry.ShouldLock = EditorGUILayout.Toggle(lockEntry.ShouldLock, GUILayout.Width(LockCheckBoxWidth));

				EditorGUI.BeginDisabledGroup(!lockEntry.ShouldLock);

				if (lockEntry.TargetObject && !lockEntry.IsMeta) {
					EditorGUILayout.ObjectField(lockEntry.TargetObject,
						lockEntry.TargetObject ? lockEntry.TargetObject.GetType() : typeof(UnityEngine.Object),
						false, GUILayout.ExpandWidth(true));
				} else {
					var assetComment = (lockEntry.StatusData.Status == VCFileStatus.Deleted) ? "deleted" : "meta";
					EditorGUILayout.TextField($"({assetComment}) {lockEntry.AssetName}");
				}

				EditorGUILayout.TextField(lockEntry.Owner, GUILayout.Width(OwnerSize));

				EditorGUI.EndDisabledGroup();

				EditorGUILayout.EndHorizontal();
			}

			EditorGUILayout.EndScrollView();



			EditorGUILayout.BeginHorizontal();

			if (GUILayout.Button("Toggle Selected")) {
				foreach(var lockEntry in m_LockEntries) {
					lockEntry.ShouldLock = !lockEntry.ShouldLock;
				}
			}

			GUILayout.FlexibleSpace();

			if (GUILayout.Button("Skip All")) {
				Close();
			}

			var prevColor = GUI.backgroundColor;
			GUI.backgroundColor = Color.green;
			if (GUILayout.Button("Force Lock Selected")) {
				var selectedStatusData = m_LockEntries
					.Where(e => e.ShouldLock)
					.Select(e => e.StatusData)
					;

				if (selectedStatusData.Any()) {
					SVNAutoLockingDatabase.Instance.ForceLock(selectedStatusData);
				}
				Close();
			}
			GUI.backgroundColor = prevColor;

			EditorGUILayout.EndHorizontal();
		}

	}
}
