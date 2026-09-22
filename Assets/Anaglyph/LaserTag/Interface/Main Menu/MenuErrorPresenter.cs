using System;
using System.Collections.Generic;
using Anaglyph.Menu;
using Anaglyph.Netcode;
using UnityEngine.UIElements;
using static Anaglyph.Menu.UIQuery;

namespace Anaglyph.LaserTag.Interface
{
	public enum MenuErrorArea { Connection, Game }

	public readonly struct MenuError
	{
		public readonly MenuErrorArea area;
		private readonly string subjectValue;
		private readonly string detailsValue;
		private readonly string table;
		private readonly object[] arguments;

		public string subject => table != null ? MenuCopy.Get(table, subjectValue) : subjectValue;
		public string details => table != null ? MenuCopy.Format(table, detailsValue, arguments) : detailsValue;

		public MenuError(MenuErrorArea area, string subject, string details,
			string table = null, params object[] arguments)
		{
			this.area = area;
			subjectValue = subject;
			detailsValue = details;
			this.table = table;
			this.arguments = arguments;
		}
	}
	public sealed class MenuErrorPresenter : IDisposable
	{
		private readonly MenuErrorArea area;
		private readonly Queue<MenuError> pending = new();
		private NavView navigation;
		private NavPage modal;
		private Label subject;
		private Label details;
		private Button dismiss;

		public MenuErrorPresenter(MenuErrorArea area)
		{
			this.area = area;
			LaserTagMapCoordinator.AlignmentErrorRaised += OnAlignmentError;
			NetcodeManagement.ErrorRaised += OnNetcodeError;
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

		private void OnNetcodeError(NetcodeManagement.Error error)
		{
			if (area != MenuErrorArea.Connection) return;
			string key = error.kind switch
			{
				NetcodeManagement.ErrorKind.VersionMismatch => "build",
				NetcodeManagement.ErrorKind.ServicesUnavailable => "relay",
				_ => "join"
			};
			Show(new MenuError(MenuErrorArea.Connection, $"error.{key}-title", $"error.{key}-details",
				"ConnectionMenu", error.hostVersion, error.clientVersion));
		}

		private void OnAlignmentError(LaserTagMapCoordinator.AlignmentError error)
		{
			MenuErrorArea errorArea = error == LaserTagMapCoordinator.AlignmentError.RequestRejected
				? MenuErrorArea.Game : MenuErrorArea.Connection;
			if (area != errorArea) return;
			string key = error switch
			{
				LaserTagMapCoordinator.AlignmentError.SharingFailed => "share",
				LaserTagMapCoordinator.AlignmentError.SharingUnsupported => "anchors",
				_ => "alignment"
			};
			Show(new MenuError(errorArea, $"error.{key}-title", $"error.{key}-details", "Map",
				MenuCopy.Get("Map", "alignment.request-rejected")));
		}

		public void Show(MenuError error)
		{
			if (error.area != area) return;
			foreach (MenuError existing in pending)
				if (existing.subject == error.subject && existing.details == error.details) return;
			pending.Enqueue(error);
			Present();
		}

		private void Present()
		{
			if (navigation == null) return;
			if (pending.Count > 0)
			{
				MenuError error = pending.Peek();
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
			LaserTagMapCoordinator.AlignmentErrorRaised -= OnAlignmentError;
			NetcodeManagement.ErrorRaised -= OnNetcodeError;
			MenuCopy.Changed -= Present;
			Unbind();
		}
	}
}
