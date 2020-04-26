using DevLocker.VersionControl.WiseSVN.Shell;
using System.Collections.Concurrent;
using System.Threading;
using UnityEditor;

namespace DevLocker.VersionControl.WiseSVN
{
	/// <summary>
	/// Simple promise class, useful in editor environment where there are no coroutines.
	/// Will execute task in another thread and when done, will call the Completed event on the main thread.
	/// Will pass on the result. User handler can track progress.
	/// Can be passed as ShellUtils.IShellMonitor to the user handler. In return it calls events to notify the user for read output.
	/// Aborting depends on the user handler.
	/// NOTE: If assembly reload happens, task will be lost, complete handler won't be called.
	/// </summary>
	public class SVNAsyncOperation<TResult> : IShellMonitor
	{
		public delegate TResult OperationHandler(SVNAsyncOperation<TResult> operation);
		public delegate void OperationCompleteHandler(SVNAsyncOperation<TResult> operation);
		public delegate void OutputLineEventHandler(string line);

		public TResult Result { get; private set; }			// Result of the operation.
		public bool HasFinished { get; private set; }		// Has the task (user handler) finished.

		public float Progress = 0f;	// Can be updated by the operation user handler on the other thread.

		public event OperationCompleteHandler Completed;    // Will be called when task is finished. Get the result from the Result field.

		// These events are called when a line was read from the output of the svn process stream.
		// These are guaranteed to be called on the Unity thread (editor update).
		public event OutputLineEventHandler CommandOutput;
		public event OutputLineEventHandler StandardOutput;
		public event OutputLineEventHandler ErrorOutput;
		public event OutputLineEventHandler AnyOutput;	// For convenience - called for any of the above.

		public bool AbortRequested { get; private set; }

		private OperationHandler m_OperationHandler;
		private Thread m_Thread;

		private readonly ConcurrentQueue<string> m_Commands = new ConcurrentQueue<string>();
		private readonly ConcurrentQueue<string> m_StandardOutput = new ConcurrentQueue<string>();
		private readonly ConcurrentQueue<string> m_ErrorOutput = new ConcurrentQueue<string>();

		public SVNAsyncOperation(OperationHandler operationHandler)
		{
			m_OperationHandler = operationHandler;
		}

		/// <summary>
		/// Create and run asynchronous operation.
		/// Subscribe to returned operation for completion.
		/// </summary>
		public static SVNAsyncOperation<TResult> Start(OperationHandler operationHandler)
		{
			var op = new SVNAsyncOperation<TResult>(operationHandler);
			op.Start();
			return op;
		}

		/// <summary>
		/// Manually start asynchronous operation.
		/// </summary>
		public void Start()
		{
			m_Thread = new Thread(() => {
				Result = m_OperationHandler(this);
				Progress = 1.0f;
				HasFinished = true;
			});

			m_Thread.Start();

			EditorApplication.update += Update;
			AssemblyReloadEvents.beforeAssemblyReload += AssemblyReload;
		}

		/// <summary>
		/// Abort asynchronous operation.
		/// WARNING: this may cause data corruption, use with caution.
		/// </summary>
		/// <param name="kill">Should process be killed or asked politely.</param>
		public void Abort(bool kill)
		{
			AbortRequested = true;
			RequestAbort?.Invoke(kill);
		}

		private void Update()
		{
			string line;
			while(m_Commands.TryDequeue(out line)) {
				CommandOutput?.Invoke(line);
				AnyOutput?.Invoke(line);
			}

			while(m_StandardOutput.TryDequeue(out line)) {
				StandardOutput?.Invoke(line);
				AnyOutput?.Invoke(line);
			}

			while(m_ErrorOutput.TryDequeue(out line)) {
				ErrorOutput?.Invoke(line);
				AnyOutput?.Invoke(line);
			}

			if (HasFinished) {
				EditorApplication.update -= Update;
				AssemblyReloadEvents.beforeAssemblyReload -= AssemblyReload;

				Completed?.Invoke(this);
			};
		}

		private void AssemblyReload()
		{
			EditorApplication.update -= Update;
			AssemblyReloadEvents.beforeAssemblyReload -= AssemblyReload;

			// Do it before Unity does it. Cause Unity aborts the thread badly sometimes :(
			if (m_Thread.IsAlive) {
				m_Thread.Abort();
			}
		}

		// These methods are most likely called from another thread.
		#region ShellUtils.IShellMonitor

		/// <summary>
		/// Used by the ShellUtils API to log data.
		/// </summary>
		public void AppendCommand(string command, string args)
		{
			m_Commands.Enqueue($"{command} {args}");
		}

		/// <summary>
		/// Used by the ShellUtils API to log data.
		/// </summary>
		public void AppendOutputLine(string line)
		{
			m_StandardOutput.Enqueue(line);
		}

		/// <summary>
		/// Used by the ShellUtils API to log data.
		/// </summary>
		public void AppendErrorLine(string line)
		{
			m_ErrorOutput.Enqueue(line);
		}

		/// <summary>
		/// Used by the ShellUtils API to log data.
		/// </summary>
		public event ShellRequestAbortEventHandler RequestAbort;
		#endregion
	}
}
