using UnityEngine;

namespace IamItem
{
    /// <summary>
    /// Раздел 9.2: экран призрака внутри предмета. Копия оформления
    /// ванильного режима головы (SpectateCamera.StateHead), только цель —
    /// не голова, а вселённый предмет.
    ///
    /// Мод не добавляет своё значение в SpectateCamera.State: ванильный
    /// LateUpdate целиком подменяется префиксом, пока идёт вселение.
    /// </summary>
    internal static class GhostCamera
    {
        private static bool entered;
        private static float settleTimer;

        public static void Enter()
        {
            entered = false;
            settleTimer = 0.5f;
        }

        public static void Exit()
        {
            entered = false;
            var cam = SpectateCamera.instance;
            if (!cam) return;

            // Заставляем ванильный StateNormal переинициализироваться: он сам
            // вернёт поле зрения, ближнюю плоскость и наблюдаемого игрока.
            cam.stateImpulse = true;
        }

        /// <summary>
        /// Куда поставить камеру наблюдателя, чтобы предмет оказался ровно
        /// в центре экрана и снаружи от камеры.
        ///
        /// Считаем не от объекта наблюдателя, а от настоящей камеры игры:
        /// она висит на нём со своим смещением и своим поворотом (тряска,
        /// наклоны), поэтому по объекту наблюдателя предмет уезжал вбок.
        /// </summary>
        private static Vector3 ViewPoint(SpectateCamera cam, PhysGrabObject target)
        {
            float gap = Cfg.CameraDistance.Value;
            if (gap <= 0f) return target.centerPoint;

            var main = GameDirector.instance ? GameDirector.instance.MainCamera : null;
            Transform eye = main ? main.transform : cam.transform;

            // Смещение настоящей камеры от объекта наблюдателя. Ставим объект
            // так, чтобы с учётом этого смещения камера встала куда надо.
            Vector3 offset = eye.position - cam.transform.position;

            // Габарит меряем по диагонали коробки предмета: она не зависит от
            // того, как предмет сейчас повёрнут.
            float outside = target.boundingBox.magnitude * 0.5f + 0.15f;
            float wanted = outside + gap;
            Vector3 back = -eye.forward;

            // Стены и статика. Слой самого предмета не берём, иначе каст
            // упрётся в него же на нулевой дистанции.
            if (Physics.SphereCast(target.centerPoint, 0.15f, back, out var hit, wanted,
                                   LayerMask.GetMask("Default", "StaticGrabObject"),
                                   QueryTriggerInteraction.Ignore))
                wanted = hit.distance - 0.1f;

            // Пол под лежащим предметом отбивал камеру вплотную к центру и
            // вид снова становился внутренним. За габарит пускаем всегда,
            // даже если там стена: чёрный край экрана лучше, чем чёрный экран.
            if (wanted < outside) wanted = outside;

            return target.centerPoint + back * wanted - offset;
        }

        /// <summary>Один кадр камеры призрака. Зовётся из префикса LateUpdate.</summary>
        public static void Tick(Possession possession)
        {
            var cam = SpectateCamera.instance;
            var target = possession.Target;
            if (!cam || !target) return;

            SemiFunc.UIHideHealth();
            SemiFunc.UIHideOvercharge();
            SemiFunc.UIHideEnergy();
            SemiFunc.UIHideInventory();
            SemiFunc.UIHideAim();
            if (MissionUI.instance) MissionUI.instance.Hide();

            var mainCamera = GameDirector.instance ? GameDirector.instance.MainCamera : null;

            if (!entered)
            {
                entered = true;
                cam.transform.position = ViewPoint(cam, target);
                if (mainCamera) mainCamera.nearClipPlane = 0.01f;
                CameraGlitch.Instance.PlayTiny();
                GameDirector.instance.CameraImpact.Shake(1f, 0.1f);
                AudioManager.instance.RestartAudioLoopDistances();
                LevelGenerator.Instance.RestartParticleDistances();
                SemiFunc.LightManagerSetCullTargetTransform(cam.transform);
                CameraAim.Instance.OverridePlayerAimDisableReset();
                CameraAim.Instance.SetPlayerAim(target.transform.rotation, _setRotation: true);
                PlayerController.instance.playerAvatarScript.localCamera.Teleported();
                AudioManager.instance.AudioListener.TargetPositionTransform = cam.transform;
            }

            if (settleTimer > 0f) settleTimer -= Time.deltaTime;

            PostProcessing.Instance.VignetteOverride(Color.black, 0.4f, 1f, 5f, 5f, 0.1f, cam.gameObject);
            PostProcessing.Instance.SaturationOverride(-50f, 20f, 20f, 0.1f, cam.gameObject);
            CameraNoise.Instance.Override(0.03f, 0.25f);

            cam.transform.localRotation = Quaternion.identity;
            cam.transform.position = Vector3.Lerp(cam.transform.position, ViewPoint(cam, target), 25f * Time.deltaTime);

            // Звук и свет считаем от предмета, а не от трупа.
            var rooms = PlayerController.instance.playerAvatarScript.RoomVolumeCheck;
            rooms.PauseCheckTimer = 1f;
            if (target.roomVolumeCheck)
            {
                rooms.CurrentRooms.Clear();
                rooms.CurrentRooms.AddRange(target.roomVolumeCheck.CurrentRooms);
            }
        }
    }
}
