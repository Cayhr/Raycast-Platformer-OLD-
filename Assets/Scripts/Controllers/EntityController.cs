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
    public Vector2 inclineVelocity = Vector2.zero;
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
    const int NUM_CORNERS = 4;
    const int TL = 0;
    const int TR = 1;
    const int BL = 2;
    const int BR = 3;
    private Vector2[] relativeCornerPos; //cornerTL, cornerTR, cornerBL, cornerBR;
    private float baseSpacing, heightSpacing;
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
        // First check which way we are facing based on DI.
        if (directionalInfluence.x > 0) facingRight = true;
        else if (directionalInfluence.x < 0) facingRight = false;

        if (state == EntityMotionState.AIR) WhileInAir();

        // First calculate the final velocity of the entity.
        currentVelocity = CompoundVelocities(vOverride, vAdd, vMult);

        // Do our Raycast Movement logic.
        RaycastMovement();

        DecayExternalVelocity();
    }

    private void RaycastMovement()
    {
        Vector2 scaledVelocity = currentVelocity * Time.deltaTime;

        Vector2 forNextFrame = Vector2.zero;
        RaycastVelocity(scaledVelocity);
        inclineVelocity = forNextFrame;

        // Sync after every transform translation.
        Physics2D.SyncTransforms();

    }

    private void RaycastVelocity(Vector2 velocity)
    {
        bool DEBUG = true;
        // If we aren't moving, save computation cycles by not doing anything.
        if (velocity == Vector2.zero) return;

        // Cache a few variables. NormDir = normalized velocity vector, formCenter = world position of form's center.
        float velocityMag = velocity.magnitude;
        Vector2 unitDir = velocity.normalized;
        Vector2 formCenter = formObject.transform.position;

        // Calculate the faces that will be raycasted from.
        // Start with the corners and check for overlap.
        bool[] cornerCheck = { false, false, false, false };
        int countedCorners = 0;
        if (velocity.x > 0f)
            cornerCheck[TR] = cornerCheck[BR] = true;
        else if (velocity.x < 0f)
            cornerCheck[TL] = cornerCheck[BL] = true;

        if (velocity.y > 0f)
            cornerCheck[TL] = cornerCheck[TR] = true;
        else if (velocity.y < 0f)
            cornerCheck[BL] = cornerCheck[BR] = true;

        for (int i = 0; i < NUM_CORNERS; i++)
            countedCorners += (cornerCheck[i]) ? 1 : 0;
        int count = countedCorners;

        // Now we add in the points along the faces, excluding the corners.
        if (velocity.x != 0f) count += (rayPrecisionHeight - 2);
        if (velocity.y != 0f) count += (rayPrecisionBase - 2);

        // Calculate the points from which to raycast from.
        Vector2[] points = new Vector2[count];

        // iPoints will be used to iterate through the final array. It will go Corners > X points > Y points.
        int iPoints = 0;

        // Start with the corners.
        for (int i = 0; i < NUM_CORNERS; i++)
            if (cornerCheck[i])
            {
                // Idk why but we need to index the relativeCornerPos's backwards to make it align properly.
                points[iPoints] = formCenter + relativeCornerPos[NUM_CORNERS - 1 - i];
                iPoints++;
            }

        // Intermediary X faces;
        float flipX = Mathf.Sign(velocity.x);
        if (velocity.x != 0f)
            for (int i = 0; i < rayPrecisionHeight - 2; i++)
            {
                points[iPoints] = formCenter + new Vector2(relativeCornerPos[TL].x * flipX, relativeCornerPos[TL].y + heightSpacing * (i + 1));
                iPoints++;
            }

        // Intermediary Y faces;
        float flipY = Mathf.Sign(velocity.y);
        if (velocity.y != 0f)
            for (int i = 0; i < rayPrecisionBase - 2; i++)
            {
                points[iPoints] = formCenter + new Vector2(relativeCornerPos[BR].x + (baseSpacing * (i + 1)), relativeCornerPos[BR].y * flipY);
                iPoints++;
            }

        // If the distance to cast is negative, turn it positive. Also compensate for RAYCAST_INLET.
        float castDist = Mathf.Abs(velocity.magnitude) + RAYCAST_INLET;
        if (castDist <= RAYCAST_INLET) castDist = RAYCAST_INLET + RAYCAST_INLET;

        float closestDelta = castDist;
        // ClosestHit is initialized to an empty RaycastHit2D, but if it ends up being unused we will skip.
        RaycastHit2D closestHit = new RaycastHit2D();
        int hits = 0;

        // Raycast from every point and get the closest collision information.
        for (int i = 0; i < count; i++)
        {
            Vector2 origin = points[i];
            RaycastHit2D currentHit = Physics2D.Raycast(origin, unitDir, castDist, LayerInfo.OBSTACLES);

            // If the raycast did not hit a collider: Skip to the next raycast.
            if (!currentHit)
            {
                if (DEBUG) Debug.DrawRay(origin, unitDir * castDist, Color.white);
                continue;
            }

            // If this is a hit we need to keep counting on.
            hits++;

            // If the raycast closestHit a collider: Find the point of contact, but skip if we closestHit something further than a previous raycast.
            if (DEBUG) Debug.DrawRay(origin, unitDir * currentHit.distance, Color.red);
            float hitDist = currentHit.distance - RAYCAST_INLET;
            if (hitDist > closestDelta) continue;
            if (currentHit.distance == 0) continue;

            // If we pass the two previous checks, increment our raycast hit counter `hits` and then check if it was the new closestDelta.
            if (hitDist < closestDelta)
            {
                castDist = hitDist;
                closestHit = currentHit;
                closestDelta = hitDist;
            }
        }

        Vector2 toMove = closestDelta * unitDir;
        if (toMove == Vector2.zero) return;
        transform.Translate(toMove);

        // If after raycasting we did not get any hits, we can just stop.
        if (hits == 0) return;
        if (closestHit.collider == null) return;

        // If we ended up getting collisions: calculate the angle we hit the surface at.
        float slopeNormal2OurNormal = Vector2.Angle(closestHit.normal, normalVector);
        float remainingVelocity = velocityMag - closestDelta;

        // Figure out along which direction of the surface should we move along it?
        Vector2 vectorDiff = (velocity.normalized + closestHit.normal);
        Vector2 unitAlongSurface = (Vector2.Perpendicular(closestHit.normal)).normalized;
        float angleBetweenVelocityAndNormal = Vector2.SignedAngle(closestHit.normal, velocity);
        float surfaceNormSide = 0f;

        // If the velocity and normal vectors are exactly opposite sides, cancel the forces.
        if (angleBetweenVelocityAndNormal == 180f) surfaceNormSide = 0f;
        else if (angleBetweenVelocityAndNormal < 0f) surfaceNormSide = -1f;
        else if (angleBetweenVelocityAndNormal > 0f) surfaceNormSide = 1f;

        // Reflect the unit vector along the surface of contact in the direction we find.
        unitAlongSurface *= surfaceNormSide;

        Debug.DrawRay(closestHit.point, closestHit.normal, Color.yellow);
        Debug.DrawRay(closestHit.point, velocity.normalized, Color.blue);
        Debug.DrawRay(closestHit.point, vectorDiff, Color.green);
        Debug.Log("Angle between velocity and surface normal: " + angleBetweenVelocityAndNormal);
        //Debug.Log(vectorDiff);
        Debug.DrawRay(closestHit.point, unitAlongSurface, Color.magenta);
        //Debug.Break();

        // Scale against 
        float angleBetweenVelocityAndSurfaceUnit = Vector2.SignedAngle(unitAlongSurface, velocity);

        // Preserve velocity in that direction.
        Vector2 extraMovementNeeded = unitAlongSurface * remainingVelocity;

        // If the angle of the surface hit is less than our climbing angle (we can stand on it), enter GroundedState.
        if (Mathf.Abs(slopeNormal2OurNormal) < maxClimbAngle)
        {
            // If we had negative velocity before colliding with said slope.
            if (velocity.y < 0f)
            {
                externalVelocity.y = 0f;
                if (state != EntityMotionState.GROUNDED)
                {
                    state = EntityMotionState.GROUNDED;
                    OnLanding();
                }
            }
            //If we were just moving and we hit a slope we can climb up.
        }
        // If the slope is too great and we cannot climb it.
        else
        {
        }

        // If we hit a surface, we need to propagate movement again in that direction.
        RaycastVelocity(extraMovementNeeded);

        // Return the Vector2 for movement along the X axis of the entity.
        return;
    }

    /*
     * Returns the angle of the Vector2 in *normal* 2D Euclidean space.
     */
    private float VectorAngle(Vector2 vec)
    {
        return Mathf.Atan2(vec.y, vec.x);
    }

    /*
     * Returns a Vector2 rotated counter-clockwise by an angle.
     * Mathematically, it is simply [2x2 rotation matrix][vec]'.
     * [x'] = [cos&, -sin&][x]
     * [y']   [sin&,  cos&][y]
     */
    private Vector2 RotateVector(Vector2 vec, float angle)
    {
        return new Vector2((vec.x * Mathf.Cos(angle)) - (vec.y * Mathf.Sin(angle)), (vec.x * Mathf.Sin(angle)) + (vec.y * Mathf.Cos(angle)));
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
        relativeCornerPos = new Vector2[NUM_CORNERS];
        relativeBasePoints = new Vector2[rayPrecisionBase];
        relativeHeightPoints = new Vector2[rayPrecisionHeight];

        // Calculate left to right and bottom to top the colinear points for X and Y respectively.
        // We shrink the form's Bounds by the RAYCAST_INLET.
        formBounds.Expand(RAYCAST_INLET * -2f);

        // Record the relative corners. We only need TopLeft and BottomRight (we can reflect them based on velocity).
        Vector2 boxSize = formBounds.size;
        relativeCornerPos[BL] = (Vector2)formBounds.center - (Vector2)formBounds.min;
        relativeCornerPos[BR] = (Vector2)formBounds.center - new Vector2(formBounds.max.x, formBounds.min.y);
        relativeCornerPos[TL] = (Vector2)formBounds.center - new Vector2(formBounds.min.x, formBounds.max.y);
        relativeCornerPos[TR] = (Vector2)formBounds.center - (Vector2)formBounds.max;

        baseSpacing = boxSize.x / (rayPrecisionBase - 1);
        heightSpacing = boxSize.y / (rayPrecisionHeight - 1);

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
        if (inclineVelocity != Vector2.zero) final = inclineVelocity * final.magnitude;
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

    private void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;
        /*
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
        */
        //Gizmos.DrawSphere((Vector2)formBounds.min, .05f);
        //Gizmos.DrawSphere((Vector2)formBounds.max, .05f);
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
