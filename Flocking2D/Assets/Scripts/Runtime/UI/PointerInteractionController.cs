using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class PointerInteractionController : MonoBehaviour
{
    private const float SelectionRingSize = 30f;

    [SerializeField] private UIDocument settingsDocument;
    [SerializeField] private Camera targetCamera;
    [SerializeField] private float pickRadius = 0.6f;

    private VisualElement root;
    private VisualElement settingsPanel;
    private VisualElement infoPanel;
    private VisualElement selectionRing;
    private VisualElement mouseRing;
    private VisualElement debugOverlay;
    private IPanel panel;

    private Label birdNameLabel;
    private Label positionValueLabel;
    private Label headingValueLabel;
    private Label speedValueLabel;
    private Label neighborsValueLabel;
    private Label neighborRadiusValueLabel;
    private Label personalRadiusValueLabel;
    private Button deselectButton;

    private EntityManager entityManager;
    private EntityQuery birdQuery;
    private EntityQuery configQuery;
    private EntityQuery mouseQuery;

    private Entity selectedBird;

    private bool queriesCreated;
    private bool isInitialized;
    private bool hasSelection;

    private struct DebugOverlayState
    {
        public bool isVisible;
        public Vector2 center;
        public float headingAngle;
        public float halfFieldOfView;
        public float personalRadiusPixels;
        public float neighborRadiusPixels;
        public bool drawPersonalRadius;
        public bool drawNeighborRadius;
        public bool drawFieldOfView;
    }

    private DebugOverlayState debugState;

    private void OnEnable()
    {
        root = GetComponent<UIDocument>().rootVisualElement;

        infoPanel = root.Q<VisualElement>("BirdInfoPanel");
        selectionRing = root.Q<VisualElement>("SelectionRing");
        mouseRing = root.Q<VisualElement>("MouseRing");

        birdNameLabel = root.Q<Label>("BirdNameLabel");
        positionValueLabel = root.Q<Label>("PositionValue");
        headingValueLabel = root.Q<Label>("HeadingValue");
        speedValueLabel = root.Q<Label>("SpeedValue");
        neighborsValueLabel = root.Q<Label>("NeighborsValue");
        neighborRadiusValueLabel = root.Q<Label>("NeighborRadiusValue");
        personalRadiusValueLabel = root.Q<Label>("PersonalRadiusValue");

        MakeCircle(selectionRing);
        MakeCircle(mouseRing);

        selectionRing.style.width = SelectionRingSize;
        selectionRing.style.height = SelectionRingSize;

        deselectButton = root.Q<Button>("DeselectButton");
        deselectButton.clicked += Deselect;

        debugOverlay = new VisualElement { pickingMode = PickingMode.Ignore };
        debugOverlay.style.position = Position.Absolute;
        debugOverlay.style.left = 0f;
        debugOverlay.style.top = 0f;
        debugOverlay.style.right = 0f;
        debugOverlay.style.bottom = 0f;
        debugOverlay.generateVisualContent += OnGenerateDebugContent;
        root.Insert(0, debugOverlay);
    }

    private void OnDisable()
    {
        deselectButton.clicked -= Deselect;
        debugOverlay.generateVisualContent -= OnGenerateDebugContent;
        debugOverlay.RemoveFromHierarchy();

        // stop pulling birds if the right button was held when this got disabled
        World world = World.DefaultGameObjectInjectionWorld;

        if (isInitialized && world != null && world.IsCreated)
            mouseQuery.SetSingleton(new MouseInputData());

        hasSelection = false;
        isInitialized = false;
    }

    private void Update()
    {
        if (!isInitialized)
        {
            TryInitialize();
            return;
        }

        UpdatePointer();
        UpdateSelection();
    }

    private void TryInitialize()
    {
        World world = World.DefaultGameObjectInjectionWorld;

        if (world == null || !world.IsCreated)
            return;

        if (root.panel == null)
            return;

        if (!queriesCreated)
        {
            entityManager = world.EntityManager;
            birdQuery = entityManager.CreateEntityQuery(typeof(BirdData), typeof(LocalTransform));
            configQuery = entityManager.CreateEntityQuery(typeof(SimulationConfigData));
            mouseQuery = entityManager.CreateEntityQuery(typeof(MouseInputData));
            queriesCreated = true;
        }

        if (configQuery.IsEmpty)
            return;

        if (mouseQuery.IsEmpty)
            entityManager.CreateEntity(typeof(MouseInputData));

        panel = root.panel;
        settingsPanel = settingsDocument.rootVisualElement;

        isInitialized = true;
    }

    private void UpdatePointer()
    {
        Mouse mouse = Mouse.current;

        if (mouse == null)
            return;

        Vector2 screenPosition = mouse.position.ReadValue();

        bool isOverUi = IsOverUi(screenPosition);
        bool hasWorldPosition = TryGetWorldPosition(screenPosition, out Vector3 worldPosition);
        bool isAttracting = hasWorldPosition && !isOverUi && mouse.rightButton.isPressed;

        mouseQuery.SetSingleton(new MouseInputData
        {
            position = new float2(worldPosition.x, worldPosition.y),
            isActive = isAttracting
        });

        UpdateMouseRing(worldPosition, isAttracting);

        if (mouse.leftButton.wasPressedThisFrame && hasWorldPosition && !isOverUi)
            SelectBirdAt(new float2(worldPosition.x, worldPosition.y));
    }

    private bool IsOverUi(Vector2 screenPosition)
    {
        Vector2 flippedPosition = new Vector2(screenPosition.x, Screen.height - screenPosition.y);

        if (IsPickingUi(panel, root, flippedPosition))
            return true;

        return settingsPanel.panel != panel && IsPickingUi(settingsPanel.panel, settingsPanel, flippedPosition);
    }

    private static bool IsPickingUi(IPanel targetPanel, VisualElement documentRoot, Vector2 flippedScreenPosition)
    {
        Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(targetPanel, flippedScreenPosition);
        VisualElement picked = targetPanel.Pick(panelPosition);

        return picked != null && picked != documentRoot && picked != targetPanel.visualTree;
    }

    private bool TryGetWorldPosition(Vector2 screenPosition, out Vector3 worldPosition)
    {
        Ray ray = targetCamera.ScreenPointToRay(screenPosition);
        Plane birdPlane = new Plane(Vector3.forward, Vector3.zero);

        if (birdPlane.Raycast(ray, out float distance))
        {
            worldPosition = ray.GetPoint(distance);
            return true;
        }

        worldPosition = Vector3.zero;
        return false;
    }

    private void UpdateMouseRing(Vector3 worldPosition, bool isActive)
    {
        SimulationConfigData config = configQuery.GetSingleton<SimulationConfigData>();
        bool isVisible = isActive && config.showMouseRadius;

        mouseRing.style.display = isVisible ? DisplayStyle.Flex : DisplayStyle.None;

        if (!isVisible)
            return;

        float radius = config.mouseRadius;

        Vector2 center = RuntimePanelUtils.CameraTransformWorldToPanel(panel, worldPosition, targetCamera);
        Vector2 edge = RuntimePanelUtils.CameraTransformWorldToPanel(panel, worldPosition + Vector3.right * radius, targetCamera);
        float diameter = Mathf.Abs(edge.x - center.x) * 2f;

        mouseRing.style.width = diameter;
        mouseRing.style.height = diameter;
        mouseRing.style.left = center.x - diameter * 0.5f;
        mouseRing.style.top = center.y - diameter * 0.5f;
    }

    private void SelectBirdAt(float2 worldPosition)
    {
        Entity closestBird = Entity.Null;
        float closestDistanceSq = pickRadius * pickRadius;

        using (NativeArray<Entity> entities = birdQuery.ToEntityArray(Allocator.Temp))
        using (NativeArray<LocalTransform> transforms = birdQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp))
        {
            for (int i = 0; i < entities.Length; i++)
            {
                float distanceSq = math.distancesq(transforms[i].Position.xy, worldPosition);

                if (distanceSq > closestDistanceSq)
                    continue;

                closestDistanceSq = distanceSq;
                closestBird = entities[i];
            }
        }

        if (closestBird == Entity.Null)
        {
            Deselect();
            return;
        }

        selectedBird = closestBird;
        hasSelection = true;

        infoPanel.style.display = DisplayStyle.Flex;
        selectionRing.style.display = DisplayStyle.Flex;
    }

    private void Deselect()
    {
        hasSelection = false;
        selectedBird = Entity.Null;

        infoPanel.style.display = DisplayStyle.None;
        selectionRing.style.display = DisplayStyle.None;

        debugState.isVisible = false;
        debugOverlay.MarkDirtyRepaint();
    }

    private void UpdateSelection()
    {
        if (!hasSelection)
            return;

        if (!entityManager.Exists(selectedBird))
        {
            Deselect();
            return;
        }

        LocalTransform localTransform = entityManager.GetComponentData<LocalTransform>(selectedBird);
        BirdMovement birdMovement = entityManager.GetComponentData<BirdMovement>(selectedBird);
        BirdData birdData = entityManager.GetComponentData<BirdData>(selectedBird);
        SimulationConfigData config = configQuery.GetSingleton<SimulationConfigData>();
        DynamicBuffer<CloseNeighbors> neighbors = entityManager.GetBuffer<CloseNeighbors>(selectedBird, true);

        float3 forward = math.mul(localTransform.Rotation, new float3(0f, 1f, 0f));
        float heading = math.degrees(math.atan2(forward.y, forward.x));

        if (heading < 0f)
            heading += 360f;

        birdNameLabel.text = $"Bird {birdData.id}";
        positionValueLabel.text = $"{localTransform.Position.x:F2}, {localTransform.Position.y:F2}";
        headingValueLabel.text = $"{heading:F0}°";
        speedValueLabel.text = $"{birdMovement.speed:F2}";
        neighborsValueLabel.text = CountVisibleNeighbors(neighbors).ToString();
        neighborRadiusValueLabel.text = $"{config.neighborRadius:F2}";
        personalRadiusValueLabel.text = $"{config.personalRadius:F2}";

        Vector3 worldPosition = new Vector3(localTransform.Position.x, localTransform.Position.y, localTransform.Position.z);
        Vector2 center = RuntimePanelUtils.CameraTransformWorldToPanel(panel, worldPosition, targetCamera);

        selectionRing.style.left = center.x - SelectionRingSize * 0.5f;
        selectionRing.style.top = center.y - SelectionRingSize * 0.5f;

        UpdateDebugOverlay(worldPosition, forward, config, center);
    }

    private void UpdateDebugOverlay(Vector3 worldPosition, float3 forward, SimulationConfigData config, Vector2 center)
    {
        Vector2 ahead = RuntimePanelUtils.CameraTransformWorldToPanel(panel, worldPosition + new Vector3(forward.x, forward.y, forward.z), targetCamera);
        Vector2 headingVector = ahead - center;
        float pixelsPerUnit = headingVector.magnitude;

        debugState = new DebugOverlayState
        {
            isVisible = true,
            center = center,
            headingAngle = Mathf.Atan2(headingVector.y, headingVector.x) * Mathf.Rad2Deg,
            halfFieldOfView = config.fieldOfViewDegrees * 0.5f,
            personalRadiusPixels = config.personalRadius * pixelsPerUnit,
            neighborRadiusPixels = config.neighborRadius * pixelsPerUnit,
            drawPersonalRadius = config.showPersonalRadius,
            drawNeighborRadius = config.showNeighborRadius,
            drawFieldOfView = config.showFieldOfView
        };

        debugOverlay.MarkDirtyRepaint();
    }

    private void OnGenerateDebugContent(MeshGenerationContext context)
    {
        if (!debugState.isVisible)
            return;

        Painter2D painter = context.painter2D;
        painter.lineWidth = 2f;

        if (debugState.drawFieldOfView)
            DrawFieldOfView(painter);

        if (debugState.drawNeighborRadius)
            DrawCircle(painter, debugState.neighborRadiusPixels, new Color(0.3f, 1f, 0.4f, 0.9f));

        if (debugState.drawPersonalRadius)
            DrawCircle(painter, debugState.personalRadiusPixels, new Color(1f, 0.35f, 0.3f, 0.9f));
    }

    private void DrawCircle(Painter2D painter, float radius, Color color)
    {
        painter.strokeColor = color;
        painter.BeginPath();
        painter.Arc(debugState.center, radius, Angle.Degrees(0f), Angle.Degrees(360f));
        painter.Stroke();
    }

    private void DrawFieldOfView(Painter2D painter)
    {
        float radius = debugState.neighborRadiusPixels;
        float startAngle = debugState.headingAngle - debugState.halfFieldOfView;
        float endAngle = debugState.headingAngle + debugState.halfFieldOfView;

        if (debugState.halfFieldOfView < 180f)
        {
            painter.fillColor = new Color(1f, 0.25f, 0.2f, 0.12f);

            painter.BeginPath();
            painter.MoveTo(debugState.center);
            painter.LineTo(debugState.center + PolarOffset(endAngle, radius));
            painter.Arc(debugState.center, radius, Angle.Degrees(endAngle), Angle.Degrees(startAngle + 360f));
            painter.ClosePath();
            painter.Fill();

            painter.fillColor = new Color(1f, 0.85f, 0.2f, 0.15f);
            painter.strokeColor = new Color(1f, 0.85f, 0.2f, 0.9f);

            painter.BeginPath();
            painter.MoveTo(debugState.center);
            painter.LineTo(debugState.center + PolarOffset(startAngle, radius));
            painter.Arc(debugState.center, radius, Angle.Degrees(startAngle), Angle.Degrees(endAngle));
            painter.ClosePath();
            painter.Fill();
            painter.Stroke();
        }

        painter.strokeColor = Color.white;

        painter.BeginPath();
        painter.MoveTo(debugState.center);
        painter.LineTo(debugState.center + PolarOffset(debugState.headingAngle, radius));
        painter.Stroke();
    }

    private static Vector2 PolarOffset(float angleDegrees, float radius)
    {
        float angleRadians = angleDegrees * Mathf.Deg2Rad;

        return new Vector2(Mathf.Cos(angleRadians), Mathf.Sin(angleRadians)) * radius;
    }

    private static int CountVisibleNeighbors(DynamicBuffer<CloseNeighbors> neighbors)
    {
        int visibleCount = 0;

        for (int i = 0; i < neighbors.Length; i++)
        {
            if (neighbors[i].isVisible)
                visibleCount++;
        }

        return visibleCount;
    }

    private static void MakeCircle(VisualElement element)
    {
        StyleLength fullRadius = new StyleLength(new Length(50f, LengthUnit.Percent));

        element.style.borderTopLeftRadius = fullRadius;
        element.style.borderTopRightRadius = fullRadius;
        element.style.borderBottomLeftRadius = fullRadius;
        element.style.borderBottomRightRadius = fullRadius;
    }
}
