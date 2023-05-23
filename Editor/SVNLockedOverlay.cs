// MIT License Copyright(c) 2022 Filip Slavov, https://github.com/NibbleByte/UnityWiseSVN

using System;
using System.Collections.Generic;
using System.IO;
using DevLocker.VersionControl.WiseSVN.Preferences;
using DevLocker.VersionControl.WiseSVN.Utils;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DevLocker.VersionControl.WiseSVN
{
	/// <summary>
	/// Renders scene or prefab overlay indicating that the asset is locked or out of date.
	/// </summary>
	class SVNLockedOverlay : EditorPersistentSingleton<SVNLockedOverlay>
	{
		[InitializeOnLoad]
		class SVNLockedOverlayStarter
		{
			// HACK: If this was the SVNLockPromptDatabase itself it causes exceptions on assembly reload.
			//		 The static constructor gets called during reload because the instance exists.
			static SVNLockedOverlayStarter()
			{
				Instance.PreferencesChanged();
			}
		}

		[SerializeField]
		private string m_SceneMessage;
		[SerializeField]
		private float m_SceneMessageWidth;
		[SerializeField]
		private string m_PrefabMessage;
		[SerializeField]
		private float m_PrefabMessageWidth;



		[SerializeField]
		private List<Scene> m_CurrentScenes = new List<Scene>();

		[SerializeField]
		private string m_CurrentPrefabPath = string.Empty;

		[NonSerialized]
		private GUIStyle m_MessageStyle;

		private SVNPreferencesManager.PersonalPreferences m_PersonalPrefs => SVNPreferencesManager.Instance.PersonalPrefs;

		private bool IsActive => m_PersonalPrefs.EnableCoreIntegration
		                         && m_PersonalPrefs.PopulateStatusesDatabase
		                         && SVNPreferencesManager.Instance.DownloadRepositoryChanges
		                         && !SVNPreferencesManager.Instance.NeedsToAuthenticate
								 && m_PersonalPrefs.WarnForPotentialConflicts;

		public override void Initialize(bool freshlyCreated)
		{
			SVNPreferencesManager.Instance.PreferencesChanged += PreferencesChanged;
			SVNStatusesDatabase.Instance.DatabaseChanged += OnDatabaseChanged;
		}

		private GUIStyle GetMessageStyle()
		{
			if (m_MessageStyle == null) {
				m_MessageStyle = new GUIStyle(GUI.skin.box);
				m_MessageStyle.alignment = TextAnchor.MiddleCenter;
				m_MessageStyle.normal.textColor = Color.white;
				m_MessageStyle.active.textColor = Color.white;
				m_MessageStyle.focused.textColor = Color.white;
				m_MessageStyle.hover.textColor = Color.white;
			}

			return m_MessageStyle;
		}

		private void PreferencesChanged()
		{
			if (IsActive) {
#if UNITY_2019_1_OR_NEWER
				SceneView.duringSceneGui -= SceneViewOnGUI;
				SceneView.duringSceneGui += SceneViewOnGUI;
#else
				SceneView.onSceneGUIDelegate -= SceneViewOnGUI;
				SceneView.onSceneGUIDelegate += SceneViewOnGUI;
#endif
			} else {
#if UNITY_2019_1_OR_NEWER
				SceneView.duringSceneGui -= SceneViewOnGUI;
#else
				SceneView.onSceneGUIDelegate -= SceneViewOnGUI;
#endif
			}

			OnDatabaseChanged();
		}

		private void OnDatabaseChanged()
		{
			EditorApplication.RepaintProjectWindow();
		}

		private void CheckScenes()
		{
			if (m_CurrentScenes.Count != SceneManager.sceneCount) {
				RefreshScenesMessage();
				return;
			}

			for (int i = 0; i < SceneManager.sceneCount; ++i) {
				if (m_CurrentScenes[i].handle != SceneManager.GetSceneAt(i).handle) {
					RefreshScenesMessage();
					return;
				}
			}
		}

		private void RefreshScenesMessage()
		{
			m_CurrentScenes.Clear();
			m_SceneMessage = string.Empty;

			for (int i = 0; i < SceneManager.sceneCount; ++i) {
				Scene scene = SceneManager.GetSceneAt(i);

				m_CurrentScenes.Add(scene);

				if (string.IsNullOrEmpty(scene.path))
					continue;

				var guid = AssetDatabase.AssetPathToGUID(scene.path);
				var statusData = SVNStatusesDatabase.Instance.GetKnownStatusData(guid);

				if (statusData.RemoteStatus != VCRemoteFileStatus.None) {
					m_SceneMessage += $"Scene \"{scene.name}\" is out of date in SVN!\n";
				} else if (statusData.LockStatus == VCLockStatus.LockedOther || statusData.LockStatus == VCLockStatus.LockedButStolen) {
					m_SceneMessage += $"Scene \"{scene.name}\" is locked by {statusData.LockDetails.Owner} in SVN!\n";
				} else if (statusData.LockStatus == VCLockStatus.BrokenLock) {
					m_SceneMessage += $"Scene \"{scene.name}\" lock is broken in SVN!\n";
				}

			}

			m_SceneMessage = m_SceneMessage.TrimEnd('\n');

			m_SceneMessageWidth = GetMessageStyle().CalcSize(new GUIContent(m_SceneMessage)).x;
		}

		private void CheckPrefab()
		{
#if UNITY_2021_3_OR_NEWER
			var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
#else
			var stage = UnityEditor.Experimental.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
#endif

#if UNITY_2020_1_OR_NEWER
			string prefabPath = stage?.assetPath ?? string.Empty;
#else
			string prefabPath = stage?.prefabAssetPath ?? string.Empty;
#endif

			bool prefabIsOpen = !string.IsNullOrEmpty(prefabPath);
			bool prefabWasOpen = !string.IsNullOrEmpty(m_CurrentPrefabPath);

			if (prefabWasOpen != prefabIsOpen) {
				RefreshPrefabMessage(prefabPath);
				return;
			}

			if (prefabIsOpen && m_CurrentPrefabPath != prefabPath) {
				RefreshPrefabMessage(prefabPath);
				return;
			}
		}

		private void RefreshPrefabMessage(string prefabPath)
		{
			m_CurrentPrefabPath = prefabPath;
			m_PrefabMessage = String.Empty;

			if (!string.IsNullOrEmpty(m_CurrentPrefabPath)) {
				var guid = AssetDatabase.AssetPathToGUID(m_CurrentPrefabPath);
				var statusData = SVNStatusesDatabase.Instance.GetKnownStatusData(guid);

				if (statusData.RemoteStatus != VCRemoteFileStatus.None) {
					m_PrefabMessage = $"Prefab \"{Path.GetFileNameWithoutExtension(prefabPath)}\" is out of date in SVN!";
				} else if (statusData.LockStatus == VCLockStatus.LockedOther || statusData.LockStatus == VCLockStatus.LockedButStolen) {
					m_PrefabMessage = $"Prefab \"{Path.GetFileNameWithoutExtension(prefabPath)}\" is locked by {statusData.LockDetails.Owner} in SVN!";
				} else if (statusData.LockStatus == VCLockStatus.BrokenLock) {
					m_PrefabMessage = $"Prefab \"{Path.GetFileNameWithoutExtension(prefabPath)}\" lock is broken in SVN!";
				}

			}

			m_PrefabMessageWidth = GetMessageStyle().CalcSize(new GUIContent(m_PrefabMessage)).x;
		}

		private void SceneViewOnGUI(SceneView sceneView)
		{
			if (Application.isPlaying || !SVNStatusesDatabase.Instance.IsReady)
				return;

			Handles.BeginGUI();

			CheckScenes();

			CheckPrefab();


			if (!string.IsNullOrEmpty(m_SceneMessage) && string.IsNullOrEmpty(m_CurrentPrefabPath) || !string.IsNullOrEmpty(m_PrefabMessage)) {
				string targetMessage = string.IsNullOrEmpty(m_PrefabMessage) ? m_SceneMessage : m_PrefabMessage;
				float targetWidth = string.IsNullOrEmpty(m_PrefabMessage) ? m_SceneMessageWidth : m_PrefabMessageWidth;

				float width = Mathf.Max(300, targetWidth + 40f);
				const float height = 70f;

				Rect messageRect = new Rect();
				messageRect.x = sceneView.position.width / 2f - width / 2f;
				messageRect.y = 32;
				messageRect.width = width;
				messageRect.height = height;

				const float closeSize = 18f;
				const float closeOffset = 6f;

				Rect closeRect = new Rect();
				closeRect.x = messageRect.x + messageRect.width - closeSize + closeOffset;
				closeRect.y = messageRect.y - closeOffset;
				closeRect.width = closeRect.height = closeSize;

				var prevBackgroundColor = GUI.backgroundColor;
				GUI.backgroundColor = Color.red;


				GUI.Box(messageRect, targetMessage, GetMessageStyle());

				// HACK: the text color of the box is done in the style, because it breaks
				//		 when unity starts and displays it immediately.
				var prevColor = GUI.color;
				GUI.color = Color.white;

				if (GUI.Button(closeRect, "X")) {
					if (string.IsNullOrEmpty(m_PrefabMessage)) {
						m_SceneMessage = string.Empty;
					} else {
						m_PrefabMessage = string.Empty;
					}
				}

				GUI.color = prevColor;
				GUI.backgroundColor = prevBackgroundColor;
			}

			Handles.EndGUI();
		}
	}
}
