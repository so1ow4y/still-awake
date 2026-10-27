using System;
using System.IO;
using UnityEngine;

namespace StillAwake
{
    /// <summary>Логин и пароль аккаунта, сохранённые игрой.</summary>
    [Serializable]
    public sealed class SavedAccount
    {
        public string username;
        public string password;
    }

    /// <summary>
    /// Файл аккаунта: account.json в Application.persistentDataPath (на Windows —
    /// %USERPROFILE%\AppData\LocalLow\&lt;компания&gt;\&lt;игра&gt;\account.json). При каждом запуске игра входит
    /// логином и паролем отсюда; файла нет, он битый или сервер ответил «неверно» — игрок снова проходит вход.
    /// Пароль лежит открытым текстом: файл — то же, что «запомнить меня» в браузере, и доступен только этому
    /// пользователю Windows.
    /// </summary>
    public static class AccountFile
    {
        public static string FilePath => Path.Combine(Application.persistentDataPath, "account.json");

        /// <summary>Сохранённый аккаунт или null (файла нет, он пустой или повреждён).</summary>
        public static SavedAccount Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                var account = JsonUtility.FromJson<SavedAccount>(File.ReadAllText(FilePath));
                if (account == null || string.IsNullOrWhiteSpace(account.username) || string.IsNullOrEmpty(account.password)) return null;
                return account;
            }
            catch (Exception e)
            {
                Debug.LogWarning("Still Awake: файл аккаунта не прочитан — " + e.Message);
                return null;
            }
        }

        public static void Save(string username, string password)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonUtility.ToJson(new SavedAccount { username = username, password = password }, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning("Still Awake: файл аккаунта не сохранён — " + e.Message);
            }
        }

        public static void Delete()
        {
            try
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Still Awake: файл аккаунта не удалён — " + e.Message);
            }
        }
    }
}
