using System;
using System.Diagnostics;
using IamItem;

// Проверка кривой апгрейда батарейки. Числа взяты из диздока, раздел 5.1:
// база 30 секунд, дальше 36 / 41.7 / 47.115.
internal static class Program
{
    private static void Near(float actual, float expected, string what)
    {
        if (Math.Abs(actual - expected) > 0.01f)
            throw new Exception($"{what}: ожидалось {expected}, получено {actual}");
        Console.WriteLine($"  ok  {what} = {actual:0.###}");
    }

    private static int Main()
    {
        Near(Balance.DurationFor(30f, 0f), 30f, "без апгрейда");
        Near(Balance.DurationFor(30f, 1f), 36f, "1 уровень");
        Near(Balance.DurationFor(30f, 2f), 41.7f, "2 уровня");
        Near(Balance.DurationFor(30f, 3f), 47.115f, "3 уровня");

        // Ванильная голова: база 25, шаг 5 -> 30 / 34.75 / 39.2625.
        Near(Balance.DurationFor(25f, 1f), 30f, "ваниль, 1 уровень");
        Near(Balance.DurationFor(25f, 2f), 34.75f, "ваниль, 2 уровня");
        Near(Balance.DurationFor(25f, 3f), 39.2625f, "ваниль, 3 уровня");

        // Нижняя граница: конфиг может выставить и ноль, делить на ноль нельзя.
        Debug.Assert(Balance.DurationFor(0f, 0f) >= 1f);
        Near(Balance.DurationFor(0f, 5f), 1f, "нулевая длительность зажата снизу");

        // Выносливость: полная шкала должна давать примерно десяток прыжков.
        // Настройки по умолчанию: цена прыжка 0.085, простой съедает шкалу за
        // 90 секунд, цикл прыжка 1.2 сек (зарядка 0.7 плюс откат 0.5 —
        // зарядку укоротили, чтобы прыжок бил сразу и сильно).
        const float cycle = 1.2f;
        int jumps = Balance.JumpsPerBar(0.085f, 90f, cycle);
        if (jumps < 8)
            throw new Exception($"прыжков на полной шкале {jumps}, надо не меньше 8");
        if (jumps > 12)
            throw new Exception($"прыжков на полной шкале {jumps}, это уже слишком дёшево");
        Console.WriteLine($"  ok  прыжков на полной шкале = {jumps}");

        // Шкалу должна съедать в первую очередь сама стрельба прыжками, а не
        // утечка по времени: иначе цена прыжка ни на что не влияет.
        int idleJumps = Balance.JumpsPerBar(0f, 90f, cycle);
        if (idleJumps < jumps * 2)
            throw new Exception($"цена прыжка почти не видна: {jumps} против {idleJumps} на простое");
        Console.WriteLine($"  ok  простой без прыжков = {idleJumps} циклов");

        Debug.Assert(Balance.JumpsPerBar(0f, 0f, cycle) > 0);

        Console.WriteLine("Balance: все проверки прошли.");
        return 0;
    }
}
