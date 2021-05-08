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
	public class GuidStatusDatasBind
	{
		[UnityEngine.Serialization.FormerlySerializedAs("Guid")]
		public string Key;	// Guid or Path (if deleted).

		[UnityEngine.Serialization.FormerlySerializedAs("Data")]
		public SVNStatusData MergedStatusData;	// Merged data

		public SVNStatusData AssetStatusData;
		public SVNStatusData MetaStatusData;

		public string AssetPath => MergedStatusData.Path;

		public IEnumerable<SVNStatusData> GetSourceStatusDatas()
		{
			yield return AssetStatusData;
			yield return MetaStatusData;
		}
	}

	/// <summary>
	/// Caches known statuses for files and folders.
	/// Refreshes periodically or if file was modified or moved.
	/// Status extraction happens in another thread so overhead should be minimal.
	///
	/// NOTE: Keep in mind that this cache can be out of date.
	///		 If you want up to date information, use the WiseSVNIntegration API for direct SVN queries.
	/// </summary>
	public class SVNStatusesDatabase : Utils.DatabasePersistentSingleton<SVNStatusesDatabase, GuidStatusDatasBind>
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
#if UNITY_2018_1_OR_NEWER
		public override bool TemporaryDisabled => WiseSVNIntegration.TemporaryDisabled || Application.isBatchMode || BuildPipeline.isBuildingPlayer;
#else
		public override bool TemporaryDisabled => WiseSVNIntegration.TemporaryDisabled || UnityEditorInternal.InternalEditorUtility.inBatchMode || BuildPipeline.isBuildingPlayer;
