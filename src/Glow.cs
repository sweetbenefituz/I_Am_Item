using System.Collections.Generic;
using UnityEngine;

namespace IamItem
{
    /// <summary>
    /// Раздел 9.1: свечение по контуру вселённого предмета. Своего шейдера
    /// нет — штатный шейдер игры уже умеет _FresnelColor / _FresnelPower,
    /// ими подсвечивает себя мимик (EnemyValuable).
    ///
    /// renderer.materials создаёт копии материалов. Их обязательно уничтожать
    /// при выходе, иначе за забег накопится мусор (13.4).
    /// </summary>
    internal sealed class Glow
    {
        private static readonly int FresnelColor = Shader.PropertyToID("_FresnelColor");
        private static readonly int FresnelPower = Shader.PropertyToID("_FresnelPower");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        private struct Entry
        {
            public Material material;
            public bool hasFresnel;
            public Color originalColor;
            public float originalPower;
            public Color originalEmission;
        }

        private readonly List<Entry> entries = new List<Entry>();
        private readonly List<Renderer> touched = new List<Renderer>();
        private readonly List<Material[]> originalArrays = new List<Material[]>();

        public Color BaseColor = Color.cyan;

        public void Attach(GameObject root)
        {
            Detach();
            if (!Cfg.GlowEnabled.Value || !root) return;

            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!r || r.sharedMaterials == null || r.sharedMaterials.Length == 0) continue;

                originalArrays.Add(r.sharedMaterials);
                touched.Add(r);

                var copies = r.materials; // создаёт копии, оригиналы не портятся
                foreach (var m in copies)
                {
                    if (!m) continue;
                    var e = new Entry { material = m };
                    if (m.HasProperty(FresnelColor) && m.HasProperty(FresnelPower))
                    {
                        e.hasFresnel = true;
                        e.originalColor = m.GetColor(FresnelColor);
                        e.originalPower = m.GetFloat(FresnelPower);
                    }
                    else if (m.HasProperty(EmissionColor))
                    {
                        e.originalEmission = m.GetColor(EmissionColor);
                    }
                    else
                    {
                        continue; // нет ни фринеля, ни эмиссии — свечение пропускаем
                    }
                    entries.Add(e);
                }
            }

            // Ни фринеля, ни эмиссии ни в одном материале: предмет светиться
            // не будет, и понять это по экрану нельзя. Пишем в лог.
            if (entries.Count == 0 && touched.Count > 0)
                Plugin.Log.LogWarning("No glowable material on " + root.name + ", item stays unlit.");
        }

        /// <param name="ram">Вспышка после удара: резкий красный, частая пульсация.</param>
        public void Tick(bool ram)
        {
            if (entries.Count == 0) return;

            Color color = ram ? Color.red : BaseColor;
            float speed = ram ? 9f : 3f;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * speed);
            float power = Mathf.Lerp(ram ? 1.2f : 2.5f, ram ? 0.4f : 1.2f, pulse);

            foreach (var e in entries)
            {
                if (!e.material) continue;
                if (e.hasFresnel)
                {
                    e.material.SetColor(FresnelColor, color);
                    e.material.SetFloat(FresnelPower, power);
                }
                else
                {
                    e.material.SetColor(EmissionColor, color * Mathf.Lerp(0.15f, 0.6f, pulse));
                }
            }
        }

        public void Detach()
        {
            for (int i = 0; i < touched.Count; i++)
                if (touched[i])
                    touched[i].sharedMaterials = originalArrays[i];

            foreach (var e in entries)
                if (e.material)
                    Object.Destroy(e.material);

            entries.Clear();
            touched.Clear();
            originalArrays.Clear();
        }
    }
}
