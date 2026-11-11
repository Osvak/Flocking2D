using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class SimulationUiController : MonoBehaviour
{
    private const string defaultHelpText = "Hover a setting to see what it does.";

    private VisualElement panel;
    private Label helpLabel;

    private Slider alignmentWeightSlider;
    private Slider separationWeightSlider;
    private Slider cohesionWeightSlider;
    private Slider edgeWeightSlider;
    private Slider edgeMarginSlider;
    private Slider turnRateSlider;
    private Slider fieldOfViewDegreesSlider;
    private Slider neighborsRadiusSlider;
    private Slider personalRadiusSlider;
    private Slider cruiseSpeedSlider;
    private Slider minSpeedSlider;
    private Slider maxSpeedSlider;
    private Slider speedMatchWeightSlider;
    private Slider catchUpGainSlider;
    private Slider speedResponseSlider;
    private Slider mouseRadiusSlider;
    private Slider mouseWeightSlider;
    private Toggle showMouseRadiusToggle;
    private Toggle showPersonalRadiusToggle;
    private Toggle showNeighborRadiusToggle;
    private Toggle showFieldOfViewToggle;

    private SliderInt birdCountSlider;

    private Button pauseButton;
    private Button[] presetButtons;

    private SimulationPreset currentPreset;
    private SimulationConfigData customConfig;

    private EntityManager entityManager;
    private EntityQuery configQuery;
    private EntityQuery spawnerQuery;
    private EntityQuery resetQuery;
    private SimulationConfigData defaultConfig;

    private bool queriesCreated;
    private bool isInitialized;
    private bool isPaused;

    private void OnEnable()
    {
        VisualElement root = GetComponent<UIDocument>().rootVisualElement;

        panel = root.Q<VisualElement>("Panel");
        panel.SetEnabled(false);

        Foldout settingsFoldout = root.Q<Foldout>("SettingsFoldout");
        settingsFoldout.contentContainer.style.flexGrow = 1f;
        settingsFoldout.RegisterValueChangedCallback(evt =>
        {
            if (evt.target == settingsFoldout)
                panel.style.bottom = evt.newValue ? new StyleLength(10f) : new StyleLength(StyleKeyword.Auto);
        });

        helpLabel = root.Q<Label>("HelpLabel");
        helpLabel.text = defaultHelpText;

        alignmentWeightSlider = BindSlider(root, "AlignmentWeight");
        separationWeightSlider = BindSlider(root, "SeparationWeight");
        cohesionWeightSlider = BindSlider(root, "CohesionWeight");
        edgeWeightSlider = BindSlider(root, "EdgeWeight");
        edgeMarginSlider = BindSlider(root, "EdgeMargin");
        turnRateSlider = BindSlider(root, "TurnRate");
        fieldOfViewDegreesSlider = BindSlider(root, "FieldOfViewDegrees");
        neighborsRadiusSlider = BindSlider(root, "NeighborsRadius");
        personalRadiusSlider = BindSlider(root, "PersonalRadius");
        cruiseSpeedSlider = BindSlider(root, "CruiseSpeed");
        minSpeedSlider = BindSlider(root, "MinSpeed");
        maxSpeedSlider = BindSlider(root, "MaxSpeed");
        speedMatchWeightSlider = BindSlider(root, "SpeedMatchWeight");
        catchUpGainSlider = BindSlider(root, "CatchUpGain");
        speedResponseSlider = BindSlider(root, "SpeedResponse");
        mouseRadiusSlider = BindSlider(root, "MouseRadius");
        mouseWeightSlider = BindSlider(root, "MouseWeight");

        showMouseRadiusToggle = BindToggle(root, "ShowMouseRadius");
        showPersonalRadiusToggle = BindToggle(root, "ShowPersonalRadius");
        showNeighborRadiusToggle = BindToggle(root, "ShowNeighborRadius");
        showFieldOfViewToggle = BindToggle(root, "ShowFieldOfView");

        birdCountSlider = root.Q<SliderInt>("BirdCount");
        birdCountSlider.highValue = SpawnerData.MaxBirdAmount;
        BindDescription(birdCountSlider);

        Button resetSimulationButton = root.Q<Button>("ResetSimulationButton");
        resetSimulationButton.clicked += OnResetSimulationClicked;
        BindDescription(resetSimulationButton);

        Button resetValuesButton = root.Q<Button>("ResetValuesButton");
        resetValuesButton.clicked += OnResetValuesClicked;
        BindDescription(resetValuesButton);

        pauseButton = root.Q<Button>("PauseButton");
        pauseButton.clicked += OnPauseClicked;
        BindDescription(pauseButton);

        presetButtons = new[]
        {
            BindPresetButton(root, "PresetCustom", SimulationPreset.Custom),
            BindPresetButton(root, "PresetMurmuration", SimulationPreset.Murmuration),
            BindPresetButton(root, "PresetTight", SimulationPreset.TightSchool),
            BindPresetButton(root, "PresetChaotic", SimulationPreset.Chaotic)
        };
    }

    private void OnDisable()
    {
        Time.timeScale = 1f;
    }

    private void Update()
    {
        if (isInitialized)
            return;

        TryInitialize();
    }

    private void TryInitialize()
    {
        World world = World.DefaultGameObjectInjectionWorld;

        if (world == null || !world.IsCreated)
            return;

        if (!queriesCreated)
        {
            entityManager = world.EntityManager;
            configQuery = entityManager.CreateEntityQuery(typeof(SimulationConfigData));
            spawnerQuery = entityManager.CreateEntityQuery(typeof(SpawnerData));
            resetQuery = entityManager.CreateEntityQuery(typeof(ResetSimulationRequest));
            queriesCreated = true;
        }

        if (configQuery.IsEmpty || spawnerQuery.IsEmpty)
            return;

        defaultConfig = configQuery.GetSingleton<SimulationConfigData>();
        ApplyConfigToUi(defaultConfig);

        customConfig = defaultConfig;
        currentPreset = SimulationPreset.Custom;
        UpdatePresetButtons();

        birdCountSlider.SetValueWithoutNotify(spawnerQuery.GetSingleton<SpawnerData>().birdAmount);

        panel.SetEnabled(true);
        isInitialized = true;
    }

    private Slider BindSlider(VisualElement root, string sliderName)
    {
        Slider slider = root.Q<Slider>(sliderName);
        slider.RegisterValueChangedCallback(OnSliderChanged);
        BindDescription(slider);

        return slider;
    }

    private Toggle BindToggle(VisualElement root, string toggleName)
    {
        Toggle toggle = root.Q<Toggle>(toggleName);
        toggle.RegisterValueChangedCallback(OnToggleChanged);
        BindDescription(toggle);

        return toggle;
    }

    private void BindDescription(VisualElement element)
    {
        string description = element.tooltip;
        element.tooltip = string.Empty;

        element.RegisterCallback<PointerEnterEvent>(evt => helpLabel.text = description);
        element.RegisterCallback<PointerLeaveEvent>(evt => helpLabel.text = defaultHelpText);
    }

    private void OnSliderChanged(ChangeEvent<float> changeEvent)
    {
        if (changeEvent.target != mouseRadiusSlider && changeEvent.target != mouseWeightSlider && currentPreset != SimulationPreset.Custom)
        {
            currentPreset = SimulationPreset.Custom;
            UpdatePresetButtons();
        }

        PushConfig();
    }

    private void OnToggleChanged(ChangeEvent<bool> changeEvent)
    {
        PushConfig();
    }

    private void PushConfig()
    {
        if (!isInitialized)
            return;

        SimulationConfigData config = ReadConfigFromUi();

        if (config.minSpeed > config.maxSpeed)
        {
            config.maxSpeed = config.minSpeed;
            maxSpeedSlider.SetValueWithoutNotify(config.maxSpeed);
        }

        configQuery.SetSingleton(config);

        if (currentPreset == SimulationPreset.Custom)
            customConfig = config;
    }

    private SimulationConfigData ReadConfigFromUi()
    {
        return new SimulationConfigData
        {
            alignmentWeight = alignmentWeightSlider.value,
            separationWeight = separationWeightSlider.value,
            cohesionWeight = cohesionWeightSlider.value,
            edgeWeight = edgeWeightSlider.value,
            edgeMargin = edgeMarginSlider.value,
            turnRate = turnRateSlider.value,
            fieldOfViewDegrees = fieldOfViewDegreesSlider.value,
            neighborsRadius = neighborsRadiusSlider.value,
            personalRadius = personalRadiusSlider.value,
            cruiseSpeed = cruiseSpeedSlider.value,
            minSpeed = minSpeedSlider.value,
            maxSpeed = maxSpeedSlider.value,
            speedMatchWeight = speedMatchWeightSlider.value,
            catchUpGain = catchUpGainSlider.value,
            speedResponse = speedResponseSlider.value,
            mouseRadius = mouseRadiusSlider.value,
            mouseWeight = mouseWeightSlider.value,
            showMouseRadius = showMouseRadiusToggle.value,
            showPersonalRadius = showPersonalRadiusToggle.value,
            showNeighborRadius = showNeighborRadiusToggle.value,
            showFieldOfView = showFieldOfViewToggle.value
        };
    }

    private void ApplyConfigToUi(SimulationConfigData config)
    {
        alignmentWeightSlider.SetValueWithoutNotify(config.alignmentWeight);
        separationWeightSlider.SetValueWithoutNotify(config.separationWeight);
        cohesionWeightSlider.SetValueWithoutNotify(config.cohesionWeight);
        edgeWeightSlider.SetValueWithoutNotify(config.edgeWeight);
        edgeMarginSlider.SetValueWithoutNotify(config.edgeMargin);
        turnRateSlider.SetValueWithoutNotify(config.turnRate);
        fieldOfViewDegreesSlider.SetValueWithoutNotify(config.fieldOfViewDegrees);
        neighborsRadiusSlider.SetValueWithoutNotify(config.neighborsRadius);
        personalRadiusSlider.SetValueWithoutNotify(config.personalRadius);
        cruiseSpeedSlider.SetValueWithoutNotify(config.cruiseSpeed);
        minSpeedSlider.SetValueWithoutNotify(config.minSpeed);
        maxSpeedSlider.SetValueWithoutNotify(config.maxSpeed);
        speedMatchWeightSlider.SetValueWithoutNotify(config.speedMatchWeight);
        catchUpGainSlider.SetValueWithoutNotify(config.catchUpGain);
        speedResponseSlider.SetValueWithoutNotify(config.speedResponse);
        mouseRadiusSlider.SetValueWithoutNotify(config.mouseRadius);
        mouseWeightSlider.SetValueWithoutNotify(config.mouseWeight);
        showMouseRadiusToggle.SetValueWithoutNotify(config.showMouseRadius);
        showPersonalRadiusToggle.SetValueWithoutNotify(config.showPersonalRadius);
        showNeighborRadiusToggle.SetValueWithoutNotify(config.showNeighborRadius);
        showFieldOfViewToggle.SetValueWithoutNotify(config.showFieldOfView);
    }

    private Button BindPresetButton(VisualElement root, string buttonName, SimulationPreset preset)
    {
        Button button = root.Q<Button>(buttonName);
        button.clicked += () => OnPresetClicked(preset);
        BindDescription(button);

        return button;
    }

    private void OnPresetClicked(SimulationPreset preset)
    {
        if (!isInitialized || preset == currentPreset)
            return;

        SimulationConfigData config = SimulationPresets.Apply(preset, ReadConfigFromUi(), customConfig);

        currentPreset = preset;

        ApplyConfigToUi(config);
        configQuery.SetSingleton(config);
        UpdatePresetButtons();
    }

    private void UpdatePresetButtons()
    {
        for (int i = 0; i < presetButtons.Length; i++)
        {
            bool isSelected = i == (int)currentPreset;

            presetButtons[i].style.backgroundColor = isSelected ? new StyleColor(new Color(0.2f, 0.45f, 0.8f)) : new StyleColor(StyleKeyword.Null);
        }
    }

    private void OnResetSimulationClicked()
    {
        if (!isInitialized || !resetQuery.IsEmpty)
            return;

        SpawnerData spawner = spawnerQuery.GetSingleton<SpawnerData>();
        spawner.birdAmount = birdCountSlider.value;
        spawnerQuery.SetSingleton(spawner);

        Entity request = entityManager.CreateEntity(typeof(ResetSimulationRequest));
        entityManager.SetComponentData(request, new ResetSimulationRequest { seed = (uint)Random.Range(1, int.MaxValue) });
    }

    private void OnResetValuesClicked()
    {
        if (!isInitialized)
            return;

        ApplyConfigToUi(defaultConfig);
        configQuery.SetSingleton(defaultConfig);

        customConfig = defaultConfig;
        currentPreset = SimulationPreset.Custom;
        UpdatePresetButtons();
    }

    private void OnPauseClicked()
    {
        isPaused = !isPaused;

        Time.timeScale = isPaused ? 0f : 1f;
        pauseButton.text = isPaused ? "Resume" : "Pause";
    }
}
