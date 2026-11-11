using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

public class PerformanceReadout : MonoBehaviour
{
    private const string labelName = "PerformanceLabel";
    private const float refreshInterval = 0.25f;

    [SerializeField] private UIDocument document;

    private EntityQuery birdQuery;
    private Label label;
    private float smoothedDelta = 0.016f;
    private float refreshTimer;

    private void Start()
    {
        EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        birdQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<BirdData>());
    }

    private void Update()
    {
        smoothedDelta = Mathf.Lerp(smoothedDelta, Time.unscaledDeltaTime, 0.05f);

        refreshTimer -= Time.unscaledDeltaTime;

        if (refreshTimer > 0f)
            return;

        refreshTimer = refreshInterval;

        if (label == null)
            label = document.rootVisualElement.Q<Label>(labelName);

        int birdCount = birdQuery.CalculateEntityCount();
        label.text = $"{1f / smoothedDelta:0} FPS ({smoothedDelta * 1000f:0.0} ms)\nBirds: {birdCount}";
    }
}
