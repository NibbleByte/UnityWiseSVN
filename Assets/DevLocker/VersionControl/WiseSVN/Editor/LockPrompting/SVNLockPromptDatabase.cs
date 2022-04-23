using DevLocker.VersionControl.WiseSVN.Preferences;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DevLocker.VersionControl.WiseSVN.LockPrompting
{
	/// <summary>
	/// Listens for newly modified assets and notifies user if they were locked.
	/// </summary>
	public class SVNLockPromptDatabase : Utils.EditorPersistentSingleton<SVNLockPromptDatabase>
	{
		public bool IsActive => m_PersonalPrefs.EnableCoreIntegration && m_PersonalPrefs.PopulateStatusesDatabase && m_ProjectPrefs.EnableLockPrompt;

		private List<SVNStatusData> m_KnownData = new List<SVNStatusData>();

		// bool will be persisted on assembly reload, the collection will not.
		private bool m_HasPendingOperations = false;
		private Queue<SVNAsyncOperation<LockOperationResult>> m_PendingOperations = new Queue<SVNAsyncOperation<LockOperationResult>>();

		private SVNPreferencesManager.PersonalPreferences m_PersonalPrefs => SVNPreferencesManager.Instance.PersonalPrefs;
		private SVNPreferencesManager.ProjectPreferences m_ProjectPrefs => SVNPreferencesManager.Instance.ProjectPrefs;

		public override void Initialize(bool freshlyCreated)
		{
			SVNPreferencesManager.Instance.PreferencesChanged += OnPreferencesChanged;
			SVNStatusesDatabase.Instance.DatabaseChanged += OnStatusDatabaseChanged;

			// Assembly reload just happened in the middle of some operation. Refresh database and redo any operations.
			if (m_HasPendingOperations) {
				m_HasPendingOperations = false;
				ClearKnowledge();

				SVNStatusesDatabase.Instance.InvalidateDatabase();
			}
		}

		private void OnPreferencesChanged()
		{
			ClearKnowledge();
		}

		public void ClearKnowledge()
		{
			m_KnownData.Clear();
		}

		public bool IsAssetOfType(string assetPath, AssetType assetTypeMask, bool isDeleted)
		{
			// All flags are set, skip checks.
			if ((int)assetTypeMask == ~0)
				return true;

			if (assetTypeMask.HasFlag(AssetType.Scene) && assetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
				return true;

			if (assetTypeMask.HasFlag(AssetType.TerrainData) && AssetDatabase.LoadAssetAtPath<TerrainData>(assetPath))
				return true;



			if (assetTypeMask.HasFlag(AssetType.Prefab) || assetTypeMask.HasFlag(AssetType.Model)) {

				var go = AssetDatabase.LoadMainAssetAtPath(assetPath) as GameObject;

				if (go) {
					bool match = false;

#if UNITY_2018_1_OR_NEWER
					var prefabType = PrefabUtility.GetPrefabAssetType(go);

					if (assetTypeMask.HasFlag(AssetType.Prefab))
						match |= prefabType == PrefabAssetType.Regular || prefabType == PrefabAssetType.Variant;

					if (assetTypeMask.HasFlag(AssetType.Model))
						match |= prefabType == PrefabAssetType.Model;
#else
					var prefabType = PrefabUtility.GetPrefabType(go);
					if (assetTypeMask.HasFlag(AssetType.Prefab))
						match |= prefabType == PrefabType.Prefab;

					if (assetTypeMask.HasFlag(AssetType.Model))
						match |= prefabType == PrefabType.ModelPrefab;
#endif

					return match;
				}

				if (isDeleted) {
					if (assetTypeMask.HasFlag(AssetType.Prefab) && assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
						return true;

					// Most popular models?
					if (assetTypeMask.HasFlag(AssetType.Model) && assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
						return true;
					if (assetTypeMask.HasFlag(AssetType.Model) && assetPath.EndsWith(".dae", StringComparison.OrdinalIgnoreCase))
						return true;
					if (assetTypeMask.HasFlag(AssetType.Model) && assetPath.EndsWith(".3ds", StringComparison.OrdinalIgnoreCase))
						return true;
					if (assetTypeMask.HasFlag(AssetType.Model) && assetPath.EndsWith(".obj", StringComparison.OrdinalIgnoreCase))
						return true;
				}
			}

			if (assetTypeMask.HasFlag(AssetType.Mesh) && AssetDatabase.LoadAssetAtPath<Mesh>(assetPath))
				return true;


			if (assetTypeMask.HasFlag(AssetType.Material) && assetPath.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
				return true;

			if (assetTypeMask.HasFlag(AssetType.Texture) && AssetDatabase.LoadAssetAtPath<Texture>(assetPath))
				return true;

			// Most popular extensions?
			if (assetTypeMask.HasFlag(AssetType.Texture) && isDeleted && assetPath.EndsWith(".psd", StringComparison.OrdinalIgnoreCase))
				return true;
			if (assetTypeMask.HasFlag(AssetType.Texture) && isDeleted && assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
				return true;
			if (assetTypeMask.HasFlag(AssetType.Texture) && isDeleted && assetPath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
				return true;
			if (assetTypeMask.HasFlag(AssetType.Texture) && isDeleted && assetPath.EndsWith(".tga", StringComparison.OrdinalIgnoreCase))
				return true;
			if (assetTypeMask.HasFlag(AssetType.Texture) && isDeleted && assetPath.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase))
				return true;
			if (assetTypeMask.HasFlag(AssetType.Texture) && isDeleted && assetPath.EndsWith(".iff", StringComparison.OrdinalIgnoreCase))
				return true;




			if (assetTypeMask.HasFlag(AssetType.Animation) && AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath))
				return true;
			if (assetTypeMask.HasFlag(AssetType.Animation) && isDeleted && assetPath.EndsWith(".anim", StringComparison.OrdinalIgnoreCase))
				return true;

			if (assetTypeMask.HasFlag(AssetType.Animator) && AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(assetPath))
				return true;
			if (assetTypeMask.HasFlag(AssetType.Animator) && isDeleted && assetPath.EndsWith(".controller", StringComparison.OrdinalIgnoreCase))
				return true;
			if (assetTypeMask.HasFlag(AssetType.Animator) && isDeleted && assetPath.EndsWith(".overrideController", StringComparison.OrdinalIgnoreCase))
				return true;




			if (assetTypeMask.HasFlag(AssetType.Script) && (
					assetPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)

					|| assetPath.EndsWith(".c", StringComparison.OrdinalIgnoreCase)		// For hard-core players
					|| assetPath.EndsWith(".cpp", StringComparison.OrdinalIgnoreCase)	// For hard-core players
					|| assetPath.EndsWith(".h", StringComparison.OrdinalIgnoreCase)		// For hard-core players
					|| assetPath.EndsWith(".hpp", StringComparison.OrdinalIgnoreCase)	// For hard-core players

					|| assetPath.EndsWith(".js", StringComparison.OrdinalIgnoreCase)	// No one uses this!
					|| assetPath.EndsWith(".boo", StringComparison.OrdinalIgnoreCase)	// No one uses this!
					)
				)
				return true;

#if UNITY_2019_3_OR_NEWER
			if (assetTypeMask.HasFlag(AssetType.UIElementsAssets) && AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.StyleSheet>(assetPath))
				return true;

			if (assetTypeMask.HasFlag(AssetType.UIElementsAssets) && AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.VisualTreeAsset>(assetPath))
				return true;
#endif
			if (assetTypeMask.HasFlag(AssetType.Shader) && assetPath.EndsWith(".shader", StringComparison.OrdinalIgnoreCase))
				return true;

			if (assetTypeMask.HasFlag(AssetType.ScriptableObject) && AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath))
				return true;
			if (assetTypeMask.HasFlag(AssetType.ScriptableObject) && isDeleted && assetPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
				return true;




			if (assetTypeMask.HasFlag(AssetType.Audio) && AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath))
				return true;

			if (assetTypeMask.HasFlag(AssetType.Video) && AssetDatabase.LoadAssetAtPath<UnityEngine.Video.VideoClip>(assetPath))
				return true;

#if UNITY_2018_4 || USE_TIMELINE
			if (assetTypeMask.HasFlag(AssetType.TimeLineAssets) && AssetDatabase.LoadAssetAtPath<UnityEngine.Timeline.TimelineAsset>(assetPath))
				return true;
			if (assetTypeMask.HasFlag(AssetType.TimeLineAssets) && AssetDatabase.LoadAssetAtPath<UnityEngine.Timeline.TrackAsset>(assetPath))
				return true;
			if (assetTypeMask.HasFlag(AssetType.TimeLineAssets) && AssetDatabase.LoadAssetAtPath<UnityEngine.Playables.PlayableAsset>(assetPath))
				return true;
#endif

			return false;
		}

		private bool AddOrUpdateKnowsStatusData(SVNStatusData statusData)
		{
			for(int i = 0; i < m_KnownData.Count; ++i) {
				var knownData = m_KnownData[i];
				if (knownData.Path == statusData.Path) {
					if (knownData.Equals(statusData, false)) {
						return false;
					} else {
						m_KnownData[i] = statusData;
						return true;
					}

				}
			}

			m_KnownData.Add(statusData);
			return true;
		}

		private bool RemoveKnownStatusData(SVNStatusData statusData)
		{
			for (int i = 0; i < m_KnownData.Count; ++i) {
				if (m_KnownData[i].Path == statusData.Path) {
					m_KnownData.RemoveAt(i);
					return true;
				}
			}

			return false;
		}

		private SVNAsyncOperation<LockOperationResult> EnqueueOperation(SVNAsyncOperation<LockOperationResult>.OperationHandler operationHandler)
		{
			var op = new SVNAsyncOperation<LockOperationResult>(operationHandler);
			op.Completed += OnLockOperationFinished;
			m_PendingOperations.Enqueue(op);
			return op;
		}

		private void OnLockOperationFinished(SVNAsyncOperation<LockOperationResult> op)
		{
			Debug.Assert(m_PendingOperations.Peek() == op);
			m_PendingOperations.Dequeue();

			if (m_PendingOperations.Count > 0) {
				m_PendingOperations.Peek().Start();
			} else {
				m_HasPendingOperations = false;
				SVNStatusesDatabase.Instance.InvalidateDatabase();
			}
		}

		public void LockEntries(IEnumerable<SVNStatusData> entries, bool forceLock)
		{
			var shouldLog = SVNPreferencesManager.Instance.PersonalPrefs.TraceLogs.HasFlag(SVNTraceLogs.SVNOperations);
			var lockMessage = SVNPreferencesManager.Instance.ProjectPrefs.LockPromptMessage;

			var entriesList = entries.ToList();
			if (entriesList.Count == 0)
				return;

			var targetsFileToUse = FileUtil.GetUniqueTempPathInProject();   // Not thread safe - call in main thread only.
			EnqueueOperation(op => WiseSVNIntegration.LockFiles(entriesList.Select(sd => sd.Path), forceLock, lockMessage, "", targetsFileToUse))
			.Completed += (op) => {
				if (op.Result == LockOperationResult.NotSupported) {
					Debug.LogError($"Locking failed, because server repository doesn't support locking. Assets failed to lock:\n{string.Join("\n", entriesList.Select(sd => sd.Path))}");
					EditorUtility.DisplayDialog("SVN Lock Prompt", "Lock failed. Check the logs for more info.", "I will!");

				} else if (op.Result == LockOperationResult.RemoteHasChanges) {
					foreach (var failedStatusData in entriesList) {
						RemoveKnownStatusData(failedStatusData);
					}
					Debug.LogWarning($"Locking failed because server repository has newer changes. Please update first. Assets failed to lock:\n{string.Join("\n", entriesList.Select(sd => sd.Path))}");
					EditorUtility.DisplayDialog("SVN Lock Prompt", "Lock failed. Check the logs for more info.", "I will!");
				} else if (op.Result != LockOperationResult.Success) {
					Debug.LogError($"Locking failed with result {op.Result} for assets:\n{string.Join("\n", entriesList.Select(sd => sd.Path))}.");
					EditorUtility.DisplayDialog("SVN Lock Prompt", "Stealing lock failed. Check the logs for more info.", "I will!");
				} else if (shouldLog) {
					Debug.Log($"Locked assets:\n{string.Join("\n", entriesList.Select(sd => sd.Path))}");
				}
			};

			if (m_PendingOperations.Count > 0 && m_HasPendingOperations == false) {
				m_HasPendingOperations = true;
				m_PendingOperations.Peek().Start();
			}
		}

		private void OnStatusDatabaseChanged()
		{
			if (!IsActive)
				return;

			var statusDatabaseData = SVNStatusesDatabase.Instance.GetAllKnownStatusData(false, true, true)
				.Where(sd => !Directory.Exists(sd.Path))
				.ToList()
				;

			var newEntries = new List<SVNStatusData>();

			foreach(var statusData in statusDatabaseData) {
				if (AddOrUpdateKnowsStatusData(statusData)) {
					newEntries.Add(statusData);
				}
			}

			m_KnownData.RemoveAll(known => statusDatabaseData.All(unknown => known.Path != unknown.Path));

			var shouldLock = new List<SVNStatusData>(newEntries.Count);
			var lockedByOtherEntries = new List<SVNStatusData>(newEntries.Count);
			var shouldUnlock = new List<SVNStatusData>();

			// Check for new assets to lock.
			foreach(var statusData in newEntries) {

				// NOTE: Deleted status never occurs as it is not provided by the SVNStatusesDatabase. :(
				if (statusData.Status != VCFileStatus.Modified && statusData.Status != VCFileStatus.Deleted && statusData.Status != VCFileStatus.Replaced)
					continue;

				if (statusData.LockStatus == VCLockStatus.LockedHere)
					continue;

				var assetPath = statusData.Path;
				bool isMeta = false;
				if (statusData.Path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) {
					assetPath = statusData.Path.Substring(0, statusData.Path.LastIndexOf(".meta"));
					isMeta = true;
				}

				var lockPromptParam = m_ProjectPrefs.LockPromptParameters
					.FirstOrDefault(al => assetPath.StartsWith(al.TargetFolder, StringComparison.OrdinalIgnoreCase));

				if (string.IsNullOrEmpty(lockPromptParam.TargetFolder))
					continue;

				if (isMeta && !lockPromptParam.IncludeTargetMetas)
					continue;

				if (SVNPreferencesManager.ShouldExclude(lockPromptParam.Exclude, assetPath))
					continue;

				bool matched = IsAssetOfType(assetPath, lockPromptParam.TargetTypes, statusData.Status == VCFileStatus.Deleted);
				if (!matched && !lockPromptParam.TargetTypes.HasFlag(AssetType.OtherTypes))
					continue;

				if (statusData.LockStatus == VCLockStatus.NoLock && statusData.RemoteStatus == VCRemoteFileStatus.None) {
					shouldLock.Add(statusData);
				} else {
					lockedByOtherEntries.Add(statusData);
				}
			}

			if (m_ProjectPrefs.AutoUnlockIfUnmodified) {

				// Check for old assets to unlock.
				foreach (var statusData in statusDatabaseData) {

					if (statusData.Status != VCFileStatus.Normal)
						continue;

					var assetPath = statusData.Path;
					if (statusData.Path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) {
						assetPath = statusData.Path.Substring(0, statusData.Path.LastIndexOf(".meta"));
					}

					var lockPromptParam = m_ProjectPrefs.LockPromptParameters
						.FirstOrDefault(al => assetPath.StartsWith(al.TargetFolder, StringComparison.OrdinalIgnoreCase));

					if (string.IsNullOrEmpty(lockPromptParam.TargetFolder))
						continue;

					if (SVNPreferencesManager.ShouldExclude(lockPromptParam.Exclude, assetPath))
						continue;

					bool matched = IsAssetOfType(assetPath, lockPromptParam.TargetTypes, false);
					if (!matched && !lockPromptParam.TargetTypes.HasFlag(AssetType.OtherTypes))
						continue;

					if (statusData.LockStatus != VCLockStatus.NoLock && statusData.LockStatus != VCLockStatus.LockedOther) {
						shouldUnlock.Add(statusData);
					}
				}
			}

			var shouldLog = SVNPreferencesManager.Instance.PersonalPrefs.TraceLogs.HasFlag(SVNTraceLogs.SVNOperations);

			// Auto-locking has been removed. User needs to explicitly select what to lock and what not.
			/*
			var lockMessage = SVNPreferencesManager.Instance.ProjectPrefs.AutoLockMessage;

			if (shouldLock.Count > 0) {

				var targetsFileToUse = FileUtil.GetUniqueTempPathInProject();   // Not thread safe - call in main thread only.
				EnqueueOperation(op => WiseSVNIntegration.LockFiles(shouldLock.Select(sd => sd.Path), false, lockMessage, "", targetsFileToUse))
				.Completed += (op) => {
					if (op.Result == LockOperationResult.RemoteHasChanges) {
						foreach (var failedStatusData in shouldLock) {
							RemoveKnownStatusData(failedStatusData);
						}
						Debug.LogWarning($"Auto-locking failed because server repository has newer changes. Please update first. Assets failed to lock:\n{string.Join("\n", shouldLock.Select(sd => sd.Path))}");
					} else if (op.Result != LockOperationResult.Success) {
						Debug.LogError($"Auto-locking failed with result {op.Result} for assets:\n{string.Join("\n", shouldLock.Select(sd => sd.Path))}");
					} else if (shouldLog) {
						Debug.Log($"Auto-locked assets:\n{string.Join("\n", shouldLock.Select(sd => sd.Path))}");
					}
				};
			}
			*/

			if (shouldUnlock.Count > 0) {
				var targetsFileToUse = FileUtil.GetUniqueTempPathInProject();   // Not thread safe - call in main thread only.
				EnqueueOperation(op => WiseSVNIntegration.UnlockFiles(shouldUnlock.Select(sd => sd.Path), false, targetsFileToUse))
				.Completed += (op) => {

					// If lock was stolen or broken, there is no good way to release it without causing error.
					// In that case the lock will be cleared from the local cache, so ignore the error.
					if (op.Result != LockOperationResult.Success && op.Result != LockOperationResult.LockedByOther) {
						Debug.LogError($"Auto-unlocking failed with result {op.Result} for assets:\n{string.Join("\n", shouldUnlock.Select(sd => sd.Path))}");
					} else if (shouldLog) {
						Debug.Log($"Auto-unlocked assets:\n{string.Join("\n", shouldUnlock.Select(sd => sd.Path))}");
					}
				};
			}

			if (shouldLock.Count > 0 || lockedByOtherEntries.Count > 0) {
				SVNLockPromptWindow.PromptLock(shouldLock, lockedByOtherEntries);
			}

			if (m_PendingOperations.Count > 0 && m_HasPendingOperations == false) {
				m_HasPendingOperations = true;
				m_PendingOperations.Peek().Start();
			}
		}
	}
}
