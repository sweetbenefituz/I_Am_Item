using UnityEngine;

namespace IamItem
{
    /// <summary>
    /// Раздел 9.2: полоса стабильности, индикатор силы рывка, подсказки по
    /// клавишам и короткие сообщения. Рисуется штатным IMGUI — своих
    /// текстур и префабов мод не добавляет.
    /// </summary>
    internal sealed class Hud : MonoBehaviour
    {
        private static string message;
        private static float messageTimer;

        private static Texture2D white;
        private GUIStyle centered;

        public static void Message(string text, float seconds)
        {
            message = text;
            messageTimer = seconds;
        }

        private void Update()
        {
            if (messageTimer > 0f) messageTimer -= Time.deltaTime;
            Speech.Tick();
            PossessPrompt.Tick();
            ExitGrace.Tick();

            // Игра выключает GameObject мёртвого игрока, поэтому собственные
            // Update и FixedUpdate компонента Possession не идут. Тикаем сами
            // с этого объекта: он живёт всю игру.
            for (int i = Possession.All.Count - 1; i >= 0; i--)
            {
                var p = Possession.All[i];
                if (p) p.Tick();
            }
        }

        private void FixedUpdate()
        {
            for (int i = Possession.All.Count - 1; i >= 0; i--)
            {
                var p = Possession.All[i];
                if (p) p.PhysicsTick();
            }
        }

        private void OnGUI()
        {
            if (!white)
            {
                white = new Texture2D(1, 1);
                white.SetPixel(0, 0, Color.white);
                white.Apply();
                white.hideFlags = HideFlags.HideAndDontSave;
            }
            if (centered == null)
            {
                centered = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 16,
                };
            }

            if (messageTimer > 0f && !string.IsNullOrEmpty(message))
            {
                GUI.color = Color.white;
                GUI.Label(new Rect(0f, Screen.height * 0.35f, Screen.width, 30f), message, centered);
            }

            DrawPossessorNames();

            var me = Possession.Local;
            if (me == null || !me.Active) { GUI.color = Color.white; return; }

            float energy = Possession.LocalStamina;
            float width = Screen.width * 0.28f;
            float height = 10f;
            float x = (Screen.width - width) * 0.5f;
            float y = Screen.height - 90f;

            Color bar = energy < 0.15f
                ? Color.Lerp(new Color(0.4f, 0f, 0f), Color.red, Mathf.PingPong(Time.time * 4f, 1f))
                : new Color(0.55f, 0.85f, 1f);

            Fill(new Rect(x - 2f, y - 2f, width + 4f, height + 4f), new Color(0f, 0f, 0f, 0.55f));
            Fill(new Rect(x, y, width * Mathf.Clamp01(energy), height), bar);

            if (me.ChargeVisual > 0.01f)
            {
                Fill(new Rect(x, y + height + 6f, width * Mathf.Clamp01(me.ChargeVisual), 5f),
                     new Color(1f, 0.85f, 0.2f));
            }

            {
                GUI.color = new Color(1f, 1f, 1f, 0.85f);
                string hints = me.Class == ItemClass.Heavy
                    ? "WASD  crawl    Space  slide    LMB  noise    E  leave"
                    : "WASD  crawl    Space  jump     LMB  noise    E  leave";
                GUI.Label(new Rect(0f, y - 56f, Screen.width, 24f), hints, centered);
            }

            GUI.color = Color.white;
        }

        /// <summary>Раздел 9.1: имя вселившегося над предметом. По умолчанию выключено.</summary>
        private void DrawPossessorNames()
        {
            if (!Cfg.ShowPossessorName.Value) return;
            var camera = UnityEngine.Camera.main;
            if (!camera) return;

            foreach (var p in Possession.All)
            {
                if (!p.Active || !p.avatar || !p.Target) continue;

                Vector3 world = p.Target.centerPoint + Vector3.up * 0.35f;
                Vector3 screen = camera.WorldToScreenPoint(world);
                if (screen.z <= 0f) continue;   // за спиной

                GUI.color = new Color(1f, 1f, 1f, 0.85f);
                GUI.Label(new Rect(screen.x - 100f, Screen.height - screen.y - 12f, 200f, 24f),
                          p.avatar.playerName, centered);
            }
            GUI.color = Color.white;
        }

        private static void Fill(Rect rect, Color color)
        {
            GUI.color = color;
            GUI.DrawTexture(rect, white);
            GUI.color = Color.white;
        }
    }
}
