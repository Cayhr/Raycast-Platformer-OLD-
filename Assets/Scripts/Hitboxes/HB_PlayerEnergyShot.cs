using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HB_PlayerEnergyShot : Projectile
{
    public float speed = 0f;
    public Vector2 direction = Vector2.zero;

    public override void OnHit(EntityController en)
    {
        // Play some special FX!
        en.health -= 1;
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        Debug.Log("Hit something!");
        EntityController en = EntityFromCollision(collision);
        OnHit(en);
        gameObject.SetActive(false);
    }
}
