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
    public static bool isBusy = false;

    [Header("References")]
    public GameObject[] prefabs;
    public GameObject menuPanel;
    public Transform rightHandController;

    [Header("Spawnable Items")]
    public List<SpawnableItem> spawnableItems = new List<SpawnableItem>();
    public Transform leftHandController;

    [Header("Placement")]
    public LineRenderer placementRay;

    // ── spawn / hold state ────────────────────────────────────────
    private GameObject heldObject;
    private bool isHolding  = false;
    private bool previewMode = false;

    // ── selection state ───────────────────────────────────────────
    private GameObject selectedObject;
    private GameObject gazeCandidate;
    private float      gazeTimer      = 0f;
    private const float GAZE_HOLD_TIME = 2f;
    private List<Material> originalMaterials = new List<Material>();
    private Material highlightMat;

    // ── scale UI ──────────────────────────────────────────────────
    // Spawned in world-space near the selected object when left grip is held.
    private GameObject scaleUICanvas;
    private bool       scaleUIVisible  = false;

    // ── input tracking ────────────────────────────────────────────
    private bool leftGripWasPressed     = false;
    private bool leftGripHeld           = false;   // true every frame grip is down
    private bool rightGripWasPressed    = false;
    private bool leftTriggerWasPressed  = false;
    private bool rightTriggerWasPressed = false;

    // ── menu state ────────────────────────────────────────────────
    private string      activeCategory = "";
    private GameObject  gridPanel;
    private bool        menuVisible    = false;
    private bool        menuBuilt      = false;

    // ── colours ───────────────────────────────────────────────────
    static readonly Color BG_DARK       = new Color(0.08f, 0.10f, 0.14f, 0.95f);
    static readonly Color PURPLE_BORDER = new Color(0.72f, 0.20f, 1.00f, 1f);
    static readonly Color TEAL_ACTIVE   = new Color(0.15f, 0.50f, 0.50f, 1f);
    static readonly Color BTN_NORMAL    = new Color(0.12f, 0.15f, 0.22f, 1f);
    static readonly Color CLOSE_COLOR   = new Color(0.72f, 0.20f, 1.00f, 0.85f);
    static readonly Color HIGHLIGHT_COL = new Color(1f, 0f, 0.85f, 1f);
    static readonly Color SCALE_UP_COL  = new Color(0.10f, 0.70f, 0.35f, 0.95f);
    static readonly Color SCALE_DN_COL  = new Color(0.80f, 0.20f, 0.20f, 0.95f);

    // ═════════════════════════════════════════════════════════════
    void Start()
    {
        highlightMat = new Material(Shader.Find("Standard"));
        highlightMat.color = HIGHLIGHT_COL;
        highlightMat.EnableKeyword("_EMISSION");
        highlightMat.SetColor("_EmissionColor", HIGHLIGHT_COL * 0.6f);

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

    // ═════════════════════════════════════════════════════════════
    void Update()
    {
        if (!menuBuilt) return;

        // ── Read raw XR device values ─────────────────────────────
        UnityEngine.XR.InputDevice leftDevice  = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        UnityEngine.XR.InputDevice rightDevice = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

        leftDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton,  out bool leftTrigger);
        leftDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.gripButton,     out bool leftGrip);
        rightDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.gripButton,    out bool rightGrip);
        rightDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool rightTrigger);

        // ── Edge-detect "down" events (+ keyboard fallbacks) ──────
        bool leftTriggerDown  = (leftTrigger  && !leftTriggerWasPressed)  || Input.GetKeyDown(KeyCode.G);
        bool leftGripDown     = (leftGrip     && !leftGripWasPressed)     || Input.GetKeyDown(KeyCode.H);
        bool rightGripDown    = (rightGrip    && !rightGripWasPressed)    || Input.GetKeyDown(KeyCode.T);
        bool rightTriggerDown = (rightTrigger && !rightTriggerWasPressed) || Input.GetKeyDown(KeyCode.F);

        leftTriggerWasPressed  = leftTrigger;
        leftGripWasPressed     = leftGrip;
        rightGripWasPressed    = rightGrip;
        rightTriggerWasPressed = rightTrigger;
        leftGripHeld           = leftGrip || Input.GetKey(KeyCode.H);

        isBusy = isHolding || selectedObject != null || menuVisible;

        // ═══════════════════════════════════════════════════════════
        // HOLDING STATE
        // ═══════════════════════════════════════════════════════════
        if (isHolding && heldObject != null)
        {
            // Move object along camera look ray → floor
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

            // LEFT GRIP held → rotate held object (continuous)
            if (leftGripHeld)
                heldObject.transform.Rotate(Vector3.up, 90f * Time.deltaTime);

            // RIGHT GRIP down → place / drop
            if (rightGripDown)
                PlaceObject();

            // RIGHT TRIGGER down while holding → teleport to looked-at spot
            if (rightTriggerDown)
                TeleportHeldObject();

            // Hide scale UI while holding (no selection active)
            HideScaleUI();
            return;
        }

        // ═══════════════════════════════════════════════════════════
        // SELECTED STATE
        // ═══════════════════════════════════════════════════════════
        if (selectedObject != null)
        {
            // LEFT GRIP held → show scale buttons so user can press them
            if (leftGripHeld)
                ShowScaleUI(selectedObject);
            else
                HideScaleUI();

            // RIGHT TRIGGER down → deselect
            if (rightTriggerDown)
                Deselect();

            // RIGHT GRIP down → pick up the selected object into hold state
            if (rightGripDown)
                PickUpSelectedObject();

            return;
        }

        // ═══════════════════════════════════════════════════════════
        // IDLE STATE
        // ═══════════════════════════════════════════════════════════
        HideScaleUI();

        if (placementRay != null)
            placementRay.enabled = false;

        // LEFT TRIGGER down → toggle spawn menu
        if (leftTriggerDown)
            ToggleMenu();

        // RIGHT TRIGGER down → raycast select (Method 2)
        if (rightTriggerDown && !menuVisible)
            TryRaycastSelect();

        // Gaze select running in background (Method 1)
        if (!menuVisible)
            UpdateGazeSelect();
    }

    // ═════════════════════════════════════════════════════════════
    //  TELEPORT HELD OBJECT (right trigger while holding)
    // ═════════════════════════════════════════════════════════════
    void TeleportHeldObject()
    {
        if (heldObject == null) return;
        Camera cam = Camera.main;
        if (cam == null) return;

        Ray ray = new Ray(cam.transform.position, cam.transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, 30f, ~LayerMask.GetMask("Ignore Raycast")))
            heldObject.transform.position = hit.point + Vector3.up * 0.5f;
    }

    // ═════════════════════════════════════════════════════════════
    //  PICK UP SELECTED OBJECT (right grip while selected)
    // ═════════════════════════════════════════════════════════════
    void PickUpSelectedObject()
    {
        if (selectedObject == null) return;

        GameObject toPickUp = selectedObject;

        // Restore original materials before switching to hold state
        RestoreMaterials(toPickUp);
        originalMaterials.Clear();
        selectedObject = null;
        HideScaleUI();

        // Enter hold state
        Rigidbody rb = toPickUp.GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;
        toPickUp.layer = LayerMask.NameToLayer("Ignore Raycast");
        heldObject = toPickUp;
        isHolding  = true;
        isBusy     = true;
    }

    // ═════════════════════════════════════════════════════════════
    //  SELECTION METHOD 1 – GAZE (look for 2 s)
    // ═════════════════════════════════════════════════════════════
    void UpdateGazeSelect()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        Ray ray = new Ray(cam.transform.position, cam.transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, 15f))
        {
            Rigidbody rb = hit.collider.GetComponentInParent<Rigidbody>();
            if (rb != null && !rb.isKinematic)
            {
                GameObject root = rb.gameObject;
                if (root == gazeCandidate)
                {
                    gazeTimer += Time.deltaTime;
                    if (gazeTimer >= GAZE_HOLD_TIME)
                    {
                        SelectObject(root);
                        gazeCandidate = null;
                        gazeTimer = 0f;
                    }
                }
                else
                {
                    gazeCandidate = root;
                    gazeTimer = 0f;
                }
                return;
            }
        }

        gazeCandidate = null;
        gazeTimer = 0f;
    }

    // ═════════════════════════════════════════════════════════════
    //  SELECTION METHOD 2 – RIGHT TRIGGER RAYCAST
    // ═════════════════════════════════════════════════════════════
    void TryRaycastSelect()
    {
        // Prefer right-hand controller ray; fall back to camera ray
        Ray ray = (rightHandController != null)
            ? new Ray(rightHandController.position, rightHandController.forward)
            : new Ray(Camera.main.transform.position, Camera.main.transform.forward);

        if (Physics.Raycast(ray, out RaycastHit hit, 15f))
        {
            Rigidbody rb = hit.collider.GetComponentInParent<Rigidbody>();
            if (rb != null && !rb.isKinematic)
                SelectObject(rb.gameObject);
        }
    }

    // ═════════════════════════════════════════════════════════════
    //  HIGHLIGHT HELPERS
    // ═════════════════════════════════════════════════════════════
    void SelectObject(GameObject obj)
    {
        if (selectedObject == obj) return;
        if (selectedObject != null) Deselect();

        selectedObject = obj;
        originalMaterials.Clear();

        foreach (var r in obj.GetComponentsInChildren<Renderer>())
        {
            foreach (var m in r.materials)
                originalMaterials.Add(m);

            Material[] mats = new Material[r.materials.Length];
            for (int i = 0; i < mats.Length; i++)
                mats[i] = highlightMat;
            r.materials = mats;
        }

        isBusy = true;
    }

    void Deselect()
    {
        if (selectedObject == null) return;
        RestoreMaterials(selectedObject);
        originalMaterials.Clear();
        selectedObject = null;
        HideScaleUI();
        // Delay clearing isBusy by one frame so locomotion systems
        // don't pick up a stale input and lurch the camera forward.
        StartCoroutine(ClearBusyNextFrame());
    }

    IEnumerator ClearBusyNextFrame()
    {
        isBusy = true;                  // stay busy through end of this frame
        yield return new WaitForEndOfFrame();
        yield return null;              // also skip the next frame's Update
        isBusy = isHolding || selectedObject != null || menuVisible;
    }

    void RestoreMaterials(GameObject obj)
    {
        int matIndex = 0;
        foreach (var r in obj.GetComponentsInChildren<Renderer>())
        {
            Material[] mats = new Material[r.materials.Length];
            for (int i = 0; i < mats.Length; i++)
                if (matIndex < originalMaterials.Count)
                    mats[i] = originalMaterials[matIndex++];
            r.materials = mats;
        }
    }

    // ═════════════════════════════════════════════════════════════
    //  SCALE UI  (shown while left grip is held + object selected)
    // ═════════════════════════════════════════════════════════════
    void ShowScaleUI(GameObject target)
    {
        if (scaleUICanvas == null)
            BuildScaleUI();

        // Only position when first becoming visible (same logic as spawn menu)
        if (!scaleUIVisible)
        {
            Camera cam = Camera.main;
            if (cam != null)
            {
                scaleUICanvas.transform.position = cam.transform.position + cam.transform.forward * 0.7f;
                scaleUICanvas.transform.rotation = Quaternion.LookRotation(cam.transform.forward);
            }
            scaleUICanvas.SetActive(true);
            scaleUIVisible = true;
        }
    }

    void HideScaleUI()
    {
        if (scaleUIVisible && scaleUICanvas != null)
        {
            scaleUICanvas.SetActive(false);
            scaleUIVisible = false;
        }
    }

    void BuildScaleUI()
    {
        scaleUICanvas = new GameObject("ScaleUI_Canvas");
        scaleUICanvas.layer = LayerMask.NameToLayer("UI");

        var canvas = scaleUICanvas.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.WorldSpace;
        canvas.sortingOrder = 20;
        scaleUICanvas.AddComponent<GraphicRaycaster>();

        var rt = scaleUICanvas.GetComponent<RectTransform>();
        rt.sizeDelta        = new Vector2(700, 500);
        rt.anchoredPosition = Vector2.zero;
        // Same world scale as the spawn menu
        scaleUICanvas.transform.localScale = Vector3.one * 0.001f;

        // Background — same dark bg + purple border as spawn menu
        var bg = MakeImage(scaleUICanvas.transform, "BG", new Vector2(700, 500), Vector2.zero, BG_DARK);
        AddOutline(bg, PURPLE_BORDER, 4f);

        // Title
        MakeLabel(bg.transform, "SCALE OBJECT", 22, new Vector2(0, 180), new Vector2(700, 50));

        // Hint
        MakeLabel(bg.transform, "Hold LEFT GRIP to keep panel open", 12,
                  new Vector2(0, 140), new Vector2(700, 30));

        // BIGGER button
        var upImg = MakeImage(bg.transform, "ScaleUp", new Vector2(220, 100), new Vector2(-130, 20), SCALE_UP_COL);
        AddOutline(upImg, PURPLE_BORDER, 3f);
        MakeLabel(upImg.transform, "▲  BIGGER", 20, Vector2.zero, new Vector2(220, 100));
        var upBtn = upImg.gameObject.AddComponent<Button>();
        var upColors = upBtn.colors;
        upColors.highlightedColor = new Color(0.15f, 0.90f, 0.50f, 1f);
        upColors.pressedColor     = PURPLE_BORDER;
        upBtn.colors        = upColors;
        upBtn.targetGraphic = upImg;
        upBtn.onClick.AddListener(() =>
        {
            if (selectedObject != null)
                selectedObject.transform.localScale *= 1.25f;
        });

        // SMALLER button
        var dnImg = MakeImage(bg.transform, "ScaleDn", new Vector2(220, 100), new Vector2(130, 20), SCALE_DN_COL);
        AddOutline(dnImg, PURPLE_BORDER, 3f);
        MakeLabel(dnImg.transform, "▼  SMALLER", 20, Vector2.zero, new Vector2(220, 100));
        var dnBtn = dnImg.gameObject.AddComponent<Button>();
        var dnColors = dnBtn.colors;
        dnColors.highlightedColor = new Color(1f, 0.40f, 0.40f, 1f);
        dnColors.pressedColor     = PURPLE_BORDER;
        dnBtn.colors        = dnColors;
        dnBtn.targetGraphic = dnImg;
        dnBtn.onClick.AddListener(() =>
        {
            if (selectedObject != null)
                selectedObject.transform.localScale *= 0.8f;
        });

        // CLOSE button — matches spawn menu close style
        var closeImg = MakeImage(bg.transform, "CloseBtn", new Vector2(200, 40), new Vector2(0, -195), CLOSE_COLOR);
        AddOutline(closeImg, PURPLE_BORDER, 2f);
        MakeLabel(closeImg.transform, "CLOSE", 16, Vector2.zero, new Vector2(200, 40));
        var closeBtn = closeImg.gameObject.AddComponent<Button>();
        closeBtn.onClick.AddListener(() => HideScaleUI());

        scaleUICanvas.SetActive(false);
    }

    // ═════════════════════════════════════════════════════════════
    //  PLACEMENT
    // ═════════════════════════════════════════════════════════════
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
            Renderer[] renderers = heldObject.GetComponentsInChildren<Renderer>();
            float lowestY = 0f;
            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                lowestY = heldObject.transform.position.y - bounds.min.y;
            }

            heldObject.transform.position = new Vector3(
                heldObject.transform.position.x,
                hit.point.y + lowestY + 1.5f,
                heldObject.transform.position.z);
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
            rb.linearVelocity  = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic     = false;
            rb.constraints     = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        }

        if (placementRay != null) placementRay.enabled = false;

        heldObject  = null;
        isHolding   = false;
        previewMode = false;
        isBusy      = false;
    }

    // ═════════════════════════════════════════════════════════════
    //  SPAWN
    // ═════════════════════════════════════════════════════════════
    public void SpawnInstant(int index)
    {
        if (isHolding && heldObject != null) PlaceObject();
        previewMode = false;

        if (prefabs == null || index >= prefabs.Length || prefabs[index] == null) return;
        if (menuPanel != null) menuPanel.SetActive(false);

        GameObject obj = Instantiate(prefabs[index]);
        Camera cam = Camera.main;
        obj.transform.position = cam != null
            ? new Vector3(cam.transform.position.x + cam.transform.forward.x * 2.5f, 2f,
                          cam.transform.position.z + cam.transform.forward.z * 2.5f)
            : rightHandController.position;
        obj.transform.rotation = Quaternion.Euler(0, cam != null ? cam.transform.eulerAngles.y + 180f : 180f, 0);

        Rigidbody rb = obj.GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;

        heldObject = obj;
        isHolding  = true;
        isBusy     = true;
        obj.layer  = LayerMask.NameToLayer("Ignore Raycast");
    }

    public void SpawnItem(SpawnableItem item)
    {
        if (isHolding && heldObject != null) PlaceObject();
        previewMode = false;

        if (item?.prefab == null) return;
        SetMenuVisible(false);

        GameObject obj = Instantiate(item.prefab);
        Camera cam = Camera.main;
        obj.transform.position = cam != null
            ? new Vector3(cam.transform.position.x + cam.transform.forward.x * 2.5f, 2f,
                          cam.transform.position.z + cam.transform.forward.z * 2.5f)
            : rightHandController != null
                ? new Vector3(rightHandController.position.x, 2f, rightHandController.position.z)
                : Vector3.zero;
        obj.transform.rotation = Quaternion.Euler(0, cam != null ? cam.transform.eulerAngles.y + 180f : 180f, 0);

        if (!obj.TryGetComponent<Rigidbody>(out var rb))
            rb = obj.AddComponent<Rigidbody>();
        rb.isKinematic = true;

        if (obj.GetComponentInChildren<Collider>() == null)
            obj.AddComponent<BoxCollider>();

        heldObject = obj;
        isHolding  = true;
        isBusy     = true;
        obj.layer  = LayerMask.NameToLayer("Ignore Raycast");
    }

    // ═════════════════════════════════════════════════════════════
    //  MENU
    // ═════════════════════════════════════════════════════════════
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
        isBusy = v || isHolding || selectedObject != null;
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

        var bg = MakeImage(menuPanel.transform, "BG", new Vector2(700, 500), Vector2.zero, BG_DARK);
        AddOutline(bg, PURPLE_BORDER, 4f);

        MakeCategoryTab(bg.transform, "FURNITURE", new Vector2(185, 120), "Furniture");
        MakeCategoryTab(bg.transform, "ITEMS",     new Vector2(185,  60), "Items");

        var closeImg = MakeImage(bg.transform, "CloseBtn", new Vector2(200, 40), new Vector2(185, -195), CLOSE_COLOR);
        AddOutline(closeImg, PURPLE_BORDER, 2f);
        MakeLabel(closeImg.transform, "CLOSE", 16, Vector2.zero, new Vector2(200, 40));
        var closeBtn = closeImg.gameObject.AddComponent<Button>();
        closeBtn.onClick.AddListener(() => SetMenuVisible(false));

        var gridImg = MakeImage(bg.transform, "GridPanel", new Vector2(310, 320), new Vector2(-160, 20),
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
            var cell = MakeImage(gridPanel.transform, item.itemName, new Vector2(64, 64), Vector2.zero, BTN_NORMAL);
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
                MakeLabel(cell.transform, item.itemName, 10, Vector2.zero, new Vector2(60, 60));

            var btn      = cell.gameObject.AddComponent<Button>();
            var captured = item;
            btn.onClick.AddListener(() => SpawnItem(captured));
        }
    }

    void SetCategory(string cat) { activeCategory = cat; RefreshGrid(); }

    // ── UI helpers ────────────────────────────────────────────────
    Image MakeImage(Transform parent, string name, Vector2 size, Vector2 pos, Color color)
    {
        var go  = new GameObject(name);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        var rt = go.GetComponent<RectTransform>();
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
        var btn    = img.gameObject.AddComponent<Button>();
        var colors = btn.colors;
        colors.normalColor      = category == activeCategory ? TEAL_ACTIVE : BTN_NORMAL;
        colors.highlightedColor = TEAL_ACTIVE;
        colors.pressedColor     = PURPLE_BORDER;
        colors.selectedColor    = TEAL_ACTIVE;
        btn.colors        = colors;
        btn.targetGraphic = img;
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