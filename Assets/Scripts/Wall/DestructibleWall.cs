using System.Collections.Generic;
using UnityEngine;
using Akila.FPSFramework;

[RequireComponent(typeof(MeshFilter), typeof(MeshCollider))]
public class DestructibleWall : MonoBehaviour, IDamageable
{
    [Header("Thresholds (Area in 2D projection)")]
    [SerializeField] private float bigBreachThreshold = 0.25f;// 작은/큰 파괴 구분
    [SerializeField] private float bigIslandThreshold = 0.25f;    // 작은/큰 '고립 섬' 구분

    // 외곽/섬 판정 튜닝값
    [SerializeField, Range(0.9f, 1f), Tooltip("외곽 접속성 판정 여유(1에 가까울수록 엄격)")]
    private float edgeContactFactor = 0.999f;

    [SerializeField, Min(0f), Tooltip("섬 면적 계산 시 무시할 최소 2D 면적")]
    private float islandAreaEpsilon = 1e-6f;



    private MeshFilter meshFilter;
    private MeshCollider meshCollider;
    private Mesh currentMesh; // 수정 가능한 현재 메시
    private Renderer _renderer;

    public float MaxHealth { get; set; }
    public Vector3 deathForce { get; set; }
    public bool deadConfirmed { get; set; }

    [SerializeField] private float patternQuantize = 1e-3f; // XY 패턴 라운딩 단위(메시 크기에 맞게 튜닝)
    [SerializeField] private float radiusEpsilon = 1e-4f;   // 반경 여유(부동소수 오차 보정)
    [SerializeField] private WallGroupController parentGroup;


    // 메쉬/콜라이더 안전 적용: 삼각형 < 1이면 콜라이더 비활성
    private void ApplyMeshAndColliderSafe(int[] tris)
    {
        currentMesh.triangles = tris;
        currentMesh.RecalculateNormals();
        currentMesh.RecalculateBounds();

        if (tris != null && tris.Length >= 3)
        {
            if (!meshCollider.enabled) meshCollider.enabled = true;
            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = currentMesh; // OK: 유효한(>=1 tri) 메쉬만 바인딩
        }
        else
        {
            meshCollider.sharedMesh = null;
            if (meshCollider.enabled) meshCollider.enabled = false;  // 충돌체 완전히 비활성
        }
    }

    // 공통 이벤트 발행 유틸
    private void RaiseDestructionEvent(DestructionKind kind, Vector3 worldPos, float removedArea)
    {
        var bounds = _renderer ? _renderer.bounds : new Bounds(transform.position, Vector3.one);
        DestructionEventBus.Raise(new DestructionEvent
        {
            wallId = GetInstanceID(),
            worldPos = worldPos,
            worldBoundsAfter = bounds,
            removedArea = removedArea,
            isGroupCollapse = false,
            kind = kind
        });
    }


    private void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshCollider = GetComponent<MeshCollider>();
        _renderer = GetComponent<Renderer>();

        // 메시 복사 (원본 건드리지 않기)
        currentMesh = Instantiate(meshFilter.mesh);
        meshFilter.mesh = currentMesh;
        meshCollider.sharedMesh = currentMesh;

