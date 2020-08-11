using DevLocker.VersionControl.WiseSVN.Preferences;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DevLocker.VersionControl.WiseSVN
{
	// HACK: This should be internal, but due to inheritance issues it can't be.
	[Serializable]
	public class GuidStatusDataBind
	{
		public string Guid;
		public SVNStatusData Data;
	}

	/// <summary>
	/// Caches known statuses for files and folders.
	/// Refreshes periodically or if file was modified or moved.
	/// Status extraction happens in another thread so overhead should be minimal.
	///
	/// NOTE: Keep in mind that this cache can be out of date.
	///		 If you want up to date information, use the WiseSVNIntegration API for direct SVN queries.
	/// </summary>
	public class SVNStatusesDatabase : Utils.DatabasePersistentSingleton<SVNStatusesDatabase, GuidStatusDataBind>
	{
		private const string INVALID_GUID = "00000000000000000000000000000000";
		private const string ASSETS_FOLDER_GUID = "00000000000000001000000000000000";


		// Note: not all of these are rendered. Check the Database icons.
		private readonly static Dictionary<VCFileStatus, int> m_StatusPriority = new Dictionary<VCFileStatus, int> {
			{ VCFileStatus.Conflicted, 10 },
			{ VCFileStatus.Obstructed, 10 },
			{ VCFileStatus.Modified, 8},
			{ VCFileStatus.Added, 6},
			{ VCFileStatus.Deleted, 6},
			{ VCFileStatus.Missing, 6},
			{ VCFileStatus.Replaced, 5},
			{ VCFileStatus.Ignored, 3},
			{ VCFileStatus.Unversioned, 1},
			{ VCFileStatus.External, 0},
			{ VCFileStatus.Normal, 0},
		};

		private SVNPreferencesManager.PersonalPreferences m_PersonalPrefs => SVNPreferencesManager.Instance.PersonalPrefs;

		/// <summary>
		/// The database update can be enabled, but the SVN integration to be disabled as a whole.
		/// </summary>
		public override bool IsActive => m_PersonalPrefs.PopulateStatusesDatabase && m_PersonalPrefs.EnableCoreIntegration;
		public override bool TemporaryDisabled => WiseSVNIntegration.TemporaryDisabled;
		public override bool DoTraceLogs => (m_PersonalPrefs.TraceLogs & SVNTraceLogs.DatabaseUpdates) != 0;

		public override double RefreshInterval => m_PersonalPrefs.AutoRefreshDatabaseInterval;

		//
		//=============================================================================
		//
		#region Initialize

		public override void Initialize(bool freshlyCreated)
		{
			// HACK: Force WiseSVN initialize first, so it doesn't happen in the thread.
			WiseSVNIntegration.ProjectRoot.StartsWith(string.Empty);

			SVNPreferencesManager.Instance.PreferencesChanged += RefreshActive;
			RefreshActive();

			base.Initialize(freshlyCreated);
		}

		#endregion


		//
		//=============================================================================
		//
		#region Populate Data

		// Executed in a worker thread.
		protected override GuidStatusDataBind[] GatherDataInThread()
		{
			var statusOptions = new SVNStatusDataOptions() {
				Depth = SVNStatusDataOptions.SearchDepth.Infinity,
				RaiseError = false,
				Timeout = WiseSVNIntegration.ONLINE_COMMAND_TIMEOUT * 2,
				Offline = !SVNPreferencesManager.Instance.DownloadRepositoryChanges,
				FetchLockOwner = true,
			};

			// Will get statuses of all added / modified / deleted / conflicted / unversioned files. Only normal files won't be listed.
			var statuses = WiseSVNIntegration.GetStatuses("Assets", statusOptions)
#if UNITY_2018_4_OR_NEWER
				.Concat(WiseSVNIntegration.GetStatuses("Packages", statusOptions))
#endif
				// Deleted svn file can still exist for some reason. Need to show it as deleted.
				// If file doesn't exists, skip it as we can't show it anyway.
				.Where(s => s.Status != VCFileStatus.Deleted || File.Exists(s.Path))
				.Where(s => s.Status != VCFileStatus.Missing)
				.ToList();

			for (int i = 0, count = statuses.Count; i < count; ++i) {
				var statusData = statuses[i];

				// Statuses for entries under unversioned directories are not returned. Add them manually.
				if (statusData.Status == VCFileStatus.Unversioned && Directory.Exists(statusData.Path)) {
					var paths = Directory.EnumerateFileSystemEntries(statusData.Path, "*", SearchOption.AllDirectories)
						.Where(path => !WiseSVNIntegration.IsHiddenPath(path))
						;

					statuses.AddRange(paths
						.Select(path => path.Replace(WiseSVNIntegration.ProjectRoot, ""))
						.Select(path => new SVNStatusData() { Status = VCFileStatus.Unversioned, Path = path, LockDetails = LockDetails.Empty })
						);
				}
			}

			// HACK: the base class works with the DataType for pending data. Guid won't be used.
			return statuses
				.Select(s => new GuidStatusDataBind() { Data = s })
				.ToArray();
		}

		protected override void WaitAndFinishDatabaseUpdate(GuidStatusDataBind[] pendingData)
		{
			// Process the gathered statuses in the main thread, since Unity API is not thread-safe.
			foreach (var pair in pendingData) {

				// HACK: Guid is not used here.
				var foundStatusData = pair.Data;

				// Because structs can't be modified in foreach.
				var statusData = foundStatusData;

				// Meta statuses are also considered. They are shown as the asset status.
				if (statusData.Path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) {
					statusData.Path = statusData.Path.Substring(0, statusData.Path.LastIndexOf(".meta"));
				}

				// Conflicted is with priority.
				if (statusData.IsConflicted) {
					statusData.Status = VCFileStatus.Conflicted;
				}

				var guid = AssetDatabase.AssetPathToGUID(statusData.Path);
				if (string.IsNullOrEmpty(guid)) {
					// Files were added in the background without Unity noticing.
					// When the user focuses on Unity, it will refresh them as well.
					continue;
				}

				// File was added to the repository but is missing in the working copy.
				// The proper way to check this is to parse the working revision from the svn output (when used with -u)
				if (statusData.RemoteStatus == VCRemoteFileStatus.Modified
					&& statusData.Status == VCFileStatus.Normal
					&& string.IsNullOrEmpty(guid)
					)
					continue;

				// TODO: Test tree conflicts.
				SetStatusData(guid, statusData, false);

				AddModifiedFolders(statusData);
			}
		}

		private void AddModifiedFolders(SVNStatusData statusData)
		{
			var status = statusData.Status;
			if (status == VCFileStatus.Unversioned || status == VCFileStatus.Ignored || status == VCFileStatus.Normal)
				return;

			if (statusData.IsConflicted) {
				statusData.Status = VCFileStatus.Conflicted;
			} else if (status != VCFileStatus.Modified) {
				statusData.Status = VCFileStatus.Modified;
			}

			// Folders don't have locks.
			statusData.LockStatus = VCLockStatus.NoLock;

			statusData.Path = Path.GetDirectoryName(statusData.Path);

			while (!string.IsNullOrEmpty(statusData.Path)) {
				// "Packages" folder doesn't have valid guid. "Assets" do have a special guid.
				if (statusData.Path == "Packages")
					break;

				var guid = AssetDatabase.AssetPathToGUID(statusData.Path);

				// Added folders should not be shown as modified.
				if (GetKnownStatusData(guid).Status == VCFileStatus.Added)
					return;

				bool moveToNext = SetStatusData(guid, statusData, false);

				// If already exists, upper folders should be added as well.
				if (!moveToNext)
					return;

				statusData.Path = Path.GetDirectoryName(statusData.Path);
			}
		}

		#endregion


		//
		//=============================================================================
		//
		#region Invalidate Database

		internal void PostProcessAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets)
		{
			if (!IsActive)
				return;

			if (deletedAssets.Length > 0 || movedAssets.Length > 0) {
				InvalidateDatabase();
				return;
			}

			// It will probably be faster.
			if (importedAssets.Length > 10) {
				InvalidateDatabase();
				return;
			}

			foreach (var path in importedAssets) {

				// ProjectSettings, Packages are imported too but we're not interested.
				if (!path.StartsWith("Assets", StringComparison.Ordinal))
					continue;

				var statusData = WiseSVNIntegration.GetStatus(path);

				// If status is normal but asset was imported, maybe the meta changed. Use that status instead.
				if (statusData.Status == VCFileStatus.Normal && !statusData.IsConflicted) {
					statusData = WiseSVNIntegration.GetStatus(path + ".meta");
					statusData.Path = path;
				}

				var guid = AssetDatabase.AssetPathToGUID(path);

				// Conflicted file got reimported? Fuck this, just refresh.
				if (statusData.IsConflicted) {
					SetStatusData(guid, statusData, true);
					InvalidateDatabase();
					return;
				}


				if (statusData.Status == VCFileStatus.Normal) {

					// Check if just switched to normal from something else.
					var knownStatusData = GetKnownStatusData(guid);
					// Normal might be present in the database if it is locked.
					if (knownStatusData.Status != VCFileStatus.None && knownStatusData.Status != VCFileStatus.Normal) {
						RemoveStatusData(guid);
						InvalidateDatabase();
						return;
					}

					continue;
				}

				// Every time the user saves a file it will get reimported. If we already know it is modified, don't refresh every time.
				bool changed = SetStatusData(guid, statusData, true);

				if (changed) {
					InvalidateDatabase();
					return;
				}
			}
		}

		#endregion


		//
		//=============================================================================
		//
		#region Manage status data

		/// <summary>
		/// Get known status for guid.
		/// Unversioned files should return unversioned status.
		/// If status is not known, the file should be versioned unmodified or still undetected.
		/// </summary>
		public SVNStatusData GetKnownStatusData(string guid)
		{
			if (string.IsNullOrEmpty(guid)) {
				Debug.LogError($"Asking for status with empty guid");
			}

			foreach (var bind in m_Data) {
				if (bind.Guid.Equals(guid, StringComparison.Ordinal))
					return bind.Data;
			}

			return new SVNStatusData() { Status = VCFileStatus.None };
		}

		private bool SetStatusData(string guid, SVNStatusData statusData, bool skipPriorityCheck)
		{
			if (string.IsNullOrEmpty(guid)) {
				Debug.LogError($"SVN: Trying to add empty guid for \"{statusData.Path}\" with status {statusData.Status}");
				return false;
			}

			foreach (var bind in m_Data) {
				if (bind.Guid.Equals(guid, StringComparison.Ordinal)) {

					if (bind.Data.Equals(statusData))
						return false;

					// This is needed because the status of the meta might differ. In that case take the stronger status.
					if (!skipPriorityCheck) {
						if (m_StatusPriority[bind.Data.Status] > m_StatusPriority[statusData.Status]) {
							// Merge any other data.
							if (bind.Data.LockStatus == VCLockStatus.NoLock) {
								bind.Data.LockStatus = statusData.LockStatus;
							}
							if (bind.Data.RemoteStatus == VCRemoteFileStatus.None) {
								bind.Data.RemoteStatus= statusData.RemoteStatus;
							}

							return false;
						}
					}

					bind.Data = statusData;
					return true;
				}
			}

			m_Data.Add(new GuidStatusDataBind() { Guid = guid, Data = statusData });
			return true;
		}


		private bool RemoveStatusData(string guid)
		{
			if (string.IsNullOrEmpty(guid)) {
				Debug.LogError($"Trying to remove empty guid");
			}

			for(int i = 0; i < m_Data.Count; ++i) {
				if (m_Data[i].Guid.Equals(guid, StringComparison.Ordinal)) {
					m_Data.RemoveAt(i);
					return true;
				}
			}

			return false;
		}
		#endregion
	}


	internal class SVNStatusesDatabaseAssetPostprocessor : AssetPostprocessor
	{
		private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
		{
			if (!WiseSVNIntegration.TemporaryDisabled) {
				SVNStatusesDatabase.Instance.PostProcessAssets(importedAssets, deletedAssets, movedAssets);
			}
		}
	}
}
