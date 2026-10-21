using UnityEngine;

// Подвеска на лучах (как у машины): у каждого «катка» есть луч вниз, пружина с демпфером
// держит корпус над землёй. От этого корпус сам наклоняется на склонах, качается на кочках
// и «проседает» под весом. Состояние катков читают гусеницы (TrackVisuals).
// Если у корпуса есть модель (HullRig), положение и размеры катков берутся из неё.
[DefaultExecutionOrder(-50)]   // считаем подвеску раньше, чем ходовую (TankController)
[RequireComponent(typeof(Rigidbody))]
public class TankSuspension : MonoBehaviour
{
    public float maxLength = 0.45f;    // макс. вынос подвески (ход катка), м
    public float sagFraction = 0.3f;   // насколько подвеска сжата под весом танка в покое (доля хода)
    public float damping = 0.7f;       // демпфирование: 0.3 = качается, 1 = вязко
    public float mountY = 0f;          // высота точки крепления подвески в системе корпуса

    public HullRig Rig { get; private set; }
    public float wheelRadius { get; private set; }    // радиус катка (для заглушек)
    public float groundRadius { get; private set; }   // расстояние от центра катка до земли (с лентой)
    public int wheelsPerSide { get; private set; }
    public float trackX { get; private set; }
    public float trackLength { get; private set; }
    public float[] wheelZ { get; private set; }
    public float[] extension { get; private set; }    // текущий вынос подвески каждого катка [сторона * n + номер]
    public Vector3[] contactPoint { get; private set; } // где каток касается земли
    public float[] load { get; private set; }           // сила, с которой каток давит на землю, Н (0 — в воздухе)
    public Collider[] contactCollider { get; private set; }   // на чём стоит каток (следы гусениц — по поверхности)

    // Насколько каток может видимо опуститься ниже положения покоя (задаётся по размеру катка). Пружина
    // работает на весь ход, но на вид каток не «отрывается» от корпуса при разгоне и клевке
    public float maxDroop { get; private set; }

    public float trackSpeedL, trackSpeedR;            // скорость каждой гусеницы вперёд, м/с (задаёт TankController)
    public float GroundedFraction { get; private set; }
    public Vector3 GroundNormal { get; private set; }
    // Танк лёг на бок или на крышу (наклон больше ~70°): подвеска не работает — иначе её лучи, упёршись сбоку в землю
    // или стену, толкали лежащий танк. TankController в это время не двигает его
    public bool Overturned { get; private set; }
    const float OverturnUp = 0.35f;                   // cos ~70°; обратно на гусеницы — с запасом 0,1 (без дребезга)

    public float RestExtension { get { return maxLength * (1f - sagFraction); } }
    // высота центра корпуса над землёй в покое
    public float RestHeight { get { return RestExtension - mountY + groundRadius; } }

