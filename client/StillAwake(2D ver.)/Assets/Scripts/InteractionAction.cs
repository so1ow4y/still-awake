using System;
using UnityEngine;

[Serializable]
public class InteractionAction
{
    public string title; // Название кнопки (например, "Осмотреть")
    
    [TextArea(3, 10)]
    public string message; // Текст, который выведется при нажатии
}