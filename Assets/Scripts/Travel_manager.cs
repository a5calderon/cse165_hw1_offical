using UnityEngine;
using UnityEngine.XR;

public class TravelManager : MonoBehaviour
{
    [Header("References")]
    public Transform rightHandController;
    public Transform xrOrigin;

    [Header("Arc Settings")]
    public float arcVelocity = 10f;
    public int arcSegments = 30;
    public LineRenderer lineRenderer;

    private bool validTarget = false;
    private Vector3 teleportTarget;

    void Awake()
    {
        if (lineRenderer != null)
            lineRenderer.positionCount = arcSegments;
    }

    void Update()
    {
        if (lineRenderer == null || rightHandController == null) return;
        DrawArc();
        CheckGrip();
    }

    void DrawArc()
    {
        lineRenderer.positionCount = arcSegments;
        Vector3 startPos = rightHandController.position;
        Vector3 aimDir = Quaternion.AngleAxis(-30f, rightHandController.right) * rightHandController.forward;
        Vector3 startVel = aimDir * arcVelocity;

        validTarget = false;

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
                    for (int j = i; j < arcSegments; j++)
                        lineRenderer.SetPosition(j, hit.point);
                    validTarget = true;
                    break;
                }
            }
        }

        Debug.Log("Valid target: " + validTarget + " Controller pos: " + rightHandController.position);
        lineRenderer.enabled = true;
    }

    void CheckGrip()
    {
        InputDevice rightDevice = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        rightDevice.TryGetFeatureValue(CommonUsages.gripButton, out bool gripPressed);

        bool keyPressed = UnityEngine.InputSystem.Keyboard.current.gKey.wasPressedThisFrame;

        if ((gripPressed || keyPressed) && validTarget)
            Teleport();
    }

    void Teleport()
    {
        Debug.Log("Teleporting to: " + teleportTarget);
        xrOrigin.position = new Vector3(
            teleportTarget.x,
            xrOrigin.position.y,
            teleportTarget.z
        );
    }
}