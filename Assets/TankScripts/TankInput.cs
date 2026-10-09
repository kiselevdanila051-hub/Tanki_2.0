using UnityEngine;

// «Пульт управления» танка. Его заполняет либо PlayerInput (игрок), либо BotAI (бот).
public class TankInput : MonoBehaviour
{
    public float Throttle;   // -1 назад ... 1 вперёд
    public float Steer;      // -1 влево ... 1 вправо
    public float AimYaw;     // куда должна смотреть башня (угол в мире, градусы)
    public bool Fire;        // стрелять
    public bool Scoping;     // включён снайперский прицел (его выставляет пушка Шафт)
    public float AimPitch;   // наклон прицела Шафта вверх/вниз (только в оптике), градусы
}
