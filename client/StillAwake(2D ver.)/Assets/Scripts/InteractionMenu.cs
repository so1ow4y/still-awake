using UnityEngine;
using UnityEngine.UI;

public class InteractionMenu : MonoBehaviour
{
    private RectTransform rectTransform;
    private CanvasGroup canvasGroup;

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
    }

    void Start()
    {
        // Прячем при старте, но объект остается активным
        canvasGroup.alpha = 0f; 
        canvasGroup.blocksRaycasts = false; 
        canvasGroup.interactable = false;
    }

    public void OpenMenu(Vector2 screenPosition)
    {
        Debug.Log("Меню ОТКРЫВАЕТСЯ!");
        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = true;
        canvasGroup.interactable = true;

        // Ставим в центр экрана
        rectTransform.localPosition = Vector3.zero; 
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
    }

    public void CloseMenu()
    {
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;
    }
}