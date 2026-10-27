using System.Collections.Generic;
using UnityEngine;

public class Interactable : MonoBehaviour
{
    public string objectName = "Объект";
    public List<InteractionAction> actions = new List<InteractionAction>();
}