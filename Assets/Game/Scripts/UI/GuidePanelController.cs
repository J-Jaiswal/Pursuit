using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Controls the Guide Panel in the menu scene.
/// Cycles through page GameObjects (Gameplay, Controls, Level) using
/// left/right arrow buttons with wrap-around navigation.

/// </summary>
public class GuidePanelController : MonoBehaviour
{
    [Header("Pages (in display order)")]
    [Tooltip("Assign the Gameplay, Controls, and Level GameObjects in order.")]
    [SerializeField] private GameObject[] pages;

    [Header("Navigation Buttons")]
    [SerializeField] private Button leftButton;
    [SerializeField] private Button rightButton;

    [Header("Home Navigation")]
    [Tooltip("Button inside the guide panel that returns to the main menu.")]
    [SerializeField] private Button homeButton;
    [Tooltip("Menu script — CloseGuide() is called when the home button is pressed.")]
    [SerializeField] private Menu   menuScript;

    private int currentIndex = 0;

    // ── Unity Lifecycle ───────────────────────────────────────────────────────

    private void Start()
    {
        leftButton.onClick.AddListener(GoLeft);
        rightButton.onClick.AddListener(GoRight);
        if (homeButton != null) homeButton.onClick.AddListener(GoHome);

        // Show the first page on open
        ShowPage(currentIndex);
    }

    private void OnDestroy()
    {
        leftButton.onClick.RemoveListener(GoLeft);
        rightButton.onClick.RemoveListener(GoRight);
        if (homeButton != null) homeButton.onClick.RemoveListener(GoHome);
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    private void GoLeft()
    {
        currentIndex = (currentIndex - 1 + pages.Length) % pages.Length;
        ShowPage(currentIndex);
    }

    private void GoRight()
    {
        currentIndex = (currentIndex + 1) % pages.Length;
        ShowPage(currentIndex);
    }

    private void GoHome()
    {
        if (menuScript != null)
            menuScript.CloseGuide();
    }

    // ── Page Display ──────────────────────────────────────────────────────────

    private void ShowPage(int index)
    {
        for (int i = 0; i < pages.Length; i++)
        {
            if (pages[i] != null)
                pages[i].SetActive(i == index);
        }
    }

 
    public void ResetToFirstPage()
    {
        currentIndex = 0;
        ShowPage(currentIndex);
    }

}
