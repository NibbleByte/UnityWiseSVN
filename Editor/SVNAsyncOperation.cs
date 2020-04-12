using System.Threading;
using UnityEditor;

namespace DevLocker.VersionControl.WiseSVN
{
	// Simple promise class, useful in editor environment where there are no coroutines.
	// Will execute task in another thread and when done, will call the Completed event on the main thread.
	// Will pass on the result. User handler can track progress.
	// NOTE: If assembly reload happens, task will be lost, complete handler won't be called.
	public class SVNAsyncOperation<TResult>
	{
		public delegate TResult OperationHandler(SVNAsyncOperation<TResult> operation);
		public delegate void OperationCompleteHandler(SVNAsyncOperation<TResult> operation);

		public TResult Result { get; private set; }			// Result of the operation.
		public bool HasFinished { get; private set; }		// Has the task (user handler) finished.

		public float Progress = 0f;	// Can be updated by the operation user handler on the other thread.

		public event OperationCompleteHandler Completed;	// Will be called when task is finished. Get the result from the Result field.


		private OperationHandler m_OperationHandler;
		private Thread m_Thread;

		public SVNAsyncOperation(OperationHandler operationHandler)
		{
			m_OperationHandler = operationHandler;
		}

		public static SVNAsyncOperation<TResult> Start(OperationHandler operationHandler)
		{
			var op = new SVNAsyncOperation<TResult>(operationHandler);
			op.Start();
			return op;
		}

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

		private void Update()
		{
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
	}
}