    Rigidbody rb;
    readonly RaycastHit[] hitBuffer = new RaycastHit[8];
    float defaultSag = -1f, defaultDamping;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        GroundNormal = Vector3.up;
    }

    // Вызывается сборщиком танка при смене корпуса. rig == null — корпус-заглушка.
    public void Configure(HullStats h, HullRig rig)
    {
        Rig = rig;
        // Жёсткость и демпфирование: свои у корпуса (HullStats), иначе — как настроено на компоненте
        if (defaultSag < 0f) { defaultSag = sagFraction; defaultDamping = damping; }
        sagFraction = h != null && h.suspensionSag > 0f ? h.suspensionSag : defaultSag;
        damping = h != null && h.suspensionDamping > 0f ? h.suspensionDamping : defaultDamping;
        int n;
        if (rig != null)
        {
            n = rig.road.Length;
            wheelsPerSide = n;
            wheelZ = new float[n];
            for (int i = 0; i < n; i++) wheelZ[i] = rig.road[i].z;

            groundRadius = rig.road[0].r + rig.beltThickness * 0.5f;
            wheelRadius = groundRadius;
            trackX = rig.beltX;
            trackLength = rig.hullHalfLength * 2f;
            mountY = rig.road[0].y + RestExtension;     // каток в покое стоит там же, где в модели
        }
        else
        {
            wheelRadius = 0.22f + h.trackWidth * 0.2f;
            groundRadius = wheelRadius;
            trackLength = h.trackLength;
            trackX = h.bodySize.x * 0.5f + h.trackWidth * 0.5f;
            mountY = 0f;

            n = Mathf.Clamp(Mathf.RoundToInt((trackLength - 2f * wheelRadius) / 0.95f) + 1, 3, 8);
            wheelsPerSide = n;
            wheelZ = new float[n];
            float zEnd = trackLength * 0.5f - wheelRadius;
            for (int i = 0; i < n; i++)
                wheelZ[i] = Mathf.Lerp(-zEnd, zEnd, i / (float)(n - 1));
        }

        maxDroop = Mathf.Clamp(groundRadius * 0.35f, 0.03f, 0.08f);
        extension = new float[2 * n];
        contactPoint = new Vector3[2 * n];
        load = new float[2 * n];
        contactCollider = new Collider[2 * n];
        for (int i = 0; i < extension.Length; i++) extension[i] = RestExtension;
    }

    void FixedUpdate()
    {
        if (wheelZ == null) return;

        int n = wheelsPerSide;
        int total = 2 * n;
        float weightShare = rb.mass * Physics.gravity.magnitude / total;
        float k = weightShare / (sagFraction * maxLength);                    // жёсткость пружины
        float c = 2f * damping * Mathf.Sqrt(k * (rb.mass / total));           // демпфер
        float maxForce = weightShare * 4f;                                    // защита от резких скачков
        Vector3 up = transform.up;
        float rayLength = maxLength + groundRadius;

        Overturned = up.y < (Overturned ? OverturnUp + 0.1f : OverturnUp);
        if (Overturned)
        {
            for (int i = 0; i < total; i++) { extension[i] = RestExtension + maxDroop; load[i] = 0f; }
            GroundedFraction = 0f;
            GroundNormal = Vector3.Slerp(GroundNormal, Vector3.up, 0.05f);
            return;
        }

        int grounded = 0;
        Vector3 normalSum = Vector3.zero;

        for (int side = 0; side < 2; side++)
        {
            float x = side == 0 ? -trackX : trackX;
            for (int i = 0; i < n; i++)
            {
                int idx = side * n + i;
                Vector3 origin = transform.TransformPoint(new Vector3(x, mountY, wheelZ[i]));

                // Луч начинается внутри коллайдера самого танка, поэтому «своё» из попаданий выбрасываем
                int count = Physics.RaycastNonAlloc(origin, -up, hitBuffer, rayLength, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                float best = float.MaxValue;
                Vector3 bestNormal = up;
                Vector3 bestPoint = origin;
                Collider bestCol = null;
                for (int h = 0; h < count; h++)
                {
                    RaycastHit hit = hitBuffer[h];
                    if (hit.rigidbody == rb) continue;
                    if (hit.distance < best) { best = hit.distance; bestNormal = hit.normal; bestPoint = hit.point; bestCol = hit.collider; }
                }
                contactCollider[idx] = bestCol;

                if (best < float.MaxValue)
                {
                    float ext = Mathf.Clamp(best - groundRadius, 0f, maxLength);
                    extension[idx] = Mathf.Min(ext, RestExtension + maxDroop);
                    float compression = maxLength - ext;
                    float vUp = Vector3.Dot(rb.GetPointVelocity(origin), up);
                    float force = Mathf.Clamp(k * compression - c * vUp, 0f, maxForce);
                    rb.AddForceAtPosition(up * force, origin);
                    contactPoint[idx] = bestPoint;
                    load[idx] = force;
                    grounded++;
                    normalSum += bestNormal;
                }
                else
                {
                    extension[idx] = RestExtension + maxDroop; // каток в воздухе — опущен, но держится у корпуса
                    load[idx] = 0f;
                }
            }
        }

        GroundedFraction = grounded / (float)total;
        if (grounded > 0) GroundNormal = Vector3.Slerp(GroundNormal, normalSum.normalized, 0.25f);
        else GroundNormal = Vector3.Slerp(GroundNormal, Vector3.up, 0.05f);
    }

    // Выберите танк в сцене во время игры, чтобы увидеть лучи подвески
    void OnDrawGizmosSelected()
    {
        if (wheelZ == null || !Application.isPlaying) return;
        for (int side = 0; side < 2; side++)
        {
            float x = side == 0 ? -trackX : trackX;
            for (int i = 0; i < wheelsPerSide; i++)
            {
                Vector3 o = transform.TransformPoint(new Vector3(x, mountY, wheelZ[i]));
                Gizmos.color = load[side * wheelsPerSide + i] > 0f ? Color.green : Color.red;
                Gizmos.DrawLine(o, o - transform.up * (maxLength + groundRadius));
            }
        }
    }
}
