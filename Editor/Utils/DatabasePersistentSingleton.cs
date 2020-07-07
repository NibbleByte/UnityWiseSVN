using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DevLocker.VersionControl.WiseSVN.Utils
{
	/// <summary>
	/// Base class for database objects that need to survive assembly reload.
	/// Keep in mind that DataType must be Serializable!
	/// </summary>
	public abstract class DatabasePersistentSingleton<SingletonType, DataType> : EditorPersistentSingleton<SingletonType>
		where SingletonType : ScriptableObject, IEditorPersistentSingleton
	{
		// Is the database currently active.
		public abstract bool IsActive { get; }

		// Is the database temporarily disabled by code while some operation is executing.
		public abstract bool TemporaryDisabled { get; }

		// Should the database do trace logs.
		public abstract bool DoTraceLogs { get; }

		// Has the database been populated, yet?
		public bool IsReady => m_IsReady;

		// Is the database currently being populated?
		public bool IsUpdating => m_PendingUpdate;

		public event Action DatabaseChanged;

		[SerializeField] private bool m_IsReady;
		[SerializeField] protected List<DataType> m_Data = new List<DataType>();

		// Seconds after which the database will invalidate itself.
		// -1 to don't invalidate.
		public abstract double RefreshInterval { get; }

		private double m_LastRefreshTime;   // TODO: Maybe serialize this?

		#region Thread Work Related Data
		// Filled in by a worker thread.
		[NonSerialized] private DataType[] m_PendingData;

		private System.Threading.Thread m_WorkerThread;

		// Is update pending?
		// If last update didn't make it, this flag will still be true.
		// Useful if assembly reload happens and stops the work of the database update.
		[SerializeField] private bool m_PendingUpdate = false;
		#endregion


		#region Initialize & Refresh

		public override void Initialize(bool freshlyCreated)
		{
			if (DoTraceLogs && freshlyCreated) {
				Debug.Log($"{name} not found. Creating new one.");
			}

			AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;

			// Assembly reload might have killed the working thread leaving pending update.
			// Do it again.
			if (m_PendingUpdate) {
				if (IsActive) {
					StartDatabaseUpdate();
				} else {
					m_PendingUpdate = false;
				}
			}

			if (freshlyCreated) {
				InvalidateDatabase();
			}
		}

		private void OnBeforeAssemblyReload()
		{
			// Do it before Unity does it. Cause Unity aborts the thread badly sometimes :(
			if (m_WorkerThread != null && m_WorkerThread.IsAlive) {
				m_WorkerThread.Abort();
			}
		}

		// Call this when IsActive has changed.
		protected void RefreshActive()
		{
			if (IsActive) {
				EditorApplication.update -= AutoRefresh;
				EditorApplication.update += AutoRefresh;

				m_LastRefreshTime = EditorApplication.timeSinceStartup;

			} else {
				EditorApplication.update -= AutoRefresh;

				m_Data.Clear();
				m_IsReady = false;
				DatabaseChanged?.Invoke();
			}
		}

		#endregion


		//
		//=============================================================================
		//
		#region Populate Data

		protected virtual void StartDatabaseUpdate()
		{
			if (DoTraceLogs) {
				Debug.Log($"Started update of {name} at {EditorApplication.timeSinceStartup:0.00}");
			}

			if (m_WorkerThread != null || m_PendingData != null) {
				throw new Exception(name + " starting database update, while another one is pending?");
			}

			m_PendingUpdate = true;

			// Listen for the thread result in the main thread.
			// Just in case, remove previous updates.
			EditorApplication.update -= WaitAndFinishDatabaseUpdate;
			EditorApplication.update += WaitAndFinishDatabaseUpdate;

			m_WorkerThread = new System.Threading.Thread(GatherData);
			m_WorkerThread.Start();
		}

		private void GatherData()
		{
			try {
				var pendingData = GatherDataInThread();

				if (m_PendingUpdate == false) {
					// GetType() instead of just "name" since we're in another therad and Unity will blow!
					throw new Exception($"{GetType().Name} thread finished work but the update is over?");
				}

				m_PendingData = pendingData;
			}
			// Most probably the assembly got reloaded and the thread was aborted.
			catch (System.Threading.ThreadAbortException) {
				System.Threading.Thread.ResetAbort();

				// Should always be true.
				if (m_PendingUpdate) {
					m_PendingData = new DataType[0];
				}
			}
			catch (Exception ex) {
				Debug.LogException(ex);

				// Should always be true.
				if (m_PendingUpdate) {
					m_PendingData = new DataType[0];
				}
			}
		}

		// Gather needed data in a worker thread.
		protected abstract DataType[] GatherDataInThread();

		private void WaitAndFinishDatabaseUpdate()
		{
			if (m_PendingData == null)
				return;

			if (DoTraceLogs) {
				Debug.Log($"Finished updating {name} at {EditorApplication.timeSinceStartup:0.00}");
			}

			EditorApplication.update -= WaitAndFinishDatabaseUpdate;
			m_WorkerThread = null;

			m_IsReady = true;

			var pendingData = m_PendingData;
			m_PendingData = null;
			m_Data.Clear();

			// Mark update as finished.
			m_PendingUpdate = false;

			// If preferences were changed while waiting.
			if (!IsActive) {
				m_IsReady = false;
				return;
			}

			// Process the gathered data in the main thread, since Unity API is not thread-safe.
			WaitAndFinishDatabaseUpdate(pendingData);

			DatabaseChanged?.Invoke();
		}

		// Process gathered data in the main thread from the worker one.
		protected abstract void WaitAndFinishDatabaseUpdate(DataType[] pendingData);

		#endregion

		//
		//=============================================================================
		//
		#region Invalidate Database

		/// <summary>
		/// Force the database to refresh its statuses cache onto another thread.
		/// </summary>
		public void InvalidateDatabase()
		{
			if (!IsActive || m_PendingUpdate || TemporaryDisabled)
				return;

			// Will be done on assembly reload.
			if (EditorApplication.isCompiling) {
				m_PendingUpdate = true;
				return;
			}

			StartDatabaseUpdate();
		}

		private void AutoRefresh()
		{
			if (RefreshInterval <= 0 || EditorApplication.timeSinceStartup - m_LastRefreshTime < RefreshInterval)
				return;

			m_LastRefreshTime = EditorApplication.timeSinceStartup;

			InvalidateDatabase();
		}

		#endregion
	}
}
