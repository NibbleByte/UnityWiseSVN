using System;

namespace DevLocker.VersionControl.SVN
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
		//External,
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
		OverlayIcons = 1 << 4,
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
		public bool Offline;	// If false it will query the repository for additional data (like locks), hence it is slower.

		public SVNStatusDataOptions(SearchDepth depth)
		{
			Depth = depth;
			RaiseError = true;
			Timeout = SVNSimpleIntegration.COMMAND_TIMEOUT;
			Offline = true;
		}
	}
}
