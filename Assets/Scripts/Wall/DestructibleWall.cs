using System.Collections.Generic;
using UnityEngine;
using Akila.FPSFramework;

[RequireComponent(typeof(MeshFilter), typeof(MeshCollider))]
public class DestructibleWall : MonoBehaviour, IDamageable
{
    [Header("Thresholds (2D area)")]
    [SerializeField, Min(0f)]
    private float bigBreachThreshold = 0.25f;     // 작은/큰 파괴 구분 임계치 (잘린 2D 면적 기준)

    [SerializeField, Min(0f)]
    private float bigIslandThreshold = 0.25f;     // 작은/큰 '고립 섬' 구분 임계치 (2D 면적 기준)

    [SerializeField, Range(0.9f, 1f), Tooltip("외곽 접속성 판정 계수 (1에 가까울수록 엄격)")]
    private float edgeContactFactor = 0.999f; // 외곽과 '붙어있다'고 볼 여유 계수

    [SerializeField, Min(0f), Tooltip("섬 면적 계산 시 무시할 최소 2D 면적")]
    private float islandAreaEpsilon = 1e-6f;   // 너무 작은 섬은 노이즈로 간주하고 무시

    [SerializeField, Min(1e-6f), Tooltip("2D 좌표 양자화 단위 (앞/뒤 패턴 매칭 안정화)")]
    private float patternQuantize = 1e-3f;        // 패턴 매칭용 좌표 라운딩 단위

    [SerializeField, Min(0f), Tooltip("반경 판정 오차 허용치 (부동소수 오차 보정)")]
    private float radiusEpsilon = 1e-4f;     // 타격 반경 비교 시 여유 값

    [SerializeField, Tooltip("그룹 체력/연출을 관리하는 상위 컨트롤러 (선택)")]
    private WallGroupController parentGroup;  // 그룹 모드에서만 사용


    private MeshFilter _meshFilter;
    private MeshCollider _meshCollider;
    private Mesh _currentMesh; // 수정 가능한 현재 메시
    private Renderer _renderer;


    // IDamageable 호완 프로퍼티
    public float MaxHealth { get; set; }
    public Vector3 deathForce { get; set; }
    public bool deadConfirmed { get; set; }

    private void Awake()
    {
        _meshFilter = GetComponent<MeshFilter>();
        _meshCollider = GetComponent<MeshCollider>();
        _renderer = GetComponent<Renderer>();

        // 메시 복사 (원본 건드리지 않기)
        _currentMesh = Instantiate(_meshFilter.mesh);
        _meshFilter.mesh = _currentMesh;
        _meshCollider.sharedMesh = _currentMesh;

        if (!parentGroup) parentGroup = GetComponentInParent<WallGroupController>();
    }

