
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DevLocker.VersionControl.WiseSVN.ContextMenus.Implementation
{
	internal abstract class SVNContextMenusBase
	{
		// Used for the context paths functions below.
		protected abstract string FileArgumentsSeparator { get; }
		protected abstract bool FileArgumentsSurroundQuotes { get; }

		public abstract void CheckChangesAll();
		public abstract void CheckChanges();
		public abstract void Update(string filePath);
		public abstract void UpdateAll();
		public abstract void CommitAll();
		public abstract void CommitSelected();
		public abstract void AddSelected();
		public abstract void RevertAll();
		public abstract void RevertSelected();
		public abstract void ResolveAll();
		public abstract void GetLocks();
		public abstract void ReleaseLocks();
		public abstract void ShowLogAll();
		public abstract void ShowLog();
		public abstract void Blame();
		public abstract void Cleanup();

		protected string GuidsToContextPaths(string[] guids, bool includeMeta)
		{
			if (guids.Length == 0)
				return string.Empty;

			return AssetPathsToContextPaths(guids.Select(AssetDatabase.GUIDToAssetPath), includeMeta);
		}

		protected string GuidToContextPaths(string guid, bool includeMeta)
		{
			return AssetPathToContextPaths(AssetDatabase.GUIDToAssetPath(guid), includeMeta);
		}

		protected string AssetPathsToContextPaths(IEnumerable<string> assetPaths, bool includeMeta)
		{
			if (!assetPaths.Any())
				return string.Empty;

			return string.Join(FileArgumentsSeparator, assetPaths.Select(path => AssetPathToContextPaths(path, includeMeta)));
		}

		protected string AssetPathToContextPaths(string assetPath, bool includeMeta)
		{
			if (string.IsNullOrEmpty(assetPath))
				return PreparePathArg(Path.GetDirectoryName(Application.dataPath));

			var paths = PreparePathArg(assetPath);
			if (includeMeta) {
				paths += FileArgumentsSeparator + PreparePathArg(assetPath + ".meta");
			}

			return paths;
		}

		private string PreparePathArg(string path)
		{
			return FileArgumentsSurroundQuotes
				? '"' + path + '"'
				: path
				;
		}

		// Gets common working path.
		protected static string GetWorkingPath(string[] guids)
		{
			// Find the most common path of the selected assets.
			var paths = guids.Select(AssetDatabase.GUIDToAssetPath).ToList();

			int matchCharIndex = paths[0].Length;

			for (int i = 1; i < paths.Count; i++) {

				matchCharIndex = Mathf.Min(matchCharIndex, paths[i].Length);

				for (int j = 0; j < matchCharIndex; j++)
					if (paths[i][j] != paths[0][j]) {
						matchCharIndex = j;
						break;
					}
			}

			var bestMatch = paths[0].Substring(0, matchCharIndex);

			if (Directory.Exists(bestMatch))
				return bestMatch;

			return Path.GetDirectoryName(bestMatch);
		}
	}
}
