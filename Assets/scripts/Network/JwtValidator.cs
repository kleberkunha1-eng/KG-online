using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace TOP.Network
{
    public static class JwtValidator
    {
        [Serializable]
        private class JwtHeader
        {
            public string alg;
        }

        [Serializable]
        private class JwtPayload
        {
            public long sub;
            public long accountId;
            public long userId;
            public long id;
            public string usr;
            public string username;
            public string name;
            public long exp;
        }

        public static bool Validate(string token, string secret, out long accountId, out string username)
        {
            accountId = 0;
            username = null;

            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrEmpty(secret))
                return false;

            string[] parts = token.Split('.');
            if (parts.Length != 3)
                return false;

            try
            {
                string headerJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[0]));
                JwtHeader header = JsonUtility.FromJson<JwtHeader>(headerJson);
                if (header == null || !string.Equals(header.alg, "HS256", StringComparison.OrdinalIgnoreCase))
                    return false;

                byte[] expectedSignature;
                using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret)))
                {
                    expectedSignature = hmac.ComputeHash(Encoding.UTF8.GetBytes(parts[0] + "." + parts[1]));
                }

                byte[] actualSignature = Base64UrlDecode(parts[2]);
                if (!FixedTimeEquals(expectedSignature, actualSignature))
                    return false;

                string payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
                JwtPayload payload = JsonUtility.FromJson<JwtPayload>(payloadJson);
                if (payload == null)
                    return false;

                if (payload.exp > 0 && payload.exp <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                    return false;

                accountId = payload.sub != 0 ? payload.sub : payload.accountId != 0 ? payload.accountId : payload.userId != 0 ? payload.userId : payload.id;
                username = !string.IsNullOrEmpty(payload.usr) ? payload.usr : !string.IsNullOrEmpty(payload.username) ? payload.username : payload.name;

                return accountId > 0;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[JwtValidator] Token invalido: {ex.Message}");
                return false;
            }
        }

        private static byte[] Base64UrlDecode(string value)
        {
            string base64 = value.Replace('-', '+').Replace('_', '/');
            switch (base64.Length % 4)
            {
                case 2:
                    base64 += "==";
                    break;
                case 3:
                    base64 += "=";
                    break;
            }

            return Convert.FromBase64String(base64);
        }

        private static bool FixedTimeEquals(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
                return false;

            int diff = 0;
            for (int i = 0; i < left.Length; i++)
                diff |= left[i] ^ right[i];

            return diff == 0;
        }
    }
}
