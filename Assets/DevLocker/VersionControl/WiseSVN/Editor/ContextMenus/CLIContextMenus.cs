// MIT License Copyright(c) 2022 Filip Slavov, https://github.com/NibbleByte/UnityWiseSVN

using DevLocker.VersionControl.WiseSVN.Shell;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DevLocker.VersionControl.WiseSVN.ContextMenus.Implementation
{
	/// <summary>
	/// Fall-back context menus that pop an editor window with command to be executed. User can modify the command and see the output.
	/// </summary>
	internal class CLIContextMenus : SVNContextMenusBase
	{
		protected override string FileArgumentsSeparator => "\n";
		protected override bool FileArgumentsSurroundQuotes => true;

		public override void CheckChanges(IEnumerable<string> assetPaths, bool includeMeta, bool wait = false)
		{
			if (!assetPaths.Any())
				return;

			string pathsArg = AssetPathsToContextPaths(assetPaths, includeMeta);
			if (string.IsNullOrEmpty(pathsArg))
				return;

			CLIContextWindow.Show($"diff \n{pathsArg}", true);
		}

		public override void DiffChanges(string assetPath, bool wait = false)
		{
			string pathsArg = AssetPathToContextPaths(assetPath, false);
			if (string.IsNullOrEmpty(pathsArg))
				return;

			CLIContextWindow.Show($"diff \n{pathsArg}", true);
		}

		public override void Update(IEnumerable<string> assetPaths, bool includeMeta, bool wait = false)
		{
			if (!assetPaths.Any())
				return;

			// NOTE: @ is added at the end of path, to avoid problems when file name contains @, and SVN mistakes that as "At revision" syntax".
			//		https://stackoverflow.com/questions/757435/how-to-escape-characters-in-subversion-managed-file-names

			string pathsArg = AssetPathsToContextPaths(assetPaths, includeMeta);
			if (string.IsNullOrEmpty(pathsArg))
				return;

			CLIContextWindow.Show($"update --depth infinity --accept postpone\n{pathsArg}", true);
		}

		public override void Commit(IEnumerable<string> assetPaths, bool includeMeta, bool wait = false)
		{
			if (!assetPaths.Any())
				return;

			string pathsArg = AssetPathsToContextPaths(assetPaths, includeMeta);
			if (string.IsNullOrEmpty(pathsArg))
				return;

			CLIContextWindow.Show($"commit --depth infinity -m \"\"\n{pathsArg}", false);
		}



		public override void Add(IEnumerable<string> assetPaths, bool includeMeta, bool wait = false)
		{
			if (!assetPaths.Any())
				return;

			foreach (var path in assetPaths) {
				if (!WiseSVNIntegration.CheckAndAddParentFolderIfNeeded(path, true))
					return;
			}

			// Don't give versioned metas, as tortoiseSVN doesn't like it.
			var metas = assetPaths
				.Select(path => path + ".meta")
				.Where(path => WiseSVNIntegration.GetStatus(path).Status == VCFileStatus.Unversioned)
				;

			string pathsArg = AssetPathsToContextPaths(includeMeta ? assetPaths.Concat(metas) : assetPaths, false);
			if (string.IsNullOrEmpty(pathsArg))
				return;

			CLIContextWindow.Show($"add --depth infinity\n{pathsArg}", true);
		}

		public override void Revert(IEnumerable<string> assetPaths, bool includeMeta, bool wait = false)
		{
			if (!assetPaths.Any())
				return;

			string pathsArg = AssetPathsToContextPaths(assetPaths, includeMeta);
			if (string.IsNullOrEmpty(pathsArg))
				return;

			CLIContextWindow.Show($"revert --depth infinity\n{pathsArg}", false);
		}



		public override void ResolveAll(bool wait = false)
		{
			CLIContextWindow.Show($"resolve --depth infinity --accept TYPE_IN_OPTION\n", false);
		}

		public override void Resolve(string assetPath, bool wait = false)
		{
			DiffChanges(assetPath, wait);
		}



		public override void GetLocks(IEnumerable<string> assetPaths, bool includeMeta, bool wait = false)
		{
			if (!assetPaths.Any())
				return;

			string pathsArg = AssetPathsToContextPaths(assetPaths, includeMeta);
			if (string.IsNullOrEmpty(pathsArg))
				return;

			CLIContextWindow.Show($"lock -m \"\"\n{pathsArg}", false);
		}

		public override void ReleaseLocks(IEnumerable<string> assetPaths, bool includeMeta, bool wait = false)
		{
			if (!assetPaths.Any())
				return;

			string pathsArg = AssetPathsToContextPaths(assetPaths, includeMeta);
			if (string.IsNullOrEmpty(pathsArg))
				return;

			CLIContextWindow.Show($"unlock\n{pathsArg}", true);
		}

		public override void ShowLog(string assetPath, bool wait = false)
		{
			if (string.IsNullOrEmpty(assetPath))
				return;

			string pathsArg = AssetPathToContextPaths(assetPath, false);
			if (string.IsNullOrEmpty(pathsArg))
				return;

			CLIContextWindow.Show($"log \n{pathsArg}", true);
		}



		public override void Blame(string assetPath, bool wait = false)
		{
			if (string.IsNullOrEmpty(assetPath))
				return;

			string pathsArg = AssetPathToContextPaths(assetPath, false);
			if (string.IsNullOrEmpty(pathsArg))
				return;

			CLIContextWindow.Show($"blame \n{pathsArg}", true);
		}



		public override void Cleanup(bool wait = false)
		{
			CLIContextWindow.Show($"cleanup", true);
		}


		public override void RepoBrowser(string url, bool wait = false)
		{
			if (string.IsNullOrEmpty(url))
				return;

			CLIContextWindow.Show($"list\n\"{url}\"", true);
		}

		public override void Switch(string localPath, string url, bool wait = false)
		{
			if (string.IsNullOrEmpty(localPath) || string.IsNullOrEmpty(url))
				return;

			// TODO: Test?
			CLIContextWindow.Show($"switch\n\"{url}\"\n\"{localPath}\"", false);
		}
	}
}
