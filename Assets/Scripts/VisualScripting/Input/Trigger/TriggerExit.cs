using UnityEngine;

namespace _Project.Scripts.VisualScripting
{
    public class TriggerExit : ProcessBase
    {
        [SerializeField] private string selectedTag = "";

        private void OnTriggerExit(Collider other)
        {
            if (other.CompareTag(selectedTag))
                // Execute();
                IsOn = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag(selectedTag))
                IsOn = false;
        }

        public override void Execute()
        {
            IsOn = true;
        }
        
        private void OnDrawGizmos()
        {
            Gizmos.color = Color.blue;
		        
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
        }
    }
    
}