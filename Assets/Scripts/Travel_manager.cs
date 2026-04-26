using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

public class TravelManager : MonoBehaviour
{
    [Header("References")]
    public Transform rightHandController;
    public Transform xrOrigin;
    public GameObject teleportIndicator;   // flat circle prefab

    [Header("Arc Settings")]
    public float arcVelocity  = 8f;
    public int   arcSegments  = 20;
    public LineRenderer lineRenderer;

    private bool      validTarget        = false;
    private Vector3   teleportTarget;
    private bool      triggerWasPressed  = false;

    private readonly List<InputDevice> _rightDevices = new List<InputDevice>();

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
    }
    void Update()
    {
        if (lineRenderer == null || rightHandController == null) return;

        // While SpawnMenu is busy (holding, selected, menu open) hide arc
        if (SpawnMenu.isBusy)
        {
            lineRenderer.enabled = false;
            if (teleportIndicator != null)
                teleportIndicator.SetActive(false);
            // Still need to track trigger so we don't get a phantom "down"
            // when busy ends — read and discard.
            ReadTrigger(out bool _, out bool _);
            return;
        }

        DrawArc();
        CheckTrigger();
    }
    void ReadTrigger(out bool pressed, out bool down)
    {
        pressed = false;

        InputDevices.GetDevicesAtXRNode(XRNode.RightHand, _rightDevices);
        if (_rightDevices.Count > 0)
            _rightDevices[0].TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out pressed);

        // Keyboard fallback for Mac testing
        bool keyDown = Input.GetKeyDown(KeyCode.F);
        down = (pressed && !triggerWasPressed) || keyDown;
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

                if (Physics.Raycast(prevPoint, dir, out RaycastHit hit, dir.magnitude))
                {
                    teleportTarget = hit.point;

                    if (teleportIndicator != null)
                    {
                        teleportIndicator.SetActive(true);
                        teleportIndicator.transform.position = hit.point + Vector3.up * 0.02f;
                        teleportIndicator.transform.rotation = Quaternion.Euler(
                            0, rightHandController.eulerAngles.y, 0);
                    }

                    // Fill remaining arc segments at the hit point
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
        ReadTrigger(out bool _, out bool triggerDown);

        if (triggerDown && validTarget)
            Teleport();
    }

    void Teleport()
    {
        if (xrOrigin == null) return;

        xrOrigin.position = new Vector3(
            teleportTarget.x,
            xrOrigin.position.y,
            teleportTarget.z);

        Vector3 controllerForward = rightHandController.forward;
        controllerForward.y = 0f;
        if (controllerForward != Vector3.zero)
            xrOrigin.rotation = Quaternion.LookRotation(controllerForward);

        if (teleportIndicator != null)
            teleportIndicator.SetActive(false);
    }
}