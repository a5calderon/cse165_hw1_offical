using UnityEngine;
/*
GOAL: define custom data type for spawn menu
*/
[System.Serializable]           //to show in inspector
public class SpawnableItem
{
    public string itemName;     //store name of item for UI
    public GameObject prefab;   //actual object to spawn
    public Sprite icon;         // to do: add an image to the button 
    public string category;     // group by : "furniture", "items", etc
}