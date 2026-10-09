using System.Collections.Generic;
using UnityEngine;

// Всплывающие цифры урона над целями. Критический урон — крупнее и особым цветом.
// Мелкий урон подряд (струи, горение) склеивается в одну цифру.
public class DamageNumbers : MonoBehaviour
{
    class Popup
    {
        public Health target;
        public Vector3 pos;
        public float amount;
        public bool crit;
        public float born;
    }

    static DamageNumbers inst;
    readonly List<Popup> list = new List<Popup>();
    GUIStyle style, critStyle, shadow, critShadow;

    public static void Spawn(Health target, Vector3 pos, float amount, bool crit)
    {
        if (inst == null) inst = new GameObject("Damage Numbers").AddComponent<DamageNumbers>();
        inst.Add(target, pos, amount, crit);
    }

    void Awake() { useGUILayout = false; }

    void Add(Health target, Vector3 pos, float amount, bool crit)
    {
        if (!crit)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Popup p = list[i];
                if (p.target == target && !p.crit && Time.time - p.born < 0.35f)
                {
                    p.amount += amount;
                    p.pos = pos;
                    return;
                }
            }
        }
        list.Add(new Popup { target = target, pos = pos, amount = amount, crit = crit, born = Time.time });
    }

    void Update()
    {
        list.RemoveAll(p => Time.time - p.born > 1.2f);
    }

    void OnGUI()
    {
        if (list.Count == 0 || Event.current.type != EventType.Repaint) return;
        Camera cam = Camera.main;
        if (cam == null) return;
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            critStyle = new GUIStyle(style) { fontSize = 28 };
            shadow = new GUIStyle(style);
            critShadow = new GUIStyle(critStyle);
        }

        foreach (Popup p in list)
        {
            float age = Time.time - p.born;
            if (p.amount < 0.5f && !p.crit) continue;
            Vector3 sp = cam.WorldToScreenPoint(p.pos + Vector3.up * age * 1.3f);
            if (sp.z <= 0f) continue;
            float alpha = Mathf.Clamp01(1.4f - age);
            string text = Mathf.RoundToInt(p.amount).ToString() + (p.crit ? "!" : "");
            Rect r = new Rect(sp.x - 60f, Screen.height - sp.y - 18f, 120f, 36f);

            GUIStyle s = p.crit ? critStyle : style;
            GUIStyle sh = p.crit ? critShadow : shadow;
            sh.normal.textColor = new Color(0f, 0f, 0f, alpha * 0.8f);
            GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), text, sh);
            Color c = p.crit ? Fx.CritColor : new Color(1f, 0.95f, 0.6f);
            c.a = alpha;
            s.normal.textColor = c;
            GUI.Label(r, text, s);
        }
    }
}
