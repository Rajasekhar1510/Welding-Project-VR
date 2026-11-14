using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class WeldingAssessmentManager : MonoBehaviour
{
    [Header("Scoreboard UI")]
    [Tooltip("The Scoreboard Canvas GameObject that pops up when welding finishes or is damaged")]
    [SerializeField] private GameObject scoreBoardCanvas;
    [Tooltip("Drag your TextMeshPro text here (if using TMP)")]
    [SerializeField] private TMP_Text tmpScoreText;
    [Tooltip("Or drag your legacy Unity UI Text here (if using standard UI Text)")]
    [SerializeField] private Text legacyScoreText;
    [Tooltip("Format for displaying the score ({0} is replaced by the 0-100 number)")]
    [SerializeField] private string scoreFormat = "{0}";

    [Header("Score Grade Colors (Red / Yellow / Green)")]
    [SerializeField] private Color lowScoreColor = new Color(0.95f, 0.25f, 0.25f);    // Red (0 - 49)
    [SerializeField] private Color mediumScoreColor = new Color(1.0f, 0.8f, 0.2f);    // Yellow (50 - 79)
    [SerializeField] private Color highScoreColor = new Color(0.25f, 0.9f, 0.35f);    // Green (80 - 100)
    [SerializeField] private int mediumScoreThreshold = 50;
    [SerializeField] private int highScoreThreshold = 80;
    [Tooltip("If checked, smoothly blends Red -> Yellow -> Green as the number counts up")]
    [SerializeField] private bool blendColorsSmoothly = false;

    [Header("LeanTween Animation Settings")]
    [Tooltip("How many seconds it takes for the score number to count up from 0 to FinalScore")]
    [SerializeField] private float scoreCountUpDuration = 1.2f;
    [Tooltip("If checked, pops the Scoreboard Canvas in smoothly with LeanTween.scale")]
    [SerializeField] private bool animateCanvasPopIn = true;
    [SerializeField] private float canvasPopInDuration = 0.35f;

    [Header("Optional Score Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip scorePopupSfx;
    [Tooltip("Optional short tick sound played as the numbers count up")]
    [SerializeField] private AudioClip countTickSfx;

    [Header("Scoring Rules (0 - 100)")]
    [Tooltip("Points deducted per second the player welds slightly outside the BoxCollider")]
    [SerializeField] private float penaltyPerSecondOutsideBox = 15f;
    [Tooltip("Extra penalty multiplier when welding far away from the BoxCollider edge")]
    [SerializeField] private float distanceOutsidePenaltyMultiplier = 100f;
    [Range(0f, 30f)]
    [SerializeField] private float maxWobbleInsideBoxPenalty = 0f;
    [SerializeField] private bool forceZeroScoreOnDamage = false;

    private float accumulatedOutsidePenalty = 0f;
    private float totalCenterDeviationRatio = 0f;
    private int insideTickCount = 0;
    private bool scoreDisplayed = false;
    private Vector3 initialCanvasScale = Vector3.one;
    private int lastDisplayedInt = -1;

    public int FinalScore { get; private set; } = 0;

    private void Awake()
    {
        if (scoreBoardCanvas != null)
        {
            initialCanvasScale = scoreBoardCanvas.transform.localScale;
            scoreBoardCanvas.SetActive(false);
        }
    }

    public void RecordWeldSample(bool isInsideBox, float distanceOutsideBoxMeters, float lateralOffsetRatioInsideBox, float deltaTime)
    {
        if (scoreDisplayed) return;

        if (isInsideBox)
        {
            insideTickCount++;
            totalCenterDeviationRatio += Mathf.Clamp01(lateralOffsetRatioInsideBox);
        }
        else
        {
            float distanceFactor = 1f + (distanceOutsideBoxMeters * distanceOutsidePenaltyMultiplier);
            accumulatedOutsidePenalty += penaltyPerSecondOutsideBox * distanceFactor * deltaTime;
        }
    }

    public void FinalizeAndShowScore(float normalizedBoxCoverage, bool wasDamaged)
    {
        if (scoreDisplayed) return;
        scoreDisplayed = true;

        if (wasDamaged && forceZeroScoreOnDamage)
        {
            FinalScore = 0;
        }
        else if (insideTickCount == 0 || normalizedBoxCoverage <= 0.001f)
        {
            FinalScore = 0;
        }
        else
        {
            float baseScore = Mathf.Clamp01(normalizedBoxCoverage) * 100f;
            float avgWobble = totalCenterDeviationRatio / insideTickCount;
            float wobblePenalty = avgWobble * maxWobbleInsideBoxPenalty;

            float rawScore = baseScore - accumulatedOutsidePenalty - wobblePenalty;
            FinalScore = Mathf.RoundToInt(Mathf.Clamp(rawScore, 0f, 100f));
        }

        ShowAndAnimateScoreboard();
    }

    private void ShowAndAnimateScoreboard()
    {
        LeanTween.cancel(gameObject);

        UpdateScoreTextAndColor(0);

        if (scoreBoardCanvas != null)
        {
            scoreBoardCanvas.SetActive(true);

            if (animateCanvasPopIn)
            {
                LeanTween.cancel(scoreBoardCanvas);
                scoreBoardCanvas.transform.localScale = Vector3.zero;
                LeanTween.scale(scoreBoardCanvas, initialCanvasScale, canvasPopInDuration)
                         .setEaseOutBack();
            }
        }

        if (audioSource != null && scorePopupSfx != null)
        {
            audioSource.PlayOneShot(scorePopupSfx);
        }

        if (FinalScore <= 0 || scoreCountUpDuration <= 0f)
        {
            UpdateScoreTextAndColor(FinalScore);
            return;
        }

        LeanTween.value(gameObject, 0f, (float)FinalScore, scoreCountUpDuration)
            .setEaseOutQuad()
            .setOnUpdate((float val) =>
            {
                int currentInt = Mathf.RoundToInt(val);
                if (currentInt != lastDisplayedInt)
                {
                    UpdateScoreTextAndColor(currentInt);

                    if (audioSource != null && countTickSfx != null)
                    {
                        audioSource.PlayOneShot(countTickSfx);
                    }
                }
            })
            .setOnComplete(() =>
            {
                UpdateScoreTextAndColor(FinalScore);
            });
    }

    private void UpdateScoreTextAndColor(int scoreValue)
    {
        lastDisplayedInt = scoreValue;
        string formattedScore = string.Format(scoreFormat, scoreValue);
        Color gradeColor = GetColorForScore(scoreValue);

        if (tmpScoreText != null)
        {
            tmpScoreText.text = formattedScore;
            tmpScoreText.color = gradeColor;
        }

        if (legacyScoreText != null)
        {
            legacyScoreText.text = formattedScore;
            legacyScoreText.color = gradeColor;
        }
    }

    private Color GetColorForScore(int score)
    {
        if (blendColorsSmoothly)
        {
            if (score < mediumScoreThreshold)
            {
                float t = Mathf.InverseLerp(0f, mediumScoreThreshold, score);
                return Color.Lerp(lowScoreColor, mediumScoreColor, t);
            }
            else
            {
                float t = Mathf.InverseLerp(mediumScoreThreshold, highScoreThreshold, score);
                return Color.Lerp(mediumScoreColor, highScoreColor, t);
            }
        }

        if (score >= highScoreThreshold) return highScoreColor;
        if (score >= mediumScoreThreshold) return mediumScoreColor;
        return lowScoreColor;
    }

    private void OnDestroy()
    {
        LeanTween.cancel(gameObject);
        if (scoreBoardCanvas != null)
            LeanTween.cancel(scoreBoardCanvas);
    }
}