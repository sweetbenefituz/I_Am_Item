using TMPro;
using UnityEngine;

namespace IamItem
{
    /// <summary>
    /// Подсказка "Press V" мёртвому игроку, шрифтом и цветами игры.
    ///
    /// Своего оформления мод не рисует: берём готовую подсказку "Press E"
    /// из SpectateHeadUI, клонируем её и ставим над ванильной. Шрифт,
    /// размер, обводка и дрожание достаются даром.
    ///
    /// Подсказка висит всё время, пока игрок мёртв, а не только когда шкала
    /// набралась. Иначе про мод нельзя было узнать, пока голова не зарядится
    /// полностью: игрок просто не знал, что такая кнопка есть. Пока вселяться
    /// рано, подсказка показывает процент заряда.
    /// </summary>
    internal static class PossessPrompt
    {
        // Порядок сверху вниз: наша надпись, под ней витрина, под ней ванильная
        // подсказка и голова. Так же, как у самой игры — надпись над головой.
        // Обе высоты считаются от ванильной подсказки.

        /// <summary>На сколько пикселей выше ванильной подсказки наша надпись.</summary>
        private const float OffsetY = 102f;

        /// <summary>На сколько пикселей выше ванильной подсказки центр витрины.</summary>
        private const float PreviewOffsetY = 50f;

        private static GameObject clone;
        private static TextMeshProUGUI text;
        private static string shownText;

        public static void Tick()
        {
            var ui = SpectateHeadUI.instance;
            if (!ui || !ui.promptTransform || !ui.promptTargetTransform || !Possession.PossessReady())
            {
                if (clone) clone.SetActive(false);
                Preview.Hide();
                return;
            }

            if (!clone) Build(ui);
            if (!clone) { Preview.Hide(); return; }

            if (!clone.activeSelf) clone.SetActive(true);

            bool ready = Possession.CanPossess();
            string wanted = ready ? ReadyText() : ChargingText();
            if (text && shownText != wanted)
            {
                shownText = wanted;
                text.text = wanted;
            }

            // Ванильная подсказка при скрытии уезжает вниз, поэтому держимся
            // не за неё, а за её опорную точку — она стоит на месте всегда.
            var anchor = new Vector3(
                ui.promptTransform.position.x,
                ui.promptTargetTransform.position.y + 10f + OffsetY,
                clone.transform.position.z);
            clone.transform.position = anchor;

            Preview.Tick(ui.promptTransform.parent as RectTransform, anchor, PreviewOffsetY, ready);
        }

        private static string ReadyText()
        {
            return "<color=#FF8C00>Press</color> <color=white>"
                   + Cfg.PossessKey.Value.ToString().ToUpperInvariant() + "</color>";
        }

        private static string ChargingText()
        {
            float need = Mathf.Max(0.01f, Cfg.MinEnergyToPossess.Value);
            int percent = Mathf.Clamp(Mathf.FloorToInt(Possession.Energy / need * 100f), 0, 99);
            return "<color=#FF8C00>" + Cfg.PossessKey.Value.ToString().ToUpperInvariant()
                   + "</color> <color=#808080>" + percent + "%</color>";
        }

        private static void Build(SpectateHeadUI ui)
        {
            clone = Object.Instantiate(ui.promptTransform.gameObject, ui.promptTransform.parent);
            clone.name = "IamItemPossessPrompt";
            text = clone.GetComponentInChildren<TextMeshProUGUI>(true);
            shownText = null;

            if (!text)
            {
                Plugin.Log.LogWarning("Spectate prompt has no text, possess hint stays hidden.");
                Object.Destroy(clone);
                clone = null;
            }
        }
    }
}
