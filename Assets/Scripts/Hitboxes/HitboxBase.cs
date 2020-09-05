using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class HitboxBase : MonoBehaviour
{
    public List<FactionList> blacklist;
    /*
     * The on hit function.
     */
    public abstract void OnHit(EntityController en);

    protected EntityController EntityFromCollision(Collision2D collision)
    {
        EntityController en = collision.gameObject.GetComponent<EntityController>();
        return en;
    }
}
