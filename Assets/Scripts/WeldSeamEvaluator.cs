using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;

public class WeldSeamEvaluator : MonoBehaviour
{
    [Header("Optional Assessment Scoring (For Assessment Scene)")]
    [Tooltip("Leave empty in the training scene; assign WeldingAssessmentManager in the assessment scene")]
    [SerializeField] private WeldingAssessmentManager assessmentManager;

    [Header("Scene Navigation")]
    [Tooltip("Type the name of the next scene, or leave empty to load the next scene in Build Settings (buildIndex + 1)")]
    [SerializeField] private string nextSceneName = "";

    [Header("Metal Piece to Unlock on Success")]
    [Tooltip("Drag WeldPanels_All (the parent with Grabbable) here")]
    [SerializeField] private GameObject metalPieceGrabbableRoot;
    [SerializeField] private Behaviour[] grabComponentsToToggle;

    [Header("Seam Box Collider / Trigger")]
    [Tooltip("Drag the BoxCollider (Is Trigger = true) placed along the seam between the two squares")]
    [SerializeField] private BoxCollider seamBoxTrigger;
    [SerializeField] private int boxLengthSlices = 12;
    [Range(0.5f, 1f)]
    [SerializeField] private float requiredBoxCoverage = 0.85f;
    [SerializeField] private float minWeldTimeInsideBox = 1.5f;

    [Header("Out-of-Box & Same-Spot Damage Timers")]
    [SerializeField] private float outsideBoxWrongTime = 1.5f;
    [SerializeField] private float outsideBoxDamageTime = 3.5f;
    [SerializeField] private bool checkStationaryOverheatInsideBox = true;
    [SerializeField] private float stationarySpotRadius = 0.015f;

    [Header("Burn-Through Hole Settings (From unity_welding_simulator)")]
    [Tooltip("Drag the weldHoleMask prefab from unity_welding_simulator here")]
    [SerializeField] private GameObject weldHoleMaskPrefab;
    [Tooltip("Final world size of the burned hole in meters (e.g. 0.025 = 2.5 cm wide)")]
    [SerializeField] private Vector3 holeWorldScale = new Vector3(0.025f, 0.025f, 0.025f);
    [Tooltip("Adjust if the hole mesh needs a 90-degree rotation flip")]
    [SerializeField] private Vector3 holeRotationOffset = Vector3.zero;
    [Tooltip("Duration of the LeanTween hole expansion animation")]
    [SerializeField] private float holeExpandDuration = 0.2f;
    [Tooltip("Radius around the hole where existing weld blobs get cleared so they don't hide the hole")]
    [SerializeField] private float clearBlobsRadius = 0.025f;

    [Header("Audio Sources")]
    [SerializeField] private AudioSource voiceOverAudioSource;
    [SerializeField] private AudioSource sfxAudioSource;

    [Header("1. Right Way (Success) UI & Audio")]
    [SerializeField] private GameObject rightWayCanvas;
    [SerializeField] private AudioClip rightWayVoiceOver;

    [Header("2. Wrong Way (Outside Box > 1.5s) UI & Audio")]
    [SerializeField] private GameObject wrongWayCanvas;
    [SerializeField] private AudioClip wrongWayVoiceOver;
    [SerializeField] private float wrongWayVoiceCooldown = 4.0f;

    [Header("3. Damaged / Restart (Outside Box or Same Spot > 3-4s) UI & Audio")]
    [SerializeField] private GameObject damagedWarningCanvas;
    [SerializeField] private AudioClip warningSfx;
    [SerializeField] private AudioClip damagedVoiceOver;

    private bool[] sliceWelded;
    private int weldedSlicesCount = 0;
    private float totalTimeWeldedInsideBox = 0f;
    private float timeOutsideBox = 0f;

    private Vector3 currentSpotCenter = Vector3.positiveInfinity;
    private float timeAtSameSpot = 0f;
    private float lastWrongVoiceTime = -99f;

    // Tracks latest hit surface info and spawned blobs for hole creation
    private Vector3 lastHitPoint;
    private Vector3 lastHitNormal = Vector3.up;
    private Transform lastHitTransform;
    private readonly List<GameObject> spawnedBlobs = new List<GameObject>();

    public bool IsCompleted { get; private set; } = false;
    public bool IsDamaged { get; private set; } = false;
    public bool IsFinishedOrDamaged => IsCompleted || IsDamaged;

