using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HB_PlayerEnergyShot : HitboxFramework
{
    public override void OnHit(EntityController en)
    {
        // Play some special FX!
        en.health -= 1;
        Destroy(gameObject);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        Debug.Log("Hit something!");
    }
}
