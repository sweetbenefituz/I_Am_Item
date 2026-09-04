using BepInEx.Configuration;
using UnityEngine;

namespace IamItem
{
    /// <summary>
    /// Все числа баланса из разделов 5-7 диздока. Правятся в
    /// BepInEx/config/sweet.iamitem.cfg без пересборки мода.
    /// </summary>
    internal static class Cfg
    {
        public static ConfigEntry<bool> Enabled;
        public static ConfigEntry<KeyCode> PossessKey;

        public static ConfigEntry<float> PossessionDuration;
        public static ConfigEntry<float> EntryCost;
        public static ConfigEntry<float> MinEnergyToPossess;
        public static ConfigEntry<float> JumpCost;
        public static ConfigEntry<float> CrawlSpeed;
        public static ConfigEntry<float> CrawlAcceleration;
        public static ConfigEntry<float> CrawlSpin;
        public static ConfigEntry<float> ExitGraceSeconds;
        public static ConfigEntry<float> ExitGraceMaxSeconds;

        public static ConfigEntry<bool> ValuableShield;
        public static ConfigEntry<float> GrabShieldDelay;
        public static ConfigEntry<bool> RamEnabled;
        public static ConfigEntry<float> RamMinSpeed;
        public static ConfigEntry<float> RamFullSpeed;
        public static ConfigEntry<int> RamDamage;
        public static ConfigEntry<float> RamHitCooldown;
        public static ConfigEntry<bool> RamHurtsPlayers;
        public static ConfigEntry<int> RamPlayerDamage;
        public static ConfigEntry<bool> RamTumblesPlayers;

        public static ConfigEntry<float> WallHitCostLight;
        public static ConfigEntry<float> WallHitCostMedium;
        public static ConfigEntry<float> WallHitCostHeavy;

        public static ConfigEntry<float> RattleCost;
        public static ConfigEntry<float> RattleRadiusLight;
        public static ConfigEntry<float> RattleRadiusMedium;
        public static ConfigEntry<float> RattleRadiusHeavy;
        public static ConfigEntry<float> KnockCost;

        public static ConfigEntry<bool> HeavySlideOnly;

        public static ConfigEntry<bool> SadPhrasesEnabled;
        public static ConfigEntry<bool> CartPhrasesEnabled;
        public static ConfigEntry<bool> BreakPhrasesEnabled;

        public static ConfigEntry<bool> ShowPossessorName;
        public static ConfigEntry<bool> PreviewEnabled;
        public static ConfigEntry<int> PreviewCount;
        public static ConfigEntry<bool> GlowEnabled;
        public static ConfigEntry<float> CameraDistance;

