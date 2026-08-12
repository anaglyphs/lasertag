using System;
using System.Threading;
using UnityEngine;

namespace Anaglyph.LaserTag.Maps
{
	/// <summary>
	/// Coalesces a burst of map edits into one save. Every reason to persist the map — a local
	/// edit, an object arriving from a peer, a provider re-realizing an anchor — schedules
	/// through here, so a drag that touches an object every frame costs one write rather than
	/// one per frame.
	///
	/// Trailing edge: the first schedule starts the window and later ones ride it, so a
	/// continuous stream of edits still reaches disk every <c>debounceSeconds</c>.
	/// </summary>
	internal sealed class MapAutosave : IDisposable
	{
		private readonly float debounceSeconds;
		private readonly Action save;
		private readonly CancellationTokenSource ctknSrc = new();

		private bool pending;
		private bool disposed;

		public MapAutosave(float debounceSeconds, Action save)
		{
			this.debounceSeconds = debounceSeconds;
			this.save = save ?? throw new ArgumentNullException(nameof(save));
		}

		public void Schedule()
		{
			if (pending || disposed)
				return;

			pending = true;
			Run();
		}

		private async void Run()
		{
			try
			{
				await Awaitable.WaitForSecondsAsync(debounceSeconds, ctknSrc.Token);
			}
			catch (OperationCanceledException)
			{
				return;
			}
			finally
			{
				pending = false;
			}

			save();
		}

		public void Dispose()
		{
			if (disposed)
				return;

			disposed = true;
			ctknSrc.Cancel();
			ctknSrc.Dispose();
		}
	}
}
