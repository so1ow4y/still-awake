using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class StillAwakeBackendClient : MonoBehaviour
{
    [SerializeField] private string baseUrl = "http://localhost:8080";

    public IEnumerator SendChat(string sessionId, string playerText, string gameStateJson, Action<string> onDone)
    {
        string body = $"{{\"user_message\":{JsonEscape(playerText)},\"game_state\":{gameStateJson}}}";
        using var req = new UnityWebRequest($"{baseUrl}/v1/sessions/{sessionId}/chat", "POST");
        req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            Debug.LogError(req.error + "\n" + req.downloadHandler.text);
        else
            onDone?.Invoke(req.downloadHandler.text);
    }

    private static string JsonEscape(string value)
    {
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";
    }
}
