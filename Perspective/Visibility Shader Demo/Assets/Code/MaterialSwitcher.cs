using UnityEngine;
using TinyRocketGames.Perspective.VisibilityShader;

[RequireComponent(typeof(PerspectiveObject))]
public class MaterialSwitcher : MonoBehaviour {

    public Material alternativeMaterial;

    private new Renderer renderer;
    private Material defaultMaterial;
    private PerspectiveObject perspectiveObj;

    void Start()
    {
        renderer = GetComponent<Renderer>();
        defaultMaterial = renderer.sharedMaterial;
        perspectiveObj = GetComponent<PerspectiveObject>();
    }

    // Update is called once per frame
    void Update()
    {
        renderer.sharedMaterial = (perspectiveObj.Visible) ? defaultMaterial : alternativeMaterial;
    }
}