    private void Awake()
    {
        sliceWelded = new bool[Mathf.Max(4, boxLengthSlices)];

        if (seamBoxTrigger != null)
        {
            seamBoxTrigger.isTrigger = true;

            int ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
            if (ignoreRaycastLayer != -1)
            {
                seamBoxTrigger.gameObject.layer = ignoreRaycastLayer;
            }
        }

        if (rightWayCanvas != null) rightWayCanvas.SetActive(false);
        if (wrongWayCanvas != null) wrongWayCanvas.SetActive(false);
        if (damagedWarningCanvas != null) damagedWarningCanvas.SetActive(false);

        SetMetalPieceGrabbable(false);
    }

    // Overload that receives the full surface hit details and newly spawned blob from WeldingTorch
    public void RegisterWeldTick(
        Vector3 hitPoint,
        Vector3 hitNormal,
        Transform hitTransform,
        GameObject spawnedBlob,
        float deltaTime)
    {
        lastHitPoint = hitPoint;
        lastHitNormal = hitNormal;
        lastHitTransform = hitTransform;

        if (spawnedBlob != null)
        {
            spawnedBlobs.Add(spawnedBlob);
        }

        RegisterWeldTick(hitPoint, deltaTime);
    }

    public void RegisterWeldTick(Vector3 hitPoint, float deltaTime)
    {
        if (IsFinishedOrDamaged || seamBoxTrigger == null) return;

        lastHitPoint = hitPoint;

        // 1. Check if the player is holding the torch in the SAME spot for too long (inside OR outside the box)
        if (checkStationaryOverheatInsideBox)
        {
            if (Vector3.Distance(hitPoint, currentSpotCenter) <= stationarySpotRadius)
            {
                timeAtSameSpot += deltaTime;

                if (timeAtSameSpot >= outsideBoxDamageTime)
                {
                    TriggerDamagedState();
                    return;
                }
                else if (timeAtSameSpot >= outsideBoxWrongTime)
                {
                    TriggerWrongWayFeedback();
                }
            }
            else
            {
                currentSpotCenter = hitPoint;
                timeAtSameSpot = 0f;
            }
        }

        // 2. Check if the weld point is inside or outside the seam BoxCollider
        bool isInsideBox = EvaluatePointAgainstSeamBox(
            hitPoint,
            out float normalizedLengthPos,
            out float distanceOutsideMeters,
            out float lateralOffsetRatio
        );

        if (assessmentManager != null)
        {
            assessmentManager.RecordWeldSample(isInsideBox, distanceOutsideMeters, lateralOffsetRatio, deltaTime);
        }

        if (isInsideBox)
        {
            timeOutsideBox = 0f;
            totalTimeWeldedInsideBox += deltaTime;

            int sliceIndex = Mathf.Clamp(
                Mathf.FloorToInt(normalizedLengthPos * sliceWelded.Length),
                0,
                sliceWelded.Length - 1
            );

            if (!sliceWelded[sliceIndex])
            {
                sliceWelded[sliceIndex] = true;
                weldedSlicesCount++;

                if (wrongWayCanvas != null && wrongWayCanvas.activeSelf)
                {
                    wrongWayCanvas.SetActive(false);
                }
            }

            float coverage = (float)weldedSlicesCount / sliceWelded.Length;
            if (coverage >= requiredBoxCoverage && totalTimeWeldedInsideBox >= minWeldTimeInsideBox)
            {
                TriggerRightWaySuccess();
            }
        }
        else
        {
            timeOutsideBox += deltaTime;

            if (timeOutsideBox >= outsideBoxDamageTime)
            {
                TriggerDamagedState();
                return;
            }

            if (timeOutsideBox >= outsideBoxWrongTime)
            {
                TriggerWrongWayFeedback();
            }
        }
    }

    public void OnTorchStoppedWelding()
    {
        if (IsFinishedOrDamaged) return;
        timeAtSameSpot = 0f;
        currentSpotCenter = Vector3.positiveInfinity;
    }

