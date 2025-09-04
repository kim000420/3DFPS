using UnityEngine;
using System.Collections.Generic;

public enum WallGroupMode { Invulnerable, HealthBased }

public class WallGroupController : MonoBehaviour
{
    [Header("Supports & Walls")]
    public List<DestructibleWall> walls = new List<DestructibleWall>();
    public List<WallSupportPieceGroupController> supportGroups = new List<WallSupportPieceGroupController>();

    [Header("Group Health")]
    public WallGroupMode mode = WallGroupMode.Invulnerable;
    public float maxHealth = 500f;
    [SerializeField] private float currentHealth;
    [SerializeField] private bool collapsed;

    private void Awake()
    {
        currentHealth = Mathf.Max(1f, maxHealth);
        collapsed = false;
    }

    // 크리티컬 타입 지지대 파괴 상태에 따라 파괴 결정
    public void NotifySupportDestroyed()
    {
        // 모든 그룹의 Critical 지지대가 전부 파괴되었는지 확인
        foreach (var group in supportGroups)
        {
            if (group != null && !group.AreAllCriticalDestroyed())
                return; // 아직 파괴되지 않은 그룹이 있음 → 종료
        }

        // 모든 Critical 지지대 파괴됨 → 전체 파괴
        DestroyGroup();
    }
    // 체력 상태에 따라 파괴 결정
    public void ApplyGroupDamage(float amount, Vector3 worldPos)
    {
        if (collapsed || mode != WallGroupMode.HealthBased) return;
        currentHealth -= Mathf.Max(0f, amount);
        if (currentHealth <= 0f)
        {
            collapsed = true;
            DestroyGroup(); // 기존 전체파괴 루틴 재사용
        }
    }
    public float Health01 => Mathf.Clamp01(currentHealth / Mathf.Max(1f, maxHealth));

    // 벽 그룹 전체파괴 로직
    private void DestroyGroup()
    {
        // 1) 대형 연출 알림 (그룹 붕괴)
        var center = transform.position;
        DestructionEventBus.Raise(new DestructionEvent
        {
            wallId = -1,
            worldPos = center,
            worldBoundsAfter = new Bounds(center, Vector3.one * 2f),
            removedArea = 999f,
            isGroupCollapse = true,
            kind = DestructionKind.GroupCollapse
        });

        // 2) 각 벽에 대해: 경계 연결부 제거 → 섬 전환(제거+이벤트)
        foreach (var wall in walls)
        {
            if (!wall) continue;
            wall.RemoveBoundaryTriangles();             // 경계부 제거
            wall.CleanupFloatingIslands(wall.GetThicknessAxis(out _)); // 섬 제거 + 섬 이벤트 (public로 변경됨)
        }

        // 3) 더 이상 남은 메시가 없으면 오브젝트 파괴
        foreach (var wall in walls)
        {
            if (wall && wall.GetComponent<MeshFilter>() && wall.GetComponent<MeshFilter>().mesh &&
                wall.GetComponent<MeshFilter>().mesh.triangles.Length == 0)
            {
                Destroy(wall.gameObject);
            }
        }

        // 지지대 오브젝트는 제거
        foreach (var group in supportGroups)
            if (group) Destroy(group.gameObject);

        Destroy(gameObject);
    }

}
