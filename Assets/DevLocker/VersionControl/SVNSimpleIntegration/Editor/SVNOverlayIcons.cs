using System;
using UnityEditor;
using UnityEngine;

namespace DevLocker.VersionControl.SVN
{
	/// <summary>
	/// Renders SVN overlay icons in the project windows.
	/// Hooks up to Unity file changes API and refreshes when needed to.
	/// </summary>
	[InitializeOnLoad]
	public static class SVNOverlayIcons
	{
		private static SVNPreferencesManager.PersonalPreferences m_PersonalPrefs => SVNPreferencesManager.Instance.PersonalPrefs;

		private static bool IsActive => m_PersonalPrefs.EnableCoreIntegration && m_PersonalPrefs.PopulateStatusesDatabase;

		static SVNOverlayIcons()
		{
			SVNPreferencesManager.Instance.PreferencesChanged += PreferencesChanged;
			PreferencesChanged();
		}

		private static void PreferencesChanged()
		{
			if (IsActive) {
				EditorApplication.projectWindowItemOnGUI -= ItemOnGUI;
				EditorApplication.projectWindowItemOnGUI += ItemOnGUI;
			} else {
				EditorApplication.projectWindowItemOnGUI -= ItemOnGUI;
			}
		}

		[MenuItem("Assets/SVN/Refresh Overlay Icons", false, 195)]
		private static void InvalidateDatabaseMenu()
		{
			SVNStatusesDatabase.Instance.InvalidateDatabase();
		}

		private static void ItemOnGUI(string guid, Rect selectionRect)
		{
			if (string.IsNullOrEmpty(guid) || guid.StartsWith("00000000", StringComparison.Ordinal))
				// Cause what are the chances of having a guid starting with so many zeroes?!
				//|| guid.Equals(INVALID_GUID, StringComparison.Ordinal)
				//|| guid.Equals(ASSETS_FOLDER_GUID, StringComparison.Ordinal)
				return;

			var statusData = SVNStatusesDatabase.Instance.GetKnownStatusData(guid);

			//
			// Remote Status
			//
			if (SVNPreferencesManager.Instance.DownloadRepositoryChanges && statusData.RemoteStatus != VCRemoteFileStatus.None) {
				var remoteStatusIcon = SVNPreferencesManager.Instance.GetRemoteStatusIconContent(statusData.RemoteStatus);

				if (remoteStatusIcon != null) {
					var iconRect = new Rect(selectionRect);
					if (iconRect.width > iconRect.height) {
						iconRect.x += iconRect.width - iconRect.height;
						iconRect.x -= iconRect.height;
						iconRect.width = iconRect.height;
					} else {
						iconRect.width /= 2.4f;
						iconRect.height = iconRect.width;
						var offset = selectionRect.width - iconRect.width;
						iconRect.x += offset;

						iconRect.y -= 4;
					}

					GUI.Label(iconRect, remoteStatusIcon);
				}
			}

			//
			// Lock Status
			//
			if (SVNPreferencesManager.Instance.DownloadRepositoryChanges && statusData.LockStatus != VCLockStatus.NoLock) {
				var lockStatusIcon = SVNPreferencesManager.Instance.GetLockStatusIconContent(statusData.LockStatus);

				if (lockStatusIcon != null) {
					var iconRect = new Rect(selectionRect);
					if (iconRect.width > iconRect.height) {
						iconRect.x += iconRect.width - iconRect.height;
						iconRect.x -= iconRect.height * 2;
						iconRect.width = iconRect.height;
					} else {
						iconRect.width /= 2.4f;
						iconRect.height = iconRect.width;
						var offset = selectionRect.width - iconRect.width;
						iconRect.x += offset;
						iconRect.y += offset;

						iconRect.y += 4;
					}

					GUI.Label(iconRect, lockStatusIcon);
				}
			}


			//
			// File Status
			//
			GUIContent fileStatusIcon = SVNPreferencesManager.Instance.GetFileStatusIconContent(statusData.Status);

			if (fileStatusIcon != null) {
				var iconRect = new Rect(selectionRect);
				if (iconRect.width > iconRect.height) {
					iconRect.height += 4f;
					iconRect.width = iconRect.height;
				} else {
					iconRect.width /= 1.8f;
					iconRect.height = iconRect.width;
					var offset = selectionRect.width - iconRect.width;
					iconRect.y += offset;
				}

				iconRect.x -= 3;
				iconRect.y += 1;
				GUI.Label(iconRect, fileStatusIcon);
			}
		}

	}
}
