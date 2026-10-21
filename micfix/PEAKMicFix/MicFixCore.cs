using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;
using PeakModding.Common.Audio;
using Photon.Voice;
using Photon.Voice.Unity;
using UnityEngine;

namespace PeakMicFix
{
	/// <summary>
	/// Watches Unity's microphone device list and keeps the local <see cref="Recorder"/> pointed at a
	/// usable input device, fixing PEAK's two related bugs:
	///   1. Mic inserted AFTER launch is unusable — the game only assigns the recorder's device once,
	///      from a settings id captured at startup (empty when no mic was present then).
	///   2. A mic that "suddenly loses sound" mid-game — the recorder keeps transmitting from a stale
	///      device after hot-unplug / USB power-cycle; the recorder is restarted onto a valid device.
	/// </summary>
	public sealed class MicFixCore : MonoBehaviour
	{
		private ManualLogSource log;
		private MicrophoneDeviceWatcher watcher;

		private ConfigEntry<float> pollInterval;
		private ConfigEntry<bool> applyToSettings;
		private ConfigEntry<bool> verboseLogging;

		// Recorder we're managing; reset all applied state when the local character/recorder changes.
		private Recorder currentRecorder;

		// Device id we last applied to the recorder (null until we've applied one).
		private string appliedDeviceId;

		// Throttle periodic recap: only re-print recorder state every N ticks when idle.
		private int stateLogCounter;
		private const int StateLogInterval = 300; // ~5 sec at 60 fps

		// Track recorder object instance ID to detect respawns.
		private int currentRecorderId;

		// Counters for status recap
		private int applyCount;
		private int changeCountHWM;

		public void Initialize(ConfigFile config, ManualLogSource logger)
		{
			log = logger;

			pollInterval = config.Bind("MicFix", "PollIntervalSeconds", 2f,
				"How often (s) to re-enumerate microphone devices while in a game. Smaller = faster hot-plug detection, slightly more overhead.");
			applyToSettings = config.Bind("MicFix", "ApplyToSettings", true,
				"Also write the chosen device back into the in-game microphone setting so the settings UI and save match the active device.");
			verboseLogging = config.Bind("MicFix", "VerboseLogging", false,
				"Log every device poll + every state-decision detail (very noisy, ~every frame). Default off; ON only when debugging device selection.");

			watcher = new MicrophoneDeviceWatcher(log, pollInterval.Value)
			{
				VerboseLogging = true
			};
			watcher.DevicesChanged += OnDevicesChanged;
			watcher.DevicesAdded += OnDevicesAdded;
			watcher.DevicesRemoved += OnDevicesRemoved;

			log.LogInfo($"=== MicFixCore initialized ===");
			log.LogInfo($"  Poll interval: {pollInterval.Value}s | ApplyToSettings: {applyToSettings.Value}");
			log.LogInfo($"  Available microphones at init: {ListDevices(watcher.Devices)}");
		}

		private void Update()
		{
			if (watcher == null)
				return;

			watcher.Tick();

			Recorder recorder = FindLocalRecorder();
			if (recorder == null)
				return;

			int recorderId = recorder.GetInstanceID();
			if (recorderId != currentRecorderId)
			{
				LogInfo($"[RECORDER CHANGE] old object #{currentRecorderId} → new object #{recorderId}");
				currentRecorder = recorder;
				currentRecorderId = recorderId;
				appliedDeviceId = null;

				LogRecorderState(recorder, "freshly found recorder");
			}

			if (!verboseLogging.Value && ++stateLogCounter % StateLogInterval == 0)
			{
				// Occasionally print a status line even in non-verbose mode.
				string curDevice = recorder.MicrophoneDevice.IDString;
				string curDevLabel = string.IsNullOrEmpty(curDevice) ? "(none, IsDefault=" + recorder.MicrophoneDevice.IsDefault + ")" : curDevice;
				LogInfo($"Status: recorder={curDevLabel}, applied={appliedDeviceId ?? "(nil)"}, watcher[{watcher.Devices.Count}] chg#{watcher.ChangeCount}, apply#{applyCount}");
			}

			DeviceInfo curRecorderDevice = recorder.MicrophoneDevice;

			string[] devices = watcher.Devices as string[];
			if (devices == null)
			{
				LogWarning("[BUG] watcher.Devices is not a string[], cannot proceed");
				return;
			}

			if (verboseLogging.Value)
			{
				// Every poll cycle, dump full state.
				string curId = curRecorderDevice.IDString;
				LogInfo($"StateCheck: recorderDevID=[{(string.IsNullOrEmpty(curId) ? "(empty,IsDefault=" + curRecorderDevice.IsDefault + ")" : curId)}] " +
				        $"applied=[{appliedDeviceId ?? "(nil)"}] devices=[{devices.Length}]");
			}

			// No microphones at all right now: nothing to apply.
			if (devices.Length == 0)
			{
				// But if we previously HAD one applied and it's gone, forget applied state so re-acquisition
				// happens immediately when devices return.
				if (appliedDeviceId != null)
				{
					LogInfo($"[NO DEVICES] All microphones vanished — forgetting applied device '{appliedDeviceId}'");
					appliedDeviceId = null;
				}
				return;
			}

			string target = PickTargetDevice(devices);
			if (target == null)
			{
				LogWarning($"[PICK TARGET] no target selected from [{devices.Length}] devices (saved setting empty, devices present but something went wrong).");
				return;
			}

			ApplyIfNeeded(recorder, target, curRecorderDevice);
		}

