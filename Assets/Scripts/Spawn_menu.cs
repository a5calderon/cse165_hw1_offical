using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.InputSystem;

// ─────────────────────────────────────────────────────────────────────────────
//  SpawnMenu – Fixed version
//  Key changes:
//   • Buttons are activated by GAZE + LEFT TRIGGER (no XR pointer needed)
//   • A small reticle highlights the button you're looking at
//   • Menu placement follows the camera each time it opens
//   • Scale UI uses the same gaze+trigger scheme
//   • All original game-logic kept intact
// ─────────────────────────────────────────────────────────────────────────────
public class SpawnMenu : MonoBehaviour
{
    // ── Static state ──────────────────────────────────────────────
    private static bool _isHolding = false;
    public static bool IsHoldingObject() => _isHolding;
    public static bool IsBusy()          => _isHolding;

    [Header("References")]
    public GameObject[]    prefabs;
    public GameObject      menuPanel;          // kept for Inspector compat; overwritten at runtime
    public Transform       rightHandController;
    public Transform       leftHandController;

    [Header("Spawnable Items")]
    public List<SpawnableItem> spawnableItems = new List<SpawnableItem>();

    [Header("Placement")]
    public LineRenderer placementRay;

    [Header("XR Input Actions")]
    public InputActionReference leftTriggerAction;
    public InputActionReference leftGripAction;
    public InputActionReference rightGripAction;

    // ── Spawn / hold ──────────────────────────────────────────────
    private GameObject heldObject;
    private bool isHolding   = false;
    private bool previewMode = false;

    // ── Selection ─────────────────────────────────────────────────
    private GameObject        selectedObject;
    private GameObject        gazeCandidate;
    private float             gazeTimer          = 0f;
    private const float       GAZE_HOLD_TIME     = 2f;
    private List<Material>    originalMaterials  = new List<Material>();
    private Material          highlightMat;

    // ── Scale UI ──────────────────────────────────────────────────
    private GameObject scaleUICanvas;
    private bool       scaleUIVisible = false;

    // ── Input state ───────────────────────────────────────────────
    private bool leftTriggerWasPressed = false;
    private bool leftGripWasPressed    = false;
    private bool rightGripWasPressed   = false;
    private bool leftGripHeld          = false;

    // ── Menu state ────────────────────────────────────────────────
    private string     activeCategory = "";
    private GameObject gridPanel;
    private bool       menuVisible    = false;
    private bool       menuBuilt      = false;

    // ── Gaze-button system ────────────────────────────────────────
    // Each interactive UI button registers itself here so we can
    // do a physics/UI raycast-free hover check via screen-space dot product.
    private struct GazeButton
    {
        public RectTransform rect;
        public Canvas        canvas;
        public System.Action onClick;
        public Image         image;
        public Color         normalColor;
    }
    private List<GazeButton> gazeButtons    = new List<GazeButton>();
    private int              hoveredIndex   = -1;
    private Color            hoverColor     = new Color(0.90f, 0.70f, 1.00f, 1f);

    // ── Colors ────────────────────────────────────────────────────
    static readonly Color BG_DARK       = new Color(0.08f, 0.10f, 0.14f, 0.95f);
    static readonly Color PURPLE_BORDER = new Color(0.72f, 0.20f, 1.00f, 1f);
    static readonly Color TEAL_ACTIVE   = new Color(0.15f, 0.50f, 0.50f, 1f);
    static readonly Color BTN_NORMAL    = new Color(0.12f, 0.15f, 0.22f, 1f);
    static readonly Color CLOSE_COLOR   = new Color(0.72f, 0.20f, 1.00f, 0.85f);
    static readonly Color HIGHLIGHT_COL = new Color(1f, 0f, 0.85f, 1f);
    static readonly Color SCALE_UP_COL  = new Color(0.10f, 0.70f, 0.35f, 0.95f);
    static readonly Color SCALE_DN_COL  = new Color(0.80f, 0.20f, 0.20f, 0.95f);

