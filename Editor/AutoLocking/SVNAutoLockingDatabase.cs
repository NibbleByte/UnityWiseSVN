using DevLocker.VersionControl.WiseSVN.Preferences;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DevLocker.VersionControl.WiseSVN.AutoLocking
{
	/// <summary>
	/// Listens for newly modified assets and notifies user if they were locked.
	/// </summary>
	public class SVNAutoLockingDatabase : Utils.EditorPersistentSingleton<SVNAutoLockingDatabase>
	{
		public bool IsActive => m_PersonalPrefs.EnableCoreIntegration && m_PersonalPrefs.PopulateStatusesDatabase && m_ProjectPrefs.EnableAutoLocking;

		private List<SVNStatusData> m_KnownData = new List<SVNStatusData>();

		private SVNPreferencesManager.PersonalPreferences m_PersonalPrefs => SVNPreferencesManager.Instance.PersonalPrefs;
		private SVNPreferencesManager.ProjectPreferences m_ProjectPrefs => SVNPreferencesManager.Instance.ProjectPrefs;

		public override void Initialize(bool freshlyCreated)
		{
			SVNPreferencesManager.Instance.PreferencesChanged += OnPreferencesChanged;
			SVNStatusesDatabase.Instance.DatabaseChanged += OnStatusDatabaseChanged;
		}

		private void OnPreferencesChanged()
		{
			m_KnownData.Clear();
		}

		public bool IsAssetOfType(string assetPath, AssetType assetTypeMask, bool isDeleted)
		{
			if (assetTypeMask.HasFlag(AssetType.Scene) && assetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
				return true;

			if (assetTypeMask.HasFlag(AssetType.TerrainData) && AssetDatabase.LoadAssetAtPath<TerrainData>(assetPath))
				return true;



			if (assetTypeMask.HasFlag(AssetType.Prefab) || assetTypeMask.HasFlag(AssetType.Model)) {

				var go = AssetDatabase.LoadMainAssetAtPath(assetPath) as GameObject;

				if (go) {
					var prefabType = PrefabUtility.GetPrefabAssetType(go);

					if (assetTypeMask.HasFlag(AssetType.Prefab))
						return prefabType == PrefabAssetType.Regular || prefabType == PrefabAssetType.Variant;

					if (assetTypeMask.HasFlag(AssetType.Model))
						return prefabType == PrefabAssetType.Model;
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
					|| assetPath.EndsWith(".cpp", StringComparison.OrdinalIgnoreCase)		// For hard-core players
					|| assetPath.EndsWith(".h", StringComparison.OrdinalIgnoreCase)		// For hard-core players
					|| assetPath.EndsWith(".hpp", StringComparison.OrdinalIgnoreCase)		// For hard-core players

					|| assetPath.EndsWith(".js", StringComparison.OrdinalIgnoreCase)		// No one uses this!
					|| assetPath.EndsWith(".boo", StringComparison.OrdinalIgnoreCase)		// No one uses this!
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


			if (assetTypeMask.HasFlag(AssetType.TimeLineAssets) && AssetDatabase.LoadAssetAtPath<UnityEngine.Timeline.TimelineAsset>(assetPath))
				return true;
			if (assetTypeMask.HasFlag(AssetType.TimeLineAssets) && AssetDatabase.LoadAssetAtPath<UnityEngine.Timeline.TrackAsset>(assetPath))
				return true;
			if (assetTypeMask.HasFlag(AssetType.TimeLineAssets) && AssetDatabase.LoadAssetAtPath<UnityEngine.Playables.PlayableAsset>(assetPath))
				return true;

			return false;
		}

		private bool AddOrUpdateKnowsStatusData(SVNStatusData statusData)
		{
			for(int i = 0; i < m_KnownData.Count; ++i) {
				var knownData = m_KnownData[i];
				if (knownData.Path == statusData.Path) {
					if (knownData.Equals(statusData)) {
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

			m_KnownData.RemoveAll(known => !statusDatabaseData.Any(unknown => known.Path == unknown.Path));

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
				if (statusData.Path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) {
					assetPath = statusData.Path.Substring(0, statusData.Path.LastIndexOf(".meta"));
				}
				assetPath = assetPath.Replace("\\", "/");

				var autoLockingParam = m_ProjectPrefs.AutoLockingParameters
					.FirstOrDefault(al => assetPath.StartsWith(al.TargetFolder, StringComparison.OrdinalIgnoreCase));

				if (string.IsNullOrEmpty(autoLockingParam.TargetFolder))
					continue;

				if (SVNPreferencesManager.ShouldExclude(autoLockingParam.Exclude, assetPath))
					continue;

				bool matched = IsAssetOfType(assetPath, autoLockingParam.TargetTypes, statusData.Status == VCFileStatus.Deleted);
				if (!matched && !autoLockingParam.TargetTypes.HasFlag(AssetType.OtherTypes))
					continue;

				if (statusData.LockStatus == VCLockStatus.NoLock) {
					shouldLock.Add(statusData);
					continue;
				}

				lockedByOtherEntries.Add(statusData);
			}

			// Check for old assets to unlock.
			foreach(var statusData in statusDatabaseData) {

				if (statusData.Status != VCFileStatus.Normal)
					continue;

				var assetPath = statusData.Path;
				if (statusData.Path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) {
					assetPath = statusData.Path.Substring(0, statusData.Path.LastIndexOf(".meta"));
				}
				assetPath = assetPath.Replace("\\", "/");

				var autoLockingParam = m_ProjectPrefs.AutoLockingParameters
					.FirstOrDefault(al => assetPath.StartsWith(al.TargetFolder, StringComparison.OrdinalIgnoreCase));

				if (string.IsNullOrEmpty(autoLockingParam.TargetFolder))
					continue;

				if (SVNPreferencesManager.ShouldExclude(autoLockingParam.Exclude, assetPath))
					continue;

				bool matched = IsAssetOfType(assetPath, autoLockingParam.TargetTypes, false);
				if (!matched && !autoLockingParam.TargetTypes.HasFlag(AssetType.OtherTypes))
					continue;

				if (statusData.LockStatus != VCLockStatus.NoLock && statusData.LockStatus != VCLockStatus.LockedOther) {
					shouldUnlock.Add(statusData);
				}
			}


			var shouldLog = SVNPreferencesManager.Instance.PersonalPrefs.TraceLogs.HasFlag(SVNTraceLogs.SVNOperations);
			var lockMessage = SVNPreferencesManager.Instance.ProjectPrefs.AutoLockMessage;

			// TODO: What happens on AssemblyReload and thread dies?
			// TODO: Do SVN support multiple operations at the same time? Should we queue these?


			if (shouldLock.Count > 0) {
				WiseSVNIntegration.LockFilesAsync(shouldLock.Select(sd => sd.Path), false, lockMessage)
					.Completed += (op) => {

						if (op.Result != LockOperationResult.Success) {
							Debug.LogError($"Auto-locking failed with result {op.Result} for assets:\n{string.Join("\n", shouldLock.Select(sd => sd.Path))}");
						} else if (shouldLog) {
							Debug.Log($"Auto-locked assets:\n{string.Join("\n", shouldLock.Select(sd => sd.Path))}");
							SVNStatusesDatabase.Instance.InvalidateDatabase();
						}
					};
			}

			if (shouldUnlock.Count > 0) {
				WiseSVNIntegration.UnlockFilesAsync(shouldUnlock.Select(sd => sd.Path), false)
					.Completed += (op) => {

						if (op.Result != LockOperationResult.Success) {
							if (op.Result != LockOperationResult.LockedByOther) {
								Debug.LogError($"Auto-unlocking failed with result {op.Result} for assets:\n{string.Join("\n", shouldUnlock.Select(sd => sd.Path))}");
							} else {
								// If lock was stolen or broken, there is no good way to release it without causing error.
								// In that case the lock will be cleared from the local cache, so ignore the error.
								Debug.Log($"Auto-unlocked assets:\n{string.Join("\n", shouldUnlock.Select(sd => sd.Path))}");
								SVNStatusesDatabase.Instance.InvalidateDatabase();
							}
						} else if (shouldLog) {
							Debug.Log($"Auto-unlocked assets:\n{string.Join("\n", shouldUnlock.Select(sd => sd.Path))}");
							SVNStatusesDatabase.Instance.InvalidateDatabase();
						}
					};
			}

			if (lockedByOtherEntries.Count > 0) {

				var message = new System.Text.StringBuilder(lockedByOtherEntries.Count * 20 + 80);

				message.AppendLine("These modified files are locked by someone else or lock was stolen.");
				message.AppendLine("Expect potential conflicts.");
				message.AppendLine();

				foreach(var statusData in lockedByOtherEntries) {
					message.AppendLine($"\"{Path.GetFileName(statusData.Path)}\" by {statusData.LockDetails.Owner}");
				}


				var choice = EditorUtility.DisplayDialog("SVN Auto-Locking", message.ToString(), "Force Lock", "Skip Lock");

				if (choice) {
					WiseSVNIntegration.LockFilesAsync(lockedByOtherEntries.Select(sd => sd.Path), true, lockMessage)
					.Completed += (op) => {
						if (op.Result != LockOperationResult.Success) {
							Debug.LogError($"Auto-locking by force failed with result {op.Result} for assets:\n{string.Join("\n", lockedByOtherEntries.Select(sd => sd.Path))}.");
							EditorUtility.DisplayDialog("SVN Auto-Locking", "Stealing lock failed. Check the logs for more info.", "I will!");
						} else if (shouldLog) {
							Debug.Log($"Auto-locked assets by force:\n{string.Join("\n", lockedByOtherEntries.Select(sd => sd.Path))}");
							SVNStatusesDatabase.Instance.InvalidateDatabase();
						}
					};
				}
			}
		}
	}
}
