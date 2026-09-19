using System;
using System.Threading;
using Anaglyph.Menu;
using UnityEngine;
using UnityEngine.UIElements;

namespace Anaglyph.LaserTag.Interface
{
	/// <summary>Owns the headset's spatial-map probe and its button for one visual-tree binding.</summary>
	public sealed class MapProbeBinder : IDisposable
	{
		private readonly Button probeButton;
		private readonly CancellationTokenSource bindingCancellation = new();
		private bool probing;
		private bool disposed;

		public MapProbeBinder(Button probeButton)
		{
			this.probeButton = probeButton ?? throw new ArgumentNullException(nameof(probeButton));
			probeButton.MakeActOnPress();
			probeButton.style.display = DisplayStyle.Flex;
			probeButton.clicked += Probe;
			MenuCopy.Changed += Refresh;
			Refresh();
		}

		// The host calls this when opening the catalog because a startup probe may
		// describe a room the headset has since left. The catalog itself owns no navigation.
		public async void Probe()
		{
			LaserTagMapCoordinator manager = LaserTagMapCoordinator.Instance;
			if (disposed || probing || manager == null)
				return;

			CancellationToken token = bindingCancellation.Token;
			probing = true;
			Refresh();

			try
			{
				await manager.ProbeAllMaps(token);
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
			finally
			{
				// A disposed binding can finish after a replacement has taken over this button.
				if (!token.IsCancellationRequested)
				{
					probing = false;
					Refresh();
				}
			}
		}

		public void Dispose()
		{
			if (disposed) return;
			disposed = true;
			bindingCancellation.Cancel();
			bindingCancellation.Dispose();
			probeButton.clicked -= Probe;
			probeButton.style.display = DisplayStyle.None;
			MenuCopy.Changed -= Refresh;
		}

		private void Refresh()
		{
			probeButton.text = MenuCopy.Get("Map", probing ? "maps.checking" : "maps.check");
			probeButton.SetEnabled(!probing);
		}
	}
}
