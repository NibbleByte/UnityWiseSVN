using DevLocker.VersionControl.WiseSVN.Preferences;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace DevLocker.VersionControl.WiseSVN.Branches
{
	/// <summary>
	/// Scans branches for Unity projects in the SVN repository and keeps them in a simple database.
	/// </summary>
	public class SVNBranchesDatabase : Utils.DatabasePersistentSingleton<SVNBranchesDatabase, BranchProject>
	{
		// All found Unity projects in the repository.
		public IReadOnlyCollection<BranchProject> BranchProjects => m_Data;

		// Last error that occured.
		public ListOperationResult LastError => m_LastError;

		// Which was the last processed entry by the worker thread. Kind of like progress.
		public string LastProcessedEntry { get; private set; }

		// Used to recognize if folder is actually an Unity project.
		private static readonly HashSet<string> m_UnityProjectEntries = new HashSet<string>() { "Assets", "ProjectSettings", "Packages" };

		private SVNPreferencesManager.PersonalPreferences m_PersonalPrefs => SVNPreferencesManager.Instance.PersonalPrefs;
		private SVNPreferencesManager.ProjectPreferences m_ProjectPrefs => SVNPreferencesManager.Instance.ProjectPrefs;

		public override bool IsActive => m_PersonalPrefs.EnableCoreIntegration && m_ProjectPrefs.EnableBranchesDatabase;
#if UNITY_2018_1_OR_NEWER
		public override bool TemporaryDisabled => WiseSVNIntegration.TemporaryDisabled || Application.isBatchMode || UnityEditor.BuildPipeline.isBuildingPlayer;
#else
		public override bool TemporaryDisabled => WiseSVNIntegration.TemporaryDisabled || UnityEditorInternal.InternalEditorUtility.inBatchMode || UnityEditor.BuildPipeline.isBuildingPlayer;
#endif
		public override bool DoTraceLogs => (m_PersonalPrefs.TraceLogs & SVNTraceLogs.DatabaseUpdates) != 0;

		// TODO: Have setting for this...
		public override double RefreshInterval => 24 * 60 * 60;


		[SerializeField] private ListOperationResult m_LastError;

		private List<BranchScanParameters> m_PendingScanParameters;

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

		protected override void StartDatabaseUpdate()
		{
			if (m_ProjectPrefs.BranchesDatabaseScanParameters.Count == 0) {
				Debug.LogError("User must define repository entry points to scan for branches with Unity projects!");
				return;
			}

			m_LastError = ListOperationResult.Success;

			// Duplicate this to be thread-safe.
			m_PendingScanParameters = new List<BranchScanParameters>(m_ProjectPrefs.BranchesDatabaseScanParameters);

			base.StartDatabaseUpdate();
		}

		// Executed in a worker thread.
		protected override BranchProject[] GatherDataInThread()
		{
			List<BranchProject> foundProjects = new List<BranchProject>(10);

			foreach (var scanParam in m_PendingScanParameters) {
				GatherProjectsIn(scanParam, foundProjects);
			}

			// Preferences may get changed in the main thread, but the whole ProjectPreferences object is replaced.
			// List shouldn't be modified in another thread, I think. So just keep reference to the original list.
			var pinnedBranches = m_ProjectPrefs.PinnedBranches;

			foundProjects.Sort((left, right) => {

				var leftPinnedIndex = pinnedBranches.FindIndex(s => left.BranchURL.Contains(s));
				var rightPinnedIndex = pinnedBranches.FindIndex(s => right.BranchURL.Contains(s));

				// Same match or both are -1
				if (leftPinnedIndex == rightPinnedIndex)
					return left.BranchURL.CompareTo(right.BranchURL);

				if (leftPinnedIndex == -1)
					return 1;

				if (rightPinnedIndex == -1)
					return -1;

				return leftPinnedIndex.CompareTo(rightPinnedIndex);
			});

			return foundProjects.ToArray();
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

				LastProcessedEntry = url.Length == scanParams.EntryPointURL.Length
					? scanParams.EntryPointURL
					: url.Remove(0, scanParams.EntryPointURL.Length + 1);

				listEntries.Clear();
				var opResult = WiseSVNIntegration.ListURL(url, false, listEntries);

				if (opResult != ListOperationResult.Success) {
					
					if (opResult == ListOperationResult.NotFound) {
						Debug.LogError($"{GetType().Name} failed to find url: \"{url}\".");
					}
					
					m_LastError = opResult;
					results.Clear();
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
					lastBranch.UnityProjectRelativePath = null;
					lastBranch.UnityProjectURL = null;
				}

				// This is a Unity project folder.
				if (m_UnityProjectEntries.IsSubsetOf(normalizedEntries)) {
					if (!string.IsNullOrEmpty(lastBranch.UnityProjectURL)) {
						// TODO: if BranchURL == UnityURL => Shouldn't be a problem.
						//		 if BranchURL != UnityRL => take the Unity folder name from the Working Copy (or find its original name in the repository branch). Accept only that name.
						Debug.LogError($"Multiple Unity projects found in the branch \"{lastBranch.BranchURL}\". This is still not supported.\n{lastBranch.UnityProjectURL}\n{url}");
					}

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

		protected override void WaitAndFinishDatabaseUpdate(BranchProject[] pendingData)
		{
			m_Data.AddRange(pendingData);
		}

		#endregion
	}
}
