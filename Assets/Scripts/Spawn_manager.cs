using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
public class Spawn_manager: MonoBehaviour
{
    public Transform tentCenter; 

    void Start()
    {
        // Move player to center of tent when game starts
        transform.position = tentCenter.position;
        transform.rotation = tentCenter.rotation;
    }
}
