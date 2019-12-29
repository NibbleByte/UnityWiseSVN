using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace DevLocker.VersionControl.SVN
{
	public class ShellUtils
	{
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
			return ExecuteCommand(command, args, waitForOutput, -1, logger);
		}

		public static ShellResult ExecuteCommand(string command, string args, int waitTimeout, StringBuilder logger = null)
		{
			return ExecuteCommand(command, args, true, waitTimeout, logger);
		}

		// waitTimeout is in milliseconds. -1 means forever.
		public static ShellResult ExecuteCommand(string command, string args, bool waitForOutput, int waitTimeout, StringBuilder logger = null)
		{
			ShellResult result = new ShellResult();

			result.command = command;
			result.args = args;
			if (logger != null) {
				logger.AppendLine(command + " " + args);
			}

			ProcessStartInfo processStartInfo = new ProcessStartInfo(command, args);
			processStartInfo.RedirectStandardOutput = true;
			processStartInfo.RedirectStandardError = true;
			processStartInfo.CreateNoWindow = true;
			processStartInfo.WindowStyle = ProcessWindowStyle.Hidden;
			processStartInfo.UseShellExecute = false;

			Process process;
			try {
				process = Process.Start(processStartInfo);

			} catch (System.Exception ex) {
				// Most probably file not found.
				result.error = ex.ToString();

				if (logger != null) {
					logger.AppendLine("> " + result.error);
				}

				return result;
			}

			if (waitForOutput) {

				if (waitTimeout < 0) {

					using (StreamReader streamReader = process.StandardOutput) {
						result.output = streamReader.ReadToEnd();
					}

					using (StreamReader streamReader = process.StandardError) {
						result.error = streamReader.ReadToEnd();
					}

				} else {

					var outTask = Task.Run(() => process.StandardOutput.ReadToEndAsync());
					var errTask = Task.Run(() => process.StandardError.ReadToEndAsync());

					if (process.WaitForExit(waitTimeout)) {
						result.output = outTask.Result.TrimEnd('\r', '\n');
						result.error = errTask.Result.TrimEnd('\r', '\n');
					} else {
						result.output = string.Empty;
						result.error = $"Command [{command} {args}] timed out.";
					}
				}




			} else {

				result.output = string.Empty;
				result.error = string.Empty;
			}


			if (result.HasErrors && logger != null) {
				logger.AppendLine("> " + result.error);
			}

			return result;
		}
	}
}
