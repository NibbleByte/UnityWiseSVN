// MIT License Copyright(c) 2022 Filip Slavov, https://github.com/NibbleByte/UnityWiseSVN

using DevLocker.VersionControl.WiseSVN.Preferences;
using UnityEditor;

namespace DevLocker.VersionControl.WiseSVN.LockPrompting
{
	/// <summary>
	/// Starts the database if enabled.
	/// </summary>
	[InitializeOnLoad]
	internal static class SVNLockPromptDatabaseStarter
	{
		// HACK: If this was the SVNAutoLockingDatabase itself it causes exceptions on assembly reload.
		//		 The static constructor gets called during reload because the instance exists.
		static SVNLockPromptDatabaseStarter()
		{
			TryStartIfNeeded();
		}

		internal static void TryStartIfNeeded()
		{
			var playerPrefs = SVNPreferencesManager.Instance.PersonalPrefs;
			var projectPrefs = SVNPreferencesManager.Instance.ProjectPrefs;

			// HACK: Just touch the SVNAutoLockingDatabase instance to initialize it.
			if (playerPrefs.EnableCoreIntegration && projectPrefs.EnableLockPrompt && SVNLockPromptDatabase.Instance.IsActive)
				return;
		}
	}
}
