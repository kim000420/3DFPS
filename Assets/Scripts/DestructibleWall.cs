using System.Collections.Generic;
using UnityEngine;
using Akila.FPSFramework;

[RequireComponent(typeof(MeshFilter), typeof(MeshCollider))]
public class DestructibleWall : MonoBehaviour, IDamageable
{
    [Header("Destruction Settings")]
    public float baseDestructionRadius = 0.5f; // ���� �ݰ�
    public float health = 100f;

    private MeshFilter meshFilter;
    private MeshCollider meshCollider;
    private Mesh currentMesh; // ���� ������ ���� �޽�

    public float MaxHealth { get; set; }
    public Vector3 deathForce { get; set; }
    public bool deadConfirmed { get; set; }

    private void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshCollider = GetComponent<MeshCollider>();

        // �޽� ���� (���� �ǵ帮�� �ʱ�)
        currentMesh = Instantiate(meshFilter.mesh);
        meshFilter.mesh = currentMesh;
        meshCollider.sharedMesh = currentMesh;

        MaxHealth = health;
    }

    /// <summary>
    /// Damage ȣ�� �� hitPoint�� �Բ� ����
    /// </summary>
    public void Damage(float amount, Actor damageSource)
    {
        Debug.LogWarning("[DestructibleWall] Damage() ȣ��� - hitPoint ����");
    }

    /// <summary>
    /// ���� Ÿ�� ��ġ�� �Բ� ������ ����
    /// </summary>
    public void DamageAt(Vector3 hitPoint, float amount, Actor damageSource)
    {
        // ü�� ����
        health -= amount;

        // ����� ũ�⿡ ���� �ݰ� ����
        float radius = baseDestructionRadius * Mathf.Clamp01(amount / MaxHealth);

        Debug.Log($"[DestructibleWall] Hit at {hitPoint}, Damage: {amount}, Radius: {radius}");

        // ���� ����
        TryClipMeshAt(hitPoint, radius);

        // ���� �ı� ���� Ȯ��
        if (health <= 0f && !deadConfirmed)
        {
            deadConfirmed = true;
            Debug.Log("[DestructibleWall] ���� �ı���");
            // ���� �ı� �� �ļ� ó�� ����
        }
    }
    private void TryClipMeshAt(Vector3 hitWorldPos, float radius)
    {
        Vector3 hitLocalPos = transform.InverseTransformPoint(hitWorldPos);

        // ���� ������ ����
        Vector3[] originalVerts = currentMesh.vertices;
        int[] originalTris = currentMesh.triangles;

        List<Vector3> vertsList = new List<Vector3>(originalVerts);
        List<int> keptTris = new List<int>();
        List<int> removedTris = new List<int>();

        // 1. ���� ó�� (XY ��� ����)
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

        // 2. ��輱 ����
        HashSet<(int, int)> edges = new HashSet<(int, int)>();
        AddBoundaryEdges(edges, removedTris, keptTris);

        // 3. ���� �ܸ� ���� - ��/�� �鸸
        Vector3 boundsSize = currentMesh.bounds.size;
        float depth = Mathf.Min(boundsSize.x, Mathf.Min(boundsSize.y, boundsSize.z));
        Vector3 depthDir;

        // �β� ���� ����
        if (depth == boundsSize.x) depthDir = Vector3.right;
        else if (depth == boundsSize.y) depthDir = Vector3.up;
        else depthDir = Vector3.forward;

        HashSet<int> sideVertices = new HashSet<int>();
        
        foreach (var edge in edges)
        {

            // ��輱�� �� ���� �̹� �ܸ� ���ؽ����� üũ
            if (sideVertices.Contains(edge.Item1) || sideVertices.Contains(edge.Item2))
                continue; // �̹� ������ �ܸ�

            Vector3 v1 = vertsList[edge.Item1];
            Vector3 v2 = vertsList[edge.Item2];

            // ��輱�� �β� ����� �����ϸ� (�� �����̸�) �ǳʶ�
            Vector3 edgeDir = (v2 - v1).normalized;
            if (Vector3.Dot(edgeDir, depthDir) > 0.9f) // ���� ����
                continue;


            // ���� ���ؽ� �߰�
            int idxV1Back = vertsList.Count;
            vertsList.Add(v1 + depthDir * depth);

            int idxV2Back = vertsList.Count;
            vertsList.Add(v2 + depthDir * depth);

            // ��/�� ���� �ﰢ��
            keptTris.Add(edge.Item1);
            keptTris.Add(edge.Item2);
            keptTris.Add(idxV1Back);

            keptTris.Add(edge.Item2);
            keptTris.Add(idxV2Back);
            keptTris.Add(idxV1Back);
        }

        // 4. �޽� ����
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
            edges.Remove(edge); // ���� ������ ����
    }



    // ���� ���� ��ƿ
    private void AddEdge(HashSet<(int, int)> edges, int a, int b)
    {
        var edge = a < b ? (a, b) : (b, a);
        if (!edges.Add(edge))
            edges.Remove(edge); // �̹� ������ ���� �� ���� ����
    }




    // IDamageable �ʼ� ����
    public Actor GetActor() => null;
    public float GetHealth() => health;
    public bool IsDead() => health <= 0f;
    public int GetGroupsCount() => 0;
    public Ragdoll GetRagdoll() => null;
}
