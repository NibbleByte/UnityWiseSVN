using DevLocker.VersionControl.WiseSVN.Preferences;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
		public const string INVALID_GUID = "00000000000000000000000000000000";
		public const string ASSETS_FOLDER_GUID = "00000000000000001000000000000000";


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
		private SVNPreferencesManager.ProjectPreferences m_ProjectPrefs => SVNPreferencesManager.Instance.ProjectPrefs;

		private SVNPreferencesManager.PersonalPreferences m_PersonalCachedPrefs;
		private SVNPreferencesManager.ProjectPreferences m_ProjectCachedPrefs;
		private bool m_DownloadRepositoryChangesCached = false;


		/// <summary>
		/// The database update can be enabled, but the SVN integration to be disabled as a whole.
		/// </summary>
		public override bool IsActive => m_PersonalPrefs.PopulateStatusesDatabase && m_PersonalPrefs.EnableCoreIntegration;
#if UNITY_2018_1_OR_NEWER
		public override bool TemporaryDisabled => WiseSVNIntegration.TemporaryDisabled || Application.isBatchMode || BuildPipeline.isBuildingPlayer;
#else
		public override bool TemporaryDisabled => WiseSVNIntegration.TemporaryDisabled || UnityEditorInternal.InternalEditorUtility.inBatchMode || BuildPipeline.isBuildingPlayer;
