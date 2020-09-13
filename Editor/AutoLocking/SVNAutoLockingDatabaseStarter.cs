using DevLocker.VersionControl.WiseSVN.Preferences;
using UnityEditor;

namespace DevLocker.VersionControl.WiseSVN.AutoLocking
{
	/// <summary>
	/// Starts the database if enabled.
	/// </summary>
	[InitializeOnLoad]
	public static class SVNAutoLockingDatabaseStarter
	{
		// HACK: If this was the SVNAutoLockingDatabase itself it causes exceptions on assembly reload.
		//		 The static constructor gets called during reload because the instance exists.
		static SVNAutoLockingDatabaseStarter()
		{
			TryStartIfNeeded();
		}

		internal static void TryStartIfNeeded()
		{
			var playerPrefs = SVNPreferencesManager.Instance.PersonalPrefs;
			var projectPrefs = SVNPreferencesManager.Instance.ProjectPrefs;

			// HACK: Just touch the SVNAutoLockingDatabase instance to initialize it.
			if (playerPrefs.EnableCoreIntegration && projectPrefs.EnableAutoLocking && SVNAutoLockingDatabase.Instance.IsActive)
				return;
		}
	}
}
