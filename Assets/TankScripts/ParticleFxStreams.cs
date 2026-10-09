using System.Collections.Generic;
using UnityEngine;

// Частицы выстрелов и струй (часть ParticleFx):
//   FlameJet — Огнемёт: непрерывная струя жидкого пламени — у сопла раскалённое ядро, дальше турбулентные языки огня
//     (флипбук горения) раздуваются, краснеют и гаснут, на конце струи валит чёрный дым; искры; капли огня разбиваются
//     о стены и броню (столкновения частиц);
//   FrostJet — Фриз: плотная струя ледяного газа — белёсо-голубой пар (флипбук облака, тяжелее воздуха — оседает),
//     внутри яркие струйки жидкого азота, вокруг искрящиеся кристаллы льда;
//   ChargeMotes — искры, стягивающиеся к дулу Рельсы при зарядке; RailSpiral — спираль искр вдоль луча Рельсы;
//   BeamContact — свечение и искры там, где дуга Изиды касается цели.
// Струи — свои системы на дуле (StreamFx включает и выключает выпуск), частицы — в мире, наследуют скорость танка.
public static partial class ParticleFx
{
    static ParticleSystem motes, spiral;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStreams()
    {
        motes = spiral = null;
    }

    static void Cone(ParticleSystem ps, float angle, float radius)
    {
        ParticleSystem.ShapeModule sh = ps.shape;
        sh.enabled = true;
        sh.shapeType = ParticleSystemShapeType.Cone;
        sh.angle = angle;
        sh.radius = radius;
        sh.radiusThickness = 1f;
        ParticleSystem.InheritVelocityModule iv = ps.inheritVelocity;
        iv.enabled = true;
        iv.mode = ParticleSystemInheritVelocityMode.Initial;
        iv.curveMultiplier = 1f;
    }

    static void Turbulence(ParticleSystem ps, float strength, float frequency)
    {
        ParticleSystem.NoiseModule nz = ps.noise;
        nz.enabled = true;
        nz.strength = strength;
        nz.frequency = frequency;
        nz.scrollSpeed = 1.2f;
        nz.octaveCount = 2;
        nz.quality = ParticleSystemNoiseQuality.Medium;
        nz.damping = true;
    }

    // Струя бьётся о стены и броню: частицы растекаются по поверхности
    static void Splash(ParticleSystem ps, float radiusScale)
    {
        ParticleSystem.CollisionModule col = ps.collision;
        col.enabled = true;
        col.type = ParticleSystemCollisionType.World;
        col.mode = ParticleSystemCollisionMode.Collision3D;
        col.quality = ParticleSystemCollisionQuality.Medium;
        col.dampen = 0.75f;
        col.bounce = 0.05f;
        col.lifetimeLoss = 0.12f;
        col.radiusScale = radiusScale;
        col.collidesWith = Physics.DefaultRaycastLayers;
        col.enableDynamicColliders = true;
    }

    static void Rate(ParticleSystem ps, float rate)
    {
        ParticleSystem.EmissionModule em = ps.emission;
        em.rateOverTime = rate;
    }

    static ParticleSystem Jet(string name, Transform muzzle, Material m, AnimationCurve frames = null, float startFrames = 0f)
    {
        ParticleSystem ps = Make(name, muzzle.position, muzzle, false, m, frames, startFrames);
        if (ps == null) return null;
        ps.transform.rotation = muzzle.rotation;                          // конус выпуска — вдоль дула
        return ps;
    }

    static Gradient Keys(Color[] cs, float[] ct, float[] av, float[] at)
    {
        GradientColorKey[] ck = new GradientColorKey[cs.Length];
        for (int i = 0; i < cs.Length; i++) ck[i] = new GradientColorKey(cs[i], ct[i]);
        GradientAlphaKey[] ak = new GradientAlphaKey[av.Length];
        for (int i = 0; i < av.Length; i++) ak[i] = new GradientAlphaKey(av[i], at[i]);
        Gradient g = new Gradient();
        g.SetKeys(ck, ak);
        return g;
    }

