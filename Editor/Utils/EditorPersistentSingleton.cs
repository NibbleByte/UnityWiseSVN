// MIT License Copyright(c) 2022 Filip Slavov, https://github.com/NibbleByte/UnityWiseSVN

using System.Linq;
using UnityEngine;

namespace DevLocker.VersionControl.WiseSVN.Utils
{
	public interface IEditorPersistentSingleton
	{
		void Initialize(bool freshlyCreated);
	}

	/// <summary>
	/// Base class for singletons that have to survive assembly reloads.
	/// It uses hidden ScriptableObject.
	/// </summary>
	public abstract class EditorPersistentSingleton<SingletonType> : ScriptableObject, IEditorPersistentSingleton
		where SingletonType : ScriptableObject, IEditorPersistentSingleton
	{

		private static bool m_SingletonInitialized;
		private static SingletonType m_Instance;

		public static SingletonType Instance {
			get {
				bool freshlyCreated = false;

				if (m_Instance == null) {
					ScriptableObject.CreateInstance<SingletonType>(); // No need to assign it - the constructor will do it.
					m_Instance.name = m_Instance.GetType().Name;	// nameof(SingletonType) returns "SingletonType".

					// Setting this flag will tell Unity NOT to destroy this object on assembly reload (as no scene references this object).
					// We're essentially leaking this object. But we can still get a reference to it after reload,
					// when Unity recreates the existing scriptable objects (this will call the object constructor during deserialization).
					// More info on this: https://blogs.unity3d.com/2012/10/25/unity-serialization/
					m_Instance.hideFlags = HideFlags.HideAndDontSave;

					freshlyCreated = true;
				}

				// Data is already deserialized by Unity onto the scriptable object.
				// Even though OnEnable is not yet called, data is there after assembly reload.
				// It is deserialized even before static constructors [InitializeOnLoad] are called. I tested it! :D

				// The idea here is to save some time on assembly reload from deserializing json as the reload is already slow enough for big projects.

				if (!m_SingletonInitialized || freshlyCreated) {	// Freshly created also, just in case?
					m_SingletonInitialized = true;
					m_Instance.Initialize(freshlyCreated);
				}

				return m_Instance;
			}


		}


		protected EditorPersistentSingleton()
		{
			// Constructor will get called on calling Instance OR during deserialization on assembly reload.
			// If this is assembly reload, fields will not be deserialized yet - initialize on demand, not here.
			// This method was inspared by the Unity ScriptableSingleton:
			// https://docs.unity3d.com/2020.1/Documentation/ScriptReference/ScriptableSingleton_1.html
			if (m_Instance == null) {
				m_Instance = this as SingletonType;

			} else {
				Debug.LogError($"{GetType().Name} singleton found another instance! Abort!", this);
			}
		}

		public abstract void Initialize(bool freshlyCreated);
	}
}
