using UnityEngine;
using UnityEngine.InputSystem;

public class SpawnMenu : MonoBehaviour
{
    public GameObject[] prefabs;
    public GameObject menuPanel;
    public Transform spawnPoint;

    private GameObject selectedPrefab;

    void Start()
    {
        if (menuPanel != null)
            menuPanel.SetActive(true);
    }

    void Update()
    {
        if (Keyboard.current.tKey.wasPressedThisFrame)
        {
            Debug.Log("T PRESSED");
            SpawnInstant(0);
        }
    }

    public void SpawnInstant(int index)
    {
        // Safety checks
        if (prefabs == null || prefabs.Length == 0)
        {
            Debug.LogError("Prefabs array is empty!");
            return;
        }

        if (prefabs[index] == null)
        {
            Debug.LogError("Prefab at index " + index + " is NULL!");
            return;
        }

        if (spawnPoint == null)
        {
            Debug.LogError("SpawnPoint is NOT assigned in Inspector!");
            return;
        }

        // Instantiate object
        GameObject obj = Instantiate(prefabs[index]);

        // Spawn at correct VR-safe position
        obj.transform.position = spawnPoint.position;
        obj.transform.rotation = spawnPoint.rotation;

        // Enable physics
        Rigidbody rb = obj.GetComponent<Rigidbody>();
        if (rb != null)
            rb.isKinematic = false;

        Debug.Log("Spawned: " + obj.name);
    }

    public void PickItem(int index)
    {
        selectedPrefab = prefabs[index];
        if (menuPanel != null)
            menuPanel.SetActive(false);
    }

    public void ToggleMenu()
    {
        if (menuPanel != null)
            menuPanel.SetActive(!menuPanel.activeSelf);
    }
}