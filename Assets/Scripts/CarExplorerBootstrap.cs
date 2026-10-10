using UnityEngine;

internal static class CarExplorerBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallCarExplorer()
    {
        GameObject car = GameObject.Find("CarConcept");
        if (car == null)
        {
            Debug.LogError("[Car Explorer] SampleScene has no active CarConcept object.");
            return;
        }

        if (car.GetComponent<CarExplorerController>() == null)
            car.AddComponent<CarExplorerController>();
    }
}
