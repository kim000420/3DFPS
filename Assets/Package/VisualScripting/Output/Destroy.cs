using _Project.Scripts.VisualScripting;
using UnityEngine;

public class Destroy : ProcessBase
{
    [SerializeField] private GameObject target;
    
    public override void Execute()
    {
        if (target != null)
            Destroy(target);

        IsOn = true;
    }
}
