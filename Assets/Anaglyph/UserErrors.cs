using System;
using UnityEngine;

namespace Anaglyph
{
	public readonly struct UserError
	{
		public readonly string subject;
		public readonly string details;

		public UserError(string subject, string details)
		{
			this.subject = subject;
			this.details = details;
		}
	}

	/// <summary>
	/// One-way channel for reporting a problem the user needs to know about.
	/// Systems raise errors without knowing whether anything is listening;
	/// whatever is presenting the UI subscribes.
	/// </summary>
	public static class UserErrors
	{
		public static event Action<UserError> Raised = delegate { };

		public static void Raise(string subject, string details)
		{
			Debug.LogWarning($"[{nameof(UserErrors)}] {subject}: {details}");
			Raised.Invoke(new UserError(subject, details));
		}

		// statics persist across play sessions while domain reload is disabled
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void Init()
		{
			Raised = delegate { };
		}
	}
}
