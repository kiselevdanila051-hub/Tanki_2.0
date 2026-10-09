using UnityEngine;

// Создаёт готовый танк (игрока или бота) со всеми нужными компонентами.
public static class TankFactory
{
    static PhysicsMaterial slippery;

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

    // team: 1 — игрок, 2 — враги. hullMk / gunMk — модификации 0..3 (М0..М3)
    public static GameObject Create(string name, Vector3 pos, bool isPlayer, int team, HullType hull, int hullMk, GunType gun, int gunMk)
    {
        GameObject root = new GameObject(name);
        root.transform.position = pos;
        root.AddComponent<Rigidbody>();

        BoxCollider col = root.AddComponent<BoxCollider>();
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
}
