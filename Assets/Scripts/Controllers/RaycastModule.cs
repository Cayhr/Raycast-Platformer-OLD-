using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/*
 * The heart of the movement in GunNagi: 2D Raycasting.
 * Every EntityController script has a RaycastModule in it that stores information specific to that Entity's currently assumed form.
 * The form is just the BoxCollider2D and associated graphics information (anything else inside the GameObject that makes its
 *      representation in the game world pretty such as lights, sprites, particle generators, and sound sources).
 * The form is set by the EntityController itself, initializing it to its initially designated first form.
 */
public class RaycastModule
{
    // CONSTANTS AND IMPORTANT INFO
    private const int NUM_CORNERS = 4;
    private const int TL = 0;
    private const int TR = 1;
    private const int BL = 2;
    private const int BR = 3;
    protected static readonly float RAYCAST_INLET = 0.015f;
    private static readonly int MAXIMUM_RAYCASTS = 5;

    // REFERENCES TO ENTITY CONTROLLER
    private readonly EntityController owner;

    // DEFINED ON FORM CHANGE
    private Vector2[] relativeCornerPos; //cornerTL, cornerTR, cornerBL, cornerBR;
    private Vector2[] relativeBasePoints;
    private Vector2[] relativeHeightPoints;
    private GameObject formObject;
    private BoxCollider2D formCollider;
    private Bounds formBounds;
    private int rayPrecisionBase, rayPrecisionHeight;
    private float baseSpacing, heightSpacing;

    // RUNTIME VARIABLES
    private Vector2[] currentActiveRelativeOrigins;
    private Vector2 currentVelocitySigns = Vector2.zero;

    public RaycastModule(EntityController en) { owner = en; }

    public RaycastModule(EntityController en, GameObject formObj, int _basePrecision, int _heightPrecision)
    {
        owner = en;
        SetFormForRaycastModule(formObj, _basePrecision, _heightPrecision);
    }

