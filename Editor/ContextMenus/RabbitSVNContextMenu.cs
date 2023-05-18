// MIT License Copyright(c) 2022 Filip Slavov, https://github.com/NibbleByte/UnityWiseSVN

using DevLocker.VersionControl.WiseSVN.Shell;
using System.Collections.Generic;
using System.Linq;
using System;
using UnityEngine;

namespace DevLocker.VersionControl.WiseSVN.ContextMenus.Implementation
{
#if UNITY_EDITOR_LINUX
	// TortoiseSVN Commands: https://tortoisesvn.net/docs/release/TortoiseSVN_en/tsvn-automation.html
	internal class RabbitSVNContextMenu : SVNContextMenusBase
	{
		private const string ClientCommand = "rabbitvcs";

		protected override string FileArgumentsSeparator => "*";
		protected override bool FileArgumentsSurroundQuotes => false;

		public override void CheckChanges(IEnumerable<string> assetPaths, bool includeMeta, bool wait = false)
		{
			if (!assetPaths.Any())
				return;

			string pathsArg = AssetPathsToContextPaths(assetPaths, includeMeta);
			if (string.IsNullOrEmpty(pathsArg))
				return;

			var result = ShellUtils.ExecuteCommand(ClientCommand, $"changes \"{pathsArg}\"", wait);
			if (MayHaveRabbitVCSError(result.Error)) {
				Debug.LogError($"SVN Error: {result.Error}");
			}
		}

		public override void DiffChanges(string assetPath, bool wait = false)
		{
			string pathsArg = AssetPathToContextPaths(assetPath, false);
			if (string.IsNullOrEmpty(pathsArg))
				return;

			var result = ShellUtils.ExecuteCommand(ClientCommand, $"diff \"{pathsArg}\"", wait);
			if (MayHaveRabbitVCSError(result.Error)) {
				Debug.LogError($"SVN Error: {result.Error}");
			}
		}

		public override void Update(IEnumerable<string> assetPaths, bool includeMeta, bool wait = false)
		{
			if (!assetPaths.Any())
				return;

			string pathsArg = AssetPathsToContextPaths(assetPaths, includeMeta);
			if (string.IsNullOrEmpty(pathsArg))
				return;

			var result = ShellUtils.ExecuteCommand(ClientCommand, $"update \"{pathsArg}\"", wait);
			if (MayHaveRabbitVCSError(result.Error)) {
				Debug.LogError($"SVN Error: {result.Error}");
			}
		}

		public override void Commit(IEnumerable<string> assetPaths, bool includeMeta, bool wait = false)
		{
			if (!assetPaths.Any())
				return;

			string pathsArg = AssetPathsToContextPaths(assetPaths, includeMeta);
			if (string.IsNullOrEmpty(pathsArg))
				return;

			var result = ShellUtils.ExecuteCommand(ClientCommand, $"commit \"{pathsArg}\"", wait);
			if (MayHaveRabbitVCSError(result.Error)) {
				Debug.LogError($"SVN Error: {result.Error}");
			}
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

			var result = ShellUtils.ExecuteCommand(ClientCommand, $"add \"{pathsArg}\"", wait);
			if (MayHaveRabbitVCSError(result.Error)) {
				Debug.LogError($"SVN Error: {result.Error}");
			}
		}

		public override void Revert(IEnumerable<string> assetPaths, bool includeMeta, bool wait = false)
		{
			if (!assetPaths.Any())
				return;

			string pathsArg = AssetPathsToContextPaths(assetPaths, includeMeta);
			if (string.IsNullOrEmpty(pathsArg))
				return;

			var result = ShellUtils.ExecuteCommand(ClientCommand, $"revert \"{pathsArg}\"", wait);
			if (MayHaveRabbitVCSError(result.Error)) {
				Debug.LogError($"SVN Error: {result.Error}");
			}
		}

		public override void ResolveAll(bool wait = false)
		{
			UnityEditor.EditorUtility.DisplayDialog("Not Supported", "RabbitSVN does not support Resolve All yet.", "OK");
		}

		public override void Resolve(string assetPath, bool wait = false)
		{
			UnityEditor.EditorUtility.DisplayDialog("Not Supported", "RabbitSVN does not support Resolve yet.", "OK");
		}

		public override void GetLocks(IEnumerable<string> assetPaths, bool includeMeta, bool wait = false)
		{
			if (!assetPaths.Any())
				return;

			string pathsArg = AssetPathsToContextPaths(assetPaths, includeMeta);
			if (string.IsNullOrEmpty(pathsArg))
				return;

			var result = ShellUtils.ExecuteCommand(ClientCommand, $"lock \"{pathsArg}\"", wait);
			if (MayHaveRabbitVCSError(result.Error)) {
				Debug.LogError($"SVN Error: {result.Error}");
			}
		}

		public override void ReleaseLocks(IEnumerable<string> assetPaths, bool includeMeta, bool wait = false)
		{
			if (!assetPaths.Any())
				return;

			string pathsArg = AssetPathsToContextPaths(assetPaths, includeMeta);
			if (string.IsNullOrEmpty(pathsArg))
				return;

			var result = ShellUtils.ExecuteCommand(ClientCommand, $"unlock \"{pathsArg}\"", wait);
			if (MayHaveRabbitVCSError(result.Error)) {
				Debug.LogError($"SVN Error: {result.Error}");
			}
		}

		public override void ShowLog(string assetPath, bool wait = false)
		{
			if (string.IsNullOrEmpty(assetPath))
				return;

			string pathsArg = AssetPathToContextPaths(assetPath, false);
			if (string.IsNullOrEmpty(pathsArg))
				return;

			var result = ShellUtils.ExecuteCommand(ClientCommand, $"log \"{pathsArg}\"", wait);
			if (MayHaveRabbitVCSError(result.Error)) {
				Debug.LogError($"SVN Error: {result.Error}");
			}
		}

		public override void Blame(string assetPath, bool wait = false)
		{
			UnityEditor.EditorUtility.DisplayDialog("Not Supported", "RabbitSVN does not support Blame function yet.", "OK");
		}

		public override void Cleanup(bool wait = false)
		{
			var result = ShellUtils.ExecuteCommand(ClientCommand, $"cleanup \"{WiseSVNIntegration.ProjectRootNative}\"", wait);
			if (MayHaveRabbitVCSError(result.Error)) {
				Debug.LogError($"SVN Error: {result.Error}");
			}
		}

		public override void RepoBrowser(string url, bool wait = false)
		{
			if (string.IsNullOrEmpty(url))
				return;

			var result = ShellUtils.ExecuteCommand(ClientCommand, $"browser \"{url}\"", wait);
			if (MayHaveRabbitVCSError(result.Error)) {
				Debug.LogError($"SVN Error: {result.Error}");
			}
		}

		public override void Switch(string localPath, string url, bool wait = false)
		{
			if (string.IsNullOrEmpty(url))
				return;

			var result = ShellUtils.ExecuteCommand(ClientCommand, $"switch \"{url}\"", wait);
			if (MayHaveRabbitVCSError(result.Error)) {
				Debug.LogError($"SVN Error: {result.Error}");
			}
			return;
		}

		private string[] possibleVcsErrorString = new[]{
			"Exception: ",
			"Error: "
		};
		private bool MayHaveRabbitVCSError(string src){
			if(string.IsNullOrWhiteSpace(src)) return false;
			foreach(string str in possibleVcsErrorString){
				if(src.IndexOf(str, StringComparison.OrdinalIgnoreCase) >= 0) return true;
			}
			return false;
		}
	}
#endif
}
