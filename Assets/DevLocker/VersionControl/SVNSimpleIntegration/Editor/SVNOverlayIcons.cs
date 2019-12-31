using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DevLocker.VersionControl.SVN
{
	[InitializeOnLoad]
	public static class SVNOverlayIcons
	{
		private const string INVALID_GUID = "00000000000000000000000000000000";
		private const string ASSETS_FOLDER_GUID = "00000000000000001000000000000000";
		private static SVNOverlayIconsDatabase m_Database;

		// Note: not all of these are rendered. Check the Database icons.
		private readonly static VCFileStatus[] StatusShowPriority = new VCFileStatus[] {
			VCFileStatus.Conflicted, 
			VCFileStatus.Obstructed, 
			VCFileStatus.Modified,
			VCFileStatus.Added,
			VCFileStatus.Deleted,
			VCFileStatus.Missing,
			VCFileStatus.Replaced,
			VCFileStatus.Ignored,
			VCFileStatus.Unversioned,
			VCFileStatus.Normal,
		};

		static SVNOverlayIcons()
		{
			EditorApplication.projectWindowItemOnGUI += ItemOnGUI;

			m_Database = Resources.FindObjectsOfTypeAll<SVNOverlayIconsDatabase>().FirstOrDefault();
			if (m_Database == null) {
				// TODO: Remove debug logs.
				Debug.LogError("SVNOverlayIconsDatabase not found! Creating new one.");

				m_Database = ScriptableObject.CreateInstance<SVNOverlayIconsDatabase>();
				m_Database.name = "SVNOverlayIconsDatabase";

				// Setting this flag will tell Unity NOT to destroy this object on assembly reload (as no scene references this object).
				// We're essentially leaking this object. But we can still find it with Resources.FindObjectsOfTypeAll() after reload.
				// More info on this: https://blogs.unity3d.com/2012/10/25/unity-serialization/
				m_Database.hideFlags = HideFlags.HideAndDontSave;

				InvalidateDatabase();
			}

			// Assembly reload might have killed the working thread leaving pending update.
			// Do it again.
			if (m_Database.PendingUpdate) {
				StartDatabaseUpdate();
			}
		}

		public static void InvalidateDatabase()
		{
			if (m_Database.PendingUpdate)
				return;

			m_Database.PendingUpdate = true;

			StartDatabaseUpdate();
		}

		private static void ItemOnGUI(string guid, Rect selectionRect)
		{
			if (string.IsNullOrEmpty(guid) || guid.StartsWith("00000000", StringComparison.Ordinal))
				// Cause what are the chances of having a guid starting with so many zeroes?!
				//|| guid.Equals(INVALID_GUID, StringComparison.Ordinal)
				//|| guid.Equals(ASSETS_FOLDER_GUID, StringComparison.Ordinal)
				return;

			GUIContent icon = null;

			foreach(var status in StatusShowPriority) {
				if (m_Database.HasGUID(status, guid)) {
					icon = m_Database.GetIconContent(status);
					break;
				}
			}

			if (icon != null) {
				var iconRect = new Rect(selectionRect);
				if (iconRect.width > iconRect.height) {
					iconRect.width = iconRect.height;
				} else {
					// Project view has zoomed in items. Scale up the icons, but keep a limit so it doesn't hide the item preview image.
					float yAdjustment = Mathf.Clamp(iconRect.width - 48, 0, float.PositiveInfinity);
					iconRect.width = iconRect.width - yAdjustment;
					iconRect.height = iconRect.width;
					
					// Compensate for the height change.
					iconRect.y += yAdjustment;
				}

				iconRect.y += 4;
				GUI.Label(iconRect, icon);
			}
		}

		
		internal static void PostProcessAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets)
		{
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
				var status = SVNSimpleIntegration.GetStatus(path).Status;

				// If status is normal but asset was imported, maybe the meta changed. Use that status instead.
				if (status == VCFileStatus.Normal) {
					status = SVNSimpleIntegration.GetStatus(path + ".meta").Status;
				}

				// Conflicted file got reimported? Fuck this, just refresh.
				if (status == VCFileStatus.Conflicted) {
					m_Database.AddGUID(VCFileStatus.Conflicted, AssetDatabase.AssetPathToGUID(path));
					InvalidateDatabase();
					return;
				}

				if (status == VCFileStatus.Normal)
					continue;

				// Every time the user saves a file it will get reimported. If we already know it is modified, don't refresh every time.
				bool wasModifiedGuid = m_Database.AddGUID(status, AssetDatabase.AssetPathToGUID(path));

				if (status != VCFileStatus.Normal && !wasModifiedGuid) {
					InvalidateDatabase();
					return;
				}

				// Changed back to normal.
				if (status == VCFileStatus.Normal && wasModifiedGuid) {
					InvalidateDatabase();
					return;
				}
			}
		}

		private static void StartDatabaseUpdate()
		{
			m_Database.ClearAll();

			// TODO: Remove debug logs.
			Debug.LogWarning("Update Database");

			// TODO: Do this in thread.
			// TODO: GetStatuses is not thread safe? It calls Debug.LogError?
			var statuses = SVNSimpleIntegration.GetStatuses(SVNSimpleIntegration.ProjectDataPath);

			// Will get statuses of all added / modified / deleted / conflicted / unversioned files. Only normal files won't be listed.
			foreach (var status in statuses) {

				// Deleted svn file can still exist for some reason. Need to show it as deleted.
				// If file doesn't exists, skip it as we can't show it anyway.
				if (status.Status == VCFileStatus.Deleted && !File.Exists(status.Path))
					continue;

				// Meta statuses are also considered. They are shown as the asset status.
				if (status.Path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) {
					var assetPath = status.Path.Substring(0, status.Path.LastIndexOf(".meta"));
					m_Database.AddGUID(status.Status, AssetDatabase.AssetPathToGUID(assetPath));
					AddModifiedFolders(status.Status, status.Path);
					continue;
				}

				// TODO: Test tree conflicts.

				m_Database.AddGUID(status.Status, AssetDatabase.AssetPathToGUID(status.Path));
				AddModifiedFolders(status.Status, status.Path);
			}

			m_Database.PendingUpdate = false;
		}

		private static void AddModifiedFolders(VCFileStatus status, string path)
		{
			if (status == VCFileStatus.Unversioned || status == VCFileStatus.Ignored)
				return;

			if (status != VCFileStatus.Modified && status != VCFileStatus.Conflicted) {
				status = VCFileStatus.Modified;
			}

			path = Path.GetDirectoryName(path);

			while (!string.IsNullOrEmpty(path)) {
				var guid = AssetDatabase.AssetPathToGUID(path);

				bool moveToNext = m_Database.HasGUID(VCFileStatus.Added, guid)
					? false		// Added folders should not be shown as modified.
					: m_Database.AddGUID(status, guid);

				// If already exists, upper folders should be added as well.
				if (!moveToNext)
					return;

				path = Path.GetDirectoryName(path);
			}
		}
	}

	internal class SVNOverlayIconsDatabase : ScriptableObject
	{
		// GUIDs
		[SerializeField] private List<string> Added = new List<string>();
		[SerializeField] private List<string> Modified = new List<string>();
		[SerializeField] private List<string> Deleted = new List<string>();
		[SerializeField] private List<string> Conflicted = new List<string>();
		[SerializeField] private List<string> Unversioned = new List<string>();

		// Icons are stored in the database so we don't reload them every time.
		[SerializeField] private GUIContent[] Icons = new GUIContent[0];

		// Is update pending?
		// If last update didn't make it, this flag will still be true.
		// Useful if assembly reload happens and stops the work of the database update.
		[SerializeField] public bool PendingUpdate = false;

		private void OnEnable()
		{
			// Load only if needed.
			if (Icons.Length == 0) {
				Icons = new GUIContent[Enum.GetValues(typeof(VCFileStatus)).Length];
				Icons[(int)VCFileStatus.Added] = new GUIContent(Resources.Load<Texture2D>("Editor/SVNOverlayIcons/SVNAddedIcon"));
				Icons[(int)VCFileStatus.Modified] = new GUIContent(Resources.Load<Texture2D>("Editor/SVNOverlayIcons/SVNModifiedIcon"));
				Icons[(int)VCFileStatus.Deleted] = new GUIContent(Resources.Load<Texture2D>("Editor/SVNOverlayIcons/SVNDeletedIcon"));
				Icons[(int)VCFileStatus.Conflicted] = new GUIContent(Resources.Load<Texture2D>("Editor/SVNOverlayIcons/SVNConflictIcon"));
				Icons[(int)VCFileStatus.Unversioned] = new GUIContent(Resources.Load<Texture2D>("Editor/SVNOverlayIcons/SVNUnversionedIcon"));
			}
		}

		public GUIContent GetIconContent(VCFileStatus status)
		{
			return Icons[(int)status];
		}

		public bool HasGUID(VCFileStatus status, string guid)
		{
			switch (status) {
				case VCFileStatus.Added:
					return Added.Contains(guid, StringComparer.Ordinal);
				case VCFileStatus.Modified:
				case VCFileStatus.Replaced:
					return Modified.Contains(guid, StringComparer.Ordinal);
				case VCFileStatus.Deleted:
					return Deleted.Contains(guid, StringComparer.Ordinal);
				case VCFileStatus.Conflicted:
					return Conflicted.Contains(guid, StringComparer.Ordinal);
				case VCFileStatus.Unversioned:
					return Unversioned.Contains(guid, StringComparer.Ordinal);
				default:
					return false;
			}
		}

		public bool AddGUID(VCFileStatus status, string guid)
		{
			if (string.IsNullOrEmpty(guid)) {
				Debug.LogError($"Trying to add empty guid for status {status}");
			}

			switch (status) {
				case VCFileStatus.Added:
					return AddUnique(Added, guid);
				case VCFileStatus.Modified:
				case VCFileStatus.Replaced:
					return AddUnique(Modified, guid);
				case VCFileStatus.Deleted:
					return AddUnique(Deleted, guid);
				case VCFileStatus.Conflicted:
					return AddUnique(Conflicted, guid);
				case VCFileStatus.Unversioned:
					return AddUnique(Unversioned, guid);
				default:
					return false;
			}
		}


		public bool RemoveGUID(VCFileStatus status, string guid)
		{
			if (string.IsNullOrEmpty(guid)) {
				Debug.LogError($"Trying to remove empty guid for status {status}");
			}

			switch (status) {
				case VCFileStatus.Added:
					return Added.Remove(guid);
				case VCFileStatus.Modified:
				case VCFileStatus.Replaced:
					return Modified.Remove(guid);
				case VCFileStatus.Deleted:
					return Deleted.Remove(guid);
				case VCFileStatus.Conflicted:
					return Conflicted.Remove(guid);
				case VCFileStatus.Unversioned:
					return Unversioned.Remove(guid);
				default:
					return false;
			}
		}

		public void ClearAll()
		{
			Added.Clear();
			Modified.Clear();
			Deleted.Clear();
			Conflicted.Clear();
			Unversioned.Clear();
		}

		private bool AddUnique(List<string> list, string value)
		{
			if (list.Contains(value, StringComparer.Ordinal))
				return false;

			list.Add(value);
			return true;
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
