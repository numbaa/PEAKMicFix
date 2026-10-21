using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using UnityEngine;

namespace PeakModding.Common.Audio
{
	/// <summary>
	/// Monitors Unity's <see cref="Microphone.devices"/> device list at runtime and raises events when
	/// the set of available input devices changes. Works around the fact that PEAK's voice pipeline
	/// reads the device list only once at startup and never re-enumerates.
	/// </summary>
	/// <remarks>
	/// On Windows, Unity re-queries the audio device endpoints each time <see cref="Microphone.devices"/>
	/// is accessed after the OS posts a plug/unplug notification, so polling the getter (plus rescans on
	/// app focus) is enough to observe hot-plug events. This manager encapsulates that polling and the
	/// change detection so voice mods don't each re-implement it.
	/// </remarks>
	public sealed class MicrophoneDeviceWatcher : IDisposable
	{
		private readonly ManualLogSource log;
		private readonly float pollIntervalSec;

		private float nextPollTime;
		private string[] snapshot = Array.Empty<string>();

		/// <summary>Number of microphone device changes observed since the watcher was created.</summary>
		public int ChangeCount { get; private set; }

		/// <summary>When true, every poll logs the full current device list (for diagnostics).</summary>
		public bool VerboseLogging { get; set; }

		/// <summary>Called with the new device list whenever it differs from the previous snapshot.</summary>
		public event Action<string[]> DevicesChanged;

		/// <summary>Called when one or more devices are added (unplugged).</summary>
		public event Action<string[]> DevicesAdded;

		/// <summary>Called when one or more devices are removed (unplugged).</summary>
		public event Action<string[]> DevicesRemoved;

		public MicrophoneDeviceWatcher(ManualLogSource log, float pollIntervalSec = 2f)
		{
			this.log = log;
			this.pollIntervalSec = Mathf.Max(0.2f, pollIntervalSec);
			Refresh(initial: true);
		}

		/// <summary>The most recently observed device list (names as returned by Unity).</summary>
		public IReadOnlyList<string> Devices => snapshot;

		/// <summary>Force an immediate rescans and raise change events if the list differs.</summary>
		public void ForceRefresh()
		{
			Refresh();
		}

		/// <summary>Cheaply re-checks for changes; call from a per-frame update (e.g. an Update/MonoBehaviour).</summary>
		public void Tick()
		{
			if (Time.unscaledTime >= nextPollTime)
			{
				nextPollTime = Time.unscaledTime + pollIntervalSec;
				Refresh();
			}
		}

		private void Refresh(bool initial = false)
		{
			string[] currents;
			try
			{
				currents = Microphone.devices ?? Array.Empty<string>();
			}
			catch (Exception ex)
			{
				log?.LogWarning($"MicrophoneDeviceWatcher: Microphone.devices threw: {ex.Message}");
				snapshot = Array.Empty<string>();
				return;
			}

			if (VerboseLogging || initial)
			{
				string phase = initial ? "initial" : "tick";
				log?.LogInfo(FormatLog($"Poll#{phase} — devices[{currents.Length}]",
					currents));
			}

			if (currents.Length == snapshot.Length && !currents.Except(snapshot).Any() && !snapshot.Except(currents).Any())
			{
				return; // unchanged
			}

			string[] previous = snapshot;
			snapshot = (string[])currents.Clone();
			ChangeCount++;

			var added = currents.Except(previous).ToArray();
			var removed = previous.Except(currents).ToArray();

			log?.LogInfo(FormatLog($"CHANGE#{ChangeCount} — devices[{currents.Length}] added=[{string.Join(", ", added)}] removed=[{string.Join(", ", removed)}]",
				currents));
			DevicesChanged?.Invoke((string[])snapshot.Clone());
			if (added.Length > 0)
				DevicesAdded?.Invoke(added);
			if (removed.Length > 0)
				DevicesRemoved?.Invoke(removed);
		}

		/// <summary>Formats a log line: "HH:mm:ss.fff <msg> | <index>:<name>, ...".</summary>
		internal static string FormatLog(string message, string[] devices)
		{
			var sb = new System.Text.StringBuilder();
			sb.Append(DateTime.Now.ToString("HH:mm:ss.fff"));
			sb.Append(' ').Append(message);
			for (int i = 0; i < devices.Length; i++)
			{
				sb.Append(i == 0 ? " — " : ", ");
				sb.Append(i).Append(':').Append(devices[i]);
			}
			return sb.ToString();
		}

		public void Dispose()
		{
			DevicesChanged = null;
			DevicesAdded = null;
			DevicesRemoved = null;
		}
	}
}