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
		//Incomplete,
		//Merged,
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
		public string Owner;
		public string Message;
		public string Date;

		public static LockDetails Empty => new LockDetails() {Owner = string.Empty, Message = string.Empty, Date = string.Empty};
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
