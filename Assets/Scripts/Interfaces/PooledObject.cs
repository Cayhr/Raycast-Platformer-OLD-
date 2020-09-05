using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class PooledObject : MonoBehaviour
{
    public string sourcePoolName;

    private void Start()
    {
    }

    public virtual void OnEnable()
    {
        if (sourcePoolName == null)
        {
            gameObject.SetActive(false);
        }
    }

    public virtual void OnDisable()
    {
        ObjectPoolController opc = ObjectPoolController.SharedInstance;
        if (opc == null)
        {
            Destroy(gameObject);
            return;
        }

        opc.ReturnToPool(sourcePoolName, gameObject);
    }
}
