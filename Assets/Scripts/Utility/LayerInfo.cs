using UnityEngine;

public class LayerInfo
{
    public static LayerMask TERRAIN = LayerMask.GetMask("Terrain");
    public static LayerMask ENTITIES = LayerMask.GetMask("Entities");
    public static LayerMask HITBOXES = LayerMask.GetMask("Hitboxes");
}
