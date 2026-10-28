using UnityEngine;

internal static class CarExplorerBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallCarExplorer()
    {
        // Runs for whatever scene loads first; scenes without the showroom car need no explorer.
        GameObject car = GameObject.Find("CarConcept");
        if (car == null)
            return;

        if (car.GetComponent<CarExplorerController>() == null)
            car.AddComponent<CarExplorerController>();
    }
}