    // Огнемёт. Системы выпускают частицы с заданной частотой — StreamFx запоминает её и выключает выпуск
    public static ParticleSystem[] FlameJet(Transform muzzle)
    {
        List<ParticleSystem> list = new List<ParticleSystem>();
        // языки пламени: вылетают быстро, тормозят, раздуваются в 5–6 раз, белое → оранжевое → тёмно-красное
        ParticleSystem f = Jet("Flame Jet", muzzle, FlashMat(Tex(FireOrange), 1.3f), Piecewise(0f, 0f, 1f, 0.55f), 40f);
        if (f == null) return list.ToArray();
        rotRange[f] = 180f;
        Setup(f, true, 0.45f, 0.6f, 18f, 22f, 0.35f, 0.5f, Color.white, new Color(1f, 0.9f, 0.75f), -0.35f, 220);
        // гаснет, не доходя до тёмно-красного: на конце струи — дым, а не красный туман
        Fade(f, Keys(new[] { new Color(1f, 0.95f, 0.8f), new Color(1f, 0.65f, 0.28f), new Color(0.95f, 0.38f, 0.1f), new Color(0.4f, 0.08f, 0.02f) },
                     new[] { 0f, 0.3f, 0.55f, 0.85f }, new[] { 1f, 1f, 0.45f, 0f }, new[] { 0f, 0.35f, 0.6f, 0.82f }), 5f);
        Drag(f, 1.3f);
        Cone(f, 4f, 0.06f);
        Turbulence(f, 1.4f, 0.7f);
        Splash(f, 0.35f);
        ParticleSystem.RotationOverLifetimeModule rot = f.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-1.5f, 1.5f);
        Rate(f, 85f);
        list.Add(f);

        // ядро у сопла: раскалённая струя горючего
        ParticleSystem c = Jet("Flame Core", muzzle, FlashMat(Tex(FireHot), 2.2f), Piecewise(0f, 0f, 1f, 0.15f), 56f);
        if (c != null)
        {
            Setup(c, true, 0.1f, 0.16f, 12f, 15f, 0.2f, 0.28f, Color.white, new Color(1f, 0.9f, 0.7f), 0f, 60);
            Fade(c, Grad(Color.white, new Color(1f, 0.7f, 0.35f), 0f), 2.6f);
            Cone(c, 2f, 0.03f);
            Rate(c, 70f);
            list.Add(c);
        }

        // чёрный дым на конце струи: невидим, пока горит пламя, проявляется там, где огонь гаснет
        ParticleSystem s = Jet("Flame Smoke", muzzle, SmokeMat(Cloud), Piecewise(0f, 0f, 1f, 0.5f), 30f);
        if (s != null)
        {
            rotRange[s] = 180f;
            Setup(s, true, 1.3f, 1.8f, 16f, 20f, 0.6f, 0.9f, new Color(0.13f, 0.12f, 0.11f, 1f), new Color(0.18f, 0.16f, 0.15f, 1f), -0.35f, 120);
            Fade(s, Keys(new[] { Color.white, Color.white }, new[] { 0f, 1f }, new[] { 0f, 0f, 0.75f, 0.45f, 0f }, new[] { 0f, 0.25f, 0.45f, 0.75f, 1f }), 4.5f);
            Drag(s, 1.3f);
            Cone(s, 5f, 0.06f);
            Turbulence(s, 0.9f, 0.5f);
            Splash(s, 0.3f);
            Rate(s, 24f);
            list.Add(s);
        }

