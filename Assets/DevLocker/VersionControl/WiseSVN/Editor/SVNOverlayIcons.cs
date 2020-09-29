using DevLocker.VersionControl.WiseSVN.Preferences;
using System;
using UnityEditor;
using UnityEngine;

namespace DevLocker.VersionControl.WiseSVN
{
	/// <summary>
	/// Renders SVN overlay icons in the project windows.
	/// Hooks up to Unity file changes API and refreshes when needed to.
	/// </summary>
	[InitializeOnLoad]
	internal static class SVNOverlayIcons
	{
		private static SVNPreferencesManager.PersonalPreferences m_PersonalPrefs => SVNPreferencesManager.Instance.PersonalPrefs;

		private static bool IsActive => m_PersonalPrefs.EnableCoreIntegration && (m_PersonalPrefs.PopulateStatusesDatabase || SVNPreferencesManager.Instance.ProjectPrefs.EnableAutoLocking);

		private static bool m_ShowNormalStatusIcons = false;

		static SVNOverlayIcons()
		{
			SVNPreferencesManager.Instance.PreferencesChanged += PreferencesChanged;
			SVNStatusesDatabase.Instance.DatabaseChanged += OnDatabaseChanged;

			PreferencesChanged();
		}

		private static void PreferencesChanged()
		{
			if (IsActive) {
				EditorApplication.projectWindowItemOnGUI -= ItemOnGUI;
				EditorApplication.projectWindowItemOnGUI += ItemOnGUI;

				m_ShowNormalStatusIcons = SVNPreferencesManager.Instance.PersonalPrefs.ShowNormalStatusOverlayIcon;
			} else {
				EditorApplication.projectWindowItemOnGUI -= ItemOnGUI;
			}

			OnDatabaseChanged();
		}

		public const string InvalidateDatabaseMenuText = "Assets/SVN/Refresh Overlay Icons";
		[MenuItem(InvalidateDatabaseMenuText, false, 195)]
		private static void InvalidateDatabaseMenu()
		{
			SVNStatusesDatabase.Instance.InvalidateDatabase();
			AutoLocking.SVNAutoLockingDatabase.Instance.ClearKnowledge();
		}

		private static void OnDatabaseChanged()
		{
			EditorApplication.RepaintProjectWindow();
		}

		private static void ItemOnGUI(string guid, Rect selectionRect)
		{
			if (string.IsNullOrEmpty(guid) || guid.StartsWith("00000000", StringComparison.Ordinal))
				// Cause what are the chances of having a guid starting with so many zeroes?!
				//|| guid.Equals(INVALID_GUID, StringComparison.Ordinal)
				//|| guid.Equals(ASSETS_FOLDER_GUID, StringComparison.Ordinal)
				return;

			var statusData = SVNStatusesDatabase.Instance.GetKnownStatusData(guid);

			var downloadRepositoryChanges = SVNPreferencesManager.Instance.DownloadRepositoryChanges;
			var autoLocking = SVNPreferencesManager.Instance.ProjectPrefs.EnableAutoLocking;

			//
			// Remote Status
			//
			if (downloadRepositoryChanges && statusData.RemoteStatus != VCRemoteFileStatus.None) {
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
			if ((downloadRepositoryChanges || autoLocking) && statusData.LockStatus != VCLockStatus.NoLock) {
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

						iconRect.y += 2;
					}

					if (GUI.Button(iconRect, lockStatusIcon, EditorStyles.label)) {
						var details = string.Empty;

						foreach (var knownStatusData in SVNStatusesDatabase.Instance.GetAllKnownStatusData(guid, false, true, true)) {
							DateTime date;
							string dateStr = knownStatusData.LockDetails.Date;
							if (!string.IsNullOrEmpty(dateStr)) {
								if (DateTime.TryParse(dateStr, out date) ||
								    // This covers failing to parse weird culture date formats like: 2020-09-08 23:32:13 +0300 (??, 08 ??? 2020)
									DateTime.TryParse(dateStr.Substring(0, dateStr.IndexOf("(", StringComparison.OrdinalIgnoreCase)), out date)
								) {
									dateStr = date.ToString("yyyy-MM-dd hh:mm:ss");
								}
							}
							details += $"File: {System.IO.Path.GetFileName(knownStatusData.Path)}\n" +
							          $"Lock Status: {ObjectNames.NicifyVariableName(knownStatusData.LockStatus.ToString())}\n" +
							          $"Owner: {knownStatusData.LockDetails.Owner}\n" +
							          $"Date: {dateStr}\n" +
							          $"Message:\n{knownStatusData.LockDetails.Message}";
						}
						EditorUtility.DisplayDialog("SVN Lock Details", details, "Ok");
					}
				}
			}


			//
			// File Status
			//
			var fileStatus = statusData.Status;
			switch(statusData.PropertiesStatus) {
				case VCPropertiesStatus.Conflicted:
					fileStatus = VCFileStatus.Conflicted;
					break;
				case VCPropertiesStatus.Modified:
					fileStatus = (fileStatus != VCFileStatus.Conflicted) ? VCFileStatus.Modified : VCFileStatus.Conflicted;
					break;
			}

			if (m_ShowNormalStatusIcons && !statusData.IsValid) {
				fileStatus = VCFileStatus.Normal;
			}

			GUIContent fileStatusIcon = SVNPreferencesManager.Instance.GetFileStatusIconContent(fileStatus);

			// Entries with normal status are present when there is other data to show. Skip the icon if disabled.
			if (!m_ShowNormalStatusIcons && fileStatus == VCFileStatus.Normal) {
				fileStatusIcon = null;
			}

			if (fileStatusIcon != null && fileStatusIcon.image != null) {
				var iconRect = new Rect(selectionRect);
				if (iconRect.width > iconRect.height) {
					// Line size: 16px
					iconRect.x -= 3;
					iconRect.y += 7f;
					iconRect.width = iconRect.height = 14f;
				} else {
					// Maximum zoom size: 96 x 110
					iconRect.width = iconRect.width / 3f + 2f;
					iconRect.height = iconRect.width;
					var offset = selectionRect.width - iconRect.width;
					iconRect.y += offset + 1;
				}
				GUI.Label(iconRect, fileStatusIcon);
			}
		}

	}
}