        if (!parentGroup) parentGroup = GetComponentInParent<WallGroupController>();
    }

    private (int[] keptTris, float removedArea2D, int thicknessAxis)
    ClipAtCore(Vector3 hitWorldPos, float radius)
    {
        // 0) 좌표/축 준비
        Vector3 hitLocalPos = transform.InverseTransformPoint(hitWorldPos);
        int thicknessAxis = GetThicknessAxis(out Vector3 thicknessDir);
        Vector2 hit2 = ProjectTo2D(hitLocalPos, thicknessAxis);

        var v = currentMesh.vertices;
        var t = currentMesh.triangles;
        int triCount = t.Length / 3;

        // 1) 앞면 패턴 수집
        HashSet<long> frontPattern = new HashSet<long>();
        for (int tri = 0; tri < triCount; tri++)
        {
            int ia = t[tri * 3 + 0], ib = t[tri * 3 + 1], ic = t[tri * 3 + 2];
            Vector3 v0 = v[ia], v1 = v[ib], v2 = v[ic];
            Vector3 n = Vector3.Cross(v1 - v0, v2 - v0).normalized;
            bool isFront = Vector3.Dot(n, thicknessDir) > 0f;
            Vector3 c3 = (v0 + v1 + v2) / 3f;
            Vector2 c2 = ProjectTo2D(c3, thicknessAxis);

            if (isFront && Vector2.Distance(c2, hit2) <= radius + radiusEpsilon)
                frontPattern.Add(Key2D(c2));
        }

        // 2) 제거/보존 분기 + 면적 누적
        List<int> kept = new List<int>(t.Length);
        float removedArea2D = 0f;

        for (int tri = 0; tri < triCount; tri++)
        {
            int ia = t[tri * 3 + 0], ib = t[tri * 3 + 1], ic = t[tri * 3 + 2];
            Vector3 v0 = v[ia], v1 = v[ib], v2 = v[ic];
            Vector3 c3 = (v0 + v1 + v2) / 3f;
            Vector2 c2 = ProjectTo2D(c3, thicknessAxis);

            bool inRadius = Vector2.Distance(c2, hit2) <= radius + radiusEpsilon;
            bool matchFrontKey = frontPattern.Contains(Key2D(c2));

            if (matchFrontKey || inRadius)
            {
                // 2D 삼각형 면적 누적
                Vector2 v0_2D = ProjectTo2D(v0, thicknessAxis);
                Vector2 v1_2D = ProjectTo2D(v1, thicknessAxis);
                Vector2 v2_2D = ProjectTo2D(v2, thicknessAxis);
                float triArea = Mathf.Abs((v0_2D.x * (v1_2D.y - v2_2D.y) + v1_2D.x * (v2_2D.y - v0_2D.y) + v2_2D.x * (v0_2D.y - v1_2D.y))) * 0.5f;
                removedArea2D += triArea;
                continue;
            }

            kept.Add(ia); kept.Add(ib); kept.Add(ic);
        }

        return (kept.ToArray(), removedArea2D, thicknessAxis);
    }

    private float TryClipMeshAt_ReturnArea(Vector3 hitWorldPos, float radius)
    {
        // 코어 호출
        var (kept, removedArea2D, thicknessAxis) = ClipAtCore(hitWorldPos, radius);

        // 메시/콜라이더 반영
        ApplyMeshAndColliderSafe(kept);

        // 고립 섬 정리
        RemoveFloatingIslands2D(thicknessAxis);

        return removedArea2D;
    }

    // 절단 수행 및 잘려나간 2D면적을 반환
    public void RemoveFloatingIslands2D(int thicknessAxis)
    {
        var verts = currentMesh.vertices;
        var tris = currentMesh.triangles;
        int triCount = tris.Length / 3;
        if (triCount == 0) return;

        // --- 인접 리스트 (공유 '변' 기준) ---
        List<int>[] adj = new List<int>[triCount];
        for (int i = 0; i < triCount; i++) adj[i] = new List<int>();

        var trisArr = currentMesh.triangles;
        // 에지 키: (minIdx<<32) ^ maxIdx
        Dictionary<long, List<int>> edgeToTris = new Dictionary<long, List<int>>();

        long EdgeKey(int a, int b)
        {
            if (a > b) { int tmp = a; a = b; b = tmp; }
            return ((long)a << 32) ^ (long)(uint)b;
        }

        for (int i = 0; i < triCount; i++)
        {
            int ia = trisArr[i * 3 + 0], ib = trisArr[i * 3 + 1], ic = trisArr[i * 3 + 2];
            long e0 = EdgeKey(ia, ib);
            long e1 = EdgeKey(ib, ic);
            long e2 = EdgeKey(ic, ia);

            if (!edgeToTris.TryGetValue(e0, out var L0)) edgeToTris[e0] = L0 = new List<int>();
            if (!edgeToTris.TryGetValue(e1, out var L1)) edgeToTris[e1] = L1 = new List<int>();
            if (!edgeToTris.TryGetValue(e2, out var L2)) edgeToTris[e2] = L2 = new List<int>();
            L0.Add(i); L1.Add(i); L2.Add(i);
        }

        // 같은 에지를 공유하는 삼각형끼리만 연결(두 점 공유)
        foreach (var kv in edgeToTris)
        {
            var L = kv.Value;
            for (int i = 0; i < L.Count; i++)
                for (int j = i + 1; j < L.Count; j++)
                {
                    int t0 = L[i], t1 = L[j];
                    adj[t0].Add(t1); adj[t1].Add(t0);
                }
        }

        // 외곽 접속성 판정: 2D 바운즈 변두리와 맞닿은 삼각형에서 BFS
        currentMesh.RecalculateBounds();
        var bounds = currentMesh.bounds;

        // 두께축 제외한 투영 바운즈 extents
        float ex, ey, cx, cy;
        if (thicknessAxis == 0)
        {
            ex = bounds.extents.y; ey = bounds.extents.z;
            cx = bounds.center.y; cy = bounds.center.z;
        }
        else if (thicknessAxis == 1)
        {
            ex = bounds.extents.x; ey = bounds.extents.z;
            cx = bounds.center.x; cy = bounds.center.z;
        }
        else
        {
            ex = bounds.extents.x; ey = bounds.extents.y;
            cx = bounds.center.x; cy = bounds.center.y;
        }

        float mx = ex * edgeContactFactor, my = ey * edgeContactFactor;
        bool[] visited = new bool[triCount];
        System.Collections.Generic.Queue<int> q = new System.Collections.Generic.Queue<int>();

        for (int i = 0; i < triCount; i++)
        {
            int ia = tris[i * 3 + 0], ib = tris[i * 3 + 1], ic = tris[i * 3 + 2];
            Vector2 a2 = ProjectTo2D(verts[ia], thicknessAxis);
            Vector2 b2 = ProjectTo2D(verts[ib], thicknessAxis);
            Vector2 c2 = ProjectTo2D(verts[ic], thicknessAxis);

            bool nearEdge =
                Mathf.Abs(a2.x - cx) >= mx || Mathf.Abs(a2.y - cy) >= my ||
                Mathf.Abs(b2.x - cx) >= mx || Mathf.Abs(b2.y - cy) >= my ||
                Mathf.Abs(c2.x - cx) >= mx || Mathf.Abs(c2.y - cy) >= my;

            if (nearEdge && !visited[i])
            {
                visited[i] = true; q.Enqueue(i);
                while (q.Count > 0)
                {
                    int cur = q.Dequeue();
                    foreach (var nx in adj[cur])
                        if (!visited[nx]) { visited[nx] = true; q.Enqueue(nx); }
                }
            }
        }

        // 미방문(외곽과 연결 안된 섬) 제거
        // --- 미방문(외곽과 연결 안된 섬) 제거 + 섬별 이벤트 ---
        List<int> kept = new List<int>(tris.Length);
        int removedIslands = 0;

        for (int i = 0; i < triCount; i++)
        {
            if (visited[i])
            {
                kept.Add(tris[i * 3 + 0]);
                kept.Add(tris[i * 3 + 1]);
                kept.Add(tris[i * 3 + 2]);
            }
        }

        // 섬 별로 다시 스캔: 면적/중심 계산 위해
        bool[] islandMarked = new bool[triCount];
        for (int i = 0; i < triCount; i++)
        {
            if (visited[i] || islandMarked[i]) continue;

            // i가 속한 섬의 삼각형 수집
            List<int> island = new List<int>();
            Queue<int> q2 = new Queue<int>();
            islandMarked[i] = true; q2.Enqueue(i);

            while (q2.Count > 0)
            {
                int cur = q2.Dequeue();
                island.Add(cur);
                foreach (var nx in adj[cur])
                    if (!visited[nx] && !islandMarked[nx]) { islandMarked[nx] = true; q2.Enqueue(nx); }
            }

            // 면적/무게중심(2D) 계산
            float islandArea = 0f;
            Vector2 centroidSum = Vector2.zero;
            foreach (var triIdx in island)
            {
                int ia = tris[triIdx * 3 + 0], ib = tris[triIdx * 3 + 1], ic = tris[triIdx * 3 + 2];
                Vector2 a2 = ProjectTo2D(verts[ia], thicknessAxis);
                Vector2 b2 = ProjectTo2D(verts[ib], thicknessAxis);
                Vector2 c2 = ProjectTo2D(verts[ic], thicknessAxis);
                float triArea = Mathf.Abs((a2.x * (b2.y - c2.y) + b2.x * (c2.y - a2.y) + c2.x * (a2.y - b2.y))) * 0.5f;
                islandArea += triArea;
                Vector2 triCentroid = (a2 + b2 + c2) / 3f;
                centroidSum += triCentroid * triArea;
            }
            Vector2 islandCentroid2D = islandArea > islandAreaEpsilon ? centroidSum / islandArea : Vector2.zero;

            // 2D→3D 변환(두께 중앙면으로 맵)
            Vector3 centroid3D;
            switch (thicknessAxis)
            {
                case 0: centroid3D = new Vector3(currentMesh.bounds.center.x, islandCentroid2D.x, islandCentroid2D.y); break;
                case 1: centroid3D = new Vector3(islandCentroid2D.x, currentMesh.bounds.center.y, islandCentroid2D.y); break;
                default: centroid3D = new Vector3(islandCentroid2D.x, islandCentroid2D.y, currentMesh.bounds.center.z); break;
            }
            Vector3 worldPos = transform.TransformPoint(centroid3D);

            // 섬 제거 이벤트 발행
            var kind = (islandArea >= bigIslandThreshold) ? DestructionKind.BigIsland : DestructionKind.SmallIsland; // 임계치 동일
            RaiseDestructionEvent(kind, worldPos, islandArea);


            removedIslands += island.Count;
        }

        // 변경된 삼각형 적용
        ApplyMeshAndColliderSafe(kept.ToArray());
    }

    public void DamageAtWithContext(Vector3 hitPoint, float amount, Akila.FPSFramework.Actor damageSource,
                                float radiusMul, float? bigThresholdOverride = null)
    {
        Debug.LogWarning("[DestructibleWall] DamageAtWithContext is obsolete. Shooter must call ApplyDestructionRadius.");
    }

    /// <summary>
    /// 발사 측에서 ‘절대 반지름’을 결정해 넘기는, 절단 전용 엔트리
    /// 벽은 순수하게 메시를 자르고, 결과 면적을 반환하며, 이벤트만 발행
    /// </summary>
    public float ApplyDestructionRadius(Vector3 hitPoint, float worldRadius, float? bigThresholdOverride = null)
    {
        // 1) 메시 절단 (면적 계산 포함)
        float removedArea = TryClipMeshAt_ReturnArea(hitPoint, worldRadius);

        // 2) 파괴 규모 분류(임계치 외부에서 덮어쓰기 가능)
        float th = bigThresholdOverride ?? bigBreachThreshold;
        var kind = (removedArea >= th) ? DestructionKind.BigBreach : DestructionKind.SmallHit;

        // 3) 결과 이벤트만 브로드캐스트 (체력/데미지 관여 X)
        RaiseDestructionEvent(kind, hitPoint, removedArea);


        return removedArea;
    }


    public int GetThicknessAxis(out Vector3 axisDir)
    {
        currentMesh.RecalculateBounds();
        var e = currentMesh.bounds.extents;
        // 가장 작은 extents가 "두께"라고 가정
        if (e.x <= e.y && e.x <= e.z) { axisDir = Vector3.right; return 0; } // X가 두께
        if (e.y <= e.x && e.y <= e.z) { axisDir = Vector3.up; return 1; } // Y가 두께
        axisDir = Vector3.forward; return 2; // Z가 두께
    }

    // 유틸: 두께축을 제외한 2개 축으로 2D 투영 좌표를 만든다
    private Vector2 ProjectTo2D(Vector3 p, int thicknessAxis)
    {
        switch (thicknessAxis)
        {
            case 0: // X가 두께 => (Y,Z) 투영
                return new Vector2(p.y, p.z);
            case 1: // Y가 두께 => (X,Z) 투영
                return new Vector2(p.x, p.z);
            default: // Z가 두께 => (X,Y) 투영
                return new Vector2(p.x, p.y);
        }
    }

    // 유틸: 2D 좌표 라운딩 키(앞/뒤 삼각형 매칭용)
    private long Key2D(Vector2 p)
    {
        long kx = Mathf.RoundToInt(p.x / patternQuantize);
        long ky = Mathf.RoundToInt(p.y / patternQuantize);
        return (kx << 32) ^ (ky & 0xffffffff);
    }

    // 유틸
    public Vector3 GetClosestPointOnSurface(Vector3 worldPos)
    {
        // 로컬 변환
        Vector3 local = transform.InverseTransformPoint(worldPos);

        // 두께축과 투영 축 판정
        int axis = GetThicknessAxis(out _);
        currentMesh.RecalculateBounds();
        var b = currentMesh.bounds;

        // 중심/절반폭, 투영축별로 클램프
        float cx, cy, cz, ex, ey, ez;
        cx = b.center.x; cy = b.center.y; cz = b.center.z;
        ex = b.extents.x; ey = b.extents.y; ez = b.extents.z;

        Vector3 clamped = local;
        switch (axis)
        {
            case 0: // X가 두께 → (Y,Z)만 클램프, X는 중앙면
                clamped.y = Mathf.Clamp(local.y, cy - ey, cy + ey);
                clamped.z = Mathf.Clamp(local.z, cz - ez, cz + ez);
                clamped.x = cx;
                break;
            case 1: // Y가 두께
                clamped.x = Mathf.Clamp(local.x, cx - ex, cx + ex);
                clamped.z = Mathf.Clamp(local.z, cz - ez, cz + ez);
                clamped.y = cy;
                break;
            default: // Z가 두께
                clamped.x = Mathf.Clamp(local.x, cx - ex, cx + ex);
                clamped.y = Mathf.Clamp(local.y, cy - ey, cy + ey);
                clamped.z = cz;
                break;
        }
        return transform.TransformPoint(clamped);
    }

    // 외곽과 접한 삼각형들만 제거하는 유틸
    public void DestroyBoundaryConnectedTriangles()
    {
        // 준비
        int thicknessAxis = GetThicknessAxis(out _);
        var verts = currentMesh.vertices;
        var tris = currentMesh.triangles;
        int triCount = tris.Length / 3;
        if (triCount == 0) return;

        currentMesh.RecalculateBounds();
        var bounds = currentMesh.bounds;

        // 두께축 제외한 투영 바운즈 파라미터 (RemoveFloatingIslands2D와 동일 판정 재사용)
        float ex, ey, cx, cy;
        if (thicknessAxis == 0) { ex = bounds.extents.y; ey = bounds.extents.z; cx = bounds.center.y; cy = bounds.center.z; }
        else if (thicknessAxis == 1) { ex = bounds.extents.x; ey = bounds.extents.z; cx = bounds.center.x; cy = bounds.center.z; }
        else { ex = bounds.extents.x; ey = bounds.extents.y; cx = bounds.center.x; cy = bounds.center.y; }
        float mx = ex * 0.999f, my = ey * 0.999f;

        List<int> kept = new List<int>(tris.Length);
        float removedArea2D = 0f;
        int removed = 0;

        for (int i = 0; i < triCount; i++)
        {
            int ia = tris[i * 3 + 0], ib = tris[i * 3 + 1], ic = tris[i * 3 + 2];

            Vector2 a2 = ProjectTo2D(verts[ia], thicknessAxis);
            Vector2 b2 = ProjectTo2D(verts[ib], thicknessAxis);
            Vector2 c2 = ProjectTo2D(verts[ic], thicknessAxis);

            bool nearEdge =
                Mathf.Abs(a2.x - cx) >= mx || Mathf.Abs(a2.y - cy) >= my ||
                Mathf.Abs(b2.x - cx) >= mx || Mathf.Abs(b2.y - cy) >= my ||
                Mathf.Abs(c2.x - cx) >= mx || Mathf.Abs(c2.y - cy) >= my;

            if (nearEdge)
            {
                // 2D 삼각형 면적 누적
                float triArea = Mathf.Abs((a2.x * (b2.y - c2.y) + b2.x * (c2.y - a2.y) + c2.x * (a2.y - b2.y))) * 0.5f;
                removedArea2D += triArea;
                removed++;
                continue;
            }

            kept.Add(ia); kept.Add(ib); kept.Add(ic);
        }

        if (removed > 0)
        {
            ApplyMeshAndColliderSafe(kept.ToArray());


            // 경계제거 자체도 작은/큰 파괴로 간주해 이벤트 발행 (보통은 큰 연출과 함께)
            RaiseDestructionEvent(
                removedArea2D >= bigBreachThreshold ? DestructionKind.BigBreach : DestructionKind.SmallHit,
                transform.TransformPoint(currentMesh.bounds.center),
                removedArea2D
            );
        }
    }

    // 그룹 데미지 통보용 래퍼
    public void NotifyGroupDamage(float amount, Vector3 hitPoint)
    {
        if (parentGroup) parentGroup.ApplyGroupDamage(amount, hitPoint);
    }

    // IDamageable 필수 구현
    public void Damage(float amount, Actor damageSource)
    {
        Debug.LogWarning("[DestructibleWall] Damage() 호출됨 - hitPoint 없음");
    }
    public void DamageAt(Vector3 hitPoint, float amount, Actor damageSource)
    {
        Debug.LogWarning("[DestructibleWall] DamageAt is obsolete. Shooter must call ApplyDestructionRadius.");
    }
    public Actor GetActor() => null;
    public float GetHealth()
    {
        // 그룹이 체력형이면 그룹 체력(절대값) 환산, 아니면 '사실상 무적' 의미로 +무한대
        if (parentGroup && parentGroup.mode == WallGroupMode.HealthBased)
            return parentGroup.Health01 * parentGroup.maxHealth;
        return float.PositiveInfinity;
    }
    public bool IsDead()
    {
        // 체력형: 그룹 체력 0이면 사망
        if (parentGroup && parentGroup.mode == WallGroupMode.HealthBased)
            return parentGroup.Health01 <= 0f;

        // 무적형: 메시가 완전히 비었으면(절단으로) 사실상 사망 처리
        var tris = currentMesh ? currentMesh.triangles : null;
        return tris == null || tris.Length < 3;
    }
    public int GetGroupsCount() => 0;
    public Ragdoll GetRagdoll() => null;
}
