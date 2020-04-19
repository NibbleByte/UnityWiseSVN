using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace DevLocker.VersionControl.WiseSVN
{
	public class ShellUtils
	{
		public struct ShellArgs
		{
			public string Command;
			public string Args;
			public string WorkingDirectory;
			public bool WaitForOutput;
			public int WaitTimeout;		// WaitTimeout is in milliseconds. -1 means forever. Only if WaitForOutput is true.
			public StringBuilder Logger;
		}

		public struct ShellResult
		{
			public string command;
			public string args;

			public string output;
			public string error;

			public bool HasErrors => !string.IsNullOrEmpty(error);
		}

		public static ShellResult ExecuteCommand(string command, string args)
		{
			return ExecuteCommand(command, args, true);
		}

		public static ShellResult ExecuteCommand(string command, string args, StringBuilder logger)
		{
			return ExecuteCommand(command, args, true, logger);
		}

		public static ShellResult ExecuteCommand(string command, string args, bool waitForOutput, StringBuilder logger = null)
		{
			return ExecuteCommand(new ShellArgs() {
				Command = command,
				Args = args,
				WaitForOutput = waitForOutput,
				WaitTimeout = -1,
				Logger = logger
			});
		}

		public static ShellResult ExecuteCommand(string command, string args, int waitTimeout, StringBuilder logger = null)
		{
			return ExecuteCommand(new ShellArgs() {
				Command = command,
				Args = args,
				WaitForOutput = true,
				WaitTimeout = waitTimeout,
				Logger = logger
			});
		}

		// WaitTimeout is in milliseconds. -1 means forever.
		public static ShellResult ExecuteCommand(ShellArgs shellArgs)
		{
			shellArgs.Command = shellArgs.Command ?? string.Empty;
			shellArgs.Args = shellArgs.Args ?? string.Empty;
			shellArgs.WorkingDirectory = shellArgs.WorkingDirectory ?? string.Empty;

			ShellResult result = new ShellResult();

			result.command = shellArgs.Command;
			result.args = shellArgs.Args;
			if (shellArgs.Logger != null) {
				shellArgs.Logger.AppendLine(shellArgs.Command + " " + shellArgs.Args);
			}

			ProcessStartInfo processStartInfo = new ProcessStartInfo(shellArgs.Command, shellArgs.Args);
			processStartInfo.RedirectStandardOutput = true;
			processStartInfo.RedirectStandardError = true;
			processStartInfo.CreateNoWindow = true;
			processStartInfo.WindowStyle = ProcessWindowStyle.Hidden;
			processStartInfo.UseShellExecute = false;
			processStartInfo.WorkingDirectory = shellArgs.WorkingDirectory;

			Process process;
			try {
				process = Process.Start(processStartInfo);

			} catch (System.Exception ex) {
				// Most probably file not found.
				result.error = ex.ToString();

				if (shellArgs.Logger != null) {
					shellArgs.Logger.AppendLine("> " + result.error);
				}

				return result;
			}

			if (shellArgs.WaitForOutput) {

				if (shellArgs.WaitTimeout < 0) {

					using (StreamReader streamReader = process.StandardOutput) {
						result.output = streamReader.ReadToEnd();
					}

					using (StreamReader streamReader = process.StandardError) {
						result.error = streamReader.ReadToEnd();
					}

				} else {

					var outTask = Task.Run(() => process.StandardOutput.ReadToEndAsync());
					var errTask = Task.Run(() => process.StandardError.ReadToEndAsync());

					if (process.WaitForExit(shellArgs.WaitTimeout)) {
						result.output = outTask.Result.TrimEnd('\r', '\n');
						result.error = errTask.Result.TrimEnd('\r', '\n');
					} else {
						result.output = string.Empty;
						result.error = $"Command [{shellArgs.Command} {shellArgs}] timed out.";
					}
				}




			} else {

				result.output = string.Empty;
				result.error = string.Empty;
			}


			if (result.HasErrors && shellArgs.Logger != null) {
				shellArgs.Logger.AppendLine("> " + result.error);
			}

			return result;
		}
	}
}