    #region Public Gameplay API
    // FPS Framework explosive.cs 호출용
    public float ApplyDestructionRadius(Vector3 hitPoint, float worldRadius, float? bigThresholdOverride = null)
    {
        // 1) 메시 절단 (면적 계산 포함)
        float removedArea = TryClipReturnArea(hitPoint, worldRadius);

        // 2) 파괴 규모 분류(임계치 외부에서 덮어쓰기 가능)
        float th = bigThresholdOverride ?? bigBreachThreshold;
        var kind = (removedArea >= th) ? DestructionKind.BigBreach : DestructionKind.SmallHit;

        // 3) 결과 이벤트만 브로드캐스트 (체력/데미지 관여 X)
        RaiseDestructionEvent(kind, hitPoint, removedArea);


        return removedArea;
    }
    // 외곽과 접한 삼각형들만 제거하는 유틸
    public void RemoveBoundaryTriangles()
    {
        // 준비
        int thicknessAxis = GetThicknessAxis(out _);
        var vertices = _currentMesh.vertices;
        var triangles = _currentMesh.triangles;
        int triCount = triangles.Length / 3;
        if (triCount == 0) return;

        _currentMesh.RecalculateBounds();
        var bounds = _currentMesh.bounds;

        // 두께축 제외한 투영 바운즈 파라미터 (RemoveFloatingIslands2D와 동일 판정 재사용)
        float ex, ey, cx, cy;
        if (thicknessAxis == 0) { ex = bounds.extents.y; ey = bounds.extents.z; cx = bounds.center.y; cy = bounds.center.z; }
        else if (thicknessAxis == 1) { ex = bounds.extents.x; ey = bounds.extents.z; cx = bounds.center.x; cy = bounds.center.z; }
        else { ex = bounds.extents.x; ey = bounds.extents.y; cx = bounds.center.x; cy = bounds.center.y; }
        float mx = ex * edgeContactFactor, my = ey * edgeContactFactor;

        List<int> kept = new List<int>(triangles.Length);
        float removedArea2D = 0f;
        int removed = 0;

        for (int i = 0; i < triCount; i++)
        {
            int ia = triangles[i * 3 + 0], ib = triangles[i * 3 + 1], ic = triangles[i * 3 + 2];

            Vector2 a2 = ProjectTo2D(vertices[ia], thicknessAxis);
            Vector2 b2 = ProjectTo2D(vertices[ib], thicknessAxis);
            Vector2 c2 = ProjectTo2D(vertices[ic], thicknessAxis);

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
                transform.TransformPoint(_currentMesh.bounds.center),
                removedArea2D
            );
        }
    }
    // 유틸
    public Vector3 GetClosestPointOnSurface(Vector3 worldPos)
    {
        // 로컬 변환
        Vector3 local = transform.InverseTransformPoint(worldPos);

        // 두께축과 투영 축 판정
        int axis = GetThicknessAxis(out _);
        _currentMesh.RecalculateBounds();
        var b = _currentMesh.bounds;

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
    // 그룹 데미지 통보용 래퍼
    public void NotifyGroupDamage(float amount, Vector3 hitPoint)
    {
        if (parentGroup) parentGroup.ApplyGroupDamage(amount, hitPoint);
    }
    #endregion

    #region Core Destruction Internals

    // 절단 이후 고립섭 제거, 면적확인후 파편 생성 이벤트
    private float TryClipReturnArea(Vector3 hitWorldPos, float radius)
    {
        // 코어 호출
        var (kept, removedArea2D, thicknessAxis) = ClipMeshCore(hitWorldPos, radius);

        // 메시/콜라이더 반영
        ApplyMeshAndColliderSafe(kept);

        // 고립 섬 정리
        CleanupFloatingIslands(thicknessAxis);

        return removedArea2D;
    }
    
    // 절단 수행 및 잘려나간 2D면적을 반환
    private (int[] kepttriangles, float removedArea2D, int thicknessAxis)
    ClipMeshCore(Vector3 hitWorldPos, float radius)
    {
        // 0) 좌표/축 준비
        Vector3 hitLocalPos = transform.InverseTransformPoint(hitWorldPos);
        int thicknessAxis = GetThicknessAxis(out Vector3 thicknessDir);
        Vector2 hit2 = ProjectTo2D(hitLocalPos, thicknessAxis);

        var v = _currentMesh.vertices;
        var t = _currentMesh.triangles;
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

    // 절단 후 고립된 섬들을 제거하고, 섬 면적에 따라 이벤트를 발행한다.
    public void CleanupFloatingIslands(int thicknessAxis)
    {
        var vertices = _currentMesh.vertices;
        var triangles = _currentMesh.triangles;
        int triCount = triangles.Length / 3;
        if (triCount == 0) return;

        // 1단계 인접 리스트 구성
        var adj = BuildTriangleAdjacency(triangles);

        // 2단계 외곽 접속 삼각형 마킹 + BFS 확장
        _currentMesh.RecalculateBounds();
        var bounds = _currentMesh.bounds;
        bool[] edgeConnected = MarkEdgeConnectedTriangles(thicknessAxis, vertices, triangles, bounds, edgeContactFactor);

        // BFS 확장
        var q = new Queue<int>();
        for (int i = 0; i < triCount; i++)
        {
            if (!edgeConnected[i]) continue;
            q.Enqueue(i);
            while (q.Count > 0)
            {
                int cur = q.Dequeue();
                foreach (var nx in adj[cur])
                {
                    if (!edgeConnected[nx])
                    {
                        edgeConnected[nx] = true;
                        q.Enqueue(nx);
                    }
                }
            }
        }

        // 3단계 외곽과 단절된 섬 수집
        var islands = CollectDisconnectedIslands(adj, edgeConnected);

        // 4단계 kept 삼각형 목록 구성
        var kept = new List<int>(triangles.Length);
        for (int i = 0; i < triCount; i++)
        {
            if (edgeConnected[i])
            {
                kept.Add(triangles[i * 3 + 0]);
                kept.Add(triangles[i * 3 + 1]);
                kept.Add(triangles[i * 3 + 2]);
            }
        }

        // 5단계 섬별 면적과 중심 계산 후 이벤트 발행
        foreach (var island in islands)
        {
            float islandArea = 0f;
            Vector2 centroidSum = Vector2.zero;

            foreach (var triIdx in island)
            {
                int ia = triangles[triIdx * 3 + 0], ib = triangles[triIdx * 3 + 1], ic = triangles[triIdx * 3 + 2];
                Vector2 a2 = ProjectTo2D(vertices[ia], thicknessAxis);
                Vector2 b2 = ProjectTo2D(vertices[ib], thicknessAxis);
                Vector2 c2 = ProjectTo2D(vertices[ic], thicknessAxis);

                float triArea = Mathf.Abs((a2.x * (b2.y - c2.y) + b2.x * (c2.y - a2.y) + c2.x * (a2.y - b2.y))) * 0.5f;
                islandArea += triArea;
                Vector2 triCentroid = (a2 + b2 + c2) / 3f;
                centroidSum += triCentroid * triArea;
            }

            if (islandArea <= islandAreaEpsilon) continue;

            Vector2 islandCentroid2D = centroidSum / islandArea;
            Vector3 centroid3D;
            switch (thicknessAxis)
            {
                case 0: centroid3D = new Vector3(_currentMesh.bounds.center.x, islandCentroid2D.x, islandCentroid2D.y); break;
                case 1: centroid3D = new Vector3(islandCentroid2D.x, _currentMesh.bounds.center.y, islandCentroid2D.y); break;
                default: centroid3D = new Vector3(islandCentroid2D.x, islandCentroid2D.y, _currentMesh.bounds.center.z); break;
            }
            Vector3 worldPos = transform.TransformPoint(centroid3D);

            var kind = (islandArea >= bigIslandThreshold) ? DestructionKind.BigIsland : DestructionKind.SmallIsland;
            RaiseDestructionEvent(kind, worldPos, islandArea);
        }

        // 6단계 메시와 콜라이더 적용
        ApplyMeshAndColliderSafe(kept.ToArray());
    }
    #endregion

    #region Geometry / Graph Helpers    
    // 삼각형 인접 리스트를 생성한다. 공유 에지를 기준으로 연결한다.
    private List<int>[] BuildTriangleAdjacency(int[] triangles)
    {
        int triCount = triangles.Length / 3;
        List<int>[] adj = new List<int>[triCount];
        for (int i = 0; i < triCount; i++) adj[i] = new List<int>();

        long EdgeKey(int a, int b)
        {
            if (a > b) { int t = a; a = b; b = t; }
            return ((long)a << 32) ^ (long)(uint)b;
        }

        var edgeTotriangles = new Dictionary<long, List<int>>(triCount * 3);
        for (int i = 0; i < triCount; i++)
        {
            int ia = triangles[i * 3 + 0], ib = triangles[i * 3 + 1], ic = triangles[i * 3 + 2];
            long e0 = EdgeKey(ia, ib);
            long e1 = EdgeKey(ib, ic);
            long e2 = EdgeKey(ic, ia);
            if (!edgeTotriangles.TryGetValue(e0, out var L0)) edgeTotriangles[e0] = L0 = new List<int>();
            if (!edgeTotriangles.TryGetValue(e1, out var L1)) edgeTotriangles[e1] = L1 = new List<int>();
            if (!edgeTotriangles.TryGetValue(e2, out var L2)) edgeTotriangles[e2] = L2 = new List<int>();
            L0.Add(i); L1.Add(i); L2.Add(i);
        }

        foreach (var kv in edgeTotriangles)
        {
            var L = kv.Value;
            for (int i = 0; i < L.Count; i++)
                for (int j = i + 1; j < L.Count; j++)
                {
                    int t0 = L[i], t1 = L[j];
                    adj[t0].Add(t1);
                    adj[t1].Add(t0);
                }
        }
        return adj;
    }

    // 외곽에 맞닿은 삼각형들로부터 BFS를 수행해 "외곽과 연결된" 삼각형을 표시한다.
    private bool[] MarkEdgeConnectedTriangles(
        int thicknessAxis,
        Vector3[] vertices,
        int[] triangles,
        Bounds bounds,
        float contactFactor // edgeContactFactor 사용
    )
    {
        int triCount = triangles.Length / 3;
        bool[] visited = new bool[triCount];

        float ex, ey, cx, cy;
        if (thicknessAxis == 0) { ex = bounds.extents.y; ey = bounds.extents.z; cx = bounds.center.y; cy = bounds.center.z; }
        else if (thicknessAxis == 1) { ex = bounds.extents.x; ey = bounds.extents.z; cx = bounds.center.x; cy = bounds.center.z; }
        else { ex = bounds.extents.x; ey = bounds.extents.y; cx = bounds.center.x; cy = bounds.center.y; }

        float mx = ex * contactFactor;
        float my = ey * contactFactor;

        var q = new Queue<int>();
        for (int i = 0; i < triCount; i++)
        {
            int ia = triangles[i * 3 + 0], ib = triangles[i * 3 + 1], ic = triangles[i * 3 + 2];
            Vector2 a2 = ProjectTo2D(vertices[ia], thicknessAxis);
            Vector2 b2 = ProjectTo2D(vertices[ib], thicknessAxis);
            Vector2 c2 = ProjectTo2D(vertices[ic], thicknessAxis);

            bool nearEdge =
                Mathf.Abs(a2.x - cx) >= mx || Mathf.Abs(a2.y - cy) >= my ||
                Mathf.Abs(b2.x - cx) >= mx || Mathf.Abs(b2.y - cy) >= my ||
                Mathf.Abs(c2.x - cx) >= mx || Mathf.Abs(c2.y - cy) >= my;

            if (nearEdge && !visited[i])
            {
                visited[i] = true;
                q.Enqueue(i);
                while (q.Count > 0)
                {
                    int cur = q.Dequeue();
                }
            }
        }
        // 주의: 여기서는 시작점만 표시하고, 전체 확장은 CleanupFloatingIslands에서 처리
        return visited;
    }

    // 외곽과 연결되지 않은 삼각형들을 섬 단위로 수집한다.
    private List<List<int>> CollectDisconnectedIslands(List<int>[] adj, bool[] edgeConnected)
    {
        int triCount = edgeConnected.Length;
        var islands = new List<List<int>>();
        var seen = new bool[triCount];

        for (int i = 0; i < triCount; i++)
        {
            if (edgeConnected[i] || seen[i]) continue;

            var island = new List<int>();
            var q = new Queue<int>();
            seen[i] = true;
            q.Enqueue(i);

            while (q.Count > 0)
            {
                int cur = q.Dequeue();
                island.Add(cur);
                foreach (var nx in adj[cur])
                {
                    if (!edgeConnected[nx] && !seen[nx])
                    {
                        seen[nx] = true;
                        q.Enqueue(nx);
                    }
                }
            }
            islands.Add(island);
        }
        return islands;
    }

    public int GetThicknessAxis(out Vector3 axisDir)
    {
        _currentMesh.RecalculateBounds();
        var e = _currentMesh.bounds.extents;
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
    #endregion

    #region Mesh / Collider & Events
    // 파괴/고립섭 제거 공통 이벤트 발행
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

    // 메시 절단 및 제거면적 기반 파괴규모 이벤트 발행
    private void ApplyMeshAndColliderSafe(int[] triangles)
    {
        _currentMesh.triangles = triangles;
        _currentMesh.RecalculateNormals();
        _currentMesh.RecalculateBounds();

        if (triangles != null && triangles.Length >= 3)
        {
            if (!_meshCollider.enabled) _meshCollider.enabled = true;
            _meshCollider.sharedMesh = null;
            _meshCollider.sharedMesh = _currentMesh; // OK: 유효한(>=1 tri) 메쉬만 바인딩
        }
        else
        {
            _meshCollider.sharedMesh = null;
            if (_meshCollider.enabled) _meshCollider.enabled = false;  // 충돌체 완전히 비활성
        }
    }
    #endregion

    #region IDamageable
    public void Damage(float amount, Actor damageSource) { }
    public void DamageAt(Vector3 hitPoint, float amount, Actor damageSource) { }
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
        var triangles = _currentMesh ? _currentMesh.triangles : null;
        return triangles == null || triangles.Length < 3;
    }
    public int GetGroupsCount() => 0;
    public Ragdoll GetRagdoll() => null;
    #endregion
}
