using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HB_PlayerEnergyShot : Projectile
{
    public float speed = 0f;
    public Vector2 direction = Vector2.zero;

    public override void OnHit(EntityController en)
    {
        if (en.gameObject != owner)
        {
            // Play some special FX!
            Debug.Log("Hit `" + en.gameObject + "`");
            en.health -= 1;
        }
    }
}