    private void TriggerDamagedState()
    {
        if (IsDamaged) return;
        IsDamaged = true;

        // 1. Form the burn-through hole on the mesh at the damage point!
        SpawnBurnHoleAtLastHit();

        // 2. Show Damaged Warning Canvas & play audio
        if (rightWayCanvas != null) rightWayCanvas.SetActive(false);
        if (wrongWayCanvas != null) wrongWayCanvas.SetActive(false);
        if (damagedWarningCanvas != null) damagedWarningCanvas.SetActive(true);

        if (sfxAudioSource != null && warningSfx != null)
            sfxAudioSource.PlayOneShot(warningSfx);

        PlayVoiceOver(damagedVoiceOver);
        SetMetalPieceGrabbable(false);

        // 3. Pop up the Assessment Scoreboard immediately (if in Assessment Scene)
        if (assessmentManager != null)
        {
            assessmentManager.FinalizeAndShowScore(GetNormalizedProgressRatio(), true);
        }
    }

    private void SpawnBurnHoleAtLastHit()
    {
        // Clear any weld blobs sitting right on top of the burn point so they don't cover the hole
        for (int i = spawnedBlobs.Count - 1; i >= 0; i--)
        {
            GameObject blob = spawnedBlobs[i];
            if (blob == null) continue;

            if (Vector3.Distance(blob.transform.position, lastHitPoint) <= clearBlobsRadius)
            {
                Destroy(blob);
                spawnedBlobs.RemoveAt(i);
            }
        }

        if (weldHoleMaskPrefab == null) return;

        Quaternion holeRot = Quaternion.FromToRotation(Vector3.up, lastHitNormal) * Quaternion.Euler(holeRotationOffset);
        GameObject holeObj = Instantiate(weldHoleMaskPrefab, lastHitPoint, holeRot);

        Vector3 targetLocalScale = holeWorldScale;

        if (lastHitTransform != null)
        {
            holeObj.transform.SetParent(lastHitTransform, true);

            // Counteract the metal plate's non-uniform scale so the hole stays circular
            Vector3 parentScale = lastHitTransform.lossyScale;
            targetLocalScale = new Vector3(
                holeWorldScale.x / Mathf.Max(0.0001f, Mathf.Abs(parentScale.x)),
                holeWorldScale.y / Mathf.Max(0.0001f, Mathf.Abs(parentScale.y)),
                holeWorldScale.z / Mathf.Max(0.0001f, Mathf.Abs(parentScale.z))
            );
        }

        // Start small and animate the hole burning open with LeanTween (just like WeldingHandle.cs)
        holeObj.transform.localScale = targetLocalScale * 0.25f;
        LeanTween.scale(holeObj, targetLocalScale, holeExpandDuration).setEaseOutQuad();
    }

    private void TriggerRightWaySuccess()
    {
        IsCompleted = true;

        if (wrongWayCanvas != null) wrongWayCanvas.SetActive(false);
        if (damagedWarningCanvas != null) damagedWarningCanvas.SetActive(false);
        if (rightWayCanvas != null) rightWayCanvas.SetActive(true);

        PlayVoiceOver(rightWayVoiceOver);
        SetMetalPieceGrabbable(true);

        if (assessmentManager != null)
        {
            assessmentManager.FinalizeAndShowScore(1.0f, false);
        }
    }

    private void TriggerWrongWayFeedback()
    {
        if (IsFinishedOrDamaged) return;

        if (wrongWayCanvas != null && !wrongWayCanvas.activeSelf)
            wrongWayCanvas.SetActive(true);

        if (Time.time - lastWrongVoiceTime >= wrongWayVoiceCooldown)
        {
            lastWrongVoiceTime = Time.time;
            PlayVoiceOver(wrongWayVoiceOver);
        }
    }

