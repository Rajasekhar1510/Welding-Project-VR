using System.Collections;
using UnityEngine;
using Oculus.Interaction;

public class WeldingTorch : MonoBehaviour
{
    [Header("Button Input Settings")]
    [SerializeField] private bool checkControllerButtonWhileGrabbed = true;
    [SerializeField] private OVRInput.Button activateButton = OVRInput.Button.PrimaryIndexTrigger | OVRInput.Button.SecondaryIndexTrigger;
    [SerializeField] private OVRInput.Controller controllerMask = OVRInput.Controller.Touch;

    [Header("Torch Effects")]
    [SerializeField] private Transform nozzleTip;
    [SerializeField] private ParticleSystem sparkParticles;
    [SerializeField] private AudioSource weldingAudio;
    [SerializeField] private Light torchGlowLight;

    [Header("Weld Blob Settings (From unity_welding_simulator)")]
    [SerializeField] private GameObject weldBlobPrefab;
    [SerializeField] private LayerMask weldableLayer;
    [SerializeField] private float maxWeldDistance = 0.08f; // 8 cm reach from nozzle tip
    [SerializeField] private float blobSpawnInterval = 0.1f;
    [SerializeField] private float minBlobDistance = 0.005f; // Prevents infinite stacking in one spot
    [SerializeField] private float heatRatePerSecond = 1.0f;

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
        bool shouldLightUp = isGrabbed && isButtonPressed;

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
    }

    private IEnumerator WeldAndSpawnBlobsLoop()
    {
        var wait = new WaitForSeconds(blobSpawnInterval);

        while (true)
        {
            // Only spawns a blob if the raycast hits a Collider on the weldableLayer
            if (nozzleTip != null &&
                Physics.Raycast(nozzleTip.position, nozzleTip.forward, out RaycastHit hit, maxWeldDistance, weldableLayer))
            {
                // 1. Spawn the visual metal blob on the mesh surface
                if (weldBlobPrefab != null && Vector3.Distance(hit.point, lastBlobSpawnPoint) >= minBlobDistance)
                {
                    lastBlobSpawnPoint = hit.point;

                    GameObject blob = Instantiate(
                        weldBlobPrefab,
                        hit.point,
                        Quaternion.LookRotation(hit.normal)
                    );

                    // Parent to the hit mesh so the blob stays attached when grabbed in VR
                    blob.transform.SetParent(hit.transform, true);

                    if (blob.TryGetComponent<WeldingBlobSet>(out var blobScript))
                    {
                        blobScript.ShowGlow();
                    }
                }

                // 2. Apply weld heat to attach two touching WeldablePart objects together
               /* if (hit.collider.TryGetComponent<WeldablePart>(out var part))
                {
                    part.ApplyWeldHeat(heatRatePerSecond * blobSpawnInterval, hit.point);
                }*/
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