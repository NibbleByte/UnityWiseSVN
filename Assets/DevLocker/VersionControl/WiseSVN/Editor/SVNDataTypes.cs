using System;

namespace DevLocker.VersionControl.WiseSVN
{
	// Stolen from UVC plugin.
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

	public enum VCProperty
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

	public enum LockOperationResult
	{
		Success,		// Operation succeeded.
		LockedByOther,	// File is locked by another working copy (may be the same user). Use Force to enforce the operation.
		Failed,			// Failed for some reason.
	}

	public enum CommitOperationResult
	{
		Success,				// Operation succeeded.
		OutOfDateError,			// Some folders/files have pending changes in the repository. Update them before trying to commit.
		ConflictsError,			// Some folders/files have conflicts. Clear them before trying to commit.
		UnversionedError,		// Can't commit unversioned files directly. Add them before trying to commit. Recursive skips unversioned files.
		UnableToConnectError,	// Unable to connect to repository indicating some network or server problems.
		PrecommitHookError,		// Precommit hook denied the commit on the server side. Talk with your administrator about your commit company policies. Example: always commit with a valid message.
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
		UnableToConnectError,	// Unable to connect to repository indicating some network or server problems.
		UnknownError,			// Failed for any other reason.
	}


	[Serializable]
	public struct SVNStatusData
	{
		public VCFileStatus Status;
		public VCProperty PropertyStatus;
		public VCTreeConflictStatus TreeConflictStatus;
		public VCLockStatus LockStatus;
		public VCRemoteFileStatus RemoteStatus;

		public string Path;

		public LockDetails LockDetails;

		public bool IsValid => !string.IsNullOrEmpty(Path);

		public bool IsConflicted =>
			Status == VCFileStatus.Conflicted ||
			PropertyStatus == VCProperty.Conflicted ||
			TreeConflictStatus == VCTreeConflictStatus.TreeConflict;

		public bool Equals(SVNStatusData other)
		{
			return Status == other.Status
				&& PropertyStatus == other.PropertyStatus
				&& TreeConflictStatus == other.TreeConflictStatus
				&& LockStatus == other.LockStatus
				&& RemoteStatus == other.RemoteStatus
				;
		}
	}

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

		public SVNStatusDataOptions(SearchDepth depth)
		{
			Depth = depth;
			RaiseError = true;
			Timeout = WiseSVNIntegration.COMMAND_TIMEOUT;
			Offline = true;
			FetchLockOwner = false;
		}
	}
}