#endif
		public override bool DoTraceLogs => (m_PersonalPrefs.TraceLogs & SVNTraceLogs.DatabaseUpdates) != 0;

		public override double RefreshInterval => m_PersonalPrefs.AutoRefreshDatabaseInterval;

		//
		//=============================================================================
		//
		#region Initialize

		public override void Initialize(bool freshlyCreated)
		{
			// HACK: Force WiseSVN initialize first, so it doesn't happen in the thread.
			WiseSVNIntegration.ProjectRootUnity.StartsWith(string.Empty);

			SVNPreferencesManager.Instance.PreferencesChanged += RefreshActive;
			RefreshActive();

			base.Initialize(freshlyCreated);
		}

		#endregion


		//
		//=============================================================================
		//
		#region Populate Data

		private const int SanityStatusesLimit = 600;

		// Executed in a worker thread.
		protected override GuidStatusDatasBind[] GatherDataInThread()
		{
			var statusOptions = new SVNStatusDataOptions() {
				Depth = SVNStatusDataOptions.SearchDepth.Infinity,
				RaiseError = false,
				Timeout = WiseSVNIntegration.ONLINE_COMMAND_TIMEOUT * 2,
				Offline = !SVNPreferencesManager.Instance.DownloadRepositoryChanges && !SVNPreferencesManager.Instance.ProjectPrefs.EnableAutoLocking,
				FetchLockOwner = true,
			};

			// Will get statuses of all added / modified / deleted / conflicted / unversioned files. Only normal files won't be listed.
			var statuses = WiseSVNIntegration.GetStatuses("Assets", statusOptions)
#if UNITY_2018_4_OR_NEWER
				.Concat(WiseSVNIntegration.GetStatuses("Packages", statusOptions))
#endif
				.Where(s => s.Status != VCFileStatus.Missing)
				.ToList();

			for (int i = 0, count = statuses.Count; i < count; ++i) {
				var statusData = statuses[i];

				// Statuses for entries under unversioned directories are not returned. Add them manually.
				if (statusData.Status == VCFileStatus.Unversioned && Directory.Exists(statusData.Path)) {
					try {
						var paths = Directory.EnumerateFileSystemEntries(statusData.Path, "*", SearchOption.AllDirectories)
							.Where(path => !WiseSVNIntegration.IsHiddenPath(path))
							;

						statuses.AddRange(paths
							.Select(path => path.Replace(WiseSVNIntegration.ProjectRootNative, ""))
							.Select(path => new SVNStatusData() { Status = VCFileStatus.Unversioned, Path = path, LockDetails = LockDetails.Empty })
							);
					} catch(Exception) {
						// Files must have changed while scanning. Nothing we can do.
					}
				}
			}

			if (statuses.Count >= SanityStatusesLimit) {
				// If server has many remote changes or locks, don't spam me with overlay icons.
				statuses = statuses.Where(s => s.Status != VCFileStatus.Normal).ToList();
			}

			// HACK: the base class works with the DataType for pending data. Guid won't be used.
			return statuses
				.Where(s => statuses.Count < SanityStatusesLimit	// Include everything when below the limit
				|| s.Status == VCFileStatus.Added
				|| s.Status == VCFileStatus.Modified
				|| s.Status == VCFileStatus.Conflicted
				)
				.Select(s => new GuidStatusDatasBind() { MergedStatusData = s })
				.ToArray();
		}

		protected override void WaitAndFinishDatabaseUpdate(GuidStatusDatasBind[] pendingData)
		{
			// Sanity check!
			if (pendingData.Length > SanityStatusesLimit) {
				Debug.LogWarning($"SVNStatusDatabase gathered {pendingData.Length} changes which is waay to much. Ignoring gathered changes to avoid slowing down the editor!");
				return;
			}

			// Process the gathered statuses in the main thread, since Unity API is not thread-safe.
			foreach (var pair in pendingData) {

				// HACK: Guid is not used here.
				var statusData = pair.MergedStatusData;

				var assetPath = statusData.Path;
				bool isMeta = false;

				// Meta statuses are also considered. They are shown as the asset status.
				if (statusData.Path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) {
					assetPath = statusData.Path.Substring(0, statusData.Path.LastIndexOf(".meta"));
					isMeta = true;
				}

				// Conflicted is with priority.
				if (statusData.IsConflicted) {
					statusData.Status = VCFileStatus.Conflicted;
				}

				var guid = AssetDatabase.AssetPathToGUID(assetPath);
				if (string.IsNullOrEmpty(guid)) {
					// Files were added in the background without Unity noticing.
					// When the user focuses on Unity, it will refresh them as well.
					if (statusData.Status != VCFileStatus.Deleted)
						continue;

					// HACK: Deleted assets don't have guids, but we still want to keep track of them (Auto-Locking for example).
					//		 As long as this is unique it will work.
					guid = assetPath;
				}

				// File was added to the repository but is missing in the working copy.
				// The proper way to check this is to parse the working revision from the svn output (when used with -u)
				if (statusData.RemoteStatus == VCRemoteFileStatus.Modified
					&& statusData.Status == VCFileStatus.Normal
					&& string.IsNullOrEmpty(guid)
					)
					continue;

				SetStatusData(guid, statusData, false, isMeta);

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

				// Folder may be deleted.
				if (string.IsNullOrWhiteSpace(guid))
					return;

				// Added folders should not be shown as modified.
				if (GetKnownStatusData(guid).Status == VCFileStatus.Added)
					return;

				bool moveToNext = SetStatusData(guid, statusData, false, false);

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
				bool isMeta = false;

				// If status is normal but asset was imported, maybe the meta changed. Use that status instead.
				if (statusData.Status == VCFileStatus.Normal && !statusData.IsConflicted) {
					statusData = WiseSVNIntegration.GetStatus(path + ".meta");
					isMeta = true;
				}

				var guid = AssetDatabase.AssetPathToGUID(path);

				// Conflicted file got reimported? Fuck this, just refresh.
				if (statusData.IsConflicted) {
					SetStatusData(guid, statusData, true, isMeta);
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
				bool changed = SetStatusData(guid, statusData, true, isMeta);

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
				if (bind.Key.Equals(guid, StringComparison.Ordinal))
					return bind.MergedStatusData;
			}

			return new SVNStatusData() { Status = VCFileStatus.None };
		}

		public IEnumerable<SVNStatusData> GetAllKnownStatusData(string guid, bool mergedData, bool assetData, bool metaData)
		{
			foreach(var pair in m_Data) {
				if (pair.Key.Equals(guid, StringComparison.Ordinal)) {
					if (mergedData && pair.MergedStatusData.IsValid) yield return pair.MergedStatusData;
					if (assetData && pair.AssetStatusData.IsValid) yield return pair.AssetStatusData;
					if (metaData && pair.MetaStatusData.IsValid) yield return pair.MetaStatusData;

					break;
				}
			}
		}

		public IEnumerable<SVNStatusData> GetAllKnownStatusData(bool mergedData, bool assetData, bool metaData)
		{
			foreach(var pair in m_Data) {
				if (mergedData && pair.MergedStatusData.IsValid) yield return pair.MergedStatusData;
				if (assetData && pair.AssetStatusData.IsValid) yield return pair.AssetStatusData;
				if (metaData && pair.MetaStatusData.IsValid) yield return pair.MetaStatusData;
			}
		}

		private bool SetStatusData(string guid, SVNStatusData statusData, bool skipPriorityCheck, bool isMeta)
		{
			if (string.IsNullOrEmpty(guid)) {
				Debug.LogError($"SVN: Trying to add empty guid for \"{statusData.Path}\" with status {statusData.Status}");
				return false;
			}

			foreach (var bind in m_Data) {
				if (bind.Key.Equals(guid, StringComparison.Ordinal)) {

					if (!isMeta && bind.AssetStatusData.Equals(statusData))
						return false;

					if (isMeta && bind.MetaStatusData.Equals(statusData))
						return false;

					if (!isMeta) {
						bind.AssetStatusData = statusData;
					} else {
						bind.MetaStatusData = statusData;
					}

					// This is needed because the status of the meta might differ. In that case take the stronger status.
					if (!skipPriorityCheck) {
						if (m_StatusPriority[bind.MergedStatusData.Status] > m_StatusPriority[statusData.Status]) {
							// Merge any other data.
							if (bind.MergedStatusData.PropertiesStatus == VCPropertiesStatus.Normal) {
								bind.MergedStatusData.PropertiesStatus = statusData.PropertiesStatus;
							}
							if (bind.MergedStatusData.TreeConflictStatus == VCTreeConflictStatus.Normal) {
								bind.MergedStatusData.TreeConflictStatus = statusData.TreeConflictStatus;
							}
							if (bind.MergedStatusData.SwitchedExternalStatus == VCSwitchedExternal.Normal) {
								bind.MergedStatusData.SwitchedExternalStatus = statusData.SwitchedExternalStatus;
							}
							if (bind.MergedStatusData.LockStatus == VCLockStatus.NoLock) {
								bind.MergedStatusData.LockStatus = statusData.LockStatus;
								bind.MergedStatusData.LockDetails = statusData.LockDetails;
							}
							if (bind.MergedStatusData.RemoteStatus == VCRemoteFileStatus.None) {
								bind.MergedStatusData.RemoteStatus= statusData.RemoteStatus;
							}

							return false;
						}
					}

					bind.MergedStatusData = statusData;
					if (isMeta) {
						bind.MergedStatusData.Path = statusData.Path.Substring(0, statusData.Path.LastIndexOf(".meta"));
					}
					return true;
				}
			}

			m_Data.Add(new GuidStatusDatasBind() {
				Key = guid,
				MergedStatusData = statusData,

				AssetStatusData = isMeta ? new SVNStatusData() : statusData,
				MetaStatusData = isMeta ? statusData : new SVNStatusData(),
			});

			if (isMeta) {
				m_Data.Last().MergedStatusData.Path = statusData.Path.Substring(0, statusData.Path.LastIndexOf(".meta"));
			}

			return true;
		}


		private bool RemoveStatusData(string guid)
		{
			if (string.IsNullOrEmpty(guid)) {
				Debug.LogError($"Trying to remove empty guid");
			}

			for(int i = 0; i < m_Data.Count; ++i) {
				if (m_Data[i].Key.Equals(guid, StringComparison.Ordinal)) {
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
