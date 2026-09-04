using System.Collections.Generic;
using UnityEngine;

namespace IamItem
{
    /// <summary>Три класса размера из раздела 6 диздока.</summary>
    internal enum ItemClass { Light, Medium, Heavy }

    /// <summary>
    /// Раздел 4.2: мешок ценностей уровня и правила отсева. Живёт на
    /// мастер-клиенте, он один выбирает предмет.
    /// </summary>
    internal static class Targets
    {
        private static readonly List<ValuableObject> pool = new List<ValuableObject>();

        public static ItemClass ClassOf(ValuableObject v)
        {
            switch (v.volumeType)
            {
                case ValuableVolume.Type.Tiny:
                case ValuableVolume.Type.Small:
                    return ItemClass.Light;
                case ValuableVolume.Type.Medium:
                    return ItemClass.Medium;
                default:
                    return ItemClass.Heavy;
            }
        }

        /// <summary>Случайная подходящая ценность, или null если таких нет.</summary>
        public static PhysGrabObject PickRandom()
        {
            var valuable = PickRandomValuable();
            return valuable ? valuable.physGrabObject : null;
        }

        /// <summary>
        /// То же самое, но самой ценностью. Нужно витрине с модельками: ей от
        /// предмета нужны меши, а не физика.
        /// </summary>
        public static ValuableObject PickRandomValuable()
        {
            var director = ValuableDirector.instance;
            if (!director) return null;

            pool.Clear();
            foreach (var v in director.valuableList)
                if (IsEligible(v))
                    pool.Add(v);

            if (pool.Count == 0) return null;
            return pool[Random.Range(0, pool.Count)];
        }

        private static bool IsEligible(ValuableObject v)
        {
            // valuableList не чистится при уничтожении предмета, см. 13.4.
            if (!v || !v.gameObject || !v.gameObject.activeInHierarchy) return false;

            var pgo = v.physGrabObject;
            if (!pgo || !pgo.rb) return false;

            // Технический склад невыгруженных объектов.
            if (Vector3.Distance(pgo.transform.position, AssetManager.instance.physDisabledPosition) < 100f)
                return false;

            if (pgo.rb.isKinematic) return false;

            var impact = v.impactDetector;
            if (!impact) return false;
            if (impact.inCart) return false;
            if (impact.isEnemy) return false;                       // мимик
            if (v.GetComponent<EnemyValuable>()) return false;      // он же, вторая проверка
            if (pgo.GetComponent<PlayerDeathHead>()) return false;  // чужая голова

            // Покупные инструменты: пушка, граната, аптечка, дрон.
            if (pgo.GetComponent<ItemAttributes>()) return false;

            if (v.roomVolumeCheck && v.roomVolumeCheck.inExtractionPoint) return false;

            if (Possession.IsPossessed(pgo)) return false;

            return true;
        }
    }
}
