using UnityEngine;
using UnityEngine.XR;

public class TravelManager : MonoBehaviour
{
    [Header("References")]
    public Transform rightHandController;
    public Transform xrOrigin;
    public GameObject teleportIndicator; // flat circle prefab

    [Header("Arc Settings")]
    public float arcVelocity = 8f;
    public int arcSegments = 20;
    public LineRenderer lineRenderer;

    private bool validTarget = false;
    private Vector3 teleportTarget;
    private bool triggerWasPressed = false;

    void Awake()
    {
        if (lineRenderer != null)
        {
            lineRenderer.positionCount = arcSegments;
            lineRenderer.startWidth = 0.02f;
            lineRenderer.endWidth = 0.01f;
        }
        if (teleportIndicator != null)
            teleportIndicator.SetActive(false);
    }

    void Update()
    {
        if (lineRenderer == null || rightHandController == null) return;
        if (SpawnMenu.isBusy)
        {
            lineRenderer.enabled = false;
            if (teleportIndicator != null)
                teleportIndicator.SetActive(false);
            return;
        }
        DrawArc();
        CheckTrigger();
    }

    void DrawArc()
    {
        lineRenderer.positionCount = arcSegments;
        Vector3 startPos = rightHandController.position;
        Vector3 aimDir = Quaternion.AngleAxis(-45f, rightHandController.right) * rightHandController.forward;
        Vector3 startVel = aimDir * arcVelocity;

        validTarget = false;

        if (teleportIndicator != null)
            teleportIndicator.SetActive(false);

        for (int i = 0; i < arcSegments; i++)
        {
            float t = i * 0.05f;
            Vector3 point = startPos + startVel * t + 0.5f * Physics.gravity * t * t;
            lineRenderer.SetPosition(i, point);

            if (i > 0)
            {
                float tPrev = (i - 1) * 0.05f;
                Vector3 prevPoint = startPos + startVel * tPrev + 0.5f * Physics.gravity * tPrev * tPrev;
                Vector3 dir = point - prevPoint;

                if (Physics.Raycast(prevPoint, dir, out RaycastHit hit, dir.magnitude))
                {
                    teleportTarget = hit.point;

                    // show indicator at landing spot
                    if (teleportIndicator != null)
                    {
                        teleportIndicator.SetActive(true);
                        teleportIndicator.transform.position = hit.point + Vector3.up * 0.02f;
                        teleportIndicator.transform.rotation = Quaternion.LookRotation(rightHandController.forward, Vector3.up);
                        teleportIndicator.transform.rotation = Quaternion.Euler(0, teleportIndicator.transform.eulerAngles.y, 0);
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
        InputDevice rightDevice = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        rightDevice.TryGetFeatureValue(CommonUsages.triggerButton, out bool triggerPressed);

        bool triggerDown = (triggerPressed && !triggerWasPressed) || Input.GetKeyDown(KeyCode.F);
        triggerWasPressed = triggerPressed;

        if (triggerDown && validTarget && !SpawnMenu.isBusy)
            Teleport();
    }

    void Teleport()
    {
        xrOrigin.position = new Vector3(
            teleportTarget.x,
            xrOrigin.position.y,
            teleportTarget.z
        );

        Vector3 controllerForward = rightHandController.forward;
        controllerForward.y = 0;
        if (controllerForward != Vector3.zero)
            xrOrigin.rotation = Quaternion.LookRotation(controllerForward);

        if (teleportIndicator != null)
            teleportIndicator.SetActive(false);
    }
}