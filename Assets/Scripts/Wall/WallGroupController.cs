using UnityEngine;
using System.Collections.Generic;

public class WallGroupController : MonoBehaviour
{
    [Header("Supports & Walls")]
    public List<DestructibleWall> walls = new List<DestructibleWall>();
    public List<WallSupportPieceGroupController> supportGroups = new List<WallSupportPieceGroupController>();


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

    private void DestroyGroup()
    {
        foreach (var wall in walls)
        {
            if (wall != null)
                Destroy(wall.gameObject);
        }

        foreach (var group in supportGroups)
        {
            if (group != null)
                Destroy(group.gameObject);
        }

        Destroy(gameObject);
    }
}
