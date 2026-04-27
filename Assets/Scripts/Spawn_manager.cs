using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
public class Spawn_manager : MonoBehaviour
{
    public Transform tentCenter;    //reference point to pos in scene
    public Transform xrOrigin;      // player rig

    void Start()
    {
        if (xrOrigin != null && tentCenter != null)     //if valid
        {
            xrOrigin.position = tentCenter.position;    //move player to tent center location
            xrOrigin.rotation = tentCenter.rotation;    //rotate player to face a certain direction
        }
    }
}