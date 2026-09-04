namespace IamItem
{
    /// <summary>
    /// Чистая арифметика баланса — без Unity, чтобы её можно было проверить
    /// вне игры (см. tests/BalanceTest).
    /// </summary>
    internal static class Balance
    {
        /// <summary>
        /// Сколько секунд держит полная шкала. Кривая ровно такая же, как у
        /// ванильного управления головой: первый уровень апгрейда даёт +20%
        /// от базы, каждый следующий на 5% слабее предыдущего.
        /// База 30 -> 30 / 36 / 41.7 / 47.115 секунды.
        /// </summary>
        public static float DurationFor(float baseDuration, float upgradeLevel)
        {
            float total = baseDuration;
            float step = baseDuration * 0.2f;
            for (float i = upgradeLevel; i > 0f; i--)
            {
                total += step;
                step *= 0.95f;
            }
            return total < 1f ? 1f : total;
        }

        /// <summary>
        /// Сколько прыжков даёт полная выносливость, если прыгать без пауз.
        /// Считает обе статьи расхода: саму цену прыжка и утечку по времени,
        /// пока идёт зарядка и откат. Ради этого числа шкалу и отвязали от
        /// энергии головы: на общей шкале выходило меньше пяти.
        /// </summary>
        /// <param name="jumpCost">Цена одного прыжка, доля шкалы.</param>
        /// <param name="idleSeconds">За сколько секунд шкала уходит в простое.</param>
        /// <param name="secondsPerJump">Зарядка плюс откат, секунды.</param>
        public static int JumpsPerBar(float jumpCost, float idleSeconds, float secondsPerJump)
        {
            float perJump = jumpCost;
            if (idleSeconds > 0f) perJump += secondsPerJump / idleSeconds;
            if (perJump <= 0f) return int.MaxValue;
            return (int)(1f / perJump);
        }
    }
}