        public static void Bind(ConfigFile c)
        {
            Enabled = c.Bind("General", "Enabled", true,
                "Master switch for the whole mod.");
            PossessKey = c.Bind("General", "PossessKey", KeyCode.V,
                "Key a dead spectating player presses to possess a random valuable. " +
                "Exit is the vanilla Interact key (E).");

            PossessionDuration = c.Bind("Balance", "PossessionDuration", 90f,
                "Seconds a full stamina bar lasts while doing nothing at all, before the Death Head " +
                "Battery upgrade. Every action costs on top of this.");
            EntryCost = c.Bind("Balance", "EntryCost", 1f,
                "Head energy spent the moment you possess. This is the cooldown between possessions: " +
                "head energy refills on its own, stamina inside the item is a separate bar.");
            MinEnergyToPossess = c.Bind("Balance", "MinEnergyToPossess", 0.75f,
                "Head energy needed before possession is allowed. Share of the bar, 0..1. " +
                "Head energy refills in about 100 seconds, so 0.75 is roughly a 75 second wait.");
            JumpCost = c.Bind("Balance", "JumpCost", 0.085f,
                "Stamina spent per jump or dash, on top of the idle drain. " +
                "0.085 leaves about 8 jumps on a full bar.");
            ExitGraceSeconds = c.Bind("Balance", "ExitGraceSeconds", 3f,
                "Seconds the item has to lie still after the ghost leaves before it can break again. " +
                "The item is indestructible until it has settled: a fixed grace ran out while the item " +
                "was still rolling and it smashed anyway.");
            ExitGraceMaxSeconds = c.Bind("Balance", "ExitGraceMaxSeconds", 20f,
                "Hard ceiling on that protection, seconds. Stops an item that never settles - falling down " +
                "a shaft, stuck spinning - from staying indestructible forever.");

            CameraDistance = c.Bind("General", "CameraDistance", 0.6f,
                "Metres the camera keeps behind the possessed item, on top of the item's own size. " +
                "0 puts the camera inside the item, first person.");

            ValuableShield = c.Bind("Shield", "ValuableShield", true,
                "While a ghost is inside, the item loses no money from any impact. Deadly pits, ram mode " +
                "and living hands still break it.");
            GrabShieldDelay = c.Bind("Shield", "GrabShieldDelay", 1.5f,
                "Seconds the shield stays off after a living player lets the item go. " +
                "While the item is in someone's hands it breaks by the normal rules of the game.");
            RamEnabled = c.Bind("Ram", "RamEnabled", true,
                "A possessed item moving fast enough hurts what it hits. No key to press: speed is the weapon. " +
                "The item itself never loses money for it - the risk is where you end up, who you hit and what you break.");
            RamMinSpeed = c.Bind("Ram", "RamMinSpeed", 5f,
                "Metres per second below which a hit does no damage at all. Crawling never hurts anyone.");
            RamFullSpeed = c.Bind("Ram", "RamFullSpeed", 12f,
                "Metres per second that counts as a full power hit. Between this and RamMinSpeed damage scales up.");
            RamDamage = c.Bind("Ram", "RamDamage", 35,
                "Damage a full power hit does to an enemy, before the item size multiplier " +
                "(small 0.6, medium 1.0, big 1.6) and the enemy's own object damage multiplier. " +
                "Enough to finish small enemies, not enough to solo a big one.");
            RamHitCooldown = c.Bind("Ram", "RamHitCooldown", 0.6f,
                "Seconds between damaging hits. Stops one long roll from counting as ten hits.");
            RamHurtsPlayers = c.Bind("Ram", "RamHurtsPlayers", true,
                "A fast item hurts living players too. This is the price of a heavy item flying down a corridor.");
            RamPlayerDamage = c.Bind("Ram", "RamPlayerDamage", 18,
                "Damage a full power hit does to a living player, before the item size multiplier.");
            RamTumblesPlayers = c.Bind("Ram", "RamTumblesPlayers", true,
                "A fast item also knocks a living player off their feet.");

            WallHitCostLight = c.Bind("Impacts", "WallHitCostLight", 0f,
                "Stamina lost on a light impact. Zero by default: landing after your own jump counts " +
                "as a light impact, and charging for that punished simply moving around.");
            WallHitCostMedium = c.Bind("Impacts", "WallHitCostMedium", 0.05f, "Stamina lost on a medium impact.");
            WallHitCostHeavy = c.Bind("Impacts", "WallHitCostHeavy", 0.10f, "Stamina lost on a heavy impact.");

            RattleCost = c.Bind("Noise", "RattleCost", 0.04f, "Stamina spent per rattle.");
            RattleRadiusLight = c.Bind("Noise", "RattleRadiusLight", 6f, "Enemy attract radius for Tiny and Small items, meters.");
            RattleRadiusMedium = c.Bind("Noise", "RattleRadiusMedium", 9f, "Enemy attract radius for Medium items, meters.");
            RattleRadiusHeavy = c.Bind("Noise", "RattleRadiusHeavy", 14f, "Enemy attract radius for Big and larger items, meters.");
            KnockCost = c.Bind("Noise", "KnockCost", 0.02f, "Stamina spent per knock while a living player holds the item.");

            CrawlSpeed = c.Bind("Movement", "CrawlSpeed", 3.5f,
                "Top speed of WASD crawling along the floor, metres per second. 0 turns crawling off, jumps only. " +
                "Heavy items are capped lower by their own speed limit.");
            CrawlAcceleration = c.Bind("Movement", "CrawlAcceleration", 14f,
                "How hard the item is pushed towards CrawlSpeed. A flat push lost to floor friction and items only " +
                "rocked in place.");
            CrawlSpin = c.Bind("Movement", "CrawlSpin", 6f,
                "Extra roll applied across the crawl direction so the item rolls instead of sliding. 0 turns rolling off.");
            HeavySlideOnly = c.Bind("Movement", "HeavySlideOnly", true,
                "Big, Wide, Tall and VeryTall items slide along the floor instead of jumping.");

            SadPhrasesEnabled = c.Bind("Phrases", "SadPhrasesEnabled", true, "Phrase when a living player picks up a possessed item.");
            CartPhrasesEnabled = c.Bind("Phrases", "CartPhrasesEnabled", true, "Phrase when a possessed item goes into the cart.");
            BreakPhrasesEnabled = c.Bind("Phrases", "BreakPhrasesEnabled", true, "Phrase when a possessed item is destroyed.");

            ShowPossessorName = c.Bind("Visuals", "ShowPossessorName", false, "Draw the possessing player name above the item.");
            PreviewEnabled = c.Bind("Visuals", "PreviewEnabled", true,
                "Spinning valuable models above the spectate head, so a dead player can see the mod exists " +
                "before the bar is full. They are real items from this level, picked at random.");
            PreviewCount = c.Bind("Visuals", "PreviewCount", 3,
                "How many models stand on the shelf, 1 to 5. They are picked once per death and just spin.");
            GlowEnabled = c.Bind("Visuals", "GlowEnabled", true, "Fresnel glow on a possessed item.");
        }
    }
}
