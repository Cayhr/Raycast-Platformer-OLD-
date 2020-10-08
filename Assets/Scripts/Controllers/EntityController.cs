using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public delegate Vector2 VelocityCompoundMethod();
public delegate void StateBasedAction();
public enum FactionList { NEUTRAL, PLAYER, ENEMIES};
public enum EntityMotionState { GROUNDED, AIR, CLUTCH };

/*
 * Forms are listed in the EntityController by a list, which is set by the developer in the Unity Inspector.
 * Changing forms, however, go down to any logic that utilizes the EntityController.
 */
public class EntityController : MonoBehaviour
{
    private Rigidbody2D rb;
    private RaycastModule rcModule;
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

    [Header("Runtime Statistics")]
    public EntityMotionState state;
    public Vector2 currentVelocity = Vector2.zero;
    public Vector2 externalVelocity = Vector2.zero;
    public Vector2 inclineVelocity = Vector2.zero;
    public bool facingRight = true;

    [Header("Physics Parameters")]
    public bool gravityOn;
    public float gravityCoeff, maxGravitySpeed, inertialDampening, dampeningCutoff, maxClimbAngle, knockbackResistance;
    [SerializeField] private int rayPrecisionBase, rayPrecisionHeight;    // The amount of extra points BETWEEN the 2 on the edges.

    [Header("Runtime Trackers")]
    public float subAirTime = 0;
    public float totalAirTime = 0;
    public Vector2 directionalInfluence = Vector2.zero;
    private Vector2 forNextFrame = Vector2.zero;

    // Raycasting Variables

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        state = EntityMotionState.AIR;
        rcModule = new RaycastModule(this);
        ChangeForm(formIndex);
    }

    private void Start()
    {
        //mainCollider = GetComponent<CircleCollider2D>();
        //headCollider = GetComponent<BoxCollider2D>();
        if (forms.Count == 0)
        {
            Debug.LogError(gameObject + " has no forms.");
            gameObject.SetActive(false);
        }
        //ChangeNormalVector(normalVector);
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
        // Translate the entity in the direction calculated last frame.
        transform.Translate(forNextFrame);

        // First check which way we are facing based on DI.
        if (directionalInfluence.x > 0) facingRight = true;
        else if (directionalInfluence.x < 0) facingRight = false;

        switch (state)
        {
            case EntityMotionState.AIR:
                WhileInAir();
                break;
            case EntityMotionState.GROUNDED:
                if (!rcModule.CheckGround()) state = EntityMotionState.AIR;
                break;
            default:
                break;
        }

        // First calculate the final velocity of the entity.
        currentVelocity = CompoundVelocities(vOverride, vAdd, vMult);

        // Do our Raycast Movement logic.
        forNextFrame = rcModule.RaycastMovement(currentVelocity * Time.deltaTime);

        Physics2D.SyncTransforms();

        DecayExternalVelocity();
    }

    /*
     * Returns the angle of the Vector2 in *normal* 2D Euclidean space.
     */
    private float VectorAngle(Vector2 vec)
    {
        return Mathf.Atan2(vec.y, vec.x);
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
        rcModule.SetFormForRaycastModule(forms[index], rayPrecisionBase, rayPrecisionHeight);
        formIndex = index;
    }

    private Vector2 CompoundVelocities(VelocityCompoundMethod over, VelocityCompoundMethod add, VelocityCompoundMethod mult)
    {
        Vector2 final = (over == null) ? Vector2.zero : over();
        if (final != Vector2.zero) return final;

        if (add != null) final += add();
        if (gravityOn) final += CalculateGravityVector(this);
        final += externalVelocity;
        if (inclineVelocity != Vector2.zero) final = inclineVelocity * final.magnitude;
        if (mult != null) final.Scale(mult());

        return final;
    }

    /*
     * Can also be used externally to find the gravity vector of that entity.
     */
    public Vector2 CalculateGravityVector(EntityController en)
    {
        Vector2 dir = Vector2.down;
        float gravSpeed = (en.subAirTime * en.gravityCoeff);
        bool uncapped = en.maxGravitySpeed < 0 ? true : false;
        return dir * (uncapped ? gravSpeed : (gravSpeed > en.maxGravitySpeed ? en.maxGravitySpeed : gravSpeed));
    }

     #region Raycast Collision-Moment Functions

    /*
     * incomingVelocity may or not be equal to currentVelocity.
     * When a surface is hit, it could be redirected along that surface.
     */
    public void OnContactWithSurface(RaycastHit2D surfaceHit, Vector2 incomingVelocity, float surfaceNormSide)
    {
        float slopeNormal2OurNormal = Vector2.Angle(surfaceHit.normal, Vector2.up);
        // If the angle of the surface hit is less than our climbing angle (we can stand on it), enter GroundedState.
        if (Mathf.Abs(slopeNormal2OurNormal) < maxClimbAngle)
        {
            CheckForLanding(incomingVelocity);
        }
        // If the slope is too great and we are trying to go up it.
        else
        {
            if (surfaceNormSide != 0f) state = EntityMotionState.AIR;
        }
    }

    public void ConcaveLanding(Vector2 incomingVelocity)
    {
        CheckForLanding(incomingVelocity);
    }

    private void CheckForLanding(Vector2 incomingVelocity)
    {
        // If we had negative velocity before colliding with said slope.
        if (incomingVelocity.y < 0f)
        {
            externalVelocity.y = 0f;
            if (state != EntityMotionState.GROUNDED)
            {
                state = EntityMotionState.GROUNDED;
                OnLanding();
            }
        }
    }

    #endregion

    #region State-Based-Action Generalization Functions

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

    #endregion

    #region Utility Functions

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

    public void ApplyVelocity(Vector2 dir, float mag)
    {
        // If we pass in a negative velocity, we absVal it and invert the dir vector.
        bool invert = false;
        float finalMag = mag;
        if (finalMag < 0f)
        {
            finalMag = Mathf.Abs(mag);
            invert = true;
        }
        float adjustedMag = finalMag - knockbackResistance;
        adjustedMag = Mathf.Clamp(adjustedMag, 0f, float.MaxValue);
        if (adjustedMag > 0f) externalVelocity += (invert ? -dir : dir)  * finalMag;
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

    #endregion

    private void OnTriggerEnter2D(Collider2D collision)
    {
        // First check if the Trigger entered is a hitbox.
        HitboxBase hb;
        hb = collision.gameObject.GetComponent<HitboxBase>();
        if (hb == null) return;
        if (hb.blacklist.Contains(faction)) return;
        hb.OnHit(this);
    }
}
/*
    *
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
     *
    private int RaycastX(Vector2 normDir, float rawVelocity, ref Vector2 finalVel)
    {
        bool DEBUG = true;
        // If we aren't moving, save computation cycles by not doing anything.
        if ((normDir * rawVelocity) == Vector2.zero) return 0;
        // If the distance to cast is negative, turn it positive. Also compensate for RAYCAST_INLET.
        float castDist = Mathf.Abs(rawVelocity) + RAYCAST_INLET;
        if (castDist <= RAYCAST_INLET) castDist = RAYCAST_INLET;

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
                    //Debug.Log(slopeAngle);
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
        if (castDist <= RAYCAST_INLET) castDist = RAYCAST_INLET;

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
*/
