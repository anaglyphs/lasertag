using System;
using UnityEngine;

namespace Anaglyph
{
	public enum UserErrorArea { Connection, Game, Settings, Gate }

	public readonly struct UserError
	{
		public readonly UserErrorArea area;
		private readonly string subjectValue;
		private readonly string detailsValue;
		private readonly bool localized;
		private readonly object[] arguments;

		public string subject => localized ? MenuCopy.Get(area.ToString(), subjectValue) : subjectValue;
		public string details => localized ? MenuCopy.Format(area.ToString(), detailsValue, arguments) : detailsValue;

		public UserError(UserErrorArea area, string subject, string details,
			bool localized = false, params object[] arguments)
		{
			this.area = area;
			subjectValue = subject;
			detailsValue = details;
			this.localized = localized;
			this.arguments = arguments;
		}
	}

	/// <summary>One-way error channel. Every producer specifies the responsible panel.</summary>
	public static class UserErrors
	{
		public static event Action<UserError> Raised = delegate { };

		public static void Raise(UserErrorArea area, string subject, string details)
		{
			Debug.LogWarning($"[{nameof(UserErrors)}] {subject}: {details}");
			Raised.Invoke(new UserError(area, subject, details));
		}

		public static void RaiseLocalized(UserErrorArea area, string subjectKey, string detailsKey,
			params object[] arguments)
		{
			Debug.LogWarning($"[{nameof(UserErrors)}] {area}/{subjectKey}: {detailsKey}");
			Raised.Invoke(new UserError(area, subjectKey, detailsKey, true, arguments));
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void Init() => Raised = delegate { };
	}
}
