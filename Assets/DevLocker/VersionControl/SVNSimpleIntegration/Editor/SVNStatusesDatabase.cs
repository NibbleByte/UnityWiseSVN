using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DevLocker.VersionControl.SVN
{
	internal class SVNStatusesDatabase : ScriptableObject
	{
		private const string INVALID_GUID = "00000000000000000000000000000000";
		private const string ASSETS_FOLDER_GUID = "00000000000000001000000000000000";

		[Serializable]
		private class GuidStatusDataBind
		{
			public string Guid;
			public SVNStatusData Data;
		}

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
			{ VCFileStatus.Normal, 0},
		};

		private SVNPreferencesManager.PersonalPreferences m_PersonalPrefs => SVNPreferencesManager.Instance.PersonalPrefs;

		// The database update can be enabled, but the SVN integration to be disabled as a whole.
		public bool IsActive => m_PersonalPrefs.PopulateStatusesDatabase && m_PersonalPrefs.EnableCoreIntegration;
		private bool DoTraceLogs => (m_PersonalPrefs.TraceLogs & SVNTraceLogs.DatabaseUpdates) != 0;


		[SerializeField] private List<GuidStatusDataBind> StatusDatas = new List<GuidStatusDataBind>();
		private double m_LastRefreshTime;   // TODO: Maybe serialize this?

		public event Action DatabaseChanged;


		#region Thread Work Related Data
		// Filled in by a worker thread.
		[NonSerialized] private SVNStatusData[] m_PendingStatuses;

		private System.Threading.Thread m_WorkerThread;

		// Is update pending?
		// If last update didn't make it, this flag will still be true.
		// Useful if assembly reload happens and stops the work of the database update.
		[SerializeField] private bool PendingUpdate = false;
		#endregion

		//
		//=============================================================================
		//
		#region Initialize & Preferences

		private static SVNStatusesDatabase m_Instance;
		public static SVNStatusesDatabase Instance {
			get {
				if (m_Instance == null) {
					m_Instance = Resources.FindObjectsOfTypeAll<SVNStatusesDatabase>().FirstOrDefault();

					bool freshlyCreated = false;
					if (m_Instance == null) {

						m_Instance = ScriptableObject.CreateInstance<SVNStatusesDatabase>();
						m_Instance.name = "SVNStatusesDatabase";

						// Setting this flag will tell Unity NOT to destroy this object on assembly reload (as no scene references this object).
						// We're essentially leaking this object. But we can still find it with Resources.FindObjectsOfTypeAll() after reload.
						// More info on this: https://blogs.unity3d.com/2012/10/25/unity-serialization/
						m_Instance.hideFlags = HideFlags.HideAndDontSave;

						freshlyCreated = true;

						if (m_Instance.DoTraceLogs) {
							Debug.Log("SVNStatusesDatabase not found. Creating new one.");
						}

					} else {
						// Data is already deserialized by Unity onto the scriptable object.
						// Even though OnEnable is not yet called, data is there after assembly reload.
						// It is deserialized even before static constructors [InitializeOnLoad] are called. I tested it! :D

						// The idea here is to save some time on assembly reload from deserializing json as the reload is already slow enough for big projects.
					}

					m_Instance.Initialize(freshlyCreated);
				}

				return m_Instance;
			}
		}

		private void Initialize(bool freshlyCreated)
		{
			AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;

			// HACK: Force SVNSimpleIntegration initialize first, so it doesn't happen in the thread.
			SVNSimpleIntegration.ProjectRoot.StartsWith(string.Empty);

			SVNPreferencesManager.Instance.PreferencesChanged += PreferencesChanged;
			PreferencesChanged();

			// Assembly reload might have killed the working thread leaving pending update.
			// Do it again.
			if (PendingUpdate) {
				StartDatabaseUpdate();
			}

			if (freshlyCreated) {
				InvalidateDatabase();
			}
		}

		private void OnBeforeAssemblyReload()
		{
			// Do it before Unity does it. Cause Unity aborts the thread badly sometimes :(
			if (m_WorkerThread != null && m_WorkerThread.IsAlive) {
				m_WorkerThread.Abort();
			}
		}

		private void PreferencesChanged()
		{
			if (IsActive) {
				EditorApplication.update -= AutoRefresh;
				EditorApplication.update += AutoRefresh;

				m_LastRefreshTime = EditorApplication.timeSinceStartup;

			} else {
				EditorApplication.update -= AutoRefresh;
			}
		}

		#endregion


		//
		//=============================================================================
		//
		#region Populate Data

		private void StartDatabaseUpdate()
		{
			if (DoTraceLogs) {
				Debug.Log($"Started Update Database at {EditorApplication.timeSinceStartup:0.00}");
			}

			if (m_WorkerThread != null) {
				throw new Exception("SVN starting database update, while another one is pending?");
			}

			PendingUpdate = true;

			// Listen for the thread result in the main thread.
			// Just in case, remove previous updates.
			EditorApplication.update -= WaitAndFinishDatabaseUpdate;
			EditorApplication.update += WaitAndFinishDatabaseUpdate;

			m_WorkerThread = new System.Threading.Thread(GatherSVNStatuses);
			m_WorkerThread.Start();
		}

		// Executed in a worker thread.
		private void GatherSVNStatuses()
		{
			try {
				var statusOptions = new SVNStatusDataOptions() {
					Depth = SVNStatusDataOptions.SearchDepth.Infinity,
					RaiseError = false,
					Timeout = SVNSimpleIntegration.COMMAND_TIMEOUT * 2,
					Offline = !SVNPreferencesManager.Instance.DownloadRepositoryChanges,
				};

				// Will get statuses of all added / modified / deleted / conflicted / unversioned files. Only normal files won't be listed.
				var statuses = SVNSimpleIntegration.GetStatuses("Assets", statusOptions)
					// Deleted svn file can still exist for some reason. Need to show it as deleted.
					// If file doesn't exists, skip it as we can't show it anyway.
					.Where(s => s.Status != VCFileStatus.Deleted || File.Exists(s.Path))
					.Where(s => s.Status != VCFileStatus.Missing)
					.ToList();

				for (int i = 0, count = statuses.Count; i < count; ++i) {
					var statusData = statuses[i];

					// Statuses for entries under unversioned directories are not returned. Add them manually.
					if (statusData.Status == VCFileStatus.Unversioned && Directory.Exists(statusData.Path)) {
						var paths = Directory.EnumerateFileSystemEntries(statusData.Path, "*", SearchOption.AllDirectories);
						statuses.AddRange(paths
							.Select(path => path.Replace(SVNSimpleIntegration.ProjectRoot, ""))
							.Select(path => new SVNStatusData() { Status = VCFileStatus.Unversioned, Path = path })
							);
					}
				}

				if (PendingUpdate == false) {
					throw new Exception("SVN thread finished work but the update is over?");
				}

				m_PendingStatuses = statuses.ToArray();

			}
			// Most probably the assembly got reloaded and the thread was aborted.
			catch (System.Threading.ThreadAbortException) {
				System.Threading.Thread.ResetAbort();

				// Should always be true.
				if (PendingUpdate) {
					m_PendingStatuses = new SVNStatusData[0];
				}
			} catch (Exception ex) {
				Debug.LogException(ex);

				// Should always be true.
				if (PendingUpdate) {
					m_PendingStatuses = new SVNStatusData[0];
				}
			}
		}

		private void WaitAndFinishDatabaseUpdate()
		{
			if (m_PendingStatuses == null)
				return;

			if (DoTraceLogs) {
				Debug.Log($"Finished Update Database at {EditorApplication.timeSinceStartup:0.00}");
			}

			EditorApplication.update -= WaitAndFinishDatabaseUpdate;
			m_WorkerThread = null;

			var statuses = m_PendingStatuses;
			m_PendingStatuses = null;
			StatusDatas.Clear();

			// Mark update as finished.
			PendingUpdate = false;

			// If preferences were changed while waiting.
			if (!IsActive)
				return;

			// Process the gathered statuses in the main thread, since Unity API is not thread-safe.
			foreach (var foundStatusData in statuses) {

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

				// File was added to the repository but is missing in the working copy.
				// The proper way to check this is to parse the working revision from the svn output (when used with -u)
				if (statusData.RemoteStatus == VCRemoteFileStatus.Modified
					&& statusData.Status == VCFileStatus.Normal
					&& string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(statusData.Path))
					)
					continue;

				// TODO: Test tree conflicts.
				SetStatusData(AssetDatabase.AssetPathToGUID(statusData.Path), statusData, false);

				AddModifiedFolders(statusData);
			}

			DatabaseChanged?.Invoke();
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

		public void InvalidateDatabase()
		{
			if (!IsActive || PendingUpdate || SVNSimpleIntegration.TemporaryDisabled)
				return;

			// Will be done on assembly reload.
			if (EditorApplication.isCompiling) {
				PendingUpdate = true;
				return;
			}

			StartDatabaseUpdate();
		}

		private void AutoRefresh()
		{
			double refreshInterval = m_PersonalPrefs.AutoRefreshDatabaseInterval;

			if (refreshInterval <= 0 || EditorApplication.timeSinceStartup - m_LastRefreshTime < refreshInterval)
				return;

			m_LastRefreshTime = EditorApplication.timeSinceStartup;

			InvalidateDatabase();
		}

		internal void PostProcessAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets)
		{
			if (!IsActive)
				return;

			if (deletedAssets.Length > 0 || movedAssets.Length > 0) {
				InvalidateDatabase();
				return;
			}

			// It will probably be faster.
			if (importedAssets.Length > 20) {
				InvalidateDatabase();
				return;
			}

			foreach (var path in importedAssets) {

				// ProjectSettings, Packages are imported too but we're not interested.
				if (!path.StartsWith("Assets", StringComparison.Ordinal))
					continue;

				var statusData = SVNSimpleIntegration.GetStatus(path);

				// If status is normal but asset was imported, maybe the meta changed. Use that status instead.
				if (statusData.Status == VCFileStatus.Normal && !statusData.IsConflicted) {
					statusData = SVNSimpleIntegration.GetStatus(path + ".meta");
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
		public SVNStatusData GetKnownStatusData(string guid)
		{
			if (string.IsNullOrEmpty(guid)) {
				Debug.LogError($"Asking for status with empty guid");
			}

			foreach (var bind in StatusDatas) {
				if (bind.Guid.Equals(guid, StringComparison.Ordinal))
					return bind.Data;
			}

			return new SVNStatusData() { Status = VCFileStatus.None };
		}

		private bool SetStatusData(string guid, SVNStatusData statusData, bool skipPriorityCheck)
		{
			if (string.IsNullOrEmpty(guid)) {
				Debug.LogError($"SVN: Trying to add empty guid for \"{statusData.Path}\" with status {statusData.Status}");
			}

			foreach (var bind in StatusDatas) {
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

			StatusDatas.Add(new GuidStatusDataBind() { Guid = guid, Data = statusData });
			return true;
		}


		private bool RemoveStatusData(string guid)
		{
			if (string.IsNullOrEmpty(guid)) {
				Debug.LogError($"Trying to remove empty guid");
			}

			for(int i = 0; i < StatusDatas.Count; ++i) {
				if (StatusDatas[i].Guid.Equals(guid, StringComparison.Ordinal)) {
					StatusDatas.RemoveAt(i);
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
			SVNStatusesDatabase.Instance.PostProcessAssets(importedAssets, deletedAssets, movedAssets);
		}
	}
}
