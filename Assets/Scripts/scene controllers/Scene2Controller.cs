using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(Collider), typeof(AudioSource))]
public class Scene2Controller : MonoBehaviour
{
    [Header("Voice Over")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip placeObjectsVoiceOver;

    [Header("Objects to Place in Trigger")]
    [Tooltip("Drag the grabbable objects the player must place into this trigger")]
    [SerializeField] private List<GameObject> requiredObjects = new List<GameObject>();

    [Header("Scene Transition")]
    [SerializeField] private string scene3Name = "Scene3";
    [SerializeField] private float delayBeforeLoadingScene3 = 1.0f;

    private readonly HashSet<GameObject> placedObjects = new HashSet<GameObject>();
    private bool isTransitioning = false;

    private void Awake()
    {
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();

        // Ensure the collider on this zone is set as a Trigger
        Collider triggerCollider = GetComponent<Collider>();
        triggerCollider.isTrigger = true;
    }

    private void Start()
    {
        if (placeObjectsVoiceOver != null && audioSource != null)
        {
            audioSource.clip = placeObjectsVoiceOver;
            audioSource.Play();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (isTransitioning) return;

        GameObject matchedObj = GetMatchingRequiredObject(other);
        if (matchedObj != null)
        {
            placedObjects.Add(matchedObj);
            CheckAllObjectsPlaced();
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (isTransitioning) return;

        GameObject matchedObj = GetMatchingRequiredObject(other);
        if (matchedObj != null)
        {
            placedObjects.Remove(matchedObj);
        }
    }

    private GameObject GetMatchingRequiredObject(Collider col)
    {
        if (requiredObjects.Contains(col.gameObject))
            return col.gameObject;

        if (col.attachedRigidbody != null && requiredObjects.Contains(col.attachedRigidbody.gameObject))
            return col.attachedRigidbody.gameObject;

        return null;
    }

    private void CheckAllObjectsPlaced()
    {
        if (requiredObjects.Count > 0 && placedObjects.Count >= requiredObjects.Count)
        {
            isTransitioning = true;
            StartCoroutine(LoadScene3Routine());
        }
    }

    private IEnumerator LoadScene3Routine()
    {
        if (delayBeforeLoadingScene3 > 0f)
            yield return new WaitForSeconds(delayBeforeLoadingScene3);

        SceneManager.LoadScene(scene3Name);
    }
}