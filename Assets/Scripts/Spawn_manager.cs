using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
public class Spawn_manager : MonoBehaviour
{
    public Transform tentCenter;
    public Transform xrOrigin;

    void Start()
    {
        if (xrOrigin != null && tentCenter != null)
        {
            xrOrigin.position = tentCenter.position;
            xrOrigin.rotation = tentCenter.rotation;
        }
    }
}