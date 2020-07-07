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

		private static SingletonType m_Instance;
		public static SingletonType Instance {
			get {
				if (m_Instance == null) {
					m_Instance = Resources.FindObjectsOfTypeAll<SingletonType>().FirstOrDefault();

					bool freshlyCreated = false;
					if (m_Instance == null) {

						m_Instance = ScriptableObject.CreateInstance<SingletonType>();
						m_Instance.name = m_Instance.GetType().Name;	// nameof(SingletonType) returns "SingletonType".

						// Setting this flag will tell Unity NOT to destroy this object on assembly reload (as no scene references this object).
						// We're essentially leaking this object. But we can still find it with Resources.FindObjectsOfTypeAll() after reload.
						// More info on this: https://blogs.unity3d.com/2012/10/25/unity-serialization/
						m_Instance.hideFlags = HideFlags.HideAndDontSave;

						freshlyCreated = true;

					} else {
						// Data is already deserialized by Unity onto the scriptable object.
						// Even though OnEnable is not yet called, data is there after assembly reload.
						// It is deserialized even before static constructors [InitializeOnLoad] are called. I tested it! :D

						// The idea here is to save some time on assembly reload from deserializing json as the reload is already slow enough for big projects.
					}

					m_Instance.Initialize(freshlyCreated);
				}

				return m_Instance;
			}


		}

		public abstract void Initialize(bool freshlyCreated);
	}
}
