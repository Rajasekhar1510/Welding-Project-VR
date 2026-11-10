using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oculus.Interaction;

[RequireComponent(typeof(AudioSource))]
public class Scene1Controller : MonoBehaviour
{
    [Header("Voice Over")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip pressStartVoiceOver;

    [Header("Box Collider / Object to Disable")]
    [SerializeField] private GameObject boxColliderObject;
    [Tooltip("If checked, disables boxColliderObject right after the voice-over finishes (useful if it blocks the button during narration).")]
    [SerializeField] private bool disableAfterVoiceOverEnds = false;

    [Header("Scene Transition")]
    [SerializeField] private string scene2Name = "Scene2";
    [SerializeField] private float delayBeforeLoadingScene2 = 0.5f;

    private bool isTransitioning = false;

    private void Awake()
    {
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
    }

    private void Start()
    {
        StartCoroutine(PlayIntroSequence());
    }

    private IEnumerator PlayIntroSequence()
    {
        if (pressStartVoiceOver != null && audioSource != null)
        {
            audioSource.clip = pressStartVoiceOver;
            audioSource.Play();

            if (disableAfterVoiceOverEnds)
            {
                yield return new WaitForSeconds(pressStartVoiceOver.length);
                if (boxColliderObject != null)
                    boxColliderObject.SetActive(false);
            }
        }
    }

    // Hook this to your Start Button's PointableUnityEventWrapper -> When Select
    public void OnStartButtonPressed()
    {
        if (isTransitioning) return;
        isTransitioning = true;

        // Set the box collider GameObject active false
        if (boxColliderObject != null)
        {
            boxColliderObject.SetActive(false);
        }

        StartCoroutine(LoadScene2Routine());
    }

    // Overload in case you pick the Dynamic PointerEvent option in PointableUnityEventWrapper
    public void OnStartButtonPressed(PointerEvent evt) => OnStartButtonPressed();

    private IEnumerator LoadScene2Routine()
    {
        if (delayBeforeLoadingScene2 > 0f)
            yield return new WaitForSeconds(delayBeforeLoadingScene2);

        SceneManager.LoadScene(scene2Name);
    }
}