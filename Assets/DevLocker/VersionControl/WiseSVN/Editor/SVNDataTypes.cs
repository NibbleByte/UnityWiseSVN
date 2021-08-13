using System;
using System.Linq;
using UnityEngine;

namespace DevLocker.VersionControl.WiseSVN
{
	public enum VCFileStatus
	{
		Normal,
		Added,
		Conflicted,
		Deleted,
		Ignored,
		Modified,
		Replaced,
		Unversioned,
		Missing,
		External,
		Incomplete,	// Not used
		Merged, 	// Not used
		Obstructed,
		None,	// File not found or something worse....
	}

	public enum VCPropertiesStatus
	{
		None,
		Normal,
		Conflicted,
		Modified,
	}

	public enum VCTreeConflictStatus
	{
		Normal,
		TreeConflict
	}

	public enum VCSwitchedExternal
	{
		Normal,
		Switched,
		External,
	}

	public enum VCLockStatus
	{
		NoLock,
		LockedHere,
		LockedOther,
		LockedButStolen,
		BrokenLock
	}

	public enum VCRemoteFileStatus
	{
		None,
		Modified,
	}

	[Flags]
	public enum SVNTraceLogs
	{
		None = 0,
		SVNOperations = 1 << 0,
		DatabaseUpdates = 1 << 4,
		All = ~0,
	}

	public enum SVNMoveBehaviour
	{
		NormalSVNMove = 0,
		UseAddAndDeleteForFolders = 2,
		UseAddAndDeleteForAllAssets = 4,
	}

	public enum LockOperationResult
	{
		Success,				// Operation succeeded.
		LockedByOther,			// File is locked by another working copy (may be the same user). Use Force to enforce the operation.
		AuthenticationFailed,	// User needs to log in using normal SVN client and save their authentication.
		UnableToConnectError,	// Unable to connect to repository indicating some network or server problems.
		UnknownError,			// Failed for some reason.
	}

	public enum ListOperationResult
	{
		Success,				// Operation succeeded.
		NotFound,				// URL target was not found.
		AuthenticationFailed,	// User needs to log in using normal SVN client and save their authentication.
		UnableToConnectError,	// Unable to connect to repository indicating some network or server problems.
		UnknownError,			// Failed for some reason.
	}

	public enum LogOperationResult
	{
		Success,                // Operation succeeded.
		NotFound,				// URL target was not found.
		AuthenticationFailed,	// User needs to log in using normal SVN client and save their authentication.
		UnableToConnectError,	// Unable to connect to repository indicating some network or server problems.
		UnknownError,			// Failed for some reason.
	}

	public enum CommitOperationResult
	{
		Success,				// Operation succeeded.
		OutOfDateError,			// Some folders/files have pending changes in the repository. Update them before trying to commit.
		ConflictsError,			// Some folders/files have conflicts. Clear them before trying to commit.
		UnversionedError,		// Can't commit unversioned files directly. Add them before trying to commit. Recursive skips unversioned files.
		AuthenticationFailed,	// User needs to log in using normal SVN client and save their authentication.
		UnableToConnectError,	// Unable to connect to repository indicating some network or server problems.
		PrecommitHookError,		// Precommit hook denied the commit on the server side. Talk with your administrator about your commit company policies. Example: always commit with a valid message.
		UnknownError,			// Failed for any other reason.
	}

	public enum RevertOperationResult
	{
		Success,				// Operation succeeded.
		UnknownError,			// Failed for any other reason.
	}

	// How conflicts should be auto-resolved.
	// http://svnbook.red-bean.com/en/1.8/svn.ref.svn.html#svn.ref.svn.sw.accept
	public enum UpdateResolveConflicts
	{
		Postpone,       // Take no resolution action at all and instead allow the conflicts to be recorded for future resolution.
		Working,        // Assuming that you've manually handled the conflict resolution, choose the version of the file as it currently stands in your working copy.
		Base,           // Choose the file that was the (unmodified) BASE revision before you tried to integrate changes from the server into your working copy.

		MineConflict,   // Resolve conflicted files by preferring local modifications over the changes fetched from the server in conflicting regions of each file's content.
		TheirsConflict, // Resolve conflicted files by preferring the changes fetched from the server over local modifications in conflicting regions of each file's content.
		MineFull,       // Resolve conflicted files by preserving all local modifications and discarding all changes fetched from the server during the operation which caused the conflict.
		TheirsFull,     // Resolve conflicted files by discarding all local modifications and integrating all changes fetched from the server during the operation which caused the conflict.

		// These need to have an environment variable set to work or pass editor as an argument.
		Edit,           // Open each conflicted file in a text editor for manual resolution of line-based conflicts.
		Launch,         // Launch an interactive merge conflict resolution tool for each conflicted file.
	}

