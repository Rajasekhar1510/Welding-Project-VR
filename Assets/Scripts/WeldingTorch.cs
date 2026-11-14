using System.Collections;
using UnityEngine;
using Oculus.Interaction;

public class WeldingTorch : MonoBehaviour
{
    [Header("Seam Check System")]
    [SerializeField] private WeldSeamEvaluator seamEvaluator;

    [Header("Button Input Settings")]
    [SerializeField] private bool checkControllerButtonWhileGrabbed = true;
    [SerializeField] private OVRInput.Button activateButton = OVRInput.Button.PrimaryIndexTrigger | OVRInput.Button.SecondaryIndexTrigger;
    [SerializeField] private OVRInput.Controller controllerMask = OVRInput.Controller.Touch;

    [Header("Torch Effects")]
    [SerializeField] private Transform nozzleTip;
    [SerializeField] private ParticleSystem sparkParticles;
    [SerializeField] private AudioSource weldingAudio;
    [SerializeField] private Light torchGlowLight;

    [Header("Weld Blob Settings")]
    [SerializeField] private GameObject weldBlobPrefab;
    [SerializeField] private LayerMask weldableLayer;
    [SerializeField] private float maxWeldDistance = 0.08f;
    [SerializeField] private float blobSpawnInterval = 0.06f;
    [SerializeField] private float minBlobDistance = 0.004f;

    [Header("Blob Size & Orientation Fix")]
    [SerializeField] private Vector3 blobWorldScale = new Vector3(0.015f, 0.015f, 0.015f);
    [SerializeField] private Vector3 blobRotationOffset = Vector3.zero;

    private bool isGrabbed = false;
    private bool isButtonPressed = false;
    private Vector3 lastBlobSpawnPoint = Vector3.positiveInfinity;
    private Coroutine grabbedInputRoutine;
    private Coroutine weldRoutine;

    private void Awake()
    {
        TurnOffEffects();
    }

    #region PointableUnityEventWrapper Callbacks

    public void OnTorchGrabbed()
    {
        isGrabbed = true;

        if (checkControllerButtonWhileGrabbed)
        {
            if (grabbedInputRoutine != null) StopCoroutine(grabbedInputRoutine);
            grabbedInputRoutine = StartCoroutine(MonitorButtonWhileGrabbed());
        }
        else
        {
            EvaluateTorchState();
        }
    }

    public void OnTorchGrabbed(PointerEvent evt) => OnTorchGrabbed();

    public void OnTorchReleased()
    {
        isGrabbed = false;
        isButtonPressed = false;

        if (grabbedInputRoutine != null)
        {
            StopCoroutine(grabbedInputRoutine);
            grabbedInputRoutine = null;
        }

        EvaluateTorchState();
    }

    public void OnTorchReleased(PointerEvent evt) => OnTorchReleased();

    public void OnButtonPressed()
    {
        isButtonPressed = true;
        EvaluateTorchState();
    }

    public void OnButtonReleased()
    {
        isButtonPressed = false;
        EvaluateTorchState();
    }

    #endregion

    private IEnumerator MonitorButtonWhileGrabbed()
    {
        while (isGrabbed)
        {
            bool pressedNow = OVRInput.Get(activateButton, controllerMask);

            if (pressedNow != isButtonPressed)
            {
                isButtonPressed = pressedNow;
                EvaluateTorchState();
            }

            yield return null;
        }
    }

    private void EvaluateTorchState()
    {
        bool canWeld = seamEvaluator == null || !seamEvaluator.IsFinishedOrDamaged;
        bool shouldLightUp = isGrabbed && isButtonPressed && canWeld;

        if (shouldLightUp && weldRoutine == null)
        {
            StartWelding();
        }
        else if (!shouldLightUp && weldRoutine != null)
        {
            StopWelding();
        }
    }

    private void StartWelding()
    {
        if (sparkParticles != null && !sparkParticles.isPlaying) sparkParticles.Play();
        if (weldingAudio != null && !weldingAudio.isPlaying) weldingAudio.Play();
        if (torchGlowLight != null) torchGlowLight.enabled = true;

        weldRoutine = StartCoroutine(WeldAndSpawnBlobsLoop());
    }

    private void StopWelding()
    {
        TurnOffEffects();
        lastBlobSpawnPoint = Vector3.positiveInfinity;

        if (weldRoutine != null)
        {
            StopCoroutine(weldRoutine);
            weldRoutine = null;
        }

        if (seamEvaluator != null)
        {
            seamEvaluator.OnTorchStoppedWelding();
        }
    }

    private IEnumerator WeldAndSpawnBlobsLoop()
    {
        var wait = new WaitForSeconds(blobSpawnInterval);

        while (true)
        {
            if (seamEvaluator != null && seamEvaluator.IsFinishedOrDamaged)
            {
                StopWelding();
                yield break;
            }

            if (nozzleTip != null &&
                Physics.Raycast(
                    nozzleTip.position,
                    nozzleTip.forward,
                    out RaycastHit hit,
                    maxWeldDistance,
                    weldableLayer,
                    QueryTriggerInteraction.Collide))
            {
                GameObject blob = null; // <-- CHANGE 1: Declared outside the 'if' block

                // 1. Spawn the visual blob if the torch moved at least minBlobDistance
                if (weldBlobPrefab != null && Vector3.Distance(hit.point, lastBlobSpawnPoint) >= minBlobDistance)
                {
                    lastBlobSpawnPoint = hit.point;

                    Quaternion surfaceRot = Quaternion.FromToRotation(Vector3.up, hit.normal) * Quaternion.Euler(blobRotationOffset);
                    blob = Instantiate(weldBlobPrefab, hit.point, surfaceRot);

                    blob.transform.SetParent(hit.transform, true);

                    Vector3 parentScale = hit.transform.lossyScale;
                    blob.transform.localScale = new Vector3(
                        blobWorldScale.x / Mathf.Max(0.0001f, Mathf.Abs(parentScale.x)),
                        blobWorldScale.y / Mathf.Max(0.0001f, Mathf.Abs(parentScale.y)),
                        blobWorldScale.z / Mathf.Max(0.0001f, Mathf.Abs(parentScale.z))
                    );

                    if (blob.TryGetComponent<WeldingBlobSet>(out var blobScript))
                    {
                        blobScript.ShowGlow();
                    }
                }

                // 2. Pass hit.normal, hit.transform, and blob so the hole spawns flat and clears blobs
                if (seamEvaluator != null)
                {
                    // <-- CHANGE 2: Added hit.normal, hit.transform, and blob
                    seamEvaluator.RegisterWeldTick(hit.point, hit.normal, hit.transform, blob, blobSpawnInterval);
                }
            }

            yield return wait;
        }
    }

    private void TurnOffEffects()
    {
        if (sparkParticles != null && sparkParticles.isPlaying) sparkParticles.Stop();
        if (weldingAudio != null && weldingAudio.isPlaying) weldingAudio.Stop();
        if (torchGlowLight != null) torchGlowLight.enabled = false;
    }

    private void OnDisable()
    {
        if (grabbedInputRoutine != null) StopCoroutine(grabbedInputRoutine);
        StopWelding();
    }
}