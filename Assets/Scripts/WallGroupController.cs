using UnityEngine;
using System.Collections.Generic;

public class WallGroupController : MonoBehaviour
{
    [Header("Supports & Walls")]
    public List<DestructibleWall> walls = new List<DestructibleWall>();
    public List<WallSupportPieceGroupController> supportGroups = new List<WallSupportPieceGroupController>();


    public void NotifySupportDestroyed()
    {
        // ��� �׷��� Critical �����밡 ���� �ı��Ǿ����� Ȯ��
        foreach (var group in supportGroups)
        {
            if (group != null && !group.AreAllCriticalDestroyed())
                return; // ���� �ı����� ���� �׷��� ���� �� ����
        }

        // ��� Critical ������ �ı��� �� ��ü �ı�
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