        // дрожание горячего воздуха над струёй
        Material hm = HazeMat(false);
        ParticleSystem hz = hm != null ? Jet("Flame Haze", muzzle, hm) : null;
        if (hz != null)
        {
            Setup(hz, true, 0.7f, 0.9f, 11f, 14f, 0.8f, 1.1f, new Color(1f, 1f, 1f, 0.8f), new Color(1f, 1f, 1f, 0.6f), -0.3f, 60);
            Fade(hz, Keys(new[] { Color.white, Color.white }, new[] { 0f, 1f }, new[] { 0f, 1f, 0f }, new[] { 0f, 0.3f, 1f }), 3f);
            Drag(hz, 1.2f);
            Cone(hz, 6f, 0.1f);
            hz.GetComponent<ParticleSystemRenderer>().SetActiveVertexStreams(hazeStreams);
            Rate(hz, 22f);
            list.Add(hz);
        }

        // искры и горящие капли: падают
        ParticleSystem e = Jet("Flame Embers", muzzle, FlashMat(Soft(), 2.5f));
        if (e != null)
        {
            ParticleSystemRenderer r = e.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.03f;
            r.lengthScale = 1.2f;
            Setup(e, true, 0.5f, 0.9f, 10f, 16f, 0.03f, 0.055f, new Color(1f, 0.75f, 0.35f), new Color(1f, 0.5f, 0.15f), 0.8f, 80);
            Fade(e, Grad(Color.white, new Color(1f, 0.3f, 0.05f), 0f), 0.6f);
            Drag(e, 0.8f);
            Cone(e, 10f, 0.05f);
            Rate(e, 18f);
            list.Add(e);
        }
        return list.ToArray();
    }

    // Фриз
    public static ParticleSystem[] FrostJet(Transform muzzle)
    {
        List<ParticleSystem> list = new List<ParticleSystem>();
        // ледяной пар: белёсо-голубой, тяжелее воздуха — оседает; у сопла светится холодным светом
        Material vm = SmokeMat(Cloud);
        if (IsSmoke(vm))
        {
            vm.SetColor("_FireColor", new Color(0.35f, 0.6f, 0.95f));
            vm.SetFloat("_FireGlow", 0.45f);
            vm.SetFloat("_FireTime", 0.5f);
        }
        ParticleSystem v = Jet("Frost Vapor", muzzle, vm, Piecewise(0f, 0f, 1f, 0.5f), 30f);
        if (v == null) return list.ToArray();
        rotRange[v] = 180f;
        Setup(v, true, 0.8f, 1.1f, 17f, 21f, 0.3f, 0.45f, new Color(0.82f, 0.9f, 1f, 0.6f), new Color(0.9f, 0.95f, 1f, 0.5f), 0.25f, 200);
        Fade(v, Keys(new[] { Color.white, Color.white }, new[] { 0f, 1f }, new[] { 0.3f, 1f, 0.6f, 0f }, new[] { 0f, 0.1f, 0.6f, 1f }), 6.5f);
        Drag(v, 1.4f);
        Cone(v, 4f, 0.06f);
        Turbulence(v, 1f, 0.6f);
        Splash(v, 0.3f);
        Rate(v, 70f);
        list.Add(v);

        // струйки жидкого азота внутри
        ParticleSystem c = Jet("Frost Spray", muzzle, FlashMat(Soft(), 1.3f));
        if (c != null)
        {
            ParticleSystemRenderer r = c.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.045f;
            r.lengthScale = 1f;
            Setup(c, true, 0.22f, 0.32f, 20f, 25f, 0.1f, 0.16f, new Color(0.75f, 0.92f, 1f), new Color(0.55f, 0.8f, 1f), 0f, 120);
            Fade(c, Grad(Color.white, new Color(0.6f, 0.8f, 1f), 0f), 2f);
            Drag(c, 1.5f);
            Cone(c, 3f, 0.04f);
            Rate(c, 90f);
            list.Add(c);
        }

        // кристаллы льда: мелкие искры, разлетаются и падают
        ParticleSystem x = Jet("Frost Crystals", muzzle, FlashMat(Soft(), 3f));
        if (x != null)
        {
            Setup(x, true, 0.5f, 0.9f, 12f, 18f, 0.025f, 0.05f, Color.white, new Color(0.7f, 0.9f, 1f), 0.7f, 100);
            Fade(x, Grad(Color.white, new Color(0.6f, 0.85f, 1f), 0f), 0.8f);
            Drag(x, 1f);
            Cone(x, 12f, 0.05f);
            Rate(x, 60f);
            list.Add(x);
        }
        return list.ToArray();
    }

    // ---------- Рельса ----------
    static ParticleSystem Motes()
    {
        if (motes != null) return motes;
        if (Shared(ref motes, "Rail Motes", FlashMat(Soft(), 3f), true, Grad(new Color(0.6f, 0.8f, 1f), Color.white, 0.3f), 0.6f, 0f, 600) != null)
        {
            ParticleSystemRenderer r = motes.GetComponent<ParticleSystemRenderer>();
            r.velocityScale = 0.05f;
            r.lengthScale = 1f;
        }
        return motes;
    }

    // Искры стягиваются к точке (зарядка Рельсы); k — доля зарядки: к концу чаще и ближе
    public static void ChargeMotes(Vector3 center, Color c, float k)
    {
        ParticleSystem m = Motes();
        if (m == null) return;
        int n = Random.value < 0.4f + 0.6f * k ? 1 + Mathf.RoundToInt(2f * k) : 0;
        for (int i = 0; i < n; i++)
        {
            Vector3 dir = Random.onUnitSphere;
            float r = Random.Range(0.6f, 1.3f) * (1.1f - 0.4f * k);
            float life = Random.Range(0.15f, 0.25f);
            One(m, center + dir * r, -dir * (r / life), Random.Range(0.025f, 0.045f), life, Color.Lerp(c, Color.white, 0.5f));
        }
    }

    static ParticleSystem Spiral()
    {
        if (spiral != null) return spiral;
        return Shared(ref spiral, "Rail Spiral", FlashMat(Soft(), 2.5f), false, Grad(Color.white, new Color(0.5f, 0.75f, 1f), 0f), 2.2f, -0.05f, 2500, 1.5f);
    }

    // Спираль искр вдоль луча Рельсы: медленно расходится и гаснет
    public static void RailSpiral(Vector3 a, Vector3 b, Color c)
    {
        ParticleSystem s = Spiral();
        if (s == null || RingTest.NoFx) return;
        Vector3 d = b - a;
        float len = d.magnitude;
        if (len < 0.5f) return;
        Vector3 f = d / len;
        Vector3 side, up;
        Basis(f, out side, out up);
        float step = Mathf.Max(0.55f, len / 120f);
        float r = 0.12f, turns = 1.6f;                                     // оборотов на метр
        for (float x = 0.8f; x < len; x += step)
        {
            float ang = x * turns * Mathf.PI * 2f;
            Vector3 o = side * Mathf.Cos(ang) + up * Mathf.Sin(ang);
            One(s, a + f * x + o * r, o * Random.Range(0.3f, 0.7f), Random.Range(0.05f, 0.08f), Random.Range(0.5f, 0.9f), Color.Lerp(c, Color.white, 0.3f));
        }
    }

    // ---------- Изида ----------
    // Свечение и искры там, где дуга касается цели (каждый кадр, пока луч на цели)
    public static void BeamContact(Vector3 p, Vector3 n, Color c)
    {
        if (RingTest.NoFx) return;
        One(Glow(), p, Vector3.zero, Random.Range(0.5f, 0.8f), 0.06f, Color.Lerp(c, Color.white, 0.3f));
        if (Random.value < 0.6f)
            Emit(Sparks(), 1, p, 0.05f, n, 0.9f, 2f, 6f, 0.02f, 0.04f, 0.15f, 0.35f, Color.Lerp(c, Color.white, 0.5f), c);
    }
}
