using UnityEngine;
using Akila.FPSFramework;

public enum SupportType { Critical, Normal }

public class WallSupportPieceController : MonoBehaviour, IDamageable
{
    public SupportType type = SupportType.Normal;
    public WallSupportPieceGroupController parentGroup;
    public WallGroupController parentWallGroup;

    public float health = 50f;
    public bool isDestroyed { get; private set; }
    public float MaxHealth { get; set; }
    public Vector3 deathForce { get; set; }
    public bool deadConfirmed { get; set; }

    private void Awake()
    {
        MaxHealth = health;
    }

    public void Damage(float amount, Actor source)
    {
        if (isDestroyed) return;

        health -= amount;
        if (health <= 0f)
        {
            isDestroyed = true;
            deadConfirmed = true;

            if (parentWallGroup != null)
                parentWallGroup.NotifySupportDestroyed();

            Destroy(gameObject);
        }
    }

    public Actor GetActor() => null;
    public float GetHealth() => health;
    public bool IsDead() => isDestroyed;
    public int GetGroupsCount() => 0;
    public Ragdoll GetRagdoll() => null;
}