#endif
		public override bool DoTraceLogs => (m_PersonalCachedPrefs.TraceLogs & SVNTraceLogs.DatabaseUpdates) != 0;

		public override double RefreshInterval => m_PersonalPrefs.AutoRefreshDatabaseInterval;

		// Any assets contained in these folders are considered unversioned.
		private string[] m_UnversionedFolders = new string[0];

		// SVN-Ignored files and folders.
		private string[] m_IgnoredEntries = new string[0];
		// SVN-Global-ignored entries are stored separately as they are checked only once, because they are much slower.
		private string[] m_GlobalIgnoredEntries = new string[0];

		public bool m_GlobalIgnoresCollected = false;

		/// <summary>
		/// The collected statuses are not complete due to some reason (for example, they were too many).
		/// </summary>
		public bool DataIsIncomplete { get; private set; }

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

		protected override void RefreshActive()
		{
			base.RefreshActive();

			if (!IsActive) {
				DataIsIncomplete = false;
			}

			// Copy them so they can be safely accessed from the worker thread.
			m_PersonalCachedPrefs = m_PersonalPrefs.Clone();
			m_ProjectCachedPrefs = m_ProjectPrefs.Clone();
			m_DownloadRepositoryChangesCached = SVNPreferencesManager.Instance.DownloadRepositoryChanges;
		}

		#endregion


		//
		//=============================================================================
		//
		#region Populate Data

		private const int SanityStatusesLimit = 600;
		private const int SanityUnversionedFoldersLimit = 250;
		private const int SanityIgnoresLimit = 250;

		// Executed in a worker thread.
		protected override GuidStatusDatasBind[] GatherDataInThread()
		{
			List<SVNStatusData> statuses = new List<SVNStatusData>();
			List<string> unversionedFolders = new List<string>();
			List<string> ignoredEntries = new List<string>();
			List<string> globalIgnoredEntries = new List<string>();

			var timings = new StringBuilder("SVNStatusesDatabase Gathering Data Timings:\n");

			var stopwatch = System.Diagnostics.Stopwatch.StartNew();

			GatherStatusDataInThreadRecursive("Assets", statuses, unversionedFolders);
#if UNITY_2018_4_OR_NEWER
			GatherStatusDataInThreadRecursive("Packages", statuses, unversionedFolders);
#endif

			// Add excluded items explicitly so their icon shows even when "Normal status green icon" is disabled.
			foreach(string excludedPath in m_ProjectCachedPrefs.Exclude) {
				statuses.Add(new SVNStatusData() { Path = excludedPath, Status = VCFileStatus.Excluded, LockDetails = LockDetails.Empty });
			}

			timings.AppendLine("Gather Status Data - " + (stopwatch.ElapsedMilliseconds / 1000f));
			stopwatch.Restart();


			if (m_PersonalCachedPrefs.PopulateIgnoresDatabase) {
				GatherIgnoresInThread("Assets", ignoredEntries);
#if UNITY_2018_4_OR_NEWER
				GatherIgnoresInThread("Packages", ignoredEntries);
#endif
				timings.AppendLine("Gather svn:ignore - " + (stopwatch.ElapsedMilliseconds / 1000f));
				stopwatch.Restart();

				if (!m_GlobalIgnoresCollected) {
					GatherGlobalIgnoresInThread(globalIgnoredEntries);

					timings.AppendLine("Gather svn:global-ignores - " + (stopwatch.ElapsedMilliseconds / 1000f));
					stopwatch.Restart();
				}
			}

			DataIsIncomplete = unversionedFolders.Count >= SanityUnversionedFoldersLimit || statuses.Count >= SanityStatusesLimit || ignoredEntries.Count > SanityIgnoresLimit || globalIgnoredEntries.Count > SanityIgnoresLimit;

			// Just in case...
			if (unversionedFolders.Count >= SanityUnversionedFoldersLimit) {
				unversionedFolders.Clear();
			}

			if (statuses.Count >= SanityStatusesLimit) {
				// If server has many remote changes, don't spam me with overlay icons.
				// Keep showing locked assets or scenes out of date.
				statuses = statuses
					.Where(s => s.Status != VCFileStatus.Normal || s.LockStatus != VCLockStatus.NoLock || s.Path.EndsWith(".unity"))
					.ToList();
			}

			if (ignoredEntries.Count >= SanityIgnoresLimit) {
				ignoredEntries.RemoveRange(SanityIgnoresLimit, ignoredEntries.Count - SanityIgnoresLimit);
			}

			if (globalIgnoredEntries.Count >= SanityIgnoresLimit) {
				globalIgnoredEntries.RemoveRange(SanityIgnoresLimit, globalIgnoredEntries.Count - SanityIgnoresLimit);
			}


			// HACK: the base class works with the DataType for pending data. Guid won't be used.
			GuidStatusDatasBind[] pendingData = statuses
				.Where(s => statuses.Count < SanityStatusesLimit	// Include everything when below the limit
				|| s.Status == VCFileStatus.Added
				|| s.Status == VCFileStatus.Modified
				|| s.Status == VCFileStatus.Conflicted
				|| s.LockStatus != VCLockStatus.NoLock
				|| s.Path.EndsWith(".unity")
				)
				.Select(s => new GuidStatusDatasBind() { MergedStatusData = s })
				.ToArray();

			string projectRootPath = WiseSVNIntegration.ProjectRootNative + '\\';
			m_IgnoredEntries = ignoredEntries
				.Select(path => path.Replace(projectRootPath, ""))
				.Select(path => path.Replace('\\', '/'))
				.Distinct()
				.ToArray();

			if (!m_GlobalIgnoresCollected) {
				m_GlobalIgnoredEntries = globalIgnoredEntries
					.Select(path => path.Replace(projectRootPath, ""))
					.Select(path => path.Replace('\\', '/'))
					.Distinct()
					.ToArray();

				m_GlobalIgnoresCollected = true;
			}

			m_UnversionedFolders = unversionedFolders.ToArray();

			timings.AppendLine("Gather Processing Data - " + (stopwatch.ElapsedMilliseconds / 1000f));
			stopwatch.Restart();

			if (DoTraceLogs) {
				Debug.Log(timings.ToString());
			}

			return pendingData;
		}

		private void GatherStatusDataInThreadRecursive(string repositoryPath, List<SVNStatusData> foundStatuses, List<string> foundUnversionedFolders)
		{
			var statusOptions = new SVNStatusDataOptions() {
				Depth = SVNStatusDataOptions.SearchDepth.Infinity,
				RaiseError = false,
				Timeout = WiseSVNIntegration.ONLINE_COMMAND_TIMEOUT * 2,
				Offline = !m_DownloadRepositoryChangesCached && !m_ProjectCachedPrefs.EnableLockPrompt,
				FetchLockOwner = true,
			};

			// Will get statuses of all added / modified / deleted / conflicted / unversioned files. Only normal files won't be listed.
			var statuses = WiseSVNIntegration.GetStatuses(repositoryPath, statusOptions)
				.Where(s => !m_ProjectCachedPrefs.Exclude.Any(e => s.Path.StartsWith(e, StringComparison.OrdinalIgnoreCase)))
				.Where(s => s.Status != VCFileStatus.Missing)
				.ToList();

			for (int i = 0; i < statuses.Count; ++i) {
				var statusData = statuses[i];

				// Statuses for entries under unversioned directories are not returned so we need to keep track of them.
				if (statusData.Status == VCFileStatus.Unversioned && Directory.Exists(statusData.Path)) {

					// Nested repositories return unknown status, but are hidden in the TortoiseSVN commit window.
					// Add their statuses to support them. Also removing this folder data should display it as normal status.
					if (Directory.Exists($"{statusData.Path}/.svn")) {
						GatherStatusDataInThreadRecursive(statusData.Path, foundStatuses, foundUnversionedFolders);

						// Folder meta file could also be unversioned. This will force unversioned overlay icon to show, even though the folder status is removed.
						// Remove the meta file status as well.
						var metaIndex = statuses.FindIndex(sd => sd.Status == VCFileStatus.Unversioned && sd.Path == statusData.Path + ".meta");
						if (metaIndex != -1) {

							foundStatuses.RemoveAll(sd => sd.Path == statusData.Path + ".meta");

							statuses.RemoveAt(metaIndex);

							if (metaIndex < i) {
								--i;
							}
						}

						statuses.RemoveAt(i);
						--i;
						continue;
					}

					foundUnversionedFolders.Add(statusData.Path);
				}

				foundStatuses.Add(statusData);
			}
		}

		private void GatherIgnoresInThread(string repositoryPath, List<string> foundIgnoredEntries)
		{
			var propgets = new List<PropgetEntry>();

			PropgetOperationResult result = WiseSVNIntegration.Propget(repositoryPath, "svn:ignore", true, propgets);
			if (result != PropgetOperationResult.Success && DoTraceLogs) {
				Debug.LogError($"SVN: Failed to collect svn ignored entries for \"{repositoryPath}\". Type: \"svn:ignore\".");
				return;
			}

			// Keep in mind that "svn:ignore" values may include wildcards (* and ?).
			// Also all ignored entries do not appear in the "svn status" command, which we consider as "normal" status.
			// This is why we need to collect actual ignored files, as there is no good other way to recognize them.
			foreach (PropgetEntry propget in propgets) {
				foreach (string line in propget.Lines) {

					// Skip hidden folders starting with ".". Some people put comments starting with "#".
					// Example: # ---------------[ Unity generated ]------------------ #
					if (line.StartsWith(".", StringComparison.OrdinalIgnoreCase) || line.StartsWith("#", StringComparison.OrdinalIgnoreCase) || line.StartsWith("/", StringComparison.OrdinalIgnoreCase) || line.StartsWith(":", StringComparison.OrdinalIgnoreCase))
						continue;

					var matchedEntries = Directory.EnumerateFileSystemEntries(propget.Path, line, SearchOption.TopDirectoryOnly);
					foundIgnoredEntries.AddRange(matchedEntries);
				}
			}
		}

		private void GatherGlobalIgnoresInThread(List<string> foundIgnoredEntries)
		{
			var propgets = new List<PropgetEntry>();

			PropgetOperationResult result = WiseSVNIntegration.Propget(WiseSVNIntegration.ProjectRootNative, "svn:global-ignores", true, propgets);
			if (result != PropgetOperationResult.Success && DoTraceLogs) {
				Debug.LogError($"SVN: Failed to collect svn ignored entries for \"{WiseSVNIntegration.ProjectRootNative}\". Type: \"svn:global-ignores\".");
				return;
			}

			foreach (PropgetEntry propget in propgets) {

				// Start folder is returned as "."
				if (propget.Path == ".") {

					// Enumerating the root folder would be too expensive (Library is huge). Just enumerate meaningful folders.
					// "svn:global-ignores" are applied recursively to all sub-folders.
					foreach (string line in propget.Lines) {

						// Skip hidden folders starting with ".". Some people put comments starting with "#".
						// Example: # ---------------[ Unity generated ]------------------ #
						if (line.StartsWith(".", StringComparison.OrdinalIgnoreCase) || line.StartsWith("#", StringComparison.OrdinalIgnoreCase) || line.StartsWith("/", StringComparison.OrdinalIgnoreCase) || line.StartsWith(":", StringComparison.OrdinalIgnoreCase))
							continue;

						var matchedEntries = Directory.EnumerateFileSystemEntries("Assets", line, SearchOption.AllDirectories);
						foundIgnoredEntries.AddRange(matchedEntries);
					}

#if UNITY_2018_4_OR_NEWER
					foreach (string line in propget.Lines) {

						// Skip hidden folders starting with ".". Some people put comments starting with "#".
						// Example: # ---------------[ Unity generated ]------------------ #
						if (line.StartsWith(".", StringComparison.OrdinalIgnoreCase) || line.StartsWith("#", StringComparison.OrdinalIgnoreCase) || line.StartsWith("/", StringComparison.OrdinalIgnoreCase) || line.StartsWith(":", StringComparison.OrdinalIgnoreCase))
							continue;

						var matchedEntries = Directory.EnumerateFileSystemEntries("Packages", line, SearchOption.AllDirectories);
						foundIgnoredEntries.AddRange(matchedEntries);
					}
#endif

					continue;
				}

				foreach (string line in propget.Lines) {

					// Skip hidden folders starting with ".". Some people put comments starting with "#".
					// Example: # ---------------[ Unity generated ]------------------ #
					if (line.StartsWith(".", StringComparison.OrdinalIgnoreCase) || line.StartsWith("#", StringComparison.OrdinalIgnoreCase) || line.StartsWith("/", StringComparison.OrdinalIgnoreCase) || line.StartsWith(":", StringComparison.OrdinalIgnoreCase))
						continue;

					var matchedEntries = Directory.EnumerateFileSystemEntries(propget.Path, line, SearchOption.AllDirectories);
					foundIgnoredEntries.AddRange(matchedEntries);
				}
			}
		}

		protected override void WaitAndFinishDatabaseUpdate(GuidStatusDatasBind[] pendingData)
		{
			// Sanity check!
			if (pendingData.Length > SanityStatusesLimit) {
				// No more logging, displaying an icon.
				if (DoTraceLogs) {
					Debug.LogWarning($"SVNStatusDatabase gathered {pendingData.Length} changes which is waay to much. Ignoring gathered changes to avoid slowing down the editor!");
				}

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

					// HACK: Deleted assets don't have guids, but we still want to keep track of them (Lock prompt for example).
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

				SetStatusData(guid, statusData, false, true, isMeta);

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

				bool moveToNext = SetStatusData(guid, statusData, false, true, false);

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
					SetStatusData(guid, statusData, true, false, isMeta);
					InvalidateDatabase();
					return;
				}


				if (statusData.Status == VCFileStatus.Normal) {

					var knownStatusBind = m_Data.FirstOrDefault(b => b.Key == guid) ?? new GuidStatusDatasBind();
					var knownMergedData = knownStatusBind.MergedStatusData;

					// Check if just switched to normal from something else.
					// Normal might be present in the database if it is locked.
					if (knownMergedData.Status != VCFileStatus.None && knownMergedData.Status != VCFileStatus.Normal) {
						if (knownMergedData.LockStatus == VCLockStatus.NoLock && knownMergedData.RemoteStatus == VCRemoteFileStatus.None) {
							RemoveStatusData(guid);
						} else {
							bool knownIsMeta = knownStatusBind.AssetStatusData.Status == VCFileStatus.Normal;	// Meta, not asset.
							knownMergedData = knownIsMeta ? knownStatusBind.MetaStatusData : knownStatusBind.AssetStatusData;
							knownMergedData.Status = VCFileStatus.Normal;

							SetStatusData(guid, knownMergedData, true, false, knownIsMeta);
						}

						InvalidateDatabase();
						return;
					}

					continue;
				}

				// Files inside ignored folder are returned as Unversioned. Check if they are ignored and change the status.
				if (statusData.Status == VCFileStatus.Unversioned) {
					statusData.Status = CheckForIgnoredOrExcludedStatus(statusData.Status, path);
				}

				// Every time the user saves a file it will get reimported. If we already know it is modified, don't refresh every time.
				bool changed = SetStatusData(guid, statusData, true, false, isMeta);

				if (changed) {
					InvalidateDatabase();
					return;
				}
			}
		}

		private VCFileStatus CheckForIgnoredOrExcludedStatus(VCFileStatus originalStatus, string path)
		{
			foreach (string excludedPath in m_ProjectCachedPrefs.Exclude) {
				if (path.StartsWith(excludedPath, StringComparison.OrdinalIgnoreCase)) {
					return VCFileStatus.Excluded;
				}
			}

			foreach (string ignoredPath in m_IgnoredEntries) {
				if (path.StartsWith(ignoredPath, StringComparison.OrdinalIgnoreCase)) {
					return VCFileStatus.Ignored;
				}
			}

			foreach (string ignoredPath in m_GlobalIgnoredEntries) {
				if (path.StartsWith(ignoredPath, StringComparison.OrdinalIgnoreCase)) {
					return VCFileStatus.Ignored;
				}
			}

			return originalStatus;
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
				return new SVNStatusData() { Status = VCFileStatus.None };
			}

			foreach (var bind in m_Data) {
				if (bind.Key.Equals(guid, StringComparison.Ordinal))
					return bind.MergedStatusData;
			}

			string path = null;
			if (m_UnversionedFolders.Length > 0) {
				path = AssetDatabase.GUIDToAssetPath(guid);

				foreach (string unversionedFolder in m_UnversionedFolders) {
					if (path.StartsWith(unversionedFolder, StringComparison.OrdinalIgnoreCase))
						return new SVNStatusData() { Path = path, Status = VCFileStatus.Unversioned, LockDetails = LockDetails.Empty };
				}
			}

			if (m_IgnoredEntries.Length > 0) {
				path = path ?? AssetDatabase.GUIDToAssetPath(guid);

				foreach (string ignoredPath in m_IgnoredEntries) {
					if (path.StartsWith(ignoredPath, StringComparison.OrdinalIgnoreCase)) {
						return new SVNStatusData() { Path = path, Status = VCFileStatus.Ignored, LockDetails = LockDetails.Empty };
					}
				}
			}

			if (m_GlobalIgnoredEntries.Length > 0) {
				path = path ?? AssetDatabase.GUIDToAssetPath(guid);

				foreach (string ignoredPath in m_GlobalIgnoredEntries) {
					if (path.StartsWith(ignoredPath, StringComparison.OrdinalIgnoreCase)) {
						return new SVNStatusData() { Path = path, Status = VCFileStatus.Ignored, LockDetails = LockDetails.Empty };
					}
				}
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

		private bool SetStatusData(string guid, SVNStatusData statusData, bool skipPriorityCheck, bool compareOnlineStatuses, bool isMeta)
		{
			if (string.IsNullOrEmpty(guid)) {
				Debug.LogError($"SVN: Trying to add empty guid for \"{statusData.Path}\" with status {statusData.Status}");
				return false;
			}

			foreach (var bind in m_Data) {
				if (bind.Key.Equals(guid, StringComparison.Ordinal)) {

					if (!isMeta && bind.AssetStatusData.Equals(statusData, !compareOnlineStatuses))
						return false;

					if (isMeta && bind.MetaStatusData.Equals(statusData, !compareOnlineStatuses))
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

					// Merged should always display lock and remote status.
					if (statusData.LockStatus == VCLockStatus.NoLock) {
						statusData.LockStatus = bind.MergedStatusData.LockStatus;
						statusData.LockDetails = bind.MergedStatusData.LockDetails;
					}
					if (statusData.RemoteStatus == VCRemoteFileStatus.None) {
						statusData.RemoteStatus= bind.MergedStatusData.RemoteStatus;
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
