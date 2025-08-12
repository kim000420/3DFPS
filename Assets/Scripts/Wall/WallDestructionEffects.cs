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
        if (e.kind == DestructionKind.GroupCollapse || e.kind == DestructionKind.BigBreach || e.kind == DestructionKind.BigIsland)
        {
            Spawn(bigBreachFx, e.worldPos);
        }
        else if (e.kind == DestructionKind.SmallHit || e.kind == DestructionKind.SmallIsland)
        {
            // Hit/Breach류는 removedArea 기준으로도 분기 가능 (rule?.fxAreaThreshold 사용)
            bool bigByArea = rule != null && e.removedArea >= rule.fxAreaThreshold;
            Spawn(bigByArea ? bigBreachFx : smallHitFx, e.worldPos);
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