    /*
     * DO NOT CALL FROM OUTSIDE OF THIS CLASS. ONLY USE ENTITYCONTROLLER'S CHANGEFORM FUNCTION.
     * This function is exposed because EntityController.ChangeForm() calls this function when you call that one.
     * The form stuff is important only to the RaycastModule since it contains BoxCollider2D information.
     * The form object stays pretty and is remembered in EntityController, while the backend info is sent to the RaycastModule.
     */
    public void SetFormForRaycastModule(GameObject formObj, int _basePrecision, int _heightPrecision)
    {
        BoxCollider2D collider = formObj.GetComponent<BoxCollider2D>();
        if (collider == null)
        {
            Debug.LogError("No BoxCollider2D assocated with the formObject " + formObj + " on Entity " + owner);
            return;
        }
        formCollider = collider;
        formObject = formObj;
        formBounds = formCollider.bounds;

        /* ================================================================================ 
           Update Raycast information
           ================================================================================ */
        // Reallocate the bound points in memory.
        // We must have at least 2 rays for the edges of the BoxCollider's face.
        const int MIN_RAYS = 2;
        rayPrecisionBase = Mathf.Clamp(_basePrecision, MIN_RAYS, int.MaxValue);
        rayPrecisionHeight = Mathf.Clamp(_heightPrecision, MIN_RAYS, int.MaxValue);
        relativeCornerPos = new Vector2[NUM_CORNERS];
        relativeBasePoints = new Vector2[rayPrecisionBase];
        relativeHeightPoints = new Vector2[rayPrecisionHeight];

        // Calculate left to right and bottom to top the colinear points for X and Y respectively.
        // We shrink the form's Bounds by the RAYCAST_INLET.
        formBounds.Expand(RAYCAST_INLET * -2f);
        Vector2 boxSize = formBounds.size;

        // Record the relative corners. We only need TopLeft and BottomRight (we can reflect them based on velocity).
        relativeCornerPos[BL] = (Vector2)formBounds.min - (Vector2)formBounds.center;
        relativeCornerPos[BR] = new Vector2(formBounds.max.x, formBounds.min.y) - (Vector2)formBounds.center;
        relativeCornerPos[TL] = new Vector2(formBounds.min.x, formBounds.max.y) - (Vector2)formBounds.center;
        relativeCornerPos[TR] = (Vector2)formBounds.max - (Vector2)formBounds.center;

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

    /*
     * The only exposed method that directly moves the Entity.
     * Call only when an EntityController.ChangeForm() is called.
     * <CURRENT>: Each EntityController calls RaycastMovement() and passes in the Entity's current velocity,
     *  which is calculated via CompoundVelocities() method in their own Update() function.
     * <LATER>: Consolidate all Entities into an object pool, and manage their instantiation/update calls from that "main thread."
     */
    public Vector2 RaycastMovement(Vector2 velocity)
    {
        // Confirm we can move
        if (formObject != null)
        {
            //Debug.Log("TL: " + currentCornerChecks[TL] + ", TR: " + currentCornerChecks[TR] + ", BL: " + currentCornerChecks[BL] + ", BR: " + currentCornerChecks[BR]);
            //Vector2[] relativeOrigins = CornersToCastFrom(cornerChecks);
            int recursions = 0;
            Vector2 iterationStep = Vector2.zero;
            Vector2 toMove = RaycastVelocity(velocity, ref recursions, ref iterationStep, false);
            return toMove;
        }
        else
        {
            Debug.LogError("Cannot move " + owner + "; has no form defined.");
            return Vector2.zero;
        }
    }

    /*
     * Given the velocity to raycast for the Entity, figure out which sides are activated.
     * e.g.: if the velocity is (0f, 1f), only the top face's points are activated to raycast from.
     *       if the velocity is (1f, 1f), the top and right face's points are activated.
     */
    private Vector2[] ActiveSides(Vector2 velocity)
    {
        // If our velocity would activate the same sides of the box collider, skip the calculations.
        Vector2 newVelocityDir = VelocityDirections(velocity);
        if (newVelocityDir == currentVelocitySigns) return currentActiveRelativeOrigins;
        currentVelocitySigns = newVelocityDir;

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
        Vector2 formCenter = formObject.transform.position;

        // Calculate the points from which to raycast from.
        Vector2[] points = new Vector2[count];

        // iPoints will be used to iterate through the final array. It will go Corners > X points > Y points.
        int iPoints = 0;

        // Start with the corners.
        for (int i = 0; i < NUM_CORNERS; i++)
            if (cornerCheck[i])
            {
                // Idk why but we need to index the relativeCornerPos's backwards to make it align properly.
                points[iPoints] = relativeCornerPos[i];
                //if (DEBUG) Debug.DrawRay(points[iPoints], unitDir * velocityMag, Color.white);
                iPoints++;
            }

        // Intermediary X faces;
        bool flipX = Mathf.Sign(velocity.x) > 0f ? true : false;
        if (velocity.x != 0f)
            for (int i = 0; i < rayPrecisionHeight - 2; i++)
            {
                points[iPoints] = (flipX ? relativeCornerPos[BR] : relativeCornerPos[BL]) + (Vector2.up * heightSpacing * (i + 1));
                //points[iPoints] = formCenter + new Vector2(relativeCornerPos[TL].x * flipX, relativeCornerPos[TL].y + heightSpacing * (i + 1));
                iPoints++;
            }

        // Intermediary Y faces;
        //float flipY = Mathf.Sign(velocity.y);
        bool flipY = Mathf.Sign(velocity.y) > 0f ? true : false;
        if (velocity.y != 0f)
            for (int i = 0; i < rayPrecisionBase - 2; i++)
            {
                points[iPoints] = (flipY ? relativeCornerPos[TL] : relativeCornerPos[BL]) + (Vector2.right * baseSpacing * (i + 1));
                //points[iPoints] = formCenter + new Vector2(relativeCornerPos[BR].x + (baseSpacing * (i + 1)), relativeCornerPos[BR].y * flipY);
                iPoints++;
            }

        currentActiveRelativeOrigins = points;
        return points;
    }

    /*
     * Returns a Vector2 where the values of X and Y are either -1f, 0f, or 1f.
     * These dictate what direction the player is moving in, and to activate which sides of the collider.
     */
    private Vector2 VelocityDirections(Vector2 velocity)
    {
        return new Vector2( (velocity.x != 0f) ? Mathf.Sign(velocity.x) : 0f,
                            (velocity.y != 0f) ? Mathf.Sign(velocity.y) : 0f );
    }

    /*
     * 
     */
    private Vector2 RaycastVelocity(Vector2 velocity, ref int recursions, ref Vector2 iterationStep, bool cornered)
    {

        // raycastTangentHit is applied if our closest delta is less than or equal to RAYCAST_INLET.
        // In a situation where the entity is being raycasted into a concave surface (V shaped geometry),
        // eventually we will reach a point where a recursive raycast further into the corner will make
        // connections that result in remainingVelocity unchanging.
        bool raycastTangentHit = false;

        // If we aren't moving, save computation cycles by not doing anything.
        if (velocity == Vector2.zero) return Vector2.zero;

        // Because this solution is recursive, we do need to recalculate the raycast origins each time.
        // Though, the checking is optimized to skip unnecessary calculations.
        Vector2[] origins = ActiveSides(velocity);

        // Cache a few variables. NormDir = normalized velocity vector, formCenter = world position of form's center.
        // Unfortunately, we cannot optimize by using sqrMagnitude instead, since we need a proper length in the world.
        float velocityMag = velocity.magnitude;
        Vector2 unitDir = velocity.normalized;
        Vector2 formCenter = formObject.transform.position;

        // If the distance to cast is negative, turn it positive. Also compensate for RAYCAST_INLET.
        float castDist = Mathf.Abs(velocity.magnitude) + RAYCAST_INLET;
        if (castDist <= RAYCAST_INLET) castDist = RAYCAST_INLET + RAYCAST_INLET;

        float closestDelta = castDist;
        // ClosestHit is initialized to an empty RaycastHit2D, but if it ends up being unused we will skip.
        RaycastHit2D closestHit = new RaycastHit2D();
        int hits = 0;

        // Raycast from every corner and get the closest collision information.
        for (int i = 0; i < origins.Length; i++)
        {
            Vector2 origin = formCenter + iterationStep + origins[i];
            RaycastHit2D currentHit = Physics2D.Raycast(origin, unitDir, castDist, LayerInfo.OBSTACLES);

            // If the raycast did not hit a collider: Skip to the next raycast.
            if (!currentHit)
            {
                            //if (DEBUG) Debug.DrawRay(origin + iterationStep, unitDir * castDist, Color.white);
                continue;
            }

            // If this is a hit we need to keep counting on.
            hits++;

            // If the raycast closestHit a collider: Find the point of contact, but skip if we closestHit something further than a previous raycast.
                        //if (DEBUG) Debug.DrawRay(origin + iterationStep, unitDir * currentHit.distance, Color.red);
            float hitDist = currentHit.distance - RAYCAST_INLET;
            if (hitDist > closestDelta) continue;
            if (hitDist <= 0f)
            {
                raycastTangentHit = true;
            }
            //if (currentHit.distance == 0) continue;

            // If we pass the two previous checks, increment our raycast hit counter `hits` and then check if it was the new closestDelta.
            if (hitDist < closestDelta)
            {
                closestHit = currentHit;
                closestDelta = hitDist;
            }
        }

        // If the previous raycast iteration got stuck and this one got stuck too, that means we've hit maximum depth of a concave inlet.
        if (raycastTangentHit && cornered)
        {
            owner.ConcaveLanding(velocity);
                        //Debug.Log("Hit maximum depth in a concave surface");
            return Vector2.zero;
        }

        Vector2 toMove = closestDelta * unitDir;
        iterationStep += toMove;

        // If after raycasting we did not get any hits, we can just stop.
        if (hits == 0 || closestHit.collider == null) return toMove;

        // Stop recursion if we are going too far.
        if (recursions >= MAXIMUM_RAYCASTS) return Vector2.zero;

        // If we ended up getting collisions: calculate the angle we hit the surface at.
        float remainingVelocity = velocityMag - closestDelta;
        if (remainingVelocity <= 0f) return toMove;

        // Figure out along which direction of the surface should we move along it?
        Vector2 unitAlongSurface = (Vector2.Perpendicular(closestHit.normal)).normalized;
        float angleBetweenVelocityAndNormal = Vector2.SignedAngle(closestHit.normal, velocity);
        float surfaceNormSide = 0f;

        // If the velocity and normal vectors are exactly opposite sides, cancel the forces.
        if (Mathf.Abs(angleBetweenVelocityAndNormal) >= 180f) surfaceNormSide = 0f;
        else if (angleBetweenVelocityAndNormal < 0f) surfaceNormSide = -1f;
        else if (angleBetweenVelocityAndNormal > 0f) surfaceNormSide = 1f;

        // Deflect the unit vector along the surface of contact in the direction we find.
        unitAlongSurface *= surfaceNormSide;

                    //Debug.DrawRay(closestHit.point, closestHit.normal, Color.yellow, 1f);
                    //Debug.DrawRay(closestHit.point, velocity.normalized, Color.blue, 1f);
                    //Debug.DrawRay(closestHit.point, deflectionVector, Color.green, 1f);
                    //Debug.Log("Angle between velocity and surface normal: " + angleBetweenVelocityAndNormal);
                    //Debug.Log(vectorDiff);
                    //Debug.DrawRay(closestHit.point, unitAlongSurface, Color.magenta, 1f);
                    //Debug.Log("Velocity) " + Mathf.Sin(velocity.x) + ", " + Mathf.Sin(velocity.y));

        // Calculate the vector projection of the velocity vector onto the unit vector along the surface.
        // VDotS = Dot product between <velocity vector> and <unit vector along surface>.
        // SDotS = Dot product between <unit vector along surface> and itself.
        // OPTIMIZATION: SDotS will always be 1 unless we are hitting the surface at a perfect perpendicular angle.
        //               We can skip division by SDotS and check if VDotS is 0 instead of SDotS.
        float VDotS = Vector2.Dot(velocity.normalized, unitAlongSurface);
                    //float SDotS = Vector2.Dot(unitAlongSurface, unitAlongSurface);
                    //Debug.Log("V*S = " + VDotS + ", S*S = " + SDotS);

        // Vector projection formula.
        Vector2 pVonS = (VDotS != 0f) ? (VDotS) * unitAlongSurface : Vector2.zero;

                    //Debug.DrawRay(closestHit.point, pVonS, Color.magenta);
                    //Debug.Log(pVonS);
                    //Debug.Break();

        // Preserve velocity in that direction.
        Vector2 extraMovementNeeded = pVonS * remainingVelocity;

        if (!(recursions > 0)) owner.OnContactWithSurface(closestHit, velocity, surfaceNormSide);

                    //Debug.Log("Extra movement: " + extraMovementNeeded + ", Recursion#: " + recursions);
                    //Debug.Break();

        // If we hit a surface, we need to propagate movement again in that direction.
        // Return the Vector2 for movement along the slope, if necessary.
        recursions++;
        if (extraMovementNeeded == Vector2.zero) return toMove;
        return toMove + RaycastVelocity(extraMovementNeeded, ref recursions, ref iterationStep, raycastTangentHit);

    }

    /*
     * Simply BoxCast downwards to see if we have ground beneath us. If this check fails, enter the air.
     */
    public bool CheckGround()
    {
        RaycastHit2D boxHit = Physics2D.BoxCast(formObject.transform.position, formBounds.size, 0f, Vector2.down, RAYCAST_INLET * 2f, LayerInfo.OBSTACLES);
        return (!boxHit) ? false : true;
    }

    /*
     * Returns a Vector2 rotated counter-clockwise by an angle.
     * Mathematically, it is simply [2x2 rotation matrix][vec]'.
     * [x'] = [cos&, -sin&][x]
     * [y']   [sin&,  cos&][y]
     */
    public static Vector2 RotateVector(Vector2 vec, float angle)
    {
        return new Vector2((vec.x * Mathf.Cos(angle)) - (vec.y * Mathf.Sin(angle)), (vec.x * Mathf.Sin(angle)) + (vec.y * Mathf.Cos(angle)));
    }
}
