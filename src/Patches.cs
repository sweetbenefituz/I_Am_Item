using HarmonyLib;
using UnityEngine;

namespace IamItem
{
    /// <summary>
    /// Раздел 13.3: точки врезки. Их сознательно мало — щит и таран патчей
    /// не требуют вообще, это публичные булевы поля игры.
    /// </summary>

    /// <summary>Компонент вселения вешаем на каждого игрока при рождении.</summary>
    [HarmonyPatch(typeof(PlayerAvatar), "Start")]
    internal static class PlayerAvatarStartPatch
    {
        private static void Postfix(PlayerAvatar __instance)
        {
            if (!Cfg.Enabled.Value) return;
            if (__instance.GetComponent<Possession>()) return;
            // PlayerAvatar.Start в паре веток убивает дубль игрока и выходит.
            // На такой объект компонент вешать нельзя.
            if (!GameDirector.instance || !GameDirector.instance.PlayerList.Contains(__instance)) return;

            __instance.gameObject.AddComponent<Possession>();

            // Компонент добавлен после Awake, поэтому список RPC у PhotonView
            // уже собран и нашего в нём нет. Пересобираем.
            var view = __instance.GetComponent<Photon.Pun.PhotonView>();
            if (view) view.RefreshRpcMonoBehaviourCache();
        }
    }

    /// <summary>
    /// Пока идёт вселение, ванильный наблюдатель молчит: камерой, экраном и
    /// вводом занимается мод.
    /// </summary>
    [HarmonyPatch(typeof(SpectateCamera), "LateUpdate")]
    internal static class SpectateCameraLateUpdatePatch
    {
        private static bool Prefix()
        {
            var me = Possession.Local;
            if (me == null || !me.Active) return true;
            GhostCamera.Tick(me);
            return false;
        }
    }

    /// <summary>
    /// Шкала общая с ванильным управлением головой (раздел 5), но пока игрок
    /// в предмете, расход считает мод, а не игра. Иначе ванильный код будет
    /// параллельно копить энергию в состоянии Normal.
    /// </summary>
    [HarmonyPatch(typeof(SpectateCamera), "HeadEnergyLogic")]
    internal static class HeadEnergyLogicPatch
    {
        private static bool Prefix()
        {
            var me = Possession.Local;
            return me == null || !me.Active;
        }
    }

    /// <summary>
    /// Клавиша вселения. Читается в Update наблюдателя, чтобы не спорить с
    /// ванильным Interact, который в состоянии Normal входит в голову.
    /// </summary>
    [HarmonyPatch(typeof(SpectateCamera), "Update")]
    internal static class SpectateCameraUpdatePatch
    {
        private static void Postfix()
        {
            if (!Cfg.Enabled.Value) return;
            if (MenuManager.instance && MenuManager.instance.currentMenuPage) return;
            if (ChatManager.instance && ChatManager.instance.StateIsActive()) return;

            if (!Input.GetKeyDown(Cfg.PossessKey.Value)) return;
            if (!Possession.CanPossess()) return;

            Possession.Local.RequestPossess();
        }
    }

    /// <summary>
    /// Раздел 8.2: живой взял вселённый предмет — говорит грустную фразу.
    /// GrabStarted выполняется на клиенте того, кто взял, а игра проверяет
    /// отправителя реплики, поэтому сказать за него никто другой не может.
    ///
    /// Игра зовёт GrabStarted каждый кадр, пока предмет в руках, и сама
    /// отсекает повторы полем grabbedLocal. Postfix отрабатывает и на этих
    /// повторах, поэтому состояние поля надо снять до вызова: иначе фраза
    /// ставится в очередь каждый кадр удержания.
    /// </summary>
    [HarmonyPatch(typeof(PhysGrabObject), "GrabStarted")]
    internal static class GrabStartedPatch
    {
        private static void Prefix(PhysGrabObject __instance, out bool __state)
        {
            __state = __instance.grabbedLocal;
        }

        private static void Postfix(PhysGrabObject __instance, PhysGrabber player, bool __state)
        {
            if (__state) return;   // предмет уже был в руках, это не новый захват
            if (!Cfg.Enabled.Value || !Cfg.SadPhrasesEnabled.Value) return;
            if (!player || !player.playerAvatar || !player.playerAvatar.isLocal) return;
            if (!Possession.IsPossessed(__instance)) return;

            // Фраза на каждый новый захват, как у любовного зелья. Кулдауна
            // нет: фразы встают в очередь и звучат одна за другой.
            Speech.Say(Phrases.Pick(Phrases.Pickup));
        }
    }

    /// <summary>
    /// Раздел 7.4: смертельные зоны пробивают щит. Игра зовёт этот метод ровно
    /// тогда, когда собирается спасти защищённый предмет из пропасти, лавы или
    /// дробилки. Вселённый предмет спасать не надо — он гибнет.
    /// </summary>
    [HarmonyPatch(typeof(PhysGrabObject), "DeathPitEffectCreate")]
    internal static class DeathPitPatch
    {
        private static bool Prefix(PhysGrabObject __instance)
        {
            if (!Cfg.Enabled.Value) return true;
            if (!SemiFunc.IsMasterClientOrSingleplayer()) return true;

            var possession = Possession.Of(__instance);
            if (possession == null) return true;

            possession.KillInDeathPit();
            return false;
        }
    }
}
