using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public delegate Vector2 VelocityCompoundMethod();
public delegate void StateBasedAction();
public enum FactionList { NEUTRAL, PLAYER, ENEMIES};
public enum EntityMotionState { GROUNDED, AIR, CLUTCH };

[RequireComponent(typeof(BoxCollider2D))]
public class EntityController : MonoBehaviour
{
    private Rigidbody2D rb;
    private static readonly float RAYCAST_INLET = 0.015f;
    //public CircleCollider2D mainCollider;
    //public BoxCollider2D headCollider;
    private VelocityCompoundMethod vOverride, vAdd, vMult;
    private StateBasedAction sbaOnLanding, sbaWhileInAir, sbaCollisionX, sbaCollisionY;

    [Header("References")]
    // The forms this Entity can take. At a minimum, are a GameObject with sprite and collider.
    // Different forms should only be changes in hitboxes, such as player standing and crouching form.
    [SerializeField] private List<GameObject> forms = new List<GameObject>();

    [Header("Entity Descriptors")]
    public string entityName;
    public int health, maxHealth;
    public FactionList faction;
    [SerializeField] private int formIndex;
    private GameObject formObject;
    private BoxCollider2D formCollider;

    [Header("Runtime Statistics")]
    public EntityMotionState state;
    public Vector2 currentVelocity = Vector2.zero;
    public Vector2 externalVelocity = Vector2.zero;
    public bool facingRight = true;

    [Header("Physics Parameters")]
    public Vector2 normalVector;
    public Vector2 rightVector;
    public bool gravityOn;
    public float gravityCoeff, maxGravitySpeed, inertialDampening, dampeningCutoff, maxClimbAngle;
    [SerializeField] int rayPrecisionBase, rayPrecisionHeight;    // The amount of extra points BETWEEN the 2 on the edges.

    [Header("Runtime Trackers")]
    public float subAirTime = 0;
    public float totalAirTime = 0;
    public Vector2 directionalInfluence = Vector2.zero;

    // Raycasting Variables
    private Bounds formBounds;
    private Vector2[] relativeBasePoints;
    private Vector2[] relativeHeightPoints;

