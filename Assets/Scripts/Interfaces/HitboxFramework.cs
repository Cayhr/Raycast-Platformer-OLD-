using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class HitboxFramework : MonoBehaviour
{
    public List<FactionList> blacklist;

    /*
     * The on hit function.
     */
    public abstract void OnHit(EntityController en);

    /*
    public void Init(int fac)
    {
        faction = fac;
    }

    public void OnCollisionEnter2D(Collision2D collision)
    {
        //OnHit(en);
    }
    */


}
