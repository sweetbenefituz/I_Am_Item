using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace IamItem
{
    /// <summary>
    /// Раздел 9.4: три крутящиеся модельки ценностей над головой наблюдателя.
    /// Нужны, чтобы игрок увидел мод до того, как впервые сможет им
    /// воспользоваться: раньше про вселение нельзя было узнать, пока голова не
    /// зарядится полностью.
    ///
    /// Устройство: сцена-стенд далеко за картой, своя камера снимает её в
    /// RenderTexture, RenderTexture показывается обычным RawImage в холсте игры.
    /// Так модельки не режутся геометрией и не спорят с освещением уровня.
    ///
    /// Копии моделек собираются вручную из MeshFilter и MeshRenderer оригинала.
    /// Клонировать сам предмет нельзя: вместе с ним склонируются его скрипты,
    /// физика и PhotonView, и в комнате появится второй предмет-призрак.
    /// </summary>
    internal static class Preview
    {
        // Стенд стоит заведомо в стороне от карты и от склада выгруженных
        // объектов, чтобы в кадр камеры не попало ничего чужого.
        private static readonly Vector3 StageOrigin = new Vector3(7777f, -7777f, 7777f);

        private const int TextureHeight = 64;
        private const float ModelSize = 0.3f;     // габарит модельки на стенде
        private const float OrbitRadius = 0.5f;   // радиус хоровода
        private const float OrbitSpeed = 30f;     // градусов в секунду вокруг центра
        private const float SpinSpeed = 60f;      // градусов в секунду вокруг своей оси
        private const float CameraPitch = 24f;    // наклон камеры: из него и получается эллипс

        private static GameObject stage;
        private static Camera camera;
        private static RenderTexture texture;
        private static RawImage image;
        private static Transform orbit;
        private static readonly List<Transform> slots = new List<Transform>();
        private static readonly List<GameObject> models = new List<GameObject>();

        /// <summary>Виден ли стенд прямо сейчас. Пока скрыт, камера не работает.</summary>
        private static bool visible;

        public static void Hide()
        {
            if (!visible) return;
            visible = false;
            if (stage) stage.SetActive(false);
            if (camera) camera.enabled = false;
            if (image) image.enabled = false;
        }

        /// <summary>
        /// Показать стенд над подсказкой. <paramref name="anchor"/> — подсказка
        /// "Press V", над ней и встаём.
        /// </summary>
        public static void Tick(RectTransform parent, Vector3 anchor, float offsetY, bool ready)
        {
            if (!Cfg.PreviewEnabled.Value) { Hide(); return; }

            int count = Mathf.Clamp(Cfg.PreviewCount.Value, 1, 5);
            if (!stage) Build(parent, count);
            if (!stage) { Hide(); return; }

            if (!visible)
            {
                visible = true;
                stage.SetActive(true);
                camera.enabled = true;
                image.enabled = true;

                // Набор берём заново на каждую смерть: уровень мог смениться,
                // и меши прошлых ценностей вместе с ним уже уничтожены.
                for (int i = 0; i < slots.Count; i++) Fill(i);
            }

            // Пока вселяться нельзя, показываем то же самое, только тускло:
            // мод должен быть заметен заранее, но не притворяться готовым.
            image.color = ready ? Color.white : new Color(1f, 1f, 1f, 0.45f);

            image.rectTransform.position = new Vector3(anchor.x, anchor.y + offsetY, anchor.z);

            // Хоровод целиком крутится вокруг центра, каждая моделька вдобавок
            // вокруг своей оси. Камера смотрит сверху под углом, поэтому круг
            // виден эллипсом и хоровод читается как хоровод, а не как проезд
            // моделек влево-вправо.
            if (orbit) orbit.Rotate(Vector3.up, OrbitSpeed * Time.deltaTime, Space.Self);
            for (int i = 0; i < slots.Count; i++)
                slots[i].Rotate(Vector3.up, SpinSpeed * Time.deltaTime, Space.Self);

            // Предмет мог исчезнуть вместе со своими материалами — тогда в
            // слоте останется пустой объект. Проверяем и перезаполняем.
            for (int i = 0; i < models.Count; i++)
                if (!models[i]) Fill(i);
        }

        private static void Build(RectTransform parent, int count)
        {
            if (!parent) return;

            // Кадр считаем от того, что в нём должно поместиться, а не наоборот.
            // Круг радиуса OrbitRadius при наклоне камеры проецируется эллипсом:
            // по ширине это радиус целиком, по высоте - радиус на синус наклона.
            float squash = Mathf.Sin(CameraPitch * Mathf.Deg2Rad);
            float margin = ModelSize * 0.75f;
            float halfWidth = OrbitRadius + margin;
            float halfHeight = OrbitRadius * squash + margin;
            int width = Mathf.RoundToInt(TextureHeight * (halfWidth / halfHeight));

            texture = new RenderTexture(width, TextureHeight, 16, RenderTextureFormat.ARGB32)
            {
                name = "IamItemPreviewTexture",
                antiAliasing = 2,
                hideFlags = HideFlags.HideAndDontSave,
            };

            stage = new GameObject("IamItemPreviewStage");
            stage.transform.position = StageOrigin;
            stage.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(stage);

            // Камера поднята ровно настолько, чтобы смотреть в центр стенда
            // под углом CameraPitch.
            const float cameraBack = 3f;
            var cameraObject = new GameObject("Camera");
            cameraObject.transform.SetParent(stage.transform, false);
            cameraObject.transform.localPosition =
                new Vector3(0f, cameraBack * Mathf.Tan(CameraPitch * Mathf.Deg2Rad), -cameraBack);
            cameraObject.transform.localRotation = Quaternion.Euler(CameraPitch, 0f, 0f);

            camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = halfHeight;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 8f;              // дальше стенда камера не видит ничего
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            camera.targetTexture = texture;
            camera.allowHDR = false;
            camera.allowMSAA = false;

            // Точечный свет, а не направленный: направленный осветил бы весь
            // уровень, а этот дальше своего радиуса никого не трогает.
            var lightObject = new GameObject("Light");
            lightObject.transform.SetParent(stage.transform, false);
            lightObject.transform.localPosition = new Vector3(-0.6f, 1.2f, -1.6f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 8f;
            light.intensity = 3.5f;
            light.shadows = LightShadows.None;

            orbit = new GameObject("Orbit").transform;
            orbit.SetParent(stage.transform, false);

            for (int i = 0; i < count; i++)
            {
                float angle = i * Mathf.PI * 2f / count;
                var slot = new GameObject("Slot" + i).transform;
                slot.SetParent(orbit, false);
                slot.localPosition = new Vector3(Mathf.Sin(angle) * OrbitRadius, 0f,
                                                 Mathf.Cos(angle) * OrbitRadius);
                slot.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                slots.Add(slot);
                models.Add(null);
            }

            var imageObject = new GameObject("IamItemPreview", typeof(RectTransform));
            imageObject.transform.SetParent(parent, false);
            image = imageObject.AddComponent<RawImage>();
            image.texture = texture;
            image.raycastTarget = false;

            // Без явных привязок картинка растягивается по родителю: она
            // разъезжалась влево и налезала на подсказку.
            var rect = image.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            rect.sizeDelta = new Vector2(width, TextureHeight);

            for (int i = 0; i < count; i++) Fill(i);
        }

        private static void Fill(int index)
        {
            if (index < 0 || index >= slots.Count) return;
            if (models[index]) Object.Destroy(models[index]);
            models[index] = null;

            // Пара попыток: у части ценностей меши сидят на SkinnedMeshRenderer,
            // такие просто пропускаем и берём следующую.
            for (int attempt = 0; attempt < 6; attempt++)
            {
                var valuable = Targets.PickRandomValuable();
                if (!valuable) return;

                var built = BuildModel(valuable, slots[index]);
                if (built) { models[index] = built; return; }
            }
        }

        private static GameObject BuildModel(ValuableObject source, Transform slot)
        {
            var root = new GameObject("Model");
            root.transform.SetParent(slot, false);

            var inner = new GameObject("Inner").transform;
            inner.SetParent(root.transform, false);

            var origin = source.transform;
            int copied = 0;

            foreach (var filter in source.GetComponentsInChildren<MeshFilter>(includeInactive: false))
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (!renderer || !renderer.enabled || !filter.sharedMesh) continue;

                var piece = new GameObject(filter.name);
                piece.transform.SetParent(inner, false);
                piece.transform.localPosition = origin.InverseTransformPoint(filter.transform.position);
                piece.transform.localRotation = Quaternion.Inverse(origin.rotation) * filter.transform.rotation;
                piece.transform.localScale = filter.transform.lossyScale;

                piece.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                var copy = piece.AddComponent<MeshRenderer>();
                copy.sharedMaterials = renderer.sharedMaterials;
                copy.shadowCastingMode = ShadowCastingMode.Off;
                copy.receiveShadows = false;
                copied++;
            }

            if (copied == 0) { Object.Destroy(root); return null; }

            // Приводим все ценности к одному экранному размеру: иначе рояль
            // займёт весь стенд, а флешку не разглядеть.
            var renderers = root.GetComponentsInChildren<Renderer>();
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            inner.localPosition = -root.transform.InverseTransformPoint(bounds.center);

            float largest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            root.transform.localScale = Vector3.one * (largest > 0.0001f ? ModelSize / largest : 1f);

            return root;
        }
    }
}
