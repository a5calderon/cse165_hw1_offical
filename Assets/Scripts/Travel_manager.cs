using UnityEngine;
using UnityEngine.InputSystem;

public class TravelManager : MonoBehaviour
{
    public Transform xrOrigin;
    public Transform cameraTransform;

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
        {
            Teleport();
        }
    }

    public void Teleport()
    {
        if (xrOrigin == null || cameraTransform == null)
        {
            Debug.LogError("XR Origin or Camera not assigned!");
            return;
        }

        // 🔥 STEP 1: start slightly in front of camera
        Vector3 origin =
            cameraTransform.position +
            cameraTransform.forward * 0.5f +
            Vector3.up * 0.2f;

        // 🔥 STEP 2: force downward ray (THIS guarantees floor hit)
        Vector3 direction = Vector3.down;

        Debug.DrawRay(origin, direction * 50f, Color.green, 2f);

        if (Physics.Raycast(origin, direction, out RaycastHit hit, 100f))
        {
            Debug.Log("HIT: " + hit.collider.name);

            Vector3 target = new Vector3(
                hit.point.x,
                xrOrigin.position.y,
                hit.point.z
            );

            Debug.Log("TELEPORT TARGET: " + target);

            xrOrigin.position = target;
        }
        else
        {
            Debug.Log("NO HIT - ray missed floor (check collider)");
        }
    }
}