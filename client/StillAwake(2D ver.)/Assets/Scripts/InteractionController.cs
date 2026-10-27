using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections.Generic;

public class InteractionController : MonoBehaviour
{
    [Header("UI References")]
    public GameObject actionMenu;
    public Text objectTitle;
    public Transform buttonsParent;
    public Button actionButtonPrefab;
    public Button closeButton;
    
    [Header("Message UI")]
    public GameObject messagePanel; // Фон (Panel) - скрываем его целиком
    public Text messageText;        // Сам текст

    [Header("Hover Settings")]
    public Color hoverColor = new Color(1f, 1f, 0.7f, 1f);

    private bool isShowingMessage;
    private List<string> currentMessages = new List<string>();
    private int currentMessageIndex;
    private bool isMenuOpen = false;

    private SpriteRenderer currentHoveredRenderer;
    private Color originalColor;

    private void Start()
    {
        // Скрываем и меню, и панель с текстом
        if (actionMenu != null) actionMenu.SetActive(false);
        if (messagePanel != null) messagePanel.SetActive(false);

        if (closeButton != null)
        {
            closeButton.onClick.RemoveListener(CloseMenu);
            closeButton.onClick.AddListener(CloseMenu);
        }
    }

    private void Update()
    {
        if (isMenuOpen || isShowingMessage)
        {
            ClearHover();
            if (isShowingMessage && Input.GetMouseButtonDown(0))
            {
                ShowNextMessage();
            }
            return;
        }

        Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        RaycastHit2D hit = Physics2D.Raycast(mousePos, Vector2.zero);

        if (hit.collider != null)
        {
            SpriteRenderer sr = hit.collider.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                if (currentHoveredRenderer != sr)
                {
                    ClearHover();
                    currentHoveredRenderer = sr;
                    originalColor = sr.color;
                    sr.color = hoverColor;
                }
            }
            else ClearHover();
        }
        else ClearHover();

        if (Input.GetMouseButtonDown(0))
        {
            if (hit.collider != null)
            {
                Interactable target = hit.collider.GetComponentInParent<Interactable>();
                if (target != null) OpenMenu(target);
            }
        }
    }

    private void ClearHover()
    {
        if (currentHoveredRenderer != null)
        {
            currentHoveredRenderer.color = originalColor;
            currentHoveredRenderer = null;
        }
    }

    private void OpenMenu(Interactable target)
    {
        if (isMenuOpen) return;

        if (actionMenu == null || objectTitle == null || buttonsParent == null || actionButtonPrefab == null)
        {
            Debug.LogError("Не назначены ссылки меню в Inspector.");
            return;
        }

        isMenuOpen = true;
        objectTitle.text = target.objectName;
        ClearButtons();

        foreach (InteractionAction action in target.actions)
        {
            InteractionAction capturedAction = action;
            Button button = Instantiate(actionButtonPrefab, buttonsParent);
            button.gameObject.SetActive(true);

            Text buttonText = button.GetComponentInChildren<Text>(true);
            if (buttonText != null) buttonText.text = capturedAction.title;

            button.onClick.AddListener(() => ExecuteAction(capturedAction));
        }

        actionMenu.SetActive(true);
    }

    private void ExecuteAction(InteractionAction action)
    {
        CloseMenu();

        if (string.IsNullOrWhiteSpace(action.message)) return;

        currentMessages = new List<string>(
            action.message.Split(new string[] { "\n\n" }, System.StringSplitOptions.RemoveEmptyEntries)
        );

        currentMessageIndex = 0;
        isShowingMessage = true;
        ShowCurrentMessage();
    }

    private void ShowCurrentMessage()
    {
        if (messageText == null || messagePanel == null) { CloseMessage(); return; }
        if (currentMessageIndex >= currentMessages.Count) { CloseMessage(); return; }

        messageText.text = currentMessages[currentMessageIndex];
        messagePanel.SetActive(true); // Включаем весь блок с фоном
    }

    private void ShowNextMessage()
    {
        currentMessageIndex++;
        if (currentMessageIndex >= currentMessages.Count)
            CloseMessage();
        else
            ShowCurrentMessage();
    }

    private void CloseMessage()
    {
        isShowingMessage = false;
        currentMessages.Clear();
        currentMessageIndex = 0;
        if (messagePanel != null) messagePanel.SetActive(false); // Скрываем весь блок
    }

    private void CloseMenu()
    {
        if (actionMenu != null) actionMenu.SetActive(false);
        isMenuOpen = false;
    }

    private void ClearButtons()
    {
        if (buttonsParent == null) return;
        for (int i = buttonsParent.childCount - 1; i >= 0; i--)
            Destroy(buttonsParent.GetChild(i).gameObject);
    }
}