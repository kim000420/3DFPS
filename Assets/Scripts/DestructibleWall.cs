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

        // 원본 데이터 복사
        Vector3[] originalVerts = currentMesh.vertices;
        int[] originalTris = currentMesh.triangles;

        List<Vector3> vertsList = new List<Vector3>(originalVerts);
        List<int> keptTris = new List<int>();
        List<int> removedTris = new List<int>();

        // 1. 절단 처리 (XY 평면 기준)
        for (int i = 0; i < originalTris.Length; i += 3)
        {
            Vector3 v0 = originalVerts[originalTris[i]];
            Vector3 v1 = originalVerts[originalTris[i + 1]];
            Vector3 v2 = originalVerts[originalTris[i + 2]];

            Vector2 hit2D = new Vector2(hitLocalPos.x, hitLocalPos.y);
            Vector2 center2D = new Vector2((v0.x + v1.x + v2.x) / 3f, (v0.y + v1.y + v2.y) / 3f);

            if (Vector2.Distance(center2D, hit2D) <= radius)
            {
                removedTris.Add(originalTris[i]);
                removedTris.Add(originalTris[i + 1]);
                removedTris.Add(originalTris[i + 2]);
            }
            else
            {
                keptTris.Add(originalTris[i]);
                keptTris.Add(originalTris[i + 1]);
                keptTris.Add(originalTris[i + 2]);
            }
        }

        // 2. 경계선 추출
        HashSet<(int, int)> edges = new HashSet<(int, int)>();
        AddBoundaryEdges(edges, removedTris, keptTris);

        // 3. 내부 단면 생성 - 앞/뒤 면만
        Vector3 boundsSize = currentMesh.bounds.size;
        float depth = Mathf.Min(boundsSize.x, Mathf.Min(boundsSize.y, boundsSize.z));
        Vector3 depthDir;

        // 두께 방향 결정
        if (depth == boundsSize.x) depthDir = Vector3.right;
        else if (depth == boundsSize.y) depthDir = Vector3.up;
        else depthDir = Vector3.forward;

        HashSet<int> sideVertices = new HashSet<int>();
        
        foreach (var edge in edges)
        {

            // 경계선의 두 점이 이미 단면 버텍스인지 체크
            if (sideVertices.Contains(edge.Item1) || sideVertices.Contains(edge.Item2))
                continue; // 이미 생성된 단면

            Vector3 v1 = vertsList[edge.Item1];
            Vector3 v2 = vertsList[edge.Item2];

            // 경계선이 두께 방향과 평행하면 (즉 옆면이면) 건너뜀
            Vector3 edgeDir = (v2 - v1).normalized;
            if (Vector3.Dot(edgeDir, depthDir) > 0.9f) // 거의 평행
                continue;


            // 뒤쪽 버텍스 추가
            int idxV1Back = vertsList.Count;
            vertsList.Add(v1 + depthDir * depth);

            int idxV2Back = vertsList.Count;
            vertsList.Add(v2 + depthDir * depth);

            // 앞/뒤 연결 삼각형
            keptTris.Add(edge.Item1);
            keptTris.Add(edge.Item2);
            keptTris.Add(idxV1Back);

            keptTris.Add(edge.Item2);
            keptTris.Add(idxV2Back);
            keptTris.Add(idxV1Back);
        }

        // 4. 메시 갱신
        currentMesh.Clear();
        currentMesh.SetVertices(vertsList);
        currentMesh.SetTriangles(keptTris, 0);
        currentMesh.RecalculateNormals();
        currentMesh.RecalculateBounds();

        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = currentMesh;
    }

    private void AddBoundaryEdges(HashSet<(int, int)> edges, List<int> removed, List<int> kept)
    {
        void ProcessEdges(List<int> tris)
        {
            for (int i = 0; i < tris.Count; i += 3)
            {
                AddOrRemoveEdge(edges, tris[i], tris[i + 1]);
                AddOrRemoveEdge(edges, tris[i + 1], tris[i + 2]);
                AddOrRemoveEdge(edges, tris[i + 2], tris[i]);
            }
        }

        ProcessEdges(removed);
        ProcessEdges(kept);
    }

    private void AddOrRemoveEdge(HashSet<(int, int)> edges, int a, int b)
    {
        var edge = a < b ? (a, b) : (b, a);
        if (!edges.Add(edge))
            edges.Remove(edge); // 내부 에지는 제거
    }



    // 에지 저장 유틸
    private void AddEdge(HashSet<(int, int)> edges, int a, int b)
    {
        var edge = a < b ? (a, b) : (b, a);
        if (!edges.Add(edge))
            edges.Remove(edge); // 이미 있으면 제거 → 내부 에지
    }




    // IDamageable 필수 구현
    public Actor GetActor() => null;
    public float GetHealth() => health;
    public bool IsDead() => health <= 0f;
    public int GetGroupsCount() => 0;
    public Ragdoll GetRagdoll() => null;
}
