using System.Collections.Generic;
using UnityEngine;

public class DebrisPool : MonoBehaviour
{
    [Header("Fallback (둘 다 비었을 때 사용)")]
    [SerializeField] GameObject defaultPrefab;

    [Header("Small Pieces")]
    [SerializeField] List<GameObject> smallPrefabs = new List<GameObject>();
    [SerializeField] int smallPrewarm = 20;

    [Header("Big Pieces")]
    [SerializeField] List<GameObject> bigPrefabs = new List<GameObject>();
    [SerializeField] int bigPrewarm = 10;

    readonly Queue<GameObject> smallPool = new Queue<GameObject>();
    readonly Queue<GameObject> bigPool = new Queue<GameObject>();

    void Awake()
    {
        for (int i = 0; i < smallPrewarm; i++)
        {
            var prefab = ChoosePrefab(smallPrefabs);
            var go = Instantiate(prefab, transform); // ← 여기서만 Instantiate
            go.SetActive(false);
            smallPool.Enqueue(go);
        }

        for (int i = 0; i < bigPrewarm; i++)
        {
            var prefab = ChoosePrefab(bigPrefabs);
            var go = Instantiate(prefab, transform);
            go.SetActive(false);
            bigPool.Enqueue(go);
        }
    }

    GameObject ChoosePrefab(List<GameObject> list)
    {
        if (list != null && list.Count > 0) return list[Random.Range(0, list.Count)];
        return defaultPrefab;
    }

    GameObject Pop(Queue<GameObject> q, List<GameObject> list)
    {
        if (q.Count > 0) return q.Dequeue();
        var prefab = ChoosePrefab(list);
        var go = Instantiate(prefab, transform);
        go.SetActive(false);
        return go;
    }

    void Push(Queue<GameObject> q, GameObject go)
    {
        if (!go) return;
        go.SetActive(false);
        q.Enqueue(go);
    }

    public void SpawnSmall(Vector3 pos, int count, float force) => SpawnBurst(pos, count, force, smallPool, smallPrefabs);
    public void SpawnBig(Vector3 pos, int count, float force) => SpawnBurst(pos, count, force, bigPool, bigPrefabs);

    void SpawnBurst(Vector3 pos, int count, float force, Queue<GameObject> pool, List<GameObject> prefabs)
    {
        for (int i = 0; i < count; i++)
        {
            var go = Pop(pool, prefabs);
            go.transform.position = pos + Random.insideUnitSphere * 0.2f;
            go.transform.rotation = Random.rotation;
            go.SetActive(true);
            if (go.TryGetComponent<Rigidbody>(out var rb))
                rb.AddExplosionForce(force, pos, 2f, 0.5f, ForceMode.Impulse);
            StartCoroutine(ReturnAfter(go, 5f, pool));
        }
    }

    System.Collections.IEnumerator ReturnAfter(GameObject go, float t, Queue<GameObject> pool)
    {
        yield return new WaitForSeconds(t);
        Push(pool, go);
    }
}
