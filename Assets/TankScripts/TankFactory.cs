using UnityEngine;

// Создаёт готовый танк (игрока, бота, манекена, танк на подиуме гаража) со всеми нужными компонентами. Основа — экземпляр
// префаба PlayerModel (Resources/Prefabs/PlayerModel: NetworkIdentity для Mirror, Rigidbody, коллайдер корпуса; префаб
// создаёт редакторский скрипт PlayerModelPrefab). У каждого танка свой экземпляр, укомплектованный под его корпус и пушку:
// скрипты танка добавляются здесь (их настройки — в коде), модель, коллайдер башни и характеристики ставит TankBuilder.Assemble.
// Dispose разбирает танк и удаляет экземпляр.
public static class TankFactory
{
    public const string PrefabPath = "Prefabs/PlayerModel";   // в Resources

    static PhysicsMaterial slippery;
    static GameObject prefab;
    static bool prefabLoaded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        prefab = null;
        prefabLoaded = false;
    }

    static PhysicsMaterial GetSlippery()
    {
        if (slippery == null)
        {
            // Нулевое трение: скорость задаём сами, трение о стены и землю только мешало бы
            slippery = new PhysicsMaterial("Slippery");
            slippery.dynamicFriction = 0f;
            slippery.staticFriction = 0f;
            slippery.bounciness = 0f;
            slippery.frictionCombine = PhysicsMaterialCombine.Minimum;
        }
        return slippery;
    }

    // Префаб загружается один раз; если его нет (ещё не создан в редакторе), танк собирается на пустом объекте, как раньше
    static GameObject Prefab
    {
        get
        {
            if (!prefabLoaded)
            {
                prefabLoaded = true;
                prefab = Resources.Load<GameObject>(PrefabPath);
                if (prefab == null)
                    Debug.LogWarning("[TankFactory] Нет префаба Resources/" + PrefabPath + " — танки собираются без него (без NetworkIdentity)");
            }
            return prefab;
        }
    }

    // team: 1 — игрок, 2 — враги. hullMk / gunMk — модификации 0..3 (М0..М3)
    public static GameObject Create(string name, Vector3 pos, bool isPlayer, int team, HullType hull, int hullMk, GunType gun, int gunMk)
    {
        GameObject root = Prefab != null ? Object.Instantiate(Prefab, pos, Quaternion.identity) : new GameObject();
        root.name = name;
        root.transform.position = pos;
        if (root.GetComponent<Rigidbody>() == null) root.AddComponent<Rigidbody>();

        // Коробка корпуса с гусеницами (размер задаёт TankBuilder); коробка башни создаётся вместе с башней
        BoxCollider col = root.GetComponent<BoxCollider>();
        if (col == null) col = root.AddComponent<BoxCollider>();
        col.sharedMaterial = GetSlippery();

        root.AddComponent<TankInput>();
        root.AddComponent<StatusEffects>();   // до TankController и TurretController: они читают замедление
        root.AddComponent<TankSuspension>();  // до TankController: он читает состояние земли
        root.AddComponent<TankController>();
        root.AddComponent<TurretController>();
        root.AddComponent<TrackVisuals>();
        root.AddComponent<TankAudio>();

        TankShooter shooter = root.AddComponent<TankShooter>();
        shooter.team = team;

        Health hp = root.AddComponent<Health>();
        hp.team = team;
        hp.tintOnDamage = false;
        hp.destroyOnDeath = false;           // уничтоженный танк остаётся остовом (Wreck)
        hp.showBar = !isPlayer;
        hp.isTank = true;
        root.AddComponent<TankDamage>();     // после Health: подписывается на уничтожение
        root.AddComponent<SelfDestruct>();

        TankBuilder builder = root.AddComponent<TankBuilder>();
        builder.isPlayer = isPlayer;
        builder.paint = isPlayer ? Paints.Selected : 0;   // краска игрока — из гаража, у ботов заводская
        builder.Assemble(hull, hullMk, gun, gunMk);

        if (isPlayer) root.AddComponent<PlayerInput>();
        root.AddComponent<SmokeWake>();      // танк расталкивает и увлекает дым (ни от кого не зависит — последним)
        return root;
    }

    // Разобрать танк и удалить его экземпляр префаба: модель с деталями и коллайдер башни убираются, копии мешей и материалов
    // танка освобождаются сразу, сам объект удаляется в конце кадра. Модели, которые больше никому не нужны, освобождает
    // ModelLibrary.ReleaseUnused (GameManager — после боя)
    public static void Dispose(GameObject tank)
    {
        if (tank == null) return;
        TankBuilder builder = tank.GetComponent<TankBuilder>();
        if (builder != null) builder.Disassemble();
        Object.Destroy(tank);
    }
}