    // ─────────────────────────────────────────────────────────────
    void OnEnable()
    {
        try { leftTriggerAction?.action.Enable(); leftGripAction?.action.Enable(); rightGripAction?.action.Enable(); }
        catch (System.Exception e) { Debug.LogWarning("[SpawnMenu] OnEnable: " + e.Message); }
    }

    void OnDisable()
    {
        try { leftTriggerAction?.action.Disable(); leftGripAction?.action.Disable(); rightGripAction?.action.Disable(); }
        catch (System.Exception e) { Debug.LogWarning("[SpawnMenu] OnDisable: " + e.Message); }
    }

    void Start()
    {
        highlightMat = new Material(Shader.Find("Standard"));
        highlightMat.color = HIGHLIGHT_COL;
        highlightMat.EnableKeyword("_EMISSION");
        highlightMat.SetColor("_EmissionColor", HIGHLIGHT_COL * 0.6f);

        // Clear any scene Canvas reference – we build our own
        if (menuPanel != null)
        {
            Debug.LogWarning("[SpawnMenu] Clearing Inspector menuPanel; runtime canvas will be used.");
            menuPanel = null;
        }

        StartCoroutine(BuildMenuNextFrame());
    }

    IEnumerator BuildMenuNextFrame()
    {
        yield return null;
        BuildMenu();
        SetMenuVisible(false);
        menuBuilt = true;
    }

    // ─────────────────────────────────────────────────────────────
    bool ReadButton(InputActionReference actionRef, KeyCode fallback)
    {
        if (actionRef != null && actionRef.action != null)
        {
            try   { return actionRef.action.ReadValue<float>() > 0.5f; }
            catch { return actionRef.action.IsPressed(); }
        }
        return Input.GetKey(fallback);
    }

