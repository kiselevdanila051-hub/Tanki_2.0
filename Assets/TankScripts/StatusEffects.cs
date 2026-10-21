using System.Collections.Generic;
using UnityEngine;

// Эффекты на танке:
//  Нагрев (Огнемёт): танк краснеет и продолжает получать урон от горения.
//  Заморозка (Фриз): танк покрывается инеем (голубеет), замедляются ход, поворот корпуса и башни.
// Огонь и холод гасят друг друга; струя союзника только снимает эффект: Огнемёт размораживает (Thaw), Фриз остужает (Cool).
// Износ (Wear) и обгоревший остов (Burnt) темнят окраску.
// Горящий танк — языки пламени по броне (ParticleFx.BurnOn), замороженный — облачка инея и искры льда (FrostOn).
// Окраска меняется в свойствах материалов самого танка (у корпуса, башни и ствола они и так свои — «(повреждения)»,
// общие материалы ходовой копируются один раз — «(эффекты)»). Не MaterialPropertyBlock: рендер с блоком выпадает
// из SRP Batcher и переполняет кольцевой буфер видеокарты.
public class StatusEffects : MonoBehaviour
{
    public float Heat { get; private set; }     // 0..1
    public float Freeze { get; private set; }   // 0..1
    public float Wear;                           // 0..1 износ (задаёт TankDamage): окраска темнеет
    public bool Burnt;                           // обгоревший остов

    float burnDps, burnUntil;
    float freezeHoldUntil, freezeSlow = 0.5f;
    Health health;
    Collider hullCol;
    Rigidbody body;
    float fireAcc, frostAcc;                     // сколько языков пламени / облачков инея накопилось к выпуску

    Renderer[] renderers;
    Material[] tintMats;                         // материалы танка, которые красятся (без повторов)
    Color[] baseColors;
    Vector4[] baseFx;
    Color[] baseFxColors;
    readonly List<Material> copies = new List<Material>();   // копии общих материалов — уничтожаются вместе с танком
    bool tinted;
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int MetalFxId = Shader.PropertyToID("_MetalFx");            // металлические детали (Tanks/DamageLit)
    static readonly int MetalFxColorId = Shader.PropertyToID("_MetalFxColor");
    const string CopySuffix = " (эффекты)";

    // Множитель скорости хода, поворота корпуса и башни (1 = без замедления)
    public float SpeedMultiplier
    {
        get { return Mathf.Lerp(1f, freezeSlow, Freeze); }
    }

    public void AddHeat(float amount, float dps, float duration)
    {
        Heat = Mathf.Clamp01(Heat + amount);
        Freeze = Mathf.Max(0f, Freeze - amount * 2f);
        burnDps = dps;
        burnUntil = Time.time + duration;
    }

    public void AddFreeze(float amount, float slowFactor, float hold)
    {
        Freeze = Mathf.Clamp01(Freeze + amount);
        Heat = Mathf.Max(0f, Heat - amount * 2f);
        if (Freeze > 0.3f) burnUntil = 0f;     // холод тушит огонь
        freezeSlow = slowFactor;
        freezeHoldUntil = Time.time + hold;
    }

    // Союзник отогревает замороженного Огнемётом: иней сходит, замедление уходит; сам танк при этом не загорается
    public void Thaw(float amount)
    {
        Freeze = Mathf.Max(0f, Freeze - amount);
        freezeHoldUntil = 0f;
    }

    // Союзник остужает разогретого Фризом: горение гаснет, нагрев спадает; сам танк при этом не леденеет
    public void Cool(float amount)
    {
        Heat = Mathf.Max(0f, Heat - amount);
        burnUntil = 0f;
    }

    float lastWear = -1f, lastFreeze = -1f;
    bool lastBurnt;

    public void InvalidateRenderers()
    {
        ClearTint();
        renderers = null;
        tintMats = null;
    }

    // Башню сорвало: копии материалов её рендеров теперь принадлежат улетевшему обломку
    public void HandOver(Transform part, Debris owner)
    {
        if (owner == null) return;
        foreach (Renderer r in part.GetComponentsInChildren<Renderer>())
            foreach (Material m in r.sharedMaterials)
                if (m != null && copies.Remove(m)) owner.AddOwned(m);
    }

