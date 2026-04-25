using UnityEngine;
using UnityEngine.XR;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public class SpawnMenu : MonoBehaviour
{
    [Header("References")]
    public GameObject[] prefabs;
    public GameObject menuPanel;
    public Transform rightHandController;

    [Header("Spawnable Items")]
    public List<SpawnableItem> spawnableItems = new List<SpawnableItem>();
    public Transform leftHandController;

    [Header("Placement")]
    public LineRenderer placementRay;

    private GameObject heldObject;
    private bool isHolding = false;
    private bool leftGripWasPressed = false;
    private bool rightGripWasPressed = false;

    private string activeCategory = "Furniture";
    private GameObject gridPanel;
    private bool menuVisible = false;
    private bool menuBuilt = false;

    static readonly Color BG_DARK       = new Color(0.08f, 0.10f, 0.14f, 0.95f);
    static readonly Color PURPLE_BORDER = new Color(0.72f, 0.20f, 1.00f, 1f);
    static readonly Color TEAL_ACTIVE   = new Color(0.15f, 0.50f, 0.50f, 1f);
    static readonly Color BTN_NORMAL    = new Color(0.12f, 0.15f, 0.22f, 1f);
    static readonly Color CLOSE_COLOR   = new Color(0.72f, 0.20f, 1.00f, 0.85f);

    void Start()
    {
        if (menuPanel != null)
            menuPanel.SetActive(false);

        StartCoroutine(BuildMenuNextFrame());
    }

    IEnumerator BuildMenuNextFrame()
    {
        yield return null;
        BuildMenu();
        SetMenuVisible(false);
        menuBuilt = true;
    }

    void Update()
    {
        if (!menuBuilt) return;

        UnityEngine.XR.InputDevice leftDevice  = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        UnityEngine.XR.InputDevice rightDevice = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

        leftDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.gripButton,  out bool leftGrip);
        rightDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.gripButton, out bool rightGrip);

        bool leftGripDown  = (leftGrip  && !leftGripWasPressed)  || Input.GetKeyDown(KeyCode.G);
        bool rightGripDown = (rightGrip && !rightGripWasPressed) || Input.GetKeyDown(KeyCode.T);

        leftGripWasPressed  = leftGrip;
        rightGripWasPressed = rightGrip;

        if (isHolding && heldObject != null)
        {
            Camera cam = Camera.main;
            if (cam != null)
            {
                Ray ray = new Ray(cam.transform.position, cam.transform.forward);
                if (Physics.Raycast(ray, out RaycastHit hit, 15f, ~LayerMask.GetMask("Ignore Raycast")))
                {
                    heldObject.transform.position = hit.point + Vector3.up * 0.1f;
                    DrawPlacementRay(cam.transform.position, hit.point);
                }
                else
                {
                    Vector3 floatPos = cam.transform.position + cam.transform.forward * 3f + Vector3.down * 1f;
                    heldObject.transform.position = floatPos;
                    DrawPlacementRay(cam.transform.position, floatPos);
                }
            }

            if (rightGripDown)
                PlaceObject();
        }
        else
        {
            if (placementRay != null)
                placementRay.enabled = false;

            if (leftGripDown)
                ToggleMenu();
        }
    }

    void DrawPlacementRay(Vector3 from, Vector3 to)
    {
        if (placementRay == null) return;
        placementRay.enabled = true;
        placementRay.positionCount = 2;
        placementRay.SetPosition(0, from);
        placementRay.SetPosition(1, to);
    }

    void PlaceObject()
{
    if (heldObject == null) return;

    Vector3 rayOrigin = new Vector3(heldObject.transform.position.x, 10f, heldObject.transform.position.z);
    int floorMask = LayerMask.GetMask("Floor");

    if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 20f, floorMask))
    {
        heldObject.transform.position = new Vector3(
            heldObject.transform.position.x,
            hit.point.y + 1.5f,
            heldObject.transform.position.z
        );
    }
    else
    {
        Debug.Log("No floor detected!");
        return;
    }

    heldObject.transform.rotation = Quaternion.Euler(0, heldObject.transform.rotation.eulerAngles.y, 0);
    heldObject.layer = 0;

    Rigidbody rb = heldObject.GetComponent<Rigidbody>();
    if (rb != null)
    {
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.linearDamping = 5f;
        rb.angularDamping = 5f;
        rb.isKinematic = false;
        rb.constraints = RigidbodyConstraints.FreezeRotationX | 
                         RigidbodyConstraints.FreezeRotationZ;
    }

    if (placementRay != null)
        placementRay.enabled = false;

    heldObject = null;
    isHolding = false;
}
    public void SpawnInstant(int index)
    {
        if (isHolding && heldObject != null)
            PlaceObject();
        if (prefabs == null || index >= prefabs.Length || prefabs[index] == null) return;
        if (menuPanel != null)
            menuPanel.SetActive(false);

        GameObject obj = Instantiate(prefabs[index]);

        Camera cam = Camera.main;
        obj.transform.position = cam != null
            ? cam.transform.position + cam.transform.forward * 1.5f
            : rightHandController.position;

        Rigidbody rb = heldObject.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = false;
        }

        heldObject = obj;
        isHolding  = true;
        obj.layer  = LayerMask.NameToLayer("Ignore Raycast");
    }

    public void SpawnItem(SpawnableItem item)
    {
        if (isHolding && heldObject != null)
            PlaceObject();

        if (item?.prefab == null) return;
        SetMenuVisible(false);

        GameObject obj = Instantiate(item.prefab);

        Camera cam = Camera.main;
        obj.transform.position = cam != null
            ? cam.transform.position + cam.transform.forward * 2.5f + Vector3.up * 0.5f
            : rightHandController != null
                ? rightHandController.position + Vector3.up * 0.5f
                : Vector3.zero;
        if (!obj.TryGetComponent<Rigidbody>(out var rb))
            rb = obj.AddComponent<Rigidbody>();
        rb.isKinematic = true;

        if (obj.GetComponentInChildren<Collider>() == null)
            obj.AddComponent<BoxCollider>();

        heldObject = obj;
        isHolding  = true;
        obj.layer  = LayerMask.NameToLayer("Ignore Raycast");
    }

    public void ToggleMenu() => SetMenuVisible(!menuVisible);

    void SetMenuVisible(bool v)
    {
        menuVisible = v;
        if (menuPanel != null)
        {
            if (v)
            {
                Camera cam = Camera.main;
                if (cam != null)
                {
                    menuPanel.transform.position = cam.transform.position + cam.transform.forward * 0.7f;
                    menuPanel.transform.rotation = Quaternion.LookRotation(cam.transform.forward);
                }
            }
            menuPanel.SetActive(v);
        }
    }

    void BuildMenu()
    {
        if (menuPanel == null)
        {
            menuPanel = new GameObject("SpawnMenuCanvas");
            menuPanel.layer = LayerMask.NameToLayer("UI");

            var canvas = menuPanel.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.WorldSpace;
            canvas.sortingOrder = 10;
            menuPanel.AddComponent<GraphicRaycaster>();

            var rt = menuPanel.GetComponent<RectTransform>();
            rt.sizeDelta        = new Vector2(700, 500);
            rt.anchoredPosition = Vector2.zero;

            menuPanel.transform.localScale = Vector3.one * 0.001f;
        }

        var bg = MakeImage(menuPanel.transform, "BG",
                           new Vector2(700, 500), Vector2.zero, BG_DARK);
        AddOutline(bg, PURPLE_BORDER, 4f);

        MakeCategoryTab(bg.transform, "FURNITURE", new Vector2(185, 120), "Furniture");
        MakeCategoryTab(bg.transform, "ITEMS",     new Vector2(185,  60), "Items");

        var closeImg = MakeImage(bg.transform, "CloseBtn",
                                 new Vector2(200, 40), new Vector2(185, -195), CLOSE_COLOR);
        AddOutline(closeImg, PURPLE_BORDER, 2f);
        MakeLabel(closeImg.transform, "CLOSE", 16, Vector2.zero, new Vector2(200, 40));
        var closeBtn = closeImg.gameObject.AddComponent<Button>();
        closeBtn.onClick.AddListener(() => SetMenuVisible(false));

        var gridImg = MakeImage(bg.transform, "GridPanel",
                                new Vector2(310, 320), new Vector2(-160, 20),
                                new Color(0.10f, 0.12f, 0.18f, 1f));
        AddOutline(gridImg, PURPLE_BORDER, 3f);
        gridPanel = gridImg.gameObject;

        var gridRt = gridPanel.GetComponent<RectTransform>();
        gridRt.sizeDelta = new Vector2(310, 320);

        var grid             = gridPanel.AddComponent<GridLayoutGroup>();
        grid.cellSize        = new Vector2(64, 64);
        grid.spacing         = new Vector2(8, 8);
        grid.padding         = new RectOffset(12, 12, 12, 12);
        grid.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 4;

        RefreshGrid();
    }

    void RefreshGrid()
    {
        if (gridPanel == null) return;
        foreach (Transform child in gridPanel.transform)
            Destroy(child.gameObject);

        var filtered = spawnableItems.FindAll(i =>
            i.category.Equals(activeCategory, System.StringComparison.OrdinalIgnoreCase));

        foreach (var item in filtered)
        {
            var cell = MakeImage(gridPanel.transform, item.itemName,
                                 new Vector2(64, 64), Vector2.zero, BTN_NORMAL);
            AddOutline(cell, PURPLE_BORDER, 2f);

            if (item.icon != null)
            {
                var icon = new GameObject("Icon").AddComponent<Image>();
                icon.transform.SetParent(cell.transform, false);
                icon.sprite = item.icon;
                var irt = icon.GetComponent<RectTransform>();
                irt.sizeDelta        = new Vector2(52, 52);
                irt.anchoredPosition = Vector2.zero;
            }
            else
            {
                MakeLabel(cell.transform, item.itemName, 10, Vector2.zero, new Vector2(60, 60));
            }

            var btn      = cell.gameObject.AddComponent<Button>();
            var captured = item;
            btn.onClick.AddListener(() => SpawnItem(captured));
        }
    }

    void SetCategory(string cat)
    {
        activeCategory = cat;
        RefreshGrid();
    }

    Image MakeImage(Transform parent, string name, Vector2 size, Vector2 pos, Color color)
    {
        var go  = new GameObject(name);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        var rt   = go.GetComponent<RectTransform>();
        rt.sizeDelta        = size;
        rt.anchoredPosition = pos;
        return img;
    }

    void AddOutline(Component target, Color color, float width)
    {
        var o = target.gameObject.AddComponent<Outline>();
        o.effectColor    = color;
        o.effectDistance = new Vector2(width, -width);
    }

    void MakeCategoryTab(Transform parent, string label, Vector2 pos, string category)
    {
        var img = MakeImage(parent, label + "Tab", new Vector2(220, 45), pos,
                            category == activeCategory ? TEAL_ACTIVE : BTN_NORMAL);
        AddOutline(img, PURPLE_BORDER, 2f);
        MakeLabel(img.transform, label, 15, Vector2.zero, new Vector2(220, 45));
        var btn = img.gameObject.AddComponent<Button>();
        btn.onClick.AddListener(() => SetCategory(category));
    }

    TextMeshProUGUI MakeLabel(Transform parent, string text, int fontSize, Vector2 pos, Vector2 size)
    {
        var go  = new GameObject("Lbl_" + text);
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = text;
        tmp.fontSize  = fontSize;
        tmp.color     = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta        = size;
        rt.anchoredPosition = pos;
        return tmp;
    }
}