	public enum UpdateOperationResult
	{
		Success,				// Operation succeeded.
		SuccessWithConflicts,   // Update was successful, but some folders/files have conflicts.
		AuthenticationFailed,	// User needs to log in using normal SVN client and save their authentication.
		UnableToConnectError,	// Unable to connect to repository indicating some network or server problems.
		UnknownError,			// Failed for any other reason.
	}


	/// <summary>
	/// Data containing all SVN status knowledge about file or folder.
	/// </summary>
	[Serializable]
	public struct SVNStatusData
	{
		public VCFileStatus Status;
		public VCPropertiesStatus PropertiesStatus;
		public VCTreeConflictStatus TreeConflictStatus;
		public VCSwitchedExternal SwitchedExternalStatus;
		public VCLockStatus LockStatus;
		public VCRemoteFileStatus RemoteStatus;

		public string Path;

		public LockDetails LockDetails;

		public bool IsValid => !string.IsNullOrEmpty(Path);

		public bool IsConflicted =>
			Status == VCFileStatus.Conflicted ||
			PropertiesStatus == VCPropertiesStatus.Conflicted ||
			TreeConflictStatus == VCTreeConflictStatus.TreeConflict;

		public bool Equals(SVNStatusData other)
		{
			return Status == other.Status
				&& PropertiesStatus == other.PropertiesStatus
				&& TreeConflictStatus == other.TreeConflictStatus
				&& SwitchedExternalStatus == other.SwitchedExternalStatus
				&& LockStatus == other.LockStatus
				&& RemoteStatus == other.RemoteStatus
				;
		}

		public override string ToString()
		{
			return $"{Status.ToString()[0]} {Path}";
		}
	}

	/// <summary>
	/// Data containing SVN lock details.
	/// </summary>
	[Serializable]
	public struct LockDetails
	{
		public string Path;
		public string Owner;
		public string Message;
		public string Date;

		public bool IsValid => !string.IsNullOrEmpty(Path);

		public static LockDetails Empty => new LockDetails() {Path = string.Empty, Owner = string.Empty, Message = string.Empty, Date = string.Empty};
	}

	public struct SVNStatusDataOptions
	{
		public enum SearchDepth
		{
			Empty,		// Only top level
			Infinity,	// Recursively all children
		}

		public SearchDepth Depth;
		public bool RaiseError;
		public int Timeout;
		public bool Offline;		// If false it will query the repository for additional data (like locks), hence it is slower.
		public bool FetchLockOwner;	// If file is locked and this is true, another query (per locked file) will be made
									// to the repository to find out the owner's user name. I.e. will execute "svn info [url]"
									// Works only in online mode.
	}

	/// <summary>
	/// Parameters to be used for Log operation.
	/// </summary>
	[Serializable]
	public struct LogParams
	{
		public bool FetchAffectedPaths;
		public bool FetchCommitMessages;
		public bool StopOnCopy;		// NOTE: "StopOnCopy = false" may result in entries that do not match requested path (since they were moved).
		public int Limit;
		public string SearchQuery;  // Search query may have additional "--search" or "--search-and" options. Check the SVN documentation.

		// REVISION or {DATE}. Leave empty for no range limitation.
		public string RangeStart;
		public string RangeEnd;
	}

	/// <summary>
	/// Data containing results for single entry of SVN log operation.
	/// </summary>
	[Serializable]
	public struct LogEntry
	{
		public int Revision;
		public string Author;
		public string Date;

		public string Message;
		public LogPath[] AffectedPaths;		// Paths matching the initially requested path.
		public LogPath[] AllPaths;			// All paths in the log entry.

		public override string ToString()
		{
			return $"Log: {Revision} | {Author} | {AllPaths?.Length ?? 0} Files | {Message?.Count(c => c == '\n') ?? 0} Lines";
		}
	}

	public enum LogPathChange
	{
		Added,
		Deleted,
		Replaced,
		Modified,
	}

	[Serializable]
	public struct LogPath
	{
		public string Path;
		public string CopiedFrom;
		public int CopiedFromRevision;
		public LogPathChange Change;

		public bool Added => Change == LogPathChange.Added;
		public bool Deleted => Change == LogPathChange.Deleted;
		public bool Replaced => Change == LogPathChange.Replaced;
		public bool Modified => Change == LogPathChange.Modified;

		public override string ToString()
		{
			return $"{Change.ToString()[0]} {Path}";
		}
	}

	[Flags]
	public enum AssetType
	{
		Scene = 1 << 3,				// t:Scene
		TerrainData = 1 << 4,		// t:TerrainData

		Prefab = 1 << 5,			// t:Prefab
		Model = 1 << 6,				// t:Model
		Mesh = 1 << 7,				// t:Mesh
		Material = 1 << 8,			// t:Material
		Texture = 1 << 9,			// t:Texture

