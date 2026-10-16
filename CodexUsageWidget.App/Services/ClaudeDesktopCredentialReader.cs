using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodexUsageWidget.App.Services;

/// <summary>
/// Claudeデスクトップアプリが同梱のClaude Code用に保持しているOAuthトークンを、読み取り専用で取り出す。
/// トークンはElectronのos_crypt形式（DPAPIで保護したAES鍵 + AES-256-GCM）で config.json に暗号化保存されている。
/// トークンの更新はアプリ自身に任せ、ファイルへは書き込まない。
/// </summary>
internal static class ClaudeDesktopCredentialReader
{
    private static readonly string[] TokenCacheKeys = ["oauth:tokenCacheV2", "oauth:tokenCache"];
    private const string InferenceScope = "user:inference";
    private const string ProfileScope = "user:profile";
    private const int NonceLength = 12;
    private const int TagLength = 16;

    public static ClaudeCredential? Read()
    {
        foreach (var directory in DataDirectories())
        {
            try
            {
                var configPath = Path.Combine(directory, "config.json");
                var localStatePath = Path.Combine(directory, "Local State");
                if (!File.Exists(configPath) || !File.Exists(localStatePath)) continue;

                var key = ReadMasterKey(ReadShared(localStatePath));
                if (key is null) continue;

                using var config = JsonDocument.Parse(ReadShared(configPath));
                foreach (var cacheKey in TokenCacheKeys)
                {
                    if (!config.RootElement.TryGetProperty(cacheKey, out var value) ||
                        value.ValueKind != JsonValueKind.String ||
                        value.GetString() is not { Length: > 0 } encrypted) continue;
                    var plain = DecryptValue(encrypted, key);
                    if (plain is null) continue;
                    var credential = SelectToken(plain);
                    if (credential is not null) return credential;
                }
            }
            catch { }
        }
        return null;
    }

    /// <summary>通常インストールとMicrosoft Store版の保存場所。</summary>
    private static IEnumerable<string> DataDirectories()
    {
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude");

        var packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
        string[] storeInstalls;
        try
        {
            storeInstalls = Directory.Exists(packages)
                ? Directory.EnumerateDirectories(packages, "Claude_*").Order(StringComparer.OrdinalIgnoreCase).ToArray()
                : [];
        }
        catch
        {
            storeInstalls = [];
        }
        foreach (var package in storeInstalls)
        {
            yield return Path.Combine(package, "LocalCache", "Roaming", "Claude");
        }
    }

    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static byte[]? ReadMasterKey(string localStateJson)
    {
        using var document = JsonDocument.Parse(localStateJson);
        if (!document.RootElement.TryGetProperty("os_crypt", out var osCrypt) ||
            !osCrypt.TryGetProperty("encrypted_key", out var encryptedKey) ||
            encryptedKey.GetString() is not { Length: > 0 } base64) return null;

        var wrapped = Convert.FromBase64String(base64);
        var prefix = "DPAPI"u8;
        if (!wrapped.AsSpan().StartsWith(prefix)) return null;
        return DpapiUnprotect(wrapped[prefix.Length..]);
    }

    /// <summary>"v10" + nonce(12) + 暗号文 + GCMタグ(16) を復号する。</summary>
    internal static string? DecryptValue(string base64, byte[] key)
    {
        var blob = Convert.FromBase64String(base64);
        var prefix = "v10"u8;
        if (!blob.AsSpan().StartsWith(prefix) || blob.Length < prefix.Length + NonceLength + TagLength) return null;

        var nonce = blob.AsSpan(prefix.Length, NonceLength);
        var body = blob.AsSpan(prefix.Length + NonceLength);
        var ciphertext = body[..^TagLength];
        var tag = body[^TagLength..];
        var plain = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(key, TagLength);
            aes.Decrypt(nonce, ciphertext, tag, plain);
        }
        catch (CryptographicException)
        {
            return null;
        }
        return Encoding.UTF8.GetString(plain);
    }

    /// <summary>
    /// キャッシュのキーは「インストール:ユーザー:URL:スコープ」。使用量APIには推論とプロフィールのスコープが要るため、
    /// 両方を持つもの → 推論のみ → それ以外の順に選び、同順位では期限が遅いものを使う。
    /// </summary>
    internal static ClaudeCredential? SelectToken(string cacheJson)
    {
        using var document = JsonDocument.Parse(cacheJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object) return null;

        return document.RootElement.EnumerateObject()
            .Select(entry => (Rank: ScopeRank(entry.Name), Credential: ParseEntry(entry.Value)))
            .Where(candidate => candidate.Credential is not null)
            .OrderBy(candidate => candidate.Rank)
            .ThenByDescending(candidate => candidate.Credential!.ExpiresAt)
            .Select(candidate => candidate.Credential)
            .FirstOrDefault();
    }

    private static int ScopeRank(string cacheKey)
    {
        // スコープは空白区切り。"user:inference_extra" のような似た名前を誤認しないよう語単位で比べる。
        var words = cacheKey.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        var inference = words.Any(word => word.EndsWith(InferenceScope, StringComparison.Ordinal));
        var profile = words.Any(word => word.EndsWith(ProfileScope, StringComparison.Ordinal));
        return inference && profile ? 0 : inference ? 1 : 2;
    }

    private static ClaudeCredential? ParseEntry(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) return null;
        if (!value.TryGetProperty("token", out var token) || token.GetString() is not { Length: > 0 } accessToken) return null;

        var expiresAt = DateTimeOffset.MaxValue;
        if (value.TryGetProperty("expiresAt", out var expires) && expires.TryGetInt64(out var raw) && raw > 0)
        {
            expiresAt = raw >= 100_000_000_000
                ? DateTimeOffset.FromUnixTimeMilliseconds(raw)
                : DateTimeOffset.FromUnixTimeSeconds(raw);
        }
        return new ClaudeCredential(accessToken, expiresAt);
    }

    private static byte[]? DpapiUnprotect(byte[] data)
    {
        var input = new DataBlob();
        var output = new DataBlob();
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            input.cbData = data.Length;
            input.pbData = handle.AddrOfPinnedObject();
            if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref output)) return null;
            var result = new byte[output.cbData];
            Marshal.Copy(output.pbData, result, 0, output.cbData);
            return result;
        }
        finally
        {
            handle.Free();
            if (output.pbData != IntPtr.Zero) LocalFree(output.pbData);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
