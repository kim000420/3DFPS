using UnityEngine;
using System;
// --- 파괴 종류 분류 ---
public enum DestructionKind
{
    SmallHit,   // 작은 파괴 (면적 임계치 미만)
    BigBreach,  // 큰 파괴 (면적 임계치 이상)
    SmallIsland,// 작은 고립파괴(섬)
    BigIsland,  // 큰 고립파괴(섬)
    GroupCollapse // 그룹 붕괴(대형 연출 트리거 용도)
}

public struct DestructionEvent
{
    public int wallId;
    public Vector3 worldPos;
    public Bounds worldBoundsAfter;
    public float removedArea;
    public bool isGroupCollapse;   
    public DestructionKind kind;  
}

public static class DestructionEventBus
{
    public static event Action<DestructionEvent> OnRaised;
    public static void Raise(DestructionEvent e) => OnRaised?.Invoke(e);
}
