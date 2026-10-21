using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace PeakMicFix
{
	[BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
	public sealed class PEAKMicFixPlugin : BaseUnityPlugin
	{
		private static PEAKMicFixPlugin instance;

		public static PEAKMicFixPlugin Instance => instance;

		public new ManualLogSource Logger => base.Logger;

		private MicFixCore core;
		private GameObject coreObject;

		private void Awake()
		{
			instance = this;

			// Plugin log source name so it's easy to spot in the BepInEx log.
			Logger.LogInfo($"{PluginInfo.Name} v{PluginInfo.Version} loading.");

			// Harmony (unused by the current implementation, but reserved for future
			// patches and to satisfy common BepInEx expectations).
			// Harmony.CreateAndPatchAll(typeof(PeakMicFixPlugin).Assembly, PluginInfo.GUID);

			// The fix runs from a plain MonoBehaviour on its own GameObject.
			coreObject = new GameObject(PluginInfo.Name);
			DontDestroyOnLoad(coreObject);
			core = coreObject.AddComponent<MicFixCore>();
			core.Initialize(Config, Logger);

			Logger.LogInfo($"{PluginInfo.Name} v{PluginInfo.Version} loaded. Mic hot-plug fix active.");
		}

		private void OnDestroy()
		{
			if (core != null)
			{
				Destroy(coreObject);
				core = null;
			}
		}
	}

	internal static class PluginInfo
	{
		public const string GUID = "numbaa.PeakMicFix";
		public const string Name = "PeakMicFix";
		public const string Version = "1.1.0";
	}
}