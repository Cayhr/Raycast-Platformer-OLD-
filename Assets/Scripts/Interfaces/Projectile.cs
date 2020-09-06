using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class Projectile : PooledObject
{
    public List<FactionList> blacklist;
    public GameObject owner;
    protected Rigidbody2D rb;

    /*
     * The on hit function.
     */
    public abstract void OnHit(EntityController en);

    protected EntityController EntityFromCollision(Collision2D collision)
    {
        EntityController en = collision.gameObject.GetComponent<EntityController>();
        return en;
    }
    /*
    public void Init(int fac)
    {
        faction = fac;
    }
    */

    public void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject != owner)
        {
            EntityController en = EntityFromCollision(collision);
            if (en != null) OnHit(en);
            gameObject.SetActive(false);
        }
    }


}
