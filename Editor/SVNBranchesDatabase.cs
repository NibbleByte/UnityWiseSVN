using DevLocker.VersionControl.WiseSVN.Preferences;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DevLocker.VersionControl.WiseSVN
{
	/// <summary>
	/// Scans branches for Unity projects in the SVN repository and keeps them in a simple database.
	/// </summary>
	public class SVNBranchesDatabase : ScriptableObject
	{
		// TODO: Make all this into a generic Database class. Maybe inherit from another class so PreferencesManager can benefit too from the ScriptableObject persitance tricks.

		// All found Unity projects in the repository.
		public IReadOnlyCollection<BranchProject> BranchProjects => m_BranchProjects;

		// Has the database been populated, yet?
		public bool IsReady => m_IsReady;

		// Is the database currently being populated?
		public bool IsUpdating => m_PendingUpdate;

		// Used to recognize if folder is actually an Unity project.
		private static readonly HashSet<string> m_UnityProjectEntries = new HashSet<string>() { "Assets", "ProjectSettings", "Packages" };

		private SVNPreferencesManager.PersonalPreferences m_PersonalPrefs => SVNPreferencesManager.Instance.PersonalPrefs;
		private SVNPreferencesManager.ProjectPreferences m_ProjectPrefs => SVNPreferencesManager.Instance.ProjectPrefs;

		public bool IsActive => m_PersonalPrefs.EnableCoreIntegration && m_ProjectPrefs.EnableBranchesDatabase;
		private bool DoTraceLogs => (m_PersonalPrefs.TraceLogs & SVNTraceLogs.DatabaseUpdates) != 0;


		[SerializeField] private bool m_IsReady;
		[SerializeField] private List<BranchProject> m_BranchProjects = new List<BranchProject>();
		private double m_LastRefreshTime;   // TODO: Maybe serialize this?

		private List<BranchScanParameters> m_PendingScanParameters;

		public event Action DatabaseChanged;


		#region Thread Work Related Data
		// Filled in by a worker thread.
		[NonSerialized] private BranchProject[] m_PendingProjects;

		private System.Threading.Thread m_WorkerThread;

		// Is update pending?
		// If last update didn't make it, this flag will still be true.
		// Useful if assembly reload happens and stops the work of the database update.
		[SerializeField] private bool m_PendingUpdate = false;
		#endregion

		//
		//=============================================================================
		//
		#region Initialize & Preferences

		private static SVNBranchesDatabase m_Instance;
		public static SVNBranchesDatabase Instance {
			get {
				if (m_Instance == null) {
					m_Instance = Resources.FindObjectsOfTypeAll<SVNBranchesDatabase>().FirstOrDefault();

					bool freshlyCreated = false;
					if (m_Instance == null) {

						m_Instance = ScriptableObject.CreateInstance<SVNBranchesDatabase>();
						m_Instance.name = nameof(SVNBranchesDatabase);

						// Setting this flag will tell Unity NOT to destroy this object on assembly reload (as no scene references this object).
						// We're essentially leaking this object. But we can still find it with Resources.FindObjectsOfTypeAll() after reload.
						// More info on this: https://blogs.unity3d.com/2012/10/25/unity-serialization/
						m_Instance.hideFlags = HideFlags.HideAndDontSave;

						freshlyCreated = true;

						if (m_Instance.DoTraceLogs) {
							Debug.Log($"{m_Instance.name} not found. Creating new one.");
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

			// HACK: Force WiseSVN initialize first, so it doesn't happen in the thread.
			WiseSVNIntegration.ProjectRoot.StartsWith(string.Empty);

			SVNPreferencesManager.Instance.PreferencesChanged += PreferencesChanged;
			PreferencesChanged();

			// Assembly reload might have killed the working thread leaving pending update.
			// Do it again.
			if (m_PendingUpdate) {
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
			if (m_ProjectPrefs.BranchesDatabaseScanParameters.Count == 0) {
				Debug.LogError("User must define repository entry points to scan for branches with Unity projects!");
				return;
			}

			if (DoTraceLogs) {
				Debug.Log($"Started Update Branches Database at {EditorApplication.timeSinceStartup:0.00}");
			}

			if (m_WorkerThread != null) {
				throw new Exception("SVN starting database update, while another one is pending?");
			}

			m_PendingUpdate = true;

			// Duplicate this to be thread-safe.
			m_PendingScanParameters = new List<BranchScanParameters>(m_ProjectPrefs.BranchesDatabaseScanParameters);

			// Listen for the thread result in the main thread.
			// Just in case, remove previous updates.
			EditorApplication.update -= WaitAndFinishDatabaseUpdate;
			EditorApplication.update += WaitAndFinishDatabaseUpdate;

			m_WorkerThread = new System.Threading.Thread(GatherData);
			m_WorkerThread.Start();
		}

		// Executed in a worker thread.
		private void GatherData()
		{
			try {
				List<BranchProject> foundProjects = new List<BranchProject>(10);

				foreach (var scanParam in m_PendingScanParameters) {
					GatherProjectsIn(scanParam, foundProjects);
				}

				if (m_PendingUpdate == false) {
					throw new Exception("SVN thread finished work but the update is over?");
				}

				m_PendingProjects = foundProjects.ToArray();

			}
			// Most probably the assembly got reloaded and the thread was aborted.
			catch (System.Threading.ThreadAbortException) {
				System.Threading.Thread.ResetAbort();

				// Should always be true.
				if (m_PendingUpdate) {
					m_PendingProjects = new BranchProject[0];
				}
			} catch (Exception ex) {
				Debug.LogException(ex);

				// Should always be true.
				if (m_PendingUpdate) {
					m_PendingProjects = new BranchProject[0];
				}
			}
		}

		private void WaitAndFinishDatabaseUpdate()
		{
			if (m_PendingProjects == null)
				return;

			if (DoTraceLogs) {
				Debug.Log($"Finished Update Branches Database at {EditorApplication.timeSinceStartup:0.00}");
			}

			EditorApplication.update -= WaitAndFinishDatabaseUpdate;
			m_WorkerThread = null;

			m_IsReady = true;

			var branchProjects = m_PendingProjects;
			m_PendingProjects = null;
			m_BranchProjects.Clear();

			// Mark update as finished.
			m_PendingUpdate = false;

			// If preferences were changed while waiting.
			if (!IsActive)
				return;

			// Process the gathered statuses in the main thread, since Unity API is not thread-safe.
			m_BranchProjects.AddRange(branchProjects);

			DatabaseChanged?.Invoke();
		}

		private void GatherProjectsIn(BranchScanParameters scanParams, List<BranchProject> results)
		{
			var listEntries = new List<string>();
			var normalizedEntries = new List<string>();

			HashSet<string> branchSignatureEntries = new HashSet<string>(scanParams.BranchSignatureRootEntries);

			var lastBranch = new BranchProject() {
				BranchName = "Unknown",
				BranchURL = scanParams.EntryPointURL,
				BranchRelativePath = string.Empty,
				UnityProjectURL = string.Empty,
			};

			Queue<string> urls = new Queue<string>();
			urls.Enqueue(scanParams.EntryPointURL);

			while (urls.Count > 0) {
				var url = urls.Dequeue();

				listEntries.Clear();
				var opResult = WiseSVNIntegration.ListURL(url, false, listEntries);

				switch (opResult) {
					case ListOperationResult.URLNotFound:
						Debug.LogError($"{GetType().Name} failed to find url: \"{url}\".");
						return;
					case ListOperationResult.InvalidWorkingCopy:
						Debug.LogError($"{GetType().Name} invalid url: \"{url}\". Please enter url to repository server folder.");
						return;
				}

				// Folders have '/' at the end but the user shouldn't define them this way.
				normalizedEntries.Clear();
				normalizedEntries.Capacity = Mathf.Max(listEntries.Count, normalizedEntries.Capacity);
				normalizedEntries.AddRange(listEntries.Select(e => e.TrimEnd('/')));

				// Is this a branch?
				if (branchSignatureEntries.IsSubsetOf(normalizedEntries)) {
					lastBranch.BranchName = Path.GetFileName(url);
					lastBranch.BranchURL = url;
					lastBranch.BranchRelativePath = url.Remove(0, scanParams.EntryPointURL.Length + 1);
				}

				// This is a Unity project folder.
				if (m_UnityProjectEntries.IsSubsetOf(normalizedEntries)) {
					lastBranch.UnityProjectURL = url;
					lastBranch.UnityProjectRelativePath = url.Remove(0, scanParams.EntryPointURL.Length + 1);
					results.Add(lastBranch);

					// No need to dig in the Unity project folder.
					continue;
				}

				for(int i = 0; i < normalizedEntries.Count; ++i) {

					// Only interested in folders.
					if (listEntries[i].LastOrDefault() != '/')
						continue;

					var folderName = normalizedEntries[i];

					if (scanParams.ExcludesFolderNames.Contains(folderName))
						continue;

					urls.Enqueue(url + "/" + folderName);
				}
			}
		}

		#endregion


		//
		//=============================================================================
		//
		#region Invalidate Database

		/// <summary>
		/// Force the database to refresh its statuses cache onto another thread.
		/// </summary>
		public void InvalidateDatabase()
		{
			if (!IsActive || m_PendingUpdate || WiseSVNIntegration.TemporaryDisabled)
				return;

			// Will be done on assembly reload.
			if (EditorApplication.isCompiling) {
				m_PendingUpdate = true;
				return;
			}

			StartDatabaseUpdate();
		}

		private void AutoRefresh()
		{
			// TODO: Have setting for this...
			//double refreshInterval = m_PersonalPrefs.AutoRefreshDatabaseInterval;
			double refreshInterval = 24 * 60 * 60;

			if (refreshInterval <= 0 || EditorApplication.timeSinceStartup - m_LastRefreshTime < refreshInterval)
				return;

			m_LastRefreshTime = EditorApplication.timeSinceStartup;

			InvalidateDatabase();
		}

		#endregion
	}
}
