using System.Collections.Generic;
using UnityEngine;

public class DebrisPool : MonoBehaviour
{
    [SerializeField] GameObject debrisPrefab;
    [SerializeField] List<GameObject> debrisPrefabs = new List<GameObject>(); // ★ 추가: 여러 개
    [SerializeField] int prewarm = 20;
    readonly Queue<GameObject> pool = new Queue<GameObject>();

    void Awake()
    {
        for (int i = 0; i < prewarm; i++)
        {
            var go = Instantiate(ChoosePrefab(), transform);
            go.SetActive(false);
            pool.Enqueue(go);
        }
    }

    GameObject ChoosePrefab()
    {
        if (debrisPrefabs != null && debrisPrefabs.Count > 0)
            return debrisPrefabs[Random.Range(0, debrisPrefabs.Count)];
        return debrisPrefab;
    }

    GameObject Pop()
    {
        if (pool.Count > 0) return pool.Dequeue();
        var go = Instantiate(ChoosePrefab(), transform);
        go.SetActive(false);
        return go;
    }

    void Push(GameObject go) { if (!go) return; go.SetActive(false); pool.Enqueue(go); }

    // 기존 기본 버스트
    public void SpawnBurst(Vector3 pos, int count = 6) => SpawnBurst(pos, count, 2.5f);

    // 파라미터 확장 버전
    public void SpawnBurst(Vector3 pos, int count, float explosionForce)
    {
        for (int i = 0; i < count; i++)
        {
            var go = Pop();
            go.transform.position = pos + Random.insideUnitSphere * 0.2f;
            go.transform.rotation = Random.rotation;
            go.SetActive(true);
            var rb = go.GetComponent<Rigidbody>();
            if (rb) rb.AddExplosionForce(explosionForce, pos, 2f, 0.5f, ForceMode.Impulse);
            StartCoroutine(ReturnAfter(go, 5f));
        }
    }

    System.Collections.IEnumerator ReturnAfter(GameObject go, float t)
    {
        yield return new WaitForSeconds(t);
        Push(go);
    }
}
