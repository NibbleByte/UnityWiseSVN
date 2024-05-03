// MIT License Copyright(c) 2022 Filip Slavov, https://github.com/NibbleByte/UnityWiseSVN

using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace DevLocker.VersionControl.WiseSVN.Shell
{
	public delegate void ShellRequestAbortEventHandler(bool kill);

	/// <summary>
	/// Using this interface the user can monitor what is the output of the command and even abort it.
	/// You can use one monitor on multiple commands in a row.
	/// </summary>
	public interface IShellMonitor
	{
		// NOTE: These methods can be called from a different thread!
		void AppendCommand(string command, string args);    // Append shell command + arguments.
		void AppendOutputLine(string line);                 // Append line from the output stream.
		void AppendErrorLine(string line);                  // Append line from the error stream.



		bool AbortRequested { get; }						// If true, all subsequent commands will abort immediately.
		event ShellRequestAbortEventHandler RequestAbort;   // Invoked when user requests abort of the operation.
															// Kill will terminate the process immediately. If false, it will ask politely.
	}

	public class ShellUtils
	{
		public struct ShellArgs
		{
			public string Command;
			public string Args;
			public string WorkingDirectory;
			public bool WaitForOutput;
			public int WaitTimeout;     // WaitTimeout is in milliseconds. -1 means forever. Only if WaitForOutput is true.
			public bool SkipTimeoutError;
			public IShellMonitor Monitor;
		}

		public struct ShellResult
		{
			public string Command;
			public string Args;

			public string Output;
			public string Error;
			public int ErrorCode;

			public int ProcessId;

			public bool HasErrors => !string.IsNullOrWhiteSpace(Error) || ErrorCode != 0;
		}

		public const string USER_ABORTED_LOG = "User aborted the operation...";

		// If result error message contains this string token, the operation was interrupted due to time out.
		public const string TIME_OUT_ERROR_TOKEN = "[ERR_TIME_OUT]";

		public static bool IsProcessAlive(int processId)
		{
			try {
				return !Process.GetProcessById(processId)?.HasExited ?? false;

			} catch (System.ArgumentException) {
				// Process is missing - it finished. Do nothing.
				return false;
			}
		}

		public static ShellResult ExecuteCommand(string command, string args)
		{
			return ExecuteCommand(command, args, true);
		}

		public static ShellResult ExecuteCommand(string command, string args, IShellMonitor monitor)
		{
			return ExecuteCommand(command, args, true, monitor);
		}

		public static ShellResult ExecuteCommand(string command, string args, bool waitForOutput, IShellMonitor monitor = null)
		{
			return ExecuteCommand(new ShellArgs() {
				Command = command,
				Args = args,
				WaitForOutput = waitForOutput,
				WaitTimeout = -1,
				Monitor = monitor
			});
		}

		public static ShellResult ExecuteCommand(string command, string args, int waitTimeout, IShellMonitor monitor = null)
		{
			return ExecuteCommand(new ShellArgs() {
				Command = command,
				Args = args,
				WaitForOutput = true,
				WaitTimeout = waitTimeout,
				Monitor = monitor
			});
		}

		public static ShellResult ExecuteCommand(string command, string args, string workingDirectory, int waitTimeout, IShellMonitor monitor = null)
		{
			return ExecuteCommand(new ShellArgs() {
				Command = command,
				Args = args,
				WaitForOutput = true,
				WaitTimeout = waitTimeout,
				WorkingDirectory = workingDirectory,
				Monitor = monitor
			});
		}

		public static ShellResult ExecuteCommand(ShellArgs shellArgs)
		{
			shellArgs.Command = shellArgs.Command ?? string.Empty;
			shellArgs.Args = shellArgs.Args ?? string.Empty;
			shellArgs.WorkingDirectory = shellArgs.WorkingDirectory ?? string.Empty;

			ShellResult result = new ShellResult();

			result.Command = shellArgs.Command;
			result.Args = shellArgs.Args;
			result.Output = string.Empty;
			result.Error = string.Empty;
			result.ErrorCode = 0;

			if (shellArgs.Monitor != null) {

				if (shellArgs.Monitor.AbortRequested) {
					result.Error = USER_ABORTED_LOG;
					return result;
				}

				shellArgs.Monitor.AppendCommand(shellArgs.Command, shellArgs.Args);
			}

			ProcessStartInfo processStartInfo = new ProcessStartInfo(shellArgs.Command, shellArgs.Args);
			processStartInfo.RedirectStandardOutput = true;
			processStartInfo.RedirectStandardError = true;
			processStartInfo.RedirectStandardInput = true;
			processStartInfo.CreateNoWindow = true;
			processStartInfo.WindowStyle = ProcessWindowStyle.Hidden;
			processStartInfo.UseShellExecute = false;
			processStartInfo.WorkingDirectory = shellArgs.WorkingDirectory;

			Process process;
			try {
				process = Process.Start(processStartInfo);
				// Input is not supported so close it directly. Prevents hangs when CLI prompts the user for something (authentication for example).
				process.StandardInput.Close();

				result.ProcessId = process.Id;

			} catch (System.Exception ex) {
				// Most probably file not found.
				result.Error = ex.ToString();

				if (shellArgs.Monitor != null) {
					shellArgs.Monitor.AppendErrorLine(result.Error);
				}

				return result;
			}

			// Run and forget...
			// TODO: No Process.Dispose() called - leak?!
			if (!shellArgs.WaitForOutput) {
				result.Output = string.Empty;
				result.Error = string.Empty;

				return result;
			}


			//
			// Handle aborting.
			//
			ShellRequestAbortEventHandler abortHandler = null;
			if (shellArgs.Monitor != null) {
				abortHandler = (bool kill) => {

					shellArgs.Monitor?.AppendErrorLine(USER_ABORTED_LOG);

					// TODO: Is this thread safe?
					if (kill) {
						process.Kill();
					} else {
						process.CloseMainWindow();
					}
				};

				shellArgs.Monitor.RequestAbort += abortHandler;
			}


			//
			// Subscribe for standard output.
			//
			StringBuilder outputBuilder = new StringBuilder();
			DataReceivedEventHandler outputReadLineHandler = null;
			bool outputEndReached = false;
			outputReadLineHandler = (sender, args) => {
				// Lock - check the Builder usage at the end.
				lock (outputBuilder) {
					if (args.Data != null) {
						outputBuilder.AppendLine(args.Data);
						if (shellArgs.Monitor != null) {
							shellArgs.Monitor.AppendOutputLine(args.Data);
						}

					} else {
						// End of stream reached
						Volatile.Write(ref outputEndReached, true);	// Not sure if needed.
					}
				}
			};
			process.OutputDataReceived += outputReadLineHandler;
			process.BeginOutputReadLine();


			//
			// Subscribe for error output.
			//
			StringBuilder errorBuilder = new StringBuilder();
			DataReceivedEventHandler errorReadLineHandler = null;
			bool errorEndReached = false;
			errorReadLineHandler = (sender, args) => {
				// Lock - check the Builder usage at the end.
				lock (errorBuilder) {
					if (args.Data != null) {
						errorBuilder.AppendLine(args.Data);
						if (shellArgs.Monitor != null) {
							shellArgs.Monitor.AppendErrorLine(args.Data);
						}
					} else {
						// End of stream reached
						Volatile.Write(ref errorEndReached, true);	// Not sure if needed.
					}
				}
			};
			process.ErrorDataReceived += errorReadLineHandler;
			process.BeginErrorReadLine();


			if (shellArgs.WaitTimeout < 0) {
				process.WaitForExit();

			} else {
				Stopwatch stopwatch = new Stopwatch();
				stopwatch.Start();
				while(!process.HasExited && stopwatch.ElapsedMilliseconds < shellArgs.WaitTimeout) {
					Thread.Sleep(20);
				}
				stopwatch.Stop();

				// If process is still running, the timeout kicked in.
				if (!process.HasExited && !shellArgs.SkipTimeoutError) {
					string timeoutError = $"Command [{shellArgs.Command} {shellArgs.Args}] timed out. {TIME_OUT_ERROR_TOKEN}";
					if (shellArgs.Monitor != null) {
						shellArgs.Monitor.AppendErrorLine(timeoutError);
					}
					result.Error += timeoutError;
				}
			}

			int waitStreamTries = 0; // Keep it here for debugging, if Dispose() hangs.
			if (process.HasExited) {
				// Sometimes stream is still pending even when process has exited. Example: git status --porcelain -z "Assets"
				// Wait a bit for the stream to catch-up.
				// Receiving null means stream ended.
				const int waitStreamTriesMax = 5;
				for (; waitStreamTries < waitStreamTriesMax; ++waitStreamTries) {
					Volatile.Read(ref outputEndReached);	// Not sure if needed.
					Volatile.Read(ref errorEndReached);		// Not sure if needed.

					if (outputEndReached && errorEndReached)
						break;

					Thread.Sleep(10);
				}
			}

			process.CancelOutputRead();
			process.OutputDataReceived -= outputReadLineHandler;
			//process.StandardOutput.Close();	// Doesn't work with async event workflow.
			lock (outputBuilder) {
				// When the main thread gets here, the process will not be running (unless it timed out),
				// but the OutputDataReceived thread might still be appending the final strings. Lock it!
				result.Output = outputBuilder.ToString().Replace("\r\n", "\n").Trim('\n');	// Don't trim spaces, they matter.
			}

			process.CancelErrorRead();
			process.ErrorDataReceived -= errorReadLineHandler;
			//process.StandardError.Close();	// Doesn't work with async event workflow.
			lock (errorBuilder) {
				// Same as above. Concat if error was present.
				result.Error += errorBuilder.ToString().Replace("\r\n", "\n").Trim('\n');
			}

			if (shellArgs.Monitor != null) {
				shellArgs.Monitor.RequestAbort -= abortHandler;
			}

			// Dispose() (invoking Close()) will wait for the process to finish.
			// If process is stuck, this will hang Unity on recompile / exit.
			// Not calling Dispose() in that regard will leak some resources / processes, but that shouldn't be the normal case anyway.
			if (process.HasExited) {

				// If the process crashes, is killed or fails to start (on OSX) it won't have error output. Check the exit code for such cases.
				// Example: x64 executable can't run on arm64 system.
				if (string.IsNullOrEmpty(result.Error) && process.ExitCode != 0) {
					result.Error = $"Process failed with exit code {process.ExitCode}.";
					result.ErrorCode = process.ExitCode;

					if (shellArgs.Monitor != null) {
						shellArgs.Monitor.AppendErrorLine(result.Error);
					}
				}

				// HACK: Dispose sometimes hangs for unknown reason. Hope this helps. Let me know if another hang is experienced.
				// EDIT: Sleep didn't help - it got stuck on simple command GetWorkingBranchDivergingCommit(), while reloading assembly.
				//if (string.IsNullOrWhiteSpace(result.Output) && string.IsNullOrWhiteSpace(result.Error)) {
				//	Thread.Sleep(50);
				//}

				process.Dispose();

				// This still hangs sometimes. Fixes tried:
				// - added process.CancelOutputRead()
				// - Sleep on empty result. I think it got stuck on empty result from GetStatus() for a file.
				// - Wait for stream end for real!
				// Useful article: https://newbedev.com/process-sometimes-hangs-while-waiting-for-exit
				//
				// UnityEditor.EditorApplication.isCompiling - Not thead-safe? Better not use it, although no exception.
				//
				// Last hang encountered: processing a lot of assets after migration of Unity version. Unity hang for a lot of time.
				// Breaking in with debugger yielded this stack:
				//
				// [Native Transition]
				// WaitHandle.WaitOneNative()
				// WaitHandle.InternalWaitOne()
				// WaitHandle.WaitOne()
				// WaitHandle.WaitOne()
				// WaitHandle.WaitOne()
				// AsyncStreamReader.Dispose()
				// AsyncStreamReader.Close()
				// Process.Close()
				// Process.Dispose()
				// Component.Dispose()
				// ShellUtils.ExecuteCommand()
				// ShellUtils.ExecuteCommand()
				// WiseSVNIntegration.GetStatuses()
				// WiseSVNIntegration.GetStatus()
				// SVNStatusesDatabase.PostProcessAssets()
				// SVNStatusesDatabaseAssetPostprocessor.OnPostprocessAllAssets()
				// ...
				// VisualEffectAssetModificationProcessor.OnWillSaveAssets()
				// ...
				// AssetModificationProcessorInternal.OnWillSaveAssets()
				// ...
				// MaterialPostprocessor.SaveAssetsToDisk()
				// MaterialReimporter.<> c.< RegisterUpgraderReimport > b__2_0()
				// EditorApplication.Internal_CallUpdateFunctions()
				//
				// The stream Dispose() kept waiting for signal. The wait handler kept returning WaitTimeout:
				// https://github.com/microsoft/referencesource/blob/51cf7850defa8a17d815b4700b67116e3fa283c2/mscorlib/system/threading/waithandle.cs#L252
				//
				// Alternative: create a thread to dispose processes in a concurrent queue. If it hangs, nobody would care (may prevent Unity from closing in Batch mode?).
			}

			return result;
		}

		public static void ExecutePrompt(string command, string args, string workingDirectory = null, string hint = null)
		{
#if UNITY_EDITOR_OSX
			// OSX doesn't open terminal window even with UseShellExecute = false.
			// Write command in a bash script, then open the Terminal application itself feeding it the script.

			workingDirectory = workingDirectory ?? ".";
			string scriptPath = $"{workingDirectory}/.Prompt_Command.sh";
			string scriptContents = $"clear\n" +
				$"echo \"\n{command} {args.Replace("\"","\\\"")}\n{hint}\"\n" +
				$"cd \"{workingDirectory}\"\n" +
				$"{command} {args}"
				;
			File.WriteAllText(scriptPath, scriptContents);
			Process.Start("chmod", $"+x \"{scriptPath}\"");	 // Must be executable.
			Process process = Process.Start("/System/Applications/Utilities/Terminal.app/Contents/MacOS/Terminal", $"\"{scriptPath}\"");

			// Waiting for terminal to close doesn't work - script finishes, but terminal remains open, which may be confusing for the user.
			Thread.Sleep(1000);

			File.Delete(scriptPath);

#else

			ProcessStartInfo processStartInfo = new ProcessStartInfo(command, args);
			processStartInfo.WindowStyle = ProcessWindowStyle.Normal;
			processStartInfo.CreateNoWindow = false;
			processStartInfo.UseShellExecute = true;
			processStartInfo.WorkingDirectory = workingDirectory ?? string.Empty;

			Process process = Process.Start(processStartInfo);
			process.WaitForExit();
			process.Dispose();
#endif
		}
	}
}
