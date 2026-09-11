using System;
using System.Collections.Generic;
using Anaglyph.Menu;
using UnityEngine.UIElements;
using static Anaglyph.Menu.UIQuery;

namespace Anaglyph.LaserTag.Interface
{
	public sealed class MenuErrorPresenter : IDisposable
	{
		private readonly UserErrorArea area;
		private readonly Queue<UserError> pending = new();
		private NavView navigation;
		private NavPage modal;
		private Label subject;
		private Label details;
		private Button dismiss;

		public MenuErrorPresenter(UserErrorArea area)
		{
			this.area = area;
			UserErrors.Raised += OnRaised;
			MenuCopy.Changed += Present;
		}

		public void Bind(NavView navigation)
		{
			Unbind();
			this.navigation = navigation;
			modal = navigation.GetPage("error-modal");
			subject = Require<Label>(modal, "error-subject");
			details = Require<Label>(modal, "error-details");
			dismiss = Require<Button>(modal, "dismiss-error-button");
			dismiss.clicked += Dismiss;
			Present();
		}

		public void Unbind()
		{
			if (dismiss != null) dismiss.clicked -= Dismiss;
			navigation = null;
			modal = null;
			subject = null;
			details = null;
			dismiss = null;
		}

		private void OnRaised(UserError error)
		{
			if (error.area != area) return;
			foreach (UserError existing in pending)
				if (existing.subject == error.subject && existing.details == error.details) return;
			pending.Enqueue(error);
			Present();
		}

		private void Present()
		{
			if (navigation == null) return;
			if (pending.Count > 0)
			{
				UserError error = pending.Peek();
				subject.text = error.subject;
				details.text = error.details;
			}
			navigation.SetModalPresented(modal, pending.Count > 0, 200);
		}

		private void Dismiss()
		{
			if (pending.Count > 0) pending.Dequeue();
			Present();
		}

		public void Dispose()
		{
			UserErrors.Raised -= OnRaised;
			MenuCopy.Changed -= Present;
			Unbind();
		}
	}
}
