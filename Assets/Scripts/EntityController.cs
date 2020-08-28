using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public delegate Vector2 VelocityCompoundMethod();

public class EntityController : MonoBehaviour
{
    public enum MotionState { GROUNDED, AIR, CLUTCH };
    private Rigidbody2D rb;
    private VelocityCompoundMethod vOverride, vAdd, vMult;

    [Header("Runtime Statistics")]
    public MotionState state;
    public Vector2 currentVelocity = Vector2.zero;
    public Vector2 externalVelocity = Vector2.zero;
    public bool facingRight = true;

    [Header("Parameters")]
    public Vector2 gravityDir;
    public bool gravityOn;
    public float gravityCoeff;
    public float maxGravitySpeed;

    [Header("Runtime Trackers")]
    public float subAirTime = 0;
    public float totalAirTime = 0;
    public Vector2 directionalInfluence = Vector2.zero;

    private void Start()
    {
        // By default, gravity vector will face (0, -1);
        gravityDir = Vector2.down;
        gravityOn = true;
        rb = GetComponent<Rigidbody2D>();
        if (rb == null)
        {
            Debug.LogError("Missing Rigidbody2D for " + gameObject);
        }
    }

    public bool FacingRight() => facingRight;

    public void SetVelocity(Vector2 ve) => currentVelocity = ve;

    public void SetVelocityFunctions(VelocityCompoundMethod over, VelocityCompoundMethod add, VelocityCompoundMethod mult)
    {
        vOverride = over;
        vAdd = add;
        vMult = mult;
    }

    public void Update()
    {
        if (directionalInfluence.x > 0) facingRight = true;
        if (directionalInfluence.x < 0) facingRight = false;
    }

    public void FixedUpdate()
    {
        currentVelocity = CompoundVelocities(vOverride, vAdd, vMult);
        rb.velocity = currentVelocity;
        DecayExternalVelocity(0.1f);
    }

    private Vector2 CompoundVelocities(VelocityCompoundMethod over, VelocityCompoundMethod add, VelocityCompoundMethod mult)
    {
        Vector2 final = (over == null) ? Vector2.zero : over();
        if (final != Vector2.zero) return final;

        if (add != null) final += add();
        if (gravityOn) final += CalculateGravityVector(this);
        final += externalVelocity;
        if (mult != null) final.Scale(mult());

        return final;
    }

    /*
     * Can also be used externally to find the gravity vector of that entity.
     */
    public Vector2 CalculateGravityVector(EntityController en)
    {
        Vector2 dir = en.gravityDir.normalized;
        float gravSpeed = (en.subAirTime * en.gravityCoeff);
        bool uncapped = en.maxGravitySpeed < 0 ? true : false;
        return dir * (uncapped ? gravSpeed : (gravSpeed > en.maxGravitySpeed ? en.maxGravitySpeed : gravSpeed));
    }

    public void TallyAirTime()
    {
        totalAirTime += subAirTime;
        subAirTime = 0;
    }

    public void ResetAirTime()
    {
        totalAirTime = 0f;
        subAirTime = 0f;
    }

    /*
     * Returns the forward vector of the character based on the facingRight boolean.
     * Extrapolated since it is used in multiple places.
     */
    public Vector2 GetForwardVector()
    {
        return Vector2.right * (facingRight ? 1f : -1f);
    }

    public void ApplyVelocity(Vector2 force)
    {
        externalVelocity = force;
    }

    public void ApplyVelocity(Vector2 dir, float mag)
    {
        externalVelocity = dir * mag;
    }

    /*
     * Smoothly 
     */
    private void DecayExternalVelocity(float rate)
    {
        externalVelocity = Vector2.Lerp(externalVelocity, Vector2.zero, rate);
    }

}
