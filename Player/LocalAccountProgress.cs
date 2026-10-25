using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

// Account identity selects a local save directory on this computer.
public static class LocalAccountProgress
{
    public static string LegacyPath => Path.Combine(Application.persistentDataPath, "progress-v1.json");
    public static string ActivePath => guest ? Path.Combine(Application.persistentDataPath, "Guest", "progress-v1.json") : string.IsNullOrEmpty(profileKey) ? LegacyPath :
        Path.Combine(Application.persistentDataPath, "Profiles", profileKey, "progress-v1.json");
    public static bool HasAccount => !string.IsNullOrEmpty(profileKey);
    static string profileKey;
    static bool guest;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { profileKey = null; guest = false; }

    public static string ProfileKey(string authority, string userId)
    {
        if (string.IsNullOrWhiteSpace(authority) || !Guid.TryParse(userId, out Guid id))
            throw new ArgumentException("帳號識別資料無效");
        using (var sha = SHA256.Create())
        {
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(authority.TrimEnd('/').ToLowerInvariant() + "|" + id.ToString("N")));
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }

    // Called only after the cloud authentication response has been validated.
    internal static void SelectAccount(string authority, string userId) { guest = false; profileKey = ProfileKey(authority, userId); }
    internal static void SelectGuest() { profileKey = null; guest = true; }
    internal static void ClearAccount() { profileKey = null; guest = false; GameProgressSave.StartFresh = false; }

    public static bool CanImportLegacy => HasAccount && !File.Exists(ActivePath) && !File.Exists(ActivePath + ".bak") &&
        GameProgressSave.TryRead(LegacyPath, out _, out _);

    public static bool ImportLegacy(out string message)
    {
        if (!CanImportLegacy) { message = "沒有可匯入的舊進度，或此帳號已有存檔"; return false; }
        if (!GameProgressSave.TryRead(LegacyPath, out GameProgressState data, out _))
        { message = "原本機進度無法讀取"; return false; }
        bool success = GameProgressSave.TryWrite(ActivePath, data, out _);
        message = success ? "已匯入原本機進度，原始存檔仍保留" : "匯入失敗，請檢查磁碟空間";
        return success;
    }
}