    // ─────────────────────────────────────────────────────────────
    void Update()
    {
        if (!menuBuilt) return;

        bool leftTrigger  = ReadButton(leftTriggerAction, KeyCode.G);
        bool leftGrip     = ReadButton(leftGripAction,    KeyCode.H);
        bool rightGrip    = ReadButton(rightGripAction,   KeyCode.T);

        bool leftTriggerDown = leftTrigger && !leftTriggerWasPressed;
        bool leftGripDown    = leftGrip    && !leftGripWasPressed;
        bool rightGripDown   = rightGrip   && !rightGripWasPressed;

        leftTriggerWasPressed = leftTrigger;
        leftGripWasPressed    = leftGrip;
        rightGripWasPressed   = rightGrip;
        leftGripHeld          = leftGrip;

        UpdateStaticHolding();

        // ── Always update gaze-button hover ───────────────────────
        UpdateGazeButtonHover();

        // ── If trigger pressed and a UI button is hovered → fire it
        if (leftTriggerDown && hoveredIndex >= 0)
        {
            gazeButtons[hoveredIndex].onClick?.Invoke();
            return; // consume the press
        }

        // ── HOLDING ───────────────────────────────────────────────
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
                    Vector3 fp = cam.transform.position + cam.transform.forward * 3f + Vector3.down * 1f;
                    heldObject.transform.position = fp;
                    DrawPlacementRay(cam.transform.position, fp);
                }
            }

            if (leftGripHeld)
                heldObject.transform.Rotate(Vector3.up, 90f * Time.deltaTime);

            if (rightGripDown)
                PlaceObject();

            HideScaleUI();
            return;
        }

        // ── SELECTED ──────────────────────────────────────────────
        if (selectedObject != null)
        {
            if (leftGripHeld)
                ShowScaleUI(selectedObject);
            else
                HideScaleUI();

            if (leftTriggerDown)
            {
                Deselect();
                return;
            }

            if (rightGripDown)
                PickUpSelectedObject();

            return;
        }

        // ── IDLE ──────────────────────────────────────────────────
        HideScaleUI();
        if (placementRay != null) placementRay.enabled = false;

        if (leftTriggerDown && !isHolding && selectedObject == null)
        {
            SetMenuVisible(!menuVisible);
            return;
        }

        if (leftGripDown && !menuVisible && selectedObject == null && !isHolding)
            TryRaycastSelect();

        if (!menuVisible)
            UpdateGazeSelect();
    }

    void UpdateStaticHolding() { _isHolding = isHolding; }

    // ─────────────────────────────────────────────────────────────
    //  GAZE-BUTTON HOVER
    //  Projects each button's world centre into screen space and
    //  checks if the camera's forward ray is close to it.
    // ─────────────────────────────────────────────────────────────
    void UpdateGazeButtonHover()
    {
        Camera cam = Camera.main;
        int  newHover = -1;

        if (cam != null)
        {
            Ray gazeRay = new Ray(cam.transform.position, cam.transform.forward);

            for (int i = 0; i < gazeButtons.Count; i++)
            {
                var gb = gazeButtons[i];
                if (gb.rect == null || gb.canvas == null) continue;

                // Only test buttons whose canvas is active
                if (!gb.canvas.gameObject.activeInHierarchy) continue;

                // World-space centre of the button
                Vector3 worldCenter = gb.rect.TransformPoint(gb.rect.rect.center);

                // Closest point on gaze ray to the button centre
                Vector3 toBtn  = worldCenter - gazeRay.origin;
                float   proj   = Vector3.Dot(toBtn, gazeRay.direction);
                if (proj < 0f) continue;                        // behind camera

                Vector3 closest = gazeRay.origin + gazeRay.direction * proj;
                float   dist    = Vector3.Distance(closest, worldCenter);

                // Approximate button "radius" in world units
                float btnRadius = Mathf.Max(
                    gb.rect.rect.width  * gb.rect.lossyScale.x,
                    gb.rect.rect.height * gb.rect.lossyScale.y) * 0.5f;

                if (dist < btnRadius)
                {
                    newHover = i;
                    break;
                }
            }
        }

        // Update tint
        if (newHover != hoveredIndex)
        {
            if (hoveredIndex >= 0 && hoveredIndex < gazeButtons.Count)
            {
                var old = gazeButtons[hoveredIndex];
                if (old.image != null) old.image.color = old.normalColor;
            }
            hoveredIndex = newHover;
            if (hoveredIndex >= 0)
            {
                var gb = gazeButtons[hoveredIndex];
                if (gb.image != null) gb.image.color = hoverColor;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Helper: register a button for gaze interaction
    // ─────────────────────────────────────────────────────────────
    void RegisterGazeButton(Image img, Canvas canvas, System.Action onClick)
    {
        gazeButtons.Add(new GazeButton
        {
            rect        = img.GetComponent<RectTransform>(),
            canvas      = canvas,
            onClick     = onClick,
            image       = img,
            normalColor = img.color
        });
    }

    // ─────────────────────────────────────────────────────────────
    void PickUpSelectedObject()
    {
        if (selectedObject == null) return;
        GameObject toPickUp = selectedObject;
        RestoreMaterials(toPickUp);
        originalMaterials.Clear();
        selectedObject = null;
        HideScaleUI();

        Rigidbody rb = toPickUp.GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;
        toPickUp.layer = LayerMask.NameToLayer("Ignore Raycast");
        heldObject = toPickUp;
        isHolding  = true;
        UpdateStaticHolding();
    }

    // ─────────────────────────────────────────────────────────────
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
                else { gazeCandidate = root; gazeTimer = 0f; }
                return;
            }
        }
        gazeCandidate = null;
        gazeTimer = 0f;
    }

    void TryRaycastSelect()
    {
        Ray ray = (leftHandController != null)
            ? new Ray(leftHandController.position, leftHandController.forward)
            : new Ray(Camera.main.transform.position, Camera.main.transform.forward);

        if (Physics.Raycast(ray, out RaycastHit hit, 15f))
        {
            Rigidbody rb = hit.collider.GetComponentInParent<Rigidbody>();
            if (rb != null && !rb.isKinematic)
                SelectObject(rb.gameObject);
        }
    }

    void SelectObject(GameObject obj)
    {
        if (selectedObject == obj) return;
        if (selectedObject != null) Deselect();
        selectedObject = obj;
        originalMaterials.Clear();
        foreach (var r in obj.GetComponentsInChildren<Renderer>())
        {
            foreach (var m in r.materials) originalMaterials.Add(m);
            Material[] mats = new Material[r.materials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = highlightMat;
            r.materials = mats;
        }
    }

    void Deselect()
    {
        if (selectedObject == null) return;
        RestoreMaterials(selectedObject);
        originalMaterials.Clear();
        selectedObject = null;
        HideScaleUI();
    }

    void RestoreMaterials(GameObject obj)
    {
        int idx = 0;
        foreach (var r in obj.GetComponentsInChildren<Renderer>())
        {
            Material[] mats = new Material[r.materials.Length];
            for (int i = 0; i < mats.Length; i++)
                if (idx < originalMaterials.Count) mats[i] = originalMaterials[idx++];
            r.materials = mats;
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  SCALE UI
    // ─────────────────────────────────────────────────────────────
    void ShowScaleUI(GameObject target)
    {
        if (scaleUICanvas == null) BuildScaleUI();
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
        rt.sizeDelta = new Vector2(700, 500);
        scaleUICanvas.transform.localScale = Vector3.one * 0.001f;

        var bg = MakeImage(scaleUICanvas.transform, "BG", new Vector2(700, 500), Vector2.zero, BG_DARK);
        AddOutline(bg, PURPLE_BORDER, 4f);
        MakeLabel(bg.transform, "SCALE OBJECT", 22, new Vector2(0, 180), new Vector2(700, 50));
        MakeLabel(bg.transform, "Hold LEFT GRIP  |  Gaze at button  |  LEFT TRIGGER to press",
                  11, new Vector2(0, 140), new Vector2(700, 30));

        var upImg = MakeImage(bg.transform, "ScaleUp", new Vector2(220, 100), new Vector2(-130, 20), SCALE_UP_COL);
        AddOutline(upImg, PURPLE_BORDER, 3f);
        MakeLabel(upImg.transform, "▲  BIGGER", 20, Vector2.zero, new Vector2(220, 100));
        RegisterGazeButton(upImg, canvas, () => { if (selectedObject) selectedObject.transform.localScale *= 1.25f; });

        var dnImg = MakeImage(bg.transform, "ScaleDn", new Vector2(220, 100), new Vector2(130, 20), SCALE_DN_COL);
        AddOutline(dnImg, PURPLE_BORDER, 3f);
        MakeLabel(dnImg.transform, "▼  SMALLER", 20, Vector2.zero, new Vector2(220, 100));
        RegisterGazeButton(dnImg, canvas, () => { if (selectedObject) selectedObject.transform.localScale *= 0.8f; });

        var closeImg = MakeImage(bg.transform, "CloseBtn", new Vector2(200, 40), new Vector2(0, -195), CLOSE_COLOR);
        AddOutline(closeImg, PURPLE_BORDER, 2f);
        MakeLabel(closeImg.transform, "CLOSE", 16, Vector2.zero, new Vector2(200, 40));
        RegisterGazeButton(closeImg, canvas, () => HideScaleUI());

        scaleUICanvas.SetActive(false);
    }

    // ─────────────────────────────────────────────────────────────
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
                Bounds b = renderers[0].bounds;
                foreach (var r in renderers) b.Encapsulate(r.bounds);
                lowestY = heldObject.transform.position.y - b.min.y;
            }
            heldObject.transform.position = new Vector3(
                heldObject.transform.position.x,
                hit.point.y + lowestY + 1.5f,
                heldObject.transform.position.z);
        }
        else
        {
            // Fallback: place on default floor at y=0
            heldObject.transform.position = new Vector3(
                heldObject.transform.position.x, 1.5f, heldObject.transform.position.z);
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
        UpdateStaticHolding();
    }

    // ─────────────────────────────────────────────────────────────
    //  SPAWN
    // ─────────────────────────────────────────────────────────────
    public void SpawnInstant(int index)
    {
        if (isHolding && heldObject != null) PlaceObject();
        if (prefabs == null || index >= prefabs.Length || prefabs[index] == null) return;
        SetMenuVisible(false);

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
        obj.layer  = LayerMask.NameToLayer("Ignore Raycast");
        UpdateStaticHolding();
    }

    public void SpawnItem(SpawnableItem item)
    {
        if (isHolding && heldObject != null) PlaceObject();
        if (item?.prefab == null) return;
        SetMenuVisible(false);

        GameObject obj = Instantiate(item.prefab);
        Camera cam = Camera.main;
        obj.transform.position = cam != null
            ? new Vector3(cam.transform.position.x + cam.transform.forward.x * 2.5f, 2f,
                          cam.transform.position.z + cam.transform.forward.z * 2.5f)
            : (rightHandController != null ? rightHandController.position : Vector3.zero);
        obj.transform.rotation = Quaternion.Euler(0, cam != null ? cam.transform.eulerAngles.y + 180f : 180f, 0);

        if (!obj.TryGetComponent<Rigidbody>(out var rb)) rb = obj.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        if (obj.GetComponentInChildren<Collider>() == null) obj.AddComponent<BoxCollider>();

        heldObject = obj;
        isHolding  = true;
        obj.layer  = LayerMask.NameToLayer("Ignore Raycast");
        UpdateStaticHolding();
    }

    // ─────────────────────────────────────────────────────────────
    //  MENU
    // ─────────────────────────────────────────────────────────────
    public void ToggleMenu() => SetMenuVisible(!menuVisible);

    void SetMenuVisible(bool v)
    {
        menuVisible = v;

        // Clear all gaze-button registrations from the menu canvas
        // (scale UI buttons are preserved because their canvas ref differs)
        if (!v)
        {
            // Remove entries belonging to the menu canvas
            Canvas menuCanvas = menuPanel != null ? menuPanel.GetComponent<Canvas>() : null;
            if (menuCanvas != null)
                gazeButtons.RemoveAll(gb => gb.canvas == menuCanvas);
        }

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
                // Re-register menu buttons when shown
                RegisterMenuButtons();
            }
            menuPanel.SetActive(v);
        }
        hoveredIndex = -1;
    }

    // ─────────────────────────────────────────────────────────────
    //  Build the menu canvas
    // ─────────────────────────────────────────────────────────────
    void BuildMenu()
    {
        menuPanel = new GameObject("SpawnMenuCanvas");
        menuPanel.layer = LayerMask.NameToLayer("UI");
        var canvas = menuPanel.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.WorldSpace;
        canvas.sortingOrder = 10;
        menuPanel.AddComponent<GraphicRaycaster>();

        var rt = menuPanel.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(700, 500);
        menuPanel.transform.localScale = Vector3.one * 0.001f;

        var bg = MakeImage(menuPanel.transform, "BG", new Vector2(700, 500), Vector2.zero, BG_DARK);
        AddOutline(bg, PURPLE_BORDER, 4f);

        MakeLabel(bg.transform, "SPAWN MENU", 20, new Vector2(-60, 200), new Vector2(300, 40));
        MakeLabel(bg.transform, "Gaze at button → LEFT TRIGGER", 10, new Vector2(-60, 175), new Vector2(300, 25));

        // Category tabs
        MakeCategoryTab(bg.transform, "FURNITURE", new Vector2(185, 120), "Furniture");
        MakeCategoryTab(bg.transform, "ITEMS",     new Vector2(185,  60), "Items");

        // Close button
        var closeImg = MakeImage(bg.transform, "CloseBtn", new Vector2(200, 40), new Vector2(185, -195), CLOSE_COLOR);
        AddOutline(closeImg, PURPLE_BORDER, 2f);
        MakeLabel(closeImg.transform, "CLOSE", 16, Vector2.zero, new Vector2(200, 40));
        // (registered in RegisterMenuButtons)

        // Grid panel
        var gridImg = MakeImage(bg.transform, "GridPanel", new Vector2(310, 320), new Vector2(-160, 20),
                                new Color(0.10f, 0.12f, 0.18f, 1f));
        AddOutline(gridImg, PURPLE_BORDER, 3f);
        gridPanel = gridImg.gameObject;

        var grid             = gridPanel.AddComponent<GridLayoutGroup>();
        grid.cellSize        = new Vector2(64, 64);
        grid.spacing         = new Vector2(8, 8);
        grid.padding         = new RectOffset(12, 12, 12, 12);
        grid.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 4;
    }

    // Called each time the menu becomes visible so gaze-buttons are current
    void RegisterMenuButtons()
    {
        Canvas menuCanvas = menuPanel.GetComponent<Canvas>();
        // Remove stale menu entries
        gazeButtons.RemoveAll(gb => gb.canvas == menuCanvas);

        // Close button
        var closeImg = menuPanel.transform.Find("BG/CloseBtn")?.GetComponent<Image>();
        if (closeImg != null)
            RegisterGazeButton(closeImg, menuCanvas, () => SetMenuVisible(false));

        // Category tabs
        var furnitureTab = menuPanel.transform.Find("BG/FURNITURETab")?.GetComponent<Image>();
        if (furnitureTab != null)
            RegisterGazeButton(furnitureTab, menuCanvas, () => SetCategory("Furniture"));

        var itemsTab = menuPanel.transform.Find("BG/ITEMSTab")?.GetComponent<Image>();
        if (itemsTab != null)
            RegisterGazeButton(itemsTab, menuCanvas, () => SetCategory("Items"));

        // Grid item buttons
        RefreshGrid();
    }

    void RefreshGrid()
    {
        if (gridPanel == null) return;

        Canvas menuCanvas = menuPanel.GetComponent<Canvas>();
        // Remove old grid-item gaze-buttons
        gazeButtons.RemoveAll(gb => gb.canvas == menuCanvas && gb.rect != null &&
                                    gb.rect.IsChildOf(gridPanel.transform));

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
                irt.sizeDelta = new Vector2(52, 52);
                irt.anchoredPosition = Vector2.zero;
            }
            else
                MakeLabel(cell.transform, item.itemName, 10, Vector2.zero, new Vector2(60, 60));

            var captured = item;
            RegisterGazeButton(cell, menuCanvas, () => SpawnItem(captured));
        }
    }

    void SetCategory(string cat)
    {
        activeCategory = cat;
        // Update tab colors
        UpdateTabColors();
        RefreshGrid();
    }

    void UpdateTabColors()
    {
        var furnitureTab = menuPanel?.transform.Find("BG/FURNITURETab")?.GetComponent<Image>();
        var itemsTab     = menuPanel?.transform.Find("BG/ITEMSTab")?.GetComponent<Image>();

        if (furnitureTab != null)
        {
            furnitureTab.color = activeCategory == "Furniture" ? TEAL_ACTIVE : BTN_NORMAL;
            // Update normalColor in gaze-button list
            for (int i = 0; i < gazeButtons.Count; i++)
            {
                var gb = gazeButtons[i];
                if (gb.image == furnitureTab) { var g2 = gb; g2.normalColor = furnitureTab.color; gazeButtons[i] = g2; }
            }
        }
        if (itemsTab != null)
        {
            itemsTab.color = activeCategory == "Items" ? TEAL_ACTIVE : BTN_NORMAL;
            for (int i = 0; i < gazeButtons.Count; i++)
            {
                var gb = gazeButtons[i];
                if (gb.image == itemsTab) { var g2 = gb; g2.normalColor = itemsTab.color; gazeButtons[i] = g2; }
            }
        }
    }

    void MakeCategoryTab(Transform parent, string label, Vector2 pos, string category)
    {
        var img = MakeImage(parent, label + "Tab", new Vector2(220, 45), pos,
                            category == activeCategory ? TEAL_ACTIVE : BTN_NORMAL);
        AddOutline(img, PURPLE_BORDER, 2f);
        MakeLabel(img.transform, label, 15, Vector2.zero, new Vector2(220, 45));
        // Gaze-buttons for tabs are registered in RegisterMenuButtons()
    }

    // ─────────────────────────────────────────────────────────────
    //  UI HELPERS
    // ─────────────────────────────────────────────────────────────
    Image MakeImage(Transform parent, string name, Vector2 size, Vector2 pos, Color color)
    {
        var go  = new GameObject(name);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        var rt  = go.GetComponent<RectTransform>();
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