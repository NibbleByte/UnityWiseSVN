using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
		private const string INVALID_GUID = "00000000000000000000000000000000";
		private const string ASSETS_FOLDER_GUID = "00000000000000001000000000000000";
		private static SVNOverlayIconsDatabase m_Database;

		public static bool Enabled { get; private set; }
		public static bool CheckLockStatus { get; private set; }
		public static double AutoRefreshInterval { get; private set; } // seconds; Less than 0 will disable it.
		private static double m_LastRefreshTime;

		// The Overlay icons can be enabled, but the SVN integration to be disabled as a whole.
		private static bool IsActive => Enabled && SVNSimpleIntegration.Enabled;

		private static bool DoTraceLogs => (SVNSimpleIntegration.TraceLogs & SVNTraceLogs.OverlayIcons) != 0;

		// Filled in by a worker thread.
		private static SVNStatusData[] m_PendingStatuses;

		private static System.Threading.Thread m_WorkerThread;

		static SVNOverlayIcons()
		{
			Enabled = EditorPrefs.GetBool("SVNOverlayIcons", true);
			CheckLockStatus = EditorPrefs.GetBool("SVNCheckLockStatus", false);
			AutoRefreshInterval = EditorPrefs.GetInt("SVNOverlayIconsRefreshInverval", 60);

			AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;

			// NOTE: This checks SVNSimpleIntegration.Enabled which is set by its static constructor.
			// This might cause a race condition, but C# says it will call them in the right order. Hope this is true.
			PreferencesChanged();

			// Assembly reload might have killed the working thread leaving pending update.
			// Do it again.
			if (m_Database && m_Database.PendingUpdate) {
				StartDatabaseUpdate();
			}
		}

		private static void OnBeforeAssemblyReload()
		{
			// Do it before Unity does it. Cause Unity aborts the thread badly sometimes :(
			if (m_WorkerThread != null && m_WorkerThread.IsAlive) {
				m_WorkerThread.Abort();
			}
		}

		public static void SavePreferences(bool enabled, bool checkLockStatus, double autoRefreshInverval)
		{
			Enabled = enabled;
			CheckLockStatus = checkLockStatus;
			AutoRefreshInterval = autoRefreshInverval;

			EditorPrefs.SetBool("SVNOverlayIcons", Enabled);
			EditorPrefs.SetBool("SVNCheckLockStatus", CheckLockStatus);
			EditorPrefs.SetInt("SVNOverlayIconsRefreshInverval", (int) AutoRefreshInterval);

			PreferencesChanged();
		}

		private static void PreferencesChanged()
		{
			if (IsActive) {

				m_Database = Resources.FindObjectsOfTypeAll<SVNOverlayIconsDatabase>().FirstOrDefault();
				if (m_Database == null) {

					if (DoTraceLogs) {
						Debug.Log("SVNOverlayIconsDatabase not found. Creating new one.");
					}

					m_Database = ScriptableObject.CreateInstance<SVNOverlayIconsDatabase>();
					m_Database.name = "SVNOverlayIconsDatabase";

					// Setting this flag will tell Unity NOT to destroy this object on assembly reload (as no scene references this object).
					// We're essentially leaking this object. But we can still find it with Resources.FindObjectsOfTypeAll() after reload.
					// More info on this: https://blogs.unity3d.com/2012/10/25/unity-serialization/
					m_Database.hideFlags = HideFlags.HideAndDontSave;

					InvalidateDatabase();
				}

				EditorApplication.projectWindowItemOnGUI -= ItemOnGUI;
				EditorApplication.projectWindowItemOnGUI += ItemOnGUI;

				EditorApplication.update -= AutoRefresh;
				EditorApplication.update += AutoRefresh;

				m_LastRefreshTime = EditorApplication.timeSinceStartup;

			} else {
				if (m_Database) {
					UnityEngine.Object.DestroyImmediate(m_Database);
					m_Database = null;
				}

				EditorApplication.projectWindowItemOnGUI -= ItemOnGUI;
				EditorApplication.update -= AutoRefresh;
			}
		}

		[MenuItem("Assets/SVN/Refresh Overlay Icons", false, 195)]
		public static void InvalidateDatabase()
		{
			if (!IsActive || m_Database.PendingUpdate || SVNSimpleIntegration.TemporaryDisabled)
				return;

			m_Database.PendingUpdate = true;

			// Will be done on assembly reload.
			if (EditorApplication.isCompiling)
				return;

			StartDatabaseUpdate();
		}

		private static void ItemOnGUI(string guid, Rect selectionRect)
		{
			if (string.IsNullOrEmpty(guid) || guid.StartsWith("00000000", StringComparison.Ordinal))
				// Cause what are the chances of having a guid starting with so many zeroes?!
				//|| guid.Equals(INVALID_GUID, StringComparison.Ordinal)
				//|| guid.Equals(ASSETS_FOLDER_GUID, StringComparison.Ordinal)
				return;

			var statusData = m_Database.GetKnownStatusData(guid);

			//
			// Remote Status
			//
			if (CheckLockStatus && statusData.RemoteStatus != VCRemoteFileStatus.None) {
				var remoteStatusIcon = m_Database.GetRemoteStatusIconContent(statusData.RemoteStatus);

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
			if (CheckLockStatus && statusData.LockStatus != VCLockStatus.NoLock) {
				var lockStatusIcon = m_Database.GetLockStatusIconContent(statusData.LockStatus);

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
			GUIContent fileStatusIcon = m_Database.GetFileStatusIconContent(statusData.Status);

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

		
		internal static void PostProcessAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets)
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

			foreach(var path in importedAssets) {

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
					m_Database.SetStatusData(guid, statusData, true);
					InvalidateDatabase();
					return;
				}


				if (statusData.Status == VCFileStatus.Normal) {

					// Check if just switched to normal from something else.
					var knownStatusData = m_Database.GetKnownStatusData(guid);
					// Normal might be present in the database if it is locked.
					if (knownStatusData.Status != VCFileStatus.None && knownStatusData.Status != VCFileStatus.Normal) {
						m_Database.RemoveStatusData(guid);
						InvalidateDatabase();
						return;
					}

					continue;
				}

				// Every time the user saves a file it will get reimported. If we already know it is modified, don't refresh every time.
				bool changed = m_Database.SetStatusData(guid, statusData, true);

				if (changed) {
					InvalidateDatabase();
					return;
				}
			}
		}

		private static void StartDatabaseUpdate()
		{
			if (DoTraceLogs) {
				Debug.Log($"Started Update Database at {EditorApplication.timeSinceStartup:0.00}");
			}

			// Listen for the thread result in the main thread.
			// Just in case, remove previous updates.
			EditorApplication.update -= WaitAndFinishDatabaseUpdate;
			EditorApplication.update += WaitAndFinishDatabaseUpdate;

			m_WorkerThread = new System.Threading.Thread(GatherSVNStatuses);
			m_WorkerThread.Start();
		}

		// Executed in a worker thread.
		private static void GatherSVNStatuses()
		{
			try {
				var statusOptions = new SVNStatusDataOptions() {
					Depth = SVNStatusDataOptions.SearchDepth.Infinity,
					RaiseError = false,
					Timeout = SVNSimpleIntegration.COMMAND_TIMEOUT * 2,
					Offline = !CheckLockStatus,
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

				m_PendingStatuses = statuses.ToArray();

			}
			// Most probably the assembly got reloaded and the thread was aborted.
			catch (System.Threading.ThreadAbortException) {
				System.Threading.Thread.ResetAbort();

				m_PendingStatuses = new SVNStatusData[0];
			} catch (Exception ex) {
				Debug.LogException(ex);

				m_PendingStatuses = new SVNStatusData[0];
			}
		}

		private static void WaitAndFinishDatabaseUpdate()
		{
			if (m_PendingStatuses == null)
				return;

			if (DoTraceLogs) {
				Debug.Log($"Finished Update Database at {EditorApplication.timeSinceStartup:0.00}");
			}

			EditorApplication.update -= WaitAndFinishDatabaseUpdate;
			m_WorkerThread = null;

			// If preferences were changed while waiting.
			if (!IsActive)
				return;

			m_Database.ClearAll();

			var statuses = m_PendingStatuses;
			m_PendingStatuses = null;

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
				m_Database.SetStatusData(AssetDatabase.AssetPathToGUID(statusData.Path), statusData, false);

				AddModifiedFolders(statusData);
			}

			// Mark update as finished.
			m_Database.PendingUpdate = false;
		}

		private static void AutoRefresh()
		{
			if (AutoRefreshInterval <= 0.0f || EditorApplication.timeSinceStartup - m_LastRefreshTime < AutoRefreshInterval)
				return;

			m_LastRefreshTime = EditorApplication.timeSinceStartup;

			InvalidateDatabase();
		}

		private static void AddModifiedFolders(SVNStatusData statusData)
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
				if (m_Database.GetKnownStatusData(guid).Status == VCFileStatus.Added)
					return;

				bool moveToNext = m_Database.SetStatusData(guid, statusData, false);

				// If already exists, upper folders should be added as well.
				if (!moveToNext)
					return;

				statusData.Path = Path.GetDirectoryName(statusData.Path);
			}
		}
	}

	internal class SVNOverlayIconsDatabase : ScriptableObject
	{
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

		[SerializeField] private List<GuidStatusDataBind> StatusDatas = new List<GuidStatusDataBind>();

		// Icons are stored in the database so we don't reload them every time.
		[SerializeField] private GUIContent[] FileStatusIcons = new GUIContent[0];
		[SerializeField] private GUIContent[] LockStatusIcons = new GUIContent[0];
		[SerializeField] private GUIContent RemoteStatusIcons = null;

		// Is update pending?
		// If last update didn't make it, this flag will still be true.
		// Useful if assembly reload happens and stops the work of the database update.
		[SerializeField] public bool PendingUpdate = false;

		private void OnEnable()
		{
			// Load only if needed.
			if (FileStatusIcons.Length == 0) {
				FileStatusIcons = new GUIContent[Enum.GetValues(typeof(VCFileStatus)).Length];
				FileStatusIcons[(int)VCFileStatus.Added] = new GUIContent(Resources.Load<Texture2D>("Editor/SVNOverlayIcons/SVNAddedIcon"));
				FileStatusIcons[(int)VCFileStatus.Modified] = new GUIContent(Resources.Load<Texture2D>("Editor/SVNOverlayIcons/SVNModifiedIcon"));
				FileStatusIcons[(int)VCFileStatus.Deleted] = new GUIContent(Resources.Load<Texture2D>("Editor/SVNOverlayIcons/SVNDeletedIcon"));
				FileStatusIcons[(int)VCFileStatus.Conflicted] = new GUIContent(Resources.Load<Texture2D>("Editor/SVNOverlayIcons/SVNConflictIcon"));
				FileStatusIcons[(int)VCFileStatus.Unversioned] = new GUIContent(Resources.Load<Texture2D>("Editor/SVNOverlayIcons/SVNUnversionedIcon"));
			}
			
			if (LockStatusIcons.Length == 0) {
				LockStatusIcons = new GUIContent[Enum.GetValues(typeof(VCLockStatus)).Length];
				LockStatusIcons[(int)VCLockStatus.LockedHere] = new GUIContent(Resources.Load<Texture2D>("Editor/SVNOverlayIcons/Locks/SVNLockedHereIcon"), "You have locked this file.");
				LockStatusIcons[(int)VCLockStatus.LockedOther] = new GUIContent(Resources.Load<Texture2D>("Editor/SVNOverlayIcons/Locks/SVNLockedOtherIcon"), "Someone else locked this file.");
				LockStatusIcons[(int)VCLockStatus.LockedButStolen] = new GUIContent(Resources.Load<Texture2D>("Editor/SVNOverlayIcons/Locks/SVNLockedOtherIcon"), "Your lock was stolen by someone else.");
			}

			if (RemoteStatusIcons == null) {
				RemoteStatusIcons = new GUIContent(Resources.Load<Texture2D>("Editor/SVNOverlayIcons/Others/SVNRemoteChangesIcon"));
			}
		}

		public GUIContent GetFileStatusIconContent(VCFileStatus status)
		{
			return FileStatusIcons[(int)status];
		}


		public GUIContent GetLockStatusIconContent(VCLockStatus status)
		{
			return LockStatusIcons[(int)status];
		}

		public GUIContent GetRemoteStatusIconContent(VCRemoteFileStatus status)
		{
			return status == VCRemoteFileStatus.Modified ? RemoteStatusIcons : null;
		}

		//
		// Status Data
		//
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

		public bool SetStatusData(string guid, SVNStatusData statusData, bool skipPriorityCheck)
		{
			if (string.IsNullOrEmpty(guid)) {
				Debug.LogError($"Trying to add empty guid for status {statusData.Status}");
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


		public bool RemoveStatusData(string guid)
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

		public void ClearAll()
		{
			StatusDatas.Clear();
		}
	}


	internal class SVNOverlayIconsDatabaseAssetPostprocessor : AssetPostprocessor
	{
		private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
		{
			SVNOverlayIcons.PostProcessAssets(importedAssets, deletedAssets, movedAssets);
		}
	}
}
