using System.Collections.Generic;
using UnityEngine;
using Akila.FPSFramework;

[RequireComponent(typeof(MeshFilter), typeof(MeshCollider))]
public class DestructibleWall : MonoBehaviour, IDamageable
{
    [Header("Destruction Settings")]
    public float baseDestructionRadius = 0.5f; // 기준 반경
    public float health = 100f;

    private MeshFilter meshFilter;
    private MeshCollider meshCollider;
    private Mesh currentMesh; // 수정 가능한 현재 메시

    public float MaxHealth { get; set; }
    public Vector3 deathForce { get; set; }
    public bool deadConfirmed { get; set; }

    private void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshCollider = GetComponent<MeshCollider>();

        // 메시 복사 (원본 건드리지 않기)
        currentMesh = Instantiate(meshFilter.mesh);
        meshFilter.mesh = currentMesh;
        meshCollider.sharedMesh = currentMesh;

        MaxHealth = health;
    }

    /// <summary>
    /// Damage 호출 시 hitPoint와 함께 실행
    /// </summary>
    public void Damage(float amount, Actor damageSource)
    {
        Debug.LogWarning("[DestructibleWall] Damage() 호출됨 - hitPoint 없음");
    }

    /// <summary>
    /// 실제 타격 위치와 함께 데미지 전달
    /// </summary>
    public void DamageAt(Vector3 hitPoint, float amount, Actor damageSource)
    {
        // 체력 차감
        health -= amount;

        // 대미지 크기에 따라 반경 조절
        float radius = baseDestructionRadius * Mathf.Clamp01(amount / MaxHealth);

        Debug.Log($"[DestructibleWall] Hit at {hitPoint}, Damage: {amount}, Radius: {radius}");

        // 절단 실행
        TryClipMeshAt(hitPoint, radius);

        // 완전 파괴 여부 확인
        if (health <= 0f && !deadConfirmed)
        {
            deadConfirmed = true;
            Debug.Log("[DestructibleWall] 완전 파괴됨");
            // 완전 파괴 시 후속 처리 가능
        }
    }
    private void TryClipMeshAt(Vector3 hitWorldPos, float radius)
    {
        Vector3 hitLocalPos = transform.InverseTransformPoint(hitWorldPos);

        Vector3[] vertices = currentMesh.vertices;
        int[] triangles = currentMesh.triangles;

        List<int> newTriangles = new List<int>();
        int removed = 0;

        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 v0 = vertices[triangles[i]];
            Vector3 v1 = vertices[triangles[i + 1]];
            Vector3 v2 = vertices[triangles[i + 2]];

            Vector3 center = (v0 + v1 + v2) / 3f;

            // 두께 방향(Z) 무시하고 XY 거리만 계산
            Vector2 center2D = new Vector2(center.x, center.y);
            Vector2 hit2D = new Vector2(hitLocalPos.x, hitLocalPos.y);

            if (Vector2.Distance(center2D, hit2D) <= radius)
            {
                removed++;
                continue;
            }

            newTriangles.Add(triangles[i]);
            newTriangles.Add(triangles[i + 1]);
            newTriangles.Add(triangles[i + 2]);
        }

        Debug.Log($"[DestructibleWall] Removed triangles: {removed}, Remaining: {newTriangles.Count / 3}");

        currentMesh.triangles = newTriangles.ToArray();
        currentMesh.RecalculateNormals();
        currentMesh.RecalculateBounds();

        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = currentMesh;
    }

    // IDamageable 필수 구현
    public Actor GetActor() => null;
    public float GetHealth() => health;
    public bool IsDead() => health <= 0f;
    public int GetGroupsCount() => 0;
    public Ragdoll GetRagdoll() => null;
}
