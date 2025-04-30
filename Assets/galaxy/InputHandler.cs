// using UnityEngine;

// public class InputHandler : MonoBehaviour
// {
//     // Reference to the GalaxyGenerator
//     private GalaxyGenerator galaxyManager;

//     private void Start()
//     {
//         // Find the GalaxyGenerator in the scene
//         galaxyManager = GalaxyGenerator.Instance;
//         if (galaxyManager == null)
//         {
//             Debug.LogError("GalaxyGenerator not found! InputHandler requires GalaxyGenerator to be in the scene.");
//         }
//     }

//     private void Update()
//     {
//         // Handle ESC key to return to galaxy view
//         if (Input.GetKeyDown(KeyCode.Escape))
//         {
//             if (galaxyManager != null)
//             {
//                 galaxyManager.ReturnToGalaxyView();
//             }
//         }
//     }
// }