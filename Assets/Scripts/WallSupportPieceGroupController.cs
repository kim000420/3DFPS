using UnityEngine;
using System.Collections.Generic;

public class WallSupportPieceGroupController : MonoBehaviour
{
    public List<WallSupportPieceController> pieces = new List<WallSupportPieceController>();

    public bool AreAllCriticalDestroyed()
    {
        foreach (var piece in pieces)
        {
            if (piece != null && piece.type == SupportType.Critical && !piece.isDestroyed)
                return false;
        }
        return true;
    }
}
