using System.Diagnostics;
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
			public int WaitTimeout;		// WaitTimeout is in milliseconds. -1 means forever. Only if WaitForOutput is true.
			public IShellMonitor Monitor;
		}

		public struct ShellResult
		{
			public string Command;
			public string Args;

			public string Output;
			public string Error;

			public bool HasErrors => !string.IsNullOrEmpty(Error);
		}

		public const string USER_ABORTED_LOG = "User aborted the operation...";

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
			outputReadLineHandler = (sender, args) => {
				// Lock - check the Builder usage at the end.
				lock (outputBuilder) {
					if (args.Data != null) {
						outputBuilder.AppendLine(args.Data);
						if (shellArgs.Monitor != null) {
							shellArgs.Monitor.AppendOutputLine(args.Data);
						}
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
			errorReadLineHandler = (sender, args) => {
				// Lock - check the Builder usage at the end.
				lock (errorBuilder) {
					if (args.Data != null) {
						errorBuilder.AppendLine(args.Data);
						if (shellArgs.Monitor != null) {
							shellArgs.Monitor.AppendErrorLine(args.Data);
						}
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
				if (!process.HasExited) {
					result.Error += $"Command [{shellArgs.Command} {shellArgs.Args}] timed out.";
				}
			}

			process.CancelOutputRead();
			process.OutputDataReceived -= outputReadLineHandler;
			//process.StandardOutput.Close();
			lock (outputBuilder) {
				// When the main thread gets here, the process will not be running (unless it timed out),
				// but the OutputDataReceived thread might still be appending the final strings. Lock it!
				result.Output = outputBuilder.ToString();
			}

			process.CancelErrorRead();
			process.ErrorDataReceived -= errorReadLineHandler;
			//process.StandardError.Close();
			lock (errorBuilder) {
				// Same as above. Concat if error was present.
				result.Error += errorBuilder.ToString();
			}

			if (shellArgs.Monitor != null) {
				shellArgs.Monitor.RequestAbort -= abortHandler;
			}

			// Dispose() (invoking Close()) will wait for the process to finish.
			// If process is stuck, this will hang Unity on recompile / exit.
			// Not calling Dispose() in that regard will leak some resources / processes, but that shouldn't be the normal case anyway.
			if (process.HasExited) {
				// TODO: This still hangs sometimes. Last fix: added process.CancelOutputRead()
				//		 Keep an eye if this keeps happening.
				//		 Useful article: https://newbedev.com/process-sometimes-hangs-while-waiting-for-exit
				process.Dispose();
			}

			return result;
		}
	}
}
