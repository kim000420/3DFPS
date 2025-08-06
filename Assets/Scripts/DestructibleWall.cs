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

            // �β� ����(Z) �����ϰ� XY �Ÿ��� ���
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

    // IDamageable �ʼ� ����
    public Actor GetActor() => null;
    public float GetHealth() => health;
    public bool IsDead() => health <= 0f;
    public int GetGroupsCount() => 0;
    public Ragdoll GetRagdoll() => null;
}
