using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace IamItem
{
    /// <summary>
    /// I Am Item. Мёртвый игрок вселяется в случайную ценность на карте и
    /// недолго ей управляет. Мод не добавляет ни одной новой модели, текстуры
    /// или звука — всё берётся из игры.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "sweet.iamitem";
        public const string Name = "I Am Item";
        public const string Version = "1.0.0";

        internal static ManualLogSource Log;
        private static Harmony harmony;

        private void Awake()
        {
            Log = Logger;
            Cfg.Bind(Config);

            if (!Cfg.Enabled.Value)
            {
                Log.LogInfo("I Am Item disabled in config, patches not applied.");
                return;
            }

            harmony = new Harmony(Guid);
            harmony.PatchAll();

            var hud = new GameObject("IamItemHud");
            hud.AddComponent<Hud>();
            DontDestroyOnLoad(hud);
            hud.hideFlags = HideFlags.HideAndDontSave;

            Log.LogInfo($"{Name} {Version} loaded. Possess key: {Cfg.PossessKey.Value}.");
        }
    }
}