    private void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        //mainCollider = GetComponent<CircleCollider2D>();
        //headCollider = GetComponent<BoxCollider2D>();
        if (forms.Count == 0)
        {
            Debug.LogError(gameObject + " has no forms.");
            gameObject.SetActive(false);
        }
        ChangeForm(formIndex);
        ChangeNormalVector(normalVector);
        if (rb == null)
        {
            Debug.LogError("Missing Rigidbody2D for " + gameObject);
        }
        else
        {
            Physics2D.IgnoreLayerCollision(8, 8, true);
        }
    }

    public bool FacingRight() => facingRight;

    public void SetVelocity(Vector2 ve) => currentVelocity = ve;

    /// <summary>
    /// Sets the velocity functions for the Entity.
    /// </summary>
    public void SetVelocityFunctions(VelocityCompoundMethod over, VelocityCompoundMethod add, VelocityCompoundMethod mult)
    {
        vOverride = over;
        vAdd = add;
        vMult = mult;
    }

    public void SetEventFunctions(StateBasedAction landing, StateBasedAction inAir)
    {
        sbaOnLanding = landing;
        sbaWhileInAir = inAir;
    }

    public void Update()
    {
        if (directionalInfluence.x > 0) facingRight = true;
        else if (directionalInfluence.x < 0) facingRight = false;

        // First calculate the final velocity of the entity.
        currentVelocity = CompoundVelocities(vOverride, vAdd, vMult);

        // Do our Raycast Movement logic.
        RaycastMovement();

        DecayExternalVelocity();
    }

    private void RaycastMovement()
    {
        Vector2 scaledVelocity = currentVelocity * Time.deltaTime;
        Vector2 moveX = Vector2.zero;
        Vector2 moveY = Vector2.zero;

        // If we are on the ground, we should check if we are still on it.
        // Slap the results of the ground checks in moveY, even though we don't use it anymore.
        int groundChecks = (state == EntityMotionState.GROUNDED) ? RaycastY(normalVector * -1f, RAYCAST_INLET * 2 , ref moveY) : 0;

        // If there is no ground below us.
        if (groundChecks == 0) WhileInAir();

        // Perpendicular of inverse of the normal points in the positive X direction, or (1, 0) on unit circle.
        int xHits = RaycastX(rightVector * Mathf.Sign(currentVelocity.x), scaledVelocity.x, ref moveX);

        // Normal Vector points "up".
        int yHits = RaycastY(normalVector * Mathf.Sign(currentVelocity.y), scaledVelocity.y, ref moveY);

        // We use Translate because Rigidbody2D.MovePosition() creates weird jittering situations. Translate is more precise!
        transform.Translate(moveX + moveY);

        // Sync after every transform translation.
        Physics2D.SyncTransforms();

    }

    /*
     * Along the subdivided points of the height faces of the BoxCollider2D in the direction of movement,
     *  raycast in those directions and detect collisions. Translate the entity appropriately.
     * ---------------------------------------------------------------------------------------------------------------------------
     * RaycastX: Calculate the movement vector we need to travel along the relative X axis.
     * ---------------------------------------------------------------------------------------------------------------------------
     * RaycastY: Calculate the movement vector we need to travel along the relative X axis.
     *  If we have negative velocity and we hit ground, we landed (taken care of in Update).
     * ---------------------------------------------------------------------------------------------------------------------------
     * <BUG WORKAROUND>: Within the loop on both X and Y, the Vector2 `origin` uses the formObject's transform position,
     *  rather than the Bounds component's center.
     *  The latter was not returning an updated world position for non-player Entities.
     */
    private int RaycastX(Vector2 normDir, float rawVelocity, ref Vector2 finalVel)
    {
        bool DEBUG = true;
        // If we aren't moving, save computation cycles by not doing anything.
        if ((normDir * rawVelocity) == Vector2.zero) return 0;
        // If the distance to cast is negative, turn it positive. Also compensate for RAYCAST_INLET.
        float castDist = Mathf.Abs(rawVelocity) + RAYCAST_INLET;
        if (castDist <= RAYCAST_INLET) castDist = 2 * RAYCAST_INLET;

        float closestDelta = castDist;
        Vector2 slopeVelocity = Vector2.zero;
        int hits = 0;
        for (int i = 0; i < rayPrecisionHeight; i++)
        {
            Vector2 origin = (Vector2)formObject.transform.position + new Vector2(relativeHeightPoints[i].x * normDir.x, relativeHeightPoints[i].y);
            RaycastHit2D hit = Physics2D.Raycast(origin, normDir, castDist, LayerInfo.OBSTACLES);

            // If the raycast hit a collider: Find the point of contact.
            if (hit)
            {
                if (DEBUG) Debug.DrawRay(origin, normDir * hit.distance, Color.red);
                float slopeAngle = Vector2.Angle(hit.normal, normalVector);
                if (i == 0 && slopeAngle <= maxClimbAngle)
                {
                    ClimbSlope(ref slopeVelocity, slopeAngle);
                    Debug.Log(slopeAngle);
                }
                hits++;
                if (hit.distance == 0) continue;
                float hitDist = hit.distance - RAYCAST_INLET;
                if (hitDist < closestDelta) closestDelta = hitDist;
            }
            else
            {
                if (DEBUG) Debug.DrawRay(origin, normDir * castDist, Color.green);
            }
        }

        if (hits > 0)
        {
            sbaCollisionX?.Invoke();
            externalVelocity.x = 0f;
        }

        // Return the Vector2 for movement along the X axis of the entity.
        finalVel = closestDelta * normDir;
        return hits;
    }

    private int RaycastY(Vector2 normDir, float rawVelocity, ref Vector2 finalVel)
    {
        bool DEBUG = true;
        // If we aren't moving, save computation cycles by not doing anything.
        if ((normDir * rawVelocity) == Vector2.zero) return 0;
        // If the distance to cast is negative, turn it positive. Also compensate for RAYCAST_INLET.
        float castDist = Mathf.Abs(rawVelocity) + RAYCAST_INLET;
        if (castDist <= RAYCAST_INLET) castDist = 2 * RAYCAST_INLET;

        float closestDelta = castDist;
        int hits = 0;
        for (int i = 0; i < rayPrecisionBase; i++)
        {
            Vector2 origin = (Vector2)formObject.transform.position + new Vector2(relativeBasePoints[i].x, relativeBasePoints[i].y * normDir.y);
            RaycastHit2D hit = Physics2D.Raycast(origin, normDir, castDist, LayerInfo.OBSTACLES);

            // If the raycast hit a collider: Find the point of contact.
            if (hit)
            {
                if (DEBUG) Debug.DrawRay(origin, normDir * hit.distance, Color.red);
                hits++;
                if (hit.distance == 0) continue;
                float hitDist = hit.distance - RAYCAST_INLET;
                if (hitDist < closestDelta) closestDelta = hitDist;
            }
            else
            {
                if (DEBUG) Debug.DrawRay(origin, normDir * castDist, Color.green);
            }
        }

        // If we have 1 or more hits while descending, we landed on ground.
        if (hits > 0)
        {
            sbaCollisionY?.Invoke();
            externalVelocity.y = 0f;
            if (rawVelocity < 0f)
            {
                state = EntityMotionState.GROUNDED;
                OnLanding();
            }
        }

        // Return the Vector2 for movement along the Y axis of the entity.
        finalVel = closestDelta * normDir;
        return hits;
    }

    private void ClimbSlope(ref Vector2 slopeVelocity, float slopeAngle)
    {
        //float moveDist = Mathf.Abs();

    }

    /*
     * Upon changing forms, a lot of hitbox information needs to change.
     * Activate the new form GameObject and deactivate the old one.
     * Cache the new form's component information into variables.
     * Calculate the relative offset points from the center of the hitbox
     * to the left and right according to the raycast precision on the respective axes.
     */
    public void ChangeForm(int index)
    {
        if (index >= forms.Count || index < 0)
        {
            Debug.LogError(gameObject + ": Tried to access form " + index + ". Only " + forms.Count + " available.");
            return;
        }
        //if (i == formIndex) return;
        forms[formIndex].SetActive(false);
        forms[index].SetActive(true);
        formObject = forms[index];
        formCollider = formObject.GetComponent<BoxCollider2D>();
        formBounds = formCollider.bounds;
        formIndex = index;

        // Reallocate the bound points in memory.
        // We must have at least 2 rays for the edges of the BoxCollider's face.
        const int MIN_RAYS = 2;
        rayPrecisionBase = Mathf.Clamp(rayPrecisionBase, MIN_RAYS, int.MaxValue);
        rayPrecisionHeight = Mathf.Clamp(rayPrecisionHeight, MIN_RAYS, int.MaxValue);
        relativeBasePoints = new Vector2[rayPrecisionBase];
        relativeHeightPoints = new Vector2[rayPrecisionHeight];

        // Calculate left to right and bottom to top the colinear points for X and Y respectively.
        formBounds.Expand(RAYCAST_INLET * -2f);
        Vector2 boxSize = formBounds.size;
        float adjustedLengthBase = boxSize.x;
        float adjustedLengthHeight = boxSize.y;
        float subLengthBase = adjustedLengthBase / (rayPrecisionBase - 1);
        float subLengthHeight = adjustedLengthHeight / (rayPrecisionHeight - 1);

        for (int i = 0; i < rayPrecisionBase; i++)
            relativeBasePoints[i] = new Vector2((subLengthBase * i) - formBounds.extents.x, formBounds.extents.y);

        for (int i = 0; i < rayPrecisionHeight; i++)
            relativeHeightPoints[i] = new Vector2(formBounds.extents.x, (subLengthHeight * i) - formBounds.extents.y);
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
        Vector2 dir = en.normalVector.normalized * -1f;
        float gravSpeed = (en.subAirTime * en.gravityCoeff);
        bool uncapped = en.maxGravitySpeed < 0 ? true : false;
        return dir * (uncapped ? gravSpeed : (gravSpeed > en.maxGravitySpeed ? en.maxGravitySpeed : gravSpeed));
    }

    /*
     * Change the Entity's normal vector. Rotate the entity appropriately, and recalculate what is "right."
     */
    public void ChangeNormalVector(Vector2 newNorm)
    {
        if (newNorm == Vector2.zero) {
            Debug.LogError("Attempted to set " + entityName + "'s normal vector to " + newNorm.ToString());
            return;
        }
        Vector2 normedNorm = newNorm.normalized;
        normalVector = normedNorm;
        rightVector = Vector2.Perpendicular(normalVector * -1f);
        gameObject.transform.rotation = Quaternion.Euler(0f, 0f, Vector2.Angle(Vector2.up, normedNorm));
    }

    /*================================================================================
     STATE BASED ACTION GENERALIZATION FUNCTIONS
     ================================================================================*/

    private void OnLanding()
    {
        state = EntityMotionState.GROUNDED;
        sbaOnLanding?.Invoke();
        ResetAirTime();
    }

    private void WhileInAir()
    {
        state = EntityMotionState.AIR;
        subAirTime += Time.deltaTime;
        sbaWhileInAir?.Invoke();
    }

    /*================================================================================
     UTILITY FUNCTIONS
     ================================================================================*/

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
     * Smoothly and gradually decay externalVelocity towards (0f, 0f).
     * Immediately set x or y to 0f if it is under the dampeningCutoff parameter.
     * This is done to obtain exactly 0f externalVelocity without having residual 1E-7 values.
     * Without dampeningCutoff, we get jittering effects when entities have externalVelocity.
     * This is called once per frame in an Entity's Update().
     */
    private void DecayExternalVelocity()
    {
        externalVelocity = Vector2.Lerp(externalVelocity, Vector2.zero, inertialDampening);
        //externalVelocity = Vector2.SmoothDamp(externalVelocity, Vector2.zero, ref externalVelocity, inertialDampening);

        //externalVelocity += new Vector2(
        //    externalVelocity.x - (inertialDampening * Time.deltaTime * Mathf.Sign(externalVelocity.x)),
        //    externalVelocity.y - (inertialDampening * Time.deltaTime * Mathf.Sign(externalVelocity.y))
        //);


        if (Mathf.Abs(externalVelocity.x) < dampeningCutoff) externalVelocity.x = 0f;
        //else
        //    if (Mathf.Abs(externalVelocity.x) - inertialDampening < 0f) externalVelocity.x = 0f;

        if (Mathf.Abs(externalVelocity.y) < dampeningCutoff) externalVelocity.y = 0f;
        //else
        //    if (Mathf.Abs(externalVelocity.y) - inertialDampening < 0f) externalVelocity.y = 0f;

    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        // First check if the Trigger entered is a hitbox.
        HitboxFramework en;
        en = collision.gameObject.GetComponent<HitboxFramework>();
        if (en == null) return;
        if (en.blacklist.Contains(faction)) return;
        en.OnHit(this);
    }

    /*
    private void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;
        for (int i = 0; i < rayPrecisionHeight; i++)
        {
            Gizmos.DrawSphere((Vector2)formBounds.center + relativeHeightPoints[i], 0.05f);
        }
        for (int i = 0; i < rayPrecisionBase; i++)
        {
            Gizmos.DrawSphere((Vector2)formBounds.center + relativeBasePoints[i], 0.05f);
        }
        for (int i = 0; i < rayPrecisionHeight; i++)
        {
            Gizmos.DrawSphere((Vector2)formBounds.center + (relativeHeightPoints[i] * -1f), 0.05f);
        }
        for (int i = 0; i < rayPrecisionBase; i++)
        {
            Gizmos.DrawSphere((Vector2)formBounds.center + (relativeBasePoints[i] * -1f), 0.05f);
        }
    }
    */
}
