using System.Collections.Generic;
using UnityEngine;

/*
 */
public class ObjectPoolController : MonoBehaviour
{
	public static ObjectPoolController SharedInstance;

	public Dictionary<string, ObjectPool> pools;

	void Awake()
	{
		SharedInstance = this;
		pools = new Dictionary<string, ObjectPool>();
	}

	/*
	 * Create a new pool.
	 */
	public ObjectPool CreateObjPool(string name, GameObject prefab, bool exp)
	{
		if (pools.ContainsKey(name))
		{
			Debug.LogError("Attempted to make duplicate of existing ObjectPool \"" + name + "\"");
			return null;
		}
		ObjectPool newPool = new ObjectPool(name, prefab, exp);
		pools.Add(name, newPool);
		return newPool;
	}

	/*
	 * Grab an object out of the pool name.
	 * When the object is retrieved, it will be disabled.
	 */
	public GameObject PullFromPool(string poolName)
	{
		if (!pools.ContainsKey(poolName)) return null;
		ObjectPool pool = pools[poolName];

		// If we attempted to grab an object out of the pool but there are none available.
		if (pool.queue.Count <= 0 && !pool.expand) return null;

		if (pool.expand)
		{
			GameObject dupe = Instantiate(pool.original);
			PooledObject scr = dupe.GetComponent<PooledObject>();
			scr.sourcePoolName = poolName;
			pool.takenOut.Add(dupe);
			return dupe;
		}
		else
		{
			GameObject pooledObj = pool.queue.Dequeue();
			pooledObj.SetActive(true);
			pool.takenOut.Add(pooledObj);
			return pooledObj;
		}
	}

	/*
	 * Adds a GameObject back to the pool.
	 * This should only be called by an object that was pulled from a pool at the end of its lifespan (when it is Disabled).
	 * As such, we do not need to disable the GameObject when it returns to the pool.
	 * If it is not a GameObject that was tracked by the ObjectPool, it will be refused.
	 */
	public void ReturnToPool(string poolName, GameObject obj)
	{
		ObjectPool op = pools[poolName];
		if (op != null)
		{
			if (op.takenOut.Contains(obj))
			{
				op.takenOut.Remove(obj);
				op.queue.Enqueue(obj);
			}
            else {
                Debug.LogError("Attempted to re-pool a GameObject already in the \"" + poolName + "\" pool.");
                return;
            }
		}
		else
		{
			Debug.LogError("Cannot return GameObject `" + obj + "` to non-existent pool \"" + poolName + "\"");
			return;
		}
	}
}

public class ObjectPool
{
	public string poolName;
	public GameObject original;
	public Queue<GameObject> queue;
	public HashSet<GameObject> takenOut;
	public bool expand;

	public ObjectPool(string name, GameObject obj, bool exp = true)
	{
		poolName = name;
		original = obj;
		queue = new Queue<GameObject>();
		takenOut = new HashSet<GameObject>();
		expand = true;
	}
}
