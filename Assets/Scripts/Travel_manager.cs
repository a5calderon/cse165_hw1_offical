using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class TravelManager : MonoBehaviour
{
    [Header("References")]
    public Transform rightHandController;
    public Transform xrOrigin;
    public GameObject teleportIndicator;

    [Header("Arc Settings")]
    public float arcVelocity = 8f;
    public int   arcSegments = 20;
    public LineRenderer lineRenderer;

    [Header("XR Input Action")]
    public InputActionReference rightTriggerAction;

    private bool    validTarget       = false;
    private Vector3 teleportTarget;
    private bool    triggerWasPressed = false;

    // Layer mask that excludes the TeleportIndicator and UI layers
    // so the arc never "lands" on the indicator itself
    private int raycastMask;

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

    void Awake()
    {
        if (lineRenderer != null)
        {
            lineRenderer.positionCount = arcSegments;
            lineRenderer.startWidth    = 0.02f;
            lineRenderer.endWidth      = 0.01f;
        }
        if (teleportIndicator != null)
            teleportIndicator.SetActive(false);

        raycastMask = ~(LayerMask.GetMask("Ignore Raycast") | LayerMask.GetMask("UI"));
    }

    void Update()
    {
        if (lineRenderer == null || rightHandController == null) return;
        if (SpawnMenu.IsHoldingObject())
        {
            lineRenderer.enabled = false;
            if (teleportIndicator != null)
                teleportIndicator.SetActive(false);
            ReadTrigger(out _, out _);
            return;
        }

        DrawArc();
        CheckTrigger();
    }

    bool ReadButton()
    {
        if (rightTriggerAction != null && rightTriggerAction.action != null)
        {
            try   { return rightTriggerAction.action.ReadValue<float>() > 0.5f; }
            catch { return rightTriggerAction.action.IsPressed(); }
        }
        // Keyboard fallback for editor testing
        return Input.GetKey(KeyCode.F);
    }

    void ReadTrigger(out bool pressed, out bool down)
    {
        pressed           = ReadButton();
        down              = pressed && !triggerWasPressed;
        triggerWasPressed = pressed;
    }

    void DrawArc()
    {
        lineRenderer.positionCount = arcSegments;

        Vector3 startPos = rightHandController.position;
        Vector3 aimDir   = Quaternion.AngleAxis(-45f, rightHandController.right) * rightHandController.forward;
        Vector3 startVel = aimDir * arcVelocity;

        validTarget = false;
        if (teleportIndicator != null)
            teleportIndicator.SetActive(false);

        for (int i = 0; i < arcSegments; i++)
        {
            float   t     = i * 0.05f;
            Vector3 point = startPos + startVel * t + 0.5f * Physics.gravity * t * t;
            lineRenderer.SetPosition(i, point);

            if (i > 0)
            {
                float   tPrev     = (i - 1) * 0.05f;
                Vector3 prevPoint = startPos + startVel * tPrev + 0.5f * Physics.gravity * tPrev * tPrev;
                Vector3 dir       = point - prevPoint;

                // Use raycastMask so we never hit the TeleportIndicator or UI
                if (Physics.Raycast(prevPoint, dir.normalized, out RaycastHit hit, dir.magnitude, raycastMask))
                {
                    teleportTarget = hit.point;

                    if (teleportIndicator != null)
                    {
                        teleportIndicator.SetActive(true);
                        teleportIndicator.transform.position = hit.point + Vector3.up * 0.02f;
                        teleportIndicator.transform.rotation = Quaternion.Euler(0, rightHandController.eulerAngles.y, 0);
                    }

                    for (int j = i; j < arcSegments; j++)
                        lineRenderer.SetPosition(j, hit.point);

                    validTarget = true;
                    break;
                }
            }
        }
        lineRenderer.enabled = true;
    }

    void CheckTrigger()
    {
        ReadTrigger(out _, out bool triggerDown);
        if (triggerDown && validTarget)
            Teleport();
    }

    void Teleport()
    {
        if (xrOrigin == null) return;
        xrOrigin.position = new Vector3(teleportTarget.x, xrOrigin.position.y, teleportTarget.z);

        Vector3 forward = rightHandController.forward;
        forward.y = 0f;
        if (forward != Vector3.zero)
            xrOrigin.rotation = Quaternion.LookRotation(forward);

        if (teleportIndicator != null)
            teleportIndicator.SetActive(false);
    }
}