using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Oculus.Interaction;

[RequireComponent(typeof(Collider))]
public class WeldablePart : MonoBehaviour
{
    [Header("Weld Settings")]
    [Tooltip("How many seconds of welding along the shared edge is required to lock them together")]
    [SerializeField] private float requiredEdgeWeldTime = 1.0f;

    [Header("Optional References (Auto-filled if left empty)")]
    [SerializeField] private Grabbable grabbable;

    // Tracks weld progress with any other part
    private readonly Dictionary<WeldablePart, float> seamHeatWithPart = new Dictionary<WeldablePart, float>();

    // All parts welded to this part (directly or indirectly)
    private readonly HashSet<WeldablePart> weldedGroup = new HashSet<WeldablePart>();

    private Coroutine followRoutine;

    public bool IsWelded => weldedGroup.Count > 1;

    private void Awake()
    {
        if (grabbable == null)
            grabbable = GetComponent<Grabbable>();

        weldedGroup.Add(this);
    }

    private void OnEnable()
    {
        if (grabbable != null)
            grabbable.WhenPointerEventRaised += HandlePointerEvent;
    }

    private void OnDisable()
    {
        if (grabbable != null)
            grabbable.WhenPointerEventRaised -= HandlePointerEvent;

        if (followRoutine != null)
        {
            StopCoroutine(followRoutine);
            followRoutine = null;
        }
    }

    // Called by WeldingTorch when the torch welds on the edge between this part and another part
    public void AddEdgeWeldProgress(WeldablePart otherPart, float deltaTime)
    {
        if (otherPart == null || otherPart == this) return;
        if (weldedGroup.Contains(otherPart)) return; // Already welded together

        if (!seamHeatWithPart.ContainsKey(otherPart))
            seamHeatWithPart[otherPart] = 0f;

        seamHeatWithPart[otherPart] += deltaTime;
        otherPart.seamHeatWithPart[this] = seamHeatWithPart[otherPart];

        if (seamHeatWithPart[otherPart] >= requiredEdgeWeldTime)
        {
            LockPartsTogether(otherPart);
        }
    }

    private void LockPartsTogether(WeldablePart otherPart)
    {
        // Merge the welded groups so 2, 3, or more parts can all be welded into one assembly
        HashSet<WeldablePart> combinedGroup = new HashSet<WeldablePart>(this.weldedGroup);
        combinedGroup.UnionWith(otherPart.weldedGroup);

        foreach (WeldablePart member in combinedGroup)
        {
            member.weldedGroup.Clear();
            foreach (WeldablePart target in combinedGroup)
            {
                member.weldedGroup.Add(target);
            }
        }

        Debug.Log($"[WeldablePart] Welded {name} and {otherPart.name} together!");
    }

    private void HandlePointerEvent(PointerEvent evt)
    {
        // Only run when this part is welded to at least one other part
        if (weldedGroup.Count <= 1) return;

        if (evt.Type == PointerEventType.Select)
        {
            if (followRoutine != null) StopCoroutine(followRoutine);
            followRoutine = StartCoroutine(SyncWeldedPartsWhileGrabbed());
        }
        else if (evt.Type == PointerEventType.Unselect)
        {
            // Stop syncing once no hand is holding this part
            if (grabbable != null && grabbable.SelectingPointsCount == 0 && followRoutine != null)
            {
                StopCoroutine(followRoutine);
                followRoutine = null;
            }
        }
    }

    // Runs ONLY while you are actively holding this part in VR
    private IEnumerator SyncWeldedPartsWhileGrabbed()
    {
        // Record each connected part's exact offset relative to this part at the start of the grab
        var offsets = new Dictionary<Transform, (Vector3 localPos, Quaternion localRot)>();
        Quaternion invMyRot = Quaternion.Inverse(transform.rotation);

        foreach (WeldablePart part in weldedGroup)
        {
            if (part == this || part == null) continue;

            Vector3 relPos = invMyRot * (part.transform.position - transform.position);
            Quaternion relRot = invMyRot * part.transform.rotation;
            offsets[part.transform] = (relPos, relRot);
        }

        while (true)
        {
            foreach (var kvp in offsets)
            {
                Transform otherTransform = kvp.Key;
                if (otherTransform == null) continue;

                var (relPos, relRot) = kvp.Value;
                otherTransform.SetPositionAndRotation(
                    transform.position + (transform.rotation * relPos),
                    transform.rotation * relRot
                );
            }

            yield return null;
        }
    }
}

