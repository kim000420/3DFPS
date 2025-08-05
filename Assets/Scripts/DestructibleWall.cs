using System.Collections.Generic;
using UnityEngine;
using Akila.FPSFramework;

[RequireComponent(typeof(MeshFilter), typeof(MeshCollider))]
public class DestructibleWall : MonoBehaviour, IDamageable
{
    [Header("Destruction Settings")]
    public float destructionRadius = 0.5f;

    private MeshFilter meshFilter;
    private MeshCollider meshCollider;
    private Mesh originalMesh;

    public float health = 100f;
    public float MaxHealth { get; set; }
    public Vector3 deathForce { get; set; }
    public bool deadConfirmed { get; set; }

    private void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshCollider = GetComponent<MeshCollider>();
        originalMesh = meshFilter.mesh;
        MaxHealth = health;
    }

    public void Damage(float amount, Actor damageSource)
    {
        // 데미지를 감소시키고 죽었으면 절단
        health -= amount;
        if (health <= 0f)
        {
            Vector3 hitPoint = transform.position + Vector3.forward * 0.1f; // 임시 hit 위치
            TryClipMeshAt(hitPoint, destructionRadius);
        }
    }

    private void TryClipMeshAt(Vector3 hitWorldPos, float radius)
    {
        Vector3 hitLocalPos = transform.InverseTransformPoint(hitWorldPos);

        Mesh mesh = Instantiate(originalMesh); // 복사해서 조작
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;

        List<int> newTriangles = new List<int>();

        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 v0 = vertices[triangles[i]];
            Vector3 v1 = vertices[triangles[i + 1]];
            Vector3 v2 = vertices[triangles[i + 2]];

            Vector3 center = (v0 + v1 + v2) / 3f;

            // 중심점이 반경 밖이면 삼각형 유지
            if ((center - hitLocalPos).magnitude > radius)
            {
                newTriangles.Add(triangles[i]);
                newTriangles.Add(triangles[i + 1]);
                newTriangles.Add(triangles[i + 2]);
            }
        }

        mesh.triangles = newTriangles.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        meshFilter.mesh = mesh;
        meshCollider.sharedMesh = mesh;
    }

    public Transform transform => base.transform;
    public Actor GetActor() => null;
    public float GetHealth() => health;
    public bool IsDead() => health <= 0f;
    public int GetGroupsCount() => 0;
    public Ragdoll GetRagdoll() => null;
}
