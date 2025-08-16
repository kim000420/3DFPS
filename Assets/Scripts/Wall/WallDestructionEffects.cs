using UnityEngine;

public class WallDestructionEffects : MonoBehaviour
{
    [SerializeField] ParticleSystem smallHitFx;
    [SerializeField] ParticleSystem bigBreachFx;
    [SerializeField] DebrisPool debrisPool;

    [Header("Rules")]
    [SerializeField] DebrisRuleset ruleset;

    void OnEnable() => DestructionEventBus.OnRaised += OnEvt;
    void OnDisable() => DestructionEventBus.OnRaised -= OnEvt;

    void OnEvt(DestructionEvent e)
    {
        var rule = ruleset ? ruleset.Find(e.kind) : null;

        // 1) FX 선택
        switch (e.kind)
        {
            case DestructionKind.GroupCollapse:
            case DestructionKind.BigBreach:
            case DestructionKind.BigIsland:
                Spawn(bigBreachFx, e.worldPos); // 큰 FX 1회
                break;

            case DestructionKind.SmallHit:
            case DestructionKind.SmallIsland:
                Spawn(smallHitFx, e.worldPos);  // 작은 FX 1회
                break;
        }


        // 2) 파편 스폰
        if (debrisPool && rule != null)
        {
            if (rule.smallCount > 0) debrisPool.SpawnSmall(e.worldPos, rule.smallCount, rule.smallForce);
            if (rule.bigCount > 0) debrisPool.SpawnBig(e.worldPos, rule.bigCount, rule.bigForce);
        }
    }

    void Spawn(ParticleSystem ps, Vector3 p)
    {
        if (!ps) return;
        var inst = Instantiate(ps, p, Quaternion.identity);
        inst.Play(); Destroy(inst.gameObject, 5f);
    }
}
