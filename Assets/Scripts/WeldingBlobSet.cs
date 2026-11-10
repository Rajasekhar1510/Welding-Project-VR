using System.Collections;
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
    private Coroutine coolingRoutine;

    private void Awake()
    {
        meshRenderer = GetComponent<MeshRenderer>();
        meshFilter = GetComponent<MeshFilter>();
    }

    public void ShowGlow()
    {
        if (coolingRoutine != null) StopCoroutine(coolingRoutine);
        coolingRoutine = StartCoroutine(CoolingSequence());
    }

    private IEnumerator CoolingSequence()
    {
        // 1. Start glowing hot
        Material runtimeHotMat = new Material(blobHotMaterial);
        meshRenderer.material = runtimeHotMat;
        Color initialColor = runtimeHotMat.color;

        yield return new WaitForSeconds(coolingDelay);

        // 2. Gradually cool down color and emission
        float elapsed = 0f;
        while (elapsed < coolingFade)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / coolingFade);

            runtimeHotMat.color = Color.Lerp(initialColor, fadeToCool, t);
            runtimeHotMat.SetColor("_EmissionColor", new Color(1f - t, 1f - t, 0f, 1f - t));

            yield return null;
        }

        // 3. Swap to final cooled material and flattened bead mesh
        if (blobCooledMaterial != null) meshRenderer.material = blobCooledMaterial;
        if (flatBlobMesh != null) meshFilter.mesh = flatBlobMesh;

        Destroy(runtimeHotMat);
    }
}