using System.Collections.Generic;
using UnityEngine;

namespace IamItem
{
    /// <summary>
    /// Раздел 9.3: вспышка и звуки входа-выхода. Всё берётся из игры, своих
    /// файлов мод не содержит.
    /// </summary>
    internal static class Effects
    {
        public static void Teleport(Vector3 position)
        {
            var prefab = AssetManager.instance ? AssetManager.instance.prefabTeleportEffect : null;
            if (prefab) Object.Instantiate(prefab, position, Quaternion.identity);
        }

        public static void PlayEquip(Vector3 position)
        {
            AssetManager.instance?.soundEquip?.Play(position);
        }

        public static void PlayUnequip(Vector3 position)
        {
            AssetManager.instance?.soundUnequip?.Play(position);
        }
    }

    /// <summary>
    /// Раздел 8.2: принудительная фраза живого игрока с озвучкой. Работает
    /// тем же механизмом, что любовное зелье и предательство.
    ///
    /// Приоритет 20 — самый низкий из встречающихся в игре (зелье 10,
    /// предательство 1), поэтому наши фразы никогда ничего не перебивают.
    /// </summary>
    internal static class Speech
    {
        // Очередь фраз. Игре их нельзя отдавать сразу: пока чат занят
        // предыдущей репликой, PossessChatScheduleStart молча выбрасывает
        // сообщение. Копим и отдаём по одной, как только чат освободится.
        private static readonly Queue<string> pending = new Queue<string>();

        public static void Tick()
        {
            if (pending.Count == 0) return;

            var chat = ChatManager.instance;
            if (!chat) return;

            // Чат занят: игрок печатает, или игра дочитывает чью-то реплику.
            // Ждём, а не выбрасываем фразу.
            if (!chat.StateIsInactive()) return;

            // Признак "идёт принудительная реплика" игра снимает у себя в
            // PossessChatCustomLogic. Если чат уже вернулся в покой, а признак
            // остался, он застрял — и без этой строчки мод говорил ровно один
            // раз за всю игру.
            if (chat.currentPossessChatID != ChatManager.PossessChatID.None)
                chat.currentPossessChatID = ChatManager.PossessChatID.None;

            string message = pending.Dequeue();

            // Ouch выбран потому, что у него нет побочных эффектов (13.2).
            chat.PossessChatScheduleStart(20);
            chat.PossessChat(ChatManager.PossessChatID.Ouch, message, 1f, new Color(0.75f, 0.75f, 0.8f, 1f));
            chat.PossessChatScheduleEnd();
        }

        /// <summary>
        /// Ставит фразу в очередь. Кулдауна нет намеренно: схватил предмет
        /// посреди фразы — вторая дождётся конца первой и прозвучит следом.
        /// </summary>
        public static void Say(string message)
        {
            if (!string.IsNullOrEmpty(message)) pending.Enqueue(message);
        }
    }
}
