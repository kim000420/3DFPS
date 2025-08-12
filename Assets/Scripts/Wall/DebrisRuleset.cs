using UnityEngine;

[System.Serializable]
public class DebrisRule
{
    public DestructionKind kind;  // SmallHit, BigBreach, SmallIsland, BigIsland, GroupCollapse
    public int smallCount = 4;
    public int bigCount = 0;
    public float smallForce = 2.5f;
    public float bigForce = 4.5f;
    public float fxAreaThreshold = 0.3f; // Hit/Breach에서 작은/큰 FX 분기용(옵션)
}

public class DebrisRuleset : MonoBehaviour
{
    public DebrisRule[] rules;

    public DebrisRule Find(DestructionKind kind)
    {
        if (rules == null) return null;
        for (int i = 0; i < rules.Length; i++)
            if (rules[i] != null && rules[i].kind == kind) return rules[i];
        return null;
    }
}
