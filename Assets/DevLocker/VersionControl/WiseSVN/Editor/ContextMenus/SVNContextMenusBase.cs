
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace DevLocker.VersionControl.WiseSVN.ContextMenus.Implementation
{
	internal abstract class SVNContextMenusBase
	{
		// Used for the context paths functions below.
		protected abstract string FileArgumentsSeparator { get; }
		protected abstract bool FileArgumentsSurroundQuotes { get; }

		public abstract void CheckChanges(IEnumerable<string> assetPaths, bool includeMeta);
		public abstract void Update(IEnumerable<string> assetPaths, bool includeMeta);
		public abstract void Commit(IEnumerable<string> assetPaths, bool includeMeta);
		public abstract void Add(IEnumerable<string> assetPaths, bool includeMeta);
		public abstract void Revert(IEnumerable<string> assetPaths, bool includeMeta);

		public abstract void GetLocks(IEnumerable<string> assetPaths, bool includeMeta);
		public abstract void ReleaseLocks(IEnumerable<string> assetPaths, bool includeMeta);

		public abstract void ShowLog(string assetPath);

		public abstract void ResolveAll();
		public abstract void Blame(string assetPath);

		public abstract void Cleanup();

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
		protected static string GetWorkingPath(IEnumerable<string> assetPaths)
		{
			// Find the most common path of the selected assets.
			var paths = assetPaths.ToList();

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
