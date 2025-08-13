using UnityEngine;

public enum ImpactKind { Pistol, Rifle, Sniper, Shotgun, Explosive }

public class WeaponImpactTag : MonoBehaviour
{
    public ImpactKind kind = ImpactKind.Rifle;

    [Header("Wall destruction tuning")]
    [Tooltip("반경 보정. 1=기본, 샷건 0.6~0.8, 스나 1.3~1.6 등")]
    public float radiusMultiplier = 1.0f;
}