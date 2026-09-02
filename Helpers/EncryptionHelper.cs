using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

public static class EncryptionHelper
{
    private static readonly string Key = "ABCDEF1234567890ABCDEF1234567890"; // Must be 32 chars
    private static readonly string IV = "1234567890ABCDEF"; // Must be 16 chars

    public static string EncryptObject(object obj)
    {
        string json = JsonConvert.SerializeObject(obj);
        return Encrypt(json);
    }

    public static T DecryptObject<T>(string encryptedValue)
    {
        string json = Decrypt(encryptedValue);
        return JsonConvert.DeserializeObject<T>(json);
    }

    private static string Encrypt(string plainText)
    {
        using var aes = Aes.Create();
        aes.Key = Encoding.UTF8.GetBytes(Key);
        aes.IV = Encoding.UTF8.GetBytes(IV);

        using var ms = new MemoryStream();
        using var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write);
        using (var sw = new StreamWriter(cs))
            sw.Write(plainText);

        return Convert.ToBase64String(ms.ToArray());
    }

    private static string Decrypt(string cipherText)
    {
        byte[] buffer = Convert.FromBase64String(cipherText);

        using var aes = Aes.Create();
        aes.Key = Encoding.UTF8.GetBytes(Key);
        aes.IV = Encoding.UTF8.GetBytes(IV);

        using var ms = new MemoryStream(buffer);
        using var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
        using var sr = new StreamReader(cs);

        return sr.ReadToEnd();
    }
}
