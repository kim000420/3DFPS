using UnityEngine;

namespace _Project.Scripts.VisualScripting
{
    public class ToggleActivate : ProcessBase
    {
        [SerializeField] private GameObject obj;
        
        // TODO: 
        private void Start()
        {
            IsOn = obj.activeSelf;
        }

        public override void Execute()
        {
            IsOn = obj.activeSelf;
            obj.SetActive(!IsOn);
        }
    }
}