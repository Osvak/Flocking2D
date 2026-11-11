using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[UpdateBefore(typeof(BirdSpawnerSystem))]
partial class ScreenBoundsSystem : SystemBase
{
    private int lastScreenWidth;
    private int lastScreenHeight;
    private float lastOrthographicSize;
    private float3 lastCameraPosition;

    protected override void OnUpdate()
    {
        Camera mainCamera = Camera.main;

        if (mainCamera == null)
        {
            return;
        }

        float3 cameraPosition = mainCamera.transform.position;
        bool hasBounds = SystemAPI.HasSingleton<ScreenBoundsData>();

        bool unchanged = hasBounds
            && Screen.width == lastScreenWidth
            && Screen.height == lastScreenHeight
            && mainCamera.orthographicSize == lastOrthographicSize
            && math.all(cameraPosition == lastCameraPosition);

        if (unchanged)
        {
            return;
        }

        lastScreenWidth = Screen.width;
        lastScreenHeight = Screen.height;
        lastOrthographicSize = mainCamera.orthographicSize;
        lastCameraPosition = cameraPosition;

        float3 minPos = mainCamera.ViewportToWorldPoint(new Vector3(0, 0, 0));
        float3 maxPos = mainCamera.ViewportToWorldPoint(new Vector3(1, 1, 0));

        ScreenBoundsData bounds = new ScreenBoundsData { min = minPos.xy, max = maxPos.xy };

        if (hasBounds)
        {
            SystemAPI.SetSingleton(bounds);
        }
        else
        {
            EntityManager.CreateSingleton(bounds);
        }
    }
}