		private Recorder FindLocalRecorder()
		{
			Character local = Character.localCharacter;
			return local?.GetComponentInChildren<Recorder>();
		}

		private string PickTargetDevice(string[] devices)
		{
			string saved = GetSavedSettingId();
			if (!string.IsNullOrEmpty(saved))
			{
				if (ArrayContains(devices, saved))
				{
					if (verboseLogging.Value)
						LogInfo($"[PICK TARGET] using saved setting: '{saved}'");
					return saved;
				}
				if (verboseLogging.Value)
					LogInfo($"[PICK TARGET] saved '{saved}' not in device list [{ListDevices(devices)}], falling back to first device");
			}
			else
			{
				if (verboseLogging.Value)
					LogInfo($"[PICK TARGET] no saved setting (id empty), using first available device");
			}

			return devices[0];
		}

		private bool ArrayContains(string[] devices, string id)
		{
			for (int i = 0; i < devices.Length; i++)
				if (devices[i] == id)
					return true;
			return false;
		}

		private string GetSavedSettingId()
		{
			try
			{
				if (GameHandler.Instance == null)
				{
					if (verboseLogging.Value)
						LogInfo("[SAVED SETTING] GameHandler.Instance is null");
					return null;
				}
				MicrophoneSetting setting = GameHandler.Instance.SettingsHandler.GetSetting<MicrophoneSetting>();
				if (setting == null)
				{
					if (verboseLogging.Value)
						LogInfo("[SAVED SETTING] MicrophoneSetting is null");
					return null;
				}
				string id = setting.Value.id;
				if (verboseLogging.Value)
					LogInfo($"[SAVED SETTING] MicrophoneSetting.Value.id = '{id ?? "(null)"}'");
				return string.IsNullOrEmpty(id) ? null : id;
			}
			catch (System.Exception ex)
			{
				LogInfo($"[SAVED SETTING] exception: {ex.Message}");
				return null;
			}
		}

		private void ApplyIfNeeded(Recorder recorder, string target, DeviceInfo curDevice)
		{
			string currentId = curDevice.IDString;

			bool needAssign = false;
			string reason = null;

			if (appliedDeviceId == null)
			{
				needAssign = true;
				reason = "appliedDeviceId is null (never applied or was reset)";
			}
			else if (appliedDeviceId != target)
			{
				needAssign = true;
				reason = $"appliedDeviceId '{appliedDeviceId}' != target '{target}'";
			}
			else if (string.IsNullOrEmpty(currentId))
			{
				needAssign = true;
				reason = "recorder has empty IDString (device was never set or was reset)";
			}
			else if (currentId != target)
			{
				// The recorder device differs from our last assignment (something else changed it).
				needAssign = true;
				reason = $"recorder device '{currentId}' != expected '{target}'";
			}

			if (!needAssign)
				return;

			LogInfo($"[APPLY] reason={reason}");
			LogInfo($"[APPLY] old Recorder.MicrophoneDevice = IDString=[{currentId}] IsDefault={curDevice.IsDefault}");
			LogInfo($"[APPLY] new DeviceInfo = new DeviceInfo(\"{target}\")");

			string[] devicesBefore = Microphone.devices;

			recorder.MicrophoneDevice = new DeviceInfo(target);

			// Check if the assignment changed anything.
			DeviceInfo after = recorder.MicrophoneDevice;
			LogInfo($"[APPLY RESULT] Recorder.MicrophoneDevice now: IDString=[{after.IDString}] IsDefault={after.IsDefault}");

			appliedDeviceId = target;
			applyCount++;

			if (applyToSettings.Value)
				WriteBackToSetting(target);

			LogInfo($"[APPLY DONE] applyCount={applyCount} | watcher changeCount={watcher.ChangeCount} | appliedDeviceId='{appliedDeviceId}'");
		}