    private bool EvaluatePointAgainstSeamBox(
        Vector3 worldPoint,
        out float normalizedLengthT,
        out float distanceOutsideMeters,
        out float lateralOffsetRatio)
    {
        Vector3 localPoint = seamBoxTrigger.transform.InverseTransformPoint(worldPoint) - seamBoxTrigger.center;
        Vector3 halfSize = seamBoxTrigger.size * 0.5f;

        bool insideX = Mathf.Abs(localPoint.x) <= halfSize.x;
        bool insideY = Mathf.Abs(localPoint.y) <= halfSize.y;
        bool insideZ = Mathf.Abs(localPoint.z) <= halfSize.z;

        Vector3 worldSize = Vector3.Scale(seamBoxTrigger.size, seamBoxTrigger.transform.lossyScale);
        float absX = Mathf.Abs(worldSize.x);
        float absY = Mathf.Abs(worldSize.y);
        float absZ = Mathf.Abs(worldSize.z);

        if (absX >= absY && absX >= absZ)
        {
            normalizedLengthT = Mathf.InverseLerp(-halfSize.x, halfSize.x, localPoint.x);
            lateralOffsetRatio = halfSize.z > 0.0001f ? Mathf.Clamp01(Mathf.Abs(localPoint.z) / halfSize.z) : 0f;
        }
        else if (absZ >= absX && absZ >= absY)
        {
            normalizedLengthT = Mathf.InverseLerp(-halfSize.z, halfSize.z, localPoint.z);
            lateralOffsetRatio = halfSize.x > 0.0001f ? Mathf.Clamp01(Mathf.Abs(localPoint.x) / halfSize.x) : 0f;
        }
        else
        {
            normalizedLengthT = Mathf.InverseLerp(-halfSize.y, halfSize.y, localPoint.y);
            lateralOffsetRatio = halfSize.x > 0.0001f ? Mathf.Clamp01(Mathf.Abs(localPoint.x) / halfSize.x) : 0f;
        }

        bool isInside = insideX && insideY && insideZ;

        if (isInside)
        {
            distanceOutsideMeters = 0f;
        }
        else
        {
            Vector3 clampedLocal = new Vector3(
                Mathf.Clamp(localPoint.x, -halfSize.x, halfSize.x),
                Mathf.Clamp(localPoint.y, -halfSize.y, halfSize.y),
                Mathf.Clamp(localPoint.z, -halfSize.z, halfSize.z)
            );
            Vector3 closestWorld = seamBoxTrigger.transform.TransformPoint(clampedLocal + seamBoxTrigger.center);
            distanceOutsideMeters = Vector3.Distance(worldPoint, closestWorld);
        }

        return isInside;
    }

    private float GetNormalizedProgressRatio()
    {
        if (sliceWelded == null || sliceWelded.Length == 0 || requiredBoxCoverage <= 0.001f) return 0f;
        float rawCoverage = (float)weldedSlicesCount / sliceWelded.Length;
        return Mathf.Clamp01(rawCoverage / requiredBoxCoverage);
    }

    public void RestartScene()
    {
        Scene currentScene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(currentScene.buildIndex);
    }

    public void RestartScene(PointerEvent evt) => RestartScene();

    public void LoadNextScene()
    {
        if (!string.IsNullOrEmpty(nextSceneName))
        {
            SceneManager.LoadScene(nextSceneName);
        }
        else
        {
            int nextSceneIndex = SceneManager.GetActiveScene().buildIndex + 1;
            if (nextSceneIndex < SceneManager.sceneCountInBuildSettings)
            {
                SceneManager.LoadScene(nextSceneIndex);
            }
            else
            {
                Debug.LogWarning("[WeldSeamEvaluator] No next scene found in Build Settings!");
            }
        }
    }

    public void LoadNextScene(PointerEvent evt) => LoadNextScene();

    private void SetMetalPieceGrabbable(bool canGrab)
    {
        if (grabComponentsToToggle != null && grabComponentsToToggle.Length > 0)
        {
            foreach (Behaviour comp in grabComponentsToToggle)
            {
                if (comp != null) comp.enabled = canGrab;
            }
        }

        if (metalPieceGrabbableRoot != null)
        {
            foreach (var grabbable in metalPieceGrabbableRoot.GetComponentsInChildren<Grabbable>(true))
                grabbable.enabled = canGrab;

            foreach (var grabInteractable in metalPieceGrabbableRoot.GetComponentsInChildren<GrabInteractable>(true))
                grabInteractable.enabled = canGrab;

            foreach (var handGrab in metalPieceGrabbableRoot.GetComponentsInChildren<HandGrabInteractable>(true))
                handGrab.enabled = canGrab;
        }
    }

    private void PlayVoiceOver(AudioClip clip)
    {
        if (voiceOverAudioSource == null || clip == null) return;
        voiceOverAudioSource.Stop();
        voiceOverAudioSource.clip = clip;
        voiceOverAudioSource.Play();
    }

    private void OnDrawGizmos()
    {
        if (seamBoxTrigger == null) return;

        Matrix4x4 oldMatrix = Gizmos.matrix;
        Gizmos.matrix = seamBoxTrigger.transform.localToWorldMatrix;

        Gizmos.color = IsCompleted ? Color.green : (IsDamaged ? Color.red : Color.cyan);
        Gizmos.DrawWireCube(seamBoxTrigger.center, seamBoxTrigger.size);

        Gizmos.matrix = oldMatrix;
    }
}