    void OnDestroy()
    {
        foreach (Material m in copies) if (m != null) Destroy(m);
        copies.Clear();
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (health == null) health = GetComponent<Health>();

        if (Time.time < burnUntil)
        {
            if (health != null && health.Current > 0f) health.TakeDamage(burnDps * dt);
            Heat = Mathf.Max(Heat, 0.35f);
        }
        else Heat = Mathf.MoveTowards(Heat, 0f, 0.5f * dt);

        if (Time.time >= freezeHoldUntil) Freeze = Mathf.MoveTowards(Freeze, 0f, 0.5f * dt);

        // видимые огонь и иней на броне (частицы едут вместе с танком)
        if (health != null && health.Current > 0f && (Heat > 0.05f || Freeze > 0.15f))
        {
            if (hullCol == null) hullCol = GetComponent<Collider>();
            if (body == null) body = GetComponent<Rigidbody>();
            Vector3 vel = NetworkTank.VelocityOf(body);
            if (Time.time < burnUntil || Heat > 0.4f)
            {
                fireAcc += Heat * 14f * dt;
                for (; fireAcc >= 1f; fireAcc -= 1f) ParticleFx.BurnOn(hullCol, vel, Heat);
            }
            if (Freeze > 0.15f)
            {
                frostAcc += Freeze * 9f * dt;
                for (; frostAcc >= 1f; frostAcc -= 1f) ParticleFx.FrostOn(hullCol, vel, Freeze);
            }
        }

        ApplyTint();
    }

    void ApplyTint()
    {
        float k = Mathf.Max(Heat, Freeze);
        if (k < 0.01f && Wear < 0.01f && !Burnt)
        {
            if (tinted) ClearTint();
            return;
        }

        // Без изменений — не трогаем материалы (нагрев мерцает, его обновляем каждый кадр)
        bool hot = Heat >= Freeze;
        bool pulsing = hot && Heat >= 0.01f;
        if (tinted && !pulsing && Mathf.Abs(Wear - lastWear) < 0.005f && Mathf.Abs(Freeze - lastFreeze) < 0.005f && Burnt == lastBurnt
            && renderers != null && RenderersAlive()) return;
        lastWear = Wear; lastFreeze = Freeze; lastBurnt = Burnt;

        if (renderers == null || !RenderersAlive()) CollectRenderers();

        Color target = hot
            ? Color.Lerp(new Color(1f, 0.4f, 0.1f), new Color(1f, 0.1f, 0.05f), Mathf.PingPong(Time.time * 3f, 1f))
            : new Color(0.7f, 0.9f, 1f);
        float amount = (hot ? Heat : Freeze) * 0.8f;
        float dark = Burnt ? 0.28f : 1f - 0.45f * Wear;

        for (int i = 0; i < tintMats.Length; i++)
        {
            Material m = tintMats[i];
            if (m == null) continue;
            Color b = baseColors[i];
            Color worn = new Color(b.r * dark, b.g * dark, b.b * dark, b.a);
            m.SetColor(BaseColorId, Color.Lerp(worn, target, amount));
            m.SetVector(MetalFxId, new Vector4(dark, amount, 0f, 0f));
            m.SetColor(MetalFxColorId, target);
        }
        tinted = true;
    }

    bool RenderersAlive()
    {
        foreach (Renderer r in renderers) if (r == null) return false;
        return true;
    }

    // Рендеры танка (кроме частиц) и их материалы: свои — как есть, общие — заменяются копиями (одна копия на общий материал)
    void CollectRenderers()
    {
        if (tinted) ClearTint();
        List<Renderer> list = new List<Renderer>();
        List<Material> mats = new List<Material>();
        Dictionary<Material, Material> copyOf = new Dictionary<Material, Material>();
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
            list.Add(r);
            Material[] shared = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < shared.Length; i++)
            {
                Material m = shared[i];
                if (m == null || !m.HasProperty(BaseColorId)) continue;
                if (!m.name.EndsWith(" (повреждения)") && !m.name.EndsWith(CopySuffix))
                {
                    Material c;
                    if (!copyOf.TryGetValue(m, out c))
                    {
                        c = new Material(m) { name = m.name + CopySuffix };
                        copyOf[m] = c;
                        copies.Add(c);
                    }
                    shared[i] = c;
                    m = c;
                    changed = true;
                }
                if (!mats.Contains(m)) mats.Add(m);
            }
            if (changed) r.sharedMaterials = shared;
        }
        renderers = list.ToArray();
        tintMats = mats.ToArray();
        baseColors = new Color[tintMats.Length];
        baseFx = new Vector4[tintMats.Length];
        baseFxColors = new Color[tintMats.Length];
        for (int i = 0; i < tintMats.Length; i++)
        {
            Material m = tintMats[i];
            baseColors[i] = m.GetColor(BaseColorId);
            baseFx[i] = m.HasProperty(MetalFxId) ? m.GetVector(MetalFxId) : new Vector4(1f, 0f, 0f, 0f);
            baseFxColors[i] = m.HasProperty(MetalFxColorId) ? m.GetColor(MetalFxColorId) : Color.white;
        }
    }

    // Вернуть исходную окраску
    void ClearTint()
    {
        tinted = false;
        if (tintMats == null) return;
        for (int i = 0; i < tintMats.Length; i++)
        {
            Material m = tintMats[i];
            if (m == null) continue;
            m.SetColor(BaseColorId, baseColors[i]);
            if (m.HasProperty(MetalFxId)) m.SetVector(MetalFxId, baseFx[i]);
            if (m.HasProperty(MetalFxColorId)) m.SetColor(MetalFxColorId, baseFxColors[i]);
        }
    }
}