		private void WriteBackToSetting(string id)
		{
			try
			{
				if (GameHandler.Instance == null)
				{
					LogInfo("[WRITE SETTING] GameHandler.Instance is null, skipped");
					return;
				}
				MicrophoneSetting setting = GameHandler.Instance.SettingsHandler.GetSetting<MicrophoneSetting>();
				if (setting == null)
				{
					LogInfo("[WRITE SETTING] MicrophoneSetting is null, skipped");
					return;
				}

				string oldId = setting.Value.id;
				if (oldId != id)
				{
					setting.Value = new MicrophoneSetting.MicrophoneInfo { id = id, name = id };
					LogInfo($"[WRITE SETTING] updated MicrophoneSetting.Value: old id='{oldId ?? "(empty)"}' → new id='{id}'");
				}
				else
				{
					if (verboseLogging.Value)
						LogInfo($"[WRITE SETTING] already set to '{id}', no change");
				}
			}
			catch (System.Exception ex)
			{
				LogInfo($"[WRITE SETTING] exception: {ex.Message}");
			}
		}

		private void LogRecorderState(Recorder recorder, string context)
		{
			DeviceInfo dev = recorder.MicrophoneDevice;
			LogInfo($"[RECORDER STATE@{context}] IDString=[{dev.IDString}] IsDefault={dev.IsDefault} " +
			        $"TransmitEnabled={recorder.TransmitEnabled} RecordingEnabled={recorder.RecordingEnabled} " +
			        $"SourceType={recorder.SourceType}");
		}

		private void OnDevicesChanged(string[] devices)
		{
			changeCountHWM = watcher.ChangeCount;
			LogInfo($"Device event: CHANGED → [{ListDevices(devices)}]");
		}

		private void OnDevicesAdded(string[] added)
		{
			LogInfo($"Device event: ADDED → [{string.Join(", ", added)}]");
		}

		private void OnDevicesRemoved(string[] removed)
		{
			LogInfo($"Device event: REMOVED → [{string.Join(", ", removed)}]");

			if (appliedDeviceId != null && ArrayContains(removed, appliedDeviceId))
			{
				LogInfo($"[HANDLE REMOVE] applied microphone '{appliedDeviceId}' disappeared — forgetting appliedDeviceId so re-acquisition will trigger on next poll.");
				appliedDeviceId = null;
			}
			else
			{
				LogInfo($"[HANDLE REMOVE] appliedDeviceId='{appliedDeviceId ?? "(nil)"}' not in removed set, no action.");
			}
		}

		private static string ListDevices(System.Collections.Generic.IReadOnlyList<string> devices)
		{
			if (devices == null || devices.Count == 0)
				return "(none)";
			return string.Join(", ", devices);
		}

		private static string ListDevices(string[] devices)
		{
			if (devices == null || devices.Length == 0)
				return "(none)";
			return string.Join(", ", devices);
		}

		private void LogInfo(string msg)
		{
			log?.LogInfo($"[{System.DateTime.Now:HH:mm:ss.fff}] {msg}");
		}

		private void LogWarning(string msg)
		{
			log?.LogWarning($"[{System.DateTime.Now:HH:mm:ss.fff}] {msg}");
		}

		private void OnDestroy()
		{
			LogInfo("Shutting down MicFixCore.");
			if (watcher != null)
			{
				watcher.DevicesChanged -= OnDevicesChanged;
				watcher.DevicesAdded -= OnDevicesAdded;
				watcher.DevicesRemoved -= OnDevicesRemoved;
				watcher.Dispose();
				watcher = null;
			}
		}
	}
}