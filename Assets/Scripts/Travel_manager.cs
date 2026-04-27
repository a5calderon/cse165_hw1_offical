using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/*
GOAL : 
1. user aims a curved arc from right controller
2. where they would land is clear (teleport indicator)
3. pull left trigger = teleport
*/
public class TravelManager : MonoBehaviour
{
    // ── Inspector vars ──────────────────────────────────────────────

    [Header("References")]
    public Transform rightHandController;   
    public Transform xrOrigin;                  //whole player rig , what moves
    public GameObject teleportIndicator;        //visual marker

    [Header("Arc Settings")]
    public float arcVelocity = 8f;              //how far arc shoots
    public int   arcSegments = 20;              // smoothness
    public LineRenderer lineRenderer;           // visible arc line

    [Header("XR Input Action")]
    public InputActionReference rightTriggerAction;     
   
    // ── States ──────────────────────────────────────────────

    private bool validTarget = false;           // can they teleport here? 
    private Vector3 teleportTarget;             //where they'll land
    private bool triggerWasPressed = false;     //detecting press, 1st
    private int raycastMask;                    // surfaces allowed to hit (floor)


    /*
    for gameObject , script, and scene,
    do if not null
    */
    void OnEnable()
    {
        try { rightTriggerAction?.action.Enable(); }
        catch (System.Exception e) { Debug.LogWarning("[TravelManager] OnEnable error: " + e.Message); }
    }

    void OnDisable()
    {
        try { rightTriggerAction?.action.Disable(); }
        catch (System.Exception e) { Debug.LogWarning("[TravelManager] OnDisable error: " + e.Message); }
    }
    
    /*
    Initalization of arc 
    */
    void Awake()
    {
        //create arc, if not already
        if (lineRenderer != null)
        {
            lineRenderer.positionCount = arcSegments;
            //thickness of arc
            lineRenderer.startWidth    = 0.02f;
            lineRenderer.endWidth      = 0.01f;
        }
        //
        if (teleportIndicator != null)
            teleportIndicator.SetActive(false);

       // raycastMask = ~(LayerMask.GetMask("Ignore Raycast") | LayerMask.GetMask("UI"));   //valid surfaces , but includes furniture ( whic has colliders)--> moves furniture

       raycastMask = LayerMask.GetMask("Floor"); // only teleport on Floor Layer, but maybe will still allow Floor that objects are on top of? 
                                                //look into: maybe invisible surface under placed furniture, so that it is no longer on Floor??
    }

    void Update() //every frame
    {   
        if (lineRenderer == null || rightHandController == null) return;    //dont do anything 
        if (SpawnMenu.IsHoldingObject())  // disable/ hide arc if user trying to place object 
        {
            lineRenderer.enabled = false;
            if (teleportIndicator != null)
                teleportIndicator.SetActive(false);
            ReadTrigger(out _, out _);
            return;
        }
        //otherwise, continue/ call functions
        DrawArc();
        CheckTrigger();
    }

    /*
    checks if right trigger is being pressed. 
    returns true --> being pressed
    */
    bool ReadButton()
    {
        if (rightTriggerAction != null && rightTriggerAction.action != null)
        {
            try   { return rightTriggerAction.action.ReadValue<float>() > 0.5f; }   //>0.5 is considereed pressed
            catch { return rightTriggerAction.action.IsPressed(); }
        }
        // fallback for editor testing
        return Input.GetKey(KeyCode.F);
    }
    /*
    tracks button state over time
    */
    void ReadTrigger(out bool pressed, out bool down)
    {
        pressed = ReadButton();                //t or f : is it being pressed
        down = pressed && !triggerWasPressed; //rn its pressed and it wasnt last frame
        triggerWasPressed = pressed;         //stores this as pressed/ updates memory (for nexrt time)
    }

    /*
    GOAL:
    1. sim arc
    2. render it
    3. detect where it hits ground / target 
    
    look into future: left trigger activates arc, after a teleportation, arc hides until next left trigger
    */
    void DrawArc()
    {
        lineRenderer.positionCount = arcSegments;

        Vector3 startPos = rightHandController.position;                                                        //arc comes from controller
        Vector3 aimDir   = Quaternion.AngleAxis(-45f, rightHandController.right) * rightHandController.forward; //launch direction, tilts downward 45 deg but overall going forward direection
        Vector3 startVel = aimDir * arcVelocity;                                                                // how far it reaches

        // assumes not valid target and hides indicator at first
        validTarget = false;
        if (teleportIndicator != null)
            teleportIndicator.SetActive(false);

        /*
        arc sim 
        */
        for (int i = 0; i < arcSegments; i++) //point by point
        {
            // physics (projectile motion equation) , forward movement and gravity pulling down
            float t = i * 0.05f;
            Vector3 point = startPos + startVel * t + 0.5f * Physics.gravity * t * t;   //calc current point
            lineRenderer.SetPosition(i, point); //draw arc visually

            if (i > 0)
            {
                float tPrev = (i - 1) * 0.05f;

                Vector3 prevPoint = startPos + startVel * tPrev + 0.5f * Physics.gravity * tPrev * tPrev;   //calc previous point

                Vector3 dir  = point - prevPoint;  // calc direction ray, subtract previous from current point

                // Use raycastMask so never hit the TeleportIndicator or UI
                //ray between the 2 points (using dir magnitude), if it hits allowed surface 
                if (Physics.Raycast(prevPoint, dir.normalized, out RaycastHit hit, dir.magnitude, raycastMask))
                {
                    teleportTarget = hit.point; //then found valid teleport spot. save location

                    //now show teleport indicator
                    if (teleportIndicator != null)
                    {
                        teleportIndicator.SetActive(true);
                        teleportIndicator.transform.position = hit.point + Vector3.up * 0.02f;  //positionn slightly above ground
                        teleportIndicator.transform.rotation = Quaternion.Euler(0, rightHandController.eulerAngles.y, 0); //indicator "faces" same directon as controller . needed?
                    }
                    //stop arc at collision point/ hit point. dont continue into/ under floor
                    for (int j = i; j < arcSegments; j++)
                        lineRenderer.SetPosition(j, hit.point);
                    //update and set as valid teleportation location
                    validTarget = true;
                    break;
                }
            }
        }
        lineRenderer.enabled = true;
    }

    /*
    final check: teleport if user just pressed the right trigger and if location is a valid target
    */
    void CheckTrigger()
    {
        ReadTrigger(out _, out bool triggerDown); //dont care about pressed val here
        if (triggerDown && validTarget)
            Teleport();
    }

    void Teleport()
    {
        if (xrOrigin == null) return; //if player rig not exist, return

        // move player position
        xrOrigin.position = new Vector3(teleportTarget.x, xrOrigin.position.y, teleportTarget.z); // chnage x and z (move along the plane), not y bc height doesnt need to change
        
        //direction they face after
        Vector3 forward = rightHandController.forward;
        forward.y = 0f; // dont account for looking up or down. this should only consider moving along plane

        if (forward != Vector3.zero) // if direction valid
            xrOrigin.rotation = Quaternion.LookRotation(forward); //apply rotation on player, so they face where they were pointing

        if (teleportIndicator != null) //hide indicator
            teleportIndicator.SetActive(false);
    }
}