		Animation = 1 << 12,		// t:AnimationClip
		Animator = 1 << 13,         // t:AnimatorController, t:AnimatorOverrideController

		Script = 1 << 16,			// t:Script
		UIElementsAssets = 1 << 17,
		Shader = 1 << 18,			// t:Shader
		ScriptableObject = 1 << 19,	// t:ScriptableObject


		Audio = 1 << 22,			// t:AudioClip
		Video = 1 << 24,			// t:VideoClip

		TimeLineAssets = 1 << 28,			// t:TimelineAsset

		// Any type that is not mentioned above.
		OtherTypes = 1 << 31,


		PresetCharacterTypes = Prefab | Model | Mesh | Material | Texture | Animation | Animator,
		PresetLevelDesignerTypes = Scene | TerrainData | Prefab | Model | Mesh | Material | Texture,
		PresetUITypes = Scene | Texture | Prefab | Animator | Animation | Script | UIElementsAssets | ScriptableObject,
		PresetScriptingTypes = Prefab | Script | UIElementsAssets | Shader | ScriptableObject,
	}

	/// <summary>
	/// Rules for auto svn locking on asset modification.
	/// </summary>
	[Serializable]
	public struct AutoLockingParameters
	{
		[Tooltip("Target folder to monitor for auto-locking, relative to the project.\n\nExample: \"Assets/Scenes\"")]
		public string TargetFolder;

		[Tooltip("Target asset types to monitor for auto-locking")]
		public AssetType TargetTypes;

		[Tooltip("Target metas of selected asset types as well.")]
		public bool IncludeTargetMetas;

#if UNITY_2020_2_OR_NEWER
		// Because it looks ugly, bad indents for some reason.
		[NonReorderable]
#endif
		[Tooltip("Relative path (contains '/') or asset name to be ignored in the Target Folder.\n\nExample: \"Assets/Scenes/Baked\" or \"_deprecated\"")]
		public string[] Exclude;

		public bool IsValid => !string.IsNullOrEmpty(TargetFolder) && TargetTypes != 0;

		public AutoLockingParameters Sanitized()
		{
			var clone = (AutoLockingParameters)MemberwiseClone();

			clone.TargetFolder = Preferences.SVNPreferencesManager.SanitizeUnityPath(TargetFolder);

			clone.Exclude = Exclude
					.Select(Preferences.SVNPreferencesManager.SanitizeUnityPath)
					.Where(s => !string.IsNullOrEmpty(s))
					.ToArray()
				;

			return clone;
		}
	}

	/// <summary>
	/// Describes location of a Unity project in some branch at the SVN repository.
	/// NOTE: UnityProjectURL can be the same as BranchURL or a sub-folder of it, depending on what you call branches.
	/// </summary>
	[Serializable]
	public struct BranchProject
	{
		public string BranchName;
		public string BranchURL;
		public string BranchRelativePath;
		public string UnityProjectURL;
		public string UnityProjectRelativePath;
	}

	/// <summary>
	/// Parameters used to scan the SVN repository for branches.
	/// </summary>
	[Serializable]
	public struct BranchScanParameters
	{
		[Tooltip("SVN url where scan for branches should start recursively.")]
		public string EntryPointURL;

#if UNITY_2020_2_OR_NEWER
		[NonReorderable]
#endif
		// Entries that must be found in folders to recognize them as a branch. Used as a branch name.
		// Example:
		//	/branches/VariantA/Server			/branches/VariantB/Server
		//	/branches/VariantA/UnityClient		/branches/VariantB/UnityClient
		// In this setup, VariantA and VariantB should be considered the root of the branch, not the UnityClient folder.
		// The recognize entries in this case would be: { "Server", "UnityClient" };
		[Tooltip("Entries to look for in folders to recognize them as branches.")]
		public string[] BranchSignatureRootEntries;

#if UNITY_2020_2_OR_NEWER
		[NonReorderable]
#endif
		// File or folder names excluded during recursive scan.
		[Tooltip("Folder names to exclude during the recursive scan.")]
		public string[] ExcludesFolderNames;

		public bool IsValid => !string.IsNullOrEmpty(EntryPointURL) && BranchSignatureRootEntries.Length > 0;

		public BranchScanParameters Sanitized()
		{
			var clone = (BranchScanParameters) MemberwiseClone();

			clone.EntryPointURL = EntryPointURL.Trim().TrimEnd('/', '\\');

			clone.BranchSignatureRootEntries = BranchSignatureRootEntries
				.Select(s => s.Trim().Replace("/", ""))
				.Where(s => !string.IsNullOrEmpty(s))
				.ToArray()
				;

			clone.ExcludesFolderNames = ExcludesFolderNames
					.Select(s => s.Trim().Replace("/", ""))
					.Where(s => !string.IsNullOrEmpty(s))
					.ToArray()
				;

			return clone;
		}
	}
}
