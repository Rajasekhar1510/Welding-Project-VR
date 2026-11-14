using UnityEngine;

[RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
public class WeldingBlobSet : MonoBehaviour
{
    [SerializeField] private Material blobHotMaterial;
    [SerializeField] private Material blobCooledMaterial;
    [SerializeField] private Mesh flatBlobMesh;
    [SerializeField] private float coolingDelay = 1.0f;
    [SerializeField] private float coolingFade = 2.0f;
    [SerializeField] private Color fadeToCool = Color.grey;

    private MeshRenderer meshRenderer;
    private MeshFilter meshFilter;
    private bool coolingStarted = false;

    private void Awake()
    {
        meshRenderer = GetComponent<MeshRenderer>();
        meshFilter = GetComponent<MeshFilter>();
    }

    private void Start()
    {
        if (!coolingStarted)
        {
            ShowGlow();
        }
    }

    public void ShowGlow()
    {
        coolingStarted = true;

        // Cancel any previous tweens on this blob just in case
        LeanTween.cancel(gameObject);

        if (blobHotMaterial != null)
        {
            meshRenderer.material = blobHotMaterial;
        }

        // 1. Wait for coolingDelay before starting the fade
        LeanTween.value(gameObject, 0f, 1f, coolingDelay).setOnComplete(() =>
        {
            Material newMaterial = Instantiate(blobHotMaterial);
            meshRenderer.material = newMaterial;

            // 2. Fade color and emission from glowing hot to cool grey
            LeanTween.value(gameObject, 0f, 1f, coolingFade).setOnUpdate((float val) =>
            {
                newMaterial.color = Color.Lerp(newMaterial.color, fadeToCool, val);
                newMaterial.SetColor("_EmissionColor", new Color(1f - val, 1f - val, 0f, 1f - val));
            })
            .setOnComplete(() =>
            {
                // 3. Swap to cooled material and flattened mesh
                if (blobCooledMaterial != null)
                    meshRenderer.material = blobCooledMaterial;

                if (flatBlobMesh != null)
                    meshFilter.mesh = flatBlobMesh;

                Destroy(newMaterial);
            });
        });
    }

    private void OnDestroy()
    {
        LeanTween.cancel(gameObject);
